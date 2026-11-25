using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiPicker = Microsoft.Maui.Controls.Picker;
using MauiSlider = Microsoft.Maui.Controls.Slider;
using MauiSwitch = Microsoft.Maui.Controls.Switch;

namespace Microsoft.UI.Reactor.Maui.Tests;

public class ModifierTests
{
    private static TestHost Host() => new(MauiHandlers.CreateRegistry());

    [Fact]
    public void Modifiers_AreApplied_AndResetWhenRemoved()
    {
        var host = Host().Render(VStack(Label("a")
            .Height(40).Opacity(0.5).IsVisible(false).BackgroundColor(Colors.Red)
            .Grid(1, 2, columnSpan: 3).Semantics("description", headingLevel: SemanticHeadingLevel.Level1)));
        var label = (MauiLabel)host.View<VerticalStackLayout>()[0];
        Assert.Equal(40, label.HeightRequest);
        Assert.Equal(0.5, label.Opacity);
        Assert.False(label.IsVisible);
        Assert.Equal(Colors.Red, label.BackgroundColor);
        Assert.Equal((1, 2, 3), (Microsoft.Maui.Controls.Grid.GetRow(label), Microsoft.Maui.Controls.Grid.GetColumn(label), Microsoft.Maui.Controls.Grid.GetColumnSpan(label)));
        Assert.Equal("description", SemanticProperties.GetDescription(label));
        Assert.Equal(SemanticHeadingLevel.Level1, SemanticProperties.GetHeadingLevel(label));

        host.Render(VStack(Label("a")));

        Assert.Equal(-1, label.HeightRequest);
        Assert.Equal(1, label.Opacity);
        Assert.True(label.IsVisible);
        Assert.Null(label.BackgroundColor);
        Assert.Equal((0, 0, 1), (Microsoft.Maui.Controls.Grid.GetRow(label), Microsoft.Maui.Controls.Grid.GetColumn(label), Microsoft.Maui.Controls.Grid.GetColumnSpan(label)));
        Assert.Null(SemanticProperties.GetDescription(label));
    }

    [Fact]
    public void OnTap_AddsOneRecognizer_ThatCallsTheCurrentCallback_AndIsRemovedWithTheModifier()
    {
        var taps = new List<string>();
        var host = Host().Render(VStack(Label("a").OnTap(() => taps.Add("first"))));
        var label = (MauiLabel)host.View<VerticalStackLayout>()[0];
        var tap = Assert.IsType<TapGestureRecognizer>(Assert.Single(label.GestureRecognizers));

        host.Render(VStack(Label("a").OnTap(() => taps.Add("second"), taps: 2)));
        Assert.Same(tap, Assert.Single(label.GestureRecognizers));
        Assert.Equal(2, tap.NumberOfTapsRequired);
        tap.SendTapped(label);
        Assert.Equal(["second"], taps);

        host.Render(VStack(Label("a")));
        Assert.Empty(label.GestureRecognizers);
    }

    [Fact]
    public void Set_RunsAgainstTheNativeView_OnMountAndUpdate()
    {
        var host = Host().Render(VStack(Label("a").Set<LabelElement, MauiLabel>(l => l.MaxLines = 3)));
        var label = (MauiLabel)host.View<VerticalStackLayout>()[0];
        Assert.Equal(3, label.MaxLines);

        label.MaxLines = 7;
        host.Render(VStack(Label("b").Set<LabelElement, MauiLabel>(l => l.MaxLines = 3)));
        Assert.Equal(3, label.MaxLines);
    }

    [Fact]
    public void GridDefinitions_AreParsed_AndInvalidOnesAreRejected()
    {
        var host = Host().Render(Grid("Auto,*,2*,48", "*", Label("a")));
        var grid = host.View<Microsoft.Maui.Controls.Grid>();

        Assert.Equal(
            [GridLength.Auto, GridLength.Star, new GridLength(2, GridUnitType.Star), new GridLength(48)],
            grid.RowDefinitions.Select(r => r.Height));
        Assert.Throws<FormatException>(() => GridLengths.Parse("Auto,wide"));
    }
}

