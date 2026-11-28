using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Maui.Controls;
using static Reactor.Maui.DeviceTests.FixtureContext;
using MauiCarouselView = Microsoft.Maui.Controls.CarouselView;

namespace Reactor.Maui.DeviceTests;

// Spec 070 §5 (4c-4): characterizations of plain MAUI's CarouselView and IndicatorView before the
// controlled-position design is fixed. They log what they measure and assert only that the measurement
// ran. They are on demand (REACTOR_DEVICE_TEST_FILTER=Characterize_Carousel): long, and on the iPhone and
// iPad simulators the drags are real XCUITest touches from host-gestures.py, which a run without it skips.
public static partial class Fixtures
{
    private static IEnumerable<Fixture> CarouselCharacterizationFixtures() =>
    [
        new("Characterize_Carousel_Probe", CarouselProbe, 60, OnDemand: true),
        new("Characterize_Carousel_Gestures", c => CarouselGestures(c, loop: false), 120, OnDemand: true),
        new("Characterize_Carousel_Gestures_Loop", c => CarouselGestures(c, loop: true), 120, OnDemand: true),
        new("Characterize_Carousel_Gestures_Peek", c => CarouselGestures(c, loop: false, peek: new Thickness(40, 0)), 120, OnDemand: true),
        new("Characterize_Carousel_Writes", CarouselWrites, 90, OnDemand: true),
        new("Characterize_Carousel_Accessibility", CarouselAccessibility, 90, OnDemand: true),
        new("Characterize_Carousel_ScrollTo", CarouselScrollTo, 90, OnDemand: true),
        new("Characterize_Carousel_ItemsChange", CarouselItemsChange, 120, OnDemand: true),
        new("Characterize_Carousel_ItemsChangeThenProject", CarouselItemsChangeThenProject, 300, OnDemand: true),
        new("Characterize_Carousel_ItemsChangeThenPostedProject", CarouselItemsChangeThenPostedProject, 300, OnDemand: true),
        new("Characterize_Carousel_ProgrammaticMotion", CarouselProgrammaticMotion, 90, OnDemand: true),
        new("Characterize_IndicatorView", IndicatorCharacterization, 90, OnDemand: true),
        new("Characterize_Carousel_LongHold", CarouselLongHold, 180, OnDemand: true),
#if ANDROID
        new("Characterize_Carousel_LongHold_HostInput", c => CarouselLongHold(c, host: true), 240, OnDemand: true),
#endif
    ];

    /// <summary>Every carousel event, with the time, the stage, whether a write of ours was running, and the platform's scroll state.</summary>
    private sealed class CarouselLog
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private MauiCarouselView? _carousel;
        public readonly List<string> Events = [];
        public string Stage = "create";
        public bool Writing;

