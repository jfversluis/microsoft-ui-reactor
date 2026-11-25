using System.Diagnostics;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui;
using Microsoft.UI.Reactor.Maui.Handlers;
using Microsoft.UI.Reactor.Navigation;
using static Microsoft.UI.Reactor.Maui.Factories;
using static Reactor.Maui.DeviceTests.FixtureContext;

namespace Reactor.Maui.DeviceTests;

/// <summary>
/// NavigationHost on the platform (spec 069 §2.4): the handle's navigations reach MAUI's stack, the
/// user's back reaches the guard exactly once, and the lifecycle runs in WinUI's order.
/// </summary>
public static partial class Fixtures
{
    private static IEnumerable<Fixture> NavigationHostFixtures() =>
    [
        new("NavigationHost_HandleNavigations_ReachThePlatformStack", HostNavigations),
        new("NavigationHost_PlatformBack_RunsTheGuardOnce_AndIsAccepted", c => HostPlatformBack(c, refuse: false, native: false)),
        new("NavigationHost_PlatformBackTheGuardRefuses_PushesThePageBack", c => HostPlatformBack(c, refuse: true, native: false)),
        new("NavigationHost_NativeBack_RunsTheGuardOnce_AndIsAccepted", c => HostPlatformBack(c, refuse: false, native: true)),
        new("NavigationHost_NativeBackTheGuardRefuses_KeepsThePage", c => HostPlatformBack(c, refuse: true, native: true)),
        new("NavigationHost_BackKey_IsConsumed_AndRunsTheGuardOnce", HostBackKey),
        new("NavigationHost_BackKeyDuringAPush_RunsTheGuardOnce", HostBackKeyDuringPush),
        new("NavigationHost_AndroidBackDispatch_IsConsumed_AndRunsTheGuardOnce", HostAndroidBackDispatch),
        new("NavigationHost_Lifecycle_RunsInOrder_AfterThePlatformFinishes", HostLifecycle),
        new("NavigationHost_AnimatedFalse_PushesAndPops", HostNotAnimated),
        new("NavigationHost_ConfiguredNotAnimated_CoversModalsAndEveryChange", HostConfiguredNotAnimated),
        new("NavigationHost_SetState_RestoresIntoTheMountedHost", HostSetState),
    ];

    private sealed class HostLog
    {
        public List<string> Entries { get; } = [];
        public HashSet<string> RefuseFrom { get; } = [];
        public Func<string>? PlatformDepth { get; set; }
        public int Guards(string route) => Entries.Count(e => e == $"{route}:navigatingFrom:{NavigationMode.Pop}");
        public string Take()
        {
            var text = string.Join(" ", Entries);
            Entries.Clear();
            return text;
        }
    }

    private sealed record HostPageProps(string Route, HostLog Log);

    private sealed class HostPage : Component<HostPageProps>
    {
        public override Element Render()
        {
            var (route, log) = (Props.Route, Props.Log);
            UseNavigationLifecycle(
                onNavigatingTo: c => log.Entries.Add($"{route}:navigatingTo:{c.Mode}"),
                // The platform's depth when the navigation completes, and whether the navigator was still
                // busy: it must have finished the push or pop.
                onNavigatedTo: c => log.Entries.Add($"{route}:navigatedTo:{c.Mode}@{log.PlatformDepth?.Invoke()}"),
                onNavigatingFrom: c =>
                {
                    log.Entries.Add($"{route}:navigatingFrom:{c.Mode}");
                    if (log.RefuseFrom.Contains(route)) c.Cancel();
                },
                onNavigatedFrom: c => log.Entries.Add($"{route}:navigatedFrom:{c.Mode}"));
            return Page(route, Label($"{route} page").AutomationId($"label-{route}"));
        }
    }

    private sealed record HostAppProps(HostLog Log, Action<NavigationHandle<string>> Report, Action<Action<bool>>? ReportModal = null);

