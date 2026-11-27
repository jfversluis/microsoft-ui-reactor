using System.Collections.ObjectModel;
using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiLabel = Microsoft.Maui.Controls.Label;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>Captures Reactor's DiagnosticLog warnings whose operation starts with a prefix.</summary>
internal sealed class WarningListener(string operationPrefix) : EventListener
{
    public List<string> Warnings { get; } = [];

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Microsoft-UI-Reactor") EnableEvents(source, EventLevel.Warning, EventKeywords.All);
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (e.EventName != "Warning" || e.Payload is not { Count: >= 3 } payload) return;
        if (payload[1] is string operation && operation.StartsWith(operationPrefix, StringComparison.Ordinal))
            lock (Warnings) Warnings.Add($"{operation}: {payload[2]}");
    }
}

/// <summary>A row that counts its mounts and keeps state, rendering its item's state.</summary>
public sealed record StateRowProps(string Id, bool Selected, List<string> Log);

public sealed class StateRow : Component<StateRowProps>
{
    public static readonly Dictionary<string, Action<int>> SetCount = [];

    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        SetCount[Props.Id] = setCount;
        UseEffect(() => { Props.Log.Add($"mount {Props.Id}"); return () => Props.Log.Add($"unmount {Props.Id}"); });
        Props.Log.Add($"render {Props.Id} {(Props.Selected ? "selected" : "plain")}");
        return Label($"{Props.Id} {(Props.Selected ? "selected" : "plain")} {count}");
    }
}

internal static class Cells
{
    // What the platform does for a visible item: create a cell and bind it.
    public static ContentView Realize(CollectionView view, int index)
    {
        var cell = (ContentView)view.ItemTemplate.CreateContent();
        cell.BindingContext = ((System.Collections.IList)view.ItemsSource)[index];
        return cell;
    }

    public static ContentView Bind(DataTemplate template, object? context)
    {
        var cell = (ContentView)template.CreateContent();
        cell.BindingContext = context;
        return cell;
    }

    public static string Text(ContentView cell) => cell.Content switch
    {
        MauiLabel label => label.Text,
        DirectViewHost host => ((DirectLabel)host.Children[0]).Text,
        _ => "<none>",
    };

    public static ItemBox Box(CollectionView view, string key) =>
        ((System.Collections.IList)view.ItemsSource).OfType<ItemBox>().Single(b => b.Key == key);
}

/// <summary>Spec 070 §3.3: what an item template knows about its selection.</summary>
public class ItemStateTests
{
    private static readonly string[] Keys = ["a", "b", "c"];

    private sealed record ListState(SelectionMode Mode, string? Key, IReadOnlyList<string>? Keys, List<string> Log, bool Reuse = false);

    private static CollectionViewElement List(ListState s, Action<IReadOnlyList<string>>? onChanged = null) =>
        CollectionView(Keys, k => k, (k, state) => Component<StateRow, StateRowProps>(new(k, state.IsSelected, s.Log))) with
        {
            SelectionMode = s.Mode,
            SelectedKey = s.Key,
            SelectedKeys = s.Keys,
            OnSelectionChanged = onChanged,
            ReuseItemTrees = s.Reuse,
        };

    [Fact]
    public void TheTemplateSees_TheDeclaredSelection_AndAChangeUpdatesCellsWithoutRemounting()
    {
        var log = new List<string>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(List(new(SelectionMode.Single, "a", null, log)));
        var view = host.View<CollectionView>();
        var cells = Enumerable.Range(0, 3).Select(i => Cells.Realize(view, i)).ToArray();
        host.Scheduler.Drain();
        StateRow.SetCount["b"](7);
        host.Scheduler.Drain();
        Assert.Equal(["a selected 0", "b plain 7", "c plain 0"], cells.Select(Cells.Text));

        host.Render(List(new(SelectionMode.Single, "b", null, log)));
        host.Scheduler.Drain();

        Assert.Equal(["a plain 0", "b selected 7", "c plain 0"], cells.Select(Cells.Text));
        Assert.Equal(3, log.Count(l => l.StartsWith("mount", StringComparison.Ordinal)));
        Assert.DoesNotContain(log, l => l.StartsWith("unmount", StringComparison.Ordinal));

        host.Render(List(new(SelectionMode.Multiple, null, ["a", "c"], log)));
        Assert.Equal(["a selected 0", "b plain 7", "c selected 0"], cells.Select(Cells.Text));
    }

