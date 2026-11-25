using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Navigation;
#if IOS || MACCATALYST
using UIKit;
#endif

namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>
/// Keeps a Shell section's stack in step with the pages a <c>NavigationHost</c> declares (spec 069
/// §4.2). The root is the section's content page (<see cref="ShellSection.Stack"/>[0] is a null
/// sentinel); the pages above it go through <c>ShellSection.Navigation</c>, one awaited
/// <c>PushAsync</c>, <c>PopAsync</c> or <c>PopToRootAsync</c> at a time, never inserting or removing
/// below the top. Each operation is tagged as the navigator's own, so its <c>Navigating</c> isn't
/// taken for the user's. A user's back is preflighted in <c>Shell.Navigating</c> (or, for the
/// Android back key, in <c>Shell.OnBackButtonPressed</c>): its guards run before any page moves, and
/// the navigator stays busy until the platform has finished the transition.
/// </summary>
/// <remarks>
/// The surface is one-directional (<see cref="INativeStack"/>): the binding declares pages to it, and a
/// user's back reaches the binding through <see cref="Binding"/>. A navigator that is detached or
/// stopped while busy stays listed by its Shell until its operation or the user's back has settled,
/// so a window swap waits for it.
/// </remarks>
internal sealed class ShellSectionNavigator : INativeStack
{
    /// <summary>After this long without settling, a user's back logs a warning (it never releases the busy state).</summary>
    internal const long SettleWarningMs = 3000;

    private ShellSection? _section;
    private ShellContent? _content;
    private IReadOnlyList<Page> _pages = [];
    private IReadOnlyList<Page> _modals = [];
    private bool _animated = true;
    private bool _running;
    private bool _stopped;
    private bool _detached;
    private bool _releaseToRoot;
    private bool _holding;
    private bool _modalsDismissed;
    private OwnOperation? _own;
    private UserBack? _userBack;
    private ReactorShell? _lingeringIn;

    // The window's modal stack is shared; this navigator manages only the modals it presented.
    private readonly OwnedModals _ownedModals = new();

    /// <summary>The host this stack belongs to.</summary>
    public NavigationHostBinding? Binding { get; set; }

    public Action<Exception>? OnError { get; set; }

    /// <summary>The handler's <c>Application.ModalPopped</c> subscription, removed at unmount.</summary>
    public EventHandler<ModalPoppedEventArgs>? ModalPoppedHandler { get; set; }

    public ShellSection? Section => _section;

    public bool IsIdle => !_running && _userBack is null;

    public event Action? BecameIdle;

    public bool HasPendingPlatformPops => false;

    /// <summary>A page change waits while one of this stack's modals is shown: Shell would dismiss it.</summary>
    public bool IsHolding => _holding;

    public bool? LastOperationAnimated { get; private set; }

    /// <summary>The user's back the navigator is waiting on, if any (tests read it).</summary>
    internal bool IsUserBackPending => _userBack is not null;

    // ── Attaching ────────────────────────────────────────────────────────────

    /// <summary>Makes this the stack of <paramref name="section"/>, whose content is <paramref name="content"/>.</summary>
    public void Attach(ShellSection section, ShellContent content)
    {
        if (ReferenceEquals(_section, section) && ReferenceEquals(_content, content) && !_detached) return;
        if (_section is { } previous && ReferenceEquals(ShellNatives.GetNavigator(previous), this)) ShellNatives.SetNavigator(previous, null);
        _section = section;
        _content = content;
        _detached = false;
        _releaseToRoot = false;
        ShellNatives.SetNavigator(section, this);
        Run();
    }

    /// <summary>
    /// The section no longer shows this stack: the tab went, or its content changed. With
    /// <paramref name="popToRoot"/> (the tab stays), the pages the stack pushed are popped without
    /// animation, after any operation still running.
    /// </summary>
    public void DetachSection(ShellSection section, bool popToRoot)
    {
        if (!ReferenceEquals(_section, section)) return;
        if (ReferenceEquals(ShellNatives.GetNavigator(section), this)) ShellNatives.SetNavigator(section, null);
        _detached = true;
        _releaseToRoot |= popToRoot;
        Linger();
        Run();
    }