    private sealed class HostApp : Component<HostAppProps>
    {
        public override Element Render()
        {
            var nav = UseNavigation("home");
            var (modal, setModal) = UseState(false);
            Props.Report(nav);
            Props.ReportModal?.Invoke(setModal);
            Func<NavigationStackElement, NavigationStackElement>? stack = Props.ReportModal is null ? null : s => s with
            {
                Animated = false,
                Modals = modal ? [Page("sheet", Label("sheet"))] : [],
                OnModalDismissed = remaining => setModal(remaining > 0),
            };
            return NavigationHost(nav, route => Component<HostPage, HostPageProps>(new(route, Props.Log)), stack);
        }
    }

    private static async Task<(NavigationHandle<string> Nav, NavigationPage Page, HostLog Log)> ShowHost(
        FixtureContext c, Action<Action<bool>>? reportModal = null)
    {
        NavigationHandle<string>? nav = null;
        var log = new HostLog();
        c.Show(Component<HostApp, HostAppProps>(new(log, n => nav = n, reportModal)));
        var page = await LoadedPage<NavigationPage>(c);
        log.PlatformDepth = () => $"{page.Navigation.NavigationStack.Count}{(Probe(page).Idle ? "" : " (busy)")}";
        await c.WaitFor(() => page.CurrentPage is { IsLoaded: true }, "the root page");
        return (nav!, page, log);
    }

    private static string Titles(NavigationPage page) => string.Join(",", page.Navigation.NavigationStack.Select(p => p.Title));

    private static async Task Settled(FixtureContext c, NavigationPage page, string titles, string what)
    {
        await c.WaitFor(() => Titles(page) == titles && Probe(page).Idle && page.CurrentPage.IsLoaded, $"{what} ({titles})", 10_000);
        await c.Settle(300);
        Equal(titles, Titles(page), what);
        if (Platform.NavigationDepth(page) is { } depth) Equal(page.Navigation.NavigationStack.Count, depth, $"platform depth after {what}");
    }

    private static async Task HostNavigations(FixtureContext c)
    {
        var (nav, page, _) = await ShowHost(c);
        var home = page.Navigation.NavigationStack[0];

        nav.Navigate("a");
        nav.Navigate("b");
        await Settled(c, page, "home,a,b", "two pushes");
        Check(ReferenceEquals(home, page.Navigation.NavigationStack[0]), "the root page stays mounted");

        nav.GoBack();
        await Settled(c, page, "home,a", "back");
        nav.Replace("c");
        await Settled(c, page, "home,c", "replace");
        nav.Navigate("d");
        nav.Navigate("e");
        await Settled(c, page, "home,c,d,e", "two more pushes");
        nav.PopTo(r => r == "c");
        await Settled(c, page, "home,c", "pop to c");
        nav.Reset("f");
        await Settled(c, page, "f", "reset");
        Equal(1, nav.Depth, "the handle's depth");
    }

    private static async Task HostPlatformBack(FixtureContext c, bool refuse, bool native)
    {
        var (nav, page, log) = await ShowHost(c);
        nav.Navigate("detail");
        await Settled(c, page, "home,detail", "the push");
        var detail = page.CurrentPage;
        log.Entries.Clear();
        if (refuse) log.RefuseFrom.Add("detail");
        var pushes = 0;
        page.Pushed += (_, _) => pushes++;

        // MAUI's pop, or the platform's own back (iOS pops first; Android asks the app first).
        var platformPops = true;
        if (native) platformPops = Platform.NativeBack(page);
        else await page.PopAsync();
        if (refuse)
        {
            await c.WaitFor(() => log.Guards("detail") == 1, "the guard");
            await c.Settle(platformPops ? 300 : 800);
            await Settled(c, page, "home,detail", platformPops ? "the page pushed back" : "the page kept");
            Check(ReferenceEquals(detail, page.CurrentPage), "the same page is shown");
            Equal("detail", nav.CurrentRoute, "the handle's route");
            Equal(platformPops ? 1 : 0, pushes, "pushes");
        }
        else
        {
            await c.WaitFor(() => nav.CurrentRoute == "home", "the back navigation to reach the handle");
            await Settled(c, page, "home", "the accepted back");
            await c.Settle(800);
            Equal(0, pushes, "pages pushed after an accepted back");
            Equal("home", Titles(page), "the page stays popped");
        }
        Equal(1, log.Guards("detail"), "guard calls");
    }