    [Fact]
    public void ARefusedTap_NeverRendersTheItemSelected()
    {
        var log = new List<string>();
        var reports = new List<IReadOnlyList<string>>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(List(new(SelectionMode.Single, "a", null, log), reports.Add));
        var view = host.View<CollectionView>();
        var cells = Enumerable.Range(0, 3).Select(i => Cells.Realize(view, i)).ToArray();
        host.Scheduler.Drain();
        log.Clear();

        view.SelectedItem = Cells.Box(view, "b");
        Assert.Equal("b", ((ItemBox)view.SelectedItem!).Key); // positive witness: the platform selection did change
        host.Scheduler.Drain();

        Assert.Equal(["b"], Assert.Single(reports));
        Assert.Equal("a", ((ItemBox)view.SelectedItem!).Key);
        Assert.DoesNotContain("render b selected", log);
        Assert.Equal("b plain 0", Cells.Text(cells[1]));
    }

    [Theory]
    [InlineData(SelectionMode.None, "a", new[] { "b" }, "", "-")]
    [InlineData(SelectionMode.Single, "a", new[] { "b" }, "a", "a")]
    [InlineData(SelectionMode.Single, null, new[] { "b" }, "", "-")]
    [InlineData(SelectionMode.Single, "zz", null, "", "-")]
    [InlineData(SelectionMode.Multiple, "a", new[] { "b", "c" }, "b,c", "b,c")]
    [InlineData(SelectionMode.Multiple, "a", new string[0], "", "")]
    [InlineData(SelectionMode.Multiple, "a", null, "", "")]
    [InlineData(SelectionMode.Multiple, null, new[] { "zz", "c" }, "c", "c")]
    public void TheRules_AreTheSameForTheTemplateAndTheWrite(SelectionMode mode, string? key, string[]? keys, string rendered, string written)
    {
        var log = new List<string>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(List(new(mode, key, keys, log)));
        var view = host.View<CollectionView>();
        var cells = Enumerable.Range(0, 3).Select(i => Cells.Realize(view, i)).ToArray();

        var selected = Keys.Where((k, i) => Cells.Text(cells[i]).Contains("selected", StringComparison.Ordinal));
        Assert.Equal(rendered, string.Join(",", selected));
        var write = mode switch
        {
            SelectionMode.Single => ((ItemBox?)view.SelectedItem)?.Key ?? "-",
            SelectionMode.Multiple => string.Join(",", view.SelectedItems.OfType<ItemBox>().Select(b => b.Key)),
            _ => view.SelectedItem is null ? "-" : "set",
        };
        Assert.Equal(written, write);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARecycledCell_RendersItsNewItemsState(bool reuse)
    {
        var log = new List<string>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(List(new(SelectionMode.Single, "a", null, log, reuse)));
        var view = host.View<CollectionView>();
        var cell = Cells.Realize(view, 0);
        Assert.Equal("a selected 0", Cells.Text(cell));

        cell.BindingContext = Cells.Box(view, "b");

        Assert.Equal("b plain 0", Cells.Text(cell));
    }

    private static bool HasSelectedBackgroundState(ContentView cell) =>
        VisualStateManager.GetVisualStateGroups(cell).SelectMany(g => g.States)
            .Any(s => s.Name == VisualStateManager.CommonStates.Selected && s.Setters.Any(x => x.Property == VisualElement.BackgroundColorProperty));

    [Fact]
    public void TheStateOverload_TurnsOffThePlatformHighlight_OnEveryCell()
    {
        var log = new List<string>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(List(new(SelectionMode.Single, "a", null, log)));
        var view = host.View<CollectionView>();
        var early = Cells.Realize(view, 0);
        host.Render(List(new(SelectionMode.Single, "b", null, log)));
        var late = Cells.Realize(view, 1);
        Assert.True(HasSelectedBackgroundState(early));
        Assert.True(HasSelectedBackgroundState(late));

        var plain = new TestHost(MauiHandlers.CreateRegistry()).Render(CollectionView(Keys, k => k, k => Label(k)));
        Assert.False(HasSelectedBackgroundState(Cells.Realize(plain.View<CollectionView>(), 0)));

        // Toggling remounts: iOS clears its highlight once and never restores it.
        host.Render(List(new(SelectionMode.Single, "b", null, log)) with { ShowsSelectionHighlight = true });
        Assert.NotSame(view, host.View<CollectionView>());
        Assert.False(HasSelectedBackgroundState(Cells.Realize(host.View<CollectionView>(), 0)));
    }
}

/// <summary>Spec 070 §3.4: selection reports, echo and the native verification.</summary>
public class CollectionSelectionTests
{
    private sealed record SelectionProps(SelectionMode Mode, List<string> Log, bool Accept, Action<Action<int>>? Other = null);

    private sealed class SelectingList : Component<SelectionProps>
    {
        public override Element Render()
        {
            var (selected, setSelected) = UseState<IReadOnlyList<string>>(["a"]);
            var (other, setOther) = UseState(0);
            Props.Other?.Invoke(setOther);
            Props.Log.Add($"render {string.Join("+", selected)} {other}");
            return CollectionView(new[] { "a", "b", "c" }, k => k, k => Label(k)) with
            {
                SelectionMode = Props.Mode,
                SelectedKey = selected.Count > 0 ? selected[0] : null,
                SelectedKeys = selected,
                OnSelectionChanged = keys =>
                {
                    Props.Log.Add($"report {string.Join("+", keys)}");
                    if (Props.Accept) setSelected(keys);
                },
            };
        }
    }

