using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Maui;
using Microsoft.UI.Reactor.Maui.Handlers;

namespace Microsoft.UI.Reactor.Navigation;

/// <summary>The props of the component <c>NavigationHost</c> renders.</summary>
internal sealed record NavigationHostProps<TRoute>(
    NavigationHandle<TRoute> Navigation,
    Func<TRoute, Element?> RouteMap,
    Func<NavigationStackElement, NavigationStackElement>? Configure)
    where TRoute : notnull;

/// <summary>Marks a <c>NavigationHost</c>'s component, so lifecycle discovery stops at a nested host.</summary>
internal interface INavigationHostComponent;

/// <summary>Tells a <c>NavigationHost</c> that it is a <c>ShellTab</c>'s content, so it renders the section's stack.</summary>
internal static class ShellTabScope
{
    public static readonly Context<bool> Context = new(false, "ShellTab");
}

/// <summary>
/// What <c>NavigationHost</c> renders (spec 069 §2.4): a <see cref="NavigationStackElement"/> declaring
/// the handle's back stack and current route, each mapped by the route map and keyed by its entry,
/// with the handle provided to the pages. The non-generic <see cref="NavigationHostBinding"/> keeps
/// the declaration and drives the lifecycle.
/// </summary>
internal sealed class NavigationHostComponent<TRoute> : Component<NavigationHostProps<TRoute>>, INavigationHostComponent
    where TRoute : notnull
{
    private static readonly Func<object, object, bool> RouteEquals =
        static (a, b) => EqualityComparer<TRoute>.Default.Equals((TRoute)a, (TRoute)b);

    public override Element? Render()
    {
        var inShellTab = UseContext(ShellTabScope.Context);
        var bindingRef = UseRef<NavigationHostBinding?>(null);
        var binding = bindingRef.Current ??= new NavigationHostBinding(RouteEquals);
        binding.Rerender = Context._requestRerender;
        binding.Bind(Props.Navigation);
        UseEffect(() => (Action)binding.Unbind, Array.Empty<object>());

        var declaration = binding.Declare();
        var entries = declaration.Entries;
        var pages = new Element?[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            // A route mapped to nothing still takes its position, so a page's position is its entry's.
            var page = Props.RouteMap((TRoute)entries[i].Route) ?? new PageElement(null, null);
            pages[i] = page.WithKey(entries[i].Key);
        }

        var stack = new NavigationStackElement(pages);
        if (Props.Configure is { } configure) stack = configure(stack);
        if (inShellTab)
        {
            if (!binding.WarnedShellTabStack && (stack.Title is not null || stack.Icon is not null || stack.BarBackgroundColor is not null || stack.BarTextColor is not null))
            {
                binding.WarnedShellTabStack = true;
                DiagnosticLog.Warning(LogCategory.Navigation, "NavigationHost.ShellTab",
                    "A NavigationHost in a ShellTab ignores its stack's Title, Icon and bar colors: the tab's title and icon come from ShellTab, the colors from the Shell's appearance.");
            }
            // In a Shell tab the pages are the section's stack (spec 069 §4.2). A host nested in one
            // of them renders its own stack again.
            return NavigationContext<TRoute>.Instance.Provide(Props.Navigation, ShellTabScope.Context.Provide(false, new ShellSectionStackElement(pages)
            {
                Modals = stack.Modals,
                OnModalDismissed = stack.OnModalDismissed,
                Animated = stack.Animated,
                HostBinding = binding,
                HostDeclaration = declaration,
            }));
        }
        // The configured Animated is the host's default; the binding applies a Navigate call's options.
        return NavigationContext<TRoute>.Instance.Provide(Props.Navigation, stack with
        {
            Pages = pages,
            OnPopped = binding.OnUserPop,
            HostBinding = binding,
            HostDeclaration = declaration,
        });
    }
}

/// <summary>One declaration a <c>NavigationHost</c> rendered.</summary>
internal sealed class HostDeclaration(NavigationHostBinding.Entry[] entries, bool? animated, bool isProposal)
{
    public NavigationHostBinding.Entry[] Entries { get; } = entries;

