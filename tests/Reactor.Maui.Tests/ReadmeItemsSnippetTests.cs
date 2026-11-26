using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiRadioButton = Microsoft.Maui.Controls.RadioButton;

namespace Microsoft.UI.Reactor.Maui.Tests;

// The README's list, swipe, radio, picker and formatted-text snippets (spec 070 §3), compiled and rendered.

public sealed record ReadmeProduct(string Id, string Name);

public sealed class ReadmeMenu : Component
{
    public static readonly ReadmeProduct[] Products = [new("p1", "Tea"), new("p2", "Coffee")];

    public override Element Render()
    {
        var (selected, setSelected) = UseState<string?>("p1");
        var products = Products;
        return CollectionView(products, p => p.Id, (p, state) =>
            Border(Label(p.Name).Padding(12))
                .Stroke(state.IsSelected ? Colors.White : Colors.Gray, state.IsSelected ? 2 : 1)
                .CornerRadius(8))
            with
        {
            Arrangement = ItemsArrangement.VerticalGrid,
            Span = 2,
            ItemSpacing = 12,
            SelectionMode = SelectionMode.Single,
            SelectedKey = selected,
            OnSelectionChanged = keys => setSelected(keys.FirstOrDefault()),
            Header = Label("Menu").FontSize(24),
            EmptyView = Label("Nothing on the menu today"),
        };
    }
}

public sealed record ReadmeMember(string Id, string Name);

public sealed record ReadmeTeam(string Name, IReadOnlyList<ReadmeMember> Members);

public sealed class ReadmeTeams : Component
{
    public override Element Render()
    {
        ReadmeTeam[] teams = [new("Blue", [new("1", "Ann"), new("2", "Bo")]), new("Red", [new("3", "Cy")])];
        return CollectionView(teams, t => t.Name, t => t.Members, m => m.Id,
            m => Label(m.Name),
            groupHeader: t => Label(t.Name).Bold(),
            groupFooter: t => Label($"{t.Members.Count} members"));
    }
}

public sealed class ReadmeTaskRow : Component<List<string>>
{
    public override Element Render()
    {
        var (_, setOpen) = UseState(false);
        var task = (Title: "Write the spec", Id: 1);
        void complete() => Props.Add("complete");
        void delete() => Props.Add("delete");
        var swipe = UseElementRef<SwipeView>();
        return (SwipeView(Label(task.Title).Padding(12)) with
        {
            LeftItems = [new SwipeItemData("Done", complete) { Content = Label("✓").Padding(16) }],
            LeftItemsMode = SwipeMode.Execute,
            RightItems = [new SwipeItemData("Delete", delete) { BackgroundColor = Colors.Red }],
            Threshold = 80,
            OnSwipeEnded = (direction, isOpen) => setOpen(isOpen),
        }).Ref(swipe);
    }
}

public sealed class ReadmeThemes : Component
{
    private static Element ThemeCard(string name, bool on) => Label(on ? $"{name} ✓" : name).Padding(12);

    public override Element Render()
    {
        var (theme, setTheme) = UseState("light");
        return VStack(8,
            (RadioButton(ThemeCard("Light", theme == "light"), theme == "light",
                on => { if (on) setTheme("light"); }, groupName: "theme") with { IsIndicatorVisible = false })
                .Semantics(description: "Light theme"),
            (RadioButton(ThemeCard("Dark", theme == "dark"), theme == "dark",
                on => { if (on) setTheme("dark"); }, groupName: "theme") with { IsIndicatorVisible = false })
                .Semantics(description: "Dark theme"));
    }
}

public sealed class ReadmeTotal : Component<List<string>>
{
    public override Element Render()
    {
        var total = 5m;
        void setShowDetails(bool show) => Props.Add($"details {show}");
        return Label(
            Run("Total ").TextColor(Colors.Gray),
            Run($"{total:C}").FontSize(28).Bold(),
            Hyperlink(" details", () => setShowDetails(true)));
    }
}

public class ReadmeItemsSnippetTests
{
    [Fact]
    public void TheMenuSnippet_DrawsTheDeclaredSelection()
    {
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Component<ReadmeMenu>());
        var view = host.View<CollectionView>();
        var tea = Cells.Realize(view, 0);
        var coffee = Cells.Realize(view, 1);
        static double Stroke(ContentView cell) => ((Border)cell.Content).StrokeThickness;
        Assert.Equal((2d, 1d), (Stroke(tea), Stroke(coffee)));

        view.SelectedItem = Cells.Box(view, "p2");
        host.Scheduler.Drain();

        Assert.Equal((1d, 2d), (Stroke(tea), Stroke(coffee)));
        Assert.Equal("Menu", ((MauiLabel)view.Header).Text);
    }

    [Fact]
    public void TheGroupsSnippet_RendersGroupsWithHeadersAndFooters()
    {
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Component<ReadmeTeams>());
        var view = host.View<CollectionView>();
        var groups = (System.Collections.ObjectModel.ObservableCollection<GroupBox>)view.ItemsSource;
        Assert.Equal(["Blue", "Red"], groups.Select(g => g.Key));
        Assert.Equal("2 members", Cells.Text(Cells.Bind(view.GroupFooterTemplate, groups[0])));
        Assert.True(view.ScrollToKey("3"));
    }

    [Fact]
    public void TheSwipeSnippet_BuildsItsItems()
    {
        var log = new List<string>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Component<ReadmeTaskRow, List<string>>(log));
        var view = host.View<SwipeView>();
        Assert.IsType<SwipeItemView>(view.LeftItems[0]);
        Assert.Equal(SwipeMode.Execute, view.LeftItems.Mode);
        ((Microsoft.Maui.ISwipeItem)view.RightItems[0]).OnInvoked();
        Assert.Equal(["delete"], log);
    }

    [Fact]
    public void TheRadioSnippet_ChecksTheTappedCard()
    {
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Component<ReadmeThemes>());
        var stack = host.View<VerticalStackLayout>();
        var (light, dark) = ((MauiRadioButton)stack[0], (MauiRadioButton)stack[1]);
        Assert.Equal("Dark theme", SemanticProperties.GetDescription(dark));

        ((IRadioButton)dark).IsChecked = true;
        host.Scheduler.Drain();

        Assert.False(light.IsChecked);
        Assert.Equal("Dark ✓", ((MauiLabel)dark.Content).Text);
    }

    [Fact]
    public void TheFormattedTextSnippet_TapsItsHyperlink()
    {
        var log = new List<string>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Component<ReadmeTotal, List<string>>(log));
        var label = host.View<MauiLabel>();
        Assert.Equal(3, label.FormattedText.Spans.Count);
        label.FormattedText.Spans[2].GestureRecognizers.OfType<TapGestureRecognizer>().Single().SendTapped(label);
        Assert.Equal(["details True"], log);
    }
}
