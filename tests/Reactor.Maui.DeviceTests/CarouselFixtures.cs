using System.Diagnostics;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Maui;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using static Reactor.Maui.DeviceTests.FixtureContext;
using MauiCarouselView = Microsoft.Maui.Controls.CarouselView;
using MauiIndicatorView = Microsoft.Maui.Controls.IndicatorView;
#if IOS || MACCATALYST
using UIKitView = UIKit.UIView;
#elif ANDROID
using UIKitView = Android.Views.View;
#endif

namespace Reactor.Maui.DeviceTests;

/// <summary>Spec 070 §5 (4c-4): the carousel's controlled position and the indicator, on the platforms.</summary>
public static partial class Fixtures
{
    private static IEnumerable<Fixture> CarouselFixtures() =>
    [
        // The hard gates (§5.9), first: the items barrier, rest detection, the shown item's geometry.
        new("Gate_CarouselView_ItemsBarrier", c => CarouselItemsChangeFixture(c, MauiHandlers.CreateRegistry()), 120),
        new("Gate_CarouselView_RestDetection", CarouselRestDetection, 60),
        new("Gate_CarouselView_ShownGeometry", CarouselShownGeometry, 90),
        new("Characterize_Carousel_CellRerender", CarouselCellRerender, 90, OnDemand: true),
        .. Renderers.SelectMany(r => new Fixture[]
        {
            new($"CarouselView_Swipe [{r.Name}]", c => CarouselSwipe(c, r.Create()), 90),
            new($"CarouselView_LongHold [{r.Name}]", c => CarouselLongHoldFixture(c, r.Create()), 90),
            new($"CarouselView_DeclaredChange [{r.Name}]", c => CarouselDeclaredChange(c, r.Create()), 60),
            new($"CarouselView_ItemsChange [{r.Name}]", c => CarouselItemsChangeFixture(c, r.Create()), 120),
            new($"CarouselView_Accessibility [{r.Name}]", c => CarouselAccessibilityFixture(c, r.Create()), 60),
            new($"CarouselView_RefScrollTo [{r.Name}]", c => CarouselRefScrollTo(c, r.Create()), 60),
            new($"CarouselView_Loop [{r.Name}]", c => CarouselLoop(c, r.Create()), 60),
            new($"CarouselView_Fling [{r.Name}]", c => CarouselFling(c, r.Create()), 90),
            new($"CarouselView_Geometry [{r.Name}]", c => CarouselGeometry(c, r.Create()), 60),
            new($"IndicatorView_Controlled [{r.Name}]", c => IndicatorControlled(c, r.Create()), 60),
        }),
    ];

    private static readonly string[] Pages = ["p0", "p1", "p2", "p3", "p4"];

    /// <summary>The app's state, reachable from the fixture.</summary>
    private sealed class CarouselState
    {
        public readonly List<string> Reports = [];
        public Action<int> SetPosition = null!;
        public Action<string[]> SetItems = null!;
        public Action<int> Tick = null!;
        public int Renders;
        public bool Accept;
        public ElementRef<MauiCarouselView> Ref = TypedElementRef.Create<MauiCarouselView>();
    }

    private sealed record CarouselProps(CarouselState State, string[] Items, int Position, bool Loop = false, Thickness? Peek = null, double Spacing = 0);

    private sealed class CarouselApp : Component<CarouselProps>
    {
        public override Element Render()
        {
            var (position, setPosition) = UseState(Props.Position);
            var (items, setItems) = UseState(Props.Items);
            var (tick, setTick) = UseState(0);
            var state = Props.State;
            state.SetPosition = setPosition;
            state.SetItems = setItems;
            state.Tick = setTick;
            state.Renders++;
            return VStack(
                Label($"tick {tick}"),
                (CarouselView(items, k => k, (k, s) => Label(s.IsCurrent ? k + " *" : k).AutomationId("cell-" + k).BackgroundColor(Colors.LightBlue), position, p =>
                {
                    state.Reports.Add($"{p}");
                    if (state.Accept) setPosition(p);
                }) with { Loop = Props.Loop, PeekAreaInsets = Props.Peek, ItemSpacing = Props.Spacing })
                    .AutomationId("carousel").Height(200).Width(300).Ref(state.Ref));
        }
    }

