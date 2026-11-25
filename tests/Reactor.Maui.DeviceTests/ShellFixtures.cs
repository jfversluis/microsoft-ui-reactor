using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui;
using Microsoft.UI.Reactor.Maui.Handlers;
using Microsoft.UI.Reactor.Navigation;
using static Microsoft.UI.Reactor.Maui.Factories;
using static Reactor.Maui.DeviceTests.FixtureContext;
using MauiShell = Microsoft.Maui.Controls.Shell;

namespace Reactor.Maui.DeviceTests;

/// <summary>
/// Declarative Shell on the platform (spec 069 §4.6): selection, stacks in tabs and the user's back,
/// asserting what the platform shows, not only Shell's model.
/// </summary>
public static partial class Fixtures
{
    private static IEnumerable<Fixture> ShellFixtures() =>
    [
        new("Shell_TabTap_IsReported_AndARefusalRestoresTheTab", ShellTabTap),
        new("Shell_PushPopResetAndRootReplacement_InATab", ShellTabStack),
        new("Shell_RestoredHistory_ShowsEveryPage", ShellRestoredHistory),
        new("Shell_NativeBack_RunsTheGuardOnce_AndIsAccepted", c => ShellNativeBack(c, refuseFrom: false, refuseTo: false)),
        new("Shell_NativeBackTheGuardRefuses_MovesNoPage", c => ShellNativeBack(c, refuseFrom: true, refuseTo: false)),
        new("Shell_NativeBackTheDestinationRefuses_MovesNoPage", c => ShellNativeBack(c, refuseFrom: false, refuseTo: true)),
        new("Shell_BackAtTheRoot_IsPassedOn", ShellBackAtRoot),
        new("Shell_StacksAreKept_AcrossTabSwitches", ShellStacksKept),
        new("Shell_RapidPushSwitchAndBack_Settle", ShellRapid),
        new("Shell_ReorderingAndRemovingTheSelectedTab", ShellRemoveSelected),
        new("Shell_NavBarAndTabBarVisibility_UpdateLive", ShellBarsLive),
        new("Shell_FlyoutHeaderAndContent_UpdateLive", ShellFlyoutLive),
        new("Shell_FlyoutOpenedByTheUser_IsReported", ShellFlyoutByUser),
        new("Shell_ModalFromAShellPage_IsShownAndDismissed", ShellModal),
        new("Shell_TypedResetAndBackWithAQuery", ShellTypedRoutes),
        new("Shell_DeepLink_SelectsATabAndItsStack", ShellDeepLink),
        new("Shell_WindowSwap_WaitsForABusySection", ShellWindowSwap),
        // The real iOS edge swipe, made from outside the app (Appium, or a person) while the fixture
        // waits (spec 069 §4.7). Run only when the filter names them, e.g. "ManualSwipe_Completes".
        .. (Environment.GetEnvironmentVariable("REACTOR_DEVICE_TEST_FILTER")?.Contains("ManualSwipe", StringComparison.Ordinal) == true
            ? new Fixture[]
            {
                new("ManualSwipe_Completes", c => ShellManualSwipe(c, expectBack: true, holdNavigation: false, swapAfter: false)),
                new("ManualSwipe_ReleasedEarly", c => ShellManualSwipe(c, expectBack: false, holdNavigation: false, swapAfter: false)),
                new("ManualSwipe_CompletesWithAHeldNavigation", c => ShellManualSwipe(c, expectBack: true, holdNavigation: true, swapAfter: false)),
                new("ManualSwipe_ReleasedEarlyWithAHeldNavigation_ThenAWindowSwap", c => ShellManualSwipe(c, expectBack: false, holdNavigation: true, swapAfter: true)),
            }
            : []),
    ];

    private sealed class ShellLab
    {
        public HostLog Log { get; } = new();
        public List<string> Selected { get; } = [];
        public bool Accept { get; set; } = true;
        public string InitialTab { get; set; } = "a";
        public Func<ShellLab, Element> Build { get; set; } = Default;
        public NavigationHandle<string> A = null!;
        public NavigationHandle<string> B = null!;
        public string Tab = "";
        public Action<string> SetTab = null!;
        public int Generation;

