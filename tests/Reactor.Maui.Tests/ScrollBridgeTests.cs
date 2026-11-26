using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiEntry = Microsoft.Maui.Controls.Entry;
using MauiScrollView = Microsoft.Maui.Controls.ScrollView;

namespace Microsoft.UI.Reactor.Maui.Tests;

public class ScrollBridgeTests
{
    // Connects a view to a recording handler and gives it a frame, as a layout pass would.
    private static StubViewHandler Place(IView view, Rect frame)
    {
        var handler = new StubViewHandler();
        if (view is VisualElement visual) visual.Handler = handler;
        else ((DirectView)view).Handler = handler;
        view.Arrange(frame);
        return handler;
    }

    private static ScrollToRequest? LastRequest(StubViewHandler handler) =>
        handler.Commands.Where(c => c.Command == nameof(IScrollView.RequestScrollTo)).Select(c => c.Args).OfType<ScrollToRequest>().LastOrDefault();

    /// <summary>
    /// A target in a direct Grid (second row, offset in the stack) in a direct VStack in the scroll
    /// view's (offset) content: every frame origin is distinct and nonzero.
    /// </summary>
    private static (TestHost Host, MauiScrollView Scroll, StubViewHandler ScrollHandler, ElementRef<IView> Target) NestedDirect()
    {
        var target = TypedElementRef.Create<IView>();
        var host = new TestHost(DirectHandlers.CreateRegistry()).Render(ScrollView(
            VStack(Label("top"), Grid("40,*", "*", Label("x"), Entry("t").Ref(target).Grid(1, 0)))));
        var scroll = host.View<MauiScrollView>();
        var content = (Grid)scroll.Content!;
        var stack = (DirectStackLayout)content[0];
        var grid = (DirectGrid)stack[1];
        var scrollHandler = Place(scroll, new Rect(0, 0, 300, 400));
        // Every origin differs and is nonzero, so a skipped or doubled level shows.
        Place(content, new Rect(2, 3, 300, 2000));
        Place(stack, new Rect(4, 6, 296, 1990));
        Place(grid, new Rect(15, 100, 270, 600));
        Place(target.Current!, new Rect(0, 40, 270, 30));
        return (host, scroll, scrollHandler, target);
    }

    [Theory]
    [InlineData(ScrollToPosition.Start, 21, 149)]
    [InlineData(ScrollToPosition.Center, 21 - 150 + 135, 149 - 200 + 15)]
    [InlineData(ScrollToPosition.End, 21 - 300 + 270, 149 - 400 + 30)]
    public void ATargetInNestedDirectLayouts_IsScrolledToBySummingItsVisualParentsFrames(ScrollToPosition position, double x, double y)
    {
        var (_, scroll, handler, target) = NestedDirect();

        _ = scroll.ScrollToAsync(target, position, animated: false);

        var request = LastRequest(handler);
        Assert.NotNull(request);
        Assert.Equal((x, y), (request.HorizontalOffset, request.VerticalOffset));
    }

    [Fact]
    public void AControlsTargetInNestedDirectLayouts_IsPlacedThroughTheLayoutItWasAttachedTo()
    {
        var target = TypedElementRef.Create<Microsoft.Maui.Controls.Label>();
        var host = new TestHost(DirectHandlers.CreateRegistry()).Render(ScrollView(
            VStack(Label("top"), Grid("40,*", "*", Label("x"), Label("t").Ref(target).Grid(1, 0)))));
        var scroll = host.View<MauiScrollView>();
        var content = (Grid)scroll.Content!;
        var stack = (DirectStackLayout)content[0];
        var grid = (DirectGrid)stack[1];
        // Its Controls parent is the anchor, which skips both direct layouts.
        Assert.Same(content, target.Current!.Parent);
        var handler = Place(scroll, new Rect(0, 0, 300, 400));
        // Every origin differs and is nonzero, so a skipped or doubled level shows.
        Place(content, new Rect(2, 3, 300, 2000));
        Place(stack, new Rect(4, 6, 296, 1990));
        Place(grid, new Rect(15, 100, 270, 600));
        Place(target.Current!, new Rect(0, 40, 270, 30));

        _ = scroll.ScrollToAsync(target, ScrollToPosition.Start, animated: false);

        Assert.Equal((21.0, 149.0), (LastRequest(handler)!.HorizontalOffset, LastRequest(handler)!.VerticalOffset));
    }

