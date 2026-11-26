using static Microsoft.UI.Reactor.Maui.Factories;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>A page's toolbar items (spec 070 §4.14.4): order, rebuilds, the current callback, and identity.</summary>
public class ToolbarTests
{
    public static TheoryData<string> Registries() => ["controls", "direct"];

    private static TestHost Host(string registry) =>
        new(PropertyMatrixTests.Registries.Single(r => r.Name == registry).Create());

    private static PageElement Screen(List<string> log, params ToolbarAction[] items) =>
        Page("Toolbar", Label("body")) with { ToolbarItems = items };

    private static ToolbarAction Item(List<string> log, string text, string? tag = null) => new(text, () => log.Add(tag ?? text));

    private static void Activate(ToolbarItem item) => ((IMenuItemController)item).Activate();

    [Theory]
    [MemberData(nameof(Registries))]
    public void ARetiredItem_InvokesNothing_WhileTheItemNowAtItsIndexInvokesItsOwn(string registry)
    {
        var log = new List<string>();
        var host = Host(registry).Render(Screen(log, Item(log, "A"), Item(log, "B")));
        var page = host.View<ContentPage>();
        var (oldA, oldB) = (page.ToolbarItems[0], page.ToolbarItems[1]);

        host.Render(Screen(log, Item(log, "C"), Item(log, "D")));
        Assert.NotSame(oldA, page.ToolbarItems[0]);
        Activate(oldA);
        Activate(oldB);
        Assert.Empty(log);
        Activate(page.ToolbarItems[0]);
        Assert.Equal(["C"], log); // positive control: the index still invokes

        log.Clear();
        host.Render(Screen(log, Item(log, "E")));
        Activate(oldA);
        Activate(oldB);
        Assert.Empty(log);
        Activate(page.ToolbarItems[0]);
        Assert.Equal(["E"], log);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void ACallbackOnlyRender_KeepsTheItems_WhichInvokeTheCurrentCallback_AndNothingAfterUnmount(string registry)
    {
        var log = new List<string>();
        var host = Host(registry).Render(Screen(log, Item(log, "A", "first"), Item(log, "B", "first B")));
        var page = host.View<ContentPage>();
        var items = page.ToolbarItems.ToList();

        host.Render(Screen(log, Item(log, "A", "second"), Item(log, "B", "second B")));
        Assert.Equal(items, page.ToolbarItems);
        Activate(page.ToolbarItems[0]);
        Assert.Equal(["second"], log);

        log.Clear();
        host.Root.Unmount();
        Activate(items[0]);
        Assert.Empty(log);
    }

    public static TheoryData<string, string> StructureChanges()
    {
        var data = new TheoryData<string, string>();
        foreach (var registry in new[] { "controls", "direct" })
            foreach (var change in new[] { "text", "icon", "order", "priority", "reorder" })
                data.Add(registry, change);
        return data;
    }

    [Theory]
    [MemberData(nameof(StructureChanges))]
    public void AStructureChange_Rebuilds_InDeclaredOrder_AndTheOldItemsInvokeNothing(string registry, string change)
    {
        var log = new List<string>();
        ToolbarAction[] Before() => [Item(log, "A") with { Priority = 1 }, Item(log, "B") with { Priority = 2 }];
        ToolbarAction[] After() => change switch
        {
            "text" => [Item(log, "A2", "A") with { Priority = 1 }, Item(log, "B") with { Priority = 2 }],
            "icon" => [Item(log, "A") with { Priority = 1, Icon = MatrixCases.Glyph("x") }, Item(log, "B") with { Priority = 2 }],
            "order" => [Item(log, "A") with { Priority = 1, Order = ToolbarItemOrder.Secondary }, Item(log, "B") with { Priority = 2 }],
            "priority" => [Item(log, "A") with { Priority = 3 }, Item(log, "B") with { Priority = 2 }],
            _ => [Item(log, "B") with { Priority = 2 }, Item(log, "A") with { Priority = 1 }],
        };
        var host = Host(registry).Render(Screen(log, Before()));
        var page = host.View<ContentPage>();
        var old = page.ToolbarItems.ToList();

        host.Render(Screen(log, After()));
        var expected = After();
        Assert.Equal(expected.Select(a => a.Text), page.ToolbarItems.Select(i => i.Text));
        Assert.Equal(expected.Select(a => a.Order), page.ToolbarItems.Select(i => i.Order));
        Assert.Equal(expected.Select(a => a.Priority), page.ToolbarItems.Select(i => i.Priority));
        Assert.All(page.ToolbarItems, item => Assert.DoesNotContain(item, old));

        foreach (var item in old) Activate(item);
        Assert.Empty(log);
        foreach (var item in page.ToolbarItems) Activate(item);
        Assert.Equal(expected.Select(a => change == "text" && a.Text == "A2" ? "A" : a.Text), log);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void ASecondaryItem_InvokesItsCurrentCallback_AndNothingOnceRetired(string registry)
    {
        var log = new List<string>();
        var host = Host(registry).Render(Screen(log, Item(log, "Main"), Item(log, "More", "first") with { Order = ToolbarItemOrder.Secondary }));
        var page = host.View<ContentPage>();
        var secondary = page.ToolbarItems[1];
        Assert.Equal(ToolbarItemOrder.Secondary, secondary.Order);

        host.Render(Screen(log, Item(log, "Main"), Item(log, "More", "second") with { Order = ToolbarItemOrder.Secondary }));
        Assert.Same(secondary, page.ToolbarItems[1]);
        Activate(secondary);
        Assert.Equal(["second"], log);

        log.Clear();
        host.Render(Screen(log, Item(log, "Main"), Item(log, "Other") with { Order = ToolbarItemOrder.Secondary }));
        Activate(secondary);
        Assert.Empty(log);
        Activate(page.ToolbarItems[1]);
        Assert.Equal(["Other"], log);
    }
}