public sealed class RejectingSwitch : Component<List<bool>>
{
    public override Element Render() => Switch(false, Props.Add);
}

public sealed class RangeSlider : Component<(double Min, double Max, double Value)>
{
    public override Element Render() => Slider(Props.Value, null, Props.Min, Props.Max);
}

public class ControlledControlTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Switch_ReportsTheToggle_AndReturnsToTheDeclaredValueWhenNotAccepted(bool direct)
    {
        var reported = new List<bool>();
        var host = new TestHost(direct ? DirectHandlers.CreateRegistry() : MauiHandlers.CreateRegistry())
            .Render(Component<RejectingSwitch, List<bool>>(reported));

        // What the platform handler does when the user flips the switch.
        ((ISwitch)host.RootView!).IsOn = true;
        Assert.Equal([true], reported);
        host.Scheduler.Drain();

        Assert.False(((ISwitch)host.RootView!).IsOn);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InputAfterTheControlIsUnmounted_IsNotReported(bool direct)
    {
        var reported = new List<string>();
        var host = new TestHost(direct ? DirectHandlers.CreateRegistry() : MauiHandlers.CreateRegistry())
            .Render(VStack(
                Entry("a", v => reported.Add($"entry {v}")) with { OnCompleted = () => reported.Add("completed") },
                Switch(false, v => reported.Add($"switch {v}")),
                Button("b", () => reported.Add("click")),
                Slider(0.5, _ => { }) with { OnDragCompleted = () => reported.Add("drag completed") }));
        var children = (IList<IView>)host.RootView!;
        var (entry, toggle, button, slider) = ((IEntry)children[0], (ISwitch)children[1], (IButton)children[2], (ISlider)children[3]);

        // What the platform handlers do on input. While mounted, each is reported. Completed, Clicked
        // and DragCompleted have no echo check, so only Live keeps them from an unmounted element.
        void Input(string text, bool on)
        {
            entry.Text = text;
            entry.Completed();
            toggle.IsOn = on;
            button.Clicked();
            slider.DragCompleted();
        }
        Input("typed", true);
        host.Scheduler.Drain();
        Assert.Equal(["entry typed", "completed", "switch True", "click", "drag completed"], reported);
        reported.Clear();

        host.Root.Unmount();
        Input("after", false);
        host.Scheduler.Drain();

        Assert.Empty(reported);
    }

    private sealed record CustomInputElement(string Text, Action<string> OnChanged) : Element;

    // A third-party handler that follows the documented pattern: IsEchoing first, then Current.
    private sealed class CustomInputHandler : Microsoft.UI.Reactor.Hosting.IElementHandler<CustomInputElement, Microsoft.Maui.Controls.Entry>
    {
        public Microsoft.Maui.Controls.Entry Mount(Microsoft.UI.Reactor.Hosting.HandlerContext<CustomInputElement> context, CustomInputElement element)
        {
            var view = new Microsoft.Maui.Controls.Entry { Text = element.Text };
            view.TextChanged += (_, e) =>
            {
                if (context.IsEchoing(view)) return;
                context.Current.OnChanged(e.NewTextValue ?? "");
            };
            return view;
        }

        public void Update(Microsoft.UI.Reactor.Hosting.HandlerContext<CustomInputElement> context, CustomInputElement oldElement, CustomInputElement newElement, Microsoft.Maui.Controls.Entry control) { }
    }

    [Fact]
    public void ToolbarAndSwipeActions_AreNotInvokedAfterUnmount()
    {
        var reported = new List<string>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(
            Page("p", SwipeView(Label("row")) with { RightItems = [new SwipeAction("Delete", () => reported.Add("swipe"))] })
                with { ToolbarItems = [new ToolbarAction("Save", () => reported.Add("toolbar"))] });
        var page = (Microsoft.Maui.Controls.ContentPage)host.RootView!;
        var toolbar = (IMenuItemController)page.ToolbarItems[0];
        var swipe = (Microsoft.Maui.ISwipeItem)((Microsoft.Maui.Controls.SwipeView)page.Content).RightItems[0];
        toolbar.Activate();
        swipe.OnInvoked();
        Assert.Equal(["toolbar", "swipe"], reported);
        reported.Clear();

        host.Root.Unmount();
        toolbar.Activate();
        swipe.OnInvoked();

        Assert.Empty(reported);
    }

    private abstract record BadgeElement(string Text) : Element;
    private sealed record BadgeElement<T>(T Value) : BadgeElement(Value?.ToString() ?? "");
    private sealed record PinnedBadge(string Text) : BadgeElement(Text);

    private sealed class BadgeHandler(string prefix) : Microsoft.UI.Reactor.Hosting.IElementHandler<BadgeElement, MauiLabel>
    {
        public MauiLabel Mount(Microsoft.UI.Reactor.Hosting.HandlerContext<BadgeElement> context, BadgeElement element) =>
            new() { Text = prefix + element.Text };

        public void Update(Microsoft.UI.Reactor.Hosting.HandlerContext<BadgeElement> context, BadgeElement oldElement, BadgeElement newElement, MauiLabel control) =>
            control.Text = prefix + newElement.Text;
    }

    private sealed class PinnedHandler : Microsoft.UI.Reactor.Hosting.IElementHandler<PinnedBadge, MauiLabel>
    {
        public MauiLabel Mount(Microsoft.UI.Reactor.Hosting.HandlerContext<PinnedBadge> context, PinnedBadge element) => new() { Text = "pinned " + element.Text };

        public void Update(Microsoft.UI.Reactor.Hosting.HandlerContext<PinnedBadge> context, PinnedBadge oldElement, PinnedBadge newElement, MauiLabel control) =>
            control.Text = "pinned " + newElement.Text;
    }

    [Fact]
    public void AHandlerForDerivedTypes_RendersEveryClosedType_UnlessAnExactOrLaterRegistrationWins()
    {
        var registry = MauiHandlers.CreateRegistry()
            .RegisterForDerivedTypes(new BadgeHandler("old "))
            .RegisterForDerivedTypes(new BadgeHandler("badge "))
            .Register(new PinnedHandler());
        var host = new TestHost(registry).Render(VStack(new BadgeElement<int>(3), new BadgeElement<string>("x"), new PinnedBadge("p")));

        var stack = (VerticalStackLayout)host.RootView!;
        Assert.Equal(["badge 3", "badge x", "pinned p"], stack.Cast<MauiLabel>().Select(l => l.Text));
        var first = stack[0];

        // An update goes through the base handler too, in place.
        host.Render(VStack(new BadgeElement<int>(4), new BadgeElement<string>("x"), new PinnedBadge("p")));
        Assert.Same(first, stack[0]);
        Assert.Equal("badge 4", ((MauiLabel)stack[0]).Text);
    }

    [Fact]
    public void AHandlerThatChecksIsEchoing_DoesNotReportInputAfterItsControlIsUnmounted()
    {
        var reported = new List<string>();
        var registry = MauiHandlers.CreateRegistry().Register(new CustomInputHandler());
        var host = new TestHost(registry).Render(new CustomInputElement("a", reported.Add));
        var entry = (Microsoft.Maui.Controls.Entry)host.RootView!;
        entry.Text = "typed";
        Assert.Equal(["typed"], reported);

        host.Root.Unmount();
        entry.Text = "after";

        Assert.Equal(["typed"], reported);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Slider_MovesToADisjointRange_WithoutAnInvalidIntermediateState(bool direct)
    {
        var host = new TestHost(direct ? DirectHandlers.CreateRegistry() : MauiHandlers.CreateRegistry());
        host.Render(Component<RangeSlider, (double, double, double)>((0, 1, 0.5)));

        host.Render(Component<RangeSlider, (double, double, double)>((10, 20, 15)));
        var up = (IRange)host.RootView!;
        Assert.Equal((10, 20, 15), (up.Minimum, up.Maximum, up.Value));

        host.Render(Component<RangeSlider, (double, double, double)>((-5, -1, -2)));
        var down = (IRange)host.RootView!;
        Assert.Equal((-5, -1, -2), (down.Minimum, down.Maximum, down.Value));
    }

    [Fact]
    public void Picker_KeepsTheSelectedIndex_WhenItsItemsChange()
    {
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Picker(["a", "b"], 1));
        var picker = host.View<MauiPicker>();
        Assert.Equal(1, picker.SelectedIndex);

        host.Render(Picker(["x", "y", "z"], 1));

        Assert.Equal(["x", "y", "z"], picker.ItemsSource.Cast<string>());
        Assert.Equal(1, picker.SelectedIndex);
    }
}