        public void Attach(MauiCarouselView carousel)
        {
            _carousel = carousel;
            carousel.PositionChanged += (_, e) => Add($"Position {e.PreviousPosition}->{e.CurrentPosition}");
            carousel.CurrentItemChanged += (_, e) => Add($"CurrentItem {e.PreviousItem ?? "null"}->{e.CurrentItem ?? "null"}");
            carousel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MauiCarouselView.IsDragging)) Add($"IsDragging={carousel.IsDragging}");
            };
        }

        public void Mark(string what) => Add("── " + what);

        private void Add(string what)
        {
            var native = _carousel is null ? "" : CarouselPlatform.State(_carousel);
            var maui = _carousel is null ? "" : $"maui drag {(_carousel.IsDragging ? 1 : 0)} scroll {(_carousel.IsScrolling ? 1 : 0)} pos {_carousel.Position}";
            Events.Add($"{_clock.ElapsedMilliseconds,6}ms {Stage} {(Writing ? "IN-WRITE" : "outside")} {what} [{native}; {maui}]");
        }

        public void Write(string what, Action write)
        {
            Mark("write " + what);
            Writing = true;
            try { write(); }
            catch (Exception ex) { Add($"threw {ex.GetType().Name}: {ex.Message}"); }
            finally { Writing = false; }
            Mark("write returned");
        }

        public void Dump(string title)
        {
            Note($"{title}:");
            foreach (var e in Events) Note("    " + e);
            Events.Clear();
        }
    }

    private static MauiCarouselView PlainCarousel(int count, bool loop = false, Thickness? peek = null)
    {
        var carousel = new MauiCarouselView
        {
            ItemsSource = new System.Collections.ObjectModel.ObservableCollection<string>(Enumerable.Range(0, count).Select(i => $"item{i}")),
            ItemTemplate = new DataTemplate(() =>
            {
                var label = new Label { HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center, BackgroundColor = Colors.LightBlue };
                label.SetBinding(Label.TextProperty, ".");
                return label;
            }),
            Loop = loop,
            HeightRequest = 200,
            WidthRequest = 300,
        };
        if (peek is { } insets) carousel.PeekAreaInsets = insets;
        return carousel;
    }

    private static async Task<(MauiCarouselView Carousel, CarouselLog Log)> ShowCarousel(FixtureContext c, int count = 5, bool loop = false, Thickness? peek = null, int? position = null)
    {
        var log = new CarouselLog();
        var carousel = PlainCarousel(count, loop, peek);
        log.Attach(carousel);
        if (position is { } p) log.Write($"Position = {p} before the handler", () => carousel.Position = p);
        c.ShowNative(new VerticalStackLayout { Children = { carousel } });
        await c.WaitFor(() => carousel.Handler?.PlatformView is not null && carousel.Width > 0, "the carousel to load");
        await c.Settle(1000);
        log.Mark("loaded");
        return (carousel, log);
    }

    /// <summary>A drag: down at the carousel's center, a move of <c>Dx</c> points over <c>MoveMs</c>, a hold of <c>HoldMs</c>, and the release.</summary>
    public sealed record Drag(double Dx, int MoveMs, int HoldMs);

    private static Task Gesture(FixtureContext c, MauiCarouselView carousel, CarouselLog log, Drag drag, Func<Task>? duringHold = null) =>
        Gesture(c, carousel, log, drag, host: false, duringHold);

    private static async Task Gesture(FixtureContext c, MauiCarouselView carousel, CarouselLog log, Drag drag, bool host, Func<Task>? duringHold = null)
    {
        log.Mark($"gesture dx {drag.Dx} over {drag.MoveMs} ms, hold {drag.HoldMs} ms{(host ? ", host input" : "")}");
#if ANDROID
        if (host) await CarouselPlatform.DragFromHost(carousel, drag, log.Mark, duringHold);
        else
#endif
        await CarouselPlatform.Drag(carousel, drag, log.Mark, duringHold);
        await CarouselPlatform.WaitAtRest(carousel, 8000);
        await c.Settle(600);
        log.Mark("at rest");
    }

    private static async Task CarouselProbe(FixtureContext c)
    {
        var (carousel, log) = await ShowCarousel(c);
        Note($"platform: {CarouselPlatform.Input}; tree: {CarouselPlatform.Describe(carousel)}");
        log.Dump("load");
    }

    private static async Task CarouselGestures(FixtureContext c, bool loop, Thickness? peek = null)
    {
        var (carousel, log) = await ShowCarousel(c, loop: loop, peek: peek);
        log.Dump($"load (loop {loop}, peek {peek?.Left ?? 0})");
        var plans = new (string Name, Drag Drag)[]
        {
            ("fling to next", new(-150, 80, 0)),
            ("slow drag past half, held still, released", new(-200, 600, 400)),
            ("slow drag under half, released", new(-90, 500, 300)),
            ("fling back", new(150, 80, 0)),
            ("two flings to the end", new(-150, 80, 0)),
        };
        foreach (var (name, drag) in plans)
        {
            log.Stage = name.Replace(' ', '_');
            await Gesture(c, carousel, log, drag);
            log.Dump($"{name} → position {carousel.Position}, current {carousel.CurrentItem}");
        }
        // To the last item and past it: a wrap with Loop, a bounce without.
        log.Stage = "to_the_end";
        foreach (var _ in Enumerable.Range(0, 4)) await Gesture(c, carousel, log, new(-150, 80, 0));
        log.Dump($"to the end → position {carousel.Position}");
        log.Stage = "past_the_end";
        await Gesture(c, carousel, log, new(-150, 80, 0));
        log.Dump($"past the end → position {carousel.Position}, current {carousel.CurrentItem}");
    }

    private static async Task CarouselWrites(FixtureContext c)
    {
        var (carousel, log) = await ShowCarousel(c, position: 3);
        log.Dump($"created with Position 3 before its handler → position {carousel.Position}, current {carousel.CurrentItem}");
        foreach (var animated in new[] { true, false })
        {
            carousel.IsScrollAnimated = animated;
            log.Stage = animated ? "animated" : "jump";
            log.Write("Position = 1", () => carousel.Position = 1);
            await c.Settle(1500);
            log.Mark("settled");
            log.Dump($"{log.Stage}: Position = 1 → position {carousel.Position}, current {carousel.CurrentItem}, {CarouselPlatform.State(carousel)}");
            log.Write("Position = 3, then 4 in the same turn", () => { carousel.Position = 3; carousel.Position = 4; });
            await c.Settle(1500);
            log.Mark("settled");
            log.Dump($"{log.Stage}: 3 then 4 → position {carousel.Position}, {CarouselPlatform.State(carousel)}");
            log.Write("Position = 0", () => carousel.Position = 0);
            await Task.Delay(30);
            log.Write("Position = 2 (30 ms later)", () => carousel.Position = 2);
            await c.Settle(1500);
            log.Mark("settled");
            log.Dump($"{log.Stage}: 0, then 2 30 ms later → position {carousel.Position}, {CarouselPlatform.State(carousel)}");
            log.Write("Position = 2 (equal)", () => carousel.Position = 2);
            await c.Settle(600);
            log.Dump($"{log.Stage}: equal write → position {carousel.Position}");
            log.Write("CurrentItem = item4", () => carousel.CurrentItem = "item4");
            await c.Settle(1500);
            log.Mark("settled");
            log.Dump($"{log.Stage}: CurrentItem = item4 → position {carousel.Position}, current {carousel.CurrentItem}, {CarouselPlatform.State(carousel)}");
            log.Write("Position = 0", () => carousel.Position = 0);
            await c.Settle(1500);
            log.Dump($"{log.Stage}: back to 0 → position {carousel.Position}");
        }
        log.Stage = "out of range";
        log.Write("Position = 9", () => carousel.Position = 9);
        await c.Settle(800);
        log.Dump($"out of range → position {carousel.Position}");
    }

    private static async Task CarouselAccessibility(FixtureContext c)
    {
        foreach (var peek in new Thickness?[] { null, new Thickness(40, 0) })
        {
        var (carousel, log) = await ShowCarousel(c, peek: peek);
        log.Dump($"load (peek {peek?.Left ?? 0}: neighbors partly visible)");
        foreach (var action in CarouselPlatform.AccessibilityActions)
        {
            log.Stage = action.Replace(' ', '_');
            log.Mark("action " + action);
            var result = CarouselPlatform.Accessibility(carousel, action);
            log.Mark("action returned " + result);
            await CarouselPlatform.WaitAtRest(carousel, 5000);
            await c.Settle(800);
            log.Mark("settled");
            log.Dump($"{action} → returned {result}; position {carousel.Position}, current {carousel.CurrentItem}, {CarouselPlatform.State(carousel)}");
        }
        }
    }

    private static async Task CarouselScrollTo(FixtureContext c)
    {
        var (carousel, log) = await ShowCarousel(c);
        log.Dump("load");
        foreach (var (name, scroll) in new (string, Action)[]
        {
            ("ScrollTo(3, animate)", () => carousel.ScrollTo(3, position: ScrollToPosition.Center, animate: true)),
            ("ScrollTo(1, no animation)", () => carousel.ScrollTo(1, position: ScrollToPosition.Center, animate: false)),
            ("ScrollTo(item4, animate)", () => carousel.ScrollTo("item4", position: ScrollToPosition.Center, animate: true)),
            ("ScrollTo(0, animate) then Position = 2 at once", () => { carousel.ScrollTo(0, position: ScrollToPosition.Center, animate: true); carousel.Position = 2; }),
        })
        {
            log.Stage = name.Replace(' ', '_');
            log.Write(name, scroll);
            await CarouselPlatform.WaitAtRest(carousel, 5000);
            await c.Settle(1200);
            log.Mark("settled");
            log.Dump($"{name} → position {carousel.Position}, current {carousel.CurrentItem}, {CarouselPlatform.State(carousel)}");
        }
    }

    private static async Task CarouselItemsChange(FixtureContext c)
    {
        foreach (var (name, setup, change) in new (string, int, Action<System.Collections.ObjectModel.ObservableCollection<string>, MauiCarouselView>)[]
        {
            ("remove the current item (2 of 5)", 2, (items, _) => items.RemoveAt(2)),
            ("remove the current last item (4 of 5)", 4, (items, _) => items.RemoveAt(4)),
            ("remove an item before the current (0, at 3)", 3, (items, _) => items.RemoveAt(0)),
            ("insert before the current (at 2)", 2, (items, _) => items.Insert(0, "new")),
            ("shrink below the position (at 4: remove 4, then 3)", 4, (items, _) => { items.RemoveAt(4); items.RemoveAt(3); }),
            ("replace the source with 2 items (at 4)", 4, (_, carousel) => carousel.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<string>(["x0", "x1"])),
            ("clear (at 2)", 2, (items, _) => items.Clear()),
            ("move the current item (2 → 0)", 2, (items, _) => items.Move(2, 0)),
        })
        {
            var (carousel, log) = await ShowCarousel(c);
            var items = (System.Collections.ObjectModel.ObservableCollection<string>)carousel.ItemsSource;
            carousel.IsScrollAnimated = false;
            log.Write($"Position = {setup}", () => carousel.Position = setup);
            await c.Settle(1200);
            log.Dump($"{name}: setup → position {carousel.Position}");
            log.Stage = "change";
            log.Write(name, () => change(items, carousel));
            await c.Settle(1500);
            log.Mark("settled");
            log.Dump($"{name} → position {carousel.Position}, current {carousel.CurrentItem}, {CarouselPlatform.State(carousel)}, centered {CarouselPlatform.Centered(carousel)}");
        }
    }

    /// <summary>
    /// What a Reactor-style commit does: the items change, then the declared position (the index that
    /// keeps the current item, or the nearest one) is projected in the same turn, as a Position write or
    /// as a ScrollTo, with MAUI's default ItemsUpdatingScrollMode or KeepScrollOffset.
    /// </summary>
    private static async Task CarouselItemsChangeThenProject(FixtureContext c)
    {
        var changes = new (string Name, int At, int Declared, Action<System.Collections.ObjectModel.ObservableCollection<string>> Change)[]
        {
            ("remove the current item (2 of 5)", 2, 2, items => items.RemoveAt(2)),
            ("remove an item before the current (0, at 3)", 3, 2, items => items.RemoveAt(0)),
            ("insert before the current (at 2)", 2, 3, items => items.Insert(0, "new")),
            ("shrink below the position (at 4: remove 4, then 3)", 4, 2, items => { items.RemoveAt(4); items.RemoveAt(3); }),
            ("move the current item (2 → 0)", 2, 0, items => items.Move(2, 0)),
            ("append after the current (at 2)", 2, 2, items => items.Add("new")),
        };
        foreach (var mode in new[] { ItemsUpdatingScrollMode.KeepItemsInView, ItemsUpdatingScrollMode.KeepScrollOffset })
        foreach (var project in new[] { "Position", "ScrollTo" })
        foreach (var (name, at, declared, change) in changes)
        {
#if ANDROID
            // Measured: MAUI's dispatched SetCurrentItem throws ArgumentOutOfRangeException on the UI
            // thread (the app crashes) for two removals in one turn under KeepScrollOffset.
            if (mode == ItemsUpdatingScrollMode.KeepScrollOffset && project == "Position" && name.StartsWith("shrink", StringComparison.Ordinal))
            {
                Note($"[{mode}, {project}] {name}: skipped (crashes the app on Android, measured)");
                continue;
            }
#endif
            var (carousel, log) = await ShowCarousel(c);
            carousel.IsScrollAnimated = false;
            carousel.ItemsUpdatingScrollMode = mode;
            carousel.Position = at;
            await c.Settle(800);
            log.Events.Clear();
            var items = (System.Collections.ObjectModel.ObservableCollection<string>)carousel.ItemsSource;
            log.Stage = "commit";
            log.Write($"{name}, then {project} {declared}", () =>
            {
                change(items);
                if (project == "Position") carousel.Position = declared;
                else carousel.ScrollTo(declared, position: ScrollToPosition.Center, animate: false);
            });
            await c.Settle(900);
            var centered = CarouselPlatform.Centered(carousel);
            var expected = items[Math.Min(declared, items.Count - 1)];
            log.Dump($"[{mode}, {project}] {name}: declared {declared} ({expected}) → position {carousel.Position}, centered {centered}{(centered == expected ? "" : "  ≠ DECLARED")}");
        }
    }

    /// <summary>
    /// The items change in the write; the declared position is projected with a ScrollTo posted to the
    /// dispatcher after MAUI's own deferred item handling (optionally also bumped by a same-turn
    /// ScrollTo, which makes MAUI skip its deferred correction). The centered item is sampled per frame.
    /// </summary>
    private static async Task CarouselItemsChangeThenPostedProject(FixtureContext c)
    {
        var changes = new (string Name, int At, int Declared, Action<System.Collections.ObjectModel.ObservableCollection<string>> Change)[]
        {
            ("remove the current item (2 of 5)", 2, 2, items => items.RemoveAt(2)),
            ("remove an item before the current (0, at 3)", 3, 2, items => items.RemoveAt(0)),
            ("insert before the current (at 2)", 2, 3, items => items.Insert(0, "new")),
            ("shrink below the position (at 4: remove 4, then 3)", 4, 2, items => { items.RemoveAt(4); items.RemoveAt(3); }),
            ("move the current item (2 → 0)", 2, 0, items => items.Move(2, 0)),
            ("append after the current (at 2)", 2, 2, items => items.Add("new")),
            ("replace all (at 3, new items)", 3, 1, items => { items.Clear(); foreach (var x in new[] { "y0", "y1", "y2" }) items.Add(x); }),
        };
        foreach (var (sameTurn, delayMs) in new[] { (false, 0), (true, 0), (true, 50), (false, 150) })
        foreach (var (name, at, declared, change) in changes)
        {
            var (carousel, log) = await ShowCarousel(c);
            carousel.IsScrollAnimated = false;
            carousel.Position = at;
            await c.Settle(800);
            log.Events.Clear();
            var items = (System.Collections.ObjectModel.ObservableCollection<string>)carousel.ItemsSource;
            log.Stage = "commit";
            var frames = new List<string>();
            log.Write($"{name}{(sameTurn ? ", same-turn ScrollTo" : "")}", () =>
            {
                change(items);
                if (sameTurn) carousel.ScrollTo(Math.Min(declared, items.Count - 1), position: ScrollToPosition.Center, animate: false);
            });
            c.Window.Dispatcher.Dispatch(async () =>
            {
                if (delayMs > 0) await Task.Delay(delayMs);
                log.Mark("posted projection");
                carousel.ScrollTo(Math.Min(declared, items.Count - 1), position: ScrollToPosition.Center, animate: false);
            });
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 600)
            {
                frames.Add(CarouselPlatform.Centered(carousel));
                await Task.Delay(16);
            }
            await c.Settle(500);
            var centered = CarouselPlatform.Centered(carousel);
            var expected = items[Math.Min(declared, items.Count - 1)];
            var shown = string.Join(",", frames.Where((f, i) => i == 0 || frames[i - 1] != f));
            log.Dump($"[{(sameTurn ? "same-turn + posted" : "posted")}{(delayMs > 0 ? $" +{delayMs}ms" : "")}] {name}: declared {declared} ({expected}) → position {carousel.Position}, centered {centered}{(centered == expected ? "" : "  ≠ DECLARED")}; shown per frame: {shown}");
        }
    }

    /// <summary>The platform's motion state, frame by frame, through a programmatic animated scroll.</summary>
    private static async Task CarouselProgrammaticMotion(FixtureContext c)
    {
        var (carousel, log) = await ShowCarousel(c);
        foreach (var (name, act) in new (string, Action)[]
        {
            ("ScrollTo(3, animate)", () => carousel.ScrollTo(3, position: ScrollToPosition.Center, animate: true)),
            ("Position = 1 (animated)", () => carousel.Position = 1),
        })
        {
            var frames = new List<string>();
            log.Write(name, act);
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 1200)
            {
                frames.Add($"{clock.ElapsedMilliseconds}:{CarouselPlatform.State(carousel).Replace("native ", "")}");
                await Task.Delay(16);
            }
            Note($"{name}: {string.Join(" | ", frames.Where((_, i) => i % 3 == 0))}");
            log.Dump(name);
        }
    }

    private static async Task IndicatorCharacterization(FixtureContext c)
    {
        var events = new List<string>();
        var writing = false;
        var indicator = new IndicatorView { Count = 5, IndicatorColor = Colors.Gray, SelectedIndicatorColor = Colors.Blue, IndicatorSize = 12, HeightRequest = 40 };
        indicator.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IndicatorView.Position)) events.Add($"{(writing ? "IN-WRITE" : "outside")} Position={indicator.Position}");
        };
        c.ShowNative(new VerticalStackLayout { Children = { indicator } });
        await c.WaitFor(() => indicator.Handler?.PlatformView is not null && indicator.Width > 0, "the indicator to load");
        await c.Settle(500);
        writing = true;
        indicator.Position = 2;
        writing = false;
        await c.Settle(300);
        Note($"write Position = 2: [{string.Join("; ", events)}], native {CarouselPlatform.IndicatorState(indicator)}");
        events.Clear();
        var input = CarouselPlatform.TapIndicator(indicator, 4);
        await c.Settle(300);
        Note($"user selects 4 ({input}): [{string.Join("; ", events)}], position {indicator.Position}, native {CarouselPlatform.IndicatorState(indicator)}");
        events.Clear();
        writing = true;
        indicator.Count = 3;
        writing = false;
        await c.Settle(300);
        Note($"Count = 3 at position 4: [{string.Join("; ", events)}], position {indicator.Position}, native {CarouselPlatform.IndicatorState(indicator)}");
        events.Clear();
        writing = true;
        indicator.Position = 7;
        writing = false;
        await c.Settle(300);
        Note($"write Position = 7 (out of range, count 3): [{string.Join("; ", events)}], position {indicator.Position}, native {CarouselPlatform.IndicatorState(indicator)}");
    }

    private static async Task CarouselLongHold(FixtureContext c) => await CarouselLongHold(c, host: false);

    private static async Task CarouselLongHold(FixtureContext c, bool host)
    {
        foreach (var (name, write) in new (string, Func<MauiCarouselView, int>?)[]
        {
            ("no write", null),
            ("write the position MAUI shows (equal)", carousel => carousel.Position),
            ("write the position the drag started at", _ => 0),
            ("write another position (3)", _ => 3),
        })
        {
            var (carousel, log) = await ShowCarousel(c);
            log.Dump($"{name}: load");
            log.Stage = "hold";
            string? before = null, after = null;
            await Gesture(c, carousel, log, new(-200, 500, 2500), host, async () =>
            {
                await Task.Delay(700);
                before = CarouselPlatform.State(carousel);
                if (write is not null) { var value = write(carousel); log.Write($"Position = {value} during the hold", () => carousel.Position = value); }
                else log.Mark("no write");
                await Task.Delay(400);
                after = CarouselPlatform.State(carousel);
            });
            log.Dump($"{name}: during the hold before [{before}] after [{after}]; at rest position {carousel.Position}, current {carousel.CurrentItem}, centered {CarouselPlatform.Centered(carousel)}");
        }
    }
}