    private static async Task HostBackKey(FixtureContext c)
    {
        var (nav, page, log) = await ShowHost(c);
        Check(!page.SendBackButtonPressed(), "at the root the back key is passed on");

        nav.Navigate("detail");
        await Settled(c, page, "home,detail", "the push");
        log.Entries.Clear();
        log.RefuseFrom.Add("detail");
        var pops = 0;
        page.Popped += (_, _) => pops++;
        Check(page.SendBackButtonPressed(), "a refused back consumes the key");
        await c.Settle(800);
        Equal(0, pops, "platform pops for a refused back (MAUI must not pop as well)");
        Equal("home,detail", Titles(page), "the stack after a refused back");
        Equal(1, log.Guards("detail"), "guard calls for the refused back");

        log.Entries.Clear();
        log.RefuseFrom.Clear();
        Check(page.SendBackButtonPressed(), "an accepted back consumes the key");
        await Settled(c, page, "home", "the accepted back");
        await c.Settle(500);
        Equal(1, log.Guards("detail"), "guard calls for the accepted back");
        Equal("home", nav.CurrentRoute, "the handle's route");
    }

    // The Android back key as the activity receives it, through MAUI's back-pressed callback and the
    // window, rather than by calling the page directly. Not at the root: unconsumed, it would finish
    // the activity.
    private static async Task HostAndroidBackDispatch(FixtureContext c)
    {
#if ANDROID
        var (nav, page, log) = await ShowHost(c);
        nav.Navigate("detail");
        await Settled(c, page, "home,detail", "the push");
        var dispatcher = ((AndroidX.Activity.ComponentActivity)Microsoft.Maui.ApplicationModel.Platform.CurrentActivity!).OnBackPressedDispatcher;
        log.Entries.Clear();
        log.RefuseFrom.Add("detail");
        var pops = 0;
        page.Popped += (_, _) => pops++;
        dispatcher.OnBackPressed();
        await c.Settle(800);
        Equal(0, pops, "platform pops for a refused back (MAUI must not pop as well)");
        Equal("home,detail", Titles(page), "the stack after a refused back");
        Equal(1, log.Guards("detail"), "guard calls for the refused back");

        log.Entries.Clear();
        log.RefuseFrom.Clear();
        dispatcher.OnBackPressed();
        await Settled(c, page, "home", "the accepted back");
        Equal(1, log.Guards("detail"), "guard calls for the accepted back");
        Equal("home", nav.CurrentRoute, "the handle's route");
#else
        Console.WriteLine("#   Android only");
        await Task.CompletedTask;
#endif
    }

    private static async Task HostBackKeyDuringPush(FixtureContext c)
    {
        var (nav, page, log) = await ShowHost(c);
        await c.Settle();
        nav.Navigate("detail");
        await c.WaitFor(() => page.Navigation.NavigationStack.Count == 2, "the push to start");
        var busy = !Probe(page).Idle;
        Check(busy, "the push is still running when the key arrives");
        Check(page.SendBackButtonPressed(), "the back key is consumed");

        await Settled(c, page, "home", "the back after the push");
        await c.Settle(800);
        Equal("home", Titles(page), "the page stays popped");
        Equal(1, log.Guards("detail"), "guard calls");
        var entries = log.Take();
        Equal("home:navigatingFrom:Push detail:navigatingTo:Push detail:navigatingFrom:Pop home:navigatingTo:Pop "
            + "home:navigatedFrom:Push home:navigatedTo:Pop@1 detail:navigatedFrom:Pop", entries,
            "every page left is told, and only home arrives");
    }

