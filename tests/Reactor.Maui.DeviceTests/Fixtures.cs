using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Maui;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;
using Microsoft.UI.Reactor.Maui.Hosting;
using static Microsoft.UI.Reactor.Maui.Factories;
using static Reactor.Maui.DeviceTests.FixtureContext;

namespace Reactor.Maui.DeviceTests;

public static partial class Fixtures
{
    private static readonly (string Name, Func<ElementHandlerRegistry> Create)[] Renderers =
    [
        ("controls", MauiHandlers.CreateRegistry),
        ("direct", DirectHandlers.CreateRegistry),
    ];

    public static IReadOnlyList<Fixture> All { get; } =
    [
        .. Renderers.SelectMany(r => new Fixture[]
        {
            new($"Counter_TapUpdatesThePlatformLabel [{r.Name}]", c => Counter(c, r.Create())),
            new($"Entry_ControlledValue_IsCorrectedOnThePlatform [{r.Name}]", c => ControlledEntry(c, r.Create())),
            new($"Grid_ArrangesColumnsOnThePlatform [{r.Name}]", c => GridGeometry(c, r.Create())),
            new($"Keyed_RowsKeepTheirStateAcrossAReorder [{r.Name}]", c => KeyedRows(c, r.Create())),
            new($"Label_WrapsLongTextByDefault [{r.Name}]", c => LabelWraps(c, r.Create())),
        }),
        .. EchoFixtures(),
        .. TeardownFixtures(),
        .. WindowFixtures(),
        .. NavigationHostFixtures(),
        .. ShellFixtures(),
        .. DialogFixtures(),
        .. ControlRefFixtures(),
        .. ItemsCharacterizationFixtures(),
        .. ItemsFixtures(),
        .. VisualsCharacterizationFixtures(),
        .. VisualsFixtures(),
        .. AuditCharacterizationFixtures(),
        .. AuditFixtures(),
        .. ShellAuditCharacterizationFixtures(),
        .. ShellAuditFixtures(),
        .. CarouselFixtures(),
        .. CarouselCharacterizationFixtures(),
        .. LookCharacterizationFixtures(),
        .. LookReviewCharacterizationFixtures(),
        .. LookRound2CharacterizationFixtures(),
        .. LookRound3CharacterizationFixtures(),
        .. LookRound4CharacterizationFixtures(),
        .. LookRound5CharacterizationFixtures(),
        .. LookRound6CharacterizationFixtures(),
        .. LookRound7CharacterizationFixtures(),
        new("NavigationStack_PushReachesThePlatform_AndBackIsReported", NavigationBack),
        new("NavigationStack_BackDuringThePushAnimation_IsReported", NavigationBackDuringPush),
        new("NavigationStack_BackTheAppRefuses_PushesThePageBack", NavigationRefusedBack),
        new("NavigationStack_BackTheAppRefuses_WhenTheNavigatorIsIdle_PushesThePageBack", NavigationRefusedBackWhenIdle),
        new("NavigationStack_AcceptedBack_NeverPushesThePageAgain", NavigationAcceptedBackNoRepush),
        new("NavigationStack_TheNavigatorsOwnPops_AreNotReportedAsBack", NavigatorPopsAreSilent),
        new("NavigationStack_ModalIsShown_AndDismissalIsReported", ModalDismissal),
        new("NavigationStack_UnmountDuringNavigation_StopsIt", UnmountDuringNavigation),
        new("NavigationStack_TwoStacks_ShareTheWindowsModalStack", TwoStacksShareModals),
        new("NavigationStack_UnmountedStacksBuriedModal_IsDismissedWhenUncovered", BuriedModalCleanup),
        new("Tabs_UserSelection_IsReported_AndRefusalRestoresTheTab", TabsSelection),
        new("CollectionView_RealizesKeyedCells_AndReportsSelection", CollectionViewCells),
        new("Hooks_WindowSizeAndTheme_TrackTheWindow", WindowHooks),
        new("ErrorBoundary_CatchesAnEventHandlerFailure", ErrorBoundaryOnDevice),
        new("Effects_CleanupRunsWhenTheTreeUnmounts", EffectCleanup),
    ];