/// <summary>Native readback and input for the carousel characterization.</summary>
public static partial class CarouselPlatform
{
#if IOS || MACCATALYST || ANDROID
    private static int s_gestures;
#endif

    /// <summary>
    /// Waits for a verified rest (spec 070 §5.9, review 8): two consecutive display frames with no motion
    /// flag and an unchanged offset. Fails while the carousel still moves at the timeout.
    /// </summary>
    public static async Task WaitAtRest(MauiCarouselView carousel, int timeoutMs)
    {
        var clock = Stopwatch.StartNew();
        var still = 0;
        double? last = null;
        while (still < 2)
        {
            if (clock.ElapsedMilliseconds > timeoutMs)
                throw new CheckFailedException($"the carousel still moved after {timeoutMs} ms: {State(carousel)}");
            await NextFrame();
            var offset = OffsetValue(carousel);
            still = !Moving(carousel) && last is { } previous && Math.Abs(previous - offset) < 0.5 ? still + 1 : 0;
            last = offset;
        }
    }

    /// <summary>Completes on the next display frame (CADisplayLink, Choreographer).</summary>
    public static Task NextFrame()
    {
        var done = new TaskCompletionSource();
#if IOS || MACCATALYST
        CoreAnimation.CADisplayLink? link = null;
        link = CoreAnimation.CADisplayLink.Create(() =>
        {
            link?.Invalidate();
            done.TrySetResult();
        });
        link.AddToRunLoop(Foundation.NSRunLoop.Main, Foundation.NSRunLoopMode.Common);
#elif ANDROID
        global::Android.Views.Choreographer.Instance!.PostFrameCallback(new FrameDone(done));
#else
        done.TrySetResult();
#endif
        return done.Task;
    }

#if ANDROID
    private sealed class FrameDone(TaskCompletionSource done) : Java.Lang.Object, global::Android.Views.Choreographer.IFrameCallback
    {
        public void DoFrame(long frameTimeNanos) => done.TrySetResult();
    }

