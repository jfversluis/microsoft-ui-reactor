using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Handlers;
using Microsoft.UI.Reactor.Navigation;
using static Microsoft.UI.Reactor.Maui.Factories;
using NavigationMode = Microsoft.UI.Reactor.Navigation.NavigationMode;

namespace Microsoft.UI.Reactor.Maui.Tests;

public sealed record NavAppProps(Action<NavigationHandle<string>> Report, Func<string, Element?> Map);

public sealed class NavApp : Component<NavAppProps>
{
    public override Element Render()
    {
        var nav = UseNavigation("home");
        Props.Report(nav);
        return NavigationHost(nav, Props.Map);
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
                log.Entries.Add($"{route}:navigatingTo:{c.Mode}");
                if (log.RefuseTo.Contains(route)) c.Cancel();
                if (log.OnNavigatingTo.TryGetValue(route, out var then)) then(c);
            },
            onNavigatedTo: c => log.Entries.Add($"{route}:navigatedTo:{c.Mode}"),
            onNavigatingFrom: c =>
            {
                log.Entries.Add($"{route}:navigatingFrom:{c.Mode}");
                if (log.RefuseFrom.Contains(route)) c.Cancel();
            },
            onNavigatedFrom: c => log.Entries.Add($"{route}:navigatedFrom:{c.Mode}"));
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

        public Harness()
        {
            Host.Render(Component<NavApp, NavAppProps>(new(nav => Nav = nav, route => Component<LogPage, LogPageProps>(new(route, Log)))));
            Settle();
        }

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
        Assert.False(h.Page.Navigator.Animated);
        Assert.Equal("home,a", h.Titles);

        h.Nav.Navigate("b");
        h.Settle();
        Assert.True(h.Page.Navigator.Animated);
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
        Assert.DoesNotContain("navigatingFrom", h.TakeLog());
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
        nav.Navigate("b");
        nav.GoBack();
        nav.GoForward();
        nav.Replace("a");
        nav.Replace("b");

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            nav.Replace("a");
            nav.Replace("b");
            nav.GoBack();
            nav.GoForward();
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
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

        Microsoft.UI.Reactor.Hosting.ReactorRoot.RerenderAll();
        host.Scheduler.Drain();

        Assert.Same(handle, nav);
        Assert.Equal(["home", "a"], handle.BackStack);
        Assert.Equal(pages, page.Navigation.NavigationStack);
    }
}