public class HybridTests
{
    [Fact]
    public void AControlsOnlySetting_SwitchesTheElementToAControlsView_AndBack()
    {
        var host = new TestHost(DirectHandlers.CreateRegistry()).Render(VStack(Label("a")));
        var stack = host.View<DirectStackLayout>();
        Assert.IsType<DirectLabel>(stack[0]);

        host.Render(VStack(Label("a").OnTap(() => { })));
        Assert.IsType<MauiLabel>(stack[0]);

        host.Render(VStack(Label("a")));
        Assert.IsType<DirectLabel>(stack[0]);
    }

    [Fact]
    public void DirectViews_ApplyTheSharedModifiers()
    {
        var host = new TestHost(DirectHandlers.CreateRegistry()).Render(Grid("*,*", "*,*",
            Label("a").Grid(1, 1).Opacity(0.5).IsVisible(false).Width(30)));
        var grid = host.View<DirectGrid>();
        var label = (DirectLabel)grid[0];

        Assert.Equal((1, 1), (grid.GetRow(label), grid.GetColumn(label)));
        Assert.Equal(0.5, label.Opacity);
        Assert.Equal(Visibility.Collapsed, label.Visibility);
        Assert.Equal(30, label.Width);

        host.Render(Grid("*,*", "*,*", Label("a")));

        Assert.Equal((0, 0), (grid.GetRow(label), grid.GetColumn(label)));
        Assert.Equal(Visibility.Visible, label.Visibility);
        Assert.True(double.IsNaN(label.Width), "an unset width is NaN in MAUI Core, as Controls reports it");
    }
}