        public void OnSelected(string key)
        {
            Selected.Add(key);
            if (Accept) SetTab(key);
        }

        public Element Host(NavigationHandle<string> nav, Func<NavigationStackElement, NavigationStackElement>? stack = null) =>
            NavigationHost(nav, route => Component<HostPage, HostPageProps>(new(route, Log)), stack);

        public static Element Default(ShellLab l) =>
            Shell(l.Tab, l.OnSelected,
                TabBar(
                    ShellTab("a", "A", l.Host(l.A)),
                    ShellTab("b", "B", l.Host(l.B)),
                    ShellTab("c", "C", Page("c", Label("c page")))));
    }

    private sealed record ShellLabProps(ShellLab Lab, int Generation);

    private sealed class ShellLabApp : Component<ShellLabProps>
    {
        public override Element Render()
        {
            var lab = Props.Lab;
            var (tab, setTab) = UseState(lab.InitialTab);
            lab.A = UseNavigation("a0");
            lab.B = UseNavigation("b0");
            lab.Tab = tab;
            lab.SetTab = setTab;
            return lab.Build(lab);
        }
    }

    private static async Task<(ShellLab Lab, ReactorShell Shell)> ShowShell(FixtureContext c, Action<ShellLab>? configure = null)
    {
        var lab = new ShellLab();
        configure?.Invoke(lab);
        c.Show(Component<ShellLabApp, ShellLabProps>(new(lab, 0)));
        var shell = await LoadedPage<ReactorShell>(c);
        await c.WaitFor(() => shell.CurrentPage is { IsLoaded: true } page && Platform.IsDisplayed(page), "the first tab's page");
        return (lab, shell);
    }

    private static void RenderAgain(FixtureContext c, ShellLab lab) => c.Render(Component<ShellLabApp, ShellLabProps>(new(lab, ++lab.Generation)));

    private static ShellSection SectionOf(ReactorShell shell, string key) =>
        shell.Items.SelectMany(i => i.Items).First(s => ShellNatives.GetKey(s) == key);

    private static string SectionPages(ShellSection section) =>
        string.Join(",", new[] { section.CurrentItem?.Content as Page }.Concat(section.Stack.Skip(1)).Select(p => p?.Title));

    private static Page TopOf(ShellSection section) => section.Stack.Count > 1 ? section.Stack[^1] : (Page)section.CurrentItem.Content;

    private static bool SectionIdle(ShellSection section) => ShellNatives.GetNavigator(section) is not { IsIdle: false };

    /// <summary>
    /// Waits until the shown tab is <paramref name="key"/> with <paramref name="pages"/>, its navigator
    /// is idle and the platform displays its top page at the native depth, then checks it stays so.
    /// </summary>
    private static async Task ShellShows(FixtureContext c, ReactorShell shell, string key, string pages, string what)
    {
        bool Shows()
        {
            var section = SectionOf(shell, key);
            return shell.ShownKey() == key && SectionPages(section) == pages && SectionIdle(section)
                && TopOf(section) is { IsLoaded: true } top && Platform.IsDisplayed(top)
                && (Platform.SectionDepth(top) is not { } depth || depth == section.Stack.Count);
        }

        await c.WaitFor(Shows, $"{what}: tab {key} showing {pages}", 10_000);
        await c.Settle(300);
        var section = SectionOf(shell, key);
        Equal(key, shell.ShownKey(), $"{what}: the tab shown");
        Equal(pages, SectionPages(section), $"{what}: the section's pages");
        Check(Platform.IsDisplayed(TopOf(section)), $"{what}: the platform displays the top page");
        if (Platform.SectionDepth(TopOf(section)) is { } nativeDepth) Equal(section.Stack.Count, nativeDepth, $"{what}: the native depth");
    }

    private static bool UserTaps(ReactorShell shell, string key)
    {
        var section = SectionOf(shell, key);
        // What the platform's tab bar does for a tap: propose the section, which sets it if accepted.
        return ((IShellItemController)section.Parent).ProposeSection(section);
    }

    // ── Selection ───────────────────────────────────────────────────────────