    [Theory]
    [InlineData(SelectionMode.Single, true)]
    [InlineData(SelectionMode.Multiple, true)]
    [InlineData(SelectionMode.Single, false)]
    [InlineData(SelectionMode.Multiple, false)]
    public void AReport_ReachesTheAppBeforeAQueuedRenderApplies(SelectionMode mode, bool accept)
    {
        var log = new List<string>();
        Action<int>? setOther = null;
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Component<SelectingList, SelectionProps>(new(mode, log, accept, s => setOther = s)));
        var view = host.View<CollectionView>();
        log.Clear();

        setOther!(1); // an unrelated render, queued before the tap
        if (mode == SelectionMode.Single) view.SelectedItem = Cells.Box(view, "b");
        else view.SelectedItems = [Cells.Box(view, "a"), Cells.Box(view, "b")];
        host.Scheduler.Drain();

        var report = mode == SelectionMode.Single ? "report b" : "report a+b";
        Assert.Equal(report, log[0]);
        Assert.Contains(log, l => l.StartsWith("render", StringComparison.Ordinal));
        var final = mode == SelectionMode.Single
            ? ((ItemBox)view.SelectedItem!).Key
            : string.Join("+", view.SelectedItems.OfType<ItemBox>().Select(b => b.Key));
        Assert.Equal(accept ? (mode == SelectionMode.Single ? "b" : "a+b") : "a", final);
        Assert.Single(log, l => l.StartsWith("report", StringComparison.Ordinal));
    }

    [Fact]
    public void AModeChange_AndAWriteWithAnItemsChange_ReportNothing()
    {
        var reports = new List<IReadOnlyList<string>>();
        CollectionViewElement List(string[] items, SelectionMode mode, string? key) =>
            CollectionView(items, k => k, k => Label(k)) with { SelectionMode = mode, SelectedKey = key, OnSelectionChanged = reports.Add };
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(List(["a", "b"], SelectionMode.Single, "b"));
        var view = host.View<CollectionView>();
        Assert.Equal("b", ((ItemBox)view.SelectedItem!).Key);

        host.Render(List(["z", "a", "b", "c"], SelectionMode.Single, "c"));
        Assert.Equal("c", ((ItemBox)view.SelectedItem!).Key);
        host.Render(List(["z", "a", "b", "c"], SelectionMode.None, "c"));
        host.Scheduler.Drain();

        Assert.Equal(SelectionMode.None, view.SelectionMode);
        Assert.Empty(reports);
    }

    [Fact]
    public void MultipleSelection_DeclaredInAnotherOrder_WritesNothing()
    {
        CollectionViewElement List(string[] keys) =>
            CollectionView(new[] { "a", "b", "c" }, k => k, k => Label(k)) with { SelectionMode = SelectionMode.Multiple, SelectedKeys = keys };
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(List(["a", "c"]));
        var view = host.View<CollectionView>();
        var written = view.SelectedItems;

        host.Render(List(["c", "a"]));

        Assert.Same(written, view.SelectedItems);
    }
}

/// <summary>Spec 070 §3.4: the native selection verification, driven through a stub port.</summary>
public class NativeSelectionVerificationTests
{
    private sealed class StubPort : ISelectionPort
    {
        public object? Owner { get; set; } = new();
        public readonly List<(string Name, Action Run)> Queue = [];
        public readonly HashSet<ItemBox> Native = new(ReferenceEqualityComparer.Instance);
        public bool Sticky;
        private int _barriers;

        public bool Barrier(Action completion)
        {
            Queue.Add(($"barrier{++_barriers}", completion));
            return true;
        }

        public IReadOnlyCollection<ItemBox> Selected() => [.. Native];

        public void Select(ItemBox box, bool selected)
        {
            if (Sticky) return;
            if (selected) Native.Add(box);
            else Native.Remove(box);
        }

        /// <summary>What MAUI's deferred SelectItem completion does: selects natively, unchecked.</summary>
        public void QueueMauiSelect(ItemBox box) => Queue.Add(("maui", () => { Native.Clear(); Native.Add(box); }));

        public void RunNext()
        {
            var (_, run) = Queue[0];
            Queue.RemoveAt(0);
            run();
        }

        public void RunAll()
        {
            while (Queue.Count > 0) RunNext();
        }
    }

    private sealed record Props(SelectionMode Mode, List<IReadOnlyList<string>> Reports, Action<Action<string?>, Action<SelectionMode>> Expose, int Tick = 0);