    /// <summary>The animation the last navigation asked for, or <see langword="null"/> for the host's default.</summary>
    public bool? Animated { get; set; } = animated;

    /// <summary>
    /// A proposal mounts the destination above the pages shown, without telling the navigator, so
    /// its <c>onNavigatingTo</c> can run before anything else changes; the final declaration follows.
    /// A push's proposal becomes the committed declaration once accepted.
    /// </summary>
    public bool IsProposal { get; set; } = isProposal;

    /// <summary>For a proposal: set once its destination's <c>onNavigatingTo</c> has run.</summary>
    public bool Processed { get; set; }
}

/// <summary>
/// Binds a <c>NavigationHost</c> to its handle and its <see cref="NavigationStackElement"/>, without
/// the route type (spec 069 §2.3): it keeps the declared entries and their keys, runs the lifecycle
/// guard over the active page, checks a destination's <c>onNavigatingTo</c>, turns a user's back
/// navigation into one guarded navigation, and completes navigations when the navigator settles.
/// </summary>
/// <remarks>
/// A lifecycle callback that throws is reported to the error boundaries above its component, as a
/// handler's asynchronous failure is (an unmounted component's error goes to its nearest mounted
/// ancestor). A throwing <c>onNavigatingFrom</c> or <c>onNavigatingTo</c> cancels the navigation.
/// </remarks>
internal sealed class NavigationHostBinding
{
    internal readonly record struct Entry(object Route, string Key);

    internal readonly record struct OwnedHook(Node Owner, NavigationLifecycleHookState Hook);

    internal sealed class Transition
    {
        public long Generation;
        public required List<OwnedHook> OldHooks { get; init; }
        public required string DestinationKey { get; init; }
        public required object Route { get; init; }
        public required object? PreviousRoute { get; init; }
        public required NavigationMode Mode { get; init; }
        public required bool? Animated { get; init; }
    }

    private enum ProposalState { Pending, Accepted, Rejected }

    /// <summary>
    /// A single-use approval of a user's back (spec 069 §4.2): the depth the user goes back to, the
    /// model's version the guards ran against, and the transition they approved.
    /// </summary>
    internal sealed class BackApproval
    {
        public required int Depth { get; init; }
        public required long Version { get; init; }
        public required Transition Transition { get; init; }
    }

    private sealed class Proposal
    {
        public required long Version { get; init; }
        public required Entry[] Final { get; init; }
        public required Transition Transition { get; init; }
        public required HostDeclaration Declaration { get; init; }
        public ProposalState State;
    }

    private readonly Func<object, object, bool> _routeEquals;
    private readonly Action<NavigatingFromContext> _guard;
    private readonly Action _routeChanged;
    private readonly Action _onNavigatorIdle;
    private readonly Action _complete;
    private INavigationHandle? _handle;

    // The stack this host renders, attached by its handler.
    private Node? _stackNode;
    private INativeStack? _surface;

    // The committed declaration: what the navigator was last asked to show, and the model version it
    // shows. A version whose destination refused stays unshown, so the next render asks again, as
    // WinUI's host does; _refusedVersion remembers it, so the user going back is still understood.
    private Entry[] _rendered = [];
    private HostDeclaration? _committed;
    private long _renderedVersion = -1;
    private long _refusedVersion = long.MinValue;
    private int _nextKey;

    private Proposal? _proposal;

    // A user's back that Shell offered before moving a page (spec 069 §4.2): approved while the
    // platform runs it, then committed to the model, whose next declaration takes it as approved.
    private BackApproval? _approval;
    private BackApproval? _committing;

    private long _generation;
    private readonly List<Transition> _inFlight = [];
    private bool _completionPosted;

    public NavigationHostBinding(Func<object, object, bool> routeEquals)
    {
        _routeEquals = routeEquals;
        _guard = OnNavigatingFrom;
        _routeChanged = () => Rerender?.Invoke();
        _onNavigatorIdle = OnNavigatorIdle;
        _complete = Complete;
        OnUserPop = depth => BackTo(depth);
    }

    /// <summary>Re-renders the host component.</summary>
    public Action? Rerender { get; set; }