    private static async Task ShellTabTap(FixtureContext c)
    {
        var (lab, shell) = await ShowShell(c, l => l.Accept = false);

        Check(UserTaps(shell, "b"), "the tap is proposed");
        await c.WaitFor(() => lab.Selected.Count == 1, "the tap to be reported");
        Equal("b", lab.Selected[0], "the reported key");
        await ShellShows(c, shell, "a", "a0", "the refused tap undone");

        // Rapid refused taps: each is reported, and the declared tab is shown again.
        UserTaps(shell, "b");
        UserTaps(shell, "c");
        await c.WaitFor(() => lab.Selected.Count == 3, "both taps to be reported");
        Equal("b,b,c", string.Join(",", lab.Selected), "the reported keys");
        await ShellShows(c, shell, "a", "a0", "rapid refused taps undone");
        Check(!Platform.IsDisplayed(TopOf(SectionOf(shell, "c"))), "tab c's page isn't displayed");

        // Rapid taps: the restore goes to the latest declaration, here the one the app accepts last.
        lab.Accept = true;
        UserTaps(shell, "b");
        UserTaps(shell, "c");
        await c.WaitFor(() => lab.Tab == "c", "the last tap to be accepted");
        await c.WaitFor(() => shell.ShownKey() == "c" && Platform.IsDisplayed(TopOf(SectionOf(shell, "c"))), "tab c to be displayed");
        await c.Settle(500);
        Equal("c", shell.ShownKey(), "the tab shown after rapid taps");
        await c.WaitFor(() => !Platform.IsDisplayed(TopOf(SectionOf(shell, "a"))), "tab a's page to be no longer displayed", 3000);
    }

    private static async Task ShellTabStack(FixtureContext c)
    {
        var (lab, shell) = await ShowShell(c);
        var root = SectionOf(shell, "a").CurrentItem.Content;

        lab.A.Navigate("a1");
        lab.A.Navigate("a2");
        await ShellShows(c, shell, "a", "a0,a1,a2", "two pushes");
        Check(ReferenceEquals(root, SectionOf(shell, "a").CurrentItem.Content), "the root page stays mounted");

        lab.A.GoBack();
        await ShellShows(c, shell, "a", "a0,a1", "back");
        lab.A.Replace("r");
        await ShellShows(c, shell, "a", "a0,r", "replace");
        lab.A.Reset("z");
        await ShellShows(c, shell, "a", "z", "reset");
        Check(!ReferenceEquals(root, SectionOf(shell, "a").CurrentItem.Content), "reset replaced the root page");
        lab.A.Replace("y");
        await ShellShows(c, shell, "a", "y", "the root replaced");
    }

    private static async Task ShellRestoredHistory(FixtureContext c)
    {
        var (lab, shell) = await ShowShell(c);
        lab.A.SetState(new NavigationState<string>(["a0", "x", "y"], "z", []));
        await ShellShows(c, shell, "a", "a0,x,y,z", "the restored history");
        lab.A.GoBack();
        await ShellShows(c, shell, "a", "a0,x,y", "back through the restored history");

        // A Shell mounted over a handle that already has a history shows all of it.
        lab.Build = _ => Page("away", Label("away page"));
        RenderAgain(c, lab);
        await c.WaitFor(() => c.Window.Page is { Title: "away", IsLoaded: true }, "the page in the Shell's place", 10_000);
        lab.Build = ShellLab.Default;
        RenderAgain(c, lab);
        await c.WaitFor(() => c.Window.Page is ReactorShell { IsLoaded: true } again && !ReferenceEquals(again, shell), "a new Shell", 10_000);
        await ShellShows(c, (ReactorShell)c.Window.Page!, "a", "a0,x,y", "the history in the new Shell");
    }

