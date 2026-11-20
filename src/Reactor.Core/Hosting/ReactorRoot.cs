using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Mounts an element tree through a renderer's handlers and keeps it up to date.
/// State changes are batched: every component marked dirty before the posted flush
/// runs re-renders once, parents before children, and effects run after the commit.
/// </summary>
/// <remarks>
/// Errors thrown while rendering, mounting or running effects go to the nearest
/// <see cref="ErrorBoundaryElement"/>, which unmounts the failed subtree and shows its fallback.
/// An error with no boundary above it unmounts the whole tree and is then reported through
/// <see cref="UnhandledException"/>, or rethrown when nothing handles that event.
/// </remarks>
public sealed class ReactorRoot
{
    private const int MaxFlushPasses = 100;

    private static readonly List<WeakReference<ReactorRoot>> LiveRoots = [];
    private static readonly Lock LiveRootsGate = new();

    private readonly ElementHandlerRegistry _registry;
    private readonly IRenderScheduler _scheduler;
    private readonly Action<object?> _setRootNative;
    private readonly EchoGuard _echo = new();
    private readonly List<Node> _dirty = [];
    private readonly List<Node> _effectQueue = [];
    private readonly List<Node> _restoreQueue = [];
    private Node? _root;
    private object? _rootNative;
    private bool _flushScheduled;

    /// <summary>For a child root, the node that created it: context lookups and errors continue there.</summary>
    internal Node? ContextParent { get; private init; }

    /// <param name="registry">The renderer's element handlers.</param>
    /// <param name="scheduler">The renderer's UI thread.</param>
    /// <param name="setRootNative">Receives the root native control (or <c>null</c>) whenever it changes.</param>
    public ReactorRoot(ElementHandlerRegistry registry, IRenderScheduler scheduler, Action<object?> setRootNative)
    {
        _registry = registry;
        _scheduler = scheduler;
        _setRootNative = setRootNative;
        lock (LiveRootsGate)
        {
            LiveRoots.RemoveAll(static r => !r.TryGetTarget(out _));
            LiveRoots.Add(new WeakReference<ReactorRoot>(this));
        }
    }

    /// <summary>
    /// Raised on the render thread for an error no <see cref="ErrorBoundaryElement"/> caught, after
    /// the tree has been unmounted. Without a handler the error is rethrown.
    /// </summary>
    public event Action<Exception>? UnhandledException;

    /// <summary>Mounts <paramref name="element"/>, or reconciles it against what is already mounted.</summary>
    public void Render(Element? element)
    {
        EnsureAccess();
        var next = element ?? EmptyElement.Instance;
        try
        {
            if (_root is not null && SameIdentity(_root.Element, next)
                && (_root.Kind != NodeKind.Host || _root.Handler!.CanUpdate(_root.Element, next)))
            {
                Update(_root, next);
            }
            else
            {
                var previous = _root;
                _root = null;
                if (previous is not null) UnmountAndReport(previous);
                _root = Mount(next, null);
            }
        }
        catch (Exception ex)
        {
            FailRoot(ex);
            return;
        }
        Commit(new HashSet<Node>(), syncRoot: true);
    }

    /// <summary>Unmounts the whole tree, running every effect cleanup.</summary>
    public void Unmount()
    {
        EnsureAccess();
        if (_root is null) return;
        var root = _root;
        _root = null;
        var errors = new List<Exception>();
        Unmount(root, errors);
        SyncRoot();
        Report(errors);
    }

    internal ReactorRoot CreateChildRoot(Node owner, Action<object?> setRootNative)
    {
        var child = new ReactorRoot(_registry, _scheduler, setRootNative) { ContextParent = owner };
        // Posted, so the parent tree is not changed while the child root is still unwinding.
        child.UnhandledException += error => _scheduler.Post(() => HandleChildError(owner, error));
        return child;
    }

    private void HandleChildError(Node owner, Exception error)
    {
        if (owner.Unmounted) return;
        var hosts = new HashSet<Node>();
        var syncRoot = false;
        try
        {
            var boundary = RouteToBoundary(owner, error);
            if (NearestHost(boundary) is { } host) hosts.Add(host);
            else syncRoot = true;
        }
        catch (Exception unhandled)
        {
            FailRoot(unhandled);
            return;
        }
        Commit(hosts, syncRoot);
    }