    private sealed class App : Component<Props>
    {
        public override Element Render()
        {
            var (selected, setSelected) = UseState<string?>("a");
            var (mode, setMode) = UseState(Props.Mode);
            Props.Expose(setSelected, setMode);
            return CollectionView(new[] { "a", "b", "c" }, k => k, k => Label(k)) with
            {
                SelectionMode = mode,
                SelectedKey = selected,
                SelectedKeys = selected is null ? null : [selected],
                OnSelectionChanged = keys => { Props.Reports.Add(keys); setSelected(keys.Count > 0 ? keys[0] : null); },
            };
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TestHost, Props> Ticks = new();

    private static (TestHost Host, ReactorCollectionView View, StubPort Port, List<IReadOnlyList<string>> Reports, Action<string?> Select, Action<SelectionMode> Mode)
        Mount(SelectionMode mode = SelectionMode.Single)
    {
        Action<string?>? select = null;
        Action<SelectionMode>? setMode = null;
        var reports = new List<IReadOnlyList<string>>();
        var props = new Props(mode, reports, (s, m) => { select = s; setMode = m; });
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Component<App, Props>(props));
        Ticks.AddOrUpdate(host, props);
        var view = host.View<ReactorCollectionView>();
        var port = new StubPort();
        view.Verifier.Port = port;
        foreach (var box in view.Host.Items.Where(b => b.Key == "a")) port.Native.Add(box);
        return (host, view, port, reports, select!, setMode!);
    }

    // A commit that changes nothing declared: the app re-renders the list.
    private static void Recommit(TestHost host)
    {
        var props = Ticks.TryGetValue(host, out var last) ? last with { Tick = last.Tick + 1 } : throw new InvalidOperationException();
        Ticks.AddOrUpdate(host, props);
        host.Render(Component<App, Props>(props));
    }

    private static string Native(StubPort port) => string.Join(",", port.Native.Select(b => b.Key).Order());

    [Fact]
    public void AnOldMauiCompletion_BetweenTheUsersSelectionAndItsRender_IsRepaired()
    {
        var (host, view, port, reports, select, _) = Mount();
        Recommit(host); // a commit: a barrier is queued
        Assert.Single(port.Queue);
        // MAUI's deferred select of "a" was queued before Reactor's barrier.
        port.Queue.Insert(0, ("maui", () => { port.Native.Clear(); port.Native.Add(Cells.Box(view, "a")); }));

        // The user selects b; the app accepts.
        port.Native.Clear();
        port.Native.Add(Cells.Box(view, "b"));
        view.SelectedItem = Cells.Box(view, "b");
        port.RunNext(); // the old MAUI completion lands: native a
        Assert.Equal("a", Native(port));
        port.RunNext(); // Reactor's barrier: stale (the user's input came after it), reissued
        host.Scheduler.Drain(); // the accepting render writes nothing virtual (already b), but schedules
        Assert.Equal("b", ((ItemBox)view.SelectedItem!).Key);
        port.RunAll();

        Assert.Equal("b", Native(port));
        Assert.Equal(1, view.Verifier.Corrections);
        Assert.Equal(["b"], Assert.Single(reports));
    }

    private static void UserSelects(ReactorCollectionView view, StubPort port, SelectionMode mode, string key)
    {
        var box = Cells.Box(view, key);
        port.Native.Clear();
        port.Native.Add(box);
        if (mode == SelectionMode.Multiple) view.SelectedItems = [box];
        else view.SelectedItem = box;
    }

    // Every barrier completes before the accepting render runs (the order that once ended the
    // verification on a declaration the input had made stale).
    [Theory]
    [InlineData(SelectionMode.Single, true)]
    [InlineData(SelectionMode.Multiple, true)]
    [InlineData(SelectionMode.Single, false)]
    [InlineData(SelectionMode.Multiple, false)]
    public void CompletionsBeforeTheAcceptingRender_NeitherEndNorUndoIt(SelectionMode mode, bool deferredA)
    {
        var (host, view, port, reports, _, _) = Mount(mode);
        Recommit(host); // a barrier is queued
        Assert.Single(port.Queue);
        if (deferredA) port.Queue.Insert(0, ("maui", () => { port.Native.Clear(); port.Native.Add(Cells.Box(view, "a")); }));

        UserSelects(view, port, mode, "b");
        port.RunAll(); // MAUI's A (if any), the stale barrier and its replacement, all before the render
        Assert.Equal(deferredA ? "a" : "b", Native(port)); // the stale declaration (a) was never applied
        Assert.Equal(0, view.Verifier.Corrections);

        host.Scheduler.Drain(); // the accepting render writes nothing virtual (already b)
        port.RunAll();

        Assert.Equal("b", Native(port));
        Assert.Equal(deferredA ? 1 : 0, view.Verifier.Corrections);
        Assert.Equal(["b"], Assert.Single(reports));
    }

