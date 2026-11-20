using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Maui.Handlers;

internal sealed class PageHandler : Reactor.Hosting.IElementHandler<PageElement, ContentPage>
{
    public ContentPage Mount(HandlerContext<PageElement> context, PageElement element)
    {
        var page = new ContentPage();
        if (element.AutomationId is { } automationId) page.AutomationId = automationId;
        page.Appearing += (_, _) => context.Current.OnAppearing?.Invoke();
        page.Disappearing += (_, _) => context.Current.OnDisappearing?.Invoke();
        Apply(context, page, null, element);
        return page;
    }

    public void Update(HandlerContext<PageElement> context, PageElement oldElement, PageElement newElement, ContentPage control) =>
        Apply(context, control, oldElement, newElement);

    private static void Apply(HandlerContext<PageElement> context, ContentPage page, PageElement? previous, PageElement next)
    {
        Props.SetRef(page, Page.TitleProperty, previous?.Title, next.Title);
        Props.SetRef(page, VisualElement.BackgroundColorProperty, previous?.BackgroundColor, next.BackgroundColor);
        Props.Set(page, Page.PaddingProperty, previous?.Padding, next.Padding);
        Props.SetImage(page, Page.IconImageSourceProperty, previous?.Icon, next.Icon, previous is null);
        Props.Set(page, NavigationPage.HasNavigationBarProperty, previous?.HasNavigationBar, next.HasNavigationBar);
        Props.Set(page, NavigationPage.HasBackButtonProperty, previous?.HasBackButton, next.HasBackButton);
        Props.SetRef(page, NavigationPage.BackButtonTitleProperty, previous?.BackButtonTitle, next.BackButtonTitle);

        if (previous is null || !SameToolbar(previous.ToolbarItems, next.ToolbarItems))
        {
            page.ToolbarItems.Clear();
            for (var i = 0; i < next.ToolbarItems.Length; i++)
            {
                var index = i;
                var action = next.ToolbarItems[i];
                var item = new ToolbarItem { Text = action.Text, IconImageSource = action.Icon, Order = action.Order, Priority = action.Priority };
                // Reads the current element, so a re-render that only swaps the callback keeps the item.
                item.Clicked += (_, _) =>
                {
                    var items = context.Current.ToolbarItems;
                    if (index < items.Length) items[index].OnClick();
                };
                page.ToolbarItems.Add(item);
            }
        }
    }

    private static bool SameToolbar(ToolbarAction[] a, ToolbarAction[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i].Text != b[i].Text || a[i].Order != b[i].Order || a[i].Priority != b[i].Priority || !Props.SameImage(a[i].Icon, b[i].Icon))
                return false;
        }
        return true;
    }

    public IChildrenStrategy<PageElement, ContentPage> Children { get; } =
        ChildrenStrategy.Single<PageElement, ContentPage, object>(
            e => e.Content,
            (page, content) =>
            {
                if (content is Page)
                    throw new InvalidOperationException("A page's content must be a view. Put pages in a NavigationStack, Tabs or Flyout instead.");
                page.Content = content is null ? null : Props.AsView(content);
            });
}

/// <summary>
/// Keeps a <see cref="NavigationPage"/>'s stack and modal stack in step with the declared ones.
/// MAUI navigation is asynchronous, so operations run one at a time; each step is chosen by
/// comparing the live stack with the latest declared one, so a change made mid-animation is
/// picked up by the next step.
/// </summary>
internal sealed class StackNavigator(NavigationPage navigationPage)
{
    // A platform pop can stay in MAUI's stacks after Popped/ModalPopped is raised: iOS removes the
    // page when the pop animation ends, and a pop during a push animation can leave it there. The
    // navigator waits up to PlatformPopGraceMs for it to go, then reconciles with what MAUI shows,
    // and keeps re-checking until PlatformPopTimeoutMs in case the page leaves later.
    private const int PlatformPopGraceMs = 600;
    private const int PlatformPopTimeoutMs = 3000;

    private IReadOnlyList<Page> _pages = [];
    private IReadOnlyList<Page> _modals = [];
    private bool _running;
    private bool _stopped;

    // The window's modal stack is shared by every navigation stack in it; this one manages only the
    // modals it presented.
    private readonly HashSet<Page> _ownedModals = new(ReferenceEqualityComparer.Instance);

    // MAUI pops only the top modal, so reordering modals pops ones that are still declared.
    private Page? _poppingModal;

    // Pages the platform popped that may still be listed in MAUI's stacks, with when.
    private readonly List<(Page Page, long At)> _platformPops = [];
    private bool _recheckScheduled;

