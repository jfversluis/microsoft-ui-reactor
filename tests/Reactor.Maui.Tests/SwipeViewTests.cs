using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiSwipeView = Microsoft.Maui.Controls.SwipeView;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>Spec 070 §3.5: swipe items on four sides, content items, threshold, events and liveness.</summary>
public class SwipeViewTests
{
    private sealed class Badge : Component<(string Name, List<string> Log)>
    {
        public static readonly Dictionary<string, Action<int>> Set = [];

        public override Element Render()
        {
            var (count, setCount) = UseState(0);
            Set[Props.Name] = setCount;
            UseEffect(() => () => Props.Log.Add($"cleanup {Props.Name}"));
            return Label($"{Props.Name} {count}");
        }
    }

    private static Element BadgeOf(string name, List<string> log) => Component<Badge, (string, List<string>)>((name, log));

    private static void Invoke(object item) => ((Microsoft.Maui.ISwipeItem)item).OnInvoked();

    [Fact]
    public void FourSides_TheirModes_AndTheThreshold()
    {
        var calls = new List<string>();
        SwipeViewElement Swipe(double? threshold) => SwipeView(Label("row")) with
        {
            LeftItems = [new SwipeItemData("l", () => calls.Add("l")) { BackgroundColor = Colors.Green }],
            RightItems = [new SwipeItemData("r1", () => calls.Add("r1")), new SwipeItemData("r2", () => calls.Add("r2"))],
            TopItems = [new SwipeItemData("t", () => calls.Add("t"))],
            BottomItems = [new SwipeItemData("b", () => calls.Add("b"))],
            LeftItemsMode = SwipeMode.Execute,
            BottomItemsMode = SwipeMode.Execute,
            Threshold = threshold,
        };
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Swipe(80));
        var view = host.View<MauiSwipeView>();

        Assert.Equal((SwipeMode.Execute, SwipeMode.Reveal, SwipeMode.Reveal, SwipeMode.Execute),
            (view.LeftItems.Mode, view.RightItems.Mode, view.TopItems.Mode, view.BottomItems.Mode));
        Assert.Equal(["r1", "r2"], view.RightItems.Cast<SwipeItem>().Select(i => i.Text));
        Assert.Equal(Colors.Green, ((SwipeItem)view.LeftItems[0]).BackgroundColor);
        Assert.Equal(80, view.Threshold);
        foreach (var items in new[] { view.LeftItems, view.RightItems, view.TopItems, view.BottomItems })
            foreach (var item in items) Invoke(item);
        Assert.Equal(["l", "r1", "r2", "t", "b"], calls);

        host.Render(Swipe(null));
        Assert.False(view.IsSet(MauiSwipeView.ThresholdProperty));
    }

    [Fact]
    public void AChangedTextColorOrIcon_RebuildsOnlyThatSide()
    {
        SwipeViewElement Swipe(string text, Color? color = null) => SwipeView(Label("row")) with
        {
            LeftItems = [new SwipeItemData("keep", () => { })],
            RightItems = [new SwipeItemData(text, () => { }) { BackgroundColor = color }],
        };
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Swipe("a"));
        var view = host.View<MauiSwipeView>();
        var (left, right) = (view.LeftItems, view.RightItems);

        host.Render(Swipe("a"));
        Assert.Same(right, view.RightItems);
        host.Render(Swipe("b"));
        Assert.NotSame(right, view.RightItems);
        right = view.RightItems;
        host.Render(Swipe("b", Colors.Red));
        Assert.NotSame(right, view.RightItems);
        Assert.Same(left, view.LeftItems);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AContentItem_RerendersInPlace_AndUnmountsWhenRemoved(bool direct)
    {
        var log = new List<string>();
        var calls = new List<string>();
        SwipeViewElement Swipe(bool withContent, string callback) => SwipeView(Label("row")) with
        {
            RightItems = withContent
                ? [new SwipeItemData("Done", () => calls.Add(callback)) { Content = BadgeOf("badge", log) }]
                : [],
        };
        var host = new TestHost(direct ? DirectHandlers.CreateRegistry() : MauiHandlers.CreateRegistry()).Render(Swipe(true, "first"));
        var view = host.View<MauiSwipeView>();
        var items = view.RightItems;
        var itemView = Assert.IsType<SwipeItemView>(items[0]);
        Assert.Equal("Done", SemanticProperties.GetDescription(itemView));
        Badge.Set["badge"](4);
        host.Scheduler.Drain();

        host.Render(Swipe(true, "second"));
        Assert.Same(items, view.RightItems);
        var text = itemView.Content switch
        {
            MauiLabel label => label.Text,
            DirectViewHost wrapper => ((DirectLabel)wrapper.Children[0]).Text,
            _ => "<none>",
        };
        Assert.Equal("badge 4", text);
        Invoke(itemView);
        Assert.Equal(["second"], calls);
        Assert.Empty(log);

        host.Render(Swipe(false, "second"));
        Assert.Equal(["cleanup badge"], log);
    }

    [Fact]
    public void Invoking_ReachesTheLiveCallback_AndNothingAfterUnmount()
    {
        var calls = new List<string>();
        SwipeViewElement Swipe(string name) => SwipeView(Label("row")) with { RightItems = [new SwipeItemData("x", () => calls.Add(name))] };
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Swipe("first"));
        var item = host.View<MauiSwipeView>().RightItems[0];

        host.Render(Swipe("second"));
        Invoke(item);
        host.Root.Unmount();
        Invoke(item);

        Assert.Equal(["second"], calls);
    }

    [Fact]
    public void ARetiredItem_InvokesNothing()
    {
        var calls = new List<string>();
        SwipeViewElement Swipe(string text, string name) => SwipeView(Label("row")) with { RightItems = [new SwipeItemData(text, () => calls.Add(name))] };
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Swipe("a", "old"));
        var view = host.View<MauiSwipeView>();
        var old = view.RightItems[0];

        host.Render(Swipe("b", "new"));
        Invoke(old);
        Assert.Empty(calls);
        Invoke(view.RightItems[0]);
        Assert.Equal(["new"], calls);
    }

    [Fact]
    public void SwipeEvents_AreReported()
    {
        var events = new List<string>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(SwipeView(Label("row")) with
        {
            OnSwipeStarted = d => events.Add($"started {d}"),
            OnSwipeChanging = (d, offset) => events.Add($"changing {d} {offset}"),
            OnSwipeEnded = (d, open) => events.Add($"ended {d} {open}"),
        });
        var controller = (ISwipeViewController)host.View<MauiSwipeView>();

        controller.SendSwipeStarted(new SwipeStartedEventArgs(SwipeDirection.Left));
        controller.SendSwipeChanging(new SwipeChangingEventArgs(SwipeDirection.Left, 12));
        controller.SendSwipeEnded(new SwipeEndedEventArgs(SwipeDirection.Left, true));

        Assert.Equal(["started Left", "changing Left 12", "ended Left True"], events);
    }
}
