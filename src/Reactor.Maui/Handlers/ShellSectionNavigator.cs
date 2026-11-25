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
/// taken for the user's. A user's back is preflighted in <c>Shell.Navigating</c>: its guards run before
/// any page moves, and the navigator stays busy until the platform has finished the transition.
/// </summary>
internal sealed class ShellSectionNavigator : INativeStack
{
    /// <summary>After this long without settling, a user's back logs a warning, then settles once the platform shows no transition.</summary>
    internal const long SettleWarningMs = 3000;

    private ShellSection? _section;
    private ShellContent? _content;
    private IReadOnlyList<Page> _pages = [];
    private IReadOnlyList<Page> _modals = [];
    private bool _animated = true;
    private bool _running;
    private bool _stopped;
    private OwnOperation? _own;
    private UserBack? _userBack;

    // The window's modal stack is shared; this navigator manages only the modals it presented.
    private readonly HashSet<Page> _ownedModals = new(ReferenceEqualityComparer.Instance);
    private Page? _poppingModal;

    /// <summary>The host this stack belongs to.</summary>
    public NavigationHostBinding? Binding { get; set; }

    public Action<Exception>? OnError { get; set; }

    public ShellSection? Section => _section;

    public bool IsIdle => !_running && _userBack is null;

    public event Action? BecameIdle;

    public bool HasPendingPlatformPops => false;

    public bool? LastOperationAnimated { get; private set; }

    /// <summary>The user's back the navigator is waiting on, if any (tests read it).</summary>
    internal bool IsUserBackPending => _userBack is not null;

    // ── Attaching ────────────────────────────────────────────────────────────

    /// <summary>Makes this the stack of <paramref name="section"/>, whose content is <paramref name="content"/>.</summary>
    public void Attach(ShellSection section, ShellContent content)
    {
        if (ReferenceEquals(_section, section) && ReferenceEquals(_content, content)) return;
        if (_section is { } previous && ReferenceEquals(ShellNatives.GetNavigator(previous), this)) ShellNatives.SetNavigator(previous, null);
        _section = section;
        _content = content;
        ShellNatives.SetNavigator(section, this);
        Run();
    }

    /// <summary>The section no longer shows this stack.</summary>
    public void DetachSection(ShellSection section)
    {
        if (!ReferenceEquals(_section, section)) return;
        if (ReferenceEquals(ShellNatives.GetNavigator(section), this)) ShellNatives.SetNavigator(section, null);
        _section = null;
        _content = null;
        _userBack = null;
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
        if (_running || _stopped || _userBack is not null || _section is null) return;
        _ = RunAsync();
    }

