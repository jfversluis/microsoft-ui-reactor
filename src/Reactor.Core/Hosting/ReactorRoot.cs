using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Mounts an element tree through a renderer's handlers and keeps it up to date.
/// State changes are batched: every component marked dirty before the posted flush
/// runs re-renders once, parents before children, and effects run after the commit.
/// </summary>
public sealed class ReactorRoot
{
    private const int MaxFlushPasses = 100;

    private readonly ElementHandlerRegistry _registry;
    private readonly IRenderScheduler _scheduler;
    private readonly Action<object?> _setRootNative;
    private readonly EchoGuard _echo = new();
    private readonly List<Node> _dirty = [];
    private readonly List<Node> _effectQueue = [];
    private Node? _root;
    private object? _rootNative;
    private bool _flushScheduled;

    /// <param name="registry">The renderer's element handlers.</param>
    /// <param name="scheduler">The renderer's UI thread.</param>
    /// <param name="setRootNative">Receives the root native control (or <c>null</c>) whenever it changes.</param>
    public ReactorRoot(ElementHandlerRegistry registry, IRenderScheduler scheduler, Action<object?> setRootNative)
    {
        _registry = registry;
        _scheduler = scheduler;
        _setRootNative = setRootNative;
    }

    /// <summary>Mounts <paramref name="element"/>, or reconciles it against what is already mounted.</summary>
    public void Render(Element? element)
    {
        EnsureAccess();
        var next = element ?? EmptyElement.Instance;
        if (_root is not null && SameType(_root.Element, next) && _root.Element.Key == next.Key)
        {
            Update(_root, next);
        }
        else
        {
            if (_root is not null) Unmount(_root);
            _root = Mount(next, null);
        }
        SyncRoot();
        RunEffects();
    }

    /// <summary>Unmounts the whole tree, running every effect cleanup.</summary>
    public void Unmount()
    {
        EnsureAccess();
        if (_root is null) return;
        Unmount(_root);
        _root = null;
        SyncRoot();
    }

    internal void MarkDirty(Node node)
    {
        if (!_scheduler.CheckAccess())
        {
            _scheduler.Post(() => MarkDirty(node));
            return;
        }
        if (node.Unmounted || node.Dirty) return;

        node.Dirty = true;
        _dirty.Add(node);
        if (!_flushScheduled)
        {
            _flushScheduled = true;
            _scheduler.Post(Flush);
        }
    }

    internal void Flush()
    {
        _flushScheduled = false;
        // Hosts are re-synced once the whole batch has rendered: an intermediate state (one
        // component shown before its sibling is hidden) may not be valid for its host.
        var hostsToSync = new HashSet<Node>();
        var syncRoot = false;
        for (var pass = 0; _dirty.Count > 0; pass++)
        {
            if (pass == MaxFlushPasses)
                throw new InvalidOperationException(
                    $"Rendering did not settle after {MaxFlushPasses} passes. A component is probably setting state on every render.");

            var batch = _dirty.ToArray();
            _dirty.Clear();
            // Parents first: re-rendering a parent re-renders its dirty descendants and clears their flag.
            Array.Sort(batch, static (a, b) => a.Depth.CompareTo(b.Depth));
            foreach (var node in batch)
            {
                if (node.Unmounted || !node.Dirty) continue;
                RenderComponent(node);
                if (NearestHost(node) is { } host) hostsToSync.Add(host);
                else syncRoot = true;
            }
        }

        foreach (var host in hostsToSync)
            if (!host.Unmounted) SyncHost(host);
        if (syncRoot) SyncRoot();
        RunEffects();
    }

    private Node Mount(Element? element, Node? parent)
    {
        element ??= EmptyElement.Instance;
        switch (element)
        {
            case EmptyElement:
                return new Node(element, parent, NodeKind.Empty);

            case GroupElement group:
            {
                var node = new Node(element, parent, NodeKind.Group);
                ReconcileChildren(node, group.Children);
                return node;
            }

            case ComponentElement componentElement:
            {
                var component = componentElement.CreateInstance();
                if (component is IPropsReceiver receiver) receiver.SetProps(componentElement.Props);
                var node = new Node(element, parent, NodeKind.Component) { Component = component, Context = component.Context };
                Wire(node);
                RenderComponent(node);
                return node;
            }

            case FuncElement:
            {
                var node = new Node(element, parent, NodeKind.Func) { Context = new RenderContext() };
                Wire(node);
                RenderComponent(node);
                return node;
            }

            default:
            {
                var handler = _registry.Get(element.GetType());
                var node = new Node(element, parent, NodeKind.Host) { Handler = handler };
                node.Native = handler.Mount(node, _echo);
                ReconcileChildren(node, handler.GetChildren(element));
                SyncHost(node);
                return node;
            }
        }
    }

    private void Update(Node node, Element next)
    {
        var previous = node.Element;
        if (ReferenceEquals(previous, next) && !node.Dirty) return;
        node.Element = next;

        switch (node.Kind)
        {
            case NodeKind.Group:
                ReconcileChildren(node, ((GroupElement)next).Children);
                break;

            case NodeKind.Component:
            {
                var rerender = node.Dirty;
                if (node.Component is IPropsReceiver receiver)
                {
                    var newProps = ((ComponentElement)next).Props;
                    rerender |= receiver.ShouldUpdate(((ComponentElement)previous).Props, newProps);
                    // Installed even when skipping, so a later state-driven render sees current props.
                    receiver.SetProps(newProps);
                }
                else
                {
                    rerender |= node.Component!.ShouldUpdate();
                }
                if (rerender) RenderComponent(node);
                break;
            }

            case NodeKind.Func:
                RenderComponent(node);
                break;

            case NodeKind.Host:
                node.Handler!.Update(node, _echo, previous, next, node.Native!);
                ReconcileChildren(node, node.Handler.GetChildren(next));
                SyncHost(node);
                break;
        }
    }