    private static double OffsetValue(MauiCarouselView carousel) => Recycler(carousel).ComputeHorizontalScrollOffset();
#elif IOS || MACCATALYST
    private static double OffsetValue(MauiCarouselView carousel) => Scroller(carousel).ContentOffset.X;
#endif

#if IOS || MACCATALYST
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendNint(IntPtr receiver, IntPtr selector, nint value);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern bool SendBool(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libAccessibility.dylib", EntryPoint = "_AXSSetAutomationEnabled")]
    private static extern void SetAutomationEnabled(int enabled);

    /// <summary>
    /// UIKit loads its accessibility runtime only while an assistive client (VoiceOver, an XCUITest
    /// session) is attached; without it labels aren't accessibility elements and VoiceOver-style
    /// actions are declined. Enables it the way UI test frameworks do (EarlGrey, KIF).
    /// </summary>
    public static async Task EnableAccessibilityRuntime()
    {
        SetAutomationEnabled(1);
        await Task.Delay(500);
    }

    private static IEnumerable<UIKit.UIView> Walk(UIKit.UIView root)
    {
        var pending = new Stack<UIKit.UIView>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            yield return next;
            foreach (var child in next.Subviews) pending.Push(child);
        }
    }

    /// <summary>The scroll view that pages the items: CV2's orthogonal section scroller.</summary>
    public static UIKit.UIScrollView Scroller(IView carousel)
    {
        var root = carousel.Handler?.PlatformView as UIKit.UIView ?? throw new CheckFailedException("no platform view");
        return Walk(root).OfType<UIKit.UIScrollView>().FirstOrDefault(s => s.Class.Name?.Contains("Orthogonal", StringComparison.Ordinal) == true)
            ?? throw new CheckFailedException("no orthogonal scroller");
    }

