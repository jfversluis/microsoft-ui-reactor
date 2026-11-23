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