    /// <summary>Re-renders every component of every live root. Used by hot reload.</summary>
    public static void RerenderAll()
    {
        ReactorRoot[] roots;
        lock (LiveRootsGate)
            roots = [.. LiveRoots.Select(static r => r.TryGetTarget(out var root) ? root : null).OfType<ReactorRoot>()];
        foreach (var root in roots) root._scheduler.Post(root.MarkAllDirty);
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
        ScheduleFlush();
    }

    /// <summary>
    /// Re-applies <paramref name="node"/>'s current element to its control after the next flush, so
    /// a controlled control whose change callback did not update state returns to its declared value.
    /// </summary>
    internal void RequestRestore(Node node)
    {
        _restoreQueue.Add(node);
        ScheduleFlush();
    }

    internal void Flush()
    {
        _flushScheduled = false;
        Commit(new HashSet<Node>(), syncRoot: false);
    }

    /// <summary>
    /// Finishes a render: re-renders every dirty component (parents first, in passes until nothing is
    /// dirty), then synchronizes the native children of every host that changed, restores controlled
    /// values and runs effects. Hosts are synchronized only once the whole batch has rendered, because
    /// an intermediate state (one component shown before its sibling is hidden) may not be valid for its host.
    /// </summary>
    private void Commit(HashSet<Node> hostsToSync, bool syncRoot)
    {
        try
        {
            for (var pass = 0; _dirty.Count > 0; pass++)
            {
                if (pass == MaxFlushPasses)
                {
                    foreach (var node in _dirty) node.Dirty = false;
                    _dirty.Clear();
                    throw new InvalidOperationException(
                        $"Rendering did not settle after {MaxFlushPasses} passes. A component is probably setting state on every render.");
                }

                var batch = _dirty.ToArray();
                _dirty.Clear();
                // Parents first: re-rendering a parent re-renders its dirty descendants and clears their flag.
                Array.Sort(batch, static (a, b) => a.Depth.CompareTo(b.Depth));
                foreach (var node in batch)
                {
                    if (node.Unmounted || !node.Dirty) continue;
                    var changed = node;
                    try
                    {
                        RenderNode(node);
                    }
                    catch (Exception ex)
                    {
                        changed = RouteToBoundary(node, ex);
                    }
                    if (NearestHost(changed) is { } host) hostsToSync.Add(host);
                    else syncRoot = true;
                }
            }

            SyncHosts(hostsToSync, ref syncRoot);
            if (syncRoot) SyncRoot();
            RestoreControlledValues();
        }
        catch (Exception ex)
        {
            FailRoot(ex);
            return;
        }
        RunEffects();
    }

    /// <summary>
    /// Synchronizes each host's native children. A host whose children strategy rejects the result
    /// (for example two controls in a single-child host) is treated like a render error: the nearest
    /// boundary above it shows its fallback, and that boundary's host is synchronized in turn.
    /// </summary>
    private void SyncHosts(HashSet<Node> hosts, ref bool syncRoot)
    {
        var pending = new Queue<Node>(hosts);
        while (pending.TryDequeue(out var host))
        {
            if (host.Unmounted) continue;
            try
            {
                SyncHost(host);
            }
            catch (Exception ex)
            {
                var boundary = RouteToBoundary(host, ex);
                if (NearestHost(boundary) is { } parentHost) pending.Enqueue(parentHost);
                else syncRoot = true;
            }
        }
    }

    private void ScheduleFlush()
    {
        if (_flushScheduled) return;
        _flushScheduled = true;
        _scheduler.Post(Flush);
    }

    private void MarkAllDirty()
    {
        if (_root is null) return;
        var stack = new Stack<Node>();
        stack.Push(_root);
        while (stack.TryPop(out var node))
        {
            if (node.IsRenderable) MarkDirty(node);
            foreach (var child in node.Children) stack.Push(child);
        }
    }