public sealed record Item(string Id, string Title);

public static class Accent
{
    public static readonly Context<string> Color = new("none");
}

public sealed record ListProps(IReadOnlyList<Item> Items, string Accent, bool Reuse = false);

public sealed class ItemList : Component<ListProps>
{
    public override Element Render() => Accent.Color.Provide(Props.Accent,
        CollectionView(Props.Items, i => i.Id, i => Component<ItemRow, Item>(i)) with { ReuseItemTrees = Props.Reuse });
}

public sealed class ItemRow : Component<Item>
{
    public static readonly Dictionary<string, Action<int>> SetCount = [];

    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        SetCount[Props.Id] = setCount;
        return Label($"{Props.Title} {UseContext(Accent.Color)} {count}");
    }
}

public class CollectionViewTests
{
    private static (TestHost Host, CollectionView View) Mount(ListProps props)
    {
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Component<ItemList, ListProps>(props));
        return (host, host.View<CollectionView>());
    }

    private static ContentView Realize(CollectionView view, int index)
    {
        // What the platform does for a visible item: create a cell and bind it.
        var cell = (ContentView)view.ItemTemplate.CreateContent();
        cell.BindingContext = ((System.Collections.IList)view.ItemsSource)[index];
        return cell;
    }

    private static string Text(ContentView cell) => ((MauiLabel)cell.Content).Text;

    [Fact]
    public void Items_AreDiffedByKey_KeepingUnchangedEntries()
    {
        Item a = new("a", "A"), b = new("b", "B"), c = new("c", "C");
        var (host, view) = Mount(new([a, b, c], "red"));
        var source = (System.Collections.IList)view.ItemsSource;
        var boxes = source.Cast<object>().ToArray();

        host.Render(Component<ItemList, ListProps>(new([c, a with { Title = "A2" }, new("d", "D")], "red")));

        Assert.Equal(["c", "a", "d"], source.Cast<ItemBox>().Select(box => box.Key));
        Assert.Same(boxes[2], source[0]);
        Assert.NotSame(boxes[0], source[1]);
        Assert.Equal("A2", ((Item)((ItemBox)source[1]!).Item!).Title);
    }

    [Fact]
    public void Cells_RenderTheTemplate_InheritContext_AndRerenderWhenTheParentDoes()
    {
        var (host, view) = Mount(new([new("a", "A")], "red"));
        var cell = Realize(view, 0);
        Assert.Equal("A red 0", Text(cell));

        host.Render(Component<ItemList, ListProps>(new([new("a", "A")], "blue")));
        host.Scheduler.Drain();

        Assert.Equal("A blue 0", Text(cell));
    }

    [Fact]
    public void Cells_Rerender_WhenTheTemplateCapturesChangedParentState()
    {
        var items = new[] { new Item("a", "A") };
        Element List(string suffix) => CollectionView(items, i => i.Id, i => Label($"{i.Title} {suffix}"));
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(List("one"));
        var cell = Realize(host.View<CollectionView>(), 0);
        Assert.Equal("A one", Text(cell));

        host.Render(List("two"));

        Assert.Equal("A two", Text(cell));
    }

    [Theory]
    [InlineData(false, "B blue 0")]
    [InlineData(true, "B blue 5")]
    public void ARecycledCell_ResetsItemState_UnlessTreesAreReused(bool reuse, string expected)
    {
        var (host, view) = Mount(new([new("a", "A"), new("b", "B")], "blue", reuse));
        var cell = Realize(view, 0);
        ItemRow.SetCount["a"](5);
        host.Scheduler.Drain();
        Assert.Equal("A blue 5", Text(cell));

        cell.BindingContext = ((System.Collections.IList)view.ItemsSource)[1];

        Assert.Equal(expected, Text(cell));
    }

    [Fact]
    public void ACellBoundAfterTheListIsUnmounted_StartsNoTree()
    {
        var (host, view) = Mount(new([new("a", "A"), new("b", "B")], "red"));
        // Positive control: a cell bound while mounted renders.
        Assert.Equal("A red 0", Text(Realize(view, 0)));

        host.Root.Unmount();
        var late = Realize(view, 1);

        Assert.Null(late.Content);
    }

    [Fact]
    public void Selection_IsControlledByKey()
    {
        var selections = new List<IReadOnlyList<string>>();
        var items = new[] { new Item("a", "A"), new Item("b", "B") };
        Element List(string? selected) =>
            CollectionView(items, i => i.Id, i => Label(i.Title)) with
            {
                SelectionMode = SelectionMode.Single,
                SelectedKey = selected,
                OnSelectionChanged = selections.Add,
            };
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(List("b"));
        var view = host.View<CollectionView>();
        Assert.Equal("b", ((ItemBox)view.SelectedItem!).Key);

        // The user selects "a"; the app does not accept it, so the selection returns to "b".
        view.SelectedItem = ((System.Collections.IList)view.ItemsSource)[0];
        host.Scheduler.Drain();

        Assert.Equal(["a"], Assert.Single(selections));
        Assert.Equal("b", ((ItemBox)view.SelectedItem!).Key);
    }
}