    /// <summary>Receives failures of the asynchronous navigation work.</summary>
    public Action<Exception>? OnError { get; set; }

    /// <summary>
    /// Whether a pop of <paramref name="popped"/> was the user's: the app still declares the page,
    /// so only the platform can have removed it (the navigator pops only undeclared pages). When it
    /// was, <paramref name="depth"/> is the depth left: the page's position in the declared stack.
    /// This is decided by the declaration, not by timing or MAUI's stack, which iOS updates only
    /// after it raises <c>Popped</c>.
    /// </summary>
    public bool IsUserPop(Page popped, bool modal, out int depth)
    {
        depth = 0;
        if (_stopped || (modal && ReferenceEquals(popped, _poppingModal))) return false;
        var declared = modal ? _modals : _pages;
        for (depth = 0; depth < declared.Count; depth++)
            if (ReferenceEquals(declared[depth], popped)) return true;
        return false;
    }

    /// <summary>
    /// Records a pop the platform made. Reconciling waits until MAUI has removed the page, so a
    /// page the app wants back is pushed after it is gone rather than "found" still on the stack.
    /// </summary>
    public void NotePlatformPop(Page page) => _platformPops.Add((page, Environment.TickCount64));

    /// <summary>A modal left the window's stack, whoever popped it.</summary>
    public void ForgetModal(Page modal) => _ownedModals.Remove(modal);

    /// <summary>True when no reconcile is running (for tests that need a settled navigator).</summary>
    internal bool IsIdle => !_running;

    public void SetDesired(IReadOnlyList<Page> pages, IReadOnlyList<Page> modals)
    {
        if (_stopped) return;
        _pages = pages;
        _modals = modals;
        Run();
    }

    public void Run()
    {
        if (_running || _stopped) return;
        _ = RunAsync();
    }