    [Theory]
    [InlineData(SelectionMode.Single)]
    [InlineData(SelectionMode.Multiple)]
    public void AnAcceptedTap_CostsOneBarrier(SelectionMode mode)
    {
        var (host, view, port, reports, _, _) = Mount(mode);
        Recommit(host);
        port.RunAll();
        var issued = view.Verifier.Issued;

        UserSelects(view, port, mode, "b");
        host.Scheduler.Drain(); // the accepting render and the restore both commit
        port.RunAll();

        Assert.Equal(issued + 1, view.Verifier.Issued);
        Assert.Equal("b", Native(port));
        Assert.Equal(["b"], Assert.Single(reports));
    }

    [Fact]
    public void AUsersSelection_IsNeverUndone_ByACorrectionComputedBeforeIt()
    {
        var (host, view, port, reports, _, _) = Mount();
        Recommit(host);
        Assert.Single(port.Queue);
        // The user selects b before Reactor's barrier completes; the accepting render hasn't run yet.
        port.Native.Clear();
        port.Native.Add(Cells.Box(view, "b"));
        view.SelectedItem = Cells.Box(view, "b");

        port.RunNext();

        Assert.Equal("b", Native(port));
        Assert.Equal(0, view.Verifier.Corrections);
        host.Scheduler.Drain();
        port.RunAll();
        Assert.Equal("b", Native(port));
        Assert.Equal(["b"], Assert.Single(reports));
    }

    [Fact]
    public void AnObsoleteCompletion_IssuesExactlyOneReplacement_AndComparesNothing()
    {
        var (host, view, port, _, select, _) = Mount();
        select("b");
        host.Scheduler.Drain();
        port.Native.Clear(); // a mismatch the obsolete completion must not repair
        Assert.Single(port.Queue);
        var issued = view.Verifier.Issued;
        view.Verifier.Invalidate();

        port.RunNext();

        Assert.Equal(issued + 1, view.Verifier.Issued);
        Assert.Single(port.Queue);
        Assert.Equal("", Native(port));
        port.RunNext(); // the replacement: the input's commit hasn't run, so nothing compares
        Assert.Equal("", Native(port));
        Recommit(host); // the input's commit verifies
        port.RunAll();
        Assert.Equal("b", Native(port));
    }

    [Fact]
    public void AnOlderCompletion_AfterANewerCommit_AppliesNothing()
    {
        var (host, view, port, _, select, _) = Mount();
        select("b");
        host.Scheduler.Drain();
        port.Native.Clear();
        // A second commit while the first barrier is outstanding: the outstanding one is now stale.
        select("c");
        host.Scheduler.Drain();
        Assert.Single(port.Queue);

        port.RunNext(); // stale: reissued, compares nothing
        Assert.Equal("", Native(port));
        port.RunAll();
        Assert.Equal("c", Native(port));
    }

    [Fact]
    public void AnUnrelatedCommit_AfterAMatchingVerification_IssuesNothing()
    {
        var (host, view, port, _, select, _) = Mount();
        select("b");
        host.Scheduler.Drain();
        port.RunAll();
        Assert.Equal("b", Native(port));
        var issued = view.Verifier.Issued;

        // A parent re-render that writes nothing: each barrier costs UIKit a relayout, so none is issued.
        Recommit(host);
        Recommit(host);

        Assert.Equal(issued, view.Verifier.Issued);
        Assert.Empty(port.Queue);
    }

    [Fact]
    public void ARefusedSelection_IsVerified_EvenWithNoWrite()
    {
        var (host, view, port, reports, _, _) = Mount();
        Recommit(host);
        port.RunAll();
        var issued = view.Verifier.Issued;
        // The user taps c; the app's refusal is modelled by a list whose callback keeps a.
        view.Verifier.Invalidate();
        port.Native.Clear();
        port.Native.Add(Cells.Box(view, "c"));
        Recommit(host); // the restore's commit: the virtual selection is still a, so nothing is written

        Assert.Equal(issued + 1, view.Verifier.Issued);
        port.RunAll();
        Assert.Equal("a", Native(port));
        Assert.Empty(reports);
    }

    [Theory]
    [InlineData(SelectionMode.Single, false)]
    [InlineData(SelectionMode.Multiple, false)]
    [InlineData(SelectionMode.Single, true)]
    public void EnteringNone_DeselectsTheNativeSelection(SelectionMode from, bool pendingDeferred)
    {
        var (host, view, port, reports, _, setMode) = Mount(from);
        // Settle the mount's verification (the port arrived after it), so only entering None schedules.
        Recommit(host);
        port.RunAll();
        Assert.Equal("a", Native(port));
        var issued = view.Verifier.Issued;
        if (pendingDeferred) port.QueueMauiSelect(Cells.Box(view, "b"));

        setMode(SelectionMode.None);
        host.Scheduler.Drain();
        Assert.Equal(issued + 1, view.Verifier.Issued);
        port.RunAll();

        Assert.Equal("", Native(port));
        Assert.Empty(reports);
    }

