using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Reactor.Maui;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using static Reactor.Maui.DeviceTests.FixtureContext;
using MauiCollectionView = Microsoft.Maui.Controls.CollectionView;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiPicker = Microsoft.Maui.Controls.Picker;
using MauiRadioButton = Microsoft.Maui.Controls.RadioButton;
using MauiSwipeView = Microsoft.Maui.Controls.SwipeView;

namespace Reactor.Maui.DeviceTests;

/// <summary>Spec 070 §3 (phase 4c-2): collections, swipe items, radio buttons, picker display and label spans.</summary>
public static partial class Fixtures
{
    private static IEnumerable<Fixture> ItemsFixtures() =>
    [
        .. Renderers.SelectMany(r => new Fixture[]
        {
            new($"CollectionView_Layouts_Geometry [{r.Name}]", c => ListLayouts(c, r.Create())),
            new($"CollectionView_SizingStrategy [{r.Name}]", c => ListSizing(c, r.Create())),
            new($"CollectionView_HeaderFooterEmpty [{r.Name}]", c => ListSlots(c, r.Create())),
            new($"CollectionView_Threshold [{r.Name}]", c => ListThreshold(c, r.Create())),
            new($"CollectionView_Grouping [{r.Name}]", c => ListGrouping(c, r.Create())),
            new($"CollectionView_OverlappingSelection_Programmatic [{r.Name}]", c => OverlappingProgrammatic(c, r.Create())),
            new($"CollectionView_OverlappingSelection_User [{r.Name}]", c => OverlappingUser(c, r.Create())),
            new($"CollectionView_EnterNone [{r.Name}]", c => EnterNone(c, r.Create())),
            new($"CollectionView_SelectionInItems [{r.Name}]", c => SelectionInItems(c, r.Create())),
            new($"CollectionView_SelectionInItems_Recycling [{r.Name}]", c => SelectionRecycling(c, r.Create())),
            new($"SwipeView_OpenCloseThroughARef [{r.Name}]", c => SwipeOpenClose(c, r.Create())),
            new($"SwipeView_Threshold [{r.Name}]", c => SwipeThreshold(c, r.Create())),
            new($"SwipeView_ContentItems [{r.Name}]", c => SwipeContentItems(c, r.Create())),
            new($"RadioButton_Content [{r.Name}]", c => RadioContent(c, r.Create())),
            new($"RadioButton_Groups [{r.Name}]", c => RadioGroupsOnDevice(c, r.Create())),
            new($"RadioButton_Accessibility [{r.Name}]", c => RadioAccessibility(c, r.Create())),
            new($"Picker_ItemDisplay [{r.Name}]", c => PickerDisplay(c, r.Create())),
            new($"Label_Spans_Platform [{r.Name}]", c => LabelSpans(c, r.Create())),
        }),
        new("CollectionView_Selection_Characterization [controls]", SelectionThroughReactor),
        new("RadioButton_Cascade_Characterization [controls]", RadioCharacterization),
    ];

    private static readonly string[] Letters = ["a", "b", "c", "d", "e", "f"];

    // ── Layouts, sizing, slots, threshold, grouping ─────────────────────────

    private static async Task ListLayouts(FixtureContext c, ElementHandlerRegistry registry)
    {
        foreach (var (arrangement, span) in new[] { (ItemsArrangement.VerticalList, 1), (ItemsArrangement.HorizontalList, 1), (ItemsArrangement.VerticalGrid, 2) })
        {
            c.Show((CollectionView(Letters, k => k, k => Label(k).AutomationId($"cell-{k}").Size(80, 40)) with
            {
                Arrangement = arrangement,
                Span = span,
                ItemSpacing = 10,
            }).Height(400), registry);
            var a = await Loaded<IView>(c, "cell-a");
            var b = await Loaded<IView>(c, "cell-b");
            var cc = await Loaded<IView>(c, "cell-c");
            await c.WaitFor(() => Platform.Size(cc).Height > 0, "the cells to be laid out");
            await c.Settle(200);
            var (fa, fb, fc) = (Platform.WindowFrame(a), Platform.WindowFrame(b), Platform.WindowFrame(cc));
            Note($"{arrangement}: a {fa}, b {fb}, c {fc}");
            switch (arrangement)
            {
                case ItemsArrangement.VerticalList:
                    Close(fa.X, fb.X, "a vertical list stacks the cells");
                    Close(10, fb.Y - fa.Bottom, "the item spacing between rows");
                    break;
                case ItemsArrangement.HorizontalList:
                    Close(fa.Y, fb.Y, "a horizontal list keeps the cells on one row");
                    Close(10, fb.X - fa.Right, "the item spacing between columns");
                    break;
                default:
                    Close(fa.Y, fb.Y, "cells 0 and 1 share a grid row");
                    Check(fb.X > fa.Right, "cell 1 is right of cell 0");
                    Check(fc.Y >= fa.Bottom + 9, $"cell 2 starts the next row, after the spacing: {fc.Y:0.#} vs {fa.Bottom:0.#}");
                    break;
            }
            c.Unmount();
        }
    }