    [Fact]
    public void MakeVisible_ForAVisibleTarget_KeepsThePosition_AndForAnotherScrollsToIt()
    {
        var (_, scroll, handler, target) = NestedDirect();

        _ = scroll.ScrollToAsync(target, ScrollToPosition.MakeVisible, animated: false);
        Assert.Equal((0.0, 0.0), (LastRequest(handler)!.HorizontalOffset, LastRequest(handler)!.VerticalOffset));

        target.Current!.Arrange(new Rect(0, 500, 270, 30));
        _ = scroll.ScrollToAsync(target, ScrollToPosition.MakeVisible, animated: false);
        Assert.Equal(609 - 400 + 30, LastRequest(handler)!.VerticalOffset);
    }

    [Fact]
    public void ADirectTargetInAControlsStack_IsComputedToo()
    {
        var target = TypedElementRef.Create<IEntry>();
        // OnTap makes the stack a Controls layout; the IEntry ref keeps the entry direct.
        var host = new TestHost(DirectHandlers.CreateRegistry()).Render(ScrollView(VStack(Label("top"), Entry("t").Ref(target)).OnTap(() => { })));
        var scroll = host.View<MauiScrollView>();
        var stack = Assert.IsType<VerticalStackLayout>(scroll.Content);
        Assert.IsType<DirectEntry>(target.Current);
        var handler = Place(scroll, new Rect(0, 0, 300, 400));
        Place(stack, new Rect(0, 8, 300, 2000));
        Place(target.Current!, new Rect(0, 300, 300, 30));
        var requests = new List<ScrollToRequestedEventArgs>();
        scroll.ScrollToRequested += (_, e) => requests.Add(e);

        _ = scroll.ScrollToAsync(target, ScrollToPosition.Start, animated: false);

        Assert.Equal(ScrollToMode.Position, Assert.Single(requests).Mode);
        Assert.Equal(308, LastRequest(handler)!.VerticalOffset);
    }

    [Fact]
    public void AnAllControlsChain_UsesMauisOwnElementOverload()
    {
        var target = TypedElementRef.Create<MauiEntry>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(ScrollView(VStack(Label("top"), Entry("t").Ref(target))));
        var scroll = host.View<MauiScrollView>();
        var stack = (VerticalStackLayout)scroll.Content!;
        Place(scroll, new Rect(0, 0, 300, 400));
        Place(stack, new Rect(0, 0, 300, 2000));
        Place(target.Current!, new Rect(0, 300, 300, 30));
        var requests = new List<ScrollToRequestedEventArgs>();
        scroll.ScrollToRequested += (_, e) => requests.Add(e);

        _ = scroll.ScrollToAsync(target, ScrollToPosition.Start, animated: false);

        var request = Assert.Single(requests);
        Assert.Equal(ScrollToMode.Element, request.Mode);
        Assert.Same(target.Current, request.Element);
    }

