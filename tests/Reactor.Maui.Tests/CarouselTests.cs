using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiCarouselView = Microsoft.Maui.Controls.CarouselView;
using MauiLabel = Microsoft.Maui.Controls.Label;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>A carousel's platform for headless tests: the tests set what it shows and run its frames and barriers.</summary>
internal sealed class StubCarouselPort : ICarouselPort
{
    public object? Owner { get; set; } = new();
    public int Shown;
    public bool Aligned = true;
    public double Offset;
    public bool Moving;
    public bool User;
    public bool Laid = true;

    /// <summary>A ScrollTo lands at once (a jump), unless a test drives the motion itself.</summary>
    public bool AutoLand = true;

    public readonly List<(int Index, bool Animate)> Scrolls = [];
    public readonly List<Action> Barriers = [];
    public readonly List<Action> Frames = [];
    public Action? Interaction;

    public CarouselSample? Sample() => Laid ? new CarouselSample(Shown, Aligned, Offset, Moving, User) : null;

    public void ScrollTo(int index, bool animate)
    {
        Scrolls.Add((index, animate));
        if (!AutoLand) return;
        Shown = index;
        Aligned = true;
        Offset = 0;
    }

    public bool RefuseBarriers;

    public bool Barrier(Action completion)
    {
        if (RefuseBarriers) return false;
        Barriers.Add(completion);
        return true;
    }

    public void NextFrame(Action callback) => Frames.Add(callback);

    public void Watch(Action onInteraction) => Interaction = onInteraction;

    /// <summary>Runs one display frame's callbacks.</summary>
    public void Frame()
    {
        var frames = Frames.ToList();
        Frames.Clear();
        foreach (var frame in frames) frame();
    }

    public void CompleteBarrier(int index = 0)
    {
        var barrier = Barriers[index];
        Barriers.RemoveAt(index);
        barrier();
    }
}

/// <summary>Spec 070 §5.4: the carousel's controlled position, with a stub platform.</summary>
public class CarouselPositionTests
{
    private sealed record AppProps(string[] Items, int Position, List<string> Log, bool Accept, Action<Harness> Expose);

    internal sealed class Harness
    {
        public TestHost Host = null!;
        public ReactorCarouselView View = null!;
        public StubCarouselPort Port = null!;
        public List<string> Log = null!;
        public Action<int> SetPosition = null!;
        public Action<string[]> SetItems = null!;
        public Action<int> Tick = null!;
        public Action<Action<int>> SetCallback = null!;

        /// <summary>Runs frames (and the posted reports and restores they cause) until nothing is pending.</summary>
        public void Settle(int maxFrames = 200)
        {
            for (var i = 0; i < maxFrames; i++)
            {
                Host.Scheduler.Drain();
                if (Port.Frames.Count == 0) return;
                Port.Frame();
            }
            Host.Scheduler.Drain();
        }