    private static double MidX(CoreGraphics.CGRect r) => r.X + r.Width / 2;

    private static UIKit.UICollectionView Collection(IView carousel) =>
        Walk((UIKit.UIView)carousel.Handler!.PlatformView!).OfType<UIKit.UICollectionView>().First();

    private static bool Moving(MauiCarouselView carousel)
    {
        var s = Scroller(carousel);
        return s.Tracking || s.Dragging || s.Decelerating;
    }

    public static string State(MauiCarouselView carousel)
    {
        if (carousel.Handler?.PlatformView is null) return "no view";
        var s = Scroller(carousel);
        return $"native track {(s.Tracking ? 1 : 0)} drag {(s.Dragging ? 1 : 0)} decel {(s.Decelerating ? 1 : 0)} offset {s.ContentOffset.X:0.#}";
    }

    /// <summary>The text of the label nearest the scroller's center.</summary>
    public static string Centered(MauiCarouselView carousel)
    {
        var s = Scroller(carousel);
        var center = new CoreGraphics.CGPoint(s.ContentOffset.X + s.Bounds.Width / 2, s.Bounds.Height / 2);
        return Walk(s).OfType<UIKit.UILabel>()
            .Select(l => (Label: l, Frame: l.ConvertRectToView(l.Bounds, s)))
            .OrderBy(x => Math.Abs((x.Frame.X + x.Frame.Width / 2) - center.X))
            .Select(x => x.Label.Text ?? "")
            .FirstOrDefault() ?? "-";
    }

