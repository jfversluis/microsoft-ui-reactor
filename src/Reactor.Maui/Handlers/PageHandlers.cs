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
    private IReadOnlyList<Page> _pages = [];
    private IReadOnlyList<Page> _modals = [];
    private bool _running;

    /// <summary>True while the navigator itself is changing the stacks, so its own pops are not reported as the user's.</summary>
    public bool IsNavigating { get; private set; }

    public Action<Exception>? OnError { get; set; }

    public bool HasModal(Page page) => _modals.Contains(page);

    public void SetDesired(IReadOnlyList<Page> pages, IReadOnlyList<Page> modals)
    {
        _pages = pages;
        _modals = modals;
        Run();
    }

    public void Run()
    {
        if (_running) return;
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        _running = true;
        try
        {
            for (var step = 0; step < 1000; step++)
            {
                var operation = NextOperation();
                if (operation is null) return;
                IsNavigating = true;
                try
                {
                    await operation();
                }
                finally
                {
                    IsNavigating = false;
                }
            }
            throw new InvalidOperationException("The navigation stack did not settle.");
        }
        catch (Exception ex)
        {
            if (OnError is { } onError) onError(ex);
            else throw;
        }
        finally
        {
            _running = false;
        }
    }

    private Func<Task>? NextOperation()
    {
        var navigation = navigationPage.Navigation;
        var actual = navigation.NavigationStack;
        var desired = _pages;
        if (desired.Count == 0) return null; // A navigation page cannot be empty; keep what is shown.

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
        var actual = navigation.ModalStack;
        var common = 0;
        while (common < actual.Count && common < _modals.Count && ReferenceEquals(actual[common], _modals[common])) common++;
        if (common < actual.Count) return () => navigation.PopModalAsync(animated: true);
        if (common < _modals.Count) return () => navigation.PushModalAsync(_modals[common], animated: true);
        return null;
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
    public Action? ReportPopped;

    public ReactorNavigationPage() => Popped += (_, _) => ReportPopped?.Invoke();
}

internal sealed class NavigationStackHandler : Reactor.Hosting.IElementHandler<NavigationStackElement, ReactorNavigationPage>
{
    public ReactorNavigationPage Mount(HandlerContext<NavigationStackElement> context, NavigationStackElement element)
    {
        var page = new ReactorNavigationPage();
        page.Navigator = new StackNavigator(page);
        page.ReportPopped = () =>
        {
            if (page.Navigator.IsNavigating) return;
            context.Current.OnPopped?.Invoke(page.Navigation.NavigationStack.Count);
            context.RestoreAfterEvent();
        };
        page.Loaded += (_, _) => page.Navigator.Run(); // Modals wait for a window.
        page.ModalPoppedHandler = (_, e) =>
        {
            if (page.Navigator.IsNavigating || !page.Navigator.HasModal(e.Modal)) return;
            context.Current.OnModalDismissed?.Invoke(page.Navigation.ModalStack.Count);
            context.RestoreAfterEvent();
        };
        if (Application.Current is { } app) app.ModalPopped += page.ModalPoppedHandler;
        Apply(page, null, element);
        return page;
    }

    public void Unmount(ReactorNavigationPage control)
    {
        if (Application.Current is { } app && control.ModalPoppedHandler is { } handler) app.ModalPopped -= handler;
    }

    public void Update(HandlerContext<NavigationStackElement> context, NavigationStackElement oldElement, NavigationStackElement newElement, ReactorNavigationPage control)
    {
        Apply(control, oldElement, newElement);
        // Restoring after a back gesture the app did not accept re-runs the navigator, which pushes the page back.
        control.Navigator.Run();
    }

    private static void Apply(NavigationPage page, NavigationStackElement? previous, NavigationStackElement next)
    {
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