    // ── Views, both renderers ──────────────────────────────────────────────

    private sealed class CounterApp : Component
    {
        public override Element Render()
        {
            var (count, update) = UseReducer(0);
            return VStack(8,
                Label($"Count {count}").AutomationId("count"),
                Button("Add", () => update(n => n + 1)).AutomationId("add"));
        }
    }

    private static async Task Counter(FixtureContext c, ElementHandlerRegistry registry)
    {
        c.Show(Component<CounterApp>(), registry);
        var label = await Loaded<ILabel>(c, "count");
        Equal("Count 0", Platform.Text(label), "platform label");

        Platform.Tap(c.ById<IButton>("add"));
        Platform.Tap(c.ById<IButton>("add"));
        await c.WaitFor(() => Platform.Text(label) == "Count 2", "the label to show two taps");
        Check(ReferenceEquals(label, c.ById<ILabel>("count")), "the label was updated in place, not recreated");
    }

    private sealed class UppercaseEntry : Component
    {
        public override Element Render()
        {
            var (text, setText) = UseState("");
            // The app keeps its own rule: upper case, at most five characters.
            return VStack(
                Entry(text, value => setText(new string([.. value.ToUpperInvariant().Take(5)]))).AutomationId("entry"),
                Label($"State: {text}").AutomationId("state"));
        }
    }

    private static async Task ControlledEntry(FixtureContext c, ElementHandlerRegistry registry)
    {
        c.Show(Component<UppercaseEntry>(), registry);
        var entry = await Loaded<IEntry>(c, "entry");
        var state = c.ById<ILabel>("state");

        Platform.Type(entry, "hello world");
        await c.WaitFor(() => Platform.Text(state) == "State: HELLO", "the state to take the corrected value");
        await c.WaitFor(() => Platform.Text(entry) == "HELLO", "the field to show the corrected value");
        await c.Settle(200);
        Equal("HELLO", Platform.Text(entry), "the field after settling");
    }