    public void SetDesired(IReadOnlyList<Page> pages, IReadOnlyList<Page> modals, bool animated)
    {
        if (_stopped) return;
        _pages = pages;
        _modals = modals;
        _animated = animated;
        Run();
    }

    public void Run()
    {
        // While a user's back is busy, app navigations are held; they run when it settles.
        if (_running || _userBack is not null || _section is null) return;
        _ = RunAsync();
    }

    /// <summary>
    /// Ends the navigator: its element unmounted. An operation or a user's back still running finishes
    /// first (the Shell keeps listing the navigator as busy until then); then its modals are dismissed,
    /// as their trees are gone.
    /// </summary>
    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        Linger();
        if (_section is null)
        {
            Finish();
            return;
        }
        Run();
    }

    private async Task RunAsync()
    {
        _running = true;
        try
        {
            for (var step = 0; step < 1000; step++)
            {
                if (_userBack is not null || _section is null) return;
                var operation = _stopped || _detached ? ReleaseOperation() : NextOperation();
                if (operation is null) return;
                await operation();
            }
            throw new InvalidOperationException("The Shell section's stack did not settle after 1,000 operations.");
        }
        catch (Exception ex)
        {
            if (!_stopped && !_detached) OnError?.Invoke(ex);
        }
        finally
        {
            _running = false;
            _own = null;
            Finish();
        }
    }

    /// <summary>Once nothing runs: a stopped navigator dismisses its modals, a detached one lets go of its section, and the idle state is raised.</summary>
    private void Finish()
    {
        if (!IsIdle) return;
        if (_stopped && !_modalsDismissed)
        {
            _modalsDismissed = true;
            var section = _section;
            _ownedModals.CleanUp(() => section?.Navigation);
        }
        if (_detached)
        {
            _section = null;
            _content = null;
        }
        if (_lingeringIn is { } shell)
        {
            shell.Lingering.Remove(this);
            _lingeringIn = null;
        }
        BecameIdle?.Invoke();
    }

    /// <summary>Keeps the navigator listed by its Shell while it is busy after leaving its section.</summary>
    private void Linger()
    {
        if (IsIdle && !_releaseToRoot) return;
        if (_lingeringIn is null && ShellNatives.ShellOf(_section) is { } shell)
        {
            _lingeringIn = shell;
            shell.Lingering.Add(this);
        }
    }

    private Func<Task>? ReleaseOperation()
    {
        if (!_detached || !_releaseToRoot || _section is not { } section) return null;
        _releaseToRoot = false;
        if (section.Stack.Count <= 1) return null;
        return Own(OperationKind.Pop, false, () => section.Navigation.PopToRootAsync(false));
    }

    // ── Operations ───────────────────────────────────────────────────────────

    private Func<Task>? NextOperation()
    {
        var section = _section!;
        var navigation = section.Navigation;

        // A modal the app dropped goes first.
        if (NextModalPop(navigation) is { } modalPop) return modalPop;
        if (PageOperation(section, navigation) is { } pageOperation)
        {
            // Shell dismisses the modals it finds before a page navigation (and reports it as the
            // user's): a page change waits until this stack's modals are gone.
            _holding = _ownedModals.In(navigation.ModalStack).Count > 0;
            if (!_holding) return pageOperation;
        }
        else
        {
            _holding = false;
        }
        return NextModalPush(navigation);
    }

    private Func<Task>? PageOperation(ShellSection section, INavigation navigation)
    {
        if (_pages.Count == 0) return null;
        var content = _content!;
        var stack = section.Stack;
        var root = _pages[0];
        if (!ReferenceEquals(content.Content, root))
        {
            // A new root (Reset, or Replace at the root): back to the root, then replace the content's page.
            if (stack.Count > 1) return Own(OperationKind.Pop, false, () => navigation.PopToRootAsync(false));
            return () =>
            {
                content.Content = root;
                return Task.CompletedTask;
            };
        }

        var common = 1;
        while (common < stack.Count && common < _pages.Count && ReferenceEquals(stack[common], _pages[common])) common++;

        if (common < stack.Count)
        {
            // Back to the deepest page that is still declared, then push the rest. Only the last step animates.
            var pushesFollow = common < _pages.Count;
            if (common == 1 && stack.Count > 2)
            {
                var toRoot = _animated && !pushesFollow;
                return Own(OperationKind.Pop, toRoot, () => navigation.PopToRootAsync(toRoot));
            }
            var animated = _animated && !pushesFollow && common == stack.Count - 1;
            return Own(OperationKind.Pop, animated, () => navigation.PopAsync(animated));
        }

        if (common < _pages.Count)
        {
            var page = _pages[common];
            var animated = _animated;
            return Own(OperationKind.Push, animated, () => navigation.PushAsync(page, animated));
        }
        return null;
    }

    private Func<Task>? NextModalPop(INavigation navigation)
    {
        var stack = navigation.ModalStack;
        if (stack.Count == 0 || _ownedModals.Count == 0) return null;
        var actual = _ownedModals.In(stack);
        var common = 0;
        while (common < actual.Count && common < _modals.Count && ReferenceEquals(actual[common], _modals[common])) common++;
        if (common >= actual.Count) return null;
        var top = actual[^1];
        // Another stack's modal is above this one's: wait until it is dismissed.
        if (!ReferenceEquals(stack[^1], top)) return null;
        var animated = _animated;
        return Own(OperationKind.Pop, animated, () => _ownedModals.Pop(navigation, top, animated));
    }

    private Func<Task>? NextModalPush(INavigation navigation)
    {
        // Shell presents a modal on the visible section; a hidden tab's modal waits until it is shown.
        if (_modals.Count == 0 || !ShellNatives.IsVisible(_section!) || _section!.Window is not { } window) return null;
        var actual = _ownedModals.In(navigation.ModalStack);
        if (actual.Count >= _modals.Count) return null;
        var next = _modals[actual.Count];
        var animated = _animated;
        return Own(OperationKind.Push, animated, () => _ownedModals.Push(navigation, window, next, animated));
    }

    /// <summary>
    /// Runs one of the navigator's own operations, tagged so that the <c>Navigating</c> it raises isn't
    /// taken for the user's. MAUI raises it before the operation's first await (after a pending
    /// navigation of the section, if there is one), and the first matching event clears the tag, so it
    /// never covers the platform animation.
    /// </summary>
    private Func<Task> Own(OperationKind kind, bool animated, Func<Task> operation) => async () =>
    {
        LastOperationAnimated = animated;
        _own = new OwnOperation(kind);
        try
        {
            await operation();
        }
        finally
        {
            _own = null;
        }
    };

    private enum OperationKind { Push, Pop }

    private sealed record OwnOperation(OperationKind Kind);

    /// <summary>
    /// Whether <paramref name="args"/> is this navigator's own operation: one is waiting for its event,
    /// of the same kind, and the event targets this section. The operation claims one event.
    /// </summary>
    public bool TryClaim(ShellNavigatingEventArgs args)
    {
        if (_own is not { } own || _section is not { } section) return false;
        var kind = args.Source switch
        {
            ShellNavigationSource.Push or ShellNavigationSource.Insert => OperationKind.Push,
            ShellNavigationSource.Pop or ShellNavigationSource.PopToRoot or ShellNavigationSource.Remove or ShellNavigationSource.Unknown => OperationKind.Pop,
            _ => (OperationKind?)null,
        };
        if (kind != own.Kind || !Targets(args, section)) return false;
        _own = null;
        return true;
    }

    /// <summary>
    /// Whether a navigation targets <paramref name="section"/>: an absolute target names its route; a
    /// relative one (<c>..</c>, or a pushed page's route) is relative to the section shown.
    /// </summary>
    internal static bool Targets(ShellNavigatingEventArgs args, ShellSection section)
    {
        // Shell trims implicit item and section routes from an absolute location, but keeps the content's.
        var target = args.Target?.Location?.OriginalString;
        if (string.IsNullOrEmpty(target)) return false;
        if (target.StartsWith('/'))
        {
            var segments = target.Split('/');
            return segments.Contains(section.Route, StringComparer.Ordinal)
                || section.Items.Any(content => segments.Contains(content.Route, StringComparer.Ordinal));
        }
        return ShellNatives.IsVisible(section);
    }

    // ── The user's back ──────────────────────────────────────────────────────

    private sealed class UserBack
    {
        public required ShellNavigatingEventArgs Args { get; init; }
        public required NavigationHostBinding.BackApproval Approval { get; init; }
        public required ShellSection Section { get; init; }
        public required Page Departing { get; init; }
        public required Page? Destination { get; init; }
        public required int Depth { get; init; }
        public required long StartedAt { get; init; }
        public bool Observing;
        public bool Warned;

        /// <summary>The user let go of the swipe before it could complete; UIKit is rolling it back.</summary>
        public bool InteractionCancelled;
    }

    /// <summary>
    /// A back the user started on this (shown) section, offered by Shell before any page moves: the
    /// iOS and Mac Catalyst back button and swipe (spec 069 §4.2), and Android's when the back key isn't
    /// taken in <c>OnBackButtonPressed</c>. Runs the guards; a refusal cancels it, so no page moves.
    /// Accepted, the navigator is busy until the platform has finished, and the model follows then.
    /// </summary>
    public void OnUserBack(ReactorShell shell, ShellNavigatingEventArgs args)
    {
        if (_section is not { } section || _content is not { } content || _stopped || _detached || Binding is not { } binding) return;
        // At the root Shell offers the back key with an empty target: it belongs to the app or the OS.
        if (string.IsNullOrEmpty(args.Target?.Location?.OriginalString)) return;
        // A modal's dismissal isn't a back in this stack, and another section's navigation isn't either.
        if (section.Navigation.ModalStack.Count > 0 || !Targets(args, section)) return;
        var stack = section.Stack;
        if (stack.Count <= 1 || !args.CanCancel) return;
        if (_userBack is not null)
        {
            // One back at a time (iOS 26 can offer the same back twice).
            args.Cancel();
            return;
        }

        if (!CanPreflight(stack, content, binding))
        {
            // The model holds a change not shown yet, or the stack is mid-operation: the back becomes
            // a guarded navigation of the model, which the navigator then shows, as the back key does
            // for a NavigationPage (spec 069 §2.6).
            args.Cancel();
            binding.OnBackButton();
            return;
        }

        var depth = args.Source == ShellNavigationSource.PopToRoot ? 1 : stack.Count - 1;
        if (binding.PreflightBack(depth) is not { } approval)
        {
            args.Cancel();
            return;
        }

        var back = _userBack = new UserBack
        {
            Args = args,
            Approval = approval,
            Section = section,
            Departing = stack[^1],
            Destination = depth == 1 ? content.Content as Page : stack[depth - 1],
            Depth = depth,
            StartedAt = shell.Clock(),
        };
        shell.Turns.Next(() => Watch(shell, back));
    }

    /// <summary>
    /// The back key (Android's key and toolbar back reach <c>Shell.OnBackButtonPressed</c>): one guarded
    /// back of the model, after which the navigator pops as its own operation, so nothing is
    /// preflighted against a platform pop. Returns whether the key is consumed; at the root, with
    /// nowhere to go, it is passed on to the app or the OS.
    /// </summary>
    public bool OnBackKey()
    {
        if (_section is not { } section || _content is not { } content || _stopped || _detached || Binding is not { } binding) return false;
        if (_userBack is not null) return true;
        var stack = section.Stack;
        if (stack.Count > 1 && CanPreflight(stack, content, binding))
        {
            var depth = stack.Count - 1;
            if (binding.PreflightBack(depth) is { } approval)
            {
                // The declaration follows at once, so the pop starts now, as the navigator's own.
                _pages = [.. _pages.Take(depth)];
                binding.CommitBack(approval);
                Run();
            }
            return true;
        }
        return binding.OnBackButton();
    }

    private bool CanPreflight(IReadOnlyList<Page> stack, ShellContent content, NavigationHostBinding binding) =>
        !_running && !_holding && ShowsDeclaration(stack, content) && binding.ShowsModel;

    private bool ShowsDeclaration(IReadOnlyList<Page> stack, ShellContent content)
    {
        if (_pages.Count != stack.Count || !ReferenceEquals(content.Content, _pages[0])) return false;
        for (var i = 1; i < stack.Count; i++)
            if (!ReferenceEquals(stack[i], _pages[i])) return false;
        return true;
    }

    /// <summary>
    /// Watches an approved back until the platform has finished it, one dispatcher turn at a time. A
    /// cancel by another handler (or a deferral that ends cancelled) settles it as cancelled; on iOS and
    /// Mac Catalyst the transition coordinator reports a cancelled swipe after UIKit's rollback; a
    /// completed back settles at <c>Navigated</c>. No timeout releases it: after
    /// <see cref="SettleWarningMs"/> a warning is logged. A transition the platform started and ended
    /// without reporting either is settled by whether the page left; with no transition signal (Android,
    /// or iOS before the pop starts, as during a <c>Navigating</c> deferral) it keeps waiting.
    /// </summary>
    private void Watch(ReactorShell shell, UserBack back)
    {
        if (!ReferenceEquals(_userBack, back)) return;
        if (back.Args.Cancelled)
        {
            Settle(back, completed: false);
            return;
        }
        if (!back.Observing)
            back.Observing = shell.Transitions.TryObserve(
                back.Departing,
                interactionCancelled: () => back.InteractionCancelled = true,
                completed: cancelled =>
                {
                    if (cancelled) Settle(back, completed: false);
                });
        if (!back.Warned && shell.Clock() - back.StartedAt > SettleWarningMs)
        {
            back.Warned = true;
            DiagnosticLog.Warning(LogCategory.Navigation, "ShellSection.UserBack",
                back.InteractionCancelled
                    ? "A cancelled back swipe in a Shell section has not settled; still waiting for the platform."
                    : "A user's back in a Shell section has not settled; still waiting for the platform.");
        }
        if (back.Warned && back.Observing && !shell.Transitions.InProgress(back.Departing))
        {
            Settle(back, completed: !back.Section.Stack.Contains(back.Departing));
            return;
        }
        shell.Turns.Next(() => Watch(shell, back));
    }

    /// <summary>Shell navigated: a back the user made on this section has completed.</summary>
    public void OnNavigated(ShellNavigatedEventArgs args)
    {
        if (_userBack is { } back)
        {
            if (!back.Section.Stack.Contains(back.Departing)) Settle(back, completed: true);
        }
        else if (IsIdle && !_stopped && !_detached)
        {
            // A modal waits for its section to be shown.
            Run();
        }
    }

    private void Settle(UserBack back, bool completed)
    {
        if (!ReferenceEquals(_userBack, back)) return;
        _userBack = null;
        var section = back.Section;
        // The approval names the pages it was given for: the platform must have left exactly those.
        var landed = completed && !_stopped && !_detached
            && section.Stack.Count == back.Depth
            && !section.Stack.Contains(back.Departing)
            && (back.Depth == 1 ? ReferenceEquals(_content?.Content, back.Destination) : ReferenceEquals(section.Stack[back.Depth - 1], back.Destination));
        if (landed)
        {
            // The declaration follows the platform at once, so nothing pushes the page back before the
            // model's next render.
            _pages = [.. _pages.Take(back.Depth)];
            Binding?.CommitBack(back.Approval);
        }
        else
        {
            Binding?.DiscardBack(back.Approval);
        }
        if (_running) return;
        if (_section is null)
        {
            Finish();
            return;
        }
        Run();
    }

    // ── Modals the user dismissed ─────────────────────────────────────────────

    /// <summary>
    /// Whether <paramref name="modal"/> left the window's stack without the navigator popping it: the
    /// app still declares it. <paramref name="remaining"/> is its position, the declared modals left.
    /// </summary>
    public bool IsUserModalPop(Page modal, out int remaining)
    {
        remaining = 0;
        _ownedModals.Forget(modal);
        if (_stopped || _detached || ReferenceEquals(modal, _ownedModals.Popping)) return false;
        for (remaining = 0; remaining < _modals.Count; remaining++)
            if (ReferenceEquals(_modals[remaining], modal)) return true;
        return false;
    }
}