    /// <summary>Whether the host warned that a Shell tab ignores its stack's title, icon and bar colors.</summary>
    public bool WarnedShellTabStack { get; set; }

    /// <summary>The stack's <c>OnPopped</c>: the user went back to the given declared depth.</summary>
    public Action<int> OnUserPop { get; }

    // ── The handle ────────────────────────────────────────────────────────────

    public void Bind(INavigationHandle handle)
    {
        if (!ReferenceEquals(_handle, handle))
        {
            Unbind();
            _handle = handle;
            handle.RouteChanged += _routeChanged;
            // Another handle's history: recompute the declaration from it.
            _renderedVersion = long.MinValue;
            _refusedVersion = long.MinValue;
            _proposal = null;
            _approval = null;
            _committing = null;
        }
        handle.LifecycleGuard = _guard;
    }

    public void Unbind()
    {
        if (_handle is not { } handle) return;
        handle.RouteChanged -= _routeChanged;
        if (ReferenceEquals(handle.LifecycleGuard, _guard)) handle.LifecycleGuard = null;
        _handle = null;
    }

    // ── The stack ─────────────────────────────────────────────────────────────

    public void Attach(Node stackNode, ReactorNavigationPage page)
    {
        page.HostBinding = this;
        Attach(stackNode, page.Navigator);
    }

    public void Detach(ReactorNavigationPage page)
    {
        if (ReferenceEquals(page.HostBinding, this)) page.HostBinding = null;
        Detach(page.Navigator);
    }

    /// <summary>Attaches the native stack the host's element rendered (spec 069 §4.2).</summary>
    public void Attach(Node stackNode, INativeStack surface)
    {
        _stackNode = stackNode;
        _surface = surface;
        surface.BecameIdle += _onNavigatorIdle;
    }

    public void Detach(INativeStack surface)
    {
        surface.BecameIdle -= _onNavigatorIdle;
        if (!ReferenceEquals(_surface, surface)) return;
        _surface = null;
        _stackNode = null;
        _inFlight.Clear();
        _approval = null;
    }

    // ── Declaring ─────────────────────────────────────────────────────────────

    /// <summary>The declaration for this render of the host. Called during the host's render.</summary>
    public HostDeclaration Declare()
    {
        var handle = _handle!;
        var version = handle.Version;

        if (_proposal is { } proposal)
        {
            if (proposal.Version == version)
            {
                switch (proposal.State)
                {
                    case ProposalState.Pending:
                        return proposal.Declaration;
                    case ProposalState.Accepted:
                        _proposal = null;
                        return Commit(proposal.Final, version, proposal.Transition);
                    default:
                        // Refused: show the committed pages; the next render asks again.
                        _proposal = null;
                        return _committed!;
                }
            }
            // A newer change supersedes it before it was shown; its destination unmounts.
            _proposal = null;
        }

        if (version == _renderedVersion && _committed is not null) return _committed;

        var final = Entries(handle);
        if (_committed is null || _rendered.Length == 0 || SameKeys(final, _rendered))
            return Commit(final, version, null);

        if (_committing is { } approved)
        {
            _committing = null;
            // The user's back the platform already made: its guards ran before the page moved, so the
            // destination isn't asked again.
            if (version == approved.Version + 1 && final.Length == approved.Depth && final[^1].Key == approved.Transition.DestinationKey)
                return Commit(final, version, approved.Transition);
        }

        var destination = final[^1];
        var transition = new Transition
        {
            OldHooks = CollectHooks(ActivePage()),
            DestinationKey = destination.Key,
            Route = destination.Route,
            PreviousRoute = _rendered[^1].Route,
            Mode = handle.LastMode,
            Animated = handle.LastOptions?.Animated,
        };

        if (Contains(_rendered, destination.Key))
        {
            // The destination is mounted already (a pop): ask it before changing anything.
            if (!NavigatingTo(transition))
            {
                _refusedVersion = version;
                return _committed;
            }
            return Commit(final, version, transition);
        }

        // The destination has to be mounted to be asked: propose it above the pages shown.
        var declaration = new HostDeclaration([.. _rendered, destination], _committed.Animated, isProposal: true);
        _proposal = new Proposal { Version = version, Final = final, Transition = transition, Declaration = declaration };
        return declaration;
    }

