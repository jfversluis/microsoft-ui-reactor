using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Reactor.Maui;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using static Reactor.Maui.DeviceTests.FixtureContext;
using MauiEntry = Microsoft.Maui.Controls.Entry;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiScrollView = Microsoft.Maui.Controls.ScrollView;
using MauiWebView = Microsoft.Maui.Controls.WebView;

namespace Reactor.Maui.DeviceTests;

/// <summary>
/// Spec 070 §2 (phase 4c-1): element refs and what they enable on the platform: focus, scrolling,
/// WebView commands and cancellation, and an entry's selection.
/// </summary>
public static partial class Fixtures
{
    private static IEnumerable<Fixture> ControlRefFixtures() =>
    [
        .. Renderers.SelectMany(r => new Fixture[]
        {
            new($"Ref_FocusFromAnEvent_FocusesThePlatformView [{r.Name}]", c => FocusFromEvent(c, r.Create(), r.Name)),
            new($"Ref_FocusOnMount_PerPlatform [{r.Name}]", c => FocusOnMount(c, r.Create(), r.Name)),
            new($"ScrollView_ScrollToFromARef_ReachesThePlatform [{r.Name}]", c => ScrollToPoint(c, r.Create())),
            new($"ScrollView_ScrollBars_ReachThePlatform [{r.Name}]", c => ScrollBars(c, r.Create())),
            new($"RefreshView_UserRefresh_IsReported_AndRefusalStopsTheSpinner [{r.Name}]", c => UserRefresh(c, r.Create())),
            new($"Entry_Selection_Characterization [{r.Name}]", c => SelectionCharacterization(c, r.Create(), r.Name)),
            new($"Entry_Selection_Sequences [{r.Name}]", c => SelectionSequences(c, r.Create(), r.Name)),
            new($"Entry_ReturnType_ReachesThePlatform [{r.Name}]", c => ReturnTypeReachesThePlatform(c, r.Create())),
        }),
        new("ScrollView_ScrollToAnElement_ThroughNestedDirectLayouts [direct]", ScrollToNestedDirect),
        new("ScrollView_ScrollToAnElement_EquivalentTrees_WithInsets", ScrollToEquivalentTrees),
        new("ScrollView_ScrollToAnElement_NotReady", ScrollToNotReady),
        new("ScrollView_ScrollToAnElement_Superseded", ScrollToSuperseded),
        new("WebView_Navigations_AreAttemptedAndCompleted", WebViewNavigations),
        new("WebView_CommandsThroughARef", WebViewCommands),
        new("WebView_DeclaredUrlCancelled_LoadsNothing", WebViewDeclaredUrlCancelled),
        new("WebView_RedeclaredUrl_DoesNothing", WebViewRedeclaredUrl),
        new("CollectionView_RefsInItemComponents_FollowTheirCells [controls]", CollectionViewItemRefs),
    ];

    private static void Note(string line) => Console.WriteLine("#   " + line);

    // ── Focus ───────────────────────────────────────────────────────────────

    private sealed record FocusProps(ElementRef<MauiEntry> Controls, ElementRef<IView> Any, List<bool> Results);

    private sealed class FocusApp : Component<FocusProps>
    {
        public override Element Render() => VStack(8,
            Entry("controls").Ref(Props.Controls).AutomationId("controls-entry"),
            Entry("any").Ref(Props.Any).AutomationId("any-entry"),
            Button("focus controls", () => Props.Results.Add(Props.Controls.Current?.Focus() ?? false)).AutomationId("focus-controls"),
            Button("focus any", () => Props.Results.Add(Props.Any.Current?.Focus() ?? false)).AutomationId("focus-any"));
    }

    private static async Task FocusFromEvent(FixtureContext c, ElementHandlerRegistry registry, string renderer)
    {
        var props = new FocusProps(TypedElementRef.Create<MauiEntry>(), TypedElementRef.Create<IView>(), []);
        c.Show(Component<FocusApp, FocusProps>(props), registry);
        var controls = await Loaded<IEntry>(c, "controls-entry");
        var any = await Loaded<IEntry>(c, "any-entry");
        Check(props.Controls.Current is MauiEntry, "an ElementRef<Entry> names a Controls Entry on every registry");
        Check(renderer == "direct" ? props.Any.Current is DirectEntry : props.Any.Current is MauiEntry,
            $"an ElementRef<IView> keeps the registry's own view ({props.Any.Current?.GetType().Name})");

        Platform.Tap(c.ById<IButton>("focus-controls"));
        await c.WaitFor(() => RefPlatform.IsFocused(controls), "the Controls entry to have platform focus");
        Platform.Tap(c.ById<IButton>("focus-any"));
        await c.WaitFor(() => RefPlatform.IsFocused(any), "the IView entry to have platform focus");
        Equal("True,True", string.Join(",", props.Results), "Focus() results");
        Check(!RefPlatform.IsFocused(controls), "focus moved off the first entry");
    }

    private sealed record MountFocusProps(ElementRef<IView> Ref, bool FromLoaded, List<string> Log);

    private sealed class MountFocusApp : Component<MountFocusProps>
    {
        public override Element Render()
        {
            UseEffect(() =>
            {
                if (!Props.FromLoaded)
                {
                    Props.Log.Add($"effect focus returned {Props.Ref.Current?.Focus()}");
                    return;
                }
                // The positive control: focus once the view is loaded.
                if (Props.Ref.Current is VisualElement visual)
                {
                    void OnLoaded(object? sender, EventArgs e)
                    {
                        visual.Loaded -= OnLoaded;
                        Props.Log.Add($"loaded focus returned {visual.Focus()}");
                    }
                    if (visual.IsLoaded) OnLoaded(null, EventArgs.Empty);
                    else visual.Loaded += OnLoaded;
                }
            }, Array.Empty<object>());
            return VStack(Label("focus target below"), Entry("x").Ref(Props.Ref).AutomationId("mount-entry"));
        }
    }