    private static async Task ListSizing(FixtureContext c, ElementHandlerRegistry registry)
    {
        var tops = new Dictionary<ItemSizingStrategy, (double, double)>();
        foreach (var strategy in new[] { ItemSizingStrategy.MeasureAllItems, ItemSizingStrategy.MeasureFirstItem })
        {
            var heights = new Dictionary<string, double> { ["a"] = 40, ["b"] = 80, ["c"] = 40 };
            c.Show((CollectionView(new[] { "a", "b", "c" }, k => k, k => Label(k).AutomationId($"cell-{k}").Height(heights[k])) with
            {
                ItemSizingStrategy = strategy,
            }).Height(400), registry);
            var labels = new List<IView>();
            foreach (var k in new[] { "a", "b", "c" }) labels.Add(await Loaded<IView>(c, $"cell-{k}"));
            await c.WaitFor(() => Platform.Size(labels[2]).Height > 0, "the cells to be laid out");
            await c.Settle(300);
            // The cells' frames, not their content's: content taller than its cell is centered in it.
            var cells = new[] { "a", "b", "c" }.Select(k => c.Find<ContentView>(v => v.GetType().Name == "ReactorCell" && v.BindingContext is ItemBox box && box.Key == k)).ToArray();
            var ys = cells.Select(v => Platform.WindowFrame(v).Y).ToArray();
            tops[strategy] = (ys[1] - ys[0], ys[2] - ys[1]);
            Note($"{strategy}: steps {ys[1] - ys[0]:0.#}, {ys[2] - ys[1]:0.#}");
            c.Unmount();
        }
        var all = tops[ItemSizingStrategy.MeasureAllItems];
        var first = tops[ItemSizingStrategy.MeasureFirstItem];
        Check(Math.Abs(all.Item2 - all.Item1) > 20, "under MeasureAllItems the taller item's cell is taller (positive control)");
        Close(first.Item1, first.Item2, "under MeasureFirstItem every cell takes the first item's size", 1.5);
    }

    private static async Task ListSlots(FixtureContext c, ElementHandlerRegistry registry)
    {
        CollectionViewElement List(string[] items) => CollectionView(items, k => k, k => Label(k).AutomationId($"cell-{k}").Height(40)) with
        {
            Header = Label("header").AutomationId("header").Height(30),
            Footer = Label("footer").AutomationId("footer").Height(30),
            EmptyView = Label("nothing").AutomationId("empty"),
        };
        c.Show(List(["a", "b"]).Height(400), registry);
        var header = await Loaded<IView>(c, "header");
        var a = await Loaded<IView>(c, "cell-a");
        var b = await Loaded<IView>(c, "cell-b");
        var footer = await Loaded<IView>(c, "footer");
        await c.WaitFor(() => Platform.Size(footer).Height > 0, "the slots to be laid out");
        await c.Settle(200);
        Check(Platform.WindowFrame(header).Bottom <= Platform.WindowFrame(a).Y + 0.5, "the header is above the first cell");
        Check(Platform.WindowFrame(footer).Y >= Platform.WindowFrame(b).Bottom - 0.5, "the footer is below the last cell");
        Check(c.TryFind<IView>(v => v.AutomationId == "empty") is not { } empty || !ItemsPlatform.IsOnScreen(empty), "no empty view while there are items");

        c.Render(List([]).Height(400));
        var shown = await Loaded<IView>(c, "empty");
        await c.WaitFor(() => Platform.Size(shown).Height > 0, "the empty view to be shown");
        await c.Settle(200);
        Check(c.FindAll<IView>(v => v.AutomationId == "cell-a").All(cell => !ItemsPlatform.IsOnScreen(cell)), "no cell on screen while the list is empty");

        c.Render(List(["a"]).Height(400));
        var back = await Loaded<IView>(c, "cell-a");
        await c.WaitFor(() => Platform.Size(back).Height > 0, "the cell to come back");
        await c.Settle(300);
        Check(c.FindAll<IView>(v => v.AutomationId == "empty").All(gone => !ItemsPlatform.IsOnScreen(gone)), "the empty view is gone once there are items");
    }

    private static async Task ListThreshold(FixtureContext c, ElementHandlerRegistry registry)
    {
        var items = Enumerable.Range(0, 50).Select(i => $"i{i}").ToArray();
        foreach (var threshold in new[] { 5, -1 })
        {
            var reached = 0;
            var list = TypedElementRef.Create<MauiCollectionView>();
            c.Show((CollectionView(items, k => k, k => Label(k).AutomationId(k).Height(44)) with
            {
                RemainingItemsThreshold = threshold,
                OnRemainingItemsThresholdReached = () => reached++,
            }).Height(300).Ref(list), registry);
            await Loaded<IView>(c, "i0");
            await c.Settle(200);
            var before = reached;
            // Animated: Android's jump scroll doesn't run MAUI's scroll listener, which checks the threshold.
            list.Current!.ScrollTo(49, position: ScrollToPosition.End, animate: true);
            await c.WaitFor(() => c.TryFind<IView>(v => v.AutomationId == "i49") is { Handler.PlatformView: not null }, "the end to be reached", 10_000);
            await c.Settle();
            Note($"threshold {threshold}: {before} before the scroll, {reached} after");
            if (threshold > 0) Check(reached - before >= 1, "reaching the end reports the threshold");
            else Equal(0, reached, "a disabled threshold reports nothing");
            c.Unmount();
        }
    }

    private sealed record Section(string Name, string[] Items);

    private static async Task ListGrouping(FixtureContext c, ElementHandlerRegistry registry)
    {
        CollectionViewElement List(Section[] sections) =>
            CollectionView(sections, s => s.Name, s => s.Items, i => i, i => Label(i).AutomationId($"cell-{i}").Height(40),
                s => Label($"header {s.Name}").AutomationId($"h-{s.Name}").Height(30),
                s => Label($"footer {s.Name}").AutomationId($"f-{s.Name}").Height(20));
        c.Show(List([new("x", ["a", "b"]), new("y", ["c"])]).Height(500), registry);
        string[] order = ["h-x", "cell-a", "cell-b", "f-x", "h-y", "cell-c", "f-y"];
        var views = new List<IView>();
        foreach (var id in order) views.Add(await Loaded<IView>(c, id));
        await c.WaitFor(() => Platform.Size(views[^1]).Height > 0, "the groups to be laid out");
        await c.Settle(200);
        var ys = views.Select(v => Platform.WindowFrame(v).Y).ToArray();
        Note(string.Join(", ", order.Zip(ys, (id, y) => $"{id} {y:0}")));
        for (var i = 1; i < ys.Length; i++) Check(ys[i] > ys[i - 1], $"{order[i]} follows {order[i - 1]}");

        c.Render(List([new("x", ["a"]), new("y", ["b", "c"])]).Height(500));
        await c.WaitFor(() => Platform.WindowFrame(c.ById<IView>("cell-b")).Y > Platform.WindowFrame(c.ById<IView>("h-y")).Y, "b to move into group y");
        await c.Settle(300);
        var hy = Platform.WindowFrame(c.ById<IView>("h-y")).Y;
        var fy = Platform.WindowFrame(c.ById<IView>("f-y")).Y;
        var moved = Platform.WindowFrame(c.ById<IView>("cell-b")).Y;
        Check(moved > hy && moved < fy, "the moved item's cell is between its new group's header and footer");
    }