    private static async Task ShellNativeBack(FixtureContext c, bool refuseFrom, bool refuseTo)
    {
        var (lab, shell) = await ShowShell(c);
        lab.A.Navigate("a1");
        await ShellShows(c, shell, "a", "a0,a1", "the push");
        var section = SectionOf(shell, "a");
        var a1 = section.Stack[1];
        lab.Log.Entries.Clear();
        if (refuseFrom) lab.Log.RefuseFrom.Add("a1");
        if (refuseTo) lab.Log.RefuseTo.Add("a0");
        var navigated = new List<ShellNavigationSource>();
        shell.Navigated += (_, e) => navigated.Add(e.Source);

        Platform.ShellBack(section);

        if (refuseFrom || refuseTo)
        {
            await c.WaitFor(() => lab.Log.Guards("a1") == 1, "the guard");
            await c.Settle(800);
            await ShellShows(c, shell, "a", "a0,a1", "the refused back");
            Check(ReferenceEquals(a1, section.Stack[1]), "the same page is shown");
            Equal("a1", lab.A.CurrentRoute, "the handle's route");
            Equal(0, navigated.Count, "Shell navigations for a refused back (no page moved)");
            Equal(refuseTo ? "a1:navigatingFrom:Pop a0:navigatingTo:Pop" : "a1:navigatingFrom:Pop", lab.Log.Take(), "the callbacks");
        }
        else
        {
            await c.WaitFor(() => lab.A.CurrentRoute == "a0", "the back to reach the handle");
            await ShellShows(c, shell, "a", "a0", "the accepted back");
            await c.Settle(800);
            Equal("a0", SectionPages(section), "the page stays popped");
            Equal(1, lab.Log.Guards("a1"), "guard calls");
            await c.WaitFor(() => lab.Log.Entries.Count == 4, "the back's callbacks");
            Equal("a1:navigatingFrom:Pop a0:navigatingTo:Pop a0:navigatedTo:Pop@ a1:navigatedFrom:Pop", lab.Log.Take(), "the callbacks");
        }
    }

    private static async Task ShellBackAtRoot(FixtureContext c)
    {
        var (lab, shell) = await ShowShell(c);
        Check(!shell.SendBackButtonPressed(), "at the root the back key is passed on to the app or the OS");
        await c.Settle(300);
        Equal(0, lab.Log.Guards("a0"), "guard calls");
        Check(SectionIdle(SectionOf(shell, "a")), "no back is pending");
    }

    private static async Task ShellStacksKept(FixtureContext c)
    {
        var (lab, shell) = await ShowShell(c);
        lab.A.Navigate("a1");
        await ShellShows(c, shell, "a", "a0,a1", "a's push");
        var a1 = SectionOf(shell, "a").Stack[1];

        lab.SetTab("b");
        await ShellShows(c, shell, "b", "b0", "tab b");
        lab.B.Navigate("b1");
        await ShellShows(c, shell, "b", "b0,b1", "b's push");

        lab.SetTab("a");
        await ShellShows(c, shell, "a", "a0,a1", "tab a again");
        Check(ReferenceEquals(a1, SectionOf(shell, "a").Stack[1]), "a's page was kept");
        lab.SetTab("b");
        await ShellShows(c, shell, "b", "b0,b1", "tab b again");
    }

    private static async Task ShellRapid(FixtureContext c)
    {
        var (lab, shell) = await ShowShell(c);
        await c.Settle(300);
        lab.A.Navigate("a1");
        lab.SetTab("b");
        lab.B.Navigate("b1");
        lab.SetTab("a");
        lab.A.GoBack();
        await ShellShows(c, shell, "a", "a0", "rapid push, switch and back");
        Equal("b0,b1", SectionPages(SectionOf(shell, "b")), "tab b's stack");
        // The handle's back ran its guard (over the page shown when it was called); no user back was seen.
        Equal(1, lab.Log.Entries.Count(e => e.Contains(":navigatingFrom:Pop")), "back guards");

        // The user's back right after a push the host hasn't shown: one guarded back of the model.
        lab.A.Navigate("a1");
        await ShellShows(c, shell, "a", "a0,a1", "a push");
        lab.Log.Entries.Clear();
        lab.A.Navigate("a2");
        Platform.ShellBack(SectionOf(shell, "a"));
        await ShellShows(c, shell, "a", "a0,a1", "the back superseding the push");
        await c.Settle(500);
        Equal("a1", lab.A.CurrentRoute, "the model");
        Equal("a0,a1", SectionPages(SectionOf(shell, "a")), "the stack stays");
        Equal(1, lab.Log.Entries.Count(e => e.Contains(":navigatingFrom:Pop")), "back guards for the user's back");
    }