    [Fact]
    public void AFix_IsVerifiedOnceMore_AndASecondMismatchWarnsAndStops()
    {
        using var warnings = new WarningListener("CollectionView.NativeSelection");
        var (host, view, port, _, select, _) = Mount();
        port.RunAll();
        port.Sticky = true;
        port.Native.Clear();
        select("b");
        host.Scheduler.Drain();

        port.RunAll();

        Assert.Empty(port.Queue);
        Assert.Equal(1, view.Verifier.Corrections);
        Assert.Single(warnings.Warnings);
    }

    [Fact]
    public void UnmountAndAHandlerChange_RetireTheWork()
    {
        var (host, view, port, _, select, _) = Mount();
        select("b");
        host.Scheduler.Drain();
        port.Native.Clear();
        var issued = view.Verifier.Issued;
        port.Owner = new object(); // another handler took over
        port.RunAll();
        Assert.Equal(issued, view.Verifier.Issued);
        Assert.Equal("", Native(port));

        select("c");
        host.Scheduler.Drain();
        Assert.Single(port.Queue);
        host.Root.Unmount();
        port.RunAll();
        Assert.Equal("", Native(port));
    }
}

/// <summary>Spec 070 §3.4: layouts, slots, threshold, grouping and ScrollToKey.</summary>
public class CollectionViewFeatureTests
{
    [Fact]
    public void Arrangements_GiveMauisLayouts_AndAnUnchangedRenderWritesNone()
    {
        CollectionViewElement List(ItemsArrangement arrangement, int span = 1, double spacing = 0, ItemSizingStrategy? sizing = null) =>
            CollectionView(new[] { "a" }, k => k, k => Label(k)) with { Arrangement = arrangement, Span = span, ItemSpacing = spacing, ItemSizingStrategy = sizing };
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(List(ItemsArrangement.VerticalList, spacing: 4));
        var view = host.View<CollectionView>();
        var linear = Assert.IsType<LinearItemsLayout>(view.ItemsLayout);
        Assert.Equal((ItemsLayoutOrientation.Vertical, 4.0), (linear.Orientation, linear.ItemSpacing));

        host.Render(List(ItemsArrangement.VerticalList, spacing: 4));
        Assert.Same(linear, view.ItemsLayout);

        host.Render(List(ItemsArrangement.HorizontalList, spacing: 6, sizing: ItemSizingStrategy.MeasureFirstItem));
        Assert.Equal(ItemsLayoutOrientation.Horizontal, Assert.IsType<LinearItemsLayout>(view.ItemsLayout).Orientation);
        Assert.Equal(ItemSizingStrategy.MeasureFirstItem, view.ItemSizingStrategy);

        host.Render(List(ItemsArrangement.VerticalGrid, span: 3, spacing: 8));
        var grid = Assert.IsType<GridItemsLayout>(view.ItemsLayout);
        Assert.Equal((ItemsLayoutOrientation.Vertical, 3, 8.0, 8.0), (grid.Orientation, grid.Span, grid.VerticalItemSpacing, grid.HorizontalItemSpacing));
        Assert.Equal(ItemSizingStrategy.MeasureAllItems, view.ItemSizingStrategy);

        host.Render(List(ItemsArrangement.HorizontalGrid, span: 2));
        Assert.Equal((ItemsLayoutOrientation.Horizontal, 2), (Assert.IsType<GridItemsLayout>(view.ItemsLayout).Orientation, ((GridItemsLayout)view.ItemsLayout).Span));
    }

    private sealed class SlotProbe : Component<(string Name, List<string> Log)>
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeaderFooterAndEmptyView_AreSetReplacedAndCleared(bool direct)
    {
        var log = new List<string>();
        CollectionViewElement List(bool slots, string suffix = "") =>
            CollectionView(Array.Empty<string>(), k => k, k => Label(k)) with
            {
                Header = slots ? Component<SlotProbe, (string, List<string>)>(("header" + suffix, log)).WithKey("h") : null,
                Footer = slots ? Label("footer" + suffix) : null,
                EmptyView = slots ? Label("empty" + suffix) : null,
            };
        var host = new TestHost(direct ? DirectHandlers.CreateRegistry() : MauiHandlers.CreateRegistry()).Render(List(true));
        var view = host.View<CollectionView>();
        string Text(object? slot) => slot switch
        {
            MauiLabel label => label.Text,
            DirectViewHost wrapper => ((DirectLabel)wrapper.Children[0]).Text,
            _ => "<none>",
        };
        Assert.Equal(("header 0", "footer", "empty"), (Text(view.Header), Text(view.Footer), Text(view.EmptyView)));
        if (direct) Assert.IsType<DirectViewHost>(view.Header);
        SlotProbe.Set["header"](3);
        host.Scheduler.Drain();

        host.Render(List(true, "2"));
        Assert.Equal(("header2 3", "footer2", "empty2"), (Text(view.Header), Text(view.Footer), Text(view.EmptyView)));
        Assert.Empty(log);

        host.Render(List(false));
        Assert.Equal(["cleanup header2"], log);
        Assert.Null(view.Header);
        Assert.Null(view.Footer);
        Assert.Null(view.EmptyView);
    }

