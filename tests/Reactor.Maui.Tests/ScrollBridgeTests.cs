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
    /// A target in a direct Grid (second row, offset in the stack) in a direct VStack, all
    /// inside the scroll view: every frame origin is nonzero.
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
        Place(content, new Rect(0, 0, 300, 2000));
        Place(stack, new Rect(0, 0, 300, 2000));
        Place(grid, new Rect(15, 100, 270, 600));
        Place(target.Current!, new Rect(0, 40, 270, 30));
        return (host, scroll, scrollHandler, target);
    }

    [Theory]
    [InlineData(ScrollToPosition.Start, 15, 140)]
    [InlineData(ScrollToPosition.Center, 15 - 150 + 135, 140 - 200 + 15)]
    [InlineData(ScrollToPosition.End, 15 - 300 + 270, 140 - 400 + 30)]
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
        Place(content, new Rect(0, 0, 300, 2000));
        Place(stack, new Rect(0, 0, 300, 2000));
        Place(grid, new Rect(15, 100, 270, 600));
        Place(target.Current!, new Rect(0, 40, 270, 30));

        _ = scroll.ScrollToAsync(target, ScrollToPosition.Start, animated: false);

        Assert.Equal((15.0, 140.0), (LastRequest(handler)!.HorizontalOffset, LastRequest(handler)!.VerticalOffset));
    }

    [Fact]
    public void MakeVisible_ForAVisibleTarget_KeepsThePosition_AndForAnotherScrollsToIt()
    {
        var (_, scroll, handler, target) = NestedDirect();

        _ = scroll.ScrollToAsync(target, ScrollToPosition.MakeVisible, animated: false);
        Assert.Equal((0.0, 0.0), (LastRequest(handler)!.HorizontalOffset, LastRequest(handler)!.VerticalOffset));

        target.Current!.Arrange(new Rect(0, 500, 270, 30));
        _ = scroll.ScrollToAsync(target, ScrollToPosition.MakeVisible, animated: false);
        Assert.Equal(600 - 400 + 30, LastRequest(handler)!.VerticalOffset);
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
        return (host, scroll, handler);
    }

    private static async Task<bool> Completed(Task<bool> task)
    {
        Assert.True(task.IsCompleted, "the bridge's task completed");
        return await task;
    }

    private static ScrollBridge Bridge(MauiScrollView scroll) => ((ReactorScrollView)scroll).Bridge;

    private static Task<bool> Request(MauiScrollView scroll, double y) =>
        Bridge(scroll).Start(scroll, new Point(0, y), () => scroll.ScrollToAsync(0, y, true));

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
        var failed = Bridge(scroll).Start(scroll, new Point(0, 300), () => throw new InvalidOperationException("no handler"));
        Assert.True(failed.IsCompleted);
        Assert.False(await Completed(failed));

        var next = Request(scroll, 300);
        // A finish elsewhere ends it: no earlier finish is still expected from the failed request.
        Finish(scroll, 10);
        Assert.True(next.IsCompleted);
        Assert.False(await Completed(next));
    }
}