    private static async Task ShellRemoveSelected(FixtureContext c)
    {
        var tabs = new[] { "a", "b", "c" };
        var (lab, shell) = await ShowShell(c, l =>
        {
            l.InitialTab = "b";
            l.Build = lb => Shell(lb.Tab, lb.OnSelected, TabBar([.. tabs.Select(k => ShellTab(k, k.ToUpperInvariant(), Page(k, Label($"{k} page"))))]));
        });
        await ShellShows(c, shell, "b", "b", "tab b");

        tabs = ["c", "b", "a"];
        RenderAgain(c, lab);
        await ShellShows(c, shell, "b", "b", "the selected tab reordered");
        Equal("c,b,a", string.Join(",", shell.Items[0].Items.Select(ShellNatives.GetKey)), "the tab order");

        tabs = ["c", "a"];
        lab.SetTab("c");
        await ShellShows(c, shell, "c", "c", "the selected tab removed, another declared");
        Equal("c,a", string.Join(",", shell.Items[0].Items.Select(ShellNatives.GetKey)), "the tabs left");

        // Removed without a new declaration: the first tab is shown and reported once.
        lab.Accept = false;
        tabs = ["a"];
        RenderAgain(c, lab);
        await ShellShows(c, shell, "a", "a", "the fallback tab");
        await c.Settle(300);
        Equal("a", string.Join(",", lab.Selected), "the fallback reported once");
        Check(c.UnhandledErrors.Count == 0, "no errors");
    }

    // ── Pages and the flyout ────────────────────────────────────────────────

    private sealed record BarsProps(Action<Action<bool>> Report);

    private sealed class BarsApp : Component<BarsProps>
    {
        public override Element Render()
        {
            var (visible, setVisible) = UseState(true);
            Props.Report(setVisible);
            return Shell("a", null, TabBar(
                ShellTab("a", "A", Page("bars", Label("bars")).NavBarIsVisible(visible).TabBarIsVisible(visible)),
                ShellTab("b", "B", Page("other", Label("other")))));
        }
    }

    private static async Task ShellBarsLive(FixtureContext c)
    {
        Action<bool>? setVisible = null;
        c.Show(Component<BarsApp, BarsProps>(new(set => setVisible = set)));
        var shell = await LoadedPage<ReactorShell>(c);
        var page = (Page)SectionOf(shell, "a").CurrentItem.Content;
        await c.WaitFor(() => Platform.IsDisplayed(page), "the page");
        await c.Settle(300);
        Check(Platform.NavBarShown(page) is not null, "the native navigation bar can be read");
        Check(Platform.TabBarShown(page) is not null, "the native tab bar can be read");
        Check(Platform.NavBarShown(page) == true, "the navigation bar shows");
        Check(Platform.TabBarShown(page) == true, "the tab bar shows");

        setVisible!(false);
        try
        {
            await c.WaitFor(() => !MauiShell.GetNavBarIsVisible(page) && Platform.NavBarShown(page) == false
                && Platform.TabBarShown(page) == false, "both bars to hide", 5000);
        }
        catch (CheckFailedException ex)
        {
            throw new CheckFailedException($"{ex.Message} (nav {Platform.NavBarShown(page)}, tabs {Platform.TabBarShown(page)}, {Platform.Describe()})");
        }
        Check(!MauiShell.GetTabBarIsVisible(page), "the page's tab bar property");

        setVisible(true);
        await c.WaitFor(() => Platform.NavBarShown(page) == true && Platform.TabBarShown(page) == true, "both bars to show again", 5000);
    }

    private sealed record FlyoutAppProps(Action<Action<string>, Action<bool>> Report, List<bool> Reported);

    private sealed class FlyoutApp : Component<FlyoutAppProps>
    {
        public override Element Render()
        {
            var (header, setHeader) = UseState("header one");
            var (open, setOpen) = UseState(false);
            Props.Report(setHeader, setOpen);
            return Shell("a", null,
                    FlyoutItem("A", ShellTab("a", "A", Page("a", Label("a page")))),
                    FlyoutItem("B", ShellTab("b", "B", Page("b", Label("b page")))))
                .FlyoutBehavior(FlyoutBehavior.Flyout)
                .FlyoutIsPresented(open, value =>
                {
                    Props.Reported.Add(value);
                    setOpen(value);
                })
                .FlyoutHeader(Label(header).AutomationId("flyout-header"))
                .FlyoutContent(header == "header two" ? Label("custom content").AutomationId("flyout-content") : null);
        }
    }

