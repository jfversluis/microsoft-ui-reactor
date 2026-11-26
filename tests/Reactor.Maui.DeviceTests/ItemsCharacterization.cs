using System.Collections.ObjectModel;
using MauiCollectionView = Microsoft.Maui.Controls.CollectionView;
using MauiRadioButton = Microsoft.Maui.Controls.RadioButton;
using static Reactor.Maui.DeviceTests.FixtureContext;

namespace Reactor.Maui.DeviceTests;

/// <summary>Platform readback for the spec 070 §3 fixtures (collections, radio buttons, swipe views, spans).</summary>
public static class ItemsPlatform
{
    private static object? Native(IView view) => view.Handler?.PlatformView;

#if IOS || MACCATALYST
    /// <summary>The UICollectionView a CollectionView renders (the handler's view, or one inside it).</summary>
    public static UIKit.UICollectionView? CollectionView(IView view)
    {
        if (Native(view) is not UIKit.UIView root) return null;
        var pending = new Stack<UIKit.UIView>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (next is UIKit.UICollectionView found) return found;
            foreach (var child in next.Subviews) pending.Push(child);
        }
        return null;
    }

    /// <summary>Runs <paramref name="completion"/> after the batches already queued on the collection view.</summary>
    public static bool Barrier(IView view, Action completion)
    {
        if (CollectionView(view) is not { } native) return false;
        native.PerformBatchUpdates(null, _ => completion());
        return true;
    }
#elif ANDROID
    private static AndroidX.RecyclerView.Widget.RecyclerView? Recycler(IView view)
    {
        if (Native(view) is not Android.Views.View root) return null;
        var pending = new Stack<Android.Views.View>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (next is AndroidX.RecyclerView.Widget.RecyclerView found) return found;
            if (next is Android.Views.ViewGroup group)
                for (var i = 0; i < group.ChildCount; i++) pending.Push(group.GetChildAt(i)!);
        }
        return null;
    }
#endif

    /// <summary>
    /// The native selection as "section:item" positions: iOS's selected index paths; on Android the
    /// realized item views MAUI marks selected (<c>Activated</c>).
    /// </summary>
    public static string NativeSelection(IView view)
    {
#if IOS || MACCATALYST
        if (CollectionView(view) is not { } native) return "<no native view>";
        var paths = native.GetIndexPathsForSelectedItems() ?? [];
        return string.Join(",", paths.Select(p => $"{p.Section}:{p.Item}").Order());
#elif ANDROID
        if (Recycler(view) is not { } recycler) return "<no native view>";
        var selected = new List<string>();
        for (var i = 0; i < recycler.ChildCount; i++)
        {
            var child = recycler.GetChildAt(i)!;
            if (child.Activated) selected.Add($"0:{recycler.GetChildAdapterPosition(child)}");
        }
        return string.Join(",", selected.Order());
#else
        return "<unsupported>";
#endif
    }

    /// <summary>Whether the radio button shows checked on the platform (Android's native button), else MAUI's value.</summary>
    public static bool IsChecked(MauiRadioButton radio) => Native(radio) switch
    {
#if ANDROID
        Android.Widget.CompoundButton native => native.Checked,
#endif
        _ => radio.IsChecked,
    };
}

public static partial class Fixtures
{
    private static IEnumerable<Fixture> ItemsCharacterizationFixtures() =>
    [
        new("Gate_CollectionView_SelectionChanged_InsideTheWrite", SelectionChangedInsideWrite),
        new("Gate_CollectionView_BatchCompletionOrder", BatchCompletionOrder),
        new("Gate_RadioButton_CheckedChanged_InsideTheWrite", CheckedChangedInsideWrite),
        new("Gate_RadioButton_NestedWrite_IsDelayed", RadioNestedWriteDelayed),
    ];

    private sealed class WriteLog
    {
        public readonly List<string> Events = [];
        public bool Writing;

        public void Write(Action write)
        {
            Writing = true;
            try { write(); }
            finally { Writing = false; }
        }

        public string Add(string what) { Events.Add((Writing ? "in " : "LATE ") + what); return what; }
        public int Late => Events.Count(e => e.StartsWith("LATE", StringComparison.Ordinal));
    }