    private static async Task GridGeometry(FixtureContext c, ElementHandlerRegistry registry)
    {
        c.Show(Grid("60,*", "100,*",
            BoxView(Colors.Red).AutomationId("cell00"),
            BoxView(Colors.Green).AutomationId("cell01").Grid(0, 1),
            BoxView(Colors.Blue).AutomationId("cell11").Grid(1, 1)), registry);
        var cell00 = await Loaded<IView>(c, "cell00");
        var cell01 = c.ById<IView>("cell01");
        var cell11 = c.ById<IView>("cell11");
        await c.WaitFor(() => Platform.Size(cell11).Height > 0, "the grid to be laid out");

        var page = c.Window.Page!;
        var grid = Platform.WindowFrame(c.Find<IGridLayout>());
        var origin = Platform.WindowFrame(cell00);
        var bottomRight = Platform.WindowFrame(cell11);
        // MAUI keeps content inside the safe area by insetting it within the layout's frame.
        var (insetLeft, insetRight) = Platform.SafeAreaHorizontal(cell00);
        var contentLeft = grid.X + insetLeft;
        var contentWidth = grid.Width - insetLeft - insetRight;
        Console.WriteLine($"#   grid frame ({grid.X:0.#}, {grid.Y:0.#}, {grid.Width:0.#} x {grid.Height:0.#}), safe-area left/right {insetLeft:0.#}/{insetRight:0.#}, page {page.Width:0.#} x {page.Height:0.#}");
        Close(contentLeft, origin.X, "column 0 starts at the content's left edge");
        Close(100, origin.Width, "column 0 width");
        Close(60, origin.Height, "row 0 height");
        // The star column takes the rest of the content width.
        Close(contentWidth - 100, Platform.Size(cell01).Width, "column 1 width", 2);
        Close(page.Width, grid.Width, "the grid spans the page", 2);
        Close(100, bottomRight.X - origin.X, "column 1 starts after column 0");
        Close(60, bottomRight.Y - origin.Y, "row 1 starts after row 0");
        Check(bottomRight.Height > 100, $"row 1 fills the rest of the page, got {bottomRight.Height:0.#}");
        // Content stays out of the status bar, notch and Dynamic Island.
        Console.WriteLine($"#   grid origin ({origin.X:0.#}, {origin.Y:0.#}), safe-area top {Platform.SafeAreaTop(cell00)?.ToString("0.#") ?? "n/a"}, page {page.Width:0.#}x{page.Height:0.#}");
        if (Platform.SafeAreaTop(cell00) is { } safeTop)
            Check(origin.Y >= safeTop - 0.5, $"the grid starts below the safe-area top ({safeTop:0.#}), at {origin.Y:0.#}");
    }

    private static async Task LabelWraps(FixtureContext c, ElementHandlerRegistry registry)
    {
        const string text = "A label with enough text to need several lines in a column two hundred units wide.";
        c.Show(VStack(Label("One line").AutomationId("short").Width(200), Label(text).AutomationId("long").Width(200)), registry);
        var single = await Loaded<ILabel>(c, "short");
        var wrapped = c.ById<ILabel>("long");
        await c.WaitFor(() => Platform.Size(single).Height > 0 && Platform.Size(wrapped).Height > 0, "the labels to be laid out");
        var lineHeight = Platform.Size(single).Height;
        var height = Platform.Size(wrapped).Height;
        Console.WriteLine($"#   one line {lineHeight:0.#}, long text {height:0.#}");
        // Controls labels wrap by default (LineBreakMode.WordWrap); direct labels must match.
        Check(height > lineHeight * 2.5, $"the long label wraps onto several lines: {height:0.#} vs one line {lineHeight:0.#}");
    }

    private sealed class Row : Component<string>
    {
        public override Element Render()
        {
            var (note, setNote) = UseState("");
            return HStack(8, Label(Props).AutomationId($"title-{Props}"), Entry(note, setNote).Width(120).AutomationId($"note-{Props}"));
        }
    }

    private sealed class Rows : Component<Action<Action>>
    {
        public override Element Render()
        {
            var (order, setOrder) = UseState<string[]>(["a", "b", "c"]);
            Props(() => setOrder([.. order.Reverse()]));
            return VStack([.. order.Select(key => Component<Row, string>(key).WithKey(key))]);
        }
    }

    private static async Task KeyedRows(FixtureContext c, ElementHandlerRegistry registry)
    {
        Action? reverse = null;
        c.Show(Component<Rows, Action<Action>>(r => reverse = r), registry);
        var note = await Loaded<IEntry>(c, "note-a");
        Platform.Type(note, "mine");
        await c.Settle(100);

        reverse!();
        await c.WaitFor(() => c.FindAll<ILabel>().Select(l => Platform.Text(l)).SequenceEqual(["c", "b", "a"]), "the rows to reverse");
        var after = c.ById<IEntry>("note-a");
        Check(ReferenceEquals(note, after), "row a kept its entry");
        Equal("mine", Platform.Text(after), "row a's note after the reorder");
    }

    // ── Navigation ─────────────────────────────────────────────────────────

    private sealed record StackProps(Action<RouteStack<string>> Report, Action<int>? OnPopped = null);

    private sealed class StackApp : Component<StackProps>
    {
        public override Element Render()
        {
            var stack = UseRouteStack("home");
            Props.Report(stack);
            return NavigationStack(
                stack.Items.Select(route => Page(route, Label($"{route} page")).WithKey(route)),
                depth =>
                {
                    Props.OnPopped?.Invoke(depth);
                    stack.PopTo(depth);
                });
        }
    }

    private static async Task NavigationBack(FixtureContext c)
    {
        RouteStack<string>? stack = null;
        var reported = new List<int>();
        c.Show(Component<StackApp, StackProps>(new(s => stack = s, reported.Add)));
        var navigation = await LoadedPage<NavigationPage>(c);

        stack!.Push("detail");
        await c.WaitFor(() => navigation.CurrentPage?.Title == "detail" && navigation.CurrentPage.IsLoaded, "detail to be shown");
        if (Platform.NavigationDepth(navigation) is { } depth) Equal(2, depth, "platform navigation depth");
        await c.Settle();

        // The path the platform back button takes.
        Check(navigation.SendBackButtonPressed(), "the back button was handled");
        await c.WaitFor(() => stack!.Count == 1, "the back navigation to reach the app's stack");
        Equal("1", string.Join(",", reported), "depths reported");
        // iOS updates MAUI's stack only when the pop animation ends.
        await c.WaitFor(() => navigation.Navigation.NavigationStack.Count == 1, "MAUI's stack to drop the page");
        await c.Settle();
        Equal(1, navigation.Navigation.NavigationStack.Count, "navigation stack after back");
        Equal("home", navigation.CurrentPage.Title, "current page after back");
        if (Platform.NavigationDepth(navigation) is { } after) Equal(1, after, "platform navigation depth after back");
    }

    private static async Task NavigationBackDuringPush(FixtureContext c)
    {
        RouteStack<string>? stack = null;
        var reported = new List<int>();
        c.Show(Component<StackApp, StackProps>(new(s => stack = s, reported.Add)));
        var navigation = await LoadedPage<NavigationPage>(c);
        await c.Settle();

        // Back as soon as the page is on the stack, while the push animation still runs.
        stack!.Push("detail");
        await c.WaitFor(() => navigation.Navigation.NavigationStack.Count == 2, "the push to start");
        navigation.SendBackButtonPressed();

        await c.WaitFor(() => stack!.Count == 1, "the back navigation to reach the app's stack");
        // MAUI can keep the page after raising Popped mid-push; the navigator then pops it.
        await c.WaitFor(() => navigation.Navigation.NavigationStack.Count == 1 && Probe(navigation).Idle, "the page to leave", 3000);
        await c.Settle(800);
        Equal("1", string.Join(",", reported), "depths reported");
        Equal(1, navigation.Navigation.NavigationStack.Count, "the page stays popped");
    }

    private static async Task NavigationRefusedBack(FixtureContext c)
    {
        c.Show(NavigationStack([Page("home", Label("home")).WithKey("home"), Page("detail", Label("detail")).WithKey("detail")]));
        var navigation = await LoadedPage<NavigationPage>(c);
        await c.WaitFor(() => navigation.Navigation.NavigationStack.Count == 2 && navigation.CurrentPage.IsLoaded, "both pages to be shown");
        await c.Settle();

        var popped = false;
        var pushedBack = false;
        navigation.Popped += (_, _) => popped = true;
        navigation.Pushed += (_, e) => pushedBack |= e.Page.Title == "detail";
        navigation.SendBackButtonPressed();
        await c.WaitFor(() => popped, "the platform pop");
        await c.WaitFor(() => pushedBack, "the declared page to be pushed back");
        await c.Settle(1000);
        Equal("home,detail", string.Join(",", navigation.Navigation.NavigationStack.Select(p => p.Title)), "the stack stays as declared");
    }

    private static StackNavigatorProbe Probe(NavigationPage page) => new((Microsoft.UI.Reactor.Maui.Handlers.ReactorNavigationPage)page);

    private sealed class StackNavigatorProbe(Microsoft.UI.Reactor.Maui.Handlers.ReactorNavigationPage page)
    {
        public bool Idle => page.Navigator.IsIdle;
    }

    private static async Task NavigationRefusedBackWhenIdle(FixtureContext c)
    {
        c.Show(NavigationStack([Page("home", Label("home")).WithKey("home"), Page("detail", Label("detail")).WithKey("detail")]));
        var navigation = await LoadedPage<NavigationPage>(c);
        await c.WaitFor(() => navigation.Navigation.NavigationStack.Count == 2 && navigation.CurrentPage.IsLoaded, "both pages to be shown");
        // Every navigation task, including the slow iOS push, has finished.
        await c.WaitFor(() => Probe(navigation).Idle, "the navigator to settle", 10_000);
        await c.Settle(500);

        var popped = false;
        navigation.Popped += (_, _) => popped = true;
        navigation.SendBackButtonPressed();
        await c.WaitFor(() => popped, "the platform pop");
        await c.Settle(2000);
        Equal("home,detail", string.Join(",", navigation.Navigation.NavigationStack.Select(p => p.Title)), "the stack stays as declared");
        Equal("detail", navigation.CurrentPage.Title, "current page");
    }

    private static async Task NavigationAcceptedBackNoRepush(FixtureContext c)
    {
        RouteStack<string>? stack = null;
        c.Show(Component<StackApp, StackProps>(new(s => stack = s)));
        var navigation = await LoadedPage<NavigationPage>(c);
        stack!.Push("detail");
        await c.WaitFor(() => navigation.CurrentPage?.Title == "detail" && navigation.CurrentPage.IsLoaded, "detail to be shown");
        await c.WaitFor(() => Probe(navigation).Idle, "the navigator to settle", 10_000);
        await c.Settle(500);

        var pushes = 0;
        navigation.Pushed += (_, _) => pushes++;
        navigation.SendBackButtonPressed();
        await c.WaitFor(() => stack!.Count == 1, "the back navigation to reach the app's stack");
        await c.WaitFor(() => navigation.Navigation.NavigationStack.Count == 1, "MAUI's stack to drop the page");
        await c.Settle(1500);
        Equal(0, pushes, "pages pushed after an accepted back navigation");
        Equal(1, navigation.Navigation.NavigationStack.Count, "navigation stack");
    }

    private static async Task NavigatorPopsAreSilent(FixtureContext c)
    {
        RouteStack<string>? stack = null;
        var reported = new List<int>();
        c.Show(Component<StackApp, StackProps>(new(s => stack = s, reported.Add)));
        var navigation = await LoadedPage<NavigationPage>(c);

        stack!.Push("one");
        stack.Push("two");
        await c.WaitFor(() => navigation.Navigation.NavigationStack.Count == 3 && navigation.CurrentPage.Title == "two", "three pages");
        await c.Settle();
        stack.PopToRoot();
        await c.WaitFor(() => navigation.Navigation.NavigationStack.Count == 1, "the pop to root");
        await c.Settle();
        Equal(0, reported.Count, "pops reported as user back navigations");
        Equal(1, stack.Count, "the app's stack");
    }

    private sealed class ModalApp : Component<Action<bool>>
    {
        public override Element Render()
        {
            var (open, setOpen) = UseState(false);
            Props(open);
            return NavigationStack([Page("home", Button("Open", () => setOpen(true)).AutomationId("open"))]) with
            {
                Modals = open ? [Page("sheet", Label("sheet"))] : [],
                OnModalDismissed = remaining => setOpen(remaining > 0),
            };
        }
    }

    private static async Task ModalDismissal(FixtureContext c)
    {
        var open = false;
        var presented = false;
        void Presented(object? sender, ModalPushedEventArgs e) => presented = true;
        Application.Current!.ModalPushed += Presented;
        try
        {
            c.Show(Component<ModalApp, Action<bool>>(value => open = value));
            var navigation = await LoadedPage<NavigationPage>(c);
            Platform.Tap(await Loaded<IButton>(c, "open"));
            // MAUI raises ModalPushed once the presentation has finished.
            await c.WaitFor(() => presented && navigation.Navigation.ModalStack.Count == 1, "the modal to be presented");
            await c.Settle();

            // What a swipe-down dismissal does.
            await navigation.Navigation.PopModalAsync();
            await c.WaitFor(() => !open, "the dismissal to reach the app");
            await c.WaitFor(() => navigation.Navigation.ModalStack.Count == 0, "the modal stack to empty");
            await c.Settle(800);
            Equal(0, navigation.Navigation.ModalStack.Count, "modal stack after dismissal");
            Check(!open, "the app still has the modal closed");
        }
        finally
        {
            Application.Current!.ModalPushed -= Presented;
        }
    }

    private static async Task UnmountDuringNavigation(FixtureContext c)
    {
        c.Show(NavigationStack([Page("home", Label("home")).WithKey("home")]));
        var navigation = await LoadedPage<NavigationPage>(c);
        await c.Settle();

        // Three pushes at once; unmount as soon as the first is under way.
        c.Render(NavigationStack([.. new[] { "home", "one", "two", "three" }.Select(r => Page(r, Label(r)).WithKey(r))]));
        await c.WaitFor(() => navigation.Navigation.NavigationStack.Count >= 2, "the first push");
        var depthAtUnmount = navigation.Navigation.NavigationStack.Count;
        // Keeps the navigation page in the window: replacing a window's page while a push is in
        // flight crashes MAUI on Android ("NavigationManager cannot be null here"; spec 065 §7).
        c.Unmount(keepWindowPage: true);
        await c.Settle(3000);
        Console.WriteLine($"#   depth at unmount {depthAtUnmount}, after {navigation.Navigation.NavigationStack.Count}");
        Check(navigation.Navigation.NavigationStack.Count <= Math.Max(depthAtUnmount, 2),
            $"no further pushes after unmount: depth {depthAtUnmount} at unmount, {navigation.Navigation.NavigationStack.Count} after");
    }

    private sealed class TwoStacks : Component<Action<int>>
    {
        public override Element Render()
        {
            var (tab, setTab) = UseState(0);
            return Tabs(tab, setTab,
                NavigationStack([Page("first", Label("first"))]) with
                {
                    Title = "First",
                    // Over full screen, so the tabs stay in the window beneath it (see TwoStacksShareModals).
                    Modals = [Page("sheet", Label("sheet")) with { ModalPresentationStyle = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.UIModalPresentationStyle.OverFullScreen }],
                    OnModalDismissed = Props,
                },
                NavigationStack([Page("second", Label("second"))]) with { Title = "Second" });
        }
    }

    private static async Task TwoStacksShareModals(FixtureContext c)
    {
        var dismissals = new List<int>();
        c.Show(Component<TwoStacks, Action<int>>(dismissals.Add));
        var tabs = await LoadedPage<TabbedPage>(c);
        await c.WaitFor(() => c.Window.Navigation.ModalStack.Count == 1, "the first stack's modal");
        await c.Settle();
        // Showing the second stack makes it reconcile (it loads), with the first stack's modal up. The
        // tab can load only while the tabs are in the window: a full-screen modal (MAUI's default)
        // takes the presenter's view out of the window once its presentation finishes, so a tab
        // selected after that never loads until the modal goes. The fixture used to race that
        // (selected 400 ms after the modal appeared; an iPad full run lost the race). The modal is
        // over full screen, which keeps the tabs in the window on iOS; Reactor doesn't write the
        // style on Mac Catalyst, and Android has no such removal.
        tabs.CurrentPage = tabs.Children[1];
        await c.WaitFor(() => tabs.Children[1].IsLoaded, "the second stack to load", timeoutMs: 20000);
        await c.Settle(2000);
        // The second stack declares no modals; it must leave the first stack's one alone.
        Equal(1, c.Window.Navigation.ModalStack.Count, "modals shown");
        Equal("sheet", c.Window.Navigation.ModalStack[0].Title, "the modal");
        Equal(0, dismissals.Count, "dismissals reported");
    }

    private sealed record BuriedState(bool KeepFirst, bool ShowSecondModal);

    private sealed class BuriedApp : Component<Action<Action<BuriedState>>>
    {
        public override Element Render()
        {
            var (state, setState) = UseState(new BuriedState(true, false));
            Props(setState);
            var second = NavigationStack([Page("second", Label("second"))]) with
            {
                Title = "Second",
                Modals = state.ShowSecondModal ? [Page("top", Label("top"))] : [],
            };
            return state.KeepFirst
                ? Tabs(0, null,
                    (NavigationStack([Page("first", Label("first"))]) with { Title = "First", Modals = [Page("buried", Label("buried"))] }).WithKey("first"),
                    second.WithKey("second"))
                : Tabs(0, null, second.WithKey("second"));
        }
    }

    private static async Task BuriedModalCleanup(FixtureContext c)
    {
        Action<BuriedState>? set = null;
        c.Show(Component<BuriedApp, Action<Action<BuriedState>>>(s => set = s));
        await LoadedPage<TabbedPage>(c);
        var modals = c.Window.Navigation;
        await c.WaitFor(() => modals.ModalStack.Count == 1, "the first stack's modal");
        await c.Settle();
        set!(new(true, true));
        await c.WaitFor(() => modals.ModalStack.Count == 2, "the second stack's modal on top");
        await c.Settle();

        // The first stack goes while its modal is buried under the second stack's.
        set(new(false, true));
        await c.Settle(1500);
        Equal("buried,top", string.Join(",", modals.ModalStack.Select(p => p.Title)), "modals while the first one is covered");

        // Uncovering it must dismiss it: its tree is gone.
        set(new(false, false));
        await c.WaitFor(() => modals.ModalStack.Count == 0, "both modals to be dismissed", 6000);
    }

    private sealed class TabsApp : Component<bool>
    {
        public override Element Render()
        {
            var (tab, setTab) = UseState(0);
            return Tabs(tab, index => { if (Props) setTab(index); },
                Page("One", Label("one")), Page("Two", Label("two")), Page("Three", Label("three")));
        }
    }

    private static async Task TabsSelection(FixtureContext c)
    {
        c.Show(Component<TabsApp, bool>(true));
        var tabs = await LoadedPage<TabbedPage>(c);
        Equal("One", tabs.CurrentPage.Title, "initial tab");

        tabs.CurrentPage = tabs.Children[2];
        await c.Settle();
        Equal("Three", tabs.CurrentPage.Title, "accepted selection");

        c.Render(Component<TabsApp, bool>(false));
        await c.Settle(100);
        tabs.CurrentPage = tabs.Children[1];
        await c.WaitFor(() => tabs.CurrentPage.Title == "Three", "the refused selection to be undone");
        await c.Settle();
        Equal("Three", tabs.CurrentPage.Title, "tab after a refused selection");
    }

    // ── Lists, hooks, errors, effects ───────────────────────────────────────

    private sealed class ListApp : Component<Action<string?>>
    {
        public override Element Render()
        {
            var (selected, setSelected) = UseState<string?>(null);
            Props(selected);
            var items = Enumerable.Range(1, 200).Select(i => $"Item {i}").ToArray();
            return CollectionView(items, item => item, item => Label(item).Padding(8).AutomationId(item)) with
            {
                SelectionMode = SelectionMode.Single,
                SelectedKey = selected,
                OnSelectionChanged = keys => setSelected(keys.Count > 0 ? keys[0] : null),
            };
        }
    }

    private static async Task CollectionViewCells(FixtureContext c)
    {
        string? selected = null;
        c.Show(Component<ListApp, Action<string?>>(value => selected = value));
        var first = await Loaded<ILabel>(c, "Item 1");
        Equal("Item 1", Platform.Text(first), "first cell's platform text");
        var realized = c.FindAll<ILabel>(l => ((IView)l).AutomationId?.StartsWith("Item ", StringComparison.Ordinal) == true).Count();
        Check(realized is > 1 and < 200, $"cells are virtualized: {realized} of 200 realized");

        var list = c.Find<CollectionView>();
        list.SelectedItem = list.ItemsSource.Cast<object>().ElementAt(2);
        await c.WaitFor(() => selected == "Item 3", "the selection to reach the app");
    }

    private sealed class HooksApp : Component
    {
        public override Element Render()
        {
            var size = this.UseWindowSize();
            var theme = this.UseAppTheme();
            return VStack(Label($"{size.Width:0}x{size.Height:0}").AutomationId("size"), Label(theme.ToString()).AutomationId("theme"));
        }
    }

    private static async Task WindowHooks(FixtureContext c)
    {
        var app = Application.Current!;
        var original = app.UserAppTheme;
        try
        {
            c.Show(Component<HooksApp>());
            var size = await Loaded<ILabel>(c, "size");
            await c.WaitFor(() => Platform.Text(size) == $"{c.Window.Width:0}x{c.Window.Height:0}" && c.Window.Width > 0, "the window size");

            if (DeviceInfo.Idiom == DeviceIdiom.Desktop)
            {
                // The subscription, not just the first read: resize and follow.
                var (width, height) = (c.Window.Width, c.Window.Height);
                try
                {
                    c.Window.Width = width - 160;
                    await c.WaitFor(() => Math.Abs(c.Window.Width - (width - 160)) < 1, "the window to resize");
                    await c.WaitFor(() => Platform.Text(size) == $"{c.Window.Width:0}x{c.Window.Height:0}", "the size label to follow the resize");
                }
                finally
                {
                    c.Window.Width = width;
                    c.Window.Height = height;
                }
            }

            var target = app.RequestedTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
            app.UserAppTheme = target;
            var theme = c.ById<ILabel>("theme");
            await c.WaitFor(() => Platform.Text(theme) == target.ToString(), $"the theme label to show {target}");
        }
        finally
        {
            app.UserAppTheme = original;
        }
    }

    private sealed class Faulty : Component
    {
        public override Element Render()
        {
            var (broken, setBroken) = UseState(false);
            if (broken) throw new InvalidOperationException("broken on purpose");
            return Button("Break", () => setBroken(true)).AutomationId("break");
        }
    }

    private static async Task ErrorBoundaryOnDevice(FixtureContext c)
    {
        c.Show(VStack(ErrorBoundary(Component<Faulty>(), error => Label($"Caught: {error.Message}").AutomationId("fallback"))));
        Platform.Tap(await Loaded<IButton>(c, "break"));
        var fallback = await Loaded<ILabel>(c, "fallback");
        Equal("Caught: broken on purpose", Platform.Text(fallback), "fallback text");
        Check(c.TryFind<IButton>(b => ((IView)b).AutomationId == "break") is null, "the failed subtree was removed");
    }

    private sealed class Ticker : Component<Action<string>>
    {
        public override Element Render()
        {
            UseEffect(() =>
            {
                Props("mounted");
                return () => Props("cleaned");
            });
            return Label("ticking").AutomationId("ticker");
        }
    }

    private static async Task EffectCleanup(FixtureContext c)
    {
        var log = new List<string>();
        c.Show(Component<Ticker, Action<string>>(log.Add));
        await Loaded<ILabel>(c, "ticker");
        await c.WaitFor(() => log.Contains("mounted"), "the effect to run");
        c.Unmount();
        Equal("mounted,cleaned", string.Join(",", log), "effect log");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static async Task<T> Loaded<T>(FixtureContext c, string automationId) where T : class, IView
    {
        T? found = null;
        await c.WaitFor(() => (found = c.TryFind<T>(v => v.AutomationId == automationId)) is { Handler.PlatformView: not null },
            $"'{automationId}' to get a platform view");
        return found!;
    }

    private static async Task<T> LoadedPage<T>(FixtureContext c) where T : Page
    {
        await c.WaitFor(() => c.Window.Page is T { IsLoaded: true }, $"the {typeof(T).Name} to load");
        return (T)c.Window.Page!;
    }
}