    // ── Selection ────────────────────────────────────────────────────────────

    private sealed record ListSelectionLog(List<string> Reports);

    private static CollectionViewElement SelectableList(string[] items, SelectionMode mode, string? key, ListSelectionLog log, Action<string?>? accept) =>
        CollectionView(items, k => k, (k, s) => Label(s.IsSelected ? $"{k} selected" : k).AutomationId($"cell-{k}").Height(44)) with
        {
            SelectionMode = mode,
            SelectedKey = key,
            SelectedKeys = key is null ? null : [key],
            OnSelectionChanged = keys =>
            {
                log.Reports.Add(string.Join("+", keys));
                accept?.Invoke(keys.Count > 0 ? keys[0] : null);
            },
        };

    private sealed record DetachedList(ReactorRoot Root, ContentView Holder, Func<MauiCollectionView?> List);

    /// <summary>A Reactor root whose list gets its handler off-window, as in a page that isn't shown yet.</summary>
    private static DetachedList OffWindow(FixtureContext c, ElementHandlerRegistry registry)
    {
        var holder = new ContentView { HeightRequest = 400 };
        _ = Microsoft.Maui.Platform.ElementExtensions.ToPlatform(holder, c.Window.Handler!.MauiContext!);
        var root = new ReactorRoot(registry, new Microsoft.UI.Reactor.Maui.Hosting.MauiRenderScheduler(c.Window.Dispatcher),
            native => holder.Content = native is null ? null : Props.AsView(native));
        root.UnhandledException += c.UnhandledErrors.Add;
        return new(root, holder, () => holder.Content as MauiCollectionView);
    }

    private static bool Deferred(MauiCollectionView list) => ItemsPlatform.NativeSelection(list) == "";

    private static async Task OverlappingProgrammatic(FixtureContext c, ElementHandlerRegistry registry)
    {
        foreach (var verify in new[] { true, false })
        {
            var log = new ListSelectionLog([]);
            var detached = OffWindow(c, registry);
            detached.Root.Render(SelectableList(Letters, SelectionMode.Single, "a", log, null));
            var list = detached.List()!;
            if (!verify) ((ReactorCollectionView)list).Verifier.Port = null;
            var deferred = Deferred(list);
            c.ShowNative(detached.Holder);
            // In the window now: B selects at once, while MAUI's deferred A is still queued.
            detached.Root.Render(SelectableList(Letters, SelectionMode.Single, "b", log, null));
            await c.WaitFor(() => list.IsLoaded, "the list to load");
            await c.Settle(600);
            var native = ItemsPlatform.NativeSelection(list);
            var corrections = ((ReactorCollectionView)list).Verifier.Corrections;
            Note($"verification {(verify ? "on" : "off")}: A deferred {deferred}, native [{native}], virtual {(list.SelectedItem as ItemBox)?.Key}, corrections {corrections}, reports [{string.Join(";", log.Reports)}]");
            Equal("b", (list.SelectedItem as ItemBox)?.Key, "the virtual selection");
            Equal(0, log.Reports.Count, "programmatic writes report nothing");
            if (verify) Equal("0:1", native, "the native selection is the declared one");
#if IOS || MACCATALYST
            // The overlap this fixture is about happened: A's selection was deferred off-window.
            Check(deferred, "MAUI deferred A's native selection (the precondition)");
            if (verify) Check(corrections >= 1, "the verification repaired the native selection");
            else Equal("0:0", native, "without the verification, MAUI's deferred A lands last (positive witness)");
#endif
            detached.Root.Unmount();
            c.ShowNative(new MauiLabel { Text = "between runs" });
            await c.Settle(100);
        }
    }

    private static async Task OverlappingUser(FixtureContext c, ElementHandlerRegistry registry)
    {
        var log = new ListSelectionLog([]);
        var detached = OffWindow(c, registry);
        string? selected = "a";
        void Render() => detached.Root.Render(SelectableList(Letters, SelectionMode.Single, selected, log, k => { selected = k; Render(); }));
        Render();
        var list = detached.List()!;
        var deferred = Deferred(list);
        c.ShowNative(detached.Holder);
        await c.WaitFor(() => list.Handler?.PlatformView is not null, "the list to get its platform view");
        // The user selects B while MAUI's deferred A may still be pending.
        await c.WaitFor(() => ItemsPlatform.TapItem(list, 1), "the cell to be realized for the tap");
        await c.Settle(600);
        var native = ItemsPlatform.NativeSelection(list);
        Note($"A deferred {deferred}, native [{native}], reports [{string.Join(";", log.Reports)}]");
#if IOS || MACCATALYST
        Check(deferred, "MAUI deferred A's native selection (the precondition)");
#endif
        Equal("b", string.Join(";", log.Reports), "exactly the user's selection, and no report from the repair");
        Equal("b", (list.SelectedItem as ItemBox)?.Key, "the virtual selection");
        Equal("0:1", native, "the native selection");
        detached.Root.Unmount();
    }