    private static MauiCollectionView PlainList(ObservableCollection<string> items, SelectionMode mode, WriteLog log)
    {
        var list = new MauiCollectionView
        {
            ItemsSource = items,
            SelectionMode = mode,
            ItemTemplate = new DataTemplate(() => new Microsoft.Maui.Controls.Label { Padding = 8 }.Apply(l => l.SetBinding(Microsoft.Maui.Controls.Label.TextProperty, "."))),
            HeightRequest = 400,
        };
        list.SelectionChanged += (_, e) => log.Add("selection " + string.Join("+", e.CurrentSelection.Cast<object>()));
        return list;
    }

    private static T Apply<T>(this T value, Action<T> configure) { configure(value); return value; }

    private static string Virtual(MauiCollectionView list) => list.SelectionMode == SelectionMode.Multiple
        ? string.Join("+", list.SelectedItems.Cast<object>())
        : list.SelectedItem?.ToString() ?? "-";

    private static async Task<string> SelectionCase(FixtureContext c, string name, SelectionMode mode,
        Func<MauiCollectionView, ObservableCollection<string>, WriteLog, Task> run)
    {
        var items = new ObservableCollection<string>(["a", "b", "c", "d", "e"]);
        var log = new WriteLog();
        var list = PlainList(items, mode, log);
        await run(list, items, log);
        await c.Settle();
        await Task.Yield();
        var row = $"{name}: events [{string.Join("; ", log.Events)}], virtual {Virtual(list)}, native {ItemsPlatform.NativeSelection(list)}";
        Note(row);
        Check(log.Late == 0, $"a SelectionChanged caused by a write arrived after the write returned: {row}");
        return row;
    }

    private static async Task ShowLoaded(FixtureContext c, MauiCollectionView list)
    {
        c.ShowNative(list);
        await c.WaitFor(() => list.IsLoaded && list.Handler is not null, "the list to load");
        await c.Settle(200);
    }

    /// <summary>
    /// Hard gate 1 (spec 070 §3.4, §3.11): every SelectionChanged a programmatic write causes is
    /// raised inside that write, on plain MAUI.
    /// </summary>
    private static async Task SelectionChangedInsideWrite(FixtureContext c)
    {
        await SelectionCase(c, "loaded, single b", SelectionMode.Single, async (list, items, log) =>
        {
            await ShowLoaded(c, list);
            log.Write(() => list.SelectedItem = "b");
        });
        await SelectionCase(c, "loaded, a then b in one turn", SelectionMode.Single, async (list, items, log) =>
        {
            await ShowLoaded(c, list);
            log.Write(() => { list.SelectedItem = "a"; list.SelectedItem = "b"; });
        });
        await SelectionCase(c, "before load, a then b", SelectionMode.Single, (list, items, log) =>
        {
            c.ShowNative(list);
            Note($"before load: handler {(list.Handler is null ? "none" : "set")}, loaded {list.IsLoaded}");
            log.Write(() => { list.SelectedItem = "a"; list.SelectedItem = "b"; });
            return Task.CompletedTask;
        });
        await SelectionCase(c, "loaded, items change and selection in one write", SelectionMode.Single, async (list, items, log) =>
        {
            await ShowLoaded(c, list);
            log.Write(() => { items.Insert(0, "z"); list.SelectedItem = "c"; });
        });
        await SelectionCase(c, "loaded, selected item removed with the selection cleared", SelectionMode.Single, async (list, items, log) =>
        {
            await ShowLoaded(c, list);
            log.Write(() => list.SelectedItem = "b");
            await c.Settle(200);
            log.Write(() => { items.Remove("b"); list.SelectedItem = null; });
        });
        await SelectionCase(c, "loaded, single to none with a selection", SelectionMode.Single, async (list, items, log) =>
        {
            await ShowLoaded(c, list);
            log.Write(() => list.SelectedItem = "b");
            await c.Settle(200);
            log.Write(() => list.SelectionMode = SelectionMode.None);
        });
        await SelectionCase(c, "loaded, multiple a+b then b, then none", SelectionMode.Multiple, async (list, items, log) =>
        {
            await ShowLoaded(c, list);
            log.Write(() => list.SelectedItems = ["a", "b"]);
            await c.Settle(200);
            log.Write(() => list.SelectedItems = ["b"]);
            await c.Settle(200);
            log.Write(() => list.SelectionMode = SelectionMode.None);
        });
        await SelectionCase(c, "loaded, items added then a and b in one turn", SelectionMode.Single, async (list, items, log) =>
        {
            await ShowLoaded(c, list);
            log.Write(() => { items.Add("x"); list.SelectedItem = "a"; list.SelectedItem = "b"; });
        });
        // Informational: what MAUI does on its own when the selected item is removed.
        await SelectionCase(c, "info: selected item removed, selection left", SelectionMode.Single, async (list, items, log) =>
        {
            await ShowLoaded(c, list);
            log.Write(() => list.SelectedItem = "b");
            await c.Settle(200);
            log.Write(() => items.Remove("b"));
        });
    }