    [Fact]
    public async Task OrientationNeither_CompletesFalse_WithoutARequest()
    {
        var (_, scroll, _, target) = NestedDirect();
        scroll.Orientation = ScrollOrientation.Neither;
        var requests = 0;
        scroll.ScrollToRequested += (_, _) => requests++;

        Assert.False(await scroll.ScrollToAsync(target, ScrollToPosition.Start, animated: false).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task NotReady_ReturnsFalse_AndRequestsNothing()
    {
        var target = TypedElementRef.Create<IView>();
        var host = new TestHost(DirectHandlers.CreateRegistry()).Render(ScrollView(VStack(Label("top"), Entry("t").Ref(target))));
        var scroll = host.View<MauiScrollView>();
        var requests = 0;
        scroll.ScrollToRequested += (_, _) => requests++;

        Assert.False(await scroll.ScrollToAsync(TypedElementRef.Create<IView>(), ScrollToPosition.Start, false).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.False(await scroll.ScrollToAsync(target, ScrollToPosition.Start, false).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        // Everything placed but the direct stack between the target and the content.
        var content = (Grid)scroll.Content!;
        Place(scroll, new Rect(0, 0, 300, 400));
        Place(content, new Rect(0, 0, 300, 2000));
        Place(target.Current!, new Rect(0, 300, 300, 30));
        Assert.False(await scroll.ScrollToAsync(target, ScrollToPosition.Start, false).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        Assert.Equal(0, requests);
    }

    [Fact]
    public void AnUnrelatedTarget_Throws()
    {
        var (_, scroll, _, _) = NestedDirect();
        var elsewhere = TypedElementRef.Create<IView>();
        var other = new TestHost(DirectHandlers.CreateRegistry()).Render(ScrollView(VStack(Label("x").Ref(elsewhere))));
        Assert.NotNull(other);

        Assert.Throws<InvalidOperationException>(() => { _ = scroll.ScrollToAsync(elsewhere, ScrollToPosition.Start, false); });
    }

    [Fact]
    public void ATargetWhoseSubtreeWasDetachedFromTheScrollView_Throws()
    {
        var target = TypedElementRef.Create<IView>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(VStack(Label("top"), Label("target").Ref(target)));
        var scroll = new MauiScrollView { Content = (View)host.RootView! };
        Place(scroll, new Rect(0, 0, 300, 400));

        // The content is replaced; the target's own tree is still mounted.
        scroll.Content = new Label();

        Assert.NotNull(target.Current);
        Assert.Throws<InvalidOperationException>(() => { _ = scroll.ScrollToAsync(target, ScrollToPosition.Start, false); });
    }

    [Fact]
    public void AControlsChildMovedBetweenDirectLayouts_KeepsItsNewVisualParent()
    {
        var first = new DirectStackLayout(StackOrientation.Vertical);
        var second = new DirectStackLayout(StackOrientation.Vertical);
        var label = new Label();
        first.Add(label);
        Assert.Same(first, DirectLayout.VisualParentOf(label));

        // Attached to the new layout before it is detached from the old one.
        second.Add(label);
        first.Remove(label);

        Assert.Same(second, DirectLayout.VisualParentOf(label));
        second.Remove(label);
        Assert.Null(DirectLayout.VisualParentOf(label));
    }
}

/// <summary>The bridge's completion protocol, driven through a scroll view Reactor renders.</summary>
public class ScrollBridgeProtocolTests
{
    private static (TestHost Host, MauiScrollView Scroll, StubViewHandler Handler) Mount()
    {
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(VStack(ScrollView(VStack(Label("content")))));
        var scroll = (MauiScrollView)((IList<IView>)host.RootView!)[0];
        var handler = new StubViewHandler();
        scroll.Handler = handler;
        ((IView)scroll).Arrange(new Rect(0, 0, 300, 400));
        var content = (VerticalStackLayout)scroll.Content!;
        content.Handler = new StubViewHandler();
        ((IView)content).Arrange(new Rect(0, 0, 300, 2000));
        // Stale-finish checks wait for the test to run them (the headless dispatcher would run them at once).
        ScrollBridge.DelayOverride.Value = static (_, _) => { };
        return (host, scroll, handler);
    }

    private static async Task<bool> Completed(Task<bool> task)
    {
        Assert.True(task.IsCompleted, "the bridge's task completed");
        return await task;
    }

    private static ScrollBridge Bridge(MauiScrollView scroll) => ((ReactorScrollView)scroll).Bridge;

    private static Task<bool> Request(MauiScrollView scroll, double y) =>
        Bridge(scroll).Start(new Point(0, y), () => scroll.ScrollToAsync(0, y, true));

    private static void Finish(MauiScrollView scroll, double y)
    {
        scroll.SetScrolledPosition(0, y);
        ((IScrollView)scroll).ScrollFinished();
    }

    [Fact]
    public async Task AFinishAtTheTarget_ConfirmsIt()
    {
        var (_, scroll, _) = Mount();
        var task = Request(scroll, 300);
        Assert.False(task.IsCompleted);

        Finish(scroll, 300);

        Assert.True(task.IsCompletedSuccessfully);
        Assert.True(await Completed(task));
    }

    [Fact]
    public async Task ALaterRequest_OfAnyKind_SupersedesTheOutstandingOne()
    {
        var (_, scroll, _) = Mount();
        var first = Request(scroll, 300);
        var second = Request(scroll, 500);
        Assert.True(first.IsCompleted);
        Assert.False(await Completed(first));

        _ = scroll.ScrollToAsync(0, 100, true);
        Assert.True(second.IsCompleted);
        Assert.False(await Completed(second));
    }

    [Fact]
    public async Task StaggeredAnimations_AnEarlierFinishCantConfirmTheLaterRequest()
    {
        var (_, scroll, _) = Mount();
        _ = Request(scroll, 300);
        var later = Request(scroll, 900);

        // The first animation ends (at its own target) while the second still runs.
        Finish(scroll, 300);
        Assert.False(later.IsCompleted);

        Finish(scroll, 900);
        Assert.True(await Completed(later));
    }

    [Fact]
    public async Task AnEarlierAnimationEndedInsideTheDispatch_DoesntSettleTheNewRequest()
    {
        var (_, scroll, handler) = Mount();
        _ = Request(scroll, 300);
        // UIKit ends the running animation, at its interrupted position, when the next one starts.
        handler.OnCommand = command =>
        {
            if (command == nameof(IScrollView.RequestScrollTo)) Finish(scroll, 70);
        };
        var later = Request(scroll, 900);
        handler.OnCommand = null;
        Assert.False(later.IsCompleted, "the interrupted animation's finish isn't the later request's");
        Assert.Equal(1, Bridge(scroll).ExpectedFinishes);

        Finish(scroll, 900);
        Assert.True(await Completed(later));
    }

    [Fact]
    public async Task AFinishElsewhere_WithNoEarlierFinishExpected_CompletesFalse()
    {
        var (_, scroll, _) = Mount();
        var task = Request(scroll, 300);

        Finish(scroll, 120);

        Assert.True(task.IsCompleted);
        Assert.False(await Completed(task));
    }

    [Fact]
    public async Task ATargetBeyondTheRange_IsConfirmedAtTheRangeEnd()
    {
        var (_, scroll, _) = Mount();
        var task = Request(scroll, 1800);

        Finish(scroll, 1600);

        Assert.True(await Completed(task));
    }

    [Fact]
    public async Task AHandlerDisconnectAndAnUnmount_CompleteFalse()
    {
        var (host, scroll, _) = Mount();
        var task = Request(scroll, 300);
        scroll.Handler = null;
        Assert.False(await Completed(task));

        scroll.Handler = new StubViewHandler();
        var next = Request(scroll, 300);
        host.Render(VStack());
        Assert.True(next.IsCompleted);
        Assert.False(await Completed(next));
    }

    [Fact]
    public async Task ADispatchThatThrows_CompletesFalse_AndLeavesNothingOutstanding()
    {
        var (_, scroll, _) = Mount();
        var failed = Bridge(scroll).Start(new Point(0, 300), () => throw new InvalidOperationException("no handler"));
        Assert.True(failed.IsCompleted);
        Assert.False(await Completed(failed));

        var next = Request(scroll, 300);
        // A finish elsewhere ends it: no earlier finish is still expected from the failed request.
        Finish(scroll, 10);
        Assert.True(next.IsCompleted);
        Assert.False(await Completed(next));
    }

    [Fact]
    public async Task AContinuationThatStartsAnotherScroll_IsNotCompletedByTheFinishThatConfirmedTheFirst()
    {
        var (_, scroll, _) = Mount();
        var first = Request(scroll, 300);
        Task? second = null;
        using var finished = new ManualResetEventSlim();
        // An inline continuation (an await resumed on the UI thread) that scrolls again.
        var started = first.ContinueWith(_ =>
        {
            finished.Wait(TimeSpan.FromSeconds(2));
            second = scroll.ScrollToAsync(0, 500, true);
        }, TaskContinuationOptions.ExecuteSynchronously);

        Finish(scroll, 300);
        finished.Set();
        await started.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(await Completed(first));
        Assert.NotNull(second);
        Assert.False(second.IsCompleted, "the new scroll waits for its own finish");
    }

    [Fact]
    public async Task ARequestParkedBeforeTheHandler_AndReplayed_ExpectsOneFinish()
    {
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(VStack(ScrollView(VStack(Label("content")))));
        var scroll = (MauiScrollView)((IList<IView>)host.RootView!)[0];
        var content = (VerticalStackLayout)scroll.Content!;
        content.Handler = new StubViewHandler();
        ((IView)content).Arrange(new Rect(0, 0, 300, 2000));
        ((IView)scroll).Arrange(new Rect(0, 0, 300, 400));

        // The app scrolls before the handler exists: MAUI parks it and raises ScrollToRequested again
        // when it replays it on the handler's arrival, but dispatches it once.
        _ = scroll.ScrollToAsync(0, 100, false);
        var handler = new StubViewHandler();
        scroll.Handler = handler;
        Assert.Single(handler.Commands, c => c.Command == nameof(IScrollView.RequestScrollTo));
        Assert.Equal(1, Bridge(scroll).ExpectedFinishes);
        Finish(scroll, 100);

        var later = Request(scroll, 800);
        Finish(scroll, 420);

        Assert.False(await Completed(later), "a later request that ends elsewhere settles");
    }

    [Fact]
    public async Task ARequestParkedForGeometry_AndDroppedByTheBridgesOwn_ExpectsNoFinish()
    {
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(VStack(ScrollView(VStack(Label("content")))));
        var scroll = (MauiScrollView)((IList<IView>)host.RootView!)[0];
        scroll.Handler = new StubViewHandler();
        ((IView)scroll).Arrange(new Rect(0, 0, 300, 400));
        var content = (VerticalStackLayout)scroll.Content!;
        content.Handler = new StubViewHandler();
        ScrollBridge.DelayOverride.Value = static (_, _) => { };

        // The app scrolls to an element before the content is laid out: MAUI parks the request.
        _ = scroll.ScrollToAsync((Microsoft.Maui.Controls.Element)content[0], ScrollToPosition.Start, true);
        Assert.Equal(1, Bridge(scroll).ExpectedFinishes);
        ((IView)content).Arrange(new Rect(0, 0, 300, 2000));

        // The bridge's own request drops it (MAUI finishes a dropped request without the handler).
        var request = Request(scroll, 800);
        Assert.Equal(1, Bridge(scroll).ExpectedFinishes);
        Finish(scroll, 420);
        Assert.False(await Completed(request), "a finish elsewhere, with no earlier finish expected, settles");
    }

    [Fact]
    public async Task ARequestWhoseFinishNeverComes_SettlesByPositionAtTheBound()
    {
        var (_, scroll, _) = Mount();
        var delayed = new List<(TimeSpan Delay, Action Action)>();
        ScrollBridge.DelayOverride.Value = (delay, action) => delayed.Add((delay, action));
        try
        {
            // A user's drag stops the animation part way, and the platform reports no finish.
            var interrupted = Request(scroll, 900);
            scroll.SetScrolledPosition(0, 250);
            var (bound, settle) = Assert.Single(delayed);
            Assert.Equal(ScrollBridge.SettleBound, bound);
            Assert.False(interrupted.IsCompleted);
            settle();
            Assert.False(await Completed(interrupted), "settled where the scroll stopped, not at the target");

            // At its target when the bound passes, it is confirmed.
            var arrived = Request(scroll, 600);
            scroll.SetScrolledPosition(0, 600);
            delayed[^1].Action();
            Assert.True(await Completed(arrived));

            // One that settled at its own finish isn't touched by its bound afterwards.
            var finished = Request(scroll, 100);
            Finish(scroll, 100);
            Assert.True(await Completed(finished));
            var next = Request(scroll, 700);
            delayed[^2].Action();
            Assert.False(next.IsCompleted, "an earlier request's bound doesn't settle a later request");
        }
        finally
        {
            ScrollBridge.DelayOverride.Value = static (_, _) => { };
        }
    }

    [Fact]
    public async Task AnOrientationChangedWhileARequestRuns_DoesntConfirmAnUnreachedTarget()
    {
        var (_, scroll, _) = Mount();
        var delayed = new List<Action>();
        ScrollBridge.DelayOverride.Value = (_, action) => delayed.Add(action);
        try
        {
            var atTheBound = Request(scroll, 900);
            scroll.SetScrolledPosition(0, 250);
            scroll.Orientation = ScrollOrientation.Neither;
            delayed[^1]();
            Assert.False(await Completed(atTheBound), "settled by the bound on the axes it was requested on");

            // A fresh scroll view: the first request's finish is still counted on this one.
            var (_, other, _) = Mount();
            var atAFinish = Request(other, 900);
            other.Orientation = ScrollOrientation.Horizontal;
            Finish(other, 250);
            Assert.False(await Completed(atAFinish), "settled by a finish on the axes it was requested on");
        }
        finally
        {
            ScrollBridge.DelayOverride.Value = static (_, _) => { };
        }
    }

    [Fact]
    public async Task ALateFinish_AfterItsRequestSettledByTheBound_DoesntSettleTheNextRequest()
    {
        var (_, scroll, _) = Mount();
        var delayed = new List<Action>();
        ScrollBridge.DelayOverride.Value = (_, action) => delayed.Add(action);
        try
        {
            _ = Request(scroll, 300);
            var slow = Request(scroll, 900);
            Finish(scroll, 300);
            // The second animation outlasts the bound (a large animator duration scale).
            scroll.SetScrolledPosition(0, 600);
            delayed[^1]();
            Assert.False(await Completed(slow));

            var next = Request(scroll, 1200);
            // The slow animation's finish arrives at last, at its own target.
            Finish(scroll, 900);
            Assert.False(next.IsCompleted, "the late finish was the settled request's, not the next one's");
            Finish(scroll, 1200);
            Assert.True(await Completed(next));
        }
        finally
        {
            ScrollBridge.DelayOverride.Value = static (_, _) => { };
        }
    }

    [Fact]
    public async Task AHandlerReplacedByAnother_CompletesTheOutstandingRequestFalse()
    {
        var (_, scroll, _) = Mount();
        var task = Request(scroll, 900);
        // MAUI's SetHandler swaps a connected handler for another without a null in between.
        scroll.Handler = new StubViewHandler();
        Assert.False(await Completed(task));
        Assert.Equal(0, Bridge(scroll).ExpectedFinishes);

        // The first handler isn't cancelling anything: a request before any handler is replayed.
        var replayed = Request(scroll, 400);
        Finish(scroll, 400);
        Assert.True(await Completed(replayed));
    }

    [Fact]
    public async Task ANegativeTarget_IsConfirmedAtTheStart_AndHorizontalTargetsOnX()
    {
        var (_, scroll, _) = Mount();
        var clamped = Request(scroll, -40);
        Finish(scroll, 0);
        Assert.True(await Completed(clamped));

        ((IView)scroll.Content!).Arrange(new Rect(0, 0, 2000, 2000));
        foreach (var orientation in new[] { ScrollOrientation.Horizontal, ScrollOrientation.Both })
        {
            scroll.Orientation = orientation;
            var target = new Point(120, orientation == ScrollOrientation.Both ? 60 : 0);
            var onX = Bridge(scroll).Start(target, () => scroll.ScrollToAsync(target.X, target.Y, true));
            scroll.SetScrolledPosition(40, target.Y);
            ((IScrollView)scroll).ScrollFinished();
            Assert.False(await Completed(onX), $"{orientation}: X differs");

            var again = Bridge(scroll).Start(target, () => scroll.ScrollToAsync(target.X, target.Y, true));
            scroll.SetScrolledPosition(target.X, target.Y);
            ((IScrollView)scroll).ScrollFinished();
            Assert.True(await Completed(again), $"{orientation}: confirmed on X");
        }
    }
}

/// <summary>The bridge on a scroll view Reactor didn't create.</summary>
public class ForeignScrollViewTests
{
    private static (MauiScrollView Scroll, StubViewHandler Handler) Foreign()
    {
        var scroll = new MauiScrollView { Content = new VerticalStackLayout { new Label() } };
        var handler = new StubViewHandler();
        scroll.Handler = handler;
        ((IView)scroll).Arrange(new Rect(0, 0, 300, 400));
        scroll.Content.Handler = new StubViewHandler();
        ((IView)scroll.Content).Arrange(new Rect(0, 0, 300, 2000));
        ScrollBridge.DelayOverride.Value = static (_, _) => { };
        return (scroll, handler);
    }

    private static Task<bool> Request(MauiScrollView scroll, double y) =>
        ScrollBridge.For(scroll).Start(new Point(0, y), () => scroll.ScrollToAsync(0, y, true));

    private static async Task<bool> Completed(Task<bool> task)
    {
        Assert.True(task.IsCompleted, "the bridge's task completed");
        return await task;
    }

    [Fact]
    public async Task AFinishAtTheTarget_ConfirmsIt_AndOneElsewhereDoesnt()
    {
        var (scroll, _) = Foreign();
        var task = Request(scroll, 300);
        Assert.False(task.IsCompleted);
        scroll.SetScrolledPosition(0, 300);
        ((IScrollView)scroll).ScrollFinished();
        Assert.True(await Completed(task));

        var elsewhere = Request(scroll, 600);
        scroll.SetScrolledPosition(0, 200);
        ((IScrollView)scroll).ScrollFinished();
        Assert.False(await Completed(elsewhere));
    }

    [Fact]
    public async Task ALaterRequest_ADisconnect_AndAFailedDispatch_CompleteFalse()
    {
        var (scroll, _) = Foreign();
        var first = Request(scroll, 300);
        _ = scroll.ScrollToAsync(0, 100, true);
        Assert.False(await Completed(first));

        var second = Request(scroll, 300);
        scroll.Handler = null;
        Assert.False(await Completed(second));

        scroll.Handler = new StubViewHandler();
        var third = Request(scroll, 300);
        scroll.Handler = new StubViewHandler();
        Assert.False(await Completed(third), "a handler replaced by another");

        var failed = ScrollBridge.For(scroll).Start(new Point(0, 1), () => throw new InvalidOperationException("boom"));
        Assert.False(await Completed(failed));
    }

    [Fact]
    public async Task TheExtension_ScrollsAForeignScrollView_ToAReactorTarget()
    {
        ScrollBridge.DelayOverride.Value = static (_, _) => { };
        var target = TypedElementRef.Create<IView>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(VStack(Label("top"), Label("target").Ref(target)));
        var scroll = new MauiScrollView { Content = (View)host.RootView! };
        scroll.Handler = new StubViewHandler();
        ((IView)scroll).Arrange(new Rect(0, 0, 300, 400));
        scroll.Content.Handler = new StubViewHandler();
        ((IView)scroll.Content).Arrange(new Rect(0, 0, 300, 2000));
        ((VisualElement)target.Current!).Handler = new StubViewHandler();
        target.Current!.Arrange(new Rect(0, 700, 300, 30));

        var task = scroll.ScrollToAsync(target, ScrollToPosition.Start, true);
        scroll.SetScrolledPosition(0, 700);
        ((IScrollView)scroll).ScrollFinished();

        Assert.True(await Completed(task));
    }
}