    [Fact]
    public void TheThreshold_IsSetAndCleared_AndReportedOnlyWhileMounted()
    {
        var reached = 0;
        CollectionViewElement List(int threshold) =>
            CollectionView(new[] { "a" }, k => k, k => Label(k)) with { RemainingItemsThreshold = threshold, OnRemainingItemsThresholdReached = () => reached++ };
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(List(5));
        var view = host.View<CollectionView>();
        Assert.Equal(5, view.RemainingItemsThreshold);

        view.SendRemainingItemsThresholdReached();
        Assert.Equal(1, reached);
        host.Render(List(-1));
        Assert.Equal(-1, view.RemainingItemsThreshold);

        host.Root.Unmount();
        view.SendRemainingItemsThresholdReached();
        Assert.Equal(1, reached);
    }

    private sealed record Group(string Name, string[] Items);

    private static CollectionViewElement Grouped(IReadOnlyList<Group> groups, List<string>? log = null, string? selected = null) =>
        CollectionView(groups, g => g.Name, g => g.Items, i => i,
            i => log is null ? Label(i) : Component<StateRow, StateRowProps>(new(i, false, log)),
            g => Label($"header {g.Name} {g.Items.Length}"), g => Label($"footer {g.Name}")) with
        {
            SelectionMode = SelectionMode.Single,
            SelectedKey = selected,
        };

    [Fact]
    public void Groups_AreKeyed_AndTheirCellsRenderTheTemplates()
    {
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Grouped([new("x", ["a", "b"]), new("y", ["c"])], selected: "c"));
        var view = host.View<CollectionView>();
        Assert.True(view.IsGrouped);
        var groups = (ObservableCollection<GroupBox>)view.ItemsSource;
        Assert.Equal(["x", "y"], groups.Select(g => g.Key));
        var y = groups[1];
        var a = groups[0][0];
        var header = Cells.Bind(view.GroupHeaderTemplate, y);
        var footer = Cells.Bind(view.GroupFooterTemplate, y);
        Assert.Equal(("header y 1", "footer y"), (Cells.Text(header), Cells.Text(footer)));
        Assert.Equal("c", ((ItemBox)view.SelectedItem!).Key);

        host.Render(Grouped([new("y", ["c", "d"]), new("x", ["a", "b"])], selected: "d"));
        Assert.Same(y, groups[0]);
        Assert.Same(a, groups[1][0]);
        Assert.Equal("header y 2", Cells.Text(header));
        Assert.Equal("d", ((ItemBox)view.SelectedItem!).Key);

        // A header cell rebound to another group remounts.
        header.BindingContext = groups[1];
        Assert.Equal("header x 2", Cells.Text(header));

        Assert.True(view.ScrollToKey("d"));
        Assert.Throws<InvalidOperationException>(() => host.Render(Grouped([new("x", ["a"]), new("y", ["a"])])));
    }

    private sealed class HeaderProbe : Component<string>
    {
        public static readonly Dictionary<string, Action<int>> Set = [];

        public override Element Render()
        {
            var (count, setCount) = UseState(0);
            Set[Props] = setCount;
            return Label($"header {Props} {count}");
        }
    }

    [Theory]
    [InlineData(false, "header y 0")]
    [InlineData(true, "header y 4")]
    public void AGroupHeaderCellRebound_ToAnotherGroup_RemountsUnlessTreesAreReused(bool reuse, string expected)
    {
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(
            CollectionView(new[] { new Group("x", ["a"]), new Group("y", ["b"]) }, g => g.Name, g => g.Items, i => i, i => Label(i),
                g => Component<HeaderProbe, string>(g.Name)) with { ReuseItemTrees = reuse });
        var view = host.View<CollectionView>();
        var groups = (ObservableCollection<GroupBox>)view.ItemsSource;
        var header = Cells.Bind(view.GroupHeaderTemplate, groups[0]);
        HeaderProbe.Set["x"](4);
        host.Scheduler.Drain();
        Assert.Equal("header x 4", Cells.Text(header));

        header.BindingContext = groups[1];

        Assert.Equal(expected, Cells.Text(header));
    }