    private static async Task HostLifecycle(FixtureContext c)
    {
        var (nav, page, log) = await ShowHost(c);
        await c.Settle();
        nav.Navigate("detail");
        await Settled(c, page, "home,detail", "the push");
        await c.WaitFor(() => log.Entries.Count == 4, "the push's callbacks");
        Equal("home:navigatingFrom:Push detail:navigatingTo:Push detail:navigatedTo:Push@2 home:navigatedFrom:Push", log.Take(), "push");

        nav.Replace("other");
        await Settled(c, page, "home,other", "the replace");
        await c.WaitFor(() => log.Entries.Count == 4, "the replace's callbacks");
        Equal("detail:navigatingFrom:Replace other:navigatingTo:Replace other:navigatedTo:Replace@2 detail:navigatedFrom:Replace", log.Take(), "replace");

        Platform.NativeBack(page);
        await Settled(c, page, "home", "the user's back");
        await c.WaitFor(() => log.Entries.Count == 4, "the back's callbacks", 6000);
        Equal("other:navigatingFrom:Pop home:navigatingTo:Pop home:navigatedTo:Pop@1 other:navigatedFrom:Pop", log.Take(), "back");
    }

    private static async Task HostNotAnimated(FixtureContext c)
    {
        var (nav, page, _) = await ShowHost(c);
        var navigator = Probe(page);
        await c.Settle();

        var clock = Stopwatch.StartNew();
        nav.Navigate("detail", new NavigateOptions { Animated = false });
        await Settled(c, page, "home,detail", "the push without animation");
        Console.WriteLine($"#   push without animation settled in {clock.ElapsedMilliseconds} ms");
        Equal(false, ((ReactorNavigationPage)page).Navigator.LastOperationAnimated, "the push's animation argument");

        nav.GoBack();
        await Settled(c, page, "home", "the animated pop");
        Equal(true, ((ReactorNavigationPage)page).Navigator.LastOperationAnimated, "the next pop's animation argument");
        Check(navigator.Idle, "the navigator settled");
    }

    private static async Task HostConfiguredNotAnimated(FixtureContext c)
    {
        Action<bool>? setModal = null;
        var (nav, page, _) = await ShowHost(c, set => setModal = set);
        var navigator = ((ReactorNavigationPage)page).Navigator;
        await c.Settle();

        setModal!(true);
        await c.WaitFor(() => page.Navigation.ModalStack.Count == 1 && navigator.IsIdle, "the modal");
        Equal(false, navigator.LastOperationAnimated, "the modal push's animation argument");
        setModal(false);
        await c.WaitFor(() => page.Navigation.ModalStack.Count == 0 && navigator.IsIdle, "the modal's dismissal");
        Equal(false, navigator.LastOperationAnimated, "the modal pop's animation argument");

        nav.Navigate("detail");
        await Settled(c, page, "home,detail", "the push");
        Equal(false, navigator.LastOperationAnimated, "a push without options");
        nav.GoBack();
        await Settled(c, page, "home", "back");
        Equal(false, navigator.LastOperationAnimated, "back");
        nav.Navigate("detail", new NavigateOptions { Animated = true });
        await Settled(c, page, "home,detail", "an animated push");
        Equal(true, navigator.LastOperationAnimated, "a push asking for animation");
        nav.Reset("home");
        await Settled(c, page, "home", "reset");
        Equal(false, navigator.LastOperationAnimated, "reset");
    }

    private static async Task HostSetState(FixtureContext c)
    {
        var (nav, page, log) = await ShowHost(c);
        var home = page.Navigation.NavigationStack[0];
        log.RefuseFrom.Add("home");

        nav.SetState(new NavigationState<string>(["home", "a"], "b", ["c"]));
        await Settled(c, page, "home,a,b", "the restored stack");
        Check(ReferenceEquals(home, page.Navigation.NavigationStack[0]), "the root page stays mounted");
        await c.WaitFor(() => log.Entries.Count == 3, "the restore's callbacks");
        Equal("b:navigatingTo:Reset b:navigatedTo:Reset@3 home:navigatedFrom:Reset", log.Take(), "restoring runs no guard");
        Equal("c", string.Join(",", nav.ForwardStack), "the forward stack");
    }
}