    public static string Describe(IView carousel) =>
        string.Join("; ", Walk((UIKit.UIView)carousel.Handler!.PlatformView!).OfType<UIKit.UIScrollView>().Select(s =>
            $"{s.Class.Name} frame {s.Frame.Width:0}x{s.Frame.Height:0} content {s.ContentSize.Width:0} paging {s.PagingEnabled}"));

    public static readonly string[] AccessibilityActions =
    [
        "collection accessibilityScroll Left", "collection accessibilityScroll Right", "collection accessibilityScroll Next",
        "scroller accessibilityScroll Left", "scroller accessibilityScroll Right",
        "next cell _accessibilityScrollToVisible", "previous cell _accessibilityScrollToVisible",
    ];

    public static string Accessibility(MauiCarouselView carousel, string action)
    {
        var parts = action.Split(' ');
        if (parts[1] == "accessibilityScroll")
        {
            UIKit.UIView target = parts[0] == "collection" ? Collection(carousel) : Scroller(carousel);
            var direction = Enum.Parse<UIKit.UIAccessibilityScrollDirection>(parts[2]);
            return target.AccessibilityScroll(direction).ToString();
        }
        // VoiceOver moving to an off-screen element asks the element to scroll itself into view.
        var s = Scroller(carousel);
        var pageWidth = s.Bounds.Width;
        var target2 = parts[0] == "next" ? s.ContentOffset.X + pageWidth * 1.5 : s.ContentOffset.X - pageWidth * 0.5;
        var cell = Walk(s).OfType<UIKit.UICollectionViewCell>()
            .OrderBy(cv => Math.Abs(MidX(cv.ConvertRectToView(cv.Bounds, s)) - target2)).FirstOrDefault();
        if (cell is null) return "no cell";
        var label = Walk(cell).OfType<UIKit.UILabel>().FirstOrDefault() as UIKit.UIView ?? cell;
        var selector = new ObjCRuntime.Selector("_accessibilityScrollToVisible");
        if (!label.RespondsToSelector(selector)) return "unsupported";
        var text = (label as UIKit.UILabel)?.Text;
        var before = $"label {label.ConvertRectToView(label.Bounds, s)} in scroller {s.Bounds} (a11y {label.IsAccessibilityElement}, window {label.Window is not null})";
        return $"{SendBool(label.Handle, selector.Handle)} ({text}; {before})";
    }

    public static string IndicatorState(IndicatorView indicator) =>
        indicator.Handler?.PlatformView is UIKit.UIPageControl pc ? $"page {pc.CurrentPage} of {pc.Pages}" : $"{indicator.Handler?.PlatformView?.GetType().Name}";

    /// <summary>What a tap on a page control does: UIKit sets the page, then sends ValueChanged.</summary>
    public static string TapIndicator(IndicatorView indicator, int page)
    {
        var pc = (UIKit.UIPageControl)indicator.Handler!.PlatformView!;
        pc.CurrentPage = page;
        pc.SendActionForControlEvents(UIKit.UIControlEvent.ValueChanged);
        return "CurrentPage set, ValueChanged sent";
    }
#endif

#if IOS || MACCATALYST
    public static string Input => HostGestures
        ? "XCUITest touches through Appium (host driver), real UIKit pans"
        : "simulated: the page scroller's pan recognizer driven through its states, the offset stepped (no real pointer input in-process)";

    /// <summary>Whether host-gestures.py performs this run's drags (iOS simulators only).</summary>
    public static bool HostGestures => !OperatingSystem.IsMacCatalyst() && Environment.GetEnvironmentVariable("REACTOR_HOST_GESTURES") == "1";