    /// <summary>
    /// Ends the navigator when its element unmounts: pending work stops after its current platform
    /// operation, and the modals it presented are dismissed, since their trees are gone. A modal
    /// pushed by that last operation is included, and one buried under another stack's modal is
    /// dismissed once it is uncovered.
    /// </summary>
    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        // A running loop calls CleanUpOwnedModals when its current operation finishes.
        if (!_running) CleanUpOwnedModals();
    }

    private async Task RunAsync()
    {
        _running = true;
        try
        {
            for (var step = 0; step < 1000; step++)
            {
                while (!_stopped && PendingPlatformPops(out var settling) && settling) await Task.Delay(16);
                if (_stopped) return;
                var operation = NextOperation();
                if (operation is null)
                {
                    // Settled against a stack that may still list a popped page: look again shortly.
                    if (PendingPlatformPops(out _)) ScheduleRecheck();
                    return;
                }
                await operation();
            }
            throw new InvalidOperationException("The navigation stack did not settle after 1,000 operations.");
        }
        catch (Exception ex)
        {
            if (!_stopped) OnError?.Invoke(ex);
        }
        finally
        {
            _running = false;
            if (_stopped) CleanUpOwnedModals();
        }
    }

    /// <summary>
    /// Drops platform pops that have left MAUI's stacks or timed out. Returns whether any remain, and
    /// in <paramref name="settling"/> whether one is still within its grace period.
    /// </summary>
    private bool PendingPlatformPops(out bool settling)
    {
        settling = false;
        if (_platformPops.Count == 0) return false;
        var now = Environment.TickCount64;
        var navigation = navigationPage.Navigation;
        _platformPops.RemoveAll(p => now - p.At > PlatformPopTimeoutMs
            || (!navigation.NavigationStack.Contains(p.Page) && !navigation.ModalStack.Contains(p.Page)));
        foreach (var pop in _platformPops)
            settling |= now - pop.At < PlatformPopGraceMs;
        return _platformPops.Count > 0;
    }

    private async void ScheduleRecheck()
    {
        if (_recheckScheduled) return;
        _recheckScheduled = true;
        try
        {
            await Task.Delay(100);
        }
        finally
        {
            _recheckScheduled = false;
        }
        Run();
    }

    private Func<Task>? NextOperation()
    {
        var navigation = navigationPage.Navigation;
        var actual = navigation.NavigationStack;
        var desired = _pages;
        // A navigation page cannot be empty: keep the pages shown, but still reconcile the modals.
        if (desired.Count == 0) return NextModalOperation(navigation);

        if (actual.Count == 0) return () => navigationPage.PushAsync(desired[0], animated: false);

        if (!ReferenceEquals(actual[0], desired[0]))
        {
            // New root: insert it under the stack, or drop pages above it if it is already there.
            return actual.Contains(desired[0])
                ? Sync(() => navigation.RemovePage(actual[0]))
                : Sync(() => navigation.InsertPageBefore(desired[0], actual[0]));
        }

        var common = 1;
        while (common < actual.Count && common < desired.Count && ReferenceEquals(actual[common], desired[common])) common++;

        if (common < actual.Count)
        {
            // Pages below the top go without animation; the top one pops, or is replaced by a push.
            if (common < actual.Count - 1) return Sync(() => navigation.RemovePage(actual[common]));
            return common == desired.Count
                ? () => navigationPage.PopAsync(animated: true)
                : () => navigationPage.PushAsync(desired[common], animated: true);
        }
        if (common < desired.Count) return () => navigationPage.PushAsync(desired[common], animated: true);

        return NextModalOperation(navigation);
    }

    private Func<Task>? NextModalOperation(INavigation navigation)
    {
        // Modals need the page to be in a window.
        if (navigationPage.Window is null) return null;
        var stack = navigation.ModalStack;
        var actual = stack.Where(_ownedModals.Contains).ToList();
        var common = 0;
        while (common < actual.Count && common < _modals.Count && ReferenceEquals(actual[common], _modals[common])) common++;
        if (common < actual.Count)
        {
            var top = actual[^1];
            // Another navigation stack's modal is above this one's: wait until it is dismissed.
            if (!ReferenceEquals(stack[^1], top)) return null;
            return async () =>
            {
                _poppingModal = top;
                try
                {
                    await navigation.PopModalAsync(animated: true);
                }
                finally
                {
                    _poppingModal = null;
                }
            };
        }
        if (common < _modals.Count)
        {
            var next = _modals[common];
            return () =>
            {
                _ownedModals.Add(next);
                _modalWindow = navigationPage.Window;
                return navigation.PushModalAsync(next, animated: true);
            };
        }
        return null;
    }

    private bool _cleaning;
    private Application? _cleanupApp;

    // The window this navigator presented modals in. Cleanup goes through it: by then the
    // navigation page may have left the window, and its own Navigation no longer reaches the modals.
    private Window? _modalWindow;

    /// <summary>
    /// Dismisses the modals this (stopped) navigator presented, top down while they are on top.
    /// One covered by another stack's modal is left until that modal goes: the navigator keeps
    /// listening to <see cref="Application.ModalPopped"/> until all of its modals are gone.
    /// </summary>
    private async void CleanUpOwnedModals()
    {
        if (_cleaning) return;
        _cleaning = true;
        try
        {
            // No window left: the modals went with it.
            var navigation = (_modalWindow ?? navigationPage.Window)?.Navigation;
            if (navigation is null) _ownedModals.Clear();
            while (navigation is not null && navigation.ModalStack.Count > 0 && _ownedModals.Contains(navigation.ModalStack[^1]))
            {
                var top = navigation.ModalStack[^1];
                _ownedModals.Remove(top);
                await navigation.PopModalAsync(animated: false);
            }
            if (navigation is not null) _ownedModals.RemoveWhere(m => !navigation.ModalStack.Contains(m));
        }
        catch (Exception)
        {
            // The window is closing or another owner is mid-transition; there is no tree left to report to.
        }
        finally
        {
            _cleaning = false;
        }

        if (_ownedModals.Count > 0)
        {
            if (_cleanupApp is null && Application.Current is { } app)
            {
                _cleanupApp = app;
                app.ModalPopped += OnModalPoppedWhileStopped;
            }
        }
        else if (_cleanupApp is { } subscribed)
        {
            subscribed.ModalPopped -= OnModalPoppedWhileStopped;
            _cleanupApp = null;
        }
    }

    private void OnModalPoppedWhileStopped(object? sender, ModalPoppedEventArgs e)
    {
        _ownedModals.Remove(e.Modal);
        CleanUpOwnedModals();
    }

    private static Func<Task> Sync(Action action) => () =>
    {
        action();
        return Task.CompletedTask;
    };
}

internal sealed class ReactorNavigationPage : NavigationPage
{
    public StackNavigator Navigator = null!;
    public EventHandler<ModalPoppedEventArgs>? ModalPoppedHandler;
    public Action<Page>? ReportPopped;

    public ReactorNavigationPage() => Popped += (_, e) => ReportPopped?.Invoke(e.Page);
}