public sealed record RatingElement(int Value, Action<int>? OnChanged = null) : ViewElement
{
    public Color? Color { get; init; }
    public string? Hint { get; init; }
    public bool Large { get; init; }
}

public class ControlDescriptorTests
{
    // A third-party control described the way the built-ins are: a stepper showing a rating.
    private static ControlDescriptor<RatingElement, Microsoft.Maui.Controls.Stepper> Rating(List<string> log) =>
        new ControlDescriptor<RatingElement, Microsoft.Maui.Controls.Stepper>(e => { log.Add($"create {(e.Large ? "large" : "small")}"); return new() { Maximum = 10 }; })
            .Property(VisualElement.BackgroundColorProperty, static e => e.Color)
            .Initial(static e => e.Hint, static (v, hint) => v.AutomationId = hint)
            .Controlled(
                static e => (double)e.Value,
                static v => v.Value,
                static (v, value) => v.Value = value,
                static (v, report) => v.ValueChanged += (_, e) => report(e.NewValue),
                static e => e.OnChanged is { } changed ? value => changed((int)value) : null)
            .Event((context, v) => v.Focused += (_, _) => { if (context.Live is { } live) log.Add($"focused {live.Value}"); })
            .WithCanUpdate(static (a, b) => a.Large == b.Large)
            .WithUnmount(v => log.Add($"unmount {v.Value}"));