/// <summary>Schedules the next dispatcher turn of a user's back watch (spec 069 §4.2); headless tests drive it.</summary>
internal interface IShellTurns
{
    void Next(Action work);
}

/// <summary>The platform's view of a section's navigation transitions (spec 069 §4.2); headless tests fake it.</summary>
internal interface IShellTransitions
{
    /// <summary>
    /// Registers for the end of the transition moving <paramref name="departing"/>, if one is running:
    /// <paramref name="interactionCancelled"/> runs when the user lets go of a swipe that won't complete,
    /// and <paramref name="completed"/> receives whether the transition was cancelled, after the
    /// platform's completion or rollback animation. Returns whether it registered.
    /// </summary>
    bool TryObserve(Page departing, Action interactionCancelled, Action<bool> completed);

    /// <summary>Whether the platform is running a transition for <paramref name="departing"/>'s navigation stack.</summary>
    bool InProgress(Page departing);
}

/// <summary>The platform's turns and transitions.</summary>
internal sealed class PlatformShellTransitions(IDispatcher? dispatcher) : IShellTurns, IShellTransitions
{
    public void Next(Action work)
    {
#if ANDROID || IOS || MACCATALYST || WINDOWS
        (dispatcher ?? Application.Current?.Dispatcher)?.DispatchDelayed(TimeSpan.FromMilliseconds(16), work);
#else
        // Headless: nothing runs turns unless a test installs its own.
        _ = dispatcher;
        _ = work;
#endif
    }

    public bool TryObserve(Page departing, Action interactionCancelled, Action<bool> completed)
    {
#if IOS || MACCATALYST
        if (Coordinator(departing) is not { } coordinator) return false;
        coordinator.NotifyWhenInteractionChanges(context =>
        {
            if (context.IsCancelled) interactionCancelled();
        });
        // UIKit runs the completion after the transition's completion or rollback animation.
        coordinator.AnimateAlongsideTransition(static _ => { }, context => completed(context.IsCancelled));
        return true;
#else
        _ = interactionCancelled;
        _ = completed;
        return false;
#endif
    }

    public bool InProgress(Page departing)
    {
#if IOS || MACCATALYST
        return Coordinator(departing) is not null;
#else
        return false;
#endif
    }

#if IOS || MACCATALYST
    private static UIKit.IUIViewControllerTransitionCoordinator? Coordinator(Page departing) =>
        departing.Handler is IPlatformViewHandler { ViewController: { } controller }
            ? (controller.NavigationController ?? controller.ParentViewController?.NavigationController)?.GetTransitionCoordinator()
            : null;
#endif
}