    private static async Task<(ReactorCarouselView View, CarouselState State)> ShowCarouselApp(
        FixtureContext c, ElementHandlerRegistry registry, bool accept = true, int position = 0, string[]? items = null, bool loop = false, Thickness? peek = null, double spacing = 0)
    {
        var state = new CarouselState { Accept = accept };
        c.Show(Component<CarouselApp, CarouselProps>(new(state, items ?? Pages, position, loop, peek, spacing)), registry);
        var view = (ReactorCarouselView)await Loaded<IView>(c, "carousel");
        await c.WaitFor(() => view.Controller.Port?.Sample() is not null, "the carousel's cells to be laid out", 10000);
        await CarouselSettled(c, view);
        return (view, state);
    }

    /// <summary>The platform at a verified rest (frame-based) and the controller with nothing left to do.</summary>
    private static async Task CarouselSettled(FixtureContext c, ReactorCarouselView view, int timeoutMs = 10000)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            await CarouselPlatform.WaitAtRest(view, timeoutMs);
            await c.Drain();
            var controller = view.Controller;
            if (!controller.Polling && !controller.BarrierPending && !controller.TransactionPending && !controller.AwaitingAnswer)
            {
                await CarouselPlatform.NextFrame();
                await c.Drain();
                if (!controller.Polling) return;
            }
            if (clock.ElapsedMilliseconds > timeoutMs)
                throw new CheckFailedException($"the carousel's controller didn't settle: polling {controller.Polling}, barrier {controller.BarrierPending}, transaction {controller.TransactionPending}, answer {controller.AwaitingAnswer}");
            await CarouselPlatform.NextFrame();
        }
    }

    private static string ShownKey(ReactorCarouselView view) =>
        view.Controller.Port?.Sample() is { } s && s.Shown < view.Host.Items.Count ? view.Host.Items[s.Shown].Key : "-";

    private static bool ShownAligned(ReactorCarouselView view) => view.Controller.Port?.Sample() is { Aligned: true };

    // ── Gates ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gate 3 (§5.9): rest detection sees a programmatic animation, which sets no iOS motion flag: frame
    /// by frame, the port's offset keeps changing until the landing; never two still frames on the way.
    /// </summary>
    private static async Task CarouselRestDetection(FixtureContext c)
    {
        var (view, state) = await ShowCarouselApp(c, MauiHandlers.CreateRegistry());
        var port = view.Controller.Port!;
        var frames = new List<string>();
        var offsets = new HashSet<string>();
        var longestUnprotected = 0;
        var uncovered = 0;
        var still = 0;
        CarouselSample? last = null;
        state.SetPosition(3);
        await c.Drain(1);
        var clock = Stopwatch.StartNew();
        var landed = false;
        while (clock.ElapsedMilliseconds < 3000 && !landed)
        {
            await CarouselPlatform.NextFrame();
            var now = port.Sample()!.Value;
            var pending = port.ScrollPending;
            frames.Add($"{now.Shown}:{now.Offset:0}{(now.Moving ? "m" : "")}{(pending ? "p" : "")}");
            landed = now.Shown == 3 && now.Aligned;
            if (!landed) offsets.Add($"{now.Shown}:{now.Offset:0}");
            // Motion the port doesn't cover with its pending scroll is taken for input (spec 070 §5.4).
            if (!landed && now.Moving && !pending) uncovered++;
            var moved = now.Moving || last is not { } l || l.Shown != now.Shown || Math.Abs(l.Offset - now.Offset) > 0.5;
            // A still frame the port doesn't cover with its pending scroll is one the controller counts toward a rest.
            still = moved || pending ? 0 : still + 1;
            if (!landed) longestUnprotected = Math.Max(longestUnprotected, still);
            last = now;
        }
        Note($"frames: {string.Join(" ", frames)}");
        Check(landed, "the projection landed on item 3");
        Check(longestUnprotected < 2, $"no stillness on the way the port doesn't cover with its pending scroll (longest {longestUnprotected} frames; two still frames make a rest)");
        Equal(0, uncovered, "the port covers its own scroll's motion (else it would be taken for input)");
        Check(offsets.Count >= 3, $"the animation passed through several distinct offsets before landing ({offsets.Count}; positive witness: it moved frame by frame)");
        await CarouselSettled(c, view);
        Equal(0, state.Reports.Count, "an own projection isn't reported");
    }

    /// <summary>Gate 4 (§5.9): the shown item from cell geometry is the one the platform centers, with peek, spacing and Loop.</summary>
    private static async Task CarouselShownGeometry(FixtureContext c)
    {
        foreach (var (loop, peek) in new[] { (false, (Thickness?)null), (false, new Thickness(40, 0)), (true, (Thickness?)null), (true, new Thickness(40, 0)) })
        {
            var (view, state) = await ShowCarouselApp(c, MauiHandlers.CreateRegistry(), loop: loop, peek: peek, spacing: 8);
            foreach (var target in new[] { 2, 4, 0, 1 })
            {
                state.SetPosition(target);
                await c.Drain();
                await CarouselSettled(c, view);
                var centered = CarouselPlatform.Centered(view).Replace(" *", "", StringComparison.Ordinal);
                Note($"loop {loop}, peek {peek?.Left ?? 0}, declared {target}: shown {ShownKey(view)} (aligned {ShownAligned(view)}, sample {view.Controller.Port?.Sample()}, scrolls {view.Controller.Projections}), the platform's centered label {centered}, {CarouselPlatform.State(view)}");
                Equal(Pages[target], ShownKey(view), "the shown item is the declared one");
                Equal(Pages[target], centered, "the platform centers the same item");
                // Centered; on Android an edge item with peek as far as the list scrolls (MAUI decorates its
                // outer side more, so its view rests a few points off center, where a swipe rests too).
                var offset = Math.Abs(view.Controller.Port!.Sample()!.Value.Offset);
                var edge = OperatingSystem.IsAndroid() && peek is not null && !loop && (target == 0 || target == Pages.Length - 1);
                Check(offset < (edge ? 8 : 1), $"centered (off by {offset:0.#})");
                // That rest is final: another look at it (a MAUI event) scrolls nothing.
                var projections = view.Controller.Projections;
                view.Controller.OnTrigger();
                await CarouselSettled(c, view);
                Equal(projections, view.Controller.Projections, "a further signal at that rest projects nothing");
            }
            Equal(0, state.Reports.Count, "declared changes report nothing");
            c.Unmount();
        }
    }

    /// <summary>What a cell's re-render does to where the carousel rests, and a ScrollTo in the same turn as one.</summary>
    private static async Task CarouselCellRerender(FixtureContext c)
    {
        var (view, state) = await ShowCarouselApp(c, MauiHandlers.CreateRegistry());
        var port = view.Controller.Port!;
        async Task<string> Frames(int count)
        {
            var seen = new List<string>();
            for (var i = 0; i < count; i++)
            {
                await CarouselPlatform.NextFrame();
                var key = ShownKey(view) + ":" + $"{port.Sample()?.Offset:0}";
                if (seen.Count == 0 || seen[^1] != key) seen.Add(key);
            }
            return string.Join(" ", seen);
        }
        port.ScrollTo(2, false);
        Note($"plain jump to 2: {await Frames(30)}");
        state.Tick(1);
        await c.Drain(1);
        Note($"a re-render (tick) at 2: {await Frames(40)}; MAUI Position {view.Position}");
        view.Host.RenderRealized();
        Note($"cells re-rendered at 2: {await Frames(40)}");
        state.SetPosition(2);
        await c.Drain(1);
        Note($"declared 2 (IsCurrent moves, no scroll needed): {await Frames(40)}");
        view.Host.RenderRealized();
        port.ScrollTo(4, true);
        Note($"cells re-rendered and ScrollTo(4) in one turn: {await Frames(60)}");
        view.Host.RenderRealized();
#if IOS || MACCATALYST
        ((UIKitView)view.Handler!.PlatformView!).LayoutIfNeeded();
#endif
        port.ScrollTo(1, true);
        Note($"cells re-rendered, LayoutIfNeeded, ScrollTo(1): {await Frames(60)}");
        await CarouselSettled(c, view);
        var before = view.Controller.Projections;
        state.SetPosition(3);
        await c.Drain(1);
        Note($"declared 3 through Reactor: {await Frames(60)}; projections {view.Controller.Projections - before}; MAUI Position {view.Position}");
        await CarouselSettled(c, view);
        Note($"settled: shown {ShownKey(view)}, projections {view.Controller.Projections - before}");
        var events = new List<string>();
        view.ScrollToRequested += (_, e) => events.Add($"request {e.Index} animate {e.IsAnimated}");
        view.PositionChanged += (_, e) => events.Add($"position {e.CurrentPosition}");
        view.Controller.Port!.ScrollTo(0, true);
        Note($"port ScrollTo(0) outside Reactor: {await Frames(60)}; events [{string.Join("; ", events)}]");
        c.Unmount();
        foreach (var (spacing, peek) in new[] { (8d, (Thickness?)null), (0d, new Thickness(40, 0)), (8d, new Thickness(40, 0)) })
        {
            (view, state) = await ShowCarouselApp(c, MauiHandlers.CreateRegistry(), spacing: spacing, peek: peek);
            port = view.Controller.Port!;
            port.ScrollTo(2, true);
            Note($"spacing {spacing}, peek {peek?.Left ?? 0}: ScrollTo(2, animate): {await Frames(60)}; MAUI Position {view.Position}; {CarouselPlatform.State(view)}");
            port.ScrollTo(3, false);
            Note($"spacing {spacing}, peek {peek?.Left ?? 0}: ScrollTo(3, jump): {await Frames(20)}; MAUI Position {view.Position}; {CarouselPlatform.State(view)}");
            view.Position = 1;
            Note($"spacing {spacing}, peek {peek?.Left ?? 0}: Position = 1: {await Frames(40)}; MAUI Position {view.Position}; {CarouselPlatform.State(view)}");
#if IOS || MACCATALYST
            var scroller = CarouselPlatform.Scroller(view);
            Note($"scroller content {scroller.ContentSize.Width:0} bounds {scroller.Bounds.Width:0} inset {scroller.AdjustedContentInset.Left:0}/{scroller.AdjustedContentInset.Right:0} paging {scroller.PagingEnabled}");
#endif
            await CarouselPlatform.Drag(view, new Drag(-200, 300, 100), _ => { }, null, allowSimulated: true);
            Note($"spacing {spacing}, peek {peek?.Left ?? 0}: a simulated drag: {await Frames(40)}; MAUI Position {view.Position}; {CarouselPlatform.State(view)}");
            c.Unmount();
        }
    }

    // ── Behavior ─────────────────────────────────────────────────────────────

    private static async Task CarouselSwipe(FixtureContext c, ElementHandlerRegistry registry)
    {
        Note($"input: {CarouselPlatform.Input}");
        foreach (var accept in new[] { true, false })
        {
            var (view, state) = await ShowCarouselApp(c, registry, accept);
            var reportsWhileMoving = new List<string>();
            var mauiEventsWhileMoving = 0;
            view.PositionChanged += (_, _) => { if (view.Controller.Port?.Sample() is { Moving: true }) mauiEventsWhileMoving++; };
            await CarouselPlatform.Drag(view, new Drag(-240, 400, 300), _ => { }, async () =>
            {
                await Task.Delay(200);
                await c.Drain();
                reportsWhileMoving.AddRange(state.Reports);
            }, allowSimulated: true);
            await CarouselSettled(c, view);
            Note($"{(accept ? "accepted" : "refused")}: reports while moving [{string.Join(",", reportsWhileMoving)}], MAUI events while moving {mauiEventsWhileMoving}, reports [{string.Join(",", state.Reports)}], shown {ShownKey(view)}");
            Equal(0, reportsWhileMoving.Count, "nothing is reported while moving");
            // Android reports Position during a drag: the check above had something to refuse.
            if (OperatingSystem.IsAndroid()) Check(mauiEventsWhileMoving >= 1, "MAUI raised PositionChanged while moving (positive witness)");
            Equal("1", string.Join(",", state.Reports), "the resting item, once");
            if (accept)
            {
                Equal("p1", ShownKey(view), "the accepted item shows");
                Check(ShownAligned(view), "and is aligned");
                Equal("p1 *", Platform.Text(c.ById<IView>("cell-p1")), "its template has IsCurrent");
            }
            else Equal("p0", ShownKey(view), "refused: back at the declaration");
            c.Unmount();
        }
    }

    private static async Task CarouselLongHoldFixture(FixtureContext c, ElementHandlerRegistry registry)
    {
        var (view, state) = await ShowCarouselApp(c, registry);
        var offsets = new List<string>();
        var itemCounts = new List<int>();
        var rendersBefore = 0;
        Note($"input: {CarouselPlatform.Input}");
        // A real hold: the finger stays down through the renders (the drag checks it's still down after them).
        await CarouselPlatform.Drag(view, new Drag(-240, 400, 2500), _ => { }, async () =>
        {
            rendersBefore = state.Renders;
            foreach (var render in new Action[] { () => state.Tick(1), () => state.SetItems(["new", .. Pages]), () => state.SetPosition(3) })
            {
                var before = CarouselPlatform.State(view);
                render();
                await c.Drain();
                await CarouselPlatform.NextFrame();
                await CarouselPlatform.NextFrame();
                offsets.Add($"{before} → {CarouselPlatform.State(view)}");
                itemCounts.Add(view.Host.Items.Count);
            }
            Equal(0, state.Reports.Count, "nothing reported during the hold");
        }, allowSimulated: true);
        await CarouselSettled(c, view);
        Note($"during the hold: {string.Join(" | ", offsets)}; native item counts {string.Join(",", itemCounts)}; renders {state.Renders - rendersBefore}; reports [{string.Join(",", state.Reports)}]; shown {ShownKey(view)}, items {string.Join(",", view.Host.Items.Select(b => b.Key))}");
        Check(state.Renders - rendersBefore >= 3, "the three renders ran during the hold (positive witness)");
        Check(itemCounts.All(n => n == Pages.Length), "no items were published while the finger held");
        foreach (var o in offsets)
        {
            var parts = o.Split(" → ");
            Equal(OffsetOf(parts[0]), OffsetOf(parts[1]), "a render during the hold didn't move the carousel");
        }
        // The finger rested on p1 (index 2 of the app's new items): reported once; then the app's answer lands.
        Equal("2", string.Join(",", state.Reports), "one report, mapped to the new items");
        Equal("new,p0,p1,p2,p3,p4", string.Join(",", view.Host.Items.Select(b => b.Key)), "the items landed");
        Equal("p1", ShownKey(view), "the accepted item shows");
    }

    private static string OffsetOf(string state)
    {
        var at = state.IndexOf("offset ", StringComparison.Ordinal);
        return at < 0 ? state : state[(at + 7)..];
    }

    private static async Task CarouselDeclaredChange(FixtureContext c, ElementHandlerRegistry registry)
    {
        var (view, state) = await ShowCarouselApp(c, registry);
        state.SetPosition(3);
        await c.Drain();
        await CarouselSettled(c, view);
        Note($"declared 3: shown {ShownKey(view)}, aligned {ShownAligned(view)}, MAUI's Position {view.Position}, reports [{string.Join(",", state.Reports)}]");
        Equal("p3", ShownKey(view), "the platform shows the declared item");
        Equal(0, state.Reports.Count, "a declared change isn't reported");

        // A declaration during a drag waits for its end, after the drag's report.
        state.Accept = false;
        var shownDuringHold = "";
        Note($"input: {CarouselPlatform.Input}");
        await CarouselPlatform.Drag(view, new Drag(240, 400, 2500), _ => { }, async () =>
        {
            state.SetPosition(4);
            await c.Drain();
            await CarouselPlatform.NextFrame();
            await CarouselPlatform.NextFrame();
            shownDuringHold = ShownKey(view);
        }, allowSimulated: true);
        Check(shownDuringHold != "p4", $"the declaration isn't projected while the finger holds (shown {shownDuringHold})");
        await CarouselSettled(c, view);
        Note($"declared 4 during a drag to p2: reports [{string.Join(",", state.Reports)}], shown {ShownKey(view)}");
        Equal("2", string.Join(",", state.Reports), "the drag's resting item reported first");
        Equal("p4", ShownKey(view), "then the declaration is projected");
    }

    /// <summary>Gates 1 and 2 (§5.9), and the behavior: every items case lands on the app's re-declared item, unreported.</summary>
    private static async Task CarouselItemsChangeFixture(FixtureContext c, ElementHandlerRegistry registry)
    {
        var cases = new (string Name, int At, string[] Items, int Declared)[]
        {
            ("remove the current item", 2, ["p0", "p1", "p3", "p4"], 2),
            ("remove an earlier item", 3, ["p1", "p2", "p3", "p4"], 2),
            ("insert before the current", 2, ["new", "p0", "p1", "p2", "p3", "p4"], 3),
            ("shrink below the position", 4, ["p0", "p1", "p2"], 2),
            ("move the current item", 2, ["p2", "p0", "p1", "p3", "p4"], 0),
            ("append", 2, [.. Pages, "new"], 2),
            ("replace all", 3, ["y0", "y1", "y2"], 1),
        };
        foreach (var (name, at, items, declared) in cases)
        {
            var (view, state) = await ShowCarouselApp(c, registry, position: at);
            var expected = items[declared];
            var shown = new List<string>();
            state.SetItems(items);
            state.SetPosition(declared);
            await c.Drain(1);
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 700)
            {
                await CarouselPlatform.NextFrame();
                var key = ShownKey(view);
                if (shown.Count == 0 || shown[^1] != key) shown.Add(key);
            }
            await CarouselSettled(c, view);
            Note($"{name}: declared {expected}, shown per frame {string.Join(",", shown)}, at rest {ShownKey(view)} (aligned {ShownAligned(view)}), reports [{string.Join(",", state.Reports)}], barriers {view.Controller.BarriersIssued}, projections {view.Controller.Projections}");
            Equal(expected, ShownKey(view), $"{name}: the declared item shows");
            Check(ShownAligned(view), $"{name}: aligned");
            Equal(0, state.Reports.Count, $"{name}: nothing reported");
            // MAUI's KeepItemsInView correction shows the first item for a frame (§5.1): never here.
            if (expected != items[0]) Check(!shown.Contains(items[0]), $"{name}: no frame showed the first item ({string.Join(",", shown)})");
            c.Unmount();
        }
    }

    private static async Task CarouselAccessibilityFixture(FixtureContext c, ElementHandlerRegistry registry)
    {
        var (view, state) = await ShowCarouselApp(c, registry, peek: new Thickness(40, 0));
#if ANDROID
        var rv = (AndroidX.RecyclerView.Widget.RecyclerView)view.Handler!.PlatformView!;
        var handled = rv.PerformAccessibilityAction(global::Android.Views.Accessibility.Action.ScrollForward, null);
#else
        await CarouselPlatform.EnableAccessibilityRuntime();
        var handled = CarouselPlatform.Accessibility(view, "next cell _accessibilityScrollToVisible");
#endif
        await CarouselSettled(c, view);
        Note($"accessibility: {handled}; reports [{string.Join(",", state.Reports)}], shown {ShownKey(view)}");
        Equal("1", string.Join(",", state.Reports), "the accessibility scroll is reported once");
        Equal("p1", ShownKey(view), "and kept (accepted)");
    }

    private static async Task CarouselRefScrollTo(FixtureContext c, ElementHandlerRegistry registry)
    {
        foreach (var accept in new[] { true, false })
        {
            var (view, state) = await ShowCarouselApp(c, registry, accept);
            state.Ref.Current!.ScrollTo(3, position: ScrollToPosition.Center, animate: true);
            await CarouselSettled(c, view);
            Note($"ref ScrollTo(3), {(accept ? "accepted" : "refused")}: reports [{string.Join(",", state.Reports)}], shown {ShownKey(view)}");
            Equal("3", string.Join(",", state.Reports), "the ref command is reported once");
            Equal(accept ? "p3" : "p0", ShownKey(view), accept ? "kept" : "refused: back at the declaration");
            c.Unmount();
        }
    }

    private static async Task CarouselLoop(FixtureContext c, ElementHandlerRegistry registry)
    {
        var (view, state) = await ShowCarouselApp(c, registry, position: 4, loop: true);
        Equal("p4", ShownKey(view), "starts at the last item");
        await CarouselPlatform.Drag(view, new Drag(-240, 400, 200), _ => { }, null, allowSimulated: true);
        await CarouselSettled(c, view);
        Note($"loop, a swipe from the last item: reports [{string.Join(",", state.Reports)}], shown {ShownKey(view)}, centered {CarouselPlatform.Centered(view)}");
        Equal("0", string.Join(",", state.Reports), "the wrap is reported as item 0");
        Equal("p0", ShownKey(view), "item 0 shows");
    }

    /// <summary>A fling: nothing reported during the deceleration, the resting item once (spec 070 §5.8, review M9).</summary>
    private static async Task CarouselFling(FixtureContext c, ElementHandlerRegistry registry)
    {
#if IOS || MACCATALYST
        if (!CarouselPlatform.HostGestures) throw new SkipException("a fling needs real touches: run with host-gestures.py (REACTOR_HOST_GESTURES=1)");
#endif
        Note($"input: {CarouselPlatform.Input}");
        foreach (var (name, drag) in new[] { ("one page", new Drag(-160, 60, 0)), ("a longer fling", new Drag(-420, 90, 0)) })
        {
            var (view, state) = await ShowCarouselApp(c, registry);
            var reportsWhileMoving = new List<string>();
            var motionFrames = 0;
            await CarouselPlatform.Drag(view, drag, _ => { }, null);
            // The deceleration after the lift: looked at every frame until it stops.
            for (var i = 0; i < 600 && view.Controller.Port?.Sample() is { Moving: true }; i++)
            {
                motionFrames++;
                await c.Drain(1);
                reportsWhileMoving.AddRange(state.Reports);
                await CarouselPlatform.NextFrame();
            }
            await CarouselSettled(c, view);
            var centered = CarouselPlatform.Centered(view).Replace(" *", "", StringComparison.Ordinal);
            Note($"{name}: frames moving after the lift {motionFrames}, reports while moving [{string.Join(",", reportsWhileMoving)}], reports [{string.Join(",", state.Reports)}], shown {ShownKey(view)}, centered {centered}");
            Check(motionFrames > 0, $"{name}: it decelerated after the lift (positive witness)");
            Equal(0, reportsWhileMoving.Count, $"{name}: nothing reported while it decelerates");
            Equal(1, state.Reports.Count, $"{name}: the resting item, once");
            Check(state.Reports[0] != "0", $"{name}: it moved");
            Equal(Pages[int.Parse(state.Reports[0], System.Globalization.CultureInfo.InvariantCulture)], centered, $"{name}: the report is the centered item");
            c.Unmount();
        }
    }

    private static async Task CarouselGeometry(FixtureContext c, ElementHandlerRegistry registry)
    {
        var (view, _) = await ShowCarouselApp(c, registry, position: 1, peek: new Thickness(40, 0), spacing: 10);
        var current = Platform.WindowFrame(c.ById<IView>("cell-p1"));
        var next = Platform.WindowFrame(c.ById<IView>("cell-p2"));
        var carousel = Platform.WindowFrame(view);
        Note($"carousel {carousel}, current {current}, next {next}");
        Close(carousel.Width - 80, current.Width, "the item is the width minus both peeks", 2);
        Close(current.Right + 10, next.Left, "the next item starts after the spacing", 2);
        Close(carousel.Left + carousel.Width / 2, current.Left + current.Width / 2, "the current item is centered", 2);
    }

    private static async Task IndicatorControlled(FixtureContext c, ElementHandlerRegistry registry)
    {
        foreach (var accept in new[] { true, false })
        {
            var reports = new List<string>();
            Action<int> set;
            var holder = new IndicatorHolder();
            c.Show(Component<IndicatorApp, (IndicatorHolder, List<string>, bool)>((holder, reports, accept)), registry);
            var indicator = (MauiIndicatorView)await Loaded<IView>(c, "dots");
            set = holder.Set;
            await c.Settle(300);
            Equal(1, indicator.Position, "mounted");
            set(2);
            await c.Drain();
            await c.Settle(200);
            Equal(2, indicator.Position, "a declared change shows");
            Equal(0, reports.Count, "and isn't reported");
            CarouselPlatform.TapIndicator(indicator, 4);
            await c.Drain();
            await c.Settle(300);
            Note($"{(accept ? "accepted" : "refused")}: reports [{string.Join(",", reports)}], position {indicator.Position}, native {CarouselPlatform.IndicatorState(indicator)}");
            Equal("4", string.Join(",", reports), "a tap is reported once");
            Equal(accept ? 4 : 2, indicator.Position, accept ? "kept" : "refused: restored");
            set(9);
            await c.Drain();
            await c.Settle(200);
            Equal(4, indicator.Position, "a position beyond the count shows the last");
            c.Unmount();
        }
    }

    private sealed class IndicatorHolder
    {
        public Action<int> Set = null!;
    }

    private sealed class IndicatorApp : Component<(IndicatorHolder Holder, List<string> Reports, bool Accept)>
    {
        public override Element Render()
        {
            var (position, setPosition) = UseState(1);
            Props.Holder.Set = setPosition;
            var props = Props;
            return VStack((IndicatorView(5, position, p =>
            {
                props.Reports.Add($"{p}");
                if (props.Accept) setPosition(p);
            }) with { SelectedIndicatorColor = Colors.Orange, IndicatorSize = 12 }).AutomationId("dots"));
        }
    }
}