        public void Frames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                Port.Frame();
                Host.Scheduler.Drain();
            }
        }
    }

    private sealed class App : Component<AppProps>
    {
        public override Element Render()
        {
            var (position, setPosition) = UseState(Props.Position);
            var (items, setItems) = UseState(Props.Items);
            var (tick, setTick) = UseState(0);
            var (callback, setCallback) = UseState<Action<int>?>(null);
            var harness = new Harness();
            var props = Props;
            props.Expose(harness);
            harness.SetPosition = setPosition;
            harness.SetItems = setItems;
            harness.Tick = setTick;
            harness.SetCallback = c => setCallback(c);
            _ = tick;
            return CarouselView(items, k => k, (k, state) => Label(state.IsCurrent ? k + " *" : k), position, p =>
            {
                if (callback is not null) callback(p);
                props.Log.Add($"{p}");
                if (props.Accept) setPosition(p);
            });
        }
    }

    private static readonly string[] Five = ["a", "b", "c", "d", "e"];

    internal static Harness Mount(string[]? items = null, int position = 0, bool accept = false)
    {
        Harness? last = null;
        var log = new List<string>();
        var props = new AppProps(items ?? Five, position, log, accept, h => last = h);
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Component<App, AppProps>(props));
        host.Scheduler.Drain();
        var harness = last!;
        harness.Host = host;
        harness.Log = log;
        harness.View = host.View<ReactorCarouselView>();
        harness.Port = new StubCarouselPort { Shown = Math.Clamp(position, 0, (items ?? Five).Length - 1) };
        harness.View.Controller.Port = harness.Port;
        harness.Settle();
        // State setters are stable across renders: the first render's harness keeps working.
        return harness;
    }

    // ── Reports only at rest ─────────────────────────────────────────────────

    [Fact]
    public void Input_IsReportedOnceAtRest_NeverWhileMoving()
    {
        var h = Mount();
        h.Port.Moving = true;
        h.Port.User = true;
        h.Port.Shown = 2;
        for (var i = 0; i < 3; i++) h.View.Controller.OnTrigger();
        h.Frames(10);
        Assert.Empty(h.Log);
        Assert.NotEmpty(h.Port.Frames); // positive witness: the poll is running, looking

        h.Port.Moving = false;
        h.Port.User = false;
        h.Settle();
        Assert.Equal(["2"], h.Log);

        // More signals at the same rest report nothing more.
        h.View.Controller.OnTrigger();
        h.Settle();
        Assert.Equal(["2"], h.Log);
    }

    [Fact]
    public void ASignalBeforeTheAnswer_NeitherScrollsNorReportsAgain()
    {
        var h = Mount(accept: true);
        h.Port.Shown = 3;
        h.View.Controller.OnTrigger();
        while (h.Port.Frames.Count > 0) h.Port.Frame();
        Assert.Empty(h.Log); // reported, the callback posted, not yet run

        // MAUI's late events at the same rest, before the app answered: no projection of the old declaration.
        h.View.Controller.OnTrigger();
        while (h.Port.Frames.Count > 0) h.Port.Frame();
        Assert.Empty(h.Port.Scrolls);

        h.Settle();
        Assert.Equal(["3"], h.Log);
        Assert.Empty(h.Port.Scrolls);
        Assert.Equal(3, h.Port.Shown);
    }

    [Fact]
    public void ADragBackToTheSameItem_WhileTheDeclarationChanged_IsProjectedNotReported()
    {
        var h = Mount();
        h.Port.Moving = true;
        h.Port.User = true;
        h.Port.Shown = 1;
        h.View.Controller.OnTrigger();
        h.Frames(3);
        h.SetPosition(3);
        h.Host.Scheduler.Drain();
        Assert.Empty(h.Port.Scrolls); // nothing written while the user moves it

        // Released where it started: no input to report; the new declaration is projected.
        h.Port.Shown = 0;
        h.Port.Moving = false;
        h.Port.User = false;
        h.Settle();
        Assert.Empty(h.Log);
        Assert.Equal([(3, true)], h.Port.Scrolls);
        Assert.Equal(3, h.Port.Shown);
    }

    [Fact]
    public void ARefusal_ScrollsBackAtRest_AndTheLandingIsNotReported()
    {
        var h = Mount(accept: false);
        h.Port.Shown = 3;
        h.View.Controller.OnTrigger();
        h.Settle();

        Assert.Equal(["3"], h.Log);
        Assert.Equal([(0, true)], h.Port.Scrolls);
        Assert.Equal(0, h.Port.Shown);
    }

    [Fact]
    public void AnAcceptance_WritesNothing_AndMovesIsCurrentWithoutRemounting()
    {
        var h = Mount(accept: true);
        var cellA = Cells.Bind(h.View.ItemTemplate, h.View.Host.Items[0]);
        var cellD = Cells.Bind(h.View.ItemTemplate, h.View.Host.Items[3]);
        var labelD = cellD.Content;
        Assert.Equal(("a *", "d"), (Cells.Text(cellA), Cells.Text(cellD)));

        h.Port.Shown = 3;
        h.View.Controller.OnTrigger();
        h.Settle();

        Assert.Equal(["3"], h.Log);
        Assert.Empty(h.Port.Scrolls);
        Assert.Equal(("a", "d *"), (Cells.Text(cellA), Cells.Text(cellD)));
        Assert.Same(labelD, cellD.Content);
    }

    [Fact]
    public void AJumpWithoutMotion_IsReported()
    {
        var h = Mount();
        // VoiceOver's scroll-to-visible: the platform shows item 1, no motion flag, one MAUI event.
        h.Port.Shown = 1;
        h.View.Controller.OnTrigger();
        h.Settle();
        Assert.Equal(["1"], h.Log);
    }

    [Fact]
    public void TheReport_ReachesTheCurrentCallback_AndNothingAfterUnmount()
    {
        var h = Mount(accept: true);
        var seen = new List<string>();
        h.SetCallback(p => seen.Add($"second {p}"));
        h.Host.Scheduler.Drain();
        h.Port.Shown = 2;
        h.View.Controller.OnTrigger();
        h.Settle();
        Assert.Equal(["second 2"], seen);

        // A signal before unmount, its poll and checks after it: nothing.
        h.Port.Shown = 4;
        h.View.Controller.OnTrigger();
        h.Host.Root.Unmount();
        var scrolls = h.Port.Scrolls.Count;
        h.Settle();
        Assert.Equal(["2"], h.Log);
        Assert.Equal(scrolls, h.Port.Scrolls.Count);
    }

    // ── Writes only at rest ──────────────────────────────────────────────────

    [Fact]
    public void ADeclaredChange_IsProjected_AndItsOwnSettleIsNotReported()
    {
        var h = Mount();
        h.Port.AutoLand = false;
        h.SetPosition(3);
        h.Host.Scheduler.Drain();
        Assert.Equal([(3, true)], h.Port.Scrolls);

        // The animation: MAUI-like signals while it moves, then the landing.
        h.Port.Moving = true;
        h.Port.Shown = 2;
        h.Port.Aligned = false;
        h.View.Controller.OnTrigger();
        h.Frames(3);
        h.Port.Moving = false;
        h.Port.Shown = 3;
        h.Port.Aligned = true;
        h.Settle();

        Assert.Empty(h.Log);
        Assert.Single(h.Port.Scrolls);
    }

    [Fact]
    public void ALongHold_WithUnrelatedRenders_WritesNothing_AndTheReleaseReportsOnce()
    {
        var h = Mount();
        h.Port.Moving = true;
        h.Port.User = true;
        h.Port.Shown = 1;
        h.Port.Aligned = false;
        h.View.Controller.OnTrigger();
        for (var i = 1; i <= 3; i++)
        {
            h.Tick(i);
            h.Host.Scheduler.Drain();
            h.Frames(2);
        }
        Assert.Empty(h.Port.Scrolls);
        Assert.Empty(h.Log);

        h.Port.Moving = false;
        h.Port.User = false;
        h.Settle();
        Assert.Equal(["1"], h.Log);
    }

    [Fact]
    public void ItemsAndADeclarationDuringAHold_WaitForRest()
    {
        var h = Mount(accept: true);
        h.Port.Moving = true;
        h.Port.User = true;
        h.View.Controller.OnTrigger();
        var nativeItems = string.Join(",", h.View.Host.Items.Select(b => b.Key));

        h.SetItems(["z", "a", "b", "c", "d", "e"]);
        h.SetPosition(3);
        h.Host.Scheduler.Drain();
        h.Frames(4);

        Assert.Equal(nativeItems, string.Join(",", h.View.Host.Items.Select(b => b.Key)));
        Assert.Equal(0, h.View.Controller.Publications);
        Assert.Empty(h.Port.Scrolls);
        Assert.Empty(h.Log);

        // The finger lifts where it started: the items land, then the declaration (jumps).
        h.Port.Moving = false;
        h.Port.User = false;
        h.Settle();
        Assert.Equal("z,a,b,c,d,e", string.Join(",", h.View.Host.Items.Select(b => b.Key)));
        Assert.Equal([(3, false)], h.Port.Scrolls); // the same-turn jump
        h.Port.CompleteBarrier();
        h.Settle();
        Assert.Empty(h.Log);
        Assert.Equal(3, h.Port.Shown);
    }

    [Fact]
    public void AMoveDuringAHoldWithAnItemsChange_IsReportedFirst_MappedByKey()
    {
        var h = Mount(accept: true);
        h.Port.Moving = true;
        h.Port.User = true;
        h.View.Controller.OnTrigger();
        h.SetItems(["z", "a", "b", "c", "d", "e"]);
        h.Host.Scheduler.Drain();

        // The user rests on "c" (index 2 of the published items, 3 of the app's).
        h.Port.Shown = 2;
        h.Port.Moving = false;
        h.Port.User = false;
        h.Settle();
        Assert.Equal(["3"], h.Log);
        Assert.Equal("z,a,b,c,d,e", string.Join(",", h.View.Host.Items.Select(b => b.Key)));
        h.Port.CompleteBarrier();
        h.Settle();
        Assert.Equal(3, h.Port.Shown);
        Assert.Equal(["3"], h.Log);
    }

    [Fact]
    public void ASupersededProjection_SettlesUnreported_AndTheNewestIsProjected()
    {
        var h = Mount(accept: true);
        h.Port.AutoLand = false;
        h.SetPosition(3);
        h.Host.Scheduler.Drain();
        h.Port.Moving = true;
        h.View.Controller.OnTrigger();
        h.Frames(2);
        h.SetPosition(4);
        h.Host.Scheduler.Drain();
        Assert.Equal([(3, true)], h.Port.Scrolls); // nothing written while it moves

        h.Port.Moving = false;
        h.Port.Shown = 3;
        h.Port.AutoLand = true;
        h.Settle();
        Assert.Equal([(3, true), (4, true)], h.Port.Scrolls);
        Assert.Equal(4, h.Port.Shown);
        Assert.Empty(h.Log);
    }

    [Fact]
    public void AProjectionStoppedShort_WithNoPlatformEvent_IsHandledByThePoll()
    {
        var h = Mount();
        h.Port.AutoLand = false;
        h.SetPosition(3);
        h.Host.Scheduler.Drain();
        // Interrupted between items, no flag, no MAUI event: only the frame poll sees it. The retry lands.
        h.Port.Shown = 1;
        h.Port.Aligned = false;
        h.Port.Offset = 120;
        h.Port.AutoLand = true;
        h.Settle();
        Assert.Equal([(3, true), (3, true)], h.Port.Scrolls);
        Assert.Empty(h.Log);
    }

    [Fact]
    public void AProjectionLandingOffCenter_IsAskedAgain()
    {
        var h = Mount();
        h.Port.AutoLand = false;
        h.SetPosition(3);
        h.Host.Scheduler.Drain();
        // On its item, but stopped between pages (an interrupted animation).
        h.Port.Shown = 3;
        h.Port.Aligned = false;
        h.Port.Offset = 30;
        h.Port.AutoLand = true;
        h.Settle();
        Assert.Equal([(3, true), (3, true)], h.Port.Scrolls);
        Assert.True(h.Port.Aligned);
        Assert.Empty(h.Log);
    }

    [Fact]
    public void APlatformThatNeverLands_IsAskedThreeTimes_ThenWarned()
    {
        using var warnings = new WarningListener("CarouselView.Position");
        var h = Mount();
        h.Port.AutoLand = false;
        h.SetPosition(3);
        h.Settle(2000);
        Assert.Equal([(3, true), (3, true), (3, true)], h.Port.Scrolls);
        Assert.Empty(h.Port.Frames); // stopped looking
        Assert.Single(warnings.Warnings);
        Assert.Empty(h.Log);
    }

    [Fact]
    public void ARestBetweenItems_AtTheDeclaredOne_IsAlignedWithoutAReport()
    {
        var h = Mount();
        h.Port.Aligned = false;
        h.Port.Offset = 80;
        h.View.Controller.OnTrigger();
        h.Settle();
        Assert.Empty(h.Log);
        Assert.Equal([(0, true)], h.Port.Scrolls);
    }

    [Fact]
    public void Mount_WritesThePositionClamped_AndLaterDeclarationsNeverWritePosition()
    {
        var h = Mount(items: ["a", "b", "c"], position: 7);
        Assert.Equal(2, h.View.Position);
        var writes = 0;
        h.View.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MauiCarouselView.Position)) writes++; };

        h.SetPosition(0);
        h.Host.Scheduler.Drain();
        h.Settle();
        Assert.Equal([(0, true)], h.Port.Scrolls);
        Assert.Equal(0, writes);

        h.SetPosition(9);
        h.Host.Scheduler.Drain();
        h.Settle();
        Assert.Equal((2, true), h.Port.Scrolls[^1]);

        // An empty list projects nothing.
        var count = h.Port.Scrolls.Count;
        h.SetItems([]);
        h.Host.Scheduler.Drain();
        h.Settle();
        Assert.Equal(count, h.Port.Scrolls.Count);
    }

    [Fact]
    public void IsScrollAnimated_DecidesADeclaredChange_ItemsChangesAlwaysJump()
    {
        var log = new List<string>();
        CarouselViewElement Carousel(int position, bool animated, params string[] items) =>
            CarouselView(items, k => k, k => Label(k), position, p => log.Add($"{p}")) with { IsScrollAnimated = animated };
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Carousel(0, false, Five));
        var view = host.View<ReactorCarouselView>();
        var port = new StubCarouselPort();
        view.Controller.Port = port;
        port.Frame();
        port.Frame();
        port.Frame();

        host.Render(Carousel(2, false, Five));
        host.Render(Carousel(3, true, Five));
        while (port.Frames.Count > 0) port.Frame();
        Assert.Equal([(2, false), (3, true)], port.Scrolls);

        host.Render(Carousel(3, true, ["z", .. Five]));
        Assert.Equal((3, false), port.Scrolls[^1]);
        Assert.Empty(log);
    }

    [Fact]
    public void AnItemsChangesProjection_DeferredByMotionAtTheBarrier_StillJumps()
    {
        var h = Mount(position: 2);
        h.SetItems(["z", .. Five]);
        h.Host.Scheduler.Drain();
        Assert.Equal([(2, false)], h.Port.Scrolls); // the same-turn jump

        // MAUI's own correction animates when the barrier completes: projected at rest, still as a jump.
        h.Port.AutoLand = false;
        h.Port.Moving = true;
        h.Port.Shown = 0;
        h.Port.CompleteBarrier();
        h.Frames(3);
        h.Port.Moving = false;
        h.Port.AutoLand = true;
        h.Settle();
        Assert.Equal([(2, false), (2, false)], h.Port.Scrolls);
        Assert.Empty(h.Log);
    }

    // ── Items and layout transactions ────────────────────────────────────────

    [Fact]
    public void Barriers_BelongToTheirEpoch()
    {
        var h = Mount(accept: true);
        h.SetItems(["z", .. Five]);
        h.Host.Scheduler.Drain();
        h.Settle(); // at rest: the second transaction applies at once
        h.SetItems(["y", "z", .. Five]);
        h.Host.Scheduler.Drain();
        Assert.Equal(2, h.Port.Barriers.Count);
        Assert.Equal(2, h.View.Controller.Publications);
        var scrolls = h.Port.Scrolls.Count; // the two same-turn jumps
        h.Port.Shown = 4; // where the platform happens to show something else

        // A position-only commit waits behind the pending barrier.
        h.SetPosition(1);
        h.Host.Scheduler.Drain();
        Assert.Equal(scrolls, h.Port.Scrolls.Count);

        // The obsolete completion neither projects nor releases.
        h.Port.CompleteBarrier(0);
        h.Settle();
        Assert.Equal(scrolls, h.Port.Scrolls.Count);
        Assert.True(h.View.Controller.BarrierPending);
        Assert.Empty(h.Log);

        // The current one projects the latest declaration, once.
        h.Port.CompleteBarrier(0);
        h.Settle();
        Assert.Equal((1, false), h.Port.Scrolls[^1]);
        Assert.Equal(scrolls + 1, h.Port.Scrolls.Count);
        Assert.Empty(h.Log);
    }

    [Fact]
    public void AnObsoleteCompletion_ReissuesTheCurrentBarrier_WhenNoneIsOutstanding()
    {
        var h = Mount();
        h.SetItems(["z", .. Five]);
        h.Host.Scheduler.Drain();
        h.Settle();
        var first = h.Port.Barriers[0];
        h.Port.Barriers.Clear();
        // The second transaction's barrier can't be issued (no native view for a moment).
        h.Port.RefuseBarriers = true;
        h.SetItems(["y", "z", .. Five]);
        h.Host.Scheduler.Drain();
        Assert.Empty(h.Port.Barriers);
        h.Port.RefuseBarriers = false;

        first();
        Assert.Single(h.Port.Barriers); // the current epoch's, reissued
    }

    [Fact]
    public void ADragDuringTheWindow_CancelsTheProjection_AndIsReported()
    {
        var h = Mount(accept: true);
        h.SetItems(["z", .. Five]);
        h.Host.Scheduler.Drain();
        var scrolls = h.Port.Scrolls.Count;
        h.Port.Moving = true;
        h.Port.User = true;
        h.Port.Shown = 4;
        h.View.Controller.OnTrigger();
        h.Frames(3);
        h.Port.Moving = false;
        h.Port.User = false;
        h.Port.CompleteBarrier();
        h.Settle();
        Assert.Equal(scrolls, h.Port.Scrolls.Count);
        Assert.Equal(["4"], h.Log);
    }

    [Fact]
    public void ARefCommandDuringTheWindow_IsReported_AMotionlessJumpIsOverwritten()
    {
        // The ref's ScrollTo raises ScrollToRequested (MAUI needs a handler for that).
        var h = Mount(accept: true);
        h.View.Handler = new StubViewHandler();
        h.View.Controller.Port = h.Port;
        h.Settle();
        h.SetItems(["z", .. Five]);
        h.Host.Scheduler.Drain();
        h.View.ScrollTo(4, position: ScrollToPosition.Center, animate: false);
        h.Port.Shown = 4;
        h.Port.CompleteBarrier();
        h.Settle();
        Assert.Equal(["4"], h.Log);

        // The documented limitation: a jump with no request in the window is overwritten.
        h.Log.Clear();
        h.SetItems(["y", "z", .. Five]);
        h.Host.Scheduler.Drain();
        h.Port.Shown = 1;
        h.Port.CompleteBarrier();
        h.Settle();
        Assert.Empty(h.Log);
        Assert.Equal((4, false), h.Port.Scrolls[^1]);
        Assert.Equal(4, h.Port.Shown);
    }

    [Fact]
    public void AHandlerReplacement_RetiresTheOldBarrier()
    {
        var h = Mount();
        h.SetItems(["z", .. Five]);
        h.Host.Scheduler.Drain();
        var old = h.Port;
        var replacement = new StubCarouselPort { Shown = 1 };
        h.View.Controller.Port = replacement;
        var scrolls = old.Scrolls.Count;
        old.CompleteBarrier();
        Assert.Equal(scrolls, old.Scrolls.Count);
        Assert.False(h.View.Controller.BarrierPending);
    }

    [Fact]
    public void ItemsAndKeys_SyncByKey_KeepingBoxes()
    {
        var h = Mount();
        var c = h.View.Host.Items[2];
        h.SetItems(["e", "d", "c", "b", "a"]);
        h.Host.Scheduler.Drain();
        Assert.Same(c, h.View.Host.Items[2]);
        Assert.Equal("e,d,c,b,a", string.Join(",", h.View.Host.Items.Select(b => b.Key)));
        h.Settle();
        h.Port.CompleteBarrier();
        h.SetItems(["e", "c"]);
        h.Host.Scheduler.Drain();
        h.Settle();
        Assert.Equal("e,c", string.Join(",", h.View.Host.Items.Select(b => b.Key)));
        Assert.Same(c, h.View.Host.Items[1]);
    }

    [Fact]
    public void ARecycledCell_RendersItsNewItemsState()
    {
        var h = Mount(position: 1);
        var cell = Cells.Bind(h.View.ItemTemplate, h.View.Host.Items[1]);
        Assert.Equal("b *", Cells.Text(cell));
        cell.BindingContext = h.View.Host.Items[3];
        Assert.Equal("d", Cells.Text(cell));
    }
}

/// <summary>Spec 070 §5.5: the indicator's controlled position is clamped to its count.</summary>
public class IndicatorViewTests
{
    [Fact]
    public void ThePosition_IsClampedToTheCount()
    {
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(IndicatorView(3, 9));
        var view = host.View<Microsoft.Maui.Controls.IndicatorView>();
        Assert.Equal((3, 2), (view.Count, view.Position));
        host.Render(IndicatorView(0, 2));
        Assert.Equal((0, 0), (view.Count, view.Position));
        host.Render(IndicatorView(5, -1));
        Assert.Equal(0, view.Position);
    }
}