    private sealed record RatingProps(int Initial, bool Accept, List<string> Log);

    private sealed class RatingApp : Component<RatingProps>
    {
        public override Element Render()
        {
            var (value, setValue) = UseState(Props.Initial);
            var log = Props.Log;
            return new RatingElement(value, v => { log.Add($"changed {v}"); if (Props.Accept) setValue(v); }) { Hint = $"hint {value}" }.Margin(4);
        }
    }

    private static (TestHost Host, Microsoft.Maui.Controls.Stepper View) Mount(RatingProps props)
    {
        var host = new TestHost(MauiHandlers.CreateRegistry().Register(Rating(props.Log))).Render(Component<RatingApp, RatingProps>(props));
        return (host, (Microsoft.Maui.Controls.Stepper)host.RootView!);
    }

    [Fact]
    public void Mount_WritesTheValues_AppliesModifiers_AndReportsNothing()
    {
        var log = new List<string>();
        var (_, view) = Mount(new(3, true, log));

        Assert.Equal(3, view.Value);
        Assert.Equal("hint 3", view.AutomationId);
        Assert.Equal(new Thickness(4), view.Margin);
        Assert.Equal(["create small"], log);
    }

    [Fact]
    public void AcceptedInput_IsReportedOnce_AndKept_AndInitialValuesAreNotRewritten()
    {
        var log = new List<string>();
        var (host, view) = Mount(new(3, true, log));

        view.Value = 5;
        host.Scheduler.Drain();

        Assert.Equal(["create small", "changed 5"], log);
        Assert.Equal(5, view.Value);
        Assert.Equal("hint 3", view.AutomationId);
    }

    [Fact]
    public void RefusedInput_IsReportedOnce_AndTheViewReturnsToTheDeclaredValue()
    {
        var log = new List<string>();
        var (host, view) = Mount(new(3, false, log));

        view.Value = 7;
        host.Scheduler.Drain();

        Assert.Equal(["create small", "changed 7"], log);
        Assert.Equal(3, view.Value);
    }

    [Fact]
    public void PropertiesClear_WhenUnset_AndCanUpdateRemounts_AndUnmountRuns()
    {
        var log = new List<string>();
        var host = new TestHost(MauiHandlers.CreateRegistry().Register(Rating(log)))
            .Render(new RatingElement(1) { Color = Colors.Red });
        var view = (Microsoft.Maui.Controls.Stepper)host.RootView!;
        Assert.Equal(Colors.Red, view.BackgroundColor);

        host.Render(new RatingElement(2));
        Assert.Same(view, host.RootView);
        Assert.Null(view.BackgroundColor);
        Assert.Equal(2, view.Value);

        host.Render(new RatingElement(2) { Large = true });
        Assert.NotSame(view, host.RootView);
        Assert.Equal(["create small", "unmount 2", "create large"], log);
    }

    [Fact]
    public void Events_ReachTheLiveElement_AndNothingAfterUnmount()
    {
        var log = new List<string>();
        var (host, view) = Mount(new(3, false, log));
        ((IView)view).IsFocused = true;
        Assert.Contains("focused 3", log);
        log.Clear();

        host.Root.Unmount();
        ((IView)view).IsFocused = false;
        ((IView)view).IsFocused = true;
        view.Value = 9;

        Assert.Equal(["unmount 3"], log);
    }
}