    /// <summary>
    /// Hard gate 3 (spec 070 §3.4): on iOS and Mac Catalyst, MAUI's deferred native selection (a write
    /// while the list isn't loaded) completes before a barrier issued after it, and UIKit completes
    /// queued batches in order.
    /// </summary>
    private static async Task BatchCompletionOrder(FixtureContext c)
    {
#if IOS || MACCATALYST
        var deferred = 0;
        for (var i = 0; i < 12; i++)
        {
            var items = new ObservableCollection<string>(["a", "b", "c", "d", "e"]);
            var log = new WriteLog();
            var list = PlainList(items, SelectionMode.Single, log);
            var order = new List<string>();
            // A handler before the window: MAUI's SelectItem then defers (the list isn't loaded), as for
            // a list Reactor mounts into a page that isn't shown yet.
            _ = Microsoft.Maui.Platform.ElementExtensions.ToPlatform(list, c.Window.Handler!.MauiContext!);
            string? atBarrier = null;
            var nativeView = ItemsPlatform.CollectionView(list);
            var loaded = nativeView?.Window is not null;
            list.SelectedItem = "a";
            var afterWrite = ItemsPlatform.NativeSelection(list);
            if (i % 2 == 1) items.Insert(0, "z"); // an items change queued between MAUI's deferral and the barrier
            var issued = ItemsPlatform.Barrier(list, () => { atBarrier = ItemsPlatform.NativeSelection(list); order.Add("barrier"); });
            ItemsPlatform.Barrier(list, () => order.Add("second"));
            c.ShowNative(list);
            await c.WaitFor(() => order.Count == 2, "both barriers to complete", 10_000);
            var expectedPath = i % 2 == 1 ? "0:1" : "0:0";
            var wasDeferred = afterWrite == "";
            if (wasDeferred) deferred++;
            Note($"run {i}: native view {(nativeView is null ? "none" : loaded ? "in window" : "off window")}, after write [{afterWrite}], at barrier [{atBarrier}], order {string.Join(",", order)}{(i % 2 == 1 ? " (items change)" : "")}");
            Check(issued, "the barrier was issued on the native view");
            Equal("barrier,second", string.Join(",", order), "UIKit completes queued batches in order");
            if (wasDeferred) Equal(expectedPath, atBarrier, $"run {i}: MAUI's deferred selection landed before the barrier");
            c.ShowNative(new Microsoft.Maui.Controls.Label { Text = "between runs" });
            await c.Settle(100);
        }
        Note($"deferred selections observed: {deferred} of 12");
        Check(deferred > 0, "the deferral path was exercised at least once (positive witness)");
#else
        await Task.CompletedTask;
        throw new SkipException("UIKit batch updates only");
#endif
    }

    private static MauiRadioButton PlainRadio(string name, WriteLog log, bool template)
    {
        var radio = new MauiRadioButton { GroupName = "g-" + Guid.NewGuid().ToString("N"), AutomationId = name };
        if (template)
        {
            radio.ControlTemplate = MauiRadioButton.DefaultTemplate;
            radio.Content = new Microsoft.Maui.Controls.Label { Text = name };
        }
        else
        {
            radio.Content = name;
        }
        radio.CheckedChanged += (_, e) => log.Add($"{name} {e.Value}");
        return radio;
    }