    private sealed class RequestFocusApp : Component
    {
        public override Element Render()
        {
            var (entry, requestFocus) = UseElementFocus();
            UseEffect(requestFocus, Array.Empty<object>());
            return VStack(Label("focus target below"), Entry("x").Ref(entry).AutomationId("request-entry"));
        }
    }

    // What focusing from the first effect of a newly shown tree does on each platform (spec 070 §2.6.2).
    // Recorded after measurement; null means "not measured on this platform yet": record only.
    private static bool? EffectFocusWorks(string renderer) => DeviceInfo.Platform == DevicePlatform.Android ? false
        : DeviceInfo.Platform == DevicePlatform.iOS ? true
        : DeviceInfo.Platform == DevicePlatform.MacCatalyst ? true
        : null;

    private static async Task FocusOnMount(FixtureContext c, ElementHandlerRegistry registry, string renderer)
    {
        // Two independent mounts, each with one focusable view, so one measurement can't help the other.
        var effectLog = new List<string>();
        var effectRef = TypedElementRef.Create<IView>();
        c.Show(Component<MountFocusApp, MountFocusProps>(new(effectRef, false, effectLog)), registry);
        var effectEntry = await Loaded<IEntry>(c, "mount-entry");
        await c.Settle(600);
        var effectFocused = RefPlatform.IsFocused(effectEntry);
        Note($"effect focus on {DeviceInfo.Platform} [{renderer}]: platform focused={effectFocused}; {string.Join("; ", effectLog)}");
        c.Unmount();
        await c.Settle(200);

        if (renderer == "controls")
        {
            var loadedLog = new List<string>();
            var loadedRef = TypedElementRef.Create<IView>();
            c.Show(Component<MountFocusApp, MountFocusProps>(new(loadedRef, true, loadedLog)), registry);
            var loadedEntry = await Loaded<IEntry>(c, "mount-entry");
            await c.WaitFor(() => RefPlatform.IsFocused(loadedEntry), "focus from Loaded (the positive control)");
            Note($"loaded focus: {string.Join("; ", loadedLog)}");
        }

        if (EffectFocusWorks(renderer) is { } expected)
            Equal(expected, effectFocused, $"focus from the first effect on {DeviceInfo.Platform} (spec 070 §2.15)");

        // Where the effect can't, UseElementFocus' RequestFocus waits until the view is ready.
        c.Unmount();
        await c.Settle(200);
        c.Show(Component<RequestFocusApp>(), registry);
        var requested = await Loaded<IEntry>(c, "request-entry");
        await c.WaitFor(() => RefPlatform.IsFocused(requested), "UseElementFocus' RequestFocus from the first effect");
    }

    // ── ScrollView ──────────────────────────────────────────────────────────

    private static VStackElement TallContent(int count = 60) =>
        VStack(0, [.. Enumerable.Range(0, count).Select(i => Label($"Row {i}").Height(40))]);

    private sealed record ScrollProps(ElementRef<MauiScrollView> Ref, List<(double X, double Y)> Scrolled, ScrollOrientation Orientation);

    private sealed class ScrollApp : Component<ScrollProps>
    {
        public override Element Render() =>
            (ScrollView(Props.Orientation == ScrollOrientation.Horizontal
                ? HStack(0, [.. Enumerable.Range(0, 60).Select(i => Label($"Col {i}").Width(80))])
                : TallContent()) with
            {
                Orientation = Props.Orientation,
                OnScrolled = (x, y) => Props.Scrolled.Add((x, y)),
            }).Ref(Props.Ref).AutomationId("scroll").Height(300);
    }

    private static async Task ScrollToPoint(FixtureContext c, ElementHandlerRegistry registry)
    {
        foreach (var orientation in new[] { ScrollOrientation.Vertical, ScrollOrientation.Horizontal })
        {
            var props = new ScrollProps(TypedElementRef.Create<MauiScrollView>(), [], orientation);
            c.Show(Component<ScrollApp, ScrollProps>(props), registry);
            var scroll = await Loaded<IScrollView>(c, "scroll");
            await c.WaitFor(() => ((MauiScrollView)scroll).Height > 0, "the scroll view to be laid out");
            var (x, y) = orientation == ScrollOrientation.Vertical ? (0, 300) : (300, 0);

            await props.Ref.Current!.ScrollToAsync(x, y, false);
            await c.Settle(200);

            var offset = RefPlatform.ScrollOffset(scroll);
            Close(orientation == ScrollOrientation.Vertical ? y : x, orientation == ScrollOrientation.Vertical ? offset.Y : offset.X,
                $"{orientation} platform offset");
            Check(props.Scrolled.Count > 0, $"{orientation}: OnScrolled reported the scroll");
            var last = props.Scrolled[^1];
            Close(orientation == ScrollOrientation.Vertical ? y : x, orientation == ScrollOrientation.Vertical ? last.Y : last.X,
                $"{orientation} reported offset");
            c.Unmount();
        }
    }