    /// <summary>
    /// A drag: real touches from the host's driver when it runs, else simulated (the scroller's pan
    /// recognizer through its states, so UIKit reports dragging, and the offset stepped). Characterizations
    /// pass <paramref name="allowSimulated"/> false on iOS: they record only what real touches do.
    /// </summary>
    public static async Task Drag(MauiCarouselView carousel, Fixtures.Drag drag, Action<string> mark, Func<Task>? duringHold, bool allowSimulated = false)
    {
        if (HostGestures) await HostDrag(carousel, drag, mark, duringHold);
        else if (allowSimulated || OperatingSystem.IsMacCatalyst()) await SimulatedDrag(carousel, drag, mark, duringHold);
        else throw new SkipException("real touches need the host's gesture driver (tests/Reactor.Maui.DeviceTests/host-gestures.py, REACTOR_HOST_GESTURES=1)");
    }

    private static async Task HostDrag(MauiCarouselView carousel, Fixtures.Drag drag, Action<string> mark, Func<Task>? duringHold)
    {
        var s = Scroller(carousel);
        var center = s.ConvertPointToView(new CoreGraphics.CGPoint(s.ContentOffset.X + s.Bounds.Width / 2, s.Bounds.Height / 2), null);
        var id = ++s_gestures;
        Console.WriteLine($"REACTOR-GESTURE {id} {center.X:0} {center.Y:0} {drag.Dx:0} {drag.MoveMs} {drag.HoldMs}");
        var clock = Stopwatch.StartNew();
        while (!s.Tracking)
        {
            if (clock.ElapsedMilliseconds > 20000) throw new CheckFailedException("the host's touch never arrived");
            await Task.Delay(5);
        }
        mark("finger down (native tracking)");
        if (duringHold is not null)
        {
            await Task.Delay(drag.MoveMs + 150);
            mark($"hold: native tracking {s.Tracking}");
            if (!s.Tracking) throw new CheckFailedException($"the host's finger lifted before the hold's work (hold {drag.HoldMs} ms): that work wouldn't run under a held finger");
            await duringHold();
            mark($"hold work done: native tracking {s.Tracking}");
            if (!s.Tracking) throw new CheckFailedException($"the host's finger lifted during the hold's work (hold {drag.HoldMs} ms)");
        }
        var samples = new List<string>();
        while (s.Tracking)
        {
            samples.Add($"{s.ContentOffset.X:0}");
            await Task.Delay(30);
        }
        mark($"finger up (native tracking ended); offsets while tracking: {string.Join(",", samples.Distinct())}");
    }

    private static async Task SimulatedDrag(MauiCarouselView carousel, Fixtures.Drag drag, Action<string> mark, Func<Task>? duringHold)
    {
        var s = Scroller(carousel);
        var pan = s.PanGestureRecognizer;
        var setState = ObjCRuntime.Selector.GetHandle("setState:");
        void State(UIKit.UIGestureRecognizerState state) => SendNint(pan.Handle, setState, (nint)(long)state);
        var start = s.ContentOffset.X;
        State(UIKit.UIGestureRecognizerState.Began);
        mark($"simulated pan began: native dragging {s.Dragging}");
        const int steps = 6;
        for (var i = 1; i <= steps; i++)
        {
            await NextFrame();
            s.ContentOffset = new CoreGraphics.CGPoint(start - drag.Dx * i / steps, s.ContentOffset.Y);
            State(UIKit.UIGestureRecognizerState.Changed);
        }
        if (duringHold is not null) await duringHold();
        else await Task.Delay(drag.HoldMs);
        State(UIKit.UIGestureRecognizerState.Ended);
        mark($"simulated pan ended at offset {s.ContentOffset.X:0.#}");
    }
#elif ANDROID
    public const string Input = "in-process MotionEvents dispatched to the RecyclerView (its real touch handling, velocity and snap)";

    private static AndroidX.RecyclerView.Widget.RecyclerView Recycler(IView carousel) =>
        carousel.Handler?.PlatformView as AndroidX.RecyclerView.Widget.RecyclerView ?? throw new CheckFailedException("no RecyclerView");

    private static bool Moving(MauiCarouselView carousel) => Recycler(carousel).ScrollState != AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateIdle;

    public static string State(MauiCarouselView carousel)
    {
        if (carousel.Handler?.PlatformView is null) return "no view";
        var rv = Recycler(carousel);
        var density = rv.Context!.Resources!.DisplayMetrics!.Density;
        var state = rv.ScrollState switch { 0 => "idle", 1 => "DRAGGING", 2 => "settling", var o => o.ToString() };
        var snap = rv.GetOnFlingListener() as AndroidX.RecyclerView.Widget.SnapHelper;
        var layout = rv.GetLayoutManager();
        var snaps = new List<string>();
        if (snap is not null && layout is not null)
            for (var i = 0; i < rv.ChildCount; i++)
            {
                var child = rv.GetChildAt(i)!;
                var d = snap.CalculateDistanceToFinalSnap(layout, child);
                snaps.Add($"{layout.GetPosition(child)}:{(d is null ? "null" : string.Join("/", d))}px");
            }
        return $"native {state} offset {rv.ComputeHorizontalScrollOffset() / density:0.#}, snap {snap?.GetType().Name ?? "none"} [{string.Join(" ", snaps)}]";
    }

    public static string Centered(MauiCarouselView carousel)
    {
        var rv = Recycler(carousel);
        var center = rv.Width / 2f;
        string best = "-";
        var distance = float.MaxValue;
        for (var i = 0; i < rv.ChildCount; i++)
        {
            var child = rv.GetChildAt(i)!;
            var d = Math.Abs(child.Left + child.Width / 2f - center);
            if (d < distance && FindText(child) is { } text) { distance = d; best = text; }
        }
        return best;
    }

    private static string? FindText(Android.Views.View view)
    {
        if (view is Android.Widget.TextView t) return t.Text;
        if (view is Android.Views.ViewGroup g)
            for (var i = 0; i < g.ChildCount; i++)
                if (FindText(g.GetChildAt(i)!) is { } found) return found;
        return null;
    }