    private static async Task ShellFlyoutLive(FixtureContext c)
    {
        Action<string>? setHeader = null;
        Action<bool>? setOpen = null;
        c.Show(Component<FlyoutApp, FlyoutAppProps>(new((h, o) => (setHeader, setOpen) = (h, o), [])));
        var shell = await LoadedPage<ReactorShell>(c);
        setOpen!(true);
        var label = await Loaded<ILabel>(c, "flyout-header");
        await c.WaitFor(() => Platform.Text(label) == "header one", "the header's text");
        var host = shell.FlyoutHeader;

        setHeader!("header two");
        await c.WaitFor(() => Platform.Text(label) == "header two", "the header to update in place");
        Check(ReferenceEquals(host, shell.FlyoutHeader), "the same header host");
        var content = await Loaded<ILabel>(c, "flyout-content");
        await c.WaitFor(() => Platform.Text(content) == "custom content", "the custom flyout content");
        setOpen(false);
        await c.Settle(300);
    }

    private static async Task ShellFlyoutByUser(FixtureContext c)
    {
        var reported = new List<bool>();
        c.Show(Component<FlyoutApp, FlyoutAppProps>(new((_, _) => { }, reported)));
        var shell = await LoadedPage<ReactorShell>(c);
        await c.Settle(300);

        // What the platform's flyout does when the user opens it.
        shell.SetValueFromRenderer(MauiShell.FlyoutIsPresentedProperty, true);
        await c.WaitFor(() => reported.Count == 1, "the user's change to be reported");
        Equal(true, reported[0], "the reported value");
        await c.Settle(500);
        Check(shell.FlyoutIsPresented, "the accepted flyout stays open");
        shell.SetValueFromRenderer(MauiShell.FlyoutIsPresentedProperty, false);
        await c.WaitFor(() => reported.Count == 2, "the close to be reported");
        await c.Settle(300);
    }

    private sealed record ModalProps(ShellLab Lab, Action<Action<bool>> Report);

    private sealed class ModalShellApp : Component<ModalProps>
    {
        public override Element Render()
        {
            var lab = Props.Lab;
            lab.A = UseNavigation("a0");
            var (modal, setModal) = UseState(false);
            Props.Report(setModal);
            return Shell("a", null, TabBar(ShellTab("a", "A", lab.Host(lab.A, s => s with
            {
                Modals = modal ? [Page("sheet", Label("sheet page"))] : [],
                OnModalDismissed = remaining =>
                {
                    lab.Selected.Add($"dismissed:{remaining}");
                    setModal(remaining > 0);
                },
            }))));
        }
    }

    private static async Task ShellModal(FixtureContext c)
    {
        var lab = new ShellLab();
        Action<bool>? setModal = null;
        c.Show(Component<ModalShellApp, ModalProps>(new(lab, set => setModal = set)));
        var shell = await LoadedPage<ReactorShell>(c);
        await ShellShows(c, shell, "a", "a0", "the tab");
        lab.A.Navigate("a1");
        await ShellShows(c, shell, "a", "a0,a1", "the push");
        var section = SectionOf(shell, "a");

        setModal!(true);
        await c.WaitFor(() => section.Navigation.ModalStack.Count == 1 && Platform.IsDisplayed(section.Navigation.ModalStack[0]) && SectionIdle(section),
            "the modal to show", 10_000);
        Equal("a1", lab.A.CurrentRoute, "the model under the modal");

        // A page change waits while the modal is shown: Shell would dismiss it.
        lab.A.Navigate("a2");
        await c.Settle(800);
        Equal(1, section.Navigation.ModalStack.Count, "the modal stays");
        Check(Platform.IsDisplayed(section.Navigation.ModalStack[0]), "the modal is still displayed");
        Equal("a0,a1", SectionPages(section), "the stack under the modal waits");
        Equal("", string.Join(",", lab.Selected), "dismissals reported");

        setModal(false);
        await c.WaitFor(() => section.Navigation.ModalStack.Count == 0 && SectionIdle(section), "the modal to go", 10_000);
        await ShellShows(c, shell, "a", "a0,a1,a2", "the held push after the modal");
        Equal("", string.Join(",", lab.Selected), "dismissals reported");
        Equal(0, lab.Log.Guards("a1"), "no back was seen");
    }