    /// <summary>
    /// Hard gate 2 (spec 070 §3.6): every CheckedChanged a programmatic write causes is raised inside
    /// that write, for text and templated radio buttons with unique group names.
    /// </summary>
    private static async Task CheckedChangedInsideWrite(FixtureContext c)
    {
        foreach (var template in new[] { false, true })
        {
            var log = new WriteLog();
            var a = PlainRadio("a", log, template);
            var b = PlainRadio("b", log, template);
            c.ShowNative(new VerticalStackLayout { a, b });
            await c.WaitFor(() => a.IsLoaded && b.IsLoaded, "the radio buttons to load");
            await c.Settle(200);
            log.Write(() => a.IsChecked = true);
            log.Write(() => b.IsChecked = true);
            await c.Settle(200);
            log.Write(() => { a.IsChecked = false; b.IsChecked = false; });
            log.Write(() => a.IsChecked = true);
            await c.Settle();
            await Task.Yield();
            var row = $"{(template ? "templated" : "text")}: [{string.Join("; ", log.Events)}], native a {ItemsPlatform.IsChecked(a)}, b {ItemsPlatform.IsChecked(b)}";
            Note(row);
            Check(log.Late == 0, $"a CheckedChanged caused by a write arrived after the write returned: {row}");
            // Unique names: MAUI's grouping found no peer, so both stayed checked.
            Check(a.IsChecked && !b.IsChecked, $"the final writes reached MAUI: {row}");
            Equal(5, log.Events.Count, "one notification per changing write (positive witness)");
        }
    }

    /// <summary>
    /// Hard gate 4 (spec 070 §3.6): a write to a radio button whose IsChecked setter is still active
    /// (inside its own notification) is applied only after that notification returns.
    /// </summary>
    private static async Task RadioNestedWriteDelayed(FixtureContext c)
    {
        var events = new List<string>();
        var depth = 0;
        // On Android a text radio button is the native AppCompatRadioButton, whose own change listener
        // writes IsChecked back while the setter is active; that write is delayed too, and the two
        // delayed values then alternate forever (observed: the app ran out of memory inside the outer
        // write). Templated radio buttons have no native button, so the sequence ends on every platform.
        MauiRadioButton Radio(string name) => new()
        {
            Content = new Microsoft.Maui.Controls.Label { Text = name },
            ControlTemplate = MauiRadioButton.DefaultTemplate,
            GroupName = "g-" + Guid.NewGuid().ToString("N"),
        };
        var a = Radio("a");
        var b = Radio("b");
        var cRadio = Radio("c");
        a.IsChecked = true;
        c.ShowNative(new VerticalStackLayout { a, b, cRadio });
        await c.WaitFor(() => a.IsLoaded && cRadio.IsLoaded, "the radio buttons to load");
        await c.Settle(200);

        // B's handler unchecks A; A's handler checks C; C's handler unchecks B, whose setter is active.
        b.CheckedChanged += (_, e) =>
        {
            depth++;
            events.Add($"b {e.Value} (depth {depth})");
            if (e.Value) a.IsChecked = false;
            events.Add($"b {e.Value} handler returns (b reads {b.IsChecked})");
            depth--;
        };
        a.CheckedChanged += (_, e) =>
        {
            depth++;
            events.Add($"a {e.Value} (depth {depth})");
            if (!e.Value) cRadio.IsChecked = true;
            depth--;
        };
        cRadio.CheckedChanged += (_, e) =>
        {
            depth++;
            events.Add($"c {e.Value} (depth {depth})");
            if (e.Value)
            {
                b.IsChecked = false;
                events.Add($"c sees b {b.IsChecked} after writing false");
            }
            depth--;
        };

        b.IsChecked = true;
        events.Add("outer write returned");
        await c.Settle();
        Note(string.Join(" | ", events));
        var falseAt = events.IndexOf("b False (depth 1)");
        var returnsAt = events.FindIndex(e => e.StartsWith("b True handler returns", StringComparison.Ordinal));
        var outerAt = events.IndexOf("outer write returned");
        Check(events.Contains("c sees b True after writing false"), "C's write to B was not applied while B's setter was active");
        Check(returnsAt >= 0 && falseAt > returnsAt && falseAt < outerAt,
            $"B's false is raised after B's true handler returns and before the outer write returns: {string.Join(" | ", events)}");
        Check(!b.IsChecked && cRadio.IsChecked && !a.IsChecked, "the delayed write was applied in the end");
    }
}