    public static string Describe(IView carousel)
    {
        var rv = Recycler(carousel);
        return $"{rv.GetType().Name} {rv.Width}x{rv.Height}px, layout {rv.GetLayoutManager()?.GetType().Name}, fling listener {rv.GetOnFlingListener()?.GetType().Name ?? "-"}";
    }

    public static async Task Drag(MauiCarouselView carousel, Fixtures.Drag drag, Action<string> mark, Func<Task>? duringHold, bool allowSimulated = false)
    {
        _ = allowSimulated;
        var rv = Recycler(carousel);
        var density = rv.Context!.Resources!.DisplayMetrics!.Density;
        float x = rv.Width / 2f, y = rv.Height / 2f;
        var down = Android.OS.SystemClock.UptimeMillis();
        void Send(Android.Views.MotionEventActions action, float px)
        {
            using var e = Android.Views.MotionEvent.Obtain(down, Android.OS.SystemClock.UptimeMillis(), action, px, y, 0)!;
            rv.DispatchTouchEvent(e);
        }
        Send(Android.Views.MotionEventActions.Down, x);
        mark("finger down");
        var steps = Math.Max(4, drag.MoveMs / 16);
        var dx = (float)(drag.Dx * density);
        for (var i = 1; i <= steps; i++)
        {
            await Task.Delay(drag.MoveMs / steps);
            Send(Android.Views.MotionEventActions.Move, x + dx * i / steps);
        }
        mark("moved");
        if (duringHold is not null)
        {
            var hold = Stopwatch.StartNew();
            await duringHold();
            var rest = drag.HoldMs - (int)hold.ElapsedMilliseconds;
            if (rest > 0) await Task.Delay(rest);
        }
        else if (drag.HoldMs > 0) await Task.Delay(drag.HoldMs);
        Send(Android.Views.MotionEventActions.Up, x + dx);
        mark("finger up");
    }

    /// <summary>A real touch drag from the host shell (adb input), for comparison with the in-process events.</summary>
    public static async Task DragFromHost(MauiCarouselView carousel, Fixtures.Drag drag, Action<string> mark, Func<Task>? duringHold)
    {
        var rv = Recycler(carousel);
        var density = rv.Context!.Resources!.DisplayMetrics!.Density;
        var at = new int[2];
        rv.GetLocationOnScreen(at);
        Console.WriteLine($"REACTOR-GESTURE {++s_gestures} {at[0] + rv.Width / 2} {at[1] + rv.Height / 2} {(int)(drag.Dx * density)} {drag.MoveMs} {drag.HoldMs}");
        var clock = Stopwatch.StartNew();
        while (rv.ScrollState != AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateDragging)
        {
            if (clock.ElapsedMilliseconds > 20000) throw new CheckFailedException("the host's touch never arrived");
            await Task.Delay(5);
        }
        mark("dragging (host input)");
        if (duringHold is not null)
        {
            await Task.Delay(600);
            if (rv.ScrollState != AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateDragging)
                throw new CheckFailedException($"the host's finger lifted before the hold's work (hold {drag.HoldMs} ms)");
            await duringHold();
            if (rv.ScrollState != AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateDragging)
                throw new CheckFailedException($"the host's finger lifted during the hold's work (hold {drag.HoldMs} ms)");
        }
        while (rv.ScrollState == AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateDragging) await Task.Delay(5);
        mark("drag ended (host input)");
    }

    public static string IndicatorState(IndicatorView indicator)
    {
        if (indicator.Handler?.PlatformView is not Android.Views.ViewGroup group) return "no view";
        var states = new List<string>();
        for (var i = 0; i < group.ChildCount; i++) states.Add(group.GetChildAt(i)!.Clickable ? "o" : "*");
        return string.Join("", states);
    }

    public static string TapIndicator(IndicatorView indicator, int page)
    {
        var group = (Android.Views.ViewGroup)indicator.Handler!.PlatformView!;
        return $"PerformClick {group.GetChildAt(page)!.PerformClick()}";
    }

    public static readonly string[] AccessibilityActions =
    [
        "ScrollForward", "ScrollBackward", "ScrollRight", "ScrollLeft", "ShowOnScreen next",
    ];

    public static string Accessibility(MauiCarouselView carousel, string action)
    {
        var rv = Recycler(carousel);
        switch (action)
        {
            case "ScrollForward": return rv.PerformAccessibilityAction(Android.Views.Accessibility.Action.ScrollForward, null).ToString();
            case "ScrollBackward": return rv.PerformAccessibilityAction(Android.Views.Accessibility.Action.ScrollBackward, null).ToString();
            case "ScrollRight": return rv.PerformAccessibilityAction((Android.Views.Accessibility.Action)Android.Resource.Id.AccessibilityActionScrollRight, null).ToString();
            case "ScrollLeft": return rv.PerformAccessibilityAction((Android.Views.Accessibility.Action)Android.Resource.Id.AccessibilityActionScrollLeft, null).ToString();
            default:
                // TalkBack moving to a partly visible item asks it to show itself.
                var center = rv.Width / 2f;
                Android.Views.View? next = null;
                for (var i = 0; i < rv.ChildCount; i++)
                {
                    var child = rv.GetChildAt(i)!;
                    if (child.Left + child.Width / 2f > center + 10 && (next is null || child.Left < next.Left)) next = child;
                }
                return next is null ? "no next child" : next.PerformAccessibilityAction((Android.Views.Accessibility.Action)Android.Resource.Id.AccessibilityActionShowOnScreen, null).ToString();
        }
    }
#endif
}