    // ── Typed routes and deep links ────────────────────────────────────────

    private abstract record ShopRoute
    {
        public sealed record List(bool Refresh = false) : ShopRoute;
        public sealed record Detail(int Id) : ShopRoute;
    }

    private sealed class ShopLab
    {
        public NavigationHandle<ShopRoute> Orders = null!;
        public Action<string> SetTab = null!;
        public Action<bool> SetRefresh = null!;
    }

    private sealed class ShopApp : Component<ShopLab>
    {
        public override Element Render()
        {
            var (tab, setTab) = UseState("home");
            var orders = UseNavigation<ShopRoute>(new ShopRoute.List());
            // A result for the page below, held in app state: the typed "..?refresh=true".
            var (refresh, setRefresh) = UseState(false);
            Props.Orders = orders;
            Props.SetTab = setTab;
            Props.SetRefresh = setRefresh;
            return Shell(tab, setTab, TabBar(
                ShellTab("home", "Home", Page("home", Label("home page"))),
                ShellTab("orders", "Orders", NavigationHost(orders, route => route switch
                {
                    ShopRoute.Detail d => Page($"order {d.Id}", Label($"order {d.Id}")),
                    ShopRoute.List l => Page(l.Refresh || refresh ? "orders refreshed" : "orders", Label("orders")),
                    _ => null,
                }))));
        }
    }

    private static async Task ShellTypedRoutes(FixtureContext c)
    {
        var lab = new ShopLab();
        c.Show(Component<ShopApp, ShopLab>(lab));
        var shell = await LoadedPage<ReactorShell>(c);
        lab.SetTab("orders");
        lab.Orders.Navigate(new ShopRoute.Detail(1));
        lab.Orders.Navigate(new ShopRoute.Detail(2));
        await ShellShows(c, shell, "orders", "orders,order 1,order 2", "two orders");

        // "..?refresh=true": the result goes into app state the list reads, then one back. The list
        // page stays mounted (no remount).
        lab.Orders.GoBack();
        await ShellShows(c, shell, "orders", "orders,order 1", "back");
        var list = SectionOf(shell, "orders").CurrentItem.Content;
        lab.SetRefresh(true);
        lab.Orders.GoBack();
        await ShellShows(c, shell, "orders", "orders refreshed", "back to the list with refresh=true");
        Check(ReferenceEquals(list, SectionOf(shell, "orders").CurrentItem.Content), "the list page stayed mounted");

        // "//home": an absolute route is a selection plus a reset of that tab's stack.
        lab.Orders.Navigate(new ShopRoute.Detail(3));
        await ShellShows(c, shell, "orders", "orders refreshed,order 3", "another order");
        lab.SetRefresh(false);
        lab.SetTab("home");
        lab.Orders.Reset(new ShopRoute.List());
        await c.WaitFor(() => shell.ShownKey() == "home" && Platform.IsDisplayed(TopOf(SectionOf(shell, "home"))), "home", 10_000);
        await c.WaitFor(() => SectionPages(SectionOf(shell, "orders")) == "orders" && SectionIdle(SectionOf(shell, "orders")), "the orders tab reset", 10_000);
    }

    private static async Task ShellDeepLink(FixtureContext c)
    {
        var map = new DeepLinkMap<ShopRoute>()
            .Map("/orders", _ => new ShopRoute.List())
            .Map("/orders/{id}", a => new ShopRoute.Detail(a.Get<int>("id")), () => [new ShopRoute.List()]);
        var lab = new ShopLab();
        c.Show(Component<ShopApp, ShopLab>(lab));
        var shell = await LoadedPage<ReactorShell>(c);
        await c.WaitFor(() => shell.ShownKey() == "home", "home");

        var link = map.Resolve(new Uri("shop://app/orders/42"));
        Check(link.Matched, "the link resolves");
        lab.SetTab("orders");
        lab.Orders.SetState(new NavigationState<ShopRoute>(link.Routes[..^1], link.Routes[^1], []));
        await ShellShows(c, shell, "orders", "orders,order 42", "the deep link");
    }