internal sealed class NavigationStackHandler : Reactor.Hosting.IElementHandler<NavigationStackElement, ReactorNavigationPage>
{
    public ReactorNavigationPage Mount(HandlerContext<NavigationStackElement> context, NavigationStackElement element)
    {
        var page = new ReactorNavigationPage();
        var navigator = page.Navigator = new StackNavigator(page) { OnError = context.ReportError };
        page.ReportPopped = popped =>
        {
            if (!navigator.IsUserPop(popped, modal: false, out var depth)) return;
            navigator.NotePlatformPop(popped);
            context.Current.OnPopped?.Invoke(depth);
            // A pop the app did not accept is undone once state has settled (Update with the same element).
            context.RestoreAfterEvent();
        };
        page.Loaded += (_, _) => navigator.Run(); // Modals wait for a window.
        page.ModalPoppedHandler = (_, e) =>
        {
            navigator.ForgetModal(e.Modal);
            if (!navigator.IsUserPop(e.Modal, modal: true, out var remaining))
            {
                // Possibly another stack's modal that was blocking this one's.
                navigator.Run();
                return;
            }
            navigator.NotePlatformPop(e.Modal);
            context.Current.OnModalDismissed?.Invoke(remaining);
            context.RestoreAfterEvent();
        };
        if (Application.Current is { } app) app.ModalPopped += page.ModalPoppedHandler;
        Apply(page, null, element);
        return page;
    }

    public void Unmount(ReactorNavigationPage control)
    {
        if (Application.Current is { } app && control.ModalPoppedHandler is { } handler) app.ModalPopped -= handler;
        control.ReportPopped = null;
        control.Navigator.Stop();
    }

    public void Update(HandlerContext<NavigationStackElement> context, NavigationStackElement oldElement, NavigationStackElement newElement, ReactorNavigationPage control)
    {
        Apply(control, oldElement, newElement);
        // The same element is a restore after a pop the app did not accept (or an unchanged
        // re-render): reconcile against the current declaration, which pushes the page back. A
        // changed element must not run here: its children are synced after this, and running now
        // would reconcile against the previous declaration (re-pushing a page the app just let go).
        if (ReferenceEquals(oldElement, newElement)) control.Navigator.Run();
    }

    private static void Apply(NavigationPage page, NavigationStackElement? previous, NavigationStackElement next)
    {
        Props.SetRef(page, Page.TitleProperty, previous?.Title, next.Title);
        Props.SetImage(page, Page.IconImageSourceProperty, previous?.Icon, next.Icon, previous is null);
        Props.SetRef(page, NavigationPage.BarBackgroundColorProperty, previous?.BarBackgroundColor, next.BarBackgroundColor);
        Props.SetRef(page, NavigationPage.BarTextColorProperty, previous?.BarTextColor, next.BarTextColor);
    }

    public IChildrenStrategy<NavigationStackElement, ReactorNavigationPage> Children { get; } = new StackChildren();

    private sealed class StackChildren : ISlottedChildrenStrategy<NavigationStackElement, ReactorNavigationPage>
    {
        public IReadOnlyList<Element?> GetChildren(NavigationStackElement element) => [.. element.Pages, .. element.Modals];

        public void SyncSlots(NavigationStackElement element, ReactorNavigationPage control, IReadOnlyList<IReadOnlyList<object>> slots)
        {
            var pages = slots.Take(element.Pages.Length).SelectMany(static s => s).Select(AsPage).ToList();
            var modals = slots.Skip(element.Pages.Length).SelectMany(static s => s).Select(AsPage).ToList();
            control.Navigator.SetDesired(pages, modals);
        }
    }

    internal static Page AsPage(object native) => native switch
    {
        Page page => page,
        _ => throw new InvalidOperationException(
            $"A navigation stack, tabs or flyout holds pages, but a child rendered a {native.GetType().Name}. Wrap it in Page(…)."),
    };
}

internal sealed class ReactorTabbedPage : TabbedPage
{
    public bool Syncing;
}

internal sealed class TabsHandler : Reactor.Hosting.IElementHandler<TabsElement, ReactorTabbedPage>
{
    public ReactorTabbedPage Mount(HandlerContext<TabsElement> context, TabsElement element)
    {
        var tabs = new ReactorTabbedPage();
        tabs.CurrentPageChanged += (_, _) =>
        {
            if (tabs.Syncing || context.IsEchoing(tabs)) return;
            context.Current.OnSelectedIndexChanged?.Invoke(tabs.Children.IndexOf(tabs.CurrentPage));
            context.RestoreAfterEvent();
        };
        Apply(tabs, null, element);
        return tabs;
    }

    public void Update(HandlerContext<TabsElement> context, TabsElement oldElement, TabsElement newElement, ReactorTabbedPage control)
    {
        Apply(control, oldElement, newElement);
        Select(control, newElement.SelectedIndex);
    }