    private Node Mount(Element? element, Node? parent)
    {
        element ??= EmptyElement.Instance;
        var node = element switch
        {
            EmptyElement => new Node(element, parent, NodeKind.Empty, this),
            GroupElement => new Node(element, parent, NodeKind.Group, this),
            ContextProviderElement => new Node(element, parent, NodeKind.Provider, this),
            ErrorBoundaryElement => new Node(element, parent, NodeKind.Boundary, this),
            ComponentElement => new Node(element, parent, NodeKind.Component, this),
            FuncElement => new Node(element, parent, NodeKind.Func, this) { Context = new RenderContext() },
            MemoElement => new Node(element, parent, NodeKind.Memo, this) { Context = new RenderContext() },
            _ => new Node(element, parent, NodeKind.Host, this) { Handler = _registry.Get(element.GetType()) },
        };

        try
        {
            switch (node.Kind)
            {
                case NodeKind.Group:
                    ReconcileChildren(node, ((GroupElement)element).Children);
                    break;

                case NodeKind.Provider:
                    ReconcileChildren(node, [((ContextProviderElement)element).Child]);
                    break;

                case NodeKind.Boundary:
                    ReconcileBoundary(node);
                    break;

                case NodeKind.Component:
                {
                    var componentElement = (ComponentElement)element;
                    var component = componentElement.CreateInstance();
                    if (component is IPropsReceiver receiver) receiver.SetProps(componentElement.Props);
                    node.Component = component;
                    node.Context = component.Context;
                    Wire(node);
                    RenderNode(node);
                    break;
                }

                case NodeKind.Func:
                case NodeKind.Memo:
                    Wire(node);
                    RenderNode(node);
                    break;

                case NodeKind.Host:
                    node.Native = node.Handler!.Mount(node, _echo);
                    ReconcileChildren(node, node.Handler.GetChildren(element));
                    SyncHost(node);
                    break;
            }
        }
        catch (Exception primary)
        {
            // Nothing else can reach this half-mounted node; release what it acquired. The
            // original error stays the one that propagates.
            var errors = new List<Exception>();
            Unmount(node, errors);
            ReportSecondary(errors, primary);
            throw;
        }
        return node;
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

            case NodeKind.Provider:
                UpdateProvider(node, (ContextProviderElement)previous, (ContextProviderElement)next);
                break;

            case NodeKind.Boundary:
                // A parent re-render retries the child.
                node.Error = null;
                ReconcileBoundary(node);
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
                if (rerender) RenderNode(node);
                break;
            }

            case NodeKind.Func:
                RenderNode(node);
                break;

            case NodeKind.Memo:
                if (node.Dirty || !DepsEqual(((MemoElement)previous).Dependencies, ((MemoElement)next).Dependencies))
                    RenderNode(node);
                break;

