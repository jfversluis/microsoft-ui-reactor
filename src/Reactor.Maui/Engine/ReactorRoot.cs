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
    private readonly RootRenderOwner _owner;
    // The context values in effect for the node being rendered or gated (spec 067 §13): filled
    // from its ancestor providers just for that call, so the core's UseContext and skip gate read them.
    private readonly ContextScope _contextScope = new();
    private readonly List<Node> _dirty = [];
    private readonly List<Node> _effectQueue = [];
    private readonly List<Node> _restoreQueue = [];

    // The top-most natives of the subtrees unmounted in this pass, released once their hosts have
    // been synchronized (see ReleaseRemoved).
    private List<object>? _removed;
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
        _owner = new RootRenderOwner(scheduler);
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
            if (_root is not null && CanUpdateInPlace(_root.Element, next))
            {
                Update(_root, next);
            }
            else
            {
                var previous = _root;
                _root = null;
                if (previous is not null)
                {
                    NoteRemoved(previous);
                    UnmountAndReport(previous);
                }
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
        NoteRemoved(root);
        Unmount(root, errors);
        SyncRoot();
        ReleaseRemoved();
        Report(errors);
    }

    internal ReactorRoot CreateChildRoot(Node owner, Action<object?> setRootNative)
    {
        var child = new ReactorRoot(_registry, _scheduler, setRootNative) { ContextParent = owner };
        // Posted, so the parent tree is not changed while the child root is still unwinding.
        child.UnhandledException += error => _scheduler.Post(() => HandleChildError(owner, error));
        return child;
    }

    /// <summary>Queues <paramref name="work"/> on this root's render thread.</summary>
    internal void Post(Action work) => _scheduler.Post(work);

    // Posted, like child-root errors, so it never runs inside the handler code that reported it.
    internal void ReportHandlerError(Node node, Exception error) => _scheduler.Post(() => HandleChildError(node, error));

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
        foreach (var root in roots) root._scheduler.Post(root.HotReloadPass);
    }

    // Inside the pass, a component whose edited Render() changed its hooks starts over with fresh
    // hook state instead of failing (the core's render kernel retries it).
    private void HotReloadPass()
    {
        MarkAllDirty();
        HotReloadState.WithinUpdatePass = true;
        try
        {
            Flush();
        }
        finally
        {
            HotReloadState.WithinUpdatePass = false;
        }
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
            ReleaseRemoved();
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
        var kind = KindOf(element);
        var node = new Node(element, parent, kind, this);
        switch (kind)
        {
            case NodeKind.Provider:
                var provider = (ContextProviderElement)element;
                node.Provided = new() { [provider.Context] = provider.Value };
                break;
            case NodeKind.Func or NodeKind.Memo:
                node.Context = new RenderContext();
                break;
            case NodeKind.Host:
                node.Handler = _registry.Get(element.GetType());
                break;
        }

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
                    node.Component = component;
                    InstallProps(node, componentElement.Props);
                    Wire(node);
                    RenderNode(node, ComponentRenderMode.Mount);
                    break;
                }

                case NodeKind.Func:
                case NodeKind.Memo:
                    Wire(node);
                    RenderNode(node, ComponentRenderMode.Mount);
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
            NoteRemoved(node);
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
                // A component's own state change bypasses the gate (as on WinUI); otherwise the
                // core's gate decides, through ShouldUpdate and the context values it read.
                var rerender = node.Dirty || !Gate(node, next).Skip;
                // Installed even when skipping, so a later state-driven render, and an event
                // handler that reads Props, see the current props (issue #151).
                InstallProps(node, ((ComponentElement)next).Props);
                if (rerender) RenderNode(node, ComponentRenderMode.Update);
                break;
            }

            case NodeKind.Func:
                RenderNode(node, ComponentRenderMode.Update);
                break;

            case NodeKind.Memo:
                if (node.Dirty || !Gate(node, next).Skip)
                    RenderNode(node, ComponentRenderMode.Update);
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
        // Always the current value: an equal but distinct one is what a reader that re-renders for
        // another reason must see. Equality only decides which readers re-render now.
        node.Provided![next.Context] = next.Value;
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
        boundary.ChildElements = [];
        var errors = new List<Exception>();
        foreach (var child in failed)
        {
            NoteRemoved(child);
            Unmount(child, errors);
        }
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

    /// <summary>
    /// Renders a component, function or memo node through the core's render kernel (spec 067 §13)
    /// and reconciles what it rendered. Its effects run after the commit, deepest first.
    /// </summary>
    private void RenderNode(Node node, ComponentRenderMode mode = ComponentRenderMode.Update)
    {
        node.Dirty = false;
        var context = node.HookContext!;
        // Subscriptions are rebuilt from this render's UseContext calls; providers no longer read are dropped.
        var previousSubscriptions = node.Subscriptions;
        node.Subscriptions = null;
        ReactorElement? rendered;
        var pushed = PushProviders(node);
        try
        {
            var host = new KernelHost();
            ComponentRenderKernel.TryRender(ref host, node, node.Element, mode, node.Rerender!, _contextScope, _owner, out rendered);
        }
        catch
        {
            // Keep every subscription reachable so unmounting the failed node removes them all.
            if (previousSubscriptions is not null) (node.Subscriptions ??= []).UnionWith(previousSubscriptions);
            throw;
        }
        finally
        {
            if (pushed > 0) _contextScope.Pop(pushed);
        }

        Subscribe(node, context);
        if (previousSubscriptions is not null)
        {
            foreach (var provider in previousSubscriptions)
                if (node.Subscriptions is null || !node.Subscriptions.Contains(provider))
                    provider.Subscribers?.Remove(node);
        }

        if (node.Element is MemoElement memo) node.MemoDependencies = memo.Dependencies;
        node.RenderedElement = rendered;
        ReconcileChildren(node, [(Element?)rendered]);
        if (context.HasPendingEffects) _effectQueue.Add(node);
    }

    /// <summary>The core's skip gate for a component or memo node, with its context values in scope.</summary>
    private ComponentGateResult Gate(Node node, Element next)
    {
        var pushed = PushProviders(node);
        try
        {
            return ComponentGate.Evaluate(node, next, _contextScope);
        }
        finally
        {
            if (pushed > 0) _contextScope.Pop(pushed);
        }
    }

    private static void InstallProps(Node node, object? props)
    {
        if (node.Component is not IPropsReceiver receiver) return;
        // A nullable TProps can be null; the receiver casts it back.
        receiver.SetProps(props!);
        node.PreviousProps = props;
    }

    /// <summary>
    /// Pushes the values of the providers above <paramref name="node"/> onto the context scope,
    /// outermost first so the nearest wins. Returns how many entries to pop.
    /// </summary>
    private int PushProviders(Node node) => node.NearestProvider is { } provider ? PushFrom(provider) : 0;

    private int PushFrom(Node provider)
    {
        var pushed = provider.NearestProvider is { } outer ? PushFrom(outer) : 0;
        var values = provider.Provided!;
        _contextScope.Push(values);
        return pushed + values.Count;
    }

    /// <summary>Subscribes the node to the nearest provider of each context its last render read.</summary>
    private static void Subscribe(Node node, RenderContext context)
    {
        foreach (var hook in context.ContextHooks)
        {
            for (var provider = node.NearestProvider; provider is not null; provider = provider.NearestProvider)
            {
                if (!ReferenceEquals(((ContextProviderElement)provider.Element).Context, hook.Context)) continue;
                (provider.Subscribers ??= []).Add(node);
                (node.Subscriptions ??= []).Add(provider);
                break;
            }
        }
    }

    /// <summary>
    /// The MAUI policy for the core's render kernel: every failure propagates, because the engine
    /// routes it to the nearest boundary or fails the root itself; effects are queued for after the
    /// commit rather than flushed inline; and the hook count must not change between renders.
    /// </summary>
    private struct KernelHost : IComponentRenderHost
    {
        public readonly ReactorElement? Render(ReactorComponent? component, IRenderFunctionElement? function, RenderContext context)
        {
            var rendered = component is not null ? component.Render() : function!.Render(context);
            context.ValidateHookCount();
            return rendered;
        }

        public readonly void FlushEffects(RenderContext context) { }
        public readonly bool UnderBoundary => true;
        public readonly bool IsPropagating(Exception error) => false;
        public readonly void OnRethrow(Exception error) { }
        public readonly ReactorElement OnFallback(Exception error, bool inEffects) =>
            throw new InvalidOperationException("Unreachable: every MAUI component render is treated as under a boundary.");
        public readonly bool ObservesUnhandled => false;
        public readonly void OnUnhandled(Exception error) { }
        public readonly void OnHookOrderRetry(HookOrderException error) { }
        public readonly void OnHookOrderReset() { }
    }

    /// <summary>
    /// Matches new child elements to the parent's child nodes through the core's child diff
    /// (spec 067 §13.6), the one WinUI uses: keyed children by key, with the minimal moves for a
    /// reorder; unkeyed children by position. A match the identity rules accept is updated in place,
    /// keeping its state and native control; everything else is mounted or unmounted.
    /// </summary>
    private void ReconcileChildren(Node parent, IReadOnlyList<Element?> elements)
    {
        // An Element?[] is an Element[] at run time; the diff skips the nulls itself.
        var next = elements as Element[] ?? [.. elements!];
        var teardown = new TeardownErrors();
        try
        {
            ChildReconciler<Element, Node, ChildHost>.Reconcile(
                parent.ChildElements, next, parent.ChildList, new ChildHost(this, parent, teardown), NoRerender, parent);
        }
        catch (Exception primary)
        {
            // A child the diff already unmounted is unreachable now; report its teardown failures
            // here, alongside the error that is propagating.
            if (teardown.Errors is { } failures) ReportSecondary(failures, primary);
            throw;
        }
        parent.ChildElements = next;
        if (teardown.Errors is { } errors) Report(errors);
    }

    // Each node wires its own re-render callback; the diff's is never called.
    private static readonly Action NoRerender = static () => { };

    /// <summary>Teardown errors collected while reconciling, reported once the children are in place.</summary>
    private sealed class TeardownErrors
    {
        public List<Exception>? Errors;
        public void Add(List<Exception> errors)
        {
            if (errors.Count > 0) (Errors ??= []).AddRange(errors);
        }
    }

    /// <summary>
    /// The engine's answers to the core's child diff, with child nodes as its "native" children.
    /// The engine has no exit transitions, ambient animation, skip hints or pooled natives, so those
    /// answers are constant.
    /// </summary>
    private readonly struct ChildHost(ReactorRoot root, Node parent, TeardownErrors teardown) : IChildReconcileHost<Element, Node>
    {
        public Node? Mount(Element element, Action requestRerender) => root.Mount(element, parent);

        public Node? UpdateChildTracked(Element oldElement, Element newElement, Node control, Action requestRerender, out bool unmountedByUpdate)
        {
            unmountedByUpdate = false;
            root.Update(control, newElement);
            return null;
        }

        public void UnmountChild(Node control)
        {
            root.NoteRemoved(control);
            var errors = new List<Exception>();
            Unmount(control, errors);
            teardown.Add(errors);
        }

        public bool CanUpdate(Element oldElement, Element newElement) => root.CanUpdateInPlace(oldElement, newElement);

        // A child whose element is unchanged is skipped, unless it is dirty itself (ResolveDirtyChildIndices).
        public bool CanSkipUpdate(Element oldElement, Element newElement) => ReferenceEquals(oldElement, newElement);
        public bool ForceFullRenderActive => false;
        public bool ForceRenderThroughWrapper(Element newElement) => false;
        public bool HasActiveContextValues => false;
        public bool ShouldDeclineSkip(Node control) => false;
        public bool NeedsRefreshOnSkip(Element oldElement, Element newElement) => false;
        public void RefreshSkippedChildState(Node control, Element oldElement, Element newElement) { }
        public void CountSkipped(int count) { }

        public bool TryGetChangedIndices(Element[] oldChildren, Element[] newChildren, out int[] changedIndices)
        {
            changedIndices = null!;
            return false;
        }

        // A dirty component re-renders in this pass even when its element is unchanged; its dirty
        // descendants are rendered by the batch itself.
        public DirtyChildIndices ResolveDirtyChildIndices(Node? parentControl, IChildList<Node> children)
        {
            foreach (var child in parent.Children)
                if (child.Dirty) return DirtyChildIndices.ProbeEachChild;
            return default;
        }

        public bool IsOnDirtyAncestorPath(Node control) => control.Dirty;

        public bool HasExitingControls => false;
        public bool HoldsExitingControlIn(IChildList<Node> children) => false;
        public bool IsExiting(Node control) => false;

        public void RemoveChildWithExitTransition(IChildList<Node> children, int index)
        {
            var node = children.Get(index);
            children.RemoveAt(index);
            UnmountChild(node);
        }

        public bool ReplaceChildWithExitTransition(IChildList<Node> children, int index, Node newControl)
        {
            var old = children.Get(index);
            children.Replace(index, newControl);
            UnmountChild(old);
            return false;
        }

        public bool TryGetMountedKey(Node control, out string? key)
        {
            key = control.Element.Key;
            return true;
        }

        public void ReportDuplicateSiblingKeys(Element[] children, Node? parentControl)
        {
            var seen = new HashSet<string>();
            foreach (var child in children)
                if (child?.Key is { } key && !seen.Add(key))
                    throw new InvalidOperationException(
                        $"Two children of {parent.Element.GetType().Name} have the key '{key}'. Keys must be unique among siblings.");
        }

        public bool IsEmpty(Element element) => element is EmptyElement;
        public bool HasAmbientAnimation => false;
        public void OnInsertedUnderAmbient(Node control, Element element) { }
        public void OnMovedUnderAmbient(Node control) { }
    }

    /// <summary>What kind of node an element mounts: one of the engine's own, or a host its handler renders.</summary>
    private static NodeKind KindOf(Element element) => element switch
    {
        EmptyElement => NodeKind.Empty,
        GroupElement => NodeKind.Group,
        ContextProviderElement => NodeKind.Provider,
        ErrorBoundaryElement => NodeKind.Boundary,
        ComponentElement => NodeKind.Component,
        FuncElement => NodeKind.Func,
        MemoElement => NodeKind.Memo,
        _ => NodeKind.Host,
    };

    /// <summary>Whether the node mounted for <paramref name="previous"/> can update to <paramref name="next"/>.</summary>
    private bool CanUpdateInPlace(Element previous, Element next) =>
        SameIdentity(previous, next)
        && (KindOf(next) != NodeKind.Host || _registry.Get(next.GetType()).CanUpdate(previous, next));

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

        node.HookContext?.RunCleanupsIsolated(errors.Add);
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

    /// <summary>Records the top-most natives of a subtree about to be unmounted, to release them after the pass.</summary>
    private void NoteRemoved(Node node)
    {
        if (_registry.ReleaseNative is null || node.Unmounted) return;
        CollectNatives(node, _removed ??= []);
    }

    /// <summary>
    /// Hands each native removed in this pass to the renderer's release hook, once its host has been
    /// synchronized, so the native is no longer in the native tree. The engine never reuses an
    /// unmounted node's native, so the release is final. A failing release is reported like a
    /// teardown error and doesn't stop the others.
    /// </summary>
    private void ReleaseRemoved()
    {
        if (_removed is not { Count: > 0 } removed || _registry.ReleaseNative is not { } release) return;
        _removed = null;
        foreach (var native in removed)
        {
            try
            {
                release(native);
            }
            catch (Exception ex)
            {
                if (UnhandledException is { } handler) handler(ex);
                else System.Diagnostics.Trace.TraceError($"Reactor: releasing a removed control failed: {ex}");
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

    /// <summary>
    /// Reports a failure of the host's own work outside a render (such as a deferred window-page swap)
    /// like an error that escaped every boundary: the tree is unmounted, then the error is reported.
    /// </summary>
    internal void ReportHostError(Exception error) => FailRoot(error);

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
            NoteRemoved(root);
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
        ReleaseRemoved();

        var reported = teardownErrors.Count == 0 ? error : new AggregateException(error.Message, [error, .. teardownErrors]);
        if (UnhandledException is { } handler) handler(reported);
        else System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(reported).Throw();
    }

    private void Wire(Node node)
    {
        node.Rerender = () => MarkDirty(node);
        node.HookContext!.RenderThreadEnqueue = _owner.Enqueue;
    }

    private void SyncHost(Node host)
    {
        if (host.Handler!.IsSlotted)
        {
            // One group per declared child, nulls and empties included: the diff mounts no node for
            // them, but a slotted strategy finds its slots by position (a stack's pages, then its modals).
            var declared = host.ChildElements;
            var slots = new List<IReadOnlyList<object>>(declared.Length);
            var next = 0;
            foreach (var element in declared)
            {
                var slot = new List<object>();
                if (element is not null and not EmptyElement) CollectNatives(host.Children[next++], slot);
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
                    node.HookContext!.FlushEffects();
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
                        ReleaseRemoved();
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

    private void EnsureAccess()
    {
        if (!_scheduler.CheckAccess())
            throw new InvalidOperationException("A ReactorRoot can only be rendered or unmounted on its render thread.");
    }
}