    private static async Task EnterNone(FixtureContext c, ElementHandlerRegistry registry)
    {
        foreach (var (mode, pending, verify) in new[]
        {
            (SelectionMode.Single, false, true), (SelectionMode.Multiple, false, true), (SelectionMode.Single, true, true),
            (SelectionMode.Single, false, false), (SelectionMode.Single, true, false),
        })
        {
            var log = new ListSelectionLog([]);
            var detached = OffWindow(c, registry);
            detached.Root.Render(SelectableList(Letters, mode, "b", log, null));
            var list = detached.List()!;
            if (!verify) ((ReactorCollectionView)list).Verifier.Port = null;
            c.ShowNative(detached.Holder);
            if (!pending)
            {
                await c.WaitFor(() => list.IsLoaded, "the list to load");
                await c.WaitFor(() => ItemsPlatform.NativeSelection(list) == "0:1", "the selection to reach the platform");
            }
            detached.Root.Render(SelectableList(Letters, SelectionMode.None, "b", log, null));
            await c.Settle(600);
            var native = ItemsPlatform.NativeSelection(list);
            Note($"{mode} to None, pending deferral {pending}, verification {(verify ? "on" : "off")}: native [{native}], reports [{string.Join(";", log.Reports)}]");
            Equal(0, log.Reports.Count, "entering None reports nothing");
            // Without the verification the result is recorded, not asserted: UIKit deselects everything when
            // MAUI sets AllowsSelection to false, so entering None needs no repair there (§3.13).
            if (verify) Equal("", native, "no native selection in None");
            detached.Root.Unmount();
            c.ShowNative(new MauiLabel { Text = "between runs" });
            await c.Settle(100);
        }
    }

    private static async Task SelectionInItems(FixtureContext c, ElementHandlerRegistry registry)
    {
        foreach (var accept in new[] { true, false })
        {
            var log = new ListSelectionLog([]);
            string? selected = "a";
            var seen = new HashSet<string>();
            void Render() => c.Render(SelectableList(Letters, SelectionMode.Single, selected, log, accept ? k => { selected = k; Render(); } : null).Height(400));
            c.Show(Empty(), registry);
            Render();
            var list = c.Find<MauiCollectionView>();
            await Loaded<IView>(c, "cell-b");
            await c.WaitFor(() => ItemsPlatform.NativeSelection(list) == "0:0", "a's selection to reach the platform");
            await c.WaitFor(() => ItemsPlatform.TapItem(list, 1), "the cell to be realized");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 600)
            {
                seen.Add(Platform.Text(c.ById<IView>("cell-b"))!);
                await Task.Delay(16);
            }
            var native = ItemsPlatform.NativeSelection(list);
            Note($"{(accept ? "accepted" : "refused")}: b showed [{string.Join(",", seen)}], native [{native}], highlight on the selected item {ItemsPlatform.Highlight(list, accept ? 1 : 0)}");
            Equal("b", string.Join(";", log.Reports), "the tap is reported once");
            if (accept)
            {
                Equal("b selected", Platform.Text(c.ById<IView>("cell-b")), "the item renders its selection");
                Equal("0:1", native, "the native selection");
            }
            else
            {
                Check(!seen.Contains("b selected"), "a refused tap never renders the item selected");
                Equal("0:0", native, "the native selection returns");
            }
            Equal("none", ItemsPlatform.Highlight(list, accept ? 1 : 0), "the platform's highlight is off");
            c.Unmount();
        }

