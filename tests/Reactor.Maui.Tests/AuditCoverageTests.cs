using Microsoft.Maui.Graphics;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiGraphicsView = Microsoft.Maui.Controls.GraphicsView;
using MauiGrid = Microsoft.Maui.Controls.Grid;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiRadioButton = Microsoft.Maui.Controls.RadioButton;
using MauiScrollView = Microsoft.Maui.Controls.ScrollView;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>
/// The census's covered-by claims of 4c-3b (spec 070 §4.14.2): structural values the property matrix
/// can't express, each test written to fail when its property's handling is removed.
/// </summary>
public class AuditCoverageTests
{
    public static TheoryData<string> Registries() => ["controls", "direct"];

    private static TestHost Host(string registry) =>
        new(PropertyMatrixTests.Registries.Single(r => r.Name == registry).Create());

    private static string TextOf(IView view) => view switch
    {
        MauiLabel label => label.Text,
        DirectLabel label => label.Text,
        _ => view.GetType().Name,
    };

    public static TheoryData<string, string> Layouts()
    {
        var data = new TheoryData<string, string>();
        foreach (var registry in new[] { "controls", "direct" })
            foreach (var layout in new[] { "VStack", "HStack", "Grid", "FlexLayout", "AbsoluteLayout" })
                data.Add(registry, layout);
        return data;
    }

    private static Element LayoutOf(string layout, params string[] keys)
    {
        Element?[] children = [.. keys.Select(k => Label(k).WithKey(k))];
        return layout switch
        {
            "VStack" => VStack(children),
            "HStack" => HStack(children),
            "Grid" => Grid("*", "*", children),
            "FlexLayout" => FlexLayout(children),
            _ => AbsoluteLayout(children),
        };
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void LayoutChildren_AreAddedReorderedRemovedAndCleared_KeepingKeyedViews(string registry, string layout)
    {
        var host = Host(registry).Render(LayoutOf(layout, "a", "b", "c"));
        var view = (IList<IView>)host.RootView!;
        Assert.Equal(["a", "b", "c"], view.Select(TextOf));
        var (a, c) = (view[0], view[2]);

        host.Render(LayoutOf(layout, "c", "a", "d"));
        Assert.Same(view, host.RootView);
        Assert.Equal(["c", "a", "d"], view.Select(TextOf));
        Assert.Same(c, view[0]);
        Assert.Same(a, view[1]);

        host.Render(LayoutOf(layout));
        Assert.Empty(view);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void GridDefinitions_AreSetUpdatedKeptWhenEqual_AndCleared(string registry)
    {
        static string Rows(object grid) => string.Join(",", ((IGridLayout)grid).RowDefinitions.Select(r => r.Height.ToString()));
        static string Columns(object grid) => string.Join(",", ((IGridLayout)grid).ColumnDefinitions.Select(c => c.Width.ToString()));

        var host = Host(registry).Render(Grid("auto,*", "100,2*"));
        var grid = host.RootView!;
        Assert.Equal(("1.Auto,1.Star", "100.Absolute,2.Star"), (Rows(grid), Columns(grid)));

        host.Render(Grid("*", "auto"));
        Assert.Same(grid, host.RootView);
        Assert.Equal(("1.Star", "1.Auto"), (Rows(grid), Columns(grid)));

        // An equal declaration keeps the definitions (the same instances).
        var (row, column) = (((IGridLayout)grid).RowDefinitions[0], ((IGridLayout)grid).ColumnDefinitions[0]);
        host.Render(Grid(string.Concat("", "*"), string.Concat("au", "to")));
        Assert.Same(row, ((IGridLayout)grid).RowDefinitions[0]);
        Assert.Same(column, ((IGridLayout)grid).ColumnDefinitions[0]);

        host.Render(Grid("", ""));
        Assert.Equal(("", ""), (Rows(grid), Columns(grid)));
    }

    private sealed class CountingDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect) { }
    }