    private HostDeclaration Commit(Entry[] entries, long version, Transition? transition, HostDeclaration? promote = null)
    {
        if (transition is not null)
        {
            // Tracked from here even if a newer render replaces this declaration before the navigator
            // sees it: the page it leaves still gets onNavigatedFrom.
            transition.Generation = ++_generation;
            _inFlight.Add(transition);
        }
        var animated = transition is not null ? transition.Animated : _committed?.Animated;
        _rendered = entries;
        _renderedVersion = version;
        if (promote is not null)
        {
            // The proposal the stack element already carries becomes the committed declaration, so
            // a later sync of that element (a page re-rendering) reaches the navigator.
            promote.IsProposal = false;
            promote.Animated = animated;
            _committed = promote;
        }
        else
        {
            _committed = new HostDeclaration(entries, animated, isProposal: false);
        }
        return _committed;
    }

    /// <summary>
    /// The model's routes, root first. An entry keeps its key while it and every entry below it are
    /// equal to the declared ones; anything else gets a fresh key, so it mounts a new page.
    /// </summary>
    private Entry[] Entries(INavigationHandle handle)
    {
        var entries = new Entry[handle.Depth];
        var kept = true;
        for (var i = 0; i < entries.Length; i++)
        {
            var route = handle.RouteAt(i);
            kept = kept && i < _rendered.Length && _routeEquals(_rendered[i].Route, route);
            entries[i] = new Entry(route, kept ? _rendered[i].Key : $"route-{++_nextKey}");
        }
        return entries;
    }