            case NodeKind.Host:
                node.Handler!.Update(node, _echo, previous, next, node.Native!);
                ReconcileChildren(node, node.Handler.GetChildren(next));
                SyncHost(node);
                break;
        }
    }

    private void UpdateProvider(Node node, ContextProviderElement previous, ContextProviderElement next)
    {
        // Readers are marked dirty first, so the descent below re-renders them in place even when
        // their props did not change.
        var changed = !Equals(previous.Value, next.Value);
        var readers = changed && node.Subscribers is { Count: > 0 } ? node.Subscribers.ToArray() : [];
        foreach (var reader in readers)
            if (reader.Root == this && !reader.Unmounted) reader.Dirty = true;

        ReconcileChildren(node, [next.Child]);

        // Readers below a component that skipped re-rendering were not reached by the descent. They
        // render in the commit that is already running, with the rest of the batch, so host
        // synchronization and error boundaries apply to them as to any other state change.
        foreach (var reader in readers)
        {
            if (reader.Unmounted) continue;
            if (reader.Root != this)
            {
                // A reader in a child root re-renders in that root's own flush.
                reader.Root.MarkDirty(reader);
                continue;
            }
            if (reader.Dirty && !_dirty.Contains(reader)) _dirty.Add(reader);
        }
    }

    private void ReconcileBoundary(Node boundary)
    {
        var element = (ErrorBoundaryElement)boundary.Element;
        if (boundary.Error is { } error)
        {
            ReconcileChildren(boundary, [element.Fallback(error)]);
            return;
        }

        try
        {
            ReconcileChildren(boundary, [element.Child]);
        }
        catch (Exception ex)
        {
            ShowFallback(boundary, ex);
        }
    }

    /// <summary>Unmounts the boundary's subtree and renders its fallback for <paramref name="error"/>.</summary>
    private void ShowFallback(Node boundary, Exception error)
    {
        boundary.Error = error;
        var failed = boundary.Children;
        boundary.Children = [];
        var errors = new List<Exception>();
        foreach (var child in failed) Unmount(child, errors);
        ReportSecondary(errors, error);
        // A throwing fallback propagates to the next boundary up.
        ReconcileChildren(boundary, [((ErrorBoundaryElement)boundary.Element).Fallback(error)]);
    }

    /// <summary>
    /// Sends an error raised under <paramref name="origin"/> to the nearest boundary above it.
    /// Returns the boundary, whose host now needs re-syncing. Rethrows when no boundary catches it.
    /// </summary>
    private Node RouteToBoundary(Node origin, Exception error)
    {
        for (var ancestor = origin.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            // A boundary already showing its fallback cannot catch an error from that fallback.
            if (ancestor.Kind != NodeKind.Boundary || ancestor.Unmounted || ancestor.Error is not null) continue;
            try
            {
                ShowFallback(ancestor, error);
                return ancestor;
            }
            catch (Exception fallbackError)
            {
                // The fallback failed too: keep climbing with that error.
                error = fallbackError;
                origin = ancestor;
            }
        }
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        return null!;
    }

    private void RenderNode(Node node)
    {
        node.Dirty = false;
        var context = node.Context!;
        // Subscriptions are rebuilt by this render's UseContext calls; providers no longer read are dropped.
        var previousSubscriptions = node.Subscriptions;
        node.Subscriptions = null;
        Element? rendered;
        try
        {
            context.BeginRender();
            rendered = node.Kind switch
            {
                NodeKind.Component => node.Component!.Render(),
                NodeKind.Memo => ((MemoElement)node.Element).RenderFunc(context),
                _ => ((FuncElement)node.Element).RenderFunc(context),
            };
            context.EndRender();
        }
        catch
        {
            // Keep every subscription reachable so unmounting the failed node removes them all.
            if (previousSubscriptions is not null) (node.Subscriptions ??= []).UnionWith(previousSubscriptions);
            throw;
        }

        if (previousSubscriptions is not null)
        {
            foreach (var provider in previousSubscriptions)
                if (node.Subscriptions is null || !node.Subscriptions.Contains(provider))
                    provider.Subscribers?.Remove(node);
        }

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
        try
        {
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

                if (candidate is not null && SameIdentity(candidate.Element, element)
                    && (candidate.Kind != NodeKind.Host || candidate.Handler!.CanUpdate(candidate.Element, element)))
                {
                    // Held in the result before updating, so a failed update still leaves it reachable.
                    result.Add(candidate);
                    Update(candidate, element);
                }
                else
                {
                    if (candidate is not null) UnmountAndReport(candidate);
                    result.Add(Mount(element, parent));
                }
            }
        }
        catch
        {
            // Keep every node this pass still owns reachable, so whoever handles the error can unmount them.
            if (keyedOld is not null) result.AddRange(keyedOld.Values);
            if (unkeyedOld is not null) result.AddRange(unkeyedOld.OfType<Node>());
            parent.Children = result;
            throw;
        }

        var errors = new List<Exception>();
        if (keyedOld is not null)
            foreach (var stale in keyedOld.Values) Unmount(stale, errors);
        if (unkeyedOld is not null)
            foreach (var stale in unkeyedOld) if (stale is not null) Unmount(stale, errors);

        parent.Children = result;
        Report(errors);
    }

    /// <summary>
    /// Unmounts <paramref name="node"/> and its subtree, children first. A throwing cleanup or handler
    /// does not stop the rest of the teardown; its exception is added to <paramref name="errors"/>.
    /// </summary>
    private static void Unmount(Node node, List<Exception> errors)
    {
        if (node.Unmounted) return;
        node.Unmounted = true;
        node.Dirty = false;
        foreach (var child in node.Children) Unmount(child, errors);

        node.Context?.RunCleanups(errors);
        if (node.Subscriptions is { } subscriptions)
            foreach (var provider in subscriptions) provider.Subscribers?.Remove(node);
        if (node.Kind == NodeKind.Host && node.Native is { } native)
        {
            try
            {
                node.Handler!.Unmount(native);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }
    }

    private void UnmountAndReport(Node node)
    {
        var errors = new List<Exception>();
        Unmount(node, errors);
        Report(errors);
    }

    /// <summary>Reports teardown errors. Teardown always completes before this throws.</summary>
    private void Report(List<Exception> errors)
    {
        if (errors.Count == 0) return;
        var error = errors.Count == 1 ? errors[0] : new AggregateException("Several effect cleanups or handlers failed while unmounting.", errors);
        if (UnhandledException is { } handler) handler(error);
        else System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    /// <summary>
    /// Reports teardown errors raised while already handling <paramref name="primary"/>. They go to
    /// <see cref="UnhandledException"/> when it has a handler, and are traced otherwise, because
    /// rethrowing them would replace the error that is actually being handled.
    /// </summary>
    private void ReportSecondary(List<Exception> errors, Exception primary)
    {
        foreach (var error in errors)
        {
            if (UnhandledException is { } handler) handler(error);
            else System.Diagnostics.Trace.TraceError($"Reactor: cleanup failed while handling '{primary.Message}': {error}");
        }
    }

    /// <summary>An error escaped every boundary: unmount the whole tree, then report it.</summary>
    private void FailRoot(Exception error)
    {
        _dirty.Clear();
        _effectQueue.Clear();
        _restoreQueue.Clear();
        var teardownErrors = new List<Exception>();
        if (_root is { } root)
        {
            _root = null;
            Unmount(root, teardownErrors);
        }
        try
        {
            SyncRoot();
        }
        catch (Exception syncError)
        {
            teardownErrors.Add(syncError);
        }

        var reported = teardownErrors.Count == 0 ? error : new AggregateException(error.Message, [error, .. teardownErrors]);
        if (UnhandledException is { } handler) handler(reported);
        else System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(reported).Throw();
    }

    private void Wire(Node node)
    {
        var context = node.Context!;
        context.Scheduler = _scheduler;
        context.Owner = node;
        context.RequestRerender = () => MarkDirty(node);
    }

    private void SyncHost(Node host)
    {
        if (host.Handler!.IsSlotted)
        {
            var slots = new List<IReadOnlyList<object>>(host.Children.Count);
            foreach (var child in host.Children)
            {
                var slot = new List<object>();
                CollectNatives(child, slot);
                slots.Add(slot);
            }
            host.Handler.SyncSlots(host.Element, host.Native!, slots);
            return;
        }

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

    private void RestoreControlledValues()
    {
        if (_restoreQueue.Count == 0) return;
        var nodes = _restoreQueue.ToArray();
        _restoreQueue.Clear();
        var hosts = new HashSet<Node>();
        var syncRoot = false;
        foreach (var node in nodes)
        {
            if (node.Unmounted) continue;
            try
            {
                // Updating with the same element on both sides re-asserts only values the control drifted from.
                node.Handler!.Update(node, _echo, node.Element, node.Element, node.Native!);
            }
            catch (Exception ex)
            {
                var boundary = RouteToBoundary(node, ex);
                if (NearestHost(boundary) is { } host) hosts.Add(host);
                else syncRoot = true;
            }
        }
        SyncHosts(hosts, ref syncRoot);
        if (syncRoot) SyncRoot();
    }

    private void RunEffects()
    {
        for (var pass = 0; _effectQueue.Count > 0; pass++)
        {
            if (pass == MaxFlushPasses)
            {
                FailRoot(new InvalidOperationException(
                    $"Effects did not settle after {MaxFlushPasses} passes. An effect or error fallback is probably re-triggering itself."));
                return;
            }

            // Deepest first, so a child's effects run before any ancestor's, even when the two
            // rendered separately because a memoized component between them was skipped.
            var batch = _effectQueue.OrderByDescending(static node => node.Depth).ToArray();
            _effectQueue.Clear();
            foreach (var node in batch)
            {
                if (node.Unmounted) continue;
                try
                {
                    node.Context!.RunPendingEffects();
                }
                catch (Exception ex)
                {
                    try
                    {
                        var boundary = RouteToBoundary(node, ex);
                        var hosts = new HashSet<Node>();
                        var syncRoot = false;
                        if (NearestHost(boundary) is { } host) hosts.Add(host);
                        else syncRoot = true;
                        SyncHosts(hosts, ref syncRoot);
                        if (syncRoot) SyncRoot();
                    }
                    catch (Exception unhandled)
                    {
                        FailRoot(unhandled);
                        return;
                    }
                }
            }
        }
    }

    private static bool SameIdentity(Element a, Element b) =>
        a.GetType() == b.GetType()
        && a.Key == b.Key
        && (a is not ComponentElement ca || ca.ComponentType == ((ComponentElement)b).ComponentType)
        && (a is not ContextProviderElement pa || ReferenceEquals(pa.Context, ((ContextProviderElement)b).Context));

    private static bool DepsEqual(object?[] previous, object?[] next)
    {
        if (previous.Length != next.Length) return false;
        for (var i = 0; i < previous.Length; i++)
            if (!Equals(previous[i], next[i])) return false;
        return true;
    }

    private void EnsureAccess()
    {
        if (!_scheduler.CheckAccess())
            throw new InvalidOperationException("A ReactorRoot can only be rendered or unmounted on its render thread.");
    }
}