    [Fact]
    public void GraphicsView_ANewDrawableIsAssigned_TheSameOneIsInvalidatedOnEveryUpdate()
    {
        // Controls on both registries; the invalidation is a handler command.
        var (first, second) = (new CountingDrawable(), new CountingDrawable());
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(GraphicsView(first));
        var view = host.View<MauiGraphicsView>();
        Assert.Same(first, view.Drawable);
        var handler = new StubViewHandler();
        view.Handler = handler;
        int Invalidations() => handler.Commands.Count(c => c.Command == nameof(IGraphicsView.Invalidate));

        host.Render(GraphicsView(first));
        host.Render(GraphicsView(first));
        Assert.Equal(2, Invalidations());

        handler.Commands.Clear();
        host.Render(GraphicsView(second));
        Assert.Same(second, view.Drawable);
        Assert.Equal(0, Invalidations()); // MAUI redraws an assigned drawable itself

        var direct = new TestHost(DirectHandlers.CreateRegistry()).Render(GraphicsView(first));
        Assert.IsType<MauiGraphicsView>(direct.RootView);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void ScrollView_Content_IsSetReplacedAndCleared(string registry)
    {
        var host = Host(registry).Render(ScrollView(Label("a")));
        var view = host.View<MauiScrollView>();
        var first = view.Content;
        Assert.NotNull(first);

        host.Render(ScrollView(Label("a2")));
        Assert.Same(first, view.Content);

        host.Render(ScrollView(Button("b")));
        Assert.NotSame(first, view.Content);

        host.Render(ScrollView(null));
        Assert.Null(view.Content);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void Flyout_Pages_AreSetUpdatedInPlaceReplacedAndSwapped(string registry)
    {
        var host = Host(registry).Render(Flyout(Page("Menu", Label("m")), Page("Detail", Label("d")), false));
        var flyout = host.View<FlyoutPage>();
        var (menu, detail) = (flyout.Flyout, flyout.Detail);
        Assert.Equal(("Menu", "Detail"), (menu.Title, detail.Title));

        host.Render(Flyout(Page("Menu 2", Label("m")), Page("Detail 2", Label("d")), false));
        Assert.Same(menu, flyout.Flyout);
        Assert.Same(detail, flyout.Detail);
        Assert.Equal(("Menu 2", "Detail 2"), (menu.Title, detail.Title));

        host.Render(Flyout(Page("Menu 2", Label("m")), NavigationStack([Page("Stack", Label("s"))]), false));
        Assert.Same(menu, flyout.Flyout);
        Assert.IsAssignableFrom<NavigationPage>(flyout.Detail);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void RadioGroupName_JoinsNamedGroups_ChangesGroup_AndClearsToTheUnnamedScope_KeepingItsNativeName(string registry)
    {
        var log = new List<string>();
        var r = Enumerable.Range(0, 3).Select(_ => TypedElementRef.Create<MauiRadioButton>()).ToArray();
        Element Tree(string? groupOfB) => VStack(
            RadioButton("a", true, v => log.Add($"a {v}"), "one").Ref(r[0]),
            RadioButton("b", false, v => log.Add($"b {v}"), groupOfB).Ref(r[1]),
            RadioButton("c", true, v => log.Add($"c {v}"), "two").Ref(r[2]));
        var host = Host(registry).Render(Tree("one"));
        var nativeName = r[1].Current!.GroupName;

        // In group one: checking b unchecks a, not c.
        ((IRadioButton)r[1].Current!).IsChecked = true;
        host.Scheduler.Drain();
        Assert.Equal(["a False", "b True"], log);

        // Moved to group two (the declarations as before): checking b unchecks c, not a.
        log.Clear();
        host.Render(Tree("two"));
        host.Scheduler.Drain();
        Assert.Equal(nativeName, r[1].Current!.GroupName); // Reactor's own native name, for the button's life
        ((IRadioButton)r[1].Current!).IsChecked = true;
        host.Scheduler.Drain();
        Assert.Equal(["c False", "b True"], log); // the peers report first (spec 070 §3.6)

        // Unnamed: in the stack's unnamed scope, alone there, so checking it reports only itself.
        log.Clear();
        host.Render(Tree(null));
        host.Scheduler.Drain();
        ((IRadioButton)r[1].Current!).IsChecked = true;
        host.Scheduler.Drain();
        Assert.Equal(["b True"], log);
        Assert.Equal(nativeName, r[1].Current!.GroupName);
    }
}

/// <summary>
/// A radio button's indicator (spec 070 §4.14.2). View content without a description warns, so this
/// shares the collection of the tests that listen for that warning.
/// </summary>
[Collection("RadioDescriptionWarnings")]
public class RadioIndicatorTests
{
    public static TheoryData<string> Registries() => ["controls", "direct"];

    private static TestHost Host(string registry) =>
        new(PropertyMatrixTests.Registries.Single(r => r.Name == registry).Create());

    [Theory]
    [MemberData(nameof(Registries))]
    public void RadioIndicator_IsShownAndHiddenByARender_ForViewContent_AndTextKeepsTheNativeTemplate(string registry)
    {
        var host = Host(registry).Render(RadioButton(Label("card"), false));
        Assert.Same(MauiRadioButton.DefaultTemplate, host.View<MauiRadioButton>().ControlTemplate);

        host.Render(RadioButton(Label("card"), false) with { IsIndicatorVisible = false });
        Assert.Same(RadioButtonHandler.ContentOnly, host.View<MauiRadioButton>().ControlTemplate);

        host.Render(RadioButton(Label("card"), false));
        Assert.Same(MauiRadioButton.DefaultTemplate, host.View<MauiRadioButton>().ControlTemplate);

        // Text content always shows MAUI's native indicator (no template).
        host.Render(RadioButton("text", false) with { IsIndicatorVisible = false });
        Assert.Null(host.View<MauiRadioButton>().ControlTemplate);
    }
}