    private static async Task ScrollBars(FixtureContext c, ElementHandlerRegistry registry)
    {
        Element Tree(ScrollBarVisibility visibility) =>
            (ScrollView(TallContent()) with { VerticalScrollBarVisibility = visibility }).AutomationId("scroll").Height(300);
        c.Show(Tree(ScrollBarVisibility.Never), registry);
        var scroll = await Loaded<IScrollView>(c, "scroll");
        await c.Settle(100);
        Equal(false, RefPlatform.ShowsVerticalIndicator(scroll), "Never hides the indicator");

        c.Render(Tree(ScrollBarVisibility.Always));
        await c.Settle(100);
        Equal(true, RefPlatform.ShowsVerticalIndicator(scroll), "Always shows the indicator");
    }

    // A target in a direct Grid (second row, with a margin) in a direct VStack (with padding), far
    // from both ends of a tall scroll view.
    private static ScrollViewElement NestedTree(ElementRef<IView> target, string id) =>
        (ScrollView(VStack(0,
            TallContent(20),
            Grid("120,*", "*", Label("grid header").Height(120), Entry("target").Ref(target).Height(44).Margin(12, 7).Grid(1, 0)).Margin(6),
            TallContent(40)).Padding(10)) with { }).AutomationId(id).Height(320);

    private static async Task ScrollToNestedDirect(FixtureContext c)
    {
        var target = TypedElementRef.Create<IView>();
        c.Show(NestedTree(target, "scroll"), DirectHandlers.CreateRegistry());
        var scroll = (MauiScrollView)await Loaded<IScrollView>(c, "scroll");
        await c.WaitFor(() => target.Current is DirectView { IsArranged: true } && scroll.Height > 0, "the target to be laid out");
        Check(target.Current is DirectEntry, "the target stays a direct view");

        foreach (var position in new[] { ScrollToPosition.Start, ScrollToPosition.Center, ScrollToPosition.End })
        {
            await scroll.ScrollToAsync(0, 0, false);
            await c.Settle(100);
            var confirmed = await scroll.ScrollToAsync(target, position, false);
            await c.Settle(150);
            Check(confirmed, $"{position}: the bridge confirmed the scroll");
            // The target's place in the content, measured on the platform.
            var top = RefPlatform.ContentY(scroll, target.Current!);
            var height = RefPlatform.ViewHeight(target.Current!);
            var viewport = RefPlatform.ViewportHeight(scroll);
            var expected = position switch
            {
                ScrollToPosition.Start => top,
                ScrollToPosition.Center => top - viewport / 2 + height / 2,
                _ => top - viewport + height,
            };
            Close(expected, RefPlatform.ScrollOffset(scroll).Y, $"{position}: platform offset against the measured target");
        }

        // Why the bridge exists: MAUI's own element overload walks the Controls parents, which skip the
        // direct layouts. A Controls stand-in isn't available here (the target is direct), so the
        // equivalent-trees fixture compares with MAUI; this records the bridge's own geometry.
        Note($"target content top {RefPlatform.ContentY(scroll, target.Current!):0.#}");
    }

    private sealed record TreesProps(ElementRef<MauiEntry> ControlsTarget, ElementRef<IView> DirectTarget);

    private static async Task ScrollToEquivalentTrees(FixtureContext c)
    {
        // The same geometry twice, side by side: once from Controls layouts (a ref to a Controls entry in
        // a Controls-only tree), once from direct layouts. MAUI's own overload scrolls the first, the bridge
        // the second; the platform offsets must agree.
        var controlsTarget = TypedElementRef.Create<MauiEntry>();
        var directTarget = TypedElementRef.Create<IView>();
        c.Show(Grid("*", "*,*",
            NestedTreeControls(controlsTarget).Grid(0, 0),
            NestedTree(directTarget, "direct-scroll").Grid(0, 1)), DirectHandlers.CreateRegistry());
        var controlsScroll = (MauiScrollView)await Loaded<IScrollView>(c, "controls-scroll");
        var directScroll = (MauiScrollView)await Loaded<IScrollView>(c, "direct-scroll");
        await c.WaitFor(() => controlsTarget.Current is { Width: > 0 } && directTarget.Current is DirectView { IsArranged: true },
            "both targets to be laid out");
        Close(RefPlatform.ContentY(controlsScroll, controlsTarget.Current!), RefPlatform.ContentY(directScroll, directTarget.Current!),
            "the two trees place their targets alike");

        var insets = RefPlatform.SetContentInsets(controlsScroll, 50, 30) && RefPlatform.SetContentInsets(directScroll, 50, 30);
        Note($"nonzero content insets applied: {insets}");
        await c.Settle(150);

        foreach (var (position, start) in new (ScrollToPosition, double)[]
        {
            (ScrollToPosition.Start, 0), (ScrollToPosition.Center, 0), (ScrollToPosition.End, 0),
            (ScrollToPosition.MakeVisible, 0), (ScrollToPosition.MakeVisible, 1400), (ScrollToPosition.MakeVisible, 700),
        })
        {
            await controlsScroll.ScrollToAsync(0, start, false);
            await directScroll.ScrollToAsync(0, start, false);
            await c.Settle(100);
            await controlsScroll.ScrollToAsync(controlsTarget.Current!, position, false);
            await directScroll.ScrollToAsync(directTarget, position, false);
            await c.Settle(150);
            Close(RefPlatform.NativeOffsetY(controlsScroll), RefPlatform.NativeOffsetY(directScroll),
                $"{position} from {start}: the bridge's native offset against MAUI's own");
        }
    }

    // NestedTree's geometry from Controls layouts: OnTap forces each layout to its Controls view.
    private static ScrollViewElement NestedTreeControls(ElementRef<MauiEntry> target) =>
        (ScrollView(VStack(0,
            TallContent(20).OnTap(() => { }),
            Grid("120,*", "*", Label("grid header").Height(120).OnTap(() => { }), Entry("target").Ref(target).Height(44).Margin(12, 7).Grid(1, 0)).Margin(6).OnTap(() => { }),
            TallContent(40).OnTap(() => { })).Padding(10).OnTap(() => { })) with { }).AutomationId("controls-scroll").Height(320);

