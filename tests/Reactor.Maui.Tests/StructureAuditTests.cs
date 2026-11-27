using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiShell = Microsoft.Maui.Controls.Shell;
using MauiSwipeView = Microsoft.Maui.Controls.SwipeView;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>A stateful probe: its count survives an update in place, and its cleanup is logged.</summary>
public sealed class StructureProbe : Component<(string Name, List<string> Log)>
{
    public static readonly ConcurrentDictionary<string, Action<int>> Set = [];

    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        Set[Props.Name] = setCount;
        UseEffect(() => () => Props.Log.Add($"cleanup {Props.Name}"), Array.Empty<object>());
        return Label($"{Props.Name} {count}");
    }
}

/// <summary>
/// Spec 070 §4.16.2 group S: the structure keys' set, update in place, replace and clear, on both
/// registries, for the facets the earlier tests didn't prove (each claim names its tests).
/// </summary>
public class StructureAuditTests
{
    public static TheoryData<string> Registries() => ["controls", "direct"];

    private static TestHost Host(string registry) =>
        new(PropertyMatrixTests.Registries.Single(r => r.Name == registry).Create());

    private static Element Probe(string name, List<string> log) => Component<StructureProbe, (string, List<string>)>((name, log));

    private static void SetState(TestHost host, string name, int value)
    {
        StructureProbe.Set[name](value);
        host.Scheduler.Drain();
    }

    internal static string TextOf(object? view) => view switch
    {
        null => "<none>",
        MauiLabel label => label.Text,
        DirectLabel direct => direct.Text,
        DirectViewHost wrapper => TextOf(wrapper.Children[0]),
        ContentView content => TextOf(content.Content),
        _ => view.GetType().Name,
    };