    private void RenderComponent(Node node)
    {
        node.Dirty = false;
        var context = node.Context!;
        context.BeginRender();
        var rendered = node.Kind == NodeKind.Component
            ? node.Component!.Render()
            : ((FuncElement)node.Element).RenderFunc(context);
        context.EndRender();

        ReconcileChildren(node, [rendered]);
        if (context.HasPendingEffects) _effectQueue.Add(node);
    }

    /// <summary>
    /// Matches new child elements to existing nodes: by key when one is set, otherwise by
    /// position among the unkeyed children. A match of the same type is updated in place,
    /// keeping its state and native control; everything else is mounted or unmounted.
    /// </summary>
    private void ReconcileChildren(Node parent, IReadOnlyList<Element?> elements)
    {
        Dictionary<string, Node>? keyedOld = null;
        List<Node?>? unkeyedOld = null;
        foreach (var child in parent.Children)
        {
            if (child.Element.Key is { } key) (keyedOld ??= [])[key] = child;
            else (unkeyedOld ??= []).Add(child);
        }

        var result = new List<Node>(elements.Count);
        HashSet<string>? seenKeys = null;
        var unkeyedIndex = 0;
        foreach (var raw in elements)
        {
            var element = raw ?? EmptyElement.Instance;
            Node? candidate = null;
            if (element.Key is { } key)
            {
                if (!(seenKeys ??= []).Add(key))
                    throw new InvalidOperationException(
                        $"Two children of {parent.Element.GetType().Name} have the key '{key}'. Keys must be unique among siblings.");
                keyedOld?.Remove(key, out candidate);
            }
            else
            {
                if (unkeyedOld is not null && unkeyedIndex < unkeyedOld.Count)
                {
                    candidate = unkeyedOld[unkeyedIndex];
                    unkeyedOld[unkeyedIndex] = null;
                }
                unkeyedIndex++;
            }

            if (candidate is not null && SameType(candidate.Element, element))
            {
                Update(candidate, element);
                result.Add(candidate);
            }
            else
            {
                if (candidate is not null) Unmount(candidate);
                result.Add(Mount(element, parent));
            }
        }

        if (keyedOld is not null)
            foreach (var stale in keyedOld.Values) Unmount(stale);
        if (unkeyedOld is not null)
            foreach (var stale in unkeyedOld) if (stale is not null) Unmount(stale);

        parent.Children = result;
    }

    private void Unmount(Node node)
    {
        if (node.Unmounted) return;
        node.Unmounted = true;
        foreach (var child in node.Children) Unmount(child);
        node.Context?.RunCleanups();
        if (node.Kind == NodeKind.Host) node.Handler!.Unmount(node.Native!);
    }

    private void Wire(Node node)
    {
        node.Context!.Scheduler = _scheduler;
        node.Context.RequestRerender = () => MarkDirty(node);
    }

    private void SyncHost(Node host)
    {
        var natives = new List<object>();
        foreach (var child in host.Children) CollectNatives(child, natives);
        host.Handler!.SyncChildren(host.Native!, natives);
    }

    private static Node? NearestHost(Node node)
    {
        var ancestor = node.Parent;
        while (ancestor is not null && ancestor.Kind != NodeKind.Host) ancestor = ancestor.Parent;
        return ancestor;
    }

    private void SyncRoot()
    {
        var natives = new List<object>();
        if (_root is not null) CollectNatives(_root, natives);
        if (natives.Count > 1)
            throw new InvalidOperationException(
                $"The root rendered {natives.Count} controls, but a root hosts one. Wrap them in a stack.");

        var native = natives.Count == 0 ? null : natives[0];
        if (ReferenceEquals(native, _rootNative)) return;
        _rootNative = native;
        _setRootNative(native);
    }

    private static void CollectNatives(Node node, List<object> natives)
    {
        if (node.Kind == NodeKind.Host)
        {
            natives.Add(node.Native!);
            return;
        }
        foreach (var child in node.Children) CollectNatives(child, natives);
    }

    private void RunEffects()
    {
        while (_effectQueue.Count > 0)
        {
            // Deepest first, so a child's effects run before any ancestor's, even when the two
            // rendered separately because a memoized component between them was skipped.
            var batch = _effectQueue.OrderByDescending(static node => node.Depth).ToArray();
            _effectQueue.Clear();
            foreach (var node in batch) node.Context!.RunPendingEffects();
        }
    }

    private static bool SameType(Element a, Element b) =>
        a.GetType() == b.GetType()
        && (a is not ComponentElement ca || ca.ComponentType == ((ComponentElement)b).ComponentType);

    private void EnsureAccess()
    {
        if (!_scheduler.CheckAccess())
            throw new InvalidOperationException("A ReactorRoot can only be rendered or unmounted on its render thread.");
    }
}