    private sealed record InsertProps(ElementRef<MauiScrollView> Scroll, ElementRef<IView> Target, bool Inserted);

    private static Element InsertTree(InsertProps props) =>
        ScrollView(VStack(0,
            TallContent(20),
            props.Inserted ? Entry("inserted").Ref(props.Target).AutomationId("inserted") : null,
            TallContent(20))).Ref(props.Scroll).AutomationId("scroll").Height(300);

    private static async Task ScrollToNotReady(FixtureContext c)
    {
        // A tree with no window has no handlers: nothing can scroll yet.
        var detached = new InsertProps(TypedElementRef.Create<MauiScrollView>(), TypedElementRef.Create<IView>(), true);
        var offscreen = new ReactorRoot(DirectHandlers.CreateRegistry(), new Microsoft.UI.Reactor.Maui.Hosting.MauiRenderScheduler(c.Window.Dispatcher), _ => { });
        offscreen.Render(InsertTree(detached));
        Equal(false, await detached.Scroll.Current!.ScrollToAsync(detached.Target, ScrollToPosition.Start, false), "a target without handlers");
        offscreen.Unmount();

        var props = new InsertProps(TypedElementRef.Create<MauiScrollView>(), TypedElementRef.Create<IView>(), false);
        c.Show(InsertTree(props), DirectHandlers.CreateRegistry());
        var scroll = (MauiScrollView)await Loaded<IScrollView>(c, "scroll");
        await c.Settle(200);

        // A target inserted by a render: the bridge answers false until the whole chain is laid out,
        // then scrolls to it. Whether a layout pass already ran in the render's own turn is the platform's.
        c.Render(InsertTree(props with { Inserted = true }));
        var sameTurn = await scroll.ScrollToAsync(props.Target, ScrollToPosition.Start, false);
        Note($"a target inserted in this turn on {DeviceInfo.Platform}: {(sameTurn ? "already laid out, scrolled" : "not laid out yet, false")}");
        if (!sameTurn)
        {
            Close(0, RefPlatform.ScrollOffset(scroll).Y, "nothing scrolled while not laid out");
            await c.WaitFor(() => props.Target.Current is DirectView { IsArranged: true }, "the inserted target to be laid out");
            await c.Settle(100);
            Equal(true, await scroll.ScrollToAsync(props.Target, ScrollToPosition.Start, false), "after the layout pass");
        }
        await c.Settle(100);
        Close(RefPlatform.ContentY(scroll, props.Target.Current!), RefPlatform.ScrollOffset(scroll).Y, "the offset");

        var elsewhere = TypedElementRef.Create<IView>();
        c.Render(VStack(InsertTree(props with { Inserted = true }), Label("outside").Ref(elsewhere)));
        await c.Settle(100);
        var threw = false;
        try
        {
            _ = scroll.ScrollToAsync(elsewhere, ScrollToPosition.Start, false);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }
        Check(threw, "a target outside the scroll view throws");
    }

    private static async Task ScrollToSuperseded(FixtureContext c)
    {
        var first = TypedElementRef.Create<IView>();
        var second = TypedElementRef.Create<IView>();
        c.Show(ScrollView(VStack(0, TallContent(30), Label("first").Ref(first).Height(40), TallContent(30), Label("second").Ref(second).Height(40), TallContent(30)))
            .AutomationId("scroll").Height(300), MauiHandlers.CreateRegistry());
        var scroll = (ReactorScrollView)await Loaded<IScrollView>(c, "scroll");
        await c.WaitFor(() => second.Current is VisualElement { Width: > 0 }, "the targets to be laid out");

        // Characterization: how many finishes two overlapping animated MAUI requests produce.
        var before = scroll.FinishCount;
        _ = scroll.ScrollToAsync(0, 600, true);
        await Task.Delay(50);
        _ = scroll.ScrollToAsync(0, 1500, true);
        await c.Settle(2000);
        Note($"overlapping animated requests on {DeviceInfo.Platform}: {scroll.FinishCount - before} finish(es); expected by the bridge: {(ScrollBridge.FinishesAccumulate ? 2 : 1)}");
        await scroll.ScrollToAsync(0, 0, false);
        await c.Settle(200);

        var a = scroll.ScrollToAsync(first, ScrollToPosition.Start, true);
        await Task.Delay(50);
        var b = scroll.ScrollToAsync(second, ScrollToPosition.Start, true);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await c.WaitFor(() => a.IsCompleted, "the superseded scroll to complete", 1000);
        Equal(false, a.Result, "the superseded scroll's result");
        var target = scroll.GetScrollPositionForElement((VisualElement)second.Current!, ScrollToPosition.Start);
        await c.WaitFor(() => b.IsCompleted, "the second scroll to complete", 3000);
        Equal(true, b.Result, "the second scroll's result");
        Close(target.Y, scroll.ScrollY, "the final offset is the second's");
        Note($"superseded after {clock.ElapsedMilliseconds} ms");
    }

    // ── RefreshView ─────────────────────────────────────────────────────────

    private sealed record RefreshProps(bool Accept, List<string> Log);