    /// <summary>Ends the navigator: its element unmounted. Its modals are dismissed, as their trees are gone.</summary>
    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        _userBack = null;
        if (!_running) DismissOwnedModals();
    }

    private async Task RunAsync()
    {
        _running = true;
        try
        {
            for (var step = 0; step < 1000; step++)
            {
                if (_stopped || _userBack is not null || _section is null) return;
                var operation = NextOperation();
                if (operation is null) return;
                await operation();
            }
            throw new InvalidOperationException("The Shell section's stack did not settle after 1,000 operations.");
        }
        catch (Exception ex)
        {
            if (!_stopped) OnError?.Invoke(ex);
        }
        finally
        {
            _running = false;
            _own = null;
            if (_stopped) DismissOwnedModals();
            if (IsIdle) BecameIdle?.Invoke();
        }
    }

    // ── Operations ───────────────────────────────────────────────────────────

    private Func<Task>? NextOperation()
    {
        var section = _section!;
        var content = _content!;
        var navigation = section.Navigation;

        // A modal the app dropped goes first: Shell's page navigation dismisses modals it finds.
        if (NextModalPop(navigation) is { } modalPop) return modalPop;
        if (_pages.Count == 0) return NextModalPush(navigation);

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

        return NextModalPush(navigation);
    }

    private Func<Task>? NextModalPop(INavigation navigation)
    {
        var stack = navigation.ModalStack;
        if (stack.Count == 0 || _ownedModals.Count == 0) return null;
        var actual = stack.Where(_ownedModals.Contains).ToList();
        var common = 0;
        while (common < actual.Count && common < _modals.Count && ReferenceEquals(actual[common], _modals[common])) common++;
        if (common >= actual.Count) return null;
        var top = actual[^1];
        // Another stack's modal is above this one's: wait until it is dismissed.
        if (!ReferenceEquals(stack[^1], top)) return null;
        var animated = _animated;
        return Own(OperationKind.Pop, animated, async () =>
        {
            _poppingModal = top;
            try
            {
                await navigation.PopModalAsync(animated);
            }
            finally
            {
                _poppingModal = null;
                _ownedModals.Remove(top);
            }
        });
    }

    private Func<Task>? NextModalPush(INavigation navigation)
    {
        // Shell presents a modal on the visible section; a hidden tab's modal waits until it is shown.
        if (_modals.Count == 0 || !ShellNatives.IsVisible(_section!) || _section!.Window is null) return null;
        var actual = navigation.ModalStack.Where(_ownedModals.Contains).ToList();
        if (actual.Count >= _modals.Count) return null;
        var next = _modals[actual.Count];
        var animated = _animated;
        return Own(OperationKind.Push, animated, () =>
        {
            _ownedModals.Add(next);
            return navigation.PushModalAsync(next, animated);
        });
    }

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

    private sealed class OwnOperation(OperationKind kind)
    {
        public OperationKind Kind { get; } = kind;
        public bool Claimed;
    }

    /// <summary>
    /// Whether <paramref name="args"/> is this navigator's own operation: one is running, of the same
    /// kind, and the event targets this section. Each operation claims one event.
    /// </summary>
    public bool TryClaim(ShellNavigatingEventArgs args)
    {
        if (_own is not { Claimed: false } own || _section is not { } section) return false;
        var kind = args.Source switch
        {
            ShellNavigationSource.Push or ShellNavigationSource.Insert => OperationKind.Push,
            ShellNavigationSource.Pop or ShellNavigationSource.PopToRoot or ShellNavigationSource.Remove or ShellNavigationSource.Unknown => OperationKind.Pop,
            _ => (OperationKind?)null,
        };
        if (kind != own.Kind || !Targets(args, section)) return false;
        own.Claimed = true;
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
    /// iOS back button and swipe, the Android toolbar back and back key (spec 069 §4.2). Runs the
    /// guards; a refusal cancels it, so no page moves. Accepted, the navigator is busy until the
    /// platform has finished, and the model follows then.
    /// </summary>
    public void OnUserBack(ReactorShell shell, ShellNavigatingEventArgs args)
    {
        if (_section is not { } section || _content is not { } content || _stopped || Binding is not { } binding) return;
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

        if (_running || !ShowsDeclaration(stack, content) || !binding.ShowsModel)
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
            Departing = stack[^1],
            Destination = depth == 1 ? content.Content as Page : stack[depth - 1],
            Depth = depth,
            StartedAt = shell.Clock(),
        };
        shell.Turns.Next(() => Watch(shell, back));
    }

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
    /// <see cref="SettleWarningMs"/> a warning is logged, and it settles once the platform shows no
    /// transition in progress.
    /// </summary>
    private void Watch(ReactorShell shell, UserBack back)
    {
        if (!ReferenceEquals(_userBack, back) || _stopped) return;
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
                    ? "A cancelled back swipe in a Shell section has not settled; waiting until the platform shows no transition."
                    : "A user's back in a Shell section has not settled; waiting until the platform shows no transition.");
        }
        if (back.Warned && !shell.Transitions.InProgress(back.Departing))
        {
            Settle(back, completed: !_section!.Stack.Contains(back.Departing));
            return;
        }
        shell.Turns.Next(() => Watch(shell, back));
    }

    /// <summary>Shell navigated: a back the user made on this section has completed.</summary>
    public void OnNavigated(ShellNavigatedEventArgs args)
    {
        if (_userBack is { } back && _section is { } section && !section.Stack.Contains(back.Departing))
            Settle(back, completed: true);
        else if (IsIdle && !_stopped)
            // A modal waits for its section to be shown.
            Run();
    }

    private void Settle(UserBack back, bool completed)
    {
        if (!ReferenceEquals(_userBack, back)) return;
        _userBack = null;
        var section = _section;
        // The approval names the pages it was given for: the platform must have left exactly those.
        var landed = completed && section is not null
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
        if (_section is null || _stopped)
        {
            BecameIdle?.Invoke();
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
        _ownedModals.Remove(modal);
        if (_stopped || ReferenceEquals(modal, _poppingModal)) return false;
        for (remaining = 0; remaining < _modals.Count; remaining++)
            if (ReferenceEquals(_modals[remaining], modal)) return true;
        return false;
    }

    private async void DismissOwnedModals()
    {
        try
        {
            var navigation = _section?.Navigation;
            while (navigation is not null && navigation.ModalStack.Count > 0 && _ownedModals.Contains(navigation.ModalStack[^1]))
            {
                var top = navigation.ModalStack[^1];
                _ownedModals.Remove(top);
                await navigation.PopModalAsync(false);
            }
        }
        catch (Exception)
        {
            // The window is closing; there is no tree left to report to.
        }
        _ownedModals.Clear();
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