    private static async Task ShellWindowSwap(FixtureContext c)
    {
        var (lab, shell) = await ShowShell(c);
        lab.A.Navigate("a1");
        await ShellShows(c, shell, "a", "a0,a1", "the push");
        var section = SectionOf(shell, "a");
        var navigator = ShellNatives.GetNavigator(section)!;
        // The fixture's window-page setter is the one ReactorWindow uses; wait on every platform here.
        c.Pages.WaitForNavigation = true;

        Platform.ShellBack(section);
        Check(!navigator.IsIdle, "the section is busy with the user's back");
        // The root renders a page instead of the Shell: the Shell's tree unmounts before the swap.
        lab.Build = _ => Page("next", Label("next page"));
        RenderAgain(c, lab);
        Check(c.Pages.IsPending, "the swap waits for the busy section");
        Check(ReferenceEquals(c.Window.Page, shell), "the Shell stays until the section settles");
        await c.WaitFor(() => c.Window.Page is { Title: "next" }, "the swap", 10_000);
        Check(navigator.IsIdle, "the section settled before the swap");
        Check(shell.Lingering.Count == 0, "the Shell lists no busy navigator after the swap");
    }

    /// <summary>
    /// Waits for a back swipe made from outside the app (an Appium edge swipe on the iOS simulator) and
    /// checks the page shown, the model and the guard count. With <paramref name="holdNavigation"/>, the
    /// app navigates while the gesture is in flight; the navigation is held and shown once it settles.
    /// </summary>
    private static async Task ShellManualSwipe(FixtureContext c, bool expectBack, bool holdNavigation, bool swapAfter)
    {
        var (lab, shell) = await ShowShell(c);
        lab.A.Navigate("a1");
        await ShellShows(c, shell, "a", "a0,a1", "the push");
        var section = SectionOf(shell, "a");
        var navigator = ShellNatives.GetNavigator(section)!;
        lab.Log.Entries.Clear();
        Console.WriteLine($"# MANUAL-READY {(expectBack ? "complete" : "release early")}");

        var held = false;
        var sawBusy = false;
        var minDepth = section.Stack.Count;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 22_000)
        {
            if (!navigator.IsIdle && lab.Log.Guards("a1") == 1)
            {
                sawBusy = true;
                if (holdNavigation && !held)
                {
                    held = true;
                    lab.A.Navigate("held");
                    Console.WriteLine("# MANUAL: navigated during the gesture");
                }
            }
            minDepth = Math.Min(minDepth, section.Stack.Count);
            if (sawBusy && navigator.IsIdle) break;
            await Task.Delay(8);
        }
        Check(sawBusy, "the swipe reached the guard and kept the section busy");
        Console.WriteLine($"# MANUAL: settled after {clock.ElapsedMilliseconds} ms, model {lab.A.CurrentRoute}, least depth {minDepth}, log [{string.Join(" ", lab.Log.Entries)}]");
        Equal(1, lab.Log.Guards("a1"), "guard calls");
        Equal(expectBack ? 1 : 2, minDepth, expectBack ? "the platform popped the page" : "the platform never popped the page");

        if (holdNavigation)
        {
            // The approval is void after a completed swipe (the model moved); the stack follows the model.
            await ShellShows(c, shell, "a", "a0,a1,held", "the held navigation shown after the gesture");
            Equal("held", lab.A.CurrentRoute, "the model");
        }
        else if (expectBack)
        {
            await ShellShows(c, shell, "a", "a0", "the completed swipe");
            Equal("a0", lab.A.CurrentRoute, "the model");
            Equal(1, lab.Log.Entries.Count(e => e == "a0:navigatingTo:Pop"), "the destination asked once");
        }
        else
        {
            await ShellShows(c, shell, "a", "a0,a1", "the cancelled swipe");
            Equal("a1", lab.A.CurrentRoute, "the model");
        }

        if (swapAfter)
        {
            var next = new ContentPage { Content = new Label { Text = "after the swipe" } };
            c.Pages.Set(next);
            await c.WaitFor(() => ReferenceEquals(c.Window.Page, next), "the window swap", 5000);
        }
    }
}