    private sealed class RefreshApp : Component<RefreshProps>
    {
        public override Element Render()
        {
            var (refreshing, setRefreshing) = UseState(false);
            return RefreshView(ScrollView(TallContent(10)), refreshing, () =>
            {
                Props.Log.Add("refresh");
                if (!Props.Accept) return;
                setRefreshing(true);
                _ = Task.Delay(400).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() => setRefreshing(false)));
            }).AutomationId("refresh");
        }
    }

    private static async Task UserRefresh(FixtureContext c, ElementHandlerRegistry registry)
    {
        foreach (var accept in new[] { false, true })
        {
            var log = new List<string>();
            c.Show(Component<RefreshApp, RefreshProps>(new(accept, log)), registry);
            var refresh = await Loaded<IRefreshView>(c, "refresh");
            await c.Settle(200);
            var how = await RefPlatform.PullToRefresh(refresh, c);
            if (how is null) throw new SkipException($"this platform's refresh view offers no pull-to-refresh to trigger ({DeviceInfo.Platform})");
            Note($"refresh triggered through {how}");
            await c.WaitFor(() => log.Count == 1, "the refresh to be reported");
            if (accept) await c.WaitFor(() => RefPlatform.IsSpinning(refresh) == true, "the spinner to show while the app refreshes");
            await c.WaitFor(() => RefPlatform.IsSpinning(refresh) == false, accept ? "the spinner to stop when the app is done" : "a refused refresh to stop the spinner");
            await c.Settle();
            Equal("refresh", string.Join(",", log), $"accept={accept}: exactly one report");
            c.Unmount();
        }
    }

    // ── WebView ─────────────────────────────────────────────────────────────

    private sealed class WebLog
    {
        public List<string> Attempts { get; } = [];
        public List<string> Completions { get; } = [];
        public Func<string, bool> Refuse { get; set; } = _ => false;
    }

    private static string PageOf(string url) => url.Contains("page2", StringComparison.Ordinal) ? "page2" : url.Contains("page1", StringComparison.Ordinal) ? "page1" : url;

    private static WebViewElement Web(string url, WebLog log, ElementRef<MauiWebView> web) =>
        (WebView(url) with
        {
            OnNavigating = e =>
            {
                log.Attempts.Add($"{e.NavigationEvent} {PageOf(e.Url)}");
                if (log.Refuse(e.Url)) e.Cancel = true;
            },
            OnNavigated = e => log.Completions.Add($"{PageOf(e.Url)} {e.Result}"),
        }).Ref(web).AutomationId("web").Height(300);

    // Polls a script's value without blocking the UI thread, which the web view needs to answer.
    private static async Task WaitForJs(FixtureContext c, ElementRef<MauiWebView> web, string script, string expected, string what, int timeoutMs)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var last = "";
        while ((last = await Js(web, script)) != expected)
        {
            if (clock.ElapsedMilliseconds > timeoutMs) throw new CheckFailedException($"timed out after {timeoutMs} ms waiting for {what} (last: {last})");
            await Task.Delay(50);
        }
    }

    private static async Task<string> Js(ElementRef<MauiWebView> web, string script) =>
        (await web.Current!.EvaluateJavaScriptAsync(script) ?? "").Trim('"');

    private static async Task<MauiWebView> ShowPage1(FixtureContext c, WebLog log, ElementRef<MauiWebView> web)
    {
        c.Show(Web("page1.html", log, web));
        await Loaded<IWebView>(c, "web");
        await c.WaitFor(() => log.Completions.Contains("page1 Success"), "page 1 to load", 10000);
        await WaitForJs(c, web, "document.title", "page1", "page 1's title", 5000);
        return web.Current!;
    }

    private static async Task WebViewNavigations(FixtureContext c)
    {
        var log = new WebLog();
        var web = TypedElementRef.Create<MauiWebView>();
        await ShowPage1(c, log, web);

        // Positive control: an allowed link from page 1 to page 2.
        await Js(web, "document.getElementById('next').click(); 'ok'");
        await c.WaitFor(() => log.Completions.Contains("page2 Success"), "page 2 to load", 10000);
        await WaitForJs(c, web, "document.title", "page2", "page 2's title", 5000);
        Note($"after the link: {RefPlatform.History(web.Current!)}");

        // History read in OnNavigated is current. A link clicked by script has no user gesture, which
        // Android's WebView treats as a skippable history entry (CanGoBack stays false), so the history
        // comes from a declared source, which the platform loads as the app's own navigation.
        c.Unmount();
        await c.Settle(200);
        await ShowPage1(c, log, web);
        var canGoBack = new List<bool>();
        c.Render((Web("page2.html", log, web) with { OnNavigated = e =>
        {
            log.Completions.Add($"{PageOf(e.Url)} {e.Result}");
            canGoBack.Add(web.Current?.CanGoBack ?? false);
        } }).Ref(web));
        await WaitForJs(c, web, "document.title", "page2", "page 2 declared", 10000);
        await c.WaitFor(() => canGoBack.Count >= 1, "the declared load to complete", 5000);
        Note($"after the declared load: CanGoBack in OnNavigated [{string.Join(", ", canGoBack)}], {RefPlatform.History(web.Current!)}");
        Check(canGoBack[^1], "CanGoBack read in OnNavigated after a declared navigation");

        // Refused, each started another way. What the platform asks, when, and what it reports.
        log.Refuse = url => true;
        foreach (var (name, start, keepsPage) in new (string, Func<Task>, bool)[]
        {
            ("link", () => Js(web, "document.getElementById('back').click(); 'ok'"), true),
            ("script", () => Js(web, "location.href = 'page1.html'; 'ok'"), true),
            ("reload", () => { web.Current!.Reload(); return Task.CompletedTask; }, DeviceInfo.Platform != DevicePlatform.Android),
            ("back", () => { web.Current!.GoBack(); return Task.CompletedTask; }, DeviceInfo.Platform != DevicePlatform.Android),
        })
        {
            var (attempts, completions) = (log.Attempts.Count, log.Completions.Count);
            await Js(web, "window.reactorMarker = 'kept'; 'ok'");
            await start();
            await c.Settle(1200);
            var asked = log.Attempts.Skip(attempts).ToList();
            var done = log.Completions.Skip(completions).ToList();
            var marker = await Js(web, "window.reactorMarker || 'gone'");
            var title = await Js(web, "document.title");
            Note($"{name}: asked [{string.Join(", ", asked)}], completed [{string.Join(", ", done)}], page {title}, marker {marker}");
            Check(!done.Any(d => d.EndsWith("Success", StringComparison.Ordinal)), $"{name}: a refused navigation never completes with Success");
            Check(done.All(d => d.EndsWith("Cancel", StringComparison.Ordinal)), $"{name}: any completion reported carries Cancel");
            if (keepsPage) Equal("page2 kept", $"{title} {marker}", $"{name}: the previous page stays (spec 070 §2.7)");
            if (title != "page2" && name != "back")
            {
                // A late cancel left another page: start the next case from page 2 again.
                log.Refuse = _ => false;
                c.Render(Web("page1.html", log, web));
                await Js(web, "location.href = 'page2.html'; 'ok'");
                await WaitForJs(c, web, "document.title", "page2", "page 2 again", 10000);
                log.Refuse = _ => true;
            }
        }
    }

    private static async Task WebViewCommands(FixtureContext c)
    {
        var log = new WebLog();
        var web = TypedElementRef.Create<MauiWebView>();
        await ShowPage1(c, log, web);
        // Declared, so Android keeps page 1 as a back entry (a script click has no user gesture).
        c.Render(Web("page2.html", log, web));
        await WaitForJs(c, web, "document.title", "page2", "page 2", 10000);
        await c.WaitFor(() => web.Current!.CanGoBack, "CanGoBack", 5000);

        web.Current!.GoBack();
        await WaitForJs(c, web, "document.title", "page1", "GoBack to page 1", 8000);
        await c.WaitFor(() => web.Current.CanGoForward, "CanGoForward after GoBack", 5000);
        web.Current.GoForward();
        await WaitForJs(c, web, "document.title", "page2", "GoForward to page 2", 8000);

        // A reload makes a new document: script state set before it is gone afterwards.
        await Js(web, "window.reactorMarker = 'before reload'; 'ok'");
        web.Current.Reload();
        await WaitForJs(c, web, "window.reactorMarker || 'reloaded'", "reloaded", "Reload to load page 2 again", 8000);
        Equal("page2", await Js(web, "document.title"), "the reloaded page");
        Equal("42", await Js(web, "String(6 * 7)"), "EvaluateJavaScriptAsync's value");
    }

    private static async Task WebViewDeclaredUrlCancelled(FixtureContext c)
    {
        var log = new WebLog();
        var web = TypedElementRef.Create<MauiWebView>();
        await ShowPage1(c, log, web);
        await Js(web, "window.reactorMarker = 'kept'; 'ok'");

        log.Refuse = url => url.Contains("page2", StringComparison.Ordinal);
        var completions = log.Completions.Count;
        c.Render(Web("page2.html", log, web));
        await c.Settle(1500);
        Check(log.Attempts.Any(a => a.EndsWith("page2", StringComparison.Ordinal)), "the declared page was asked about");
        Equal("page1 kept", $"{await Js(web, "document.title")} {await Js(web, "window.reactorMarker || 'gone'")}",
            "no load began: page 1 and its script state stay");
        Check(!log.Completions.Skip(completions).Any(d => d == "page2 Success"), "page 2 never completed");

        // Positive control: the same change, allowed.
        log.Refuse = _ => false;
        c.Render(Web("page1.html", log, web));
        await c.Settle(300);
        c.Render(Web("page2.html", log, web));
        await c.WaitFor(() => log.Completions.Count(d => d == "page2 Success") == 1, "the allowed page 2 to load once", 10000);
    }

    private static async Task WebViewRedeclaredUrl(FixtureContext c)
    {
        var log = new WebLog();
        var web = TypedElementRef.Create<MauiWebView>();
        await ShowPage1(c, log, web);
        await Js(web, "window.reactorMarker = 'kept'; 'ok'");
        var completions = log.Completions.Count;

        c.Render(Web("page1.html", log, web));
        await c.Settle(800);
        Equal("kept", await Js(web, "window.reactorMarker || 'gone'"), "re-declaring the same Url loads nothing");
        Equal(completions, log.Completions.Count, "no completion");

        c.Render(Web("page2.html", log, web));
        await c.WaitFor(() => log.Completions.Count(d => d == "page2 Success") == 1, "a changed Url to load once", 10000);
    }

    // ── Entry selection ─────────────────────────────────────────────────────

    private sealed class SelectionLog
    {
        public List<string> Calls { get; } = [];
        public Action<(string Text, int? Cursor, int? Length)>? Set { get; set; }
        public (int, int)? AtCommit { get; set; }
    }

    private sealed record SelectionProps(SelectionLog Log, bool Refuse);

    private sealed class SelectionApp : Component<SelectionProps>
    {
        public override Element Render()
        {
            var (state, setState) = UseState<(string Text, int? Cursor, int? Length)>(("0123456789", null, null));
            Props.Log.Set = setState;
            var entry = UseElementRef<IView>();
            // What the platform shows right after the render committed, before anything deferred runs.
            UseEffect(() => Props.Log.AtCommit = entry.Current is { } view && PlatformSelection.Read(view) is { } s ? (s.Cursor, s.Length) : null, state);
            return VStack(
                (Entry(state.Text, text =>
                {
                    Props.Log.Calls.Add($"text {text}");
                    if (!Props.Refuse) setState(state with { Text = text });
                }) with
                {
                    CursorPosition = state.Cursor,
                    SelectionLength = state.Length,
                    OnSelectionChanged = (_, cursor, length) => Props.Log.Calls.Add($"selection {cursor},{length}"),
                }).Ref(entry),
                Label("other"));
        }
    }

    private static async Task<IEntry> ShowSelection(FixtureContext c, ElementHandlerRegistry registry, SelectionProps props)
    {
        c.Show(Component<SelectionApp, SelectionProps>(props), registry);
        await c.WaitFor(() => c.TryFind<IEntry>() is { Handler.PlatformView: not null }, "the entry to load");
        await c.Settle(300);
        props.Log.Calls.Clear();
        return c.Find<IEntry>();
    }

    private static async Task Settled(FixtureContext c)
    {
        await c.Settle(400);
        await MainThread.InvokeOnMainThreadAsync(() => { });
    }

    private static async Task SelectionCharacterization(FixtureContext c, ElementHandlerRegistry registry, string renderer)
    {
        foreach (var focused in new[] { false, true })
        {
            var props = new SelectionProps(new(), false);
            var entry = await ShowSelection(c, registry, props);
            if (focused)
            {
                entry.Focus();
                await c.WaitFor(() => RefPlatform.IsFocused(entry), "the entry to be focused");
                await Settled(c);
                props.Log.Calls.Clear();
            }
            var cases = new (string Name, Action Write, (int, int) Expected)[]
            {
                ("cursor only", () => props.Log.Set!(("0123456789", 3, null)), (3, 0)),
                ("length only", () => props.Log.Set!(("0123456789", 3, 4)), (3, 4)),
                ("both", () => props.Log.Set!(("0123456789", 1, 2)), (1, 2)),
                ("past the end", () => props.Log.Set!(("0123456789", 40, 5)), (10, 0)),
                ("two in one batch", () => { props.Log.Set!(("0123456789", 2, 0)); props.Log.Set!(("0123456789", 5, 1)); }, (5, 1)),
                ("text and selection", () => props.Log.Set!(("abcdefghijklmnop", 12, 2)), (12, 2)),
            };
            foreach (var (name, write, expected) in cases)
            {
                props.Log.Calls.Clear();
                props.Log.AtCommit = null;
                write();
                await Settled(c);
                var immediately = props.Log.AtCommit;
                var settled = RefPlatform.Selection(entry);
                Note($"{renderer} focused={focused} {name}: at commit {immediately}, settled {settled}, late={immediately != settled}, calls [{string.Join("; ", props.Log.Calls.Where(x => x.StartsWith("selection", StringComparison.Ordinal)))}]");
                Equal(expected, settled, $"focused={focused} {name}: platform selection");
                Equal("", string.Join("; ", props.Log.Calls.Where(x => x.StartsWith("selection", StringComparison.Ordinal))), $"focused={focused} {name}: a programmatic write reports nothing");
            }

            // Consecutive renders.
            props.Log.Set!(("abcdefghijklmnop", 4, 0));
            await Task.Yield();
            props.Log.Set!(("abcdefghijklmnop", 6, 0));
            await Settled(c);
            Equal((6, 0), RefPlatform.Selection(entry), $"focused={focused} consecutive renders: platform selection");
            Equal("", string.Join("; ", props.Log.Calls.Where(x => x.StartsWith("selection", StringComparison.Ordinal))), $"focused={focused} consecutive renders report nothing");

            // Text changing between a write and its execution: the user's text replaces the declared one
            // right after the render (Android runs a focused SetSelection later, clamped to the text then).
            props.Log.Calls.Clear();
            props.Log.Set!(("abcdefghijklmnop", 14, 0));
            RefPlatform.SetText(entry, "abc");
            await Settled(c);
            var final = RefPlatform.Selection(entry);
            var selections = props.Log.Calls.Where(x => x.StartsWith("selection", StringComparison.Ordinal)).ToList();
            Note($"{renderer} focused={focused} text shrinks before the write runs: final {final}, calls [{string.Join("; ", props.Log.Calls)}]");
            if (selections.Count > 0) Equal($"selection {final.Item1},{final.Item2}", selections[^1], "the last report is the final selection");
            c.Unmount();
            await c.Settle(100);
        }
    }

    private static async Task SelectionSequences(FixtureContext c, ElementHandlerRegistry registry, string renderer)
    {
        var props = new SelectionProps(new(), false);
        var entry = await ShowSelection(c, registry, props);
        entry.Focus();
        await c.WaitFor(() => RefPlatform.IsFocused(entry), "the entry to be focused");
        await Settled(c);
        props.Log.Calls.Clear();

        // A user's selection, set on the platform view as a tap or a drag does.
        RefPlatform.Select(entry, 2, 3);
        await Settled(c);
        Equal("selection 2,3", string.Join("; ", props.Log.Calls),
            $"a user selection: exactly one report, with both values (virtual {((ITextInput)entry).CursorPosition},{((ITextInput)entry).SelectionLength}, platform {RefPlatform.Selection(entry)})");

        // Typing a character: the text, then what this platform reports for the caret.
        props.Log.Calls.Clear();
        RefPlatform.Select(entry, 10, 0);
        await Settled(c);
        props.Log.Calls.Clear();
        RefPlatform.TypeAtCaret(entry, "X");
        await Settled(c);
        Note($"{renderer} typing: [{string.Join("; ", props.Log.Calls)}]");
        Check(props.Log.Calls.FirstOrDefault() == "text 0123456789X", $"typing reports the text first ({string.Join("; ", props.Log.Calls)})");
        var final = RefPlatform.Selection(entry);
        var selections = props.Log.Calls.Where(x => x.StartsWith("selection", StringComparison.Ordinal)).ToList();
        if (selections.Count > 0) Equal($"selection {final.Item1},{final.Item2}", selections[^1], "typing: the last report is the final selection");
        props.Log.Calls.Clear();
        await Settled(c);
        Equal("", string.Join("; ", props.Log.Calls), "nothing more after settling");
        c.Unmount();

        // Refused typing: the restore puts the text back; the caret it moved is reported, if at all, as it is.
        var refusing = new SelectionProps(new(), true);
        entry = await ShowSelection(c, registry, refusing);
        entry.Focus();
        await c.WaitFor(() => RefPlatform.IsFocused(entry), "the entry to be focused");
        RefPlatform.Select(entry, 10, 0);
        await Settled(c);
        refusing.Log.Calls.Clear();
        RefPlatform.TypeAtCaret(entry, "X");
        await Settled(c);
        Note($"{renderer} refused typing: [{string.Join("; ", refusing.Log.Calls)}], final {RefPlatform.Selection(entry)}, text {Platform.Text(entry)}");
        Equal("0123456789", Platform.Text(entry), "the refused text is restored");
        final = RefPlatform.Selection(entry);
        selections = refusing.Log.Calls.Where(x => x.StartsWith("selection", StringComparison.Ordinal)).ToList();
        if (selections.Count > 0) Equal($"selection {final.Item1},{final.Item2}", selections[^1], "refused typing: the last report is the final selection");
        refusing.Log.Calls.Clear();
        await Settled(c);
        Equal("", string.Join("; ", refusing.Log.Calls), "refused typing: nothing more after settling");
    }

    private static async Task ReturnTypeReachesThePlatform(FixtureContext c, ElementHandlerRegistry registry)
    {
        Element Tree(ReturnType? type) => Entry("x") with { ReturnType = type };
        c.Show(Tree(ReturnType.Search), registry);
        await c.WaitFor(() => c.TryFind<IEntry>() is { Handler.PlatformView: not null }, "the entry to load");
        var entry = c.Find<IEntry>();
        Equal("Search", RefPlatform.ReturnKey(entry), "Search");
        c.Render(Tree(ReturnType.Next));
        await c.Settle(100);
        Equal("Next", RefPlatform.ReturnKey(entry), "Next");
        c.Render(Tree(null));
        await c.Settle(100);
        // MAUI maps Default to Android's Done action (ImeActionExtensions).
        Equal(DeviceInfo.Platform == DevicePlatform.Android ? "Done" : "Default", RefPlatform.ReturnKey(entry), "cleared to Default");
    }

    // ── CollectionView ──────────────────────────────────────────────────────

    private sealed class ItemTree
    {
        public required string Id { get; init; }
        public required ElementRef<IView> Ref { get; init; }
        public object? Cell { get; set; }
        public bool Unmounted { get; set; }
    }

    private sealed record ItemRefProps(string Id, List<ItemTree> Trees);

    private sealed class ItemRefRow : Component<ItemRefProps>
    {
        public override Element Render()
        {
            var r = UseElementRef<IView>();
            var tree = UseMemo(() =>
            {
                var t = new ItemTree { Id = Props.Id, Ref = r };
                Props.Trees.Add(t);
                return t;
            }, Array.Empty<object>());
            UseEffect(() =>
            {
                for (var e = (r.Current as Microsoft.Maui.Controls.Element)?.Parent; e is not null; e = e.Parent)
                    if (e is ContentView { BindingContext: ItemBox })
                    {
                        tree.Cell = e;
                        break;
                    }
                return () => tree.Unmounted = true;
            }, Array.Empty<object>());
            return Label(Props.Id).Ref(r).Height(44);
        }
    }

    private static async Task CollectionViewItemRefs(FixtureContext c)
    {
        var trees = new List<ItemTree>();
        var list = TypedElementRef.Create<CollectionView>();
        var items = Enumerable.Range(0, 300).Select(i => $"item {i}").ToArray();
        c.Show(CollectionView(items, i => i, i => Component<ItemRefRow, ItemRefProps>(new(i, trees))).Ref(list).AutomationId("list"));
        await c.WaitFor(() => trees.Count > 0, "the first cells");

        foreach (var index in new[] { 60, 140, 220, 299 })
        {
            list.Current!.ScrollTo(index, position: ScrollToPosition.Start, animate: false);
            await c.Settle(400);
        }
        // Recycling happened: one cell instance rendered at least two items, and item trees unmounted.
        var reused = trees.Where(t => t.Cell is not null).GroupBy(t => t.Cell!, ReferenceEqualityComparer.Instance)
            .Count(g => g.Select(t => t.Id).Distinct().Count() >= 2);
        var unmounted = trees.Count(t => t.Unmounted);
        Note($"item trees {trees.Count}, recycled cells {reused}, unmounted trees {unmounted}");
        Check(reused >= 1, "at least one cell was recycled for another item");
        Check(unmounted >= 1, "at least one item tree unmounted");

        foreach (var tree in trees)
        {
            if (tree.Unmounted) Check(tree.Ref.Current is null, $"{tree.Id}: an unmounted tree's ref is released");
            else Equal(tree.Id, (tree.Ref.Current as MauiLabel)?.Text, $"{tree.Id}: a live tree's ref names the label showing its item");
        }
    }
}