    // ── Pages ───────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Registries))]
    public void APagesContentAndTitleView_AreSetUpdatedInPlaceReplacedAndCleared(string registry)
    {
        var log = new List<string>();
        var (c, t) = ($"content-{registry}", $"title-{registry}");
        Element Tree(Element? content, Element? title) => Page("P", content) with { TitleView = title };
        var host = Host(registry).Render(Tree(Probe(c, log), Probe(t, log)));
        var page = host.View<ContentPage>();
        Assert.Equal(($"{c} 0", $"{t} 0"), (TextOf(page.Content), TextOf(MauiShell.GetTitleView(page))));
        SetState(host, c, 3);
        SetState(host, t, 4);
        var (content, title) = (page.Content, MauiShell.GetTitleView(page));

        host.Render(Tree(Probe(c, log), Probe(t, log)));
        Assert.Same(content, page.Content);
        Assert.Same(title, MauiShell.GetTitleView(page));
        Assert.Equal(($"{c} 3", $"{t} 4"), (TextOf(page.Content), TextOf(MauiShell.GetTitleView(page))));
        Assert.Empty(log);

        host.Render(Tree(Label("plain"), Probe(t, log)));
        Assert.Equal("plain", TextOf(page.Content));
        Assert.Equal([$"cleanup {c}"], log);

        host.Render(Tree(null, null));
        Assert.Null(page.Content);
        Assert.False(page.IsSet(MauiShell.TitleViewProperty));
        Assert.Equal([$"cleanup {c}", $"cleanup {t}"], log);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void TabsPages_AreAddedRemovedAndReordered_KeepingTheirPages(string registry)
    {
        Element Tree(params string[] keys) => Tabs(0, null, [.. keys.Select(k => Page(k, null).WithKey(k))]);
        var host = Host(registry).Render(Tree("a", "b", "c"));
        var tabs = host.View<TabbedPage>();
        var (a, c) = (tabs.Children[0], tabs.Children[2]);

        host.Render(Tree("c", "a", "d"));

        Assert.Equal(["c", "a", "d"], tabs.Children.Select(p => p.Title));
        Assert.Same(c, tabs.Children[0]);
        Assert.Same(a, tabs.Children[1]);
    }

    // ── Shell ───────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Registries))]
    public void TheShellsItems_AndTheirTabs_AreAddedRemovedAndReordered_KeepingTheirObjects(string registry)
    {
        static Element Item(string key, params string[] tabs) =>
            FlyoutItem(key, [.. tabs.Select(t => ShellTab(t, t, Page(t, null)))]).WithKey(key);
        static Element Bar(params string[] tabs) => TabBar([.. tabs.Select(t => ShellTab(t, t, Page(t, null)))]).WithKey("bar");
        var host = Host(registry).Render(Shell("a1", null, Item("A", "a1", "a2"), Bar("t1", "t2"), Item("B", "b1")));
        var shell = host.View<ReactorShell>();
        var (itemA, bar, itemB) = (shell.Items[0], shell.Items[1], shell.Items[2]);
        var (a1, a2, t1, t2) = (itemA.Items[0], itemA.Items[1], bar.Items[0], bar.Items[1]);

        // Tabs reordered inside a flyout item and a tab bar.
        host.Render(Shell("a1", null, Item("A", "a2", "a1"), Bar("t2", "t1"), Item("B", "b1")));
        Assert.Equal([a2, a1], itemA.Items);
        Assert.Equal([t2, t1], bar.Items);

        // Root items reordered, one removed, one added.
        host.Render(Shell("a1", null, Item("B", "b1"), Item("A", "a2", "a1"), Item("C", "c1")));
        Assert.Equal(["B", "A", "C"], shell.Items.Select(i => i.Title));
        Assert.Same(itemB, shell.Items[0]);
        Assert.Same(itemA, shell.Items[1]);
        Assert.DoesNotContain(bar, shell.Items);
        Assert.Same(itemA, shell.CurrentItem);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void TheShellsFlyoutHeaderFooterAndContent_AreSetUpdatedInPlaceAndCleared_InRetainedHosts(string registry)
    {
        var log = new List<string>();
        var (h, f, c) = ($"header-{registry}", $"footer-{registry}", $"content-{registry}");
        Element Tree(bool header, bool footer, bool content) => Shell(null, null, FlyoutItem("A", ShellTab("a", "A", Page("A", null)))) with
        {
            FlyoutHeader = header ? Probe(h, log) : null,
            FlyoutFooter = footer ? Probe(f, log) : null,
            FlyoutContent = content ? Probe(c, log) : null,
        };
        var host = Host(registry).Render(Tree(true, true, true));
        var shell = host.View<ReactorShell>();
        var (headerHost, footerHost) = (Assert.IsType<ContentView>(shell.FlyoutHeader), Assert.IsType<ContentView>(shell.FlyoutFooter));
        SetState(host, h, 1);
        SetState(host, f, 2);
        SetState(host, c, 3);

        host.Render(Tree(true, true, true));
        Assert.Equal(($"{h} 1", $"{f} 2", $"{c} 3"), (TextOf(shell.FlyoutHeader), TextOf(shell.FlyoutFooter), TextOf(shell.FlyoutContent)));
        Assert.Empty(log);

        // Cleared: MAUI binds the header and footer once, so their hosts stay, emptied; the trees clean up.
        host.Render(Tree(false, false, false));
        Assert.Same(headerHost, shell.FlyoutHeader);
        Assert.Same(footerHost, shell.FlyoutFooter);
        Assert.Null(headerHost.Content);
        Assert.Null(footerHost.Content);
        Assert.False(shell.IsSet(MauiShell.FlyoutContentProperty));
        Assert.Equal([$"cleanup {c}", $"cleanup {f}", $"cleanup {h}"], log.Order());

        // Set again: into the same host, a fresh tree.
        host.Render(Tree(true, false, false));
        Assert.Same(headerHost, shell.FlyoutHeader);
        Assert.Equal($"{h} 0", TextOf(headerHost));
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void AShellTabsContent_AndATopTabsPage_AreReplaced_AndTopTabsReordered_KeepingTheirObjects(string registry)
    {
        var host = Host(registry).Render(Shell("a", null, TabBar(ShellTab("a", "A", Page("one", null).WithKey("one")))));
        var shell = host.View<ReactorShell>();
        var section = shell.Items[0].Items[0];
        var owned = section.CurrentItem;
        Assert.Equal("one", ((Page)owned.Content).Title);

        host.Render(Shell("a", null, TabBar(ShellTab("a", "A", Page("two", null).WithKey("two")))));
        Assert.Same(owned, section.CurrentItem);
        Assert.Equal("two", ((Page)owned.Content).Title);

        static Element TopTabs(string xPage, params string[] order) => Shell("a", null, TabBar(ShellTab("a", "A",
            [.. order.Select(k => ShellContent(k, k, Page(k == "x" ? xPage : k, null).WithKey(k == "x" ? xPage : k)))])));
        host.Render(TopTabs("x", "x", "y"));
        var (x, y) = (section.Items[0], section.Items[1]);
        host.Render(TopTabs("x2", "y", "x"));
        Assert.Equal([y, x], section.Items);
        Assert.Equal("x2", ((Page)x.Content).Title);
    }

    // ── Collections ─────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Registries))]
    public void ACollectionsFooterAndEmptyView_KeepTheirStateOnUpdate_AndCleanUpWhenRemoved(string registry)
    {
        var log = new List<string>();
        var (f, e) = ($"footer-{registry}", $"empty-{registry}");
        Element Tree(bool slots) => CollectionView(Array.Empty<string>(), k => k, k => Label(k)) with
        {
            Footer = slots ? Probe(f, log) : null,
            EmptyView = slots ? Probe(e, log) : null,
        };
        var host = Host(registry).Render(Tree(true));
        var view = host.View<CollectionView>();
        var (footer, empty) = (view.Footer, view.EmptyView);
        SetState(host, f, 2);
        SetState(host, e, 3);

        host.Render(Tree(true));
        Assert.Same(footer, view.Footer);
        Assert.Same(empty, view.EmptyView);
        Assert.Equal(($"{f} 2", $"{e} 3"), (TextOf(view.Footer), TextOf(view.EmptyView)));
        Assert.Empty(log);

        host.Render(Tree(false));
        Assert.Null(view.Footer);
        Assert.Null(view.EmptyView);
        Assert.Equal([$"cleanup {e}", $"cleanup {f}"], log.Order());
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void ACollectionsItems_AreKeyed_TheirTemplateRerendersBoundCells_AndARecycledCellRemountsUnlessTreesAreReused(string registry)
    {
        var log = new List<string>();
        string Name(string k) => $"{registry}-item-{k}";
        Element Tree(string[] keys, bool reuse, string prefix = "") =>
            CollectionView(keys, k => k, k => prefix == "" ? Probe(Name(k), log) : Label(prefix + k)) with { ReuseItemTrees = reuse };
        var host = Host(registry).Render(Tree(["a", "b", "c"], reuse: false));
        var view = host.View<CollectionView>();
        var source = (System.Collections.IList)view.ItemsSource;
        var (b, c) = (Cells.Box(view, "b"), Cells.Box(view, "c"));
        var cell = Cells.Bind(view.ItemTemplate, b);
        SetState(host, Name("b"), 5);

        // Reordered with one added and one removed: the kept item keeps its box, and its cell its tree.
        host.Render(Tree(["c", "b", "d"], reuse: false));
        Assert.Equal(["c", "b", "d"], source.Cast<ItemBox>().Select(x => x.Key));
        Assert.Same(c, source[0]); // moved
        Assert.Same(b, source[1]);
        Assert.Equal($"{Name("b")} 5", Cells.Text(cell));

        // Recycled for another item: a fresh tree, unless trees are reused.
        cell.BindingContext = Cells.Box(view, "c");
        Assert.Equal($"{Name("c")} 0", Cells.Text(cell));
        host.Render(Tree(["c", "b", "d"], reuse: true));
        SetState(host, Name("c"), 7);
        cell.BindingContext = Cells.Box(view, "d");
        Assert.Equal($"{Name("d")} 7", Cells.Text(cell)); // the same tree (its state), rendering d

        // A changed template re-renders the bound cells.
        host.Render(Tree(["c", "b", "d"], reuse: false, prefix: "row "));
        Assert.Equal("row d", Cells.Text(cell));
    }

    private sealed record Group(string Name, string[] Items);

    [Theory]
    [MemberData(nameof(Registries))]
    public void ACollectionsGroups_AreKeyed_AndTheirTemplatesAreReplacedInPlace_AndRemoved(string registry)
    {
        Element Tree(Group[] groups, string? header, string? footer) =>
            CollectionView(groups, g => g.Name, g => g.Items, i => i, i => Label(i),
                header is null ? null : g => Label($"{header} {g.Name} {g.Items.Length}"),
                footer is null ? null : g => Label($"{footer} {g.Name}"));
        var host = Host(registry).Render(Tree([new("x", ["a"]), new("y", ["b"])], "h1", "f1"));
        var view = host.View<CollectionView>();
        var groups = (ObservableCollection<GroupBox>)view.ItemsSource;
        var y = groups[1];
        var (headerTemplate, footerTemplate) = (view.GroupHeaderTemplate, view.GroupFooterTemplate);
        var header = Cells.Bind(view.GroupHeaderTemplate, y);
        var footer = Cells.Bind(view.GroupFooterTemplate, y);
        Assert.Equal(("h1 y 1", "f1 y"), (Cells.Text(header), Cells.Text(footer)));

        // Groups reordered and a group's value changed: the group keeps its box; templates replaced by
        // others re-render the bound cells, through the same native templates.
        host.Render(Tree([new("y", ["b", "c"]), new("x", ["a"])], "h2", "f2"));
        Assert.Same(y, groups[0]);
        Assert.Same(headerTemplate, view.GroupHeaderTemplate);
        Assert.Same(footerTemplate, view.GroupFooterTemplate);
        Assert.Equal(("h2 y 2", "f2 y"), (Cells.Text(header), Cells.Text(footer)));

        host.Render(Tree([new("y", ["b", "c"]), new("x", ["a"])], null, null));
        Assert.Null(view.GroupHeaderTemplate);
        Assert.Null(view.GroupFooterTemplate);
    }

    // ── Swipe ───────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Registries))]
    public void ASwipeViewsContentAndSides_AreSetUpdatedAndCleared_AndItsItemsReachTheCurrentCallback(string registry)
    {
        var log = new List<string>();
        var calls = new List<string>();
        var c = $"swipe-{registry}";
        Element Tree(bool content, SwipeItemData[] right) => SwipeView(content ? Probe(c, log) : null) with { RightItems = right };
        SwipeItemData Item(string generation, Color? color = null, ImageSource? icon = null) =>
            new("x", () => calls.Add(generation)) { BackgroundColor = color, Icon = icon };

        var host = Host(registry).Render(Tree(true, [Item("first", Colors.Red, ImageSource.FromFile("a.png"))]));
        var view = host.View<MauiSwipeView>();
        var content = view.Content;
        SetState(host, c, 4);
        var item = (SwipeItem)view.RightItems[0];
        Assert.Equal(Colors.Red, item.BackgroundColor);
        Assert.NotNull(item.IconImageSource);

        // The same structure: the item stays, and invokes the current callback.
        host.Render(Tree(true, [Item("second", Colors.Red, ImageSource.FromFile("a.png"))]));
        Assert.Same(content, view.Content);
        Assert.Equal($"{c} 4", TextOf(view.Content));
        Assert.Same(item, view.RightItems[0]);
        ((Microsoft.Maui.ISwipeItem)item).OnInvoked();
        Assert.Equal(["second"], calls);

        // The color and icon removed: a rebuilt item at MAUI's defaults.
        host.Render(Tree(true, [Item("third")]));
        var rebuilt = (SwipeItem)view.RightItems[0];
        Assert.NotSame(item, rebuilt);
        Assert.False(rebuilt.IsSet(VisualElement.BackgroundColorProperty));
        Assert.Null(rebuilt.IconImageSource);

        // The side's items and the content cleared.
        host.Render(Tree(false, []));
        Assert.Empty(view.RightItems);
        Assert.Null(view.Content);
        Assert.Equal([$"cleanup {c}"], log);

        host.Root.Unmount();
        ((Microsoft.Maui.ISwipeItem)rebuilt).OnInvoked();
        Assert.Equal(["second"], calls);
    }
}
