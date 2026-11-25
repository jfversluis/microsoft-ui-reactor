using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Handlers;
using Microsoft.UI.Reactor.Navigation;
using static Microsoft.UI.Reactor.Maui.Factories;

namespace Microsoft.UI.Reactor.Maui.Tests;

public sealed record NavAppProps(
    Action<NavigationHandle<string>> Report,
    Func<string, Element?> Map,
    Func<NavigationStackElement, NavigationStackElement>? Stack = null,
    Action? Rendered = null,
    int Generation = 0);

public sealed class NavApp : Component<NavAppProps>
{
    public override Element Render()
    {
        var nav = UseNavigation("home");
        Props.Report(nav);
        Props.Rendered?.Invoke();
        return NavigationHost(nav, Props.Map, Props.Stack);
    }
}

/// <summary>What the lifecycle pages report to, and which of them refuse a navigation.</summary>
public sealed class NavLog
{
    public List<string> Entries { get; } = [];
    public HashSet<string> RefuseFrom { get; } = [];
    public HashSet<string> RefuseTo { get; } = [];
    public Dictionary<string, Action<NavigatingToContext>> OnNavigatingTo { get; } = [];
    public Dictionary<string, int> Mounts { get; } = [];

    /// <summary>Callbacks that throw, as "route:phase" (for example "detail:navigatedTo").</summary>
    public HashSet<string> Throw { get; } = [];

    public void Note(string route, string phase, NavigationMode mode)
    {
        Entries.Add($"{route}:{phase}:{mode}");
        if (Throw.Contains($"{route}:{phase}")) throw new InvalidOperationException($"boom {route}:{phase}");
    }
}

public sealed record LogPageProps(string Route, NavLog Log);

public sealed class LogPage : Component<LogPageProps>
{
    public override Element Render()
    {
        var (route, log) = (Props.Route, Props.Log);
        UseNavigationLifecycle(
            onNavigatingTo: c =>
            {
                log.Note(route, "navigatingTo", c.Mode);
                if (log.RefuseTo.Contains(route)) c.Cancel();
                if (log.OnNavigatingTo.TryGetValue(route, out var then)) then(c);
            },
            onNavigatedTo: c => log.Note(route, "navigatedTo", c.Mode),
            onNavigatingFrom: c =>
            {
                log.Note(route, "navigatingFrom", c.Mode);
                if (log.RefuseFrom.Contains(route)) c.Cancel();
            },
            onNavigatedFrom: c => log.Note(route, "navigatedFrom", c.Mode));
        UseEffect(() => { log.Mounts[route] = log.Mounts.GetValueOrDefault(route) + 1; }, Array.Empty<object>());
        return Page(route, Label(route));
    }
}

/// <summary>A route whose string and hash code don't tell two routes apart; only <see cref="Id"/> does.</summary>
public sealed class OpaqueRoute(int id)
{
    public int Id { get; } = id;
    public override bool Equals(object? obj) => obj is OpaqueRoute other && other.Id == Id;
    public override int GetHashCode() => 0;
    public override string ToString() => "route";
}

public sealed class OpaqueApp : Component<Action<NavigationHandle<OpaqueRoute>>>
{
    public override Element Render()
    {
        var nav = UseNavigation(new OpaqueRoute(0));
        Props(nav);
        return NavigationHost(nav, route => Page($"page {route.Id}", null));
    }
}

public abstract record ReadmeRoute
{
    public sealed record Home : ReadmeRoute;
    public sealed record Detail(int Id) : ReadmeRoute;
}

// The README's NavigationHost snippet.
public sealed class ReadmeApp : Component
{
    public override Element Render()
    {
        var nav = UseNavigation<ReadmeRoute>(new ReadmeRoute.Home());
        return NavigationHost(nav, route => route switch
        {
            ReadmeRoute.Detail d => Page($"Item {d.Id}", Label($"Item {d.Id}")),
            _ => Page("Home", Button("Open", () => nav.Navigate(new ReadmeRoute.Detail(1)))),
        });
    }
}

/// <summary>A page that replaces its own native page (a new key) when its state changes.</summary>
public sealed record SwapPageProps(string Route, Dictionary<string, Action<int>> Setters);

public sealed class SwapPage : Component<SwapPageProps>
{
    public override Element Render()
    {
        var (version, setVersion) = UseState(0);
        Props.Setters[Props.Route] = setVersion;
        return Page($"{Props.Route} {version}", null).WithKey($"v{version}");
    }
}

/// <summary>A page holding a flyout whose detail is a second NavigationHost.</summary>
public sealed record NestedProps(NavLog Log, Action<NavigationHandle<string>> Report);

public sealed class NestedPage : Component<NestedProps>
{
    public override Element Render()
    {
        var inner = UseNavigation("i0");
        Props.Report(inner);
        return Flyout(
            Component<LogPage, LogPageProps>(new("menu", Props.Log)),
            NavigationHost(inner, route => Component<LogPage, LogPageProps>(new(route, Props.Log))),
            isPresented: false);
    }
}