    private static void Apply(TabbedPage tabs, TabsElement? previous, TabsElement next)
    {
        Props.SetRef(tabs, TabbedPage.BarBackgroundColorProperty, previous?.BarBackgroundColor, next.BarBackgroundColor);
        Props.SetRef(tabs, TabbedPage.SelectedTabColorProperty, previous?.SelectedTabColor, next.SelectedTabColor);
        Props.SetRef(tabs, TabbedPage.UnselectedTabColorProperty, previous?.UnselectedTabColor, next.UnselectedTabColor);
    }

    internal static void Select(ReactorTabbedPage tabs, int index)
    {
        if (index < 0 || index >= tabs.Children.Count || ReferenceEquals(tabs.CurrentPage, tabs.Children[index])) return;
        tabs.Syncing = true;
        try
        {
            tabs.CurrentPage = tabs.Children[index];
        }
        finally
        {
            tabs.Syncing = false;
        }
    }

    public IChildrenStrategy<TabsElement, ReactorTabbedPage> Children { get; } = new TabChildren();

    // Slotted only to receive the element: the selection is applied after the tabs exist.
    private sealed class TabChildren : ISlottedChildrenStrategy<TabsElement, ReactorTabbedPage>
    {
        public IReadOnlyList<Element?> GetChildren(TabsElement element) => element.Pages;

        public void SyncSlots(TabsElement element, ReactorTabbedPage control, IReadOnlyList<IReadOnlyList<object>> slots)
        {
            control.Syncing = true;
            try
            {
                ChildrenStrategy.SyncList(control.Children, [.. slots.SelectMany(static s => s).Select(NavigationStackHandler.AsPage)]);
            }
            finally
            {
                control.Syncing = false;
            }
            Select(control, element.SelectedIndex);
        }
    }
}

internal sealed class ReactorFlyoutPage : FlyoutPage
{
    public bool Syncing;
}

internal sealed class FlyoutHandler : Reactor.Hosting.IElementHandler<FlyoutElement, ReactorFlyoutPage>
{
    public ReactorFlyoutPage Mount(HandlerContext<FlyoutElement> context, FlyoutElement element)
    {
        var flyout = new ReactorFlyoutPage();
        flyout.IsPresentedChanged += (_, _) =>
        {
            if (flyout.Syncing || context.IsEchoing(flyout)) return;
            context.Current.OnIsPresentedChanged?.Invoke(flyout.IsPresented);
            context.RestoreAfterEvent();
        };
        Props.Set(flyout, FlyoutPage.FlyoutLayoutBehaviorProperty, null, element.LayoutBehavior);
        return flyout;
    }

    public void Update(HandlerContext<FlyoutElement> context, FlyoutElement oldElement, FlyoutElement newElement, ReactorFlyoutPage control)
    {
        Props.Set(control, FlyoutPage.FlyoutLayoutBehaviorProperty, oldElement.LayoutBehavior, newElement.LayoutBehavior);
        if (control.Flyout is not null && control.IsPresented != newElement.IsPresented)
            context.WriteSuppressed(control, () => control.IsPresented = newElement.IsPresented);
    }

    public IChildrenStrategy<FlyoutElement, ReactorFlyoutPage> Children { get; } = new FlyoutChildren();

    private sealed class FlyoutChildren : ISlottedChildrenStrategy<FlyoutElement, ReactorFlyoutPage>
    {
        public IReadOnlyList<Element?> GetChildren(FlyoutElement element) => [element.Flyout, element.Detail];

        public void SyncSlots(FlyoutElement element, ReactorFlyoutPage control, IReadOnlyList<IReadOnlyList<object>> slots)
        {
            var flyout = slots[0].Select(NavigationStackHandler.AsPage).SingleOrDefault()
                ?? throw new InvalidOperationException("A flyout needs a flyout page.");
            var detail = slots[1].Select(NavigationStackHandler.AsPage).SingleOrDefault()
                ?? throw new InvalidOperationException("A flyout needs a detail page.");
            // MAUI requires the flyout page to have a title.
            flyout.Title ??= " ";
            control.Syncing = true;
            try
            {
                // A page moving to the other slot must leave its current one first: MAUI rejects a
                // page that still has a parent. Placeholders hold the slots for that moment.
                if (ReferenceEquals(control.Detail, flyout)) control.Detail = new ContentPage();
                if (ReferenceEquals(control.Flyout, detail)) control.Flyout = new ContentPage { Title = " " };
                if (!ReferenceEquals(control.Flyout, flyout)) control.Flyout = flyout;
                if (!ReferenceEquals(control.Detail, detail)) control.Detail = detail;
                if (control.IsPresented != element.IsPresented) control.IsPresented = element.IsPresented;
            }
            finally
            {
                control.Syncing = false;
            }
        }
    }
}
