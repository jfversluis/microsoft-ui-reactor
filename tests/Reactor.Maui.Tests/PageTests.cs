using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;

namespace Microsoft.UI.Reactor.Maui.Tests;

public sealed class StackApp : Component<Action<RouteStack<string>>>
{
    public override Element Render()
    {
        var stack = UseRouteStack("home");
        Props(stack);
        return NavigationStack(stack.Items.Select(route => Page(route, Label(route)).WithKey(route)), stack.PopTo);
    }
}

public sealed class TabsApp : Component<List<int>>
{
    public override Element Render() =>
        Tabs(1, Props.Add, Page("A", Label("a")), Page("B", Label("b")), Page("C", Label("c")));
}

public class PageTests
{
    private static TestHost Host() => new(MauiHandlers.CreateRegistry());

    [Fact]
    public void Page_AppliesTitleContentAndToolbar_AndToolbarItemsCallTheCurrentCallback()
    {
        var clicks = new List<string>();
        var host = Host().Render(Page("One", Label("body")) with { ToolbarItems = [new ToolbarAction("Save", () => clicks.Add("first"))] });
        var page = host.View<ContentPage>();
        Assert.Equal("One", page.Title);
        Assert.Equal("body", Assert.IsType<Label>(page.Content).Text);
        var item = Assert.Single(page.ToolbarItems);

        host.Render(Page("Two", Label("body")) with { ToolbarItems = [new ToolbarAction("Save", () => clicks.Add("second"))] });
        Assert.Same(item, Assert.Single(page.ToolbarItems));
        ((IMenuItemController)item).Activate();

        Assert.Equal("Two", page.Title);
        Assert.Equal(["second"], clicks);
    }

    [Fact]
    public void APageInsideAPage_IsRejectedWithGuidance()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Host().Render(Page("outer", Page("inner", null))));
        Assert.Contains("NavigationStack", error.Message);
    }

    [Fact]
    public void NavigationStack_FollowsTheDeclaredStack()
    {
        RouteStack<string>? stack = null;
        var host = Host().Render(Component<StackApp, Action<RouteStack<string>>>(s => stack = s));
        var navigation = host.View<NavigationPage>();
        Settle(host, () => navigation.Navigation.NavigationStack.Count == 1);
        Assert.Equal(["home"], navigation.Navigation.NavigationStack.Select(p => p.Title));

        stack!.Push("detail");
        stack.Push("more");
        Settle(host, () => navigation.Navigation.NavigationStack.Count == 3);
        Assert.Equal(["home", "detail", "more"], navigation.Navigation.NavigationStack.Select(p => p.Title));

        stack!.PopToRoot();
        Settle(host, () => navigation.Navigation.NavigationStack.Count == 1);
        Assert.Equal(["home"], navigation.Navigation.NavigationStack.Select(p => p.Title));
    }

    [Fact]
    public void ABackGesture_IsReportedAndAccepted()
    {
        RouteStack<string>? stack = null;
        var host = Host().Render(Component<StackApp, Action<RouteStack<string>>>(s => stack = s));
        var navigation = host.View<NavigationPage>();
        stack!.Push("detail");
        Settle(host, () => navigation.Navigation.NavigationStack.Count == 2);

        // What the platform does for the back button. Without a platform handler MAUI does not raise
        // Popped, so the test raises the handler's report itself.
        navigation.PopAsync();
        ((ReactorNavigationPage)navigation).ReportPopped!();
        Settle(host, () => stack!.Count == 1);

        Assert.Equal(["home"], stack!.Items);
        Assert.Equal(["home"], navigation.Navigation.NavigationStack.Select(p => p.Title));
    }

    [Fact]
    public void ABackGestureTheAppDoesNotAccept_PushesThePageBack()
    {
        var host = Host().Render(NavigationStack([Page("home", null).WithKey("home"), Page("detail", null).WithKey("detail")]));
        var navigation = host.View<ReactorNavigationPage>();
        Settle(host, () => navigation.Navigation.NavigationStack.Count == 2);

        navigation.PopAsync();
        navigation.ReportPopped!();
        Settle(host, () => navigation.Navigation.NavigationStack.Count == 2);

        Assert.Equal(["home", "detail"], navigation.Navigation.NavigationStack.Select(p => p.Title));
    }

    [Fact]
    public void Tabs_SelectAfterTheTabsExist_AndReturnToTheDeclaredTabWhenNotAccepted()
    {
        var reported = new List<int>();
        var host = Host().Render(Component<TabsApp, List<int>>(reported));
        var tabs = host.View<TabbedPage>();
        Assert.Equal("B", tabs.CurrentPage.Title);

        tabs.CurrentPage = tabs.Children[2];
        host.Scheduler.Drain();

        Assert.Equal([2], reported);
        Assert.Equal("B", tabs.CurrentPage.Title);
    }

    [Fact]
    public void Flyout_AssignsBothPages_AndGivesTheFlyoutATitle()
    {
        var host = Host().Render(Flyout(Page(null, Label("menu")), Page("Detail", Label("detail")), isPresented: false));
        var flyout = host.View<FlyoutPage>();

        Assert.NotNull(flyout.Flyout.Title);
        Assert.Equal("Detail", flyout.Detail.Title);
        Assert.False(flyout.IsPresented);
    }

    // Navigation can complete asynchronously; posted render work is drained on the test (render) thread meanwhile.
    private static void Settle(TestHost host, Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++)
        {
            host.Scheduler.Drain();
            if (!done()) Thread.Sleep(5);
        }
        host.Scheduler.Drain();
    }
}