/// <summary>Renders a NavigationHost over one of two handles.</summary>
public sealed record TwoHandlesProps(NavLog Log, Action<NavigationHandle<string>, NavigationHandle<string>, Action<bool>> Report);

public sealed class TwoHandlesApp : Component<TwoHandlesProps>
{
    public override Element Render()
    {
        var first = UseNavigation("home");
        var second = UseNavigation("other");
        var (useSecond, setUseSecond) = UseState(false);
        Props.Report(first, second, setUseSecond);
        return NavigationHost(useSecond ? second : first, route => Component<LogPage, LogPageProps>(new(route, Props.Log)));
    }
}

public class NavigationHostTests
{
    [Fact]
    public void TheReadmeSnippet_OpensItsDetailPage()
    {
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Component<ReadmeApp>());
        var page = host.View<ReactorNavigationPage>();
        ((IButtonController)Assert.IsType<Button>(((ContentPage)page.CurrentPage).Content)).SendClicked();
        host.Scheduler.Drain();

        Assert.Equal(["Home", "Item 1"], page.Navigation.NavigationStack.Select(p => p.Title));
    }

    private sealed class Harness
    {
        public TestHost Host { get; } = new(MauiHandlers.CreateRegistry());
        public NavLog Log { get; } = new();
        public NavigationHandle<string> Nav = null!;

        public List<Exception> Errors { get; } = [];
        public int AppRenders;

        public Harness(
            Func<string, NavLog, Element?>? map = null,
            Func<NavigationStackElement, NavigationStackElement>? stack = null,
            bool reportErrors = true)
        {
            if (reportErrors) Host.Root.UnhandledException += Errors.Add;
            _map = map ?? (static (route, log) => Component<LogPage, LogPageProps>(new(route, log)));
            _stack = stack;
            RenderAgain();
            Settle();
        }

        private readonly Func<string, NavLog, Element?> _map;
        private readonly Func<NavigationStackElement, NavigationStackElement>? _stack;

        /// <summary>Renders the root again, synchronously, with new props, so the host maps its routes again.</summary>
        public void RenderAgain()
        {
            // Delegates over the same method and target compare equal; a closure per call makes the
            // route map, and so the host's props, differ.
            var generation = ++_generation;
            Host.Render(Component<NavApp, NavAppProps>(new(
                nav => Nav = nav, route => generation > 0 ? _map(route, Log) : null, _stack, () => AppRenders++, generation)));
        }

        private int _generation;

        public ReactorNavigationPage Page => Host.View<ReactorNavigationPage>();
        public IReadOnlyList<Page> Stack => Page.Navigation.NavigationStack;
        public string Titles => string.Join(",", Stack.Select(p => p.Title));

        /// <summary>Drains posted work until the navigator is idle and nothing more is posted.</summary>
        public void Settle()
        {
            for (var i = 0; i < 200; i++)
            {
                Host.Scheduler.Drain();
                if (Host.RootView is not ReactorNavigationPage page || page.Navigator.IsIdle) break;
                Thread.Sleep(5);
            }
            Host.Scheduler.Drain();
        }

        public string TakeLog()
        {
            var text = string.Join(" ", Log.Entries);
            Log.Entries.Clear();
            return text;
        }

        /// <summary>What the platform does for the iOS back button or gesture: pop, then report it.</summary>
        public void UserBack()
        {
            var popped = Page.CurrentPage;
            Page.PopAsync();
            Page.ReportPopped!(popped);
            Settle();
        }
    }

    [Fact]
    public void TheHandle_PushesPopsReplacesResetsAndPopsTo_OnThePlatformStack()
    {
        var h = new Harness();
        var events = new List<string>();
        h.Nav.Navigated += e => events.Add($"{e.Mode}:{e.PreviousRoute}>{e.Route}");
        Assert.Equal("home", h.Titles);

        Assert.True(h.Nav.Navigate("a"));
        Assert.True(h.Nav.Navigate("b"));
        h.Settle();
        Assert.Equal("home,a,b", h.Titles);
        Assert.Equal(["home", "a"], h.Nav.BackStack);

        Assert.True(h.Nav.GoBack());
        h.Settle();
        Assert.Equal("home,a", h.Titles);
        Assert.True(h.Nav.CanGoForward);

        Assert.True(h.Nav.GoForward());
        Assert.True(h.Nav.Replace("c"));
        h.Settle();
        Assert.Equal("home,a,c", h.Titles);

        Assert.True(h.Nav.PopTo(r => r == "home"));
        h.Settle();
        Assert.Equal("home", h.Titles);

        Assert.True(h.Nav.Navigate("d", new NavigateOptions { PushToBackStack = false }));
        Assert.True(h.Nav.Reset("e"));
        h.Settle();
        Assert.Equal("e", h.Titles);
        Assert.Equal(1, h.Nav.Depth);
        Assert.Equal(
            ["Push:home>a", "Push:a>b", "Pop:b>a", "Forward:a>b", "Replace:b>c", "Pop:c>home", "Replace:home>d", "Reset:d>e"],
            events);
    }

    [Fact]
    public void PushAndPop_KeepThePagesBelowMounted()
    {
        var h = new Harness();
        var home = h.Stack[0];

        h.Nav.Navigate("detail");
        h.Settle();
        Assert.Same(home, h.Stack[0]);
        h.Nav.GoBack();
        h.Settle();

        Assert.Same(home, Assert.Single(h.Stack));
        Assert.Equal(1, h.Log.Mounts["home"]);
    }

    [Fact]
    public void EqualRoutesAtTwoPositions_AreTwoPages()
    {
        var h = new Harness();
        h.Nav.Navigate("home");
        h.Settle();

        Assert.Equal("home,home", h.Titles);
        Assert.NotSame(h.Stack[0], h.Stack[1]);
        Assert.Equal(2, h.Log.Mounts["home"]);
    }

    [Fact]
    public void Replace_RemountsTheCurrentPage_UnlessTheRouteIsEqual()
    {
        var h = new Harness();
        h.Nav.Navigate("a");
        h.Settle();
        var (home, a) = (h.Stack[0], h.Stack[1]);

        h.Nav.Replace("a");
        h.Settle();
        Assert.Same(a, h.Stack[1]);

        h.Nav.Replace("b");
        h.Settle();
        Assert.Equal("home,b", h.Titles);
        Assert.Same(home, h.Stack[0]);
        Assert.NotSame(a, h.Stack[1]);
    }

    [Fact]
    public void Keys_FollowRouteEquality_NotItsStringOrHash()
    {
        NavigationHandle<OpaqueRoute>? nav = null;
        var host = new TestHost(MauiHandlers.CreateRegistry());
        host.Render(Component<OpaqueApp, Action<NavigationHandle<OpaqueRoute>>>(n => nav = n));
        host.Scheduler.Drain();
        var page = host.View<ReactorNavigationPage>();
        var first = page.Navigation.NavigationStack[0];

        nav!.Replace(new OpaqueRoute(0));
        host.Scheduler.Drain();
        Assert.Same(first, page.Navigation.NavigationStack[0]);

        nav.Replace(new OpaqueRoute(1));
        host.Scheduler.Drain();
        var second = Assert.Single(page.Navigation.NavigationStack);
        Assert.NotSame(first, second);
        Assert.Equal("page 1", second.Title);
    }

    [Fact]
    public void ARefusingGuard_CancelsTheNavigation_AndNothingChanges()
    {
        var h = new Harness();
        h.Log.RefuseFrom.Add("home");

        Assert.False(h.Nav.Navigate("detail"));
        h.Settle();

        Assert.Equal("home", h.Nav.CurrentRoute);
        Assert.Equal("home", h.Titles);
        Assert.Equal("home:navigatingFrom:Push", h.TakeLog());
    }

    [Fact]
    public void Lifecycle_RunsInWinUIsOrder_ForPushPopAndReplace()
    {
        var h = new Harness();
        h.Nav.Navigate("detail");
        h.Settle();
        Assert.Equal("home:navigatingFrom:Push detail:navigatingTo:Push detail:navigatedTo:Push home:navigatedFrom:Push", h.TakeLog());

        h.Nav.Replace("other");
        h.Settle();
        Assert.Equal("detail:navigatingFrom:Replace other:navigatingTo:Replace other:navigatedTo:Replace detail:navigatedFrom:Replace", h.TakeLog());

        h.Nav.GoBack();
        h.Settle();
        Assert.Equal("other:navigatingFrom:Pop home:navigatingTo:Pop home:navigatedTo:Pop other:navigatedFrom:Pop", h.TakeLog());
    }

    [Fact]
    public void ADestinationThatRefuses_IsUnmounted_AndThePreviousPageStays()
    {
        var h = new Harness();
        h.Log.RefuseTo.Add("detail");
        h.Nav.Navigate("detail");
        h.Settle();

        Assert.Equal("home", h.Titles);
        Assert.Equal("home:navigatingFrom:Push detail:navigatingTo:Push", h.TakeLog());
        // As on WinUI, the model keeps the route; the host keeps showing the previous page.
        Assert.Equal("detail", h.Nav.CurrentRoute);
    }

    [Fact]
    public void AReplacementThatRefuses_LeavesThePreviousPageMounted()
    {
        var h = new Harness();
        h.Nav.Navigate("a");
        h.Settle();
        var a = h.Stack[1];
        h.TakeLog();

        h.Log.RefuseTo.Add("b");
        h.Nav.Replace("b");
        h.Settle();

        Assert.Same(a, h.Stack[1]);
        Assert.Equal("home,a", h.Titles);
        Assert.Equal(1, h.Log.Mounts["a"]);
        Assert.Equal("a:navigatingFrom:Replace b:navigatingTo:Replace", h.TakeLog());
    }

    [Fact]
    public void APopWhoseDestinationRefuses_KeepsThePageShown()
    {
        var h = new Harness();
        h.Nav.Navigate("detail");
        h.Settle();
        var detail = h.Stack[1];
        h.TakeLog();

        h.Log.RefuseTo.Add("home");
        h.Nav.GoBack();
        h.Settle();

        Assert.Equal("home,detail", h.Titles);
        Assert.Same(detail, h.Stack[1]);
        Assert.Equal("detail:navigatingFrom:Pop home:navigatingTo:Pop", h.TakeLog());
    }

    [Fact]
    public void ASupersededNavigation_StillCompletesTheDeparture_ButOnlyTheLatestDestinationArrives()
    {
        var h = new Harness();
        h.Nav.Navigate("detail");
        h.Settle();
        h.TakeLog();

        // Going back home redirects at once: the pop is superseded before it completes.
        h.Log.OnNavigatingTo["home"] = c =>
        {
            if (c.Mode == NavigationMode.Pop) h.Nav.Navigate("redirect");
        };
        h.Nav.GoBack();
        h.Settle();

        Assert.Equal("home,redirect", h.Titles);
        Assert.Equal(
            "detail:navigatingFrom:Pop home:navigatingTo:Pop detail:navigatingFrom:Push redirect:navigatingTo:Push "
            + "detail:navigatedFrom:Pop redirect:navigatedTo:Push home:navigatedFrom:Push",
            h.TakeLog());
    }

    [Fact]
    public void AUserBack_IsOneGuardedNavigation_ThatTheAppCanAccept()
    {
        var h = new Harness();
        h.Nav.Navigate("a");
        h.Nav.Navigate("b");
        h.Settle();
        h.TakeLog();

        h.UserBack();

        Assert.Equal("a", h.Nav.CurrentRoute);
        Assert.Equal(["b"], h.Nav.ForwardStack);
        Assert.Equal("home,a", h.Titles);
        Assert.Equal("b:navigatingFrom:Pop a:navigatingTo:Pop a:navigatedTo:Pop b:navigatedFrom:Pop", h.TakeLog());
    }

    [Fact]
    public void AUserBackTheGuardRefuses_PushesThePageBack()
    {
        var h = new Harness();
        h.Nav.Navigate("detail");
        h.Settle();
        var detail = h.Stack[1];
        h.TakeLog();
        h.Log.RefuseFrom.Add("detail");

        h.UserBack();

        Assert.Equal("detail", h.Nav.CurrentRoute);
        Assert.Equal("home,detail", h.Titles);
        Assert.Same(detail, h.Stack[1]);
        Assert.Equal("detail:navigatingFrom:Pop", h.TakeLog());
    }

    [Fact]
    public void TheBackKey_RunsTheGuardOnce_AndIsConsumed_UnlessAtTheRoot()
    {
        var h = new Harness();
        Assert.False(h.Page.SendBackButtonPressed());

        h.Nav.Navigate("detail");
        h.Settle();
        h.TakeLog();
        h.Log.RefuseFrom.Add("detail");
        Assert.True(h.Page.SendBackButtonPressed());
        h.Settle();
        Assert.Equal("detail:navigatingFrom:Pop", h.TakeLog());
        Assert.Equal("home,detail", h.Titles);

        h.Log.RefuseFrom.Clear();
        Assert.True(h.Page.SendBackButtonPressed());
        h.Settle();
        Assert.Equal("home", h.Titles);
        Assert.Equal("detail:navigatingFrom:Pop home:navigatingTo:Pop home:navigatedTo:Pop detail:navigatedFrom:Pop", h.TakeLog());
    }

    [Fact]
    public void AnimatedFalse_ReachesTheNavigator_ForThatNavigationOnly()
    {
        var h = new Harness();
        h.Nav.Navigate("a", new NavigateOptions { Animated = false });
        h.Settle();
        Assert.False(h.Page.Navigator.LastOperationAnimated);
        Assert.Equal("home,a", h.Titles);

        h.Nav.Navigate("b");
        h.Settle();
        Assert.True(h.Page.Navigator.LastOperationAnimated);
        h.Nav.GoBack();
        h.Settle();
        Assert.True(h.Page.Navigator.LastOperationAnimated);
    }

    [Fact]
    public void AHostConfiguredNotAnimated_UsesThatForEveryChange_UnlessNavigateSaysOtherwise()
    {
        var h = new Harness(stack: static s => s with { Animated = false });
        h.Nav.Navigate("a");
        h.Settle();
        Assert.Equal("home,a", h.Titles);
        Assert.False(h.Page.Navigator.LastOperationAnimated);

        h.Nav.GoBack();
        h.Settle();
        Assert.Equal("home", h.Titles);
        Assert.False(h.Page.Navigator.LastOperationAnimated);

        h.Nav.Navigate("b", new NavigateOptions { Animated = true });
        h.Settle();
        Assert.True(h.Page.Navigator.LastOperationAnimated);

        h.Nav.Reset("home");
        h.Settle();
        Assert.Equal("home", h.Titles);
        Assert.False(h.Page.Navigator.LastOperationAnimated);
    }

    [Fact]
    public void ANavigationsAnimationOption_EndsWithIt()
    {
        var setters = new Dictionary<string, Action<int>>();
        var h = new Harness(map: (route, _) => Component<SwapPage, SwapPageProps>(new(route, setters)));
        h.Nav.Navigate("a", new NavigateOptions { Animated = false });
        h.Settle();
        Assert.False(h.Page.Navigator.LastOperationAnimated);

        // A later change that isn't a navigation: the page replaces its native page.
        setters["a"](1);
        h.Settle();

        Assert.Equal("home 0,a 1", h.Titles);
        Assert.True(h.Page.Navigator.LastOperationAnimated);
    }

    [Fact]
    public void AHostInTabs_ShowsTheConfiguredTabTitle()
    {
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(RenderEachTime(ctx =>
        {
            var nav = ctx.UseNavigation("home");
            return Tabs(0, null, NavigationHost(nav, route => Page(route, null), static s => s with { Title = "Inbox" }), Page("Other", null));
        }));
        host.Scheduler.Drain();

        var tabs = host.View<TabbedPage>();
        Assert.Equal(["Inbox", "Other"], tabs.Children.Select(p => p.Title));
        Assert.Equal("home", Assert.IsType<ReactorNavigationPage>(tabs.Children[0]).CurrentPage.Title);
    }

    [Fact]
    public void SetState_IntoAMountedHost_ShowsTheRestoredStack_WithoutGuards()
    {
        var h = new Harness();
        var home = h.Stack[0];
        h.Log.RefuseFrom.Add("home");

        h.Nav.SetState(new NavigationState<string>(["home", "a"], "b", ["c"]));
        h.Settle();

        Assert.Equal("home,a,b", h.Titles);
        Assert.Same(home, h.Stack[0]);
        Assert.Equal(["c"], h.Nav.ForwardStack);
        Assert.Equal("b:navigatingTo:Reset b:navigatedTo:Reset home:navigatedFrom:Reset", h.TakeLog());
    }

    [Fact]
    public void SetState_OffTheRenderThread_AppliesTheSnapshotAsItWasPassed()
    {
        var h = new Harness();
        List<string> back = ["home", "a"];
        List<string> forward = ["c"];

        OnAnotherThread(() =>
        {
            h.Nav.SetState(new NavigationState<string>(back, "b", forward));
            back.Add("late");
            forward.Clear();
            return true;
        });
        h.Settle();

        Assert.Equal(["home", "a"], h.Nav.BackStack);
        Assert.Equal(["c"], h.Nav.ForwardStack);
        Assert.Equal("home,a,b", h.Titles);
    }

    [Fact]
    public void ADescendant_ReadsTheHandleFromTheHost()
    {
        NavigationHandle<string>? fromChild = null;
        NavigationHandle<string>? root = null;
        var host = new TestHost(MauiHandlers.CreateRegistry());
        host.Render(Component<NavApp, NavAppProps>(new(n => root = n, _ => RenderEachTime(ctx =>
        {
            fromChild = ctx.UseNavigation<string>();
            return Page("page", null);
        }))));
        host.Scheduler.Drain();

        Assert.NotNull(fromChild);
        Assert.Same(root, fromChild);
    }

    [Fact]
    public void ChildModeWithoutAHost_Throws()
    {
        var host = new TestHost(MauiHandlers.CreateRegistry());
        var error = Assert.Throws<InvalidOperationException>(() => host.Render(RenderEachTime(ctx =>
        {
            ctx.UseNavigation<string>();
            return Label("x");
        })));
        Assert.Contains("NavigationHost", error.Message);
    }

    [Fact]
    public void OffThreadNavigation_RunsOnItsOwnRootsRenderThread()
    {
        var first = new Harness();
        var second = new Harness();

        Assert.True(OnAnotherThread(() => first.Nav.Navigate("a")));
        Assert.True(OnAnotherThread(() => second.Nav.Navigate("b")));
        // Scheduled, not run: each on its own root's queue.
        Assert.Equal("home", first.Nav.CurrentRoute);
        Assert.Equal("home", second.Nav.CurrentRoute);

        first.Settle();
        Assert.Equal("a", first.Nav.CurrentRoute);
        Assert.Equal("home,a", first.Titles);
        Assert.Equal("home", second.Nav.CurrentRoute);

        second.Settle();
        Assert.Equal("home,b", second.Titles);
    }

    [Fact]
    public void OffThreadNavigation_ThrowsWhenTheRenderThreadRefusesIt()
    {
        NavigationHandle<string>? nav = null;
        var host = new TestHost(MauiHandlers.CreateRegistry());
        host.Render(RenderEachTime(ctx =>
        {
            ctx.RenderThreadEnqueue = static _ => false;
            nav = ctx.UseNavigation("home");
            return NavigationHost(nav, route => Page(route, null));
        }));

        var error = Assert.Throws<InvalidOperationException>(() => OnAnotherThread(() => nav!.Navigate("a")));
        Assert.Contains("refused", error.Message);
        Assert.Equal("home", nav!.CurrentRoute);
    }

    [Fact]
    public void APushedPage_ThatReplacesItsOwnPage_ReachesTheNativeStack()
    {
        var setters = new Dictionary<string, Action<int>>();
        var h = new Harness(map: (route, _) => Component<SwapPage, SwapPageProps>(new(route, setters)));
        h.Nav.Navigate("detail");
        h.Settle();
        var first = h.Stack[1];

        setters["detail"](1);
        h.Settle();

        Assert.Equal("home 0,detail 1", h.Titles);
        Assert.NotSame(first, h.Stack[1]);
    }

    private static Func<string, NavLog, Element?> WithBoundaries =>
        static (route, log) => ErrorBoundary(
            Component<LogPage, LogPageProps>(new(route, log)),
            error => Page($"fallback {error.Message}", null));

    [Fact]
    public void AThrowingOnNavigatedTo_ReachesThePagesErrorBoundary()
    {
        var h = new Harness(map: WithBoundaries);
        h.Log.Throw.Add("detail:navigatedTo");
        h.Nav.Navigate("detail");
        h.Settle();

        Assert.Equal("home,fallback boom detail:navigatedTo", h.Titles);
        Assert.Empty(h.Errors);
    }

    [Fact]
    public void AThrowingOnNavigatedTo_WithoutABoundary_ReachesUnhandledException()
    {
        var h = new Harness();
        h.Log.Throw.Add("detail:navigatedTo");
        h.Nav.Navigate("detail");
        h.Settle();

        Assert.Equal("boom detail:navigatedTo", Assert.Single(h.Errors).Message);
    }

    [Fact]
    public void AThrowingOnNavigatedFrom_OfAPageThatLeft_ReachesTheNearestMountedBoundary()
    {
        var host = new TestHost(MauiHandlers.CreateRegistry());
        var log = new NavLog();
        NavigationHandle<string>? nav = null;
        host.Render(ErrorBoundary(
            Component<NavApp, NavAppProps>(new(n => nav = n, route => Component<LogPage, LogPageProps>(new(route, log)))),
            error => Page($"host fallback {error.Message}", null)));
        nav!.Navigate("detail");
        host.Scheduler.Drain();
        log.Throw.Add("detail:navigatedFrom");

        nav.GoBack();
        host.Scheduler.Drain();

        Assert.Equal("host fallback boom detail:navigatedFrom", host.View<ContentPage>().Title);
    }

    [Fact]
    public void AThrowingOnNavigatedFrom_OfAReplacedComponent_ReachesTheBoundaryThatSurvived()
    {
        var replaced = false;
        var h = new Harness(map: (route, log) => ErrorBoundary(
            Component<LogPage, LogPageProps>(new(route, log)).WithKey(route == "home" && replaced ? "v2" : "v1"),
            error => Page($"fallback {error.Message}", null)));
        h.Log.Throw.Add("home:navigatedFrom");
        h.Nav.Navigate("detail");
        h.Host.Scheduler.RunOne();

        // Before the push completes, home's component is replaced beneath its boundary.
        replaced = true;
        h.RenderAgain();
        Assert.Equal(2, h.Log.Mounts["home"]);
        h.Settle();

        Assert.Equal("fallback boom home:navigatedFrom,detail", h.Titles);
        Assert.Empty(h.Errors);
    }

    [Fact]
    public void AThrowingOnNavigatingFrom_CancelsTheNavigation_AndReachesThePagesBoundary()
    {
        var h = new Harness(map: WithBoundaries);
        h.Log.Throw.Add("home:navigatingFrom");

        Assert.False(h.Nav.Navigate("detail"));
        h.Settle();

        Assert.Equal("home", h.Nav.CurrentRoute);
        Assert.Equal("fallback boom home:navigatingFrom", h.Titles);
        Assert.Empty(h.Errors);
    }

    [Fact]
    public void AThrowingOnNavigatingTo_CancelsTheNavigation_AndIsReported()
    {
        var h = new Harness();
        h.Log.Throw.Add("detail:navigatingTo");
        h.Nav.Navigate("detail");
        h.Settle();

        Assert.Equal("boom detail:navigatingTo", Assert.Single(h.Errors).Message);
        Assert.DoesNotContain("detail:navigatedTo", h.TakeLog());
    }

    [Fact]
    public void ARefusedPopDestination_IsAskedAgain_WhenTheUserGoesBack()
    {
        var h = new Harness();
        h.Nav.Navigate("detail");
        h.Settle();
        h.TakeLog();
        h.Log.RefuseTo.Add("home");
        h.Nav.GoBack();
        h.Settle();
        Assert.Equal("home,detail", h.Titles);
        Assert.Equal("detail:navigatingFrom:Pop home:navigatingTo:Pop", h.TakeLog());

        h.Log.RefuseTo.Clear();
        h.UserBack();

        Assert.Equal("home", h.Titles);
        Assert.Equal("home:navigatingTo:Pop home:navigatedTo:Pop detail:navigatedFrom:Pop", h.TakeLog());
    }

    [Fact]
    public void ARefusedPopDestination_IsAskedAgain_ByTheBackKey()
    {
        var h = new Harness();
        h.Nav.Navigate("detail");
        h.Settle();
        h.Log.RefuseTo.Add("home");
        h.Nav.GoBack();
        h.Settle();
        h.TakeLog();

        Assert.True(h.Page.SendBackButtonPressed());
        h.Settle();
        Assert.Equal("home,detail", h.Titles);
        Assert.Equal("home:navigatingTo:Pop", h.TakeLog());

        h.Log.RefuseTo.Clear();
        Assert.True(h.Page.SendBackButtonPressed());
        h.Settle();
        Assert.Equal("home", h.Titles);
        Assert.Equal("home:navigatingTo:Pop home:navigatedTo:Pop detail:navigatedFrom:Pop", h.TakeLog());
    }

    [Fact]
    public void TheBackKey_RightAfterAPushIsShown_SupersedesIt()
    {
        var h = new Harness();
        h.Nav.Navigate("detail");
        h.Host.Scheduler.RunOne();
        Assert.Equal("home,detail", h.Titles);

        Assert.True(h.Page.SendBackButtonPressed());
        h.Settle();

        Assert.Equal("home", h.Titles);
        Assert.Equal(
            "home:navigatingFrom:Push detail:navigatingTo:Push detail:navigatingFrom:Pop home:navigatingTo:Pop "
            + "home:navigatedFrom:Push home:navigatedTo:Pop detail:navigatedFrom:Pop",
            h.TakeLog());
    }

    [Fact]
    public void TheBackKey_BeforeAPushIsShown_IsConsumed_AndSupersedesThePush()
    {
        var h = new Harness();
        var pushes = 0;
        h.Page.Pushed += (_, _) => pushes++;
        h.Nav.Navigate("detail");

        // The host still shows its root, but the model can go back: the key must not close the app.
        Assert.True(h.Page.SendBackButtonPressed());
        h.Settle();

        Assert.Equal("home", h.Nav.CurrentRoute);
        Assert.False(h.Nav.CanGoBack);
        Assert.Equal("home", h.Titles);
        Assert.Equal(0, pushes);
        Assert.Equal(0, h.Log.Mounts.GetValueOrDefault("detail"));
        Assert.Equal("home:navigatingFrom:Push home:navigatingFrom:Pop", h.TakeLog());
    }

    [Fact]
    public void TheBackKey_BeforeAPushIsShown_AboveTheRoot_SupersedesThePush()
    {
        var h = new Harness();
        h.Nav.Navigate("a");
        h.Settle();
        h.TakeLog();
        var pushes = 0;
        h.Page.Pushed += (_, _) => pushes++;
        h.Nav.Navigate("b");

        Assert.True(h.Page.SendBackButtonPressed());
        h.Settle();

        Assert.Equal("a", h.Nav.CurrentRoute);
        Assert.Equal(["home"], h.Nav.BackStack);
        Assert.Equal("home,a", h.Titles);
        Assert.Equal(0, pushes);
        Assert.Equal(0, h.Log.Mounts.GetValueOrDefault("b"));
        Assert.Equal("a:navigatingFrom:Push a:navigatingFrom:Pop", h.TakeLog());
    }

    [Fact]
    public void AUserPopOfSeveralLevels_IsOneGuardedNavigation()
    {
        var h = new Harness();
        h.Nav.Navigate("a");
        h.Nav.Navigate("b");
        h.Settle();
        h.TakeLog();
        var a = h.Stack[1];

        // What the iOS back button's menu does: back to the root, reporting the page above it.
        h.Page.PopToRootAsync();
        h.Page.ReportPopped!(a);
        h.Settle();

        Assert.Equal("home", h.Nav.CurrentRoute);
        Assert.Equal("home", h.Titles);
        Assert.Equal("b:navigatingFrom:Pop home:navigatingTo:Pop home:navigatedTo:Pop b:navigatedFrom:Pop", h.TakeLog());
    }

    [Fact]
    public void AUserPop_WhileTheModelHasMovedOn_IsRefused_AndTheHostShowsTheModel()
    {
        var h = new Harness();
        h.Nav.Navigate("detail");
        h.Settle();
        h.Nav.Navigate("x");
        h.TakeLog();

        h.UserBack();

        Assert.Equal("x", h.Nav.CurrentRoute);
        Assert.Equal("home,detail,x", h.Titles);
        Assert.DoesNotContain("navigatingFrom", h.TakeLog());
    }

    [Fact]
    public void ANestedHost_OwnsItsPages_ForTheGuardAndTheBackKey()
    {
        NavigationHandle<string>? inner = null;
        var h = new Harness(map: (route, log) => route == "nested"
            ? Component<NestedPage, NestedProps>(new(log, n => inner = n))
            : Component<LogPage, LogPageProps>(new(route, log)));
        h.Nav.Navigate("nested");
        h.Settle();
        inner!.Navigate("i1");
        h.Settle();
        h.Log.RefuseFrom.Add("i1");
        h.TakeLog();

        // The key goes to the inner stack's page first.
        Assert.True(h.Page.SendBackButtonPressed());
        h.Settle();
        Assert.Equal("i1:navigatingFrom:Pop", h.TakeLog());
        Assert.Equal("nested", h.Nav.CurrentRoute);
        Assert.Equal("i1", inner.CurrentRoute);

        // The outer guard asks the outer page's components, not the inner host's pages.
        Assert.True(h.Nav.Navigate("x"));
        h.Settle();
        Assert.Equal("menu:navigatingFrom:Push x:navigatingTo:Push x:navigatedTo:Push menu:navigatedFrom:Push", h.TakeLog());
    }

    [Fact]
    public void UnmountingTheHostMidPush_RunsNoLifecycleAfter_AndReleasesTheGuard()
    {
        var h = new Harness();
        h.Nav.Navigate("detail");
        h.Host.Scheduler.RunOne();
        Assert.Equal("home,detail", h.Titles);

        h.Host.Render(Page("gone", null));
        h.Settle();

        Assert.Equal("home:navigatingFrom:Push detail:navigatingTo:Push", h.TakeLog());
        Assert.Null(((INavigationHandle)h.Nav).LifecycleGuard);

        // The handle outlives its host: navigating it is harmless.
        Assert.True(h.Nav.Navigate("y"));
        h.Settle();
        Assert.Empty(h.TakeLog());
        Assert.Equal("gone", h.Host.View<ContentPage>().Title);
    }

    [Fact]
    public void AHostMovedToAnotherHandle_ShowsItsStack_AndReleasesTheFirst()
    {
        var host = new TestHost(MauiHandlers.CreateRegistry());
        var log = new NavLog();
        NavigationHandle<string>? first = null, second = null;
        Action<bool>? useSecond = null;
        host.Render(Component<TwoHandlesApp, TwoHandlesProps>(new(log, (a, b, set) => (first, second, useSecond) = (a, b, set))));
        first!.Navigate("a");
        host.Scheduler.Drain();
        log.Entries.Clear();
        var page = host.View<ReactorNavigationPage>();

        useSecond!(true);
        host.Scheduler.Drain();

        Assert.Equal(["other"], page.Navigation.NavigationStack.Select(p => p.Title));
        Assert.Equal("other:navigatingTo:Push other:navigatedTo:Push a:navigatedFrom:Push", string.Join(" ", log.Entries));
        Assert.Null(((INavigationHandle)first).LifecycleGuard);
        Assert.NotNull(((INavigationHandle)second!).LifecycleGuard);
        log.Entries.Clear();

        first.Navigate("z");
        second.Navigate("b");
        host.Scheduler.Drain();
        Assert.Equal(["other", "b"], page.Navigation.NavigationStack.Select(p => p.Title));
        Assert.Equal("other:navigatingFrom:Push b:navigatingTo:Push b:navigatedTo:Push other:navigatedFrom:Push", string.Join(" ", log.Entries));
    }

    private static T OnAnotherThread<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.Start();
        thread.Join();
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        return result;
    }

    [Fact]
    public void Mutators_OnTheRenderThread_DoNotAllocate()
    {
        var nav = new NavigationHandle<string>(new NavigationStack<string>("a"));
        Func<string, bool> isA = static r => r == "a";
        nav.Navigate("b");
        Cycle();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) Cycle();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal("b", nav.CurrentRoute);

        // From back [a], current b, through every mutator, back to the same state.
        void Cycle()
        {
            nav.Replace("a");
            nav.Replace("b");
            nav.GoBack();
            nav.GoForward();
            nav.Navigate("c");
            nav.GoBack();
            nav.Navigate("c");
            nav.PopTo(isA);
            nav.GoForward();
            nav.Reset("a");
            nav.Navigate("b");
        }
    }
}

/// <summary>RerenderAll reaches every live root in the process, so it must not run alongside other tests.</summary>
[CollectionDefinition(nameof(NavigationHotReloadTests), DisableParallelization = true)]
public sealed class NavigationHotReloadCollection;

[Collection(nameof(NavigationHotReloadTests))]
public class NavigationHotReloadTests
{
    [Fact]
    public void HotReload_KeepsTheHandle_ItsHistory_AndThePages()
    {
        NavigationHandle<string>? nav = null;
        var host = new TestHost(MauiHandlers.CreateRegistry());
        host.Render(Component<NavApp, NavAppProps>(new(n => nav = n, route => Page(route, null))));
        var handle = nav!;
        handle.Navigate("a");
        handle.Navigate("b");
        host.Scheduler.Drain();
        var page = host.View<ReactorNavigationPage>();
        var pages = page.Navigation.NavigationStack.ToArray();

        nav = null;
        Microsoft.UI.Reactor.Hosting.ReactorRoot.RerenderAll();
        host.Scheduler.Drain();

        // The component did render again (it reported its handle), and kept it.
        Assert.Same(handle, nav);
        Assert.Equal(["home", "a"], handle.BackStack);
        Assert.Equal(pages, page.Navigation.NavigationStack);
    }
}