    [Fact]
    public void GroupedAndFlat_Remount()
    {
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Grouped([new("x", ["a"])]));
        var grouped = host.View<CollectionView>();
        host.Render(CollectionView(new[] { "a" }, k => k, k => Label(k)));
        Assert.NotSame(grouped, host.View<CollectionView>());
        Assert.False(host.View<CollectionView>().IsGrouped);
    }

    [Fact]
    public void ACrossGroupMove_KeepsStateOnlyOnADirectRebindOfTheSameCell()
    {
        var log = new List<string>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Grouped([new("x", ["k", "a"]), new("y", ["b"])], log));
        var view = host.View<CollectionView>();
        var groups = (ObservableCollection<GroupBox>)view.ItemsSource;
        var cellX = Cells.Bind(view.ItemTemplate, groups[0][0]);
        host.Scheduler.Drain();
        StateRow.SetCount["k"](5);
        host.Scheduler.Drain();
        Assert.Equal("k plain 5", Cells.Text(cellX));
        var oldBox = groups[0][0];

        host.Render(Grouped([new("x", ["a"]), new("y", ["k", "b"])], log));
        var newBox = groups[1][0];
        Assert.NotSame(oldBox, newBox);

        // Another cell realizes the moved item: fresh state.
        var cellY = Cells.Bind(view.ItemTemplate, newBox);
        Assert.Equal("k plain 0", Cells.Text(cellY));
        // The same cell rebound directly from the old box to the new one keeps its tree.
        cellX.BindingContext = newBox;
        Assert.Equal("k plain 5", Cells.Text(cellX));
        // Retired in between: fresh.
        cellX.BindingContext = null;
        cellX.BindingContext = newBox;
        Assert.Equal("k plain 0", Cells.Text(cellX));
    }

    [Fact]
    public void ScrollToKey_RequestsTheBoxAndItsGroup()
    {
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Grouped([new("x", ["a"]), new("y", ["b"])]));
        var view = host.View<CollectionView>();
        var requests = new List<ScrollToRequestEventArgs>();
        view.ScrollToRequested += (_, e) => requests.Add(e);
        view.Handler = new StubViewHandler(); // MAUI drops a scroll request on a view without a handler

        Assert.True(view.ScrollToKey("b", ScrollToPosition.Center, animate: false));
        var request = Assert.Single(requests);
        Assert.Same(((ObservableCollection<GroupBox>)view.ItemsSource)[1][0], request.Item);
        Assert.Same(((ObservableCollection<GroupBox>)view.ItemsSource)[1], request.Group);
        Assert.Equal(ScrollToPosition.Center, request.ScrollToPosition);

        Assert.False(view.ScrollToKey("zz"));
        Assert.Single(requests);
        Assert.Throws<InvalidOperationException>(() => new CollectionView().ScrollToKey("a"));
    }
}

/// <summary>Spec 070 §4.16.4 item 2: a template's exception reaches the boundaries above the list.</summary>
public class TemplateErrorTests
{
    public enum Role { Item, GroupHeader, GroupFooter }

    public static TheoryData<string, Role> Cases()
    {
        var data = new TheoryData<string, Role>();
        foreach (var (registry, _) in PropertyMatrixTests.Registries)
            foreach (var role in Enum.GetValues<Role>()) data.Add(registry, role);
        return data;
    }

    private static Element Throw(string what) => throw new InvalidOperationException($"template failed for {what}");

    private static CollectionViewElement List(Role role) => role == Role.Item
        ? CollectionView(new[] { "a", "boom" }, k => k, k => k == "boom" ? Throw(k) : Label(k))
        : CollectionView(new[] { "x", "boom" }, g => g, g => new[] { g + "1" }, i => i, i => Label(i),
            g => role == Role.GroupHeader && g == "boom" ? Throw(g) : Label("header " + g),
            g => role == Role.GroupFooter && g == "boom" ? Throw(g) : Label("footer " + g));

    [Theory]
    [MemberData(nameof(Cases))]
    public void ATemplatesException_WhenThePlatformBindsACell_ReachesTheBoundaryAboveTheList(string registry, Role role)
    {
        var errors = new List<Exception>();
        var host = new TestHost(PropertyMatrixTests.Registries.Single(r => r.Name == registry).Create());
        host.Root.UnhandledException += errors.Add;
        host.Render(ErrorBoundary(List(role), e => Label("fallback: " + e.Message)));
        var view = host.View<CollectionView>();

        // The platform binds the healthy cell, then the failing one; neither throws into the platform.
        var source = (System.Collections.IList)view.ItemsSource;
        var template = role switch { Role.GroupHeader => view.GroupHeaderTemplate, Role.GroupFooter => view.GroupFooterTemplate, _ => view.ItemTemplate };
        var healthy = Cells.Bind(template, source[0]);
        Assert.NotEqual("<none>", Cells.Text(healthy)); // positive control: the template renders
        var failing = Cells.Bind(template, source[1]);
        Assert.Equal("<none>", Cells.Text(failing));

        host.Scheduler.Drain();
        Assert.Empty(errors);
        Assert.Equal("fallback: template failed for boom", host.RootView switch
        {
            MauiLabel label => label.Text,
            DirectLabel direct => direct.Text,
            var other => other?.GetType().Name,
        });
    }
}