    private static bool SameKeys(Entry[] a, Entry[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
            if (a[i].Key != b[i].Key) return false;
        return true;
    }

    private static bool Contains(Entry[] entries, string key)
    {
        foreach (var entry in entries)
            if (entry.Key == key) return true;
        return false;
    }

    // ── Synchronizing the navigator ──────────────────────────────────────────

    /// <summary>
    /// Called by the stack's children strategy with the pages of <paramref name="declaration"/>,
    /// once they are mounted. Decides what the navigator is asked to show. <paramref name="animated"/>
    /// is the host's default, which a navigation's own options override.
    /// </summary>
    public void Sync(
        HostDeclaration declaration, bool animated, IReadOnlyList<Page> pages, IReadOnlyList<Page> modals, INativeStack navigator)
    {
        if (declaration.IsProposal)
        {
            // Until the follow-up render, the navigator keeps the committed pages.
            if (declaration.Processed) return;
            declaration.Processed = true;
            if (_proposal is not { State: ProposalState.Pending } proposal || !ReferenceEquals(proposal.Declaration, declaration)) return;

            if (!NavigatingTo(proposal.Transition))
            {
                proposal.State = ProposalState.Rejected;
                _refusedVersion = proposal.Version;
            }
            else if (SameKeys(proposal.Final, declaration.Entries))
            {
                // A push: the proposal is the final declaration.
                _proposal = null;
                Commit(proposal.Final, proposal.Version, proposal.Transition, promote: declaration);
                navigator.SetDesired(pages, modals, declaration.Animated ?? animated);
                return;
            }
            else
            {
                proposal.State = ProposalState.Accepted;
            }
            Rerender?.Invoke();
            return;
        }

        navigator.SetDesired(pages, modals, declaration.Animated ?? animated);
    }

    // ── Back ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The user went back to declared depth <paramref name="depth"/>: one guarded navigation there.
    /// Understood only while the model is the one the host shows (or one whose destination refused);
    /// otherwise the pop is refused, the page is pushed back, and the host goes on to show the model.
    /// Going back to the model's current route, which the host isn't showing because its destination
    /// refused, is another attempt to arrive there.
    /// </summary>
    private bool BackTo(int depth)
    {
        if (_handle is not { } handle || depth < 1 || depth >= _rendered.Length || depth > handle.Depth) return false;
        if (handle.Version != _renderedVersion && handle.Version != _refusedVersion) return false;
        for (var i = 0; i < depth; i++)
            if (!_routeEquals(_rendered[i].Route, handle.RouteAt(i))) return false;

        if (depth == handle.Depth)
        {
            Rerender?.Invoke();
            return true;
        }
        return handle.PopToDepth(depth);
    }

    /// <summary>Whether the host shows the model's stack as it is, with no change pending.</summary>
    public bool ShowsModel => _handle is { } handle && handle.Version == _renderedVersion && _proposal is null && _approval is null;

    /// <summary>
    /// A user's back that Shell offers before any page moves (spec 069 §4.2): runs the departing
    /// page's <c>onNavigatingFrom</c> (and the handle's guards) and the destination's
    /// <c>onNavigatingTo</c>, without changing the model. Returns the approval, or
    /// <see langword="null"/> when the back is refused or isn't one this host can approve now (the
    /// host isn't showing the model, or a callback navigated).
    /// </summary>
    public BackApproval? PreflightBack(int depth)
    {
        if (_handle is not { } handle || !ShowsModel || depth < 1 || depth >= _rendered.Length || _rendered.Length != handle.Depth)
            return null;
        for (var i = 0; i < _rendered.Length; i++)
            if (!_routeEquals(_rendered[i].Route, handle.RouteAt(i))) return null;

        var version = handle.Version;
        var destination = _rendered[depth - 1];
        var transition = new Transition
        {
            OldHooks = CollectHooks(ActivePage()),
            DestinationKey = destination.Key,
            Route = destination.Route,
            PreviousRoute = _rendered[^1].Route,
            Mode = NavigationMode.Pop,
            Animated = null,
        };
        if (!handle.CanPopToDepth(depth) || handle.Version != version) return null;
        if (!NavigatingTo(transition) || handle.Version != version) return null;
        return _approval = new BackApproval { Depth = depth, Version = version, Transition = transition };
    }

    /// <summary>
    /// The platform finished the approved back: the model moves to the approved entry without its
    /// guards running again. Void if the model changed meanwhile; the host then shows the model.
    /// </summary>
    public void CommitBack(BackApproval approval)
    {
        if (!ReferenceEquals(_approval, approval)) return;
        _approval = null;
        if (_handle is { } handle && handle.Version == approval.Version)
        {
            _committing = approval;
            if (handle.PopToDepth(approval.Depth, approved: true)) return;
            _committing = null;
        }
        Rerender?.Invoke();
    }

    /// <summary>The platform cancelled the approved back (the user let go of the swipe): nothing moved.</summary>
    public void DiscardBack(BackApproval approval)
    {
        if (!ReferenceEquals(_approval, approval)) return;
        _approval = null;
        // Changes the model made meanwhile were held; show them now.
        Rerender?.Invoke();
    }

    /// <summary>
    /// The system back button (Android): one guarded back navigation, consuming the key whether it is
    /// accepted or refused, so MAUI doesn't pop as well. While the host shows the model's stack (or a
    /// refused destination's) it goes back from the page shown; while the model holds a change the host
    /// hasn't shown yet (a pending push), it is the handle's <c>GoBack</c>, which supersedes that change.
    /// Only when neither the pages shown nor the model have anywhere to go is the key passed on, so
    /// the app or the OS handles it.
    /// </summary>
    public bool OnBackButton()
    {
        if (_handle is not { } handle) return _rendered.Length > 1;
        var shown = handle.Version == _renderedVersion || handle.Version == _refusedVersion;
        if (shown && _rendered.Length > 1)
        {
            BackTo(_rendered.Length - 1);
            return true;
        }
        // The model holds a change the host hasn't shown yet: going back in the model supersedes it.
        if (handle.CanGoBack)
        {
            handle.GoBack();
            return true;
        }
        return _rendered.Length > 1;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private Node? ActivePage() => _rendered.Length == 0 ? null : FindPage(_rendered[^1].Key);

    private Node? FindPage(string key)
    {
        if (_stackNode is not { Unmounted: false } stack) return null;
        foreach (var child in stack.Children)
            if (child.Element.Key == key) return child;
        return null;
    }

    private void OnNavigatingFrom(NavigatingFromContext context)
    {
        foreach (var (owner, hook) in CollectHooks(ActivePage()))
        {
            if (hook.OnNavigatingFrom is not { } callback) continue;
            try
            {
                callback(context);
            }
            catch (Exception ex)
            {
                context.Cancel();
                Report(owner, ex);
            }
            if (context.IsCancelled) return;
        }
    }

    private bool NavigatingTo(Transition transition)
    {
        var context = new NavigatingToContext(transition.Route, transition.PreviousRoute, transition.Mode);
        foreach (var (owner, hook) in CollectHooks(FindPage(transition.DestinationKey)))
        {
            if (hook.OnNavigatingTo is not { } callback) continue;
            try
            {
                callback(context);
            }
            catch (Exception ex)
            {
                context.Cancel();
                Report(owner, ex);
            }
            if (context.IsCancelled)
            {
                MauiNavigationDiagnostics.Cancelled(transition.Route, "destination guard");
                return false;
            }
        }
        return true;
    }

    private static void Report(Node owner, Exception error) => owner.Root.ReportCallbackError(owner, error);

    /// <summary>
    /// The lifecycle hooks of a page's components, parents first. A nested navigation stack or host
    /// owns its own pages' lifecycle, so the walk stops there.
    /// </summary>
    private static List<OwnedHook> CollectHooks(Node? page)
    {
        var hooks = new List<OwnedHook>();
        if (page is not null) Collect(page, hooks);
        return hooks;

        static void Collect(Node node, List<OwnedHook> hooks)
        {
            if (node.Element is NavigationStackElement or ShellSectionStackElement || node.Component is INavigationHostComponent) return;
            if (node.IsRenderable && node.HookContext?.GetNavigationLifecycleHook() is { } hook) hooks.Add(new(node, hook));
            foreach (var child in node.Children) Collect(child, hooks);
        }
    }

    private void OnNavigatorIdle()
    {
        if (_inFlight.Count == 0 || _completionPosted || _stackNode is not { } stack) return;
        _completionPosted = true;
        // Posted: the navigator can settle inside a commit, and callbacks may set state.
        stack.Root.Post(_complete);
    }

    /// <summary>
    /// Completes the navigations the navigator has finished: when it is idle and MAUI no longer
    /// lists a page the user popped (or that pop's window expired), and every change of the model has
    /// been shown. Every page left gets <c>onNavigatedFrom</c>; only the latest destination gets
    /// <c>onNavigatedTo</c>.
    /// </summary>
    private void Complete()
    {
        _completionPosted = false;
        if (_surface is not { IsIdle: true, HasPendingPlatformPops: false, IsHolding: false }) return;
        // A change the host hasn't shown yet may supersede these: complete them once it is shown
        // (the navigator goes idle again after that sync).
        if (_proposal is not null || (_handle is { } handle && handle.Version != _renderedVersion && handle.Version != _refusedVersion)) return;
        var finished = _inFlight.ToArray();
        _inFlight.Clear();
        foreach (var transition in finished)
        {
            if (transition.Generation == _generation)
            {
                var to = new NavigatedToContext(transition.Route, transition.PreviousRoute, transition.Mode);
                foreach (var (owner, hook) in CollectHooks(FindPage(transition.DestinationKey)))
                    Invoke(owner, hook.OnNavigatedTo, to);
            }
            if (transition.OldHooks.Count > 0)
            {
                var from = new NavigatedFromContext(transition.PreviousRoute!, transition.Route, transition.Mode);
                foreach (var (owner, hook) in transition.OldHooks)
                    Invoke(owner, hook.OnNavigatedFrom, from);
            }
        }

        // A navigation's own animation option ends with it: later changes (a modal, a page replacing
        // its native page) use the host's default.
        if (_inFlight.Count == 0 && _committed is not null) _committed.Animated = null;

        static void Invoke<T>(Node owner, Action<T>? callback, T context)
        {
            try
            {
                callback?.Invoke(context);
            }
            catch (Exception ex)
            {
                Report(owner, ex);
            }
        }
    }
}