        // Positive control: the item-only template keeps MAUI's highlight.
        c.Show((CollectionView(Letters, k => k, k => Label(k).AutomationId($"cell-{k}").Height(44)) with
        {
            SelectionMode = SelectionMode.Single,
            SelectedKey = "a",
        }).Height(400), registry);
        var plain = c.Find<MauiCollectionView>();
        await Loaded<IView>(c, "cell-a");
        await c.WaitFor(() => ItemsPlatform.NativeSelection(plain) == "0:0", "the selection to reach the platform");
        await c.Settle(300);
        Equal("shown", ItemsPlatform.Highlight(plain, 0), "MAUI's highlight on the item-only list (positive control)");
    }

    private static async Task SelectionRecycling(FixtureContext c, ElementHandlerRegistry registry)
    {
        var items = Enumerable.Range(0, 120).Select(i => $"i{i}").ToArray();
        var list = TypedElementRef.Create<MauiCollectionView>();
        var log = new ListSelectionLog([]);
        c.Show((SelectableList(items, SelectionMode.Single, "i3", log, null) with { }).Height(300).Ref(list), registry);
        await Loaded<IView>(c, "cell-i3");
        var seenByCell = new Dictionary<object, HashSet<string>>(ReferenceEqualityComparer.Instance);
        void Track()
        {
            foreach (var cell in c.FindAll<ContentView>(v => v.GetType().Name == "ReactorCell"))
                if (cell.BindingContext is ItemBox box)
                    (seenByCell.TryGetValue(cell, out var keys) ? keys : seenByCell[cell] = []).Add(box.Key);
        }
        Track();
        foreach (var target in new[] { 60, 119, 30, 0 })
        {
            list.Current!.ScrollTo(target, position: ScrollToPosition.Start, animate: false);
            await c.Settle(250);
            Track();
        }
        var reused = seenByCell.Values.Count(keys => keys.Count >= 2);
        Note($"{seenByCell.Count} cells seen, {reused} showed two or more items");
        Check(reused >= 1, "a cell instance showed two different items (identity witness)");
        foreach (var label in c.FindAll<IView>(v => v.AutomationId?.StartsWith("cell-i", StringComparison.Ordinal) == true))
        {
            if (label.Handler?.PlatformView is null) continue;
            var key = label.AutomationId["cell-".Length..];
            Equal(key == "i3" ? "i3 selected" : key, Platform.Text(label), $"the cell showing {key}");
        }
        // Back at the top: the selected item is realized again, and so is its native selection.
        await c.WaitFor(() => ItemsPlatform.NativeSelection(list.Current!) != "", "the selected item to be realized natively");
        Equal("0:3", ItemsPlatform.NativeSelection(list.Current!), "the native selection");
        Equal(0, log.Reports.Count, "scrolling reports nothing");
    }

    private static async Task SelectionThroughReactor(FixtureContext c)
    {
        var rows = new List<string>();
        async Task Case(string name, string expectedVirtual, string expectedNative, Func<ListSelectionLog, Action<Element>, Task> run)
        {
            var log = new ListSelectionLog([]);
            c.Show(Empty());
            await run(log, e => c.Render(e));
            await c.Settle();
            var list = c.Find<MauiCollectionView>();
            var selection = list.SelectionMode == SelectionMode.Multiple ? string.Join("+", list.SelectedItems.OfType<ItemBox>().Select(b => b.Key)) : (list.SelectedItem as ItemBox)?.Key ?? "-";
            var native = ItemsPlatform.NativeSelection(list);
            var row = $"{name}: reports [{string.Join(";", log.Reports)}], virtual {selection}, native [{native}]";
            rows.Add(row);
            Note(row);
            Equal(expectedVirtual, selection, $"the virtual selection ({name})");
            Equal(expectedNative, native, $"the native selection ({name})");
            c.Unmount();
        }
        Element L(string[] items, SelectionMode mode, string? key, ListSelectionLog log, string[]? keys = null) =>
            (SelectableList(items, mode, key, log, null) with { SelectedKeys = keys ?? (key is null ? null : [key]) }).Height(300);

        await Case("declared b", "b", "0:1", async (log, render) => { render(L(Letters, SelectionMode.Single, "a", log)); await c.Settle(200); render(L(Letters, SelectionMode.Single, "b", log)); });
        await Case("items change with the selection", "c", "0:3", async (log, render) => { render(L(Letters, SelectionMode.Single, "a", log)); await c.Settle(200); render(L(["z", .. Letters], SelectionMode.Single, "c", log)); });
        await Case("selected item removed", "-", "", async (log, render) => { render(L(Letters, SelectionMode.Single, "b", log)); await c.Settle(200); render(L(["a", "c"], SelectionMode.Single, "b", log)); });
        await Case("single to none", "b", "", async (log, render) => { render(L(Letters, SelectionMode.Single, "b", log)); await c.Settle(200); render(L(Letters, SelectionMode.None, "b", log)); });
        await Case("multiple a+b then b", "b", "0:1", async (log, render) => { render(L(Letters, SelectionMode.Multiple, null, log, ["a", "b"])); await c.Settle(200); render(L(Letters, SelectionMode.Multiple, null, log, ["b"])); });
        foreach (var row in rows) Check(row.Contains("reports []", StringComparison.Ordinal), $"no programmatic case reports: {row}");
    }

    // ── SwipeView ────────────────────────────────────────────────────────────

    private static async Task SwipeOpenClose(FixtureContext c, ElementHandlerRegistry registry)
    {
        var swipe = TypedElementRef.Create<MauiSwipeView>();
        SwipeViewElement Swipe(string text) => SwipeView(Label(text).AutomationId("row").Height(120).BackgroundColor(Colors.LightGray)) with
        {
            LeftItems = [new SwipeItemData("Left", () => { }) { BackgroundColor = Colors.Green }],
            RightItems = [new SwipeItemData("Archive", () => { }) { BackgroundColor = Colors.Orange }, new SwipeItemData("Delete", () => { }) { BackgroundColor = Colors.Red }],
        };
        c.Show(VStack(Swipe("row").Ref(swipe)), registry);
        await Loaded<IView>(c, "row");
        await c.Settle(300);
        var view = swipe.Current!;
        Close(0, ItemsPlatform.ContentOffsetX(view), "closed at first");

        view.Open(OpenSwipeItem.RightItems, animated: false);
        await c.WaitFor(() => ItemsPlatform.ContentOffsetX(view) < -20, "the right items to open");
        await c.Settle(300);
        var open = ItemsPlatform.ContentOffsetX(view);
        c.Render(VStack(Swipe("row, re-rendered").Ref(swipe)));
        await c.Settle(300);
        Close(open, ItemsPlatform.ContentOffsetX(view), "a re-render with new content keeps it open");
        Equal("row, re-rendered", Platform.Text(c.ById<IView>("row")), "the content updated");

        view.Close(animated: false);
        await c.WaitFor(() => Math.Abs(ItemsPlatform.ContentOffsetX(view)) < 1, "it to close");
        view.Open(OpenSwipeItem.LeftItems, animated: false);
        await c.WaitFor(() => ItemsPlatform.ContentOffsetX(view) > 20, "the left items to open");
        Note($"right open offset {open:0.#}, left open offset {ItemsPlatform.ContentOffsetX(view):0.#}");
        view.Close(animated: false);
        await c.WaitFor(() => Math.Abs(ItemsPlatform.ContentOffsetX(view)) < 1, "it to close again");
    }

    private static IView? Descendant(IVisualTreeElement from, string automationId)
    {
        foreach (var child in from.GetVisualChildren())
        {
            if (child is IView view && view.AutomationId == automationId) return view;
            if (Descendant(child, automationId) is { } found) return found;
        }
        return null;
    }

    private static async Task SwipeContentItems(FixtureContext c, ElementHandlerRegistry registry)
    {
        var swipe = TypedElementRef.Create<MauiSwipeView>();
        var calls = new List<string>();
        SwipeViewElement Swipe(string generation) => SwipeView(Label("row").AutomationId("row").Height(80).BackgroundColor(Colors.LightGray)) with
        {
            RightItems = [new SwipeItemData("Archive", () => calls.Add($"archive {generation}"))
            {
                Content = Label($"Archive {generation}").AutomationId("archive-content").Padding(12).BackgroundColor(Colors.Orange),
            }],
        };
        c.Show(VStack(Swipe("first").Ref(swipe)).Width(360), registry);
        await Loaded<IView>(c, "row");
        await c.Settle(300);
        var view = swipe.Current!;
        var item = (SwipeItemView)view.RightItems[0];

        view.Open(OpenSwipeItem.RightItems, animated: false);
        await c.WaitFor(() => ItemsPlatform.ContentOffsetX(view) < -20, "the right items to open");
        IView? content = null;
        await c.WaitFor(() => (content = Descendant(item, "archive-content")) is { Handler.PlatformView: not null } && Platform.Size(content).Width > 0 && Platform.Size(content).Height > 0,
            "the content item to be realized with a size");
        var size = Platform.Size(content!);
        Note($"open offset {ItemsPlatform.ContentOffsetX(view):0.#}, content {size.Width:0.#}x{size.Height:0.#}, text '{Platform.Text(content!)}'");
        Equal("Archive first", Platform.Text(content!), "the content item shows its element");

        // The structure holds (one content item titled Archive): the content re-renders in place and
        // the swipe stays open.
        c.Render(VStack(Swipe("second").Ref(swipe)).Width(360));
        await c.Settle(300);
        Check(ReferenceEquals(item, view.RightItems[0]), "the item wasn't rebuilt");
        Equal("Archive second", Platform.Text(Descendant(item, "archive-content")!), "the content re-rendered in place");
        Check(ItemsPlatform.ContentOffsetX(view) < -20, "the swipe stayed open");

        ((Microsoft.Maui.ISwipeItem)item).OnInvoked();
        await c.Settle(200);
        Equal("archive second", string.Join(";", calls), "invoking runs the current render's callback, once");
        view.Close(animated: false);
    }

    private static async Task SwipeThreshold(FixtureContext c, ElementHandlerRegistry registry)
    {
        var swipe = TypedElementRef.Create<MauiSwipeView>();
        var calls = new List<string>();
        SwipeViewElement Swipe(string generation, bool contentItem) => SwipeView(Label("row").AutomationId("row").Height(80).BackgroundColor(Colors.LightGray)) with
        {
            LeftItems = [contentItem
                // MAUI caps Threshold at an Execute side's open distance, which for content items is
                // their width: the content is wider than the threshold, so 100 applies on every platform.
                ? new SwipeItemData("Done", () => calls.Add($"done {generation}")) { Content = Label("✓").AutomationId("done-content").Width(120).BackgroundColor(Colors.Green) }
                : new SwipeItemData("Done", () => calls.Add($"done {generation}")) { BackgroundColor = Colors.Green }],
            LeftItemsMode = SwipeMode.Execute,
            RightItems = [new SwipeItemData("Archive", () => calls.Add($"archive {generation}")) { BackgroundColor = Colors.Orange }],
            Threshold = 100,
        };
        foreach (var contentItem in new[] { false, true })
        {
            calls.Clear();
            c.Show(VStack(Swipe("first", contentItem).Ref(swipe)).Width(360), registry);
            await Loaded<IView>(c, "row");
            await c.Settle(300);
            var view = swipe.Current!;

            await ItemsPlatform.Swipe(view, -40);
            await c.Settle(500);
            Note($"below the threshold: offset {ItemsPlatform.ContentOffsetX(view):0.#}, calls [{string.Join(";", calls)}]");
            Equal(0, calls.Count, "below the threshold nothing is invoked");
            Close(0, ItemsPlatform.ContentOffsetX(view), "below the threshold it closes", 2);

            await ItemsPlatform.Swipe(view, -160);
            await c.Settle(500);
            var revealed = ItemsPlatform.ContentOffsetX(view);
            Note($"reveal side: offset {revealed:0.#}, calls [{string.Join(";", calls)}]");
            Check(revealed < -20, "above the threshold a Reveal side stays open");
            Equal(0, calls.Count, "a Reveal side invokes nothing");
            view.Close(animated: false);
            await c.Settle(300);

            c.Render(VStack(Swipe("second", contentItem).Ref(swipe)).Width(360));
            await c.Settle(200);
            await ItemsPlatform.Swipe(view, 40);
            await c.Settle(500);
            Note($"execute side below the threshold: offset {ItemsPlatform.ContentOffsetX(view):0.#}, calls [{string.Join(";", calls)}]");
            Equal(0, calls.Count, "below the threshold an Execute side invokes nothing");
            Close(0, ItemsPlatform.ContentOffsetX(view), "below the threshold the Execute side closes", 2);

            await ItemsPlatform.Swipe(view, 160);
            await c.Settle(600);
            Note($"execute side ({(contentItem ? "content item" : "text item")}): calls [{string.Join(";", calls)}]");
            Equal("done second", string.Join(";", calls), "an Execute side invokes its first item once, with the current render's callback");
            c.Unmount();
        }
    }

    // ── RadioButton ──────────────────────────────────────────────────────────

    private sealed class ThemePicker : Component<(List<string> Log, bool Accept)>
    {
        public override Element Render()
        {
            var (theme, setTheme) = UseState("light");
            Element Card(string name) => Label(theme == name ? $"{name} on" : $"{name} off").AutomationId($"card-{name}").Padding(12);
            RadioButtonElement Option(string name, bool indicator) =>
                RadioButton(Card(name), theme == name, on =>
                {
                    Props.Log.Add($"{name} {on}");
                    if (on && Props.Accept) setTheme(name);
                }, "theme") with { IsIndicatorVisible = indicator };
            return VStack(8,
                Option("light", false).Semantics(description: "Light theme").AutomationId("radio-light"),
                Option("dark", false).Semantics(description: "Dark theme").AutomationId("radio-dark"),
                Option("system", true).Semantics(description: "System theme").AutomationId("radio-system"));
        }
    }

    private static async Task RadioContent(FixtureContext c, ElementHandlerRegistry registry)
    {
        foreach (var accept in new[] { true, false })
        {
            var log = new List<string>();
            c.Show(Component<ThemePicker, (List<string>, bool)>((log, accept)), registry);
            var light = (MauiRadioButton)await Loaded<IView>(c, "radio-light");
            var dark = (MauiRadioButton)await Loaded<IView>(c, "radio-dark");
            var system = (MauiRadioButton)await Loaded<IView>(c, "radio-system");
            var card = await Loaded<IView>(c, "card-dark");
            var systemCard = await Loaded<IView>(c, "card-system");
            await c.WaitFor(() => Platform.Size(systemCard).Width > 0, "the radio buttons to be laid out");
            await c.Settle(200);
            Close(Platform.Size(card).Width, Platform.Size(dark).Width, "a content-only radio button is as wide as its content", 2);
            Check(Platform.Size(system).Width > Platform.Size(systemCard).Width + 10, "the default template adds the indicator");

            var seen = new HashSet<string>();
            ItemsPlatform.TapRadio(dark);
            var afterTap = $"light {ItemsPlatform.IsChecked(light)}, dark {ItemsPlatform.IsChecked(dark)}";
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 500)
            {
                seen.Add(Platform.Text(card)!);
                await Task.Delay(16);
            }
            Note($"{(accept ? "accepted" : "refused")}: right after the tap {afterTap}; reports [{string.Join(";", log)}]; dark showed [{string.Join(",", seen)}]");
            Equal("light False;dark True", string.Join(";", log), "the peer's uncheck, then the check, once each");
            Equal("light False, dark True", afterTap, "the native group is settled within the tap");
            if (accept)
            {
                Equal("dark on", Platform.Text(card), "the content renders the declared check");
                Check(!light.IsChecked && dark.IsChecked, "the declared state reached the buttons");
            }
            else
            {
                Check(!seen.Contains("dark on"), "a refused tap never renders the content checked");
                Check(light.IsChecked && !dark.IsChecked, "the declarations are restored");
            }
            c.Unmount();
        }
    }

    private static async Task RadioGroupsOnDevice(FixtureContext c, ElementHandlerRegistry registry)
    {
        var log = new List<string>();
        RadioButtonElement R(string name, bool on, string? group) =>
            RadioButton(name, on, v => log.Add($"{name} {v}"), group).AutomationId($"r-{name}");
        c.Show(VStack(
            HStack(VStack(R("a", true, "g")), Grid("*", "*", VStack(R("b", false, "g")))),
            VStack(R("c", true, null), R("d", false, null)),
            VStack(R("e", true, null), R("f", false, null))), registry);
        var radios = new Dictionary<string, MauiRadioButton>();
        foreach (var name in new[] { "a", "b", "c", "d", "e", "f" }) radios[name] = (MauiRadioButton)await Loaded<IView>(c, $"r-{name}");
        await c.Settle(200);

        ItemsPlatform.TapRadio(radios["b"]);
        ItemsPlatform.TapRadio(radios["d"]);
        var state = string.Join(",", radios.Select(p => $"{p.Key}={ItemsPlatform.IsChecked(p.Value)}"));
        Note($"after the taps: {state}; reports [{string.Join(";", log)}]");
        Equal("a False;b True;c False;d True", string.Join(";", log), "each group's peers unchecked and reported");
        Equal("a=False,b=True,c=False,d=True,e=True,f=False", state, "only the tapped groups changed, natively");
        await c.Settle();
        // Refused: every button returns to its declaration.
        Equal("a=True,b=False,c=True,d=False,e=True,f=False", string.Join(",", radios.Select(p => $"{p.Key}={ItemsPlatform.IsChecked(p.Value)}")), "the declarations are restored");
        Equal(4, log.Count, "nothing more is reported");
    }

    private static async Task RadioAccessibility(FixtureContext c, ElementHandlerRegistry registry)
    {
        Element Pair(string selected) => VStack(8,
            RadioButton(Label("Light"), selected == "light", _ => { }, "theme").Semantics(description: "Light theme").AutomationId("radio-light"),
            RadioButton(Label("Dark"), selected == "dark", _ => { }, "theme").Semantics(description: "Dark theme").AutomationId("radio-dark"));
        c.Show(Pair("light"), registry);
        var light = (MauiRadioButton)await Loaded<IView>(c, "radio-light");
        var dark = (MauiRadioButton)await Loaded<IView>(c, "radio-dark");
        await c.Settle(300);
        foreach (var selected in new[] { "light", "dark" })
        {
            if (selected == "dark")
            {
                // A declared change: the exposed state flips with it.
                c.Render(Pair("dark"));
                await c.WaitFor(() => dark.IsChecked, "the declared check");
                await c.Settle(300);
            }
            var (on, off) = selected == "light" ? (light, dark) : (dark, light);
            var checkedInfo = ItemsPlatform.Accessibility(on);
            var uncheckedInfo = ItemsPlatform.Accessibility(off);
            Note($"{selected} checked: {checkedInfo}");
            Note($"{selected} checked, other: {uncheckedInfo}");
            Check(ItemsPlatform.Accessibility(light).Contains("label=Light theme", StringComparison.Ordinal), "the accessible name is the description");
            Check(ItemsPlatform.Accessibility(dark).Contains("label=Dark theme", StringComparison.Ordinal), "the accessible name is the description");
#if ANDROID
            Check(checkedInfo.Contains("checkable=True", StringComparison.Ordinal) && checkedInfo.Contains("checked=True", StringComparison.Ordinal), "the checked state is exposed");
            Check(uncheckedInfo.Contains("checkable=True", StringComparison.Ordinal) && uncheckedInfo.Contains("checked=False", StringComparison.Ordinal), "the unchecked state is exposed");
            Check(checkedInfo.Contains("RadioButton", StringComparison.Ordinal) && uncheckedInfo.Contains("RadioButton", StringComparison.Ordinal), "the radio role is exposed");
#elif IOS || MACCATALYST
            foreach (var (view, value) in new[] { (on, "1"), (off, "0") })
            {
                var native = (UIKit.UIView)view.Handler!.PlatformView!;
                Check((native.AccessibilityTraits & UIKit.UIAccessibilityTrait.Button) != 0, $"{view.AutomationId} has the Button trait");
                Equal(value, native.AccessibilityValue, $"{view.AutomationId}'s accessibility value");
            }
#endif
        }
    }

    // ── Picker, Label ────────────────────────────────────────────────────────

    private sealed record Project(string Id, string Name);

    private static async Task PickerDisplay(FixtureContext c, ElementHandlerRegistry registry)
    {
        var projects = new[] { new Project("1", "Alpha"), new Project("2", "Beta"), new Project("3", "Gamma") };
        c.Show(Picker(projects, p => $"{p.Name} ({p.Id})", 1, title: "Project").AutomationId("picker"), registry);
        var picker = await Loaded<IView>(c, "picker");
        await c.Settle(200);
        Equal("Beta (2)", Platform.Text(picker), "the platform shows the selected item's display text");
        Equal("Alpha (1)|Beta (2)|Gamma (3)", string.Join("|", ((MauiPicker)picker).Items), "the items");
    }

    private static async Task LabelSpans(FixtureContext c, ElementHandlerRegistry registry)
    {
        var calls = new List<string>();
        Element Text(bool link) => Label(
            Run("Total ").FontSize(14).TextColor(Colors.Red),
            Run("$5 ").FontSize(28),
            link ? Hyperlink("details", () => calls.Add("details")).TextColor(Colors.Blue) : Run("details").TextColor(Colors.Blue))
            .AutomationId("spans").Width(300);
        c.Show(Text(true), registry);
        var label = (MauiLabel)await Loaded<IView>(c, "spans");
        await c.WaitFor(() => label.Width > 0, "the label to be laid out");
        await c.Settle(300);
        var runs = ItemsPlatform.Runs(label);
        Note(runs);
        Check(runs.Contains("'Total ' size=14 color=FF0000", StringComparison.Ordinal), "the first run's size and color");
        Check(runs.Contains("'$5 ' size=28", StringComparison.Ordinal), "the second run's size");
        Check(runs.Contains("'details' size=", StringComparison.Ordinal) && runs.Contains("color=0000FF underline", StringComparison.Ordinal), "the hyperlink's color and underline");

        var link = label.FormattedText.Spans[2];
        var at = ItemsPlatform.PointOn(label, link) ?? throw new CheckFailedException("no point of the label hit-tests to the hyperlink");
        var plain = ItemsPlatform.PointOn(label, label.FormattedText.Spans[0]);
        Note($"hyperlink at {at}");
        await ItemsPlatform.TapLabel(label, at);
        await c.WaitFor(() => calls.Count > 0, "the hyperlink's callback");
        await c.Settle(200);
        Equal("details", string.Join(";", calls), "the hyperlink runs once");

        // Same geometry, the run there is plain: nothing runs.
        c.Render(Text(false));
        await c.Settle(300);
        await ItemsPlatform.TapLabel(label, at);
        await c.Settle(300);
        Equal(1, calls.Count, "the same point on a plain run invokes nothing (negative control)");
        c.Render(Text(true));
        await c.Settle(300);
        if (plain is null)
        {
            // Spans without recognizers aren't hit-tested by MAUI; take the left edge instead.
            plain = new Point(2, 2);
        }
        await ItemsPlatform.TapLabel(label, plain.Value);
        await c.Settle(300);
        Equal(1, calls.Count, "a tap on another run invokes nothing");
    }

    // ── Radio characterization ───────────────────────────────────────────────

    private static async Task RadioCharacterization(FixtureContext c)
    {
        var log = new List<string>();
        RadioButtonElement R(string name, bool on, Action<bool>? also = null) =>
            RadioButton(name, on, v => { log.Add($"{name} {v}"); also?.Invoke(v); }, "char").AutomationId($"rc-{name}");
        Element Pair(string selected, bool reversed) => reversed
            ? VStack(R("b", selected == "b"), R("a", selected == "a"))
            : VStack(R("a", selected == "a"), R("b", selected == "b"));
        foreach (var reversed in new[] { false, true })
        {
            log.Clear();
            c.Show(Pair("a", reversed));
            var a = (MauiRadioButton)await Loaded<IView>(c, "rc-a");
            var b = (MauiRadioButton)await Loaded<IView>(c, "rc-b");
            c.Render(Pair("b", reversed));
            await c.Settle();
            Note($"declared a→b ({(reversed ? "b first" : "a first")}): reports [{string.Join(";", log)}], native a {ItemsPlatform.IsChecked(a)}, b {ItemsPlatform.IsChecked(b)}");
            Equal(0, log.Count, "a declared change reports nothing");
            Check(!ItemsPlatform.IsChecked(a) && ItemsPlatform.IsChecked(b), "the declaration reached the platform");
            c.Unmount();
        }
    }
}
