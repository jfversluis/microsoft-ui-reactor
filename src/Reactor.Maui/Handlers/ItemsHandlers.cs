using System.Collections.ObjectModel;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using MauiCarouselView = Microsoft.Maui.Controls.CarouselView;
using MauiCollectionView = Microsoft.Maui.Controls.CollectionView;

namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>One entry of the native items list: the item and the key it is matched by.</summary>
internal sealed record ItemBox(string Key, object? Item);

/// <summary>One group of a grouped list: an observable list of item boxes, as MAUI's grouped sources expect.</summary>
internal sealed class GroupBox(string key, object? group) : ObservableCollection<ItemBox>
{
    public string Key { get; } = key;

    /// <summary>The app's group object, updated in place so a header re-renders without a section reload.</summary>
    public object? Group { get; set; } = group;
}

/// <summary>What a <see cref="ReactorCell"/> renders.</summary>
internal enum CellRole
{
    Item,
    GroupHeader,
    GroupFooter,
}

/// <summary>
/// Shared by the CollectionView and CarouselView handlers: keeps the native items list in step
/// with the element's items, and renders each realized cell through a child tree.
/// </summary>
internal sealed class ItemsHost
{
    private readonly Func<Action<object?>, ReactorRoot> _createRoot;
    private readonly HashSet<ReactorCell> _cells = [];
    private readonly Dictionary<string, (ItemBox Box, GroupBox? Group)> _byKey = [];

    public ItemsHost(Func<Action<object?>, ReactorRoot> createRoot, Action<Exception> reportError)
    {
        _createRoot = createRoot;
        ReportError = reportError;
    }

    /// <summary>
    /// Reports an exception of app code the list calls when the platform binds a cell (its templates), to
    /// the error boundaries above the list, as a cell tree's own render error goes (spec 070 §4.16.4 item 2).
    /// </summary>
    public Action<Exception> ReportError { get; }

    public ObservableCollection<ItemBox> Items { get; } = [];
    public ObservableCollection<GroupBox> Groups { get; } = [];
    public Func<object?, ItemState, Element?> Template { get; set; } = static (_, _) => null;
    public Func<object?, Element?>? GroupHeaderTemplate { get; set; }
    public Func<object?, Element?>? GroupFooterTemplate { get; set; }
    public bool ReuseItemTrees { get; set; }

    /// <summary>The state an item renders with (spec 070 §3.3); set by the list's handler on every apply.</summary>
    public Func<string, ItemState> StateOf { get; set; } = static _ => default;

    /// <summary>Whether item cells keep the platform's selection highlight. Fixed for the list's lifetime.</summary>
    public bool ShowsSelectionHighlight { get; init; } = true;

    public ReactorRoot CreateRoot(Action<object?> setRootNative) => _createRoot(setRootNative);

    public DataTemplate CreateDataTemplate(CellRole role = CellRole.Item) => new(() => new ReactorCell(this, role));

    /// <summary>
    /// True once the list's element is unmounted. The platform can still create or bind cells after
    /// that (a page animating out keeps its content); they must not start new Reactor trees.
    /// </summary>
    public bool Stopped { get; private set; }

    internal void Register(ReactorCell cell)
    {
        if (!Stopped) _cells.Add(cell);
    }

    /// <summary>
    /// Brings <see cref="Items"/> to <paramref name="items"/> with the fewest moves, inserts and
    /// removals, so the native list animates changes and keeps its scroll position. Items whose key
    /// stays but whose value changed are replaced, which re-binds their cell.
    /// </summary>
    public void Sync(IReadOnlyList<object?> items, Func<object?, string> keyOf)
    {
        var keys = KeysOf(items, keyOf, new HashSet<string>(items.Count));
        var replaced = new HashSet<object>(ReferenceEqualityComparer.Instance);
        SyncItems(Items, items, keys, replaced);
        Index();
        RenderKept(replaced);
    }

    /// <summary>
    /// Brings <see cref="Groups"/> to <paramref name="groups"/>, groups by key, then each group's items by
    /// key. Item keys are unique across the list. An item that moves to another group is removed from
    /// one and inserted in the other (a new box), as MAUI's grouped sources have no cross-section move.
    /// </summary>
    public void SyncGroups(IReadOnlyList<ItemGroup> groups, Func<object?, string> keyOf)
    {
        var groupKeys = new string[groups.Count];
        var wantedGroups = new HashSet<string>(groups.Count);
        var allItems = new HashSet<string>();
        var itemKeys = new string[groups.Count][];
        for (var g = 0; g < groups.Count; g++)
        {
            groupKeys[g] = groups[g].Key ?? throw new InvalidOperationException("A group's key must not be null.");
            if (!wantedGroups.Add(groupKeys[g]))
                throw new InvalidOperationException($"Two groups have the key '{groupKeys[g]}'. Group keys must be unique.");
            itemKeys[g] = KeysOf(groups[g].Items, keyOf, allItems);
        }

        for (var i = Groups.Count - 1; i >= 0; i--)
            if (!wantedGroups.Contains(Groups[i].Key)) Groups.RemoveAt(i);
        for (var g = 0; g < groups.Count; g++)
        {
            var at = IndexOf(Groups, groupKeys[g], g);
            if (at < 0) Groups.Insert(g, new GroupBox(groupKeys[g], groups[g].Group));
            else if (at != g) Groups.Move(at, g);
            Groups[g].Group = groups[g].Group;
        }

        var replaced = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (var g = 0; g < groups.Count; g++) SyncItems(Groups[g], groups[g].Items, itemKeys[g], replaced);
        Index();
        RenderKept(replaced);
    }

    private static string[] KeysOf(IReadOnlyList<object?> items, Func<object?, string> keyOf, HashSet<string> seen)
    {
        var keys = new string[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            keys[i] = keyOf(items[i]);
            if (!seen.Add(keys[i]))
                throw new InvalidOperationException($"Two items have the key '{keys[i]}'. Item keys must be unique.");
        }
        return keys;
    }

    private static int IndexOf(ObservableCollection<GroupBox> groups, string key, int from)
    {
        for (var j = from; j < groups.Count; j++)
            if (groups[j].Key == key) return j;
        return -1;
    }

    private static void SyncItems(ObservableCollection<ItemBox> target, IReadOnlyList<object?> items, string[] keys, HashSet<object> replaced)
    {
        var wanted = new HashSet<string>(keys);
        for (var i = target.Count - 1; i >= 0; i--)
            if (!wanted.Contains(target[i].Key)) target.RemoveAt(i);

        for (var i = 0; i < items.Count; i++)
        {
            var key = keys[i];
            if (i < target.Count && target[i].Key == key)
            {
                if (!Equals(target[i].Item, items[i])) Replace(target, i, new ItemBox(key, items[i]), replaced);
                continue;
            }

            var found = -1;
            for (var j = i + 1; j < target.Count; j++)
            {
                if (target[j].Key == key)
                {
                    found = j;
                    break;
                }
            }

            if (found < 0)
            {
                var box = new ItemBox(key, items[i]);
                target.Insert(i, box);
                replaced.Add(box);
            }
            else
            {
                target.Move(found, i);
                if (!Equals(target[i].Item, items[i])) Replace(target, i, new ItemBox(key, items[i]), replaced);
            }
        }
    }

    private static void Replace(ObservableCollection<ItemBox> target, int index, ItemBox box, HashSet<object> replaced)
    {
        target[index] = box;
        replaced.Add(box);
    }

    private void Index()
    {
        _byKey.Clear();
        foreach (var box in Items) _byKey[box.Key] = (box, null);
        foreach (var group in Groups)
            foreach (var box in group) _byKey[box.Key] = (box, group);
    }

    // The templates may read state that changed, so cells that kept their item (and group cells)
    // re-render too.
    private void RenderKept(HashSet<object> replaced)
    {
        foreach (var cell in _cells)
            if (cell.BindingContext is { } bound && !replaced.Contains(bound)) cell.Render();
    }

    /// <summary>The cells the platform created, for reading which one it shows (spec 070 §5.4).</summary>
    internal IEnumerable<ReactorCell> Cells => _cells;

    /// <summary>Re-renders every realized cell with its current item (no change is published).</summary>
    public void RenderRealized() => RenderKept(new HashSet<object>(ReferenceEqualityComparer.Instance));

    /// <summary>Whether syncing <paramref name="items"/> would change the native list: a key added, removed or moved, or an item replaced.</summary>
    public bool Differs(IReadOnlyList<object?> items, Func<object?, string> keyOf)
    {
        if (items.Count != Items.Count) return true;
        for (var i = 0; i < items.Count; i++)
            if (Items[i].Key != keyOf(items[i]) || !Equals(Items[i].Item, items[i])) return true;
        return false;
    }

    public ItemBox? Find(string? key) => key is not null && _byKey.TryGetValue(key, out var found) ? found.Box : null;

    public (ItemBox Box, GroupBox? Group)? FindWithGroup(string? key) =>
        key is not null && _byKey.TryGetValue(key, out var found) ? found : null;

    /// <summary>The native position of a box: its group's index (or 0) and its index in the group or list.</summary>
    public (int Section, int Item)? PositionOf(ItemBox box)
    {
        if (FindWithGroup(box.Key) is not { } found || !ReferenceEquals(found.Box, box)) return null;
        if (found.Group is { } group) return (Groups.IndexOf(group), group.IndexOf(box));
        return (0, Items.IndexOf(box));
    }

    /// <summary>The box at a native position, or <see langword="null"/>.</summary>
    public ItemBox? At(int section, int item)
    {
        if (Groups.Count > 0)
            return section >= 0 && section < Groups.Count && item >= 0 && item < Groups[section].Count ? Groups[section][item] : null;
        return section == 0 && item >= 0 && item < Items.Count ? Items[item] : null;
    }

    public void UnmountAll()
    {
        Stopped = true;
        foreach (var cell in _cells) cell.Unmount();
        _cells.Clear();
    }
}

/// <summary>A cell of a virtualized list. Its content is a child Reactor tree rendering the cell's item or group.</summary>
internal sealed class ReactorCell : ContentView
{
    private readonly ItemsHost _host;
    private readonly CellRole _role;
    private ReactorRoot? _root;

    public ReactorCell(ItemsHost host, CellRole role = CellRole.Item)
    {
        _host = host;
        _role = role;
        // MAUI skips the platform's selection highlight when the item's root view has a Selected visual
        // state that sets a background (iOS TemplatedCell2, Android TemplatedItemViewHolder).
        if (role == CellRole.Item && !host.ShowsSelectionHighlight) VisualStateManager.SetVisualStateGroups(this, SelectionStates());
        host.Register(this);
    }

    internal static VisualStateGroupList SelectionStates()
    {
        var selected = new VisualState { Name = VisualStateManager.CommonStates.Selected };
        selected.Setters.Add(new Setter { Property = VisualElement.BackgroundColorProperty, Value = Colors.Transparent });
        var group = new VisualStateGroup { Name = nameof(VisualStateManager.CommonStates) };
        group.States.Add(new VisualState { Name = VisualStateManager.CommonStates.Normal });
        group.States.Add(selected);
        return [group];
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        Render();
    }

    public void Render()
    {
        if (_host.Stopped) return;
        Element? element;
        string key;
        try
        {
            switch (_role, BindingContext)
            {
                case (CellRole.Item, ItemBox box):
                    element = _host.Template(box.Item, _host.StateOf(box.Key));
                    key = box.Key;
                    break;
                case (CellRole.GroupHeader, GroupBox group):
                    element = _host.GroupHeaderTemplate?.Invoke(group.Group);
                    key = group.Key;
                    break;
                case (CellRole.GroupFooter, GroupBox group):
                    element = _host.GroupFooterTemplate?.Invoke(group.Group);
                    key = group.Key;
                    break;
                default:
                    // Retired: MAUI cleared the cell's item (iOS and Mac Catalyst's TemplatedCell2.Unbind sets
                    // it to null). Its tree unmounts, running cleanups and releasing refs; a later item mounts
                    // a fresh one, with or without ReuseItemTrees (spec 070 §2.4).
                    Unmount();
                    return;
            }
        }
        catch (Exception error)
        {
            // Called while the platform binds the cell: thrown, it would escape into the platform's cell
            // realization rather than reach the boundaries above the list. A recycled cell lets go of the
            // tree of the item it showed before, which it no longer shows.
            Unmount();
            _host.ReportError(error);
            return;
        }
        _root ??= _host.CreateRoot(native => Content = native is null ? null : Props.AsView(native));
        element ??= EmptyElement.Instance;
        // Keyed by item, so a cell recycled for another item remounts instead of keeping that
        // item's component state.
        if (!_host.ReuseItemTrees) element = element with { Key = key };
        _root.Render(element);
    }

    public void Unmount()
    {
        _root?.Unmount();
        _root = null;
    }
}

/// <summary>A child tree rendered into a CollectionView slot (header, footer or empty view).</summary>
internal sealed class SlotRoot(ItemsHost host, Action<View?> assign)
{
    private ReactorRoot? _root;

    public void Render(Element? element)
    {
        if (element is null)
        {
            if (_root is null) return;
            _root.Unmount();
            _root = null;
            assign(null);
            return;
        }
        if (host.Stopped) return;
        _root ??= host.CreateRoot(native => assign(native is null ? null : Props.AsView(native)));
        _root.Render(element);
    }

    public void Unmount()
    {
        _root?.Unmount();
        _root = null;
    }
}

/// <summary>The declared selection, by spec 070 §3.3's rules, shared by the item state and the selection write.</summary>
internal static class SelectionRules
{
    /// <summary>Whether a key is selected by the declaration, existing or not.</summary>
    public static Func<string, bool> Declared(CollectionViewElement element)
    {
        switch (element.SelectionMode)
        {
            case SelectionMode.Single when element.SelectedKey is { } selected:
                return key => key == selected;
            case SelectionMode.Multiple when element.SelectedKeys is { Count: > 0 } keys:
                var set = new HashSet<string>(keys);
                return set.Contains;
            default:
                return static _ => false;
        }
    }

    /// <summary>The boxes the declaration selects that exist, in declared order.</summary>
    public static List<ItemBox> Effective(CollectionViewElement element, ItemsHost host)
    {
        var boxes = new List<ItemBox>();
        switch (element.SelectionMode)
        {
            case SelectionMode.Single:
                if (host.Find(element.SelectedKey) is { } box) boxes.Add(box);
                break;
            case SelectionMode.Multiple:
                foreach (var key in element.SelectedKeys ?? [])
                    if (host.Find(key) is { } found && !boxes.Contains(found)) boxes.Add(found);
                break;
        }
        return boxes;
    }
}

internal sealed class ReactorCollectionView : MauiCollectionView
{
    public ItemsHost Host = null!;
    public SlotRoot HeaderSlot = null!, FooterSlot = null!, EmptySlot = null!;
    public NativeSelectionVerifier Verifier = null!;
}

internal sealed class CollectionViewHandler : ViewHandler<CollectionViewElement, ReactorCollectionView>
{
    protected override ReactorCollectionView Create(HandlerContext<CollectionViewElement> context, CollectionViewElement element)
    {
        var view = new ReactorCollectionView();
        var host = new ItemsHost(context.CreateChildRoot, context.ReportError) { ShowsSelectionHighlight = element.ShowsSelectionHighlight };
        view.Host = host;
        view.HeaderSlot = new SlotRoot(host, header => view.Header = header);
        view.FooterSlot = new SlotRoot(host, footer => view.Footer = footer);
        view.EmptySlot = new SlotRoot(host, empty => view.EmptyView = empty);
        view.IsGrouped = element.Groups is not null;
        view.ItemsSource = element.Groups is null ? host.Items : host.Groups;
        view.ItemTemplate = host.CreateDataTemplate();
        var liveContext = context;
        view.Verifier = new NativeSelectionVerifier(view, () => liveContext.Live, write => liveContext.WriteSuppressed(view, write));

        view.SelectionChanged += (_, e) =>
        {
            if (context.IsEchoing(view)) return;
            // Captured from the notification's arguments, never read back later (spec 070 §3.4). User
            // input makes any pending native correction stale.
            var keys = e.CurrentSelection.OfType<ItemBox>().Select(static b => b.Key).ToArray();
            view.Verifier.Invalidate();
            context.Live?.OnSelectionChanged?.Invoke(keys);
            context.RestoreAfterEvent();
        };
        view.RemainingItemsThresholdReached += (_, _) => context.Live?.OnRemainingItemsThresholdReached?.Invoke();
        return view;
    }

    public override bool CanUpdate(CollectionViewElement oldElement, CollectionViewElement newElement) =>
        (oldElement.Groups is null) == (newElement.Groups is null)
        && oldElement.ShowsSelectionHighlight == newElement.ShowsSelectionHighlight;

    protected override void Apply(HandlerContext<CollectionViewElement> context, ReactorCollectionView view, CollectionViewElement? previous, CollectionViewElement next)
    {
        var host = view.Host;
        host.Template = next.ItemTemplate;
        host.GroupHeaderTemplate = next.GroupHeaderTemplate;
        host.GroupFooterTemplate = next.GroupFooterTemplate;
        host.ReuseItemTrees = next.ReuseItemTrees;
        var declared = SelectionRules.Declared(next);
        host.StateOf = key => new ItemState(declared(key));

        if (previous is null || previous.Arrangement != next.Arrangement || previous.Span != next.Span || previous.ItemSpacing != next.ItemSpacing)
            view.ItemsLayout = CreateLayout(next);
        if (view.RemainingItemsThreshold != next.RemainingItemsThreshold) view.RemainingItemsThreshold = next.RemainingItemsThreshold;
        Props.Set(view, MauiCollectionView.ItemSizingStrategyProperty, previous?.ItemSizingStrategy, next.ItemSizingStrategy);
        if (next.Groups is not null)
        {
            if ((previous?.GroupHeaderTemplate is null) != (next.GroupHeaderTemplate is null))
                view.GroupHeaderTemplate = next.GroupHeaderTemplate is null ? null : host.CreateDataTemplate(CellRole.GroupHeader);
            if ((previous?.GroupFooterTemplate is null) != (next.GroupFooterTemplate is null))
                view.GroupFooterTemplate = next.GroupFooterTemplate is null ? null : host.CreateDataTemplate(CellRole.GroupFooter);
        }

        var wroteSelection = false;
        context.WriteSuppressed(view, () =>
        {
            // MAUI raises SelectionChanged when a mode change changes the effective selection.
            if (view.SelectionMode != next.SelectionMode)
            {
                view.SelectionMode = next.SelectionMode;
                wroteSelection = true;
            }
            // Restoring a controlled value re-applies the same element; the items did not change then.
            if (!ReferenceEquals(previous, next))
            {
                if (next.Groups is { } groups) host.SyncGroups(groups, next.ItemKey);
                else host.Sync(next.Items, next.ItemKey);
            }
            wroteSelection |= ApplySelection(view, next);
        });

        view.HeaderSlot.Render(next.Header);
        view.FooterSlot.Render(next.Footer);
        view.EmptySlot.Render(next.EmptyView);

        // A commit that wrote the selection or its mode, or one after user input, verifies the native
        // selection behind MAUI's pending batches (spec 070 §3.4).
        view.Verifier.Schedule(wroteSelection);
    }

    public override void Unmount(ReactorCollectionView control)
    {
        control.Verifier.Retire();
        control.Host.UnmountAll();
        control.HeaderSlot.Unmount();
        control.FooterSlot.Unmount();
        control.EmptySlot.Unmount();
    }

    /// <summary>Writes the declared selection; returns whether anything was written.</summary>
    private static bool ApplySelection(ReactorCollectionView view, CollectionViewElement element)
    {
        var wanted = SelectionRules.Effective(element, view.Host);
        switch (element.SelectionMode)
        {
            case SelectionMode.Single:
            {
                var box = wanted.Count > 0 ? wanted[0] : null;
                if (ReferenceEquals(view.SelectedItem, box)) return false;
                view.SelectedItem = box;
                return true;
            }
            case SelectionMode.Multiple:
            {
                // Compared as sets: a native order that differs from the declared one isn't a change.
                var current = view.SelectedItems?.OfType<ItemBox>() ?? [];
                if (new HashSet<ItemBox>(current, ReferenceEqualityComparer.Instance).SetEquals(wanted)) return false;
                view.SelectedItems = [.. wanted];
                return true;
            }
            default:
                return false;
        }
    }

    private static IItemsLayout CreateLayout(CollectionViewElement element) => element.Arrangement switch
    {
        ItemsArrangement.HorizontalList => new LinearItemsLayout(ItemsLayoutOrientation.Horizontal) { ItemSpacing = element.ItemSpacing },
        ItemsArrangement.VerticalGrid => new GridItemsLayout(element.Span, ItemsLayoutOrientation.Vertical)
        {
            VerticalItemSpacing = element.ItemSpacing,
            HorizontalItemSpacing = element.ItemSpacing,
        },
        ItemsArrangement.HorizontalGrid => new GridItemsLayout(element.Span, ItemsLayoutOrientation.Horizontal)
        {
            VerticalItemSpacing = element.ItemSpacing,
            HorizontalItemSpacing = element.ItemSpacing,
        },
        _ => new LinearItemsLayout(ItemsLayoutOrientation.Vertical) { ItemSpacing = element.ItemSpacing },
    };
}

internal sealed class ReactorCarouselView : MauiCarouselView
{
    public ItemsHost Host = null!;
    public CarouselPositionController Controller = null!;

    /// <summary>
    /// No platform port exists here (Windows until 4g): the position falls back to writing
    /// <c>Position</c> and reporting <c>PositionChanged</c>, without the drag safety of spec 070 §5.4.
    /// </summary>
    internal bool Portless { get; set; }

    /// <summary>The element whose items and layout MAUI has (spec 070 §5.4 rule 3: published only at rest).</summary>
    public CarouselViewElement? Published;
}

/// <summary>
/// The carousel (spec 070 §5). Its position is controlled by <see cref="CarouselPositionController"/>:
/// MAUI's <c>Position</c> is written once, before the handler exists; later declarations scroll there
/// at rest, and the item the platform rests on is reported when it differs.
/// </summary>
internal sealed class CarouselViewHandler : ViewHandler<CarouselViewElement, ReactorCarouselView>
{
    protected override ReactorCarouselView Create(HandlerContext<CarouselViewElement> context, CarouselViewElement element)
    {
        var view = new ReactorCarouselView();
        var host = new ItemsHost(context.CreateChildRoot, context.ReportError);
        view.Host = host;
        view.ItemsSource = host.Items;
        view.ItemTemplate = host.CreateDataTemplate();
        var live = context;
        view.Controller = new CarouselPositionController(
            declared: () => Clamp(live.Live?.Position, host.Items.Count),
            animated: () => live.Live?.IsScrollAnimated ?? true,
            writeSuppressed: write => live.WriteSuppressed(view, write),
            report: (shown, seq) =>
            {
                // The shown index is in the published items; the app's are the element's (they differ only
                // while a transaction waits): mapped by key. An item that's gone isn't reported.
                var key = shown < host.Items.Count ? host.Items[shown].Key : null;
                live.PostReport(() =>
                {
                    view.Controller.ReportRan(seq);
                    if (live.Live is not { } current || key is null) return;
                    var index = IndexOfKey(current, key);
                    if (index >= 0) current.OnPositionChanged?.Invoke(index);
                }, restore: true);
            },
            publish: () =>
            {
                if (live.Live is { } current) Publish(live, view, current);
            },
            mounted: Clamp(element.Position, element.Items.Count) ?? 0);
        view.Portless = !CarouselPorts.IsSupported;
        // MAUI's events are signals to look at the platform, never the reported value (rule 4).
        view.PositionChanged += (_, _) =>
        {
            if (Fallback(view)) ReportFallback(live, view);
            else view.Controller.OnTrigger();
        };
        view.CurrentItemChanged += (_, _) => view.Controller.OnTrigger();
        view.Scrolled += (_, _) => view.Controller.OnTrigger();
        view.ScrollToRequested += (_, _) => view.Controller.OnScrollToRequested();
        view.HandlerChanged += (_, _) =>
        {
            var port = view.Handler is null ? null : CarouselPorts.Create(view);
            if (port is not null) view.Portless = false;
            view.Controller.Port = port;
        };
        return view;
    }

    // Without a port (Windows until 4g), MAUI's own Position is the value: written on change, reported
    // from PositionChanged outside the echo scope, restored after the callback.
    private static bool Fallback(ReactorCarouselView view) => view.Portless && view.Controller.Port is null;

    private static void ReportFallback(HandlerContext<CarouselViewElement> context, ReactorCarouselView view)
    {
        if (context.IsEchoing(view)) return;
        var position = view.Position;
        var key = position >= 0 && position < view.Host.Items.Count ? view.Host.Items[position].Key : null;
        context.PostReport(() =>
        {
            if (context.Live is not { } current || key is null) return;
            var index = IndexOfKey(current, key);
            if (index >= 0) current.OnPositionChanged?.Invoke(index);
        }, restore: true);
    }

    private static bool s_fallbackLogged;

    private static void ApplyFallback(HandlerContext<CarouselViewElement> context, ReactorCarouselView view, CarouselViewElement next)
    {
        if (!s_fallbackLogged)
        {
            s_fallbackLogged = true;
            Microsoft.UI.Reactor.Core.Diagnostics.DiagnosticLog.Warning(Microsoft.UI.Reactor.Core.Diagnostics.LogCategory.Reactor, "CarouselView.Position",
                "This platform has no carousel port yet: the position is written and reported as MAUI reports it, without the drag safety of iOS and Android (that arrives with the Windows port, phase 4g).");
        }
        Publish(context, view, next);
        if (Clamp(next.Position, next.Items.Count) is { } position && view.Position != position)
            context.WriteSuppressed(view, () => view.Position = position);
    }

    private static int IndexOfKey(CarouselViewElement element, string key)
    {
        for (var i = 0; i < element.Items.Count; i++)
            if (element.ItemKey(element.Items[i]) == key) return i;
        return -1;
    }

    /// <summary>The declaration clamped to the items: <see langword="null"/> for none.</summary>
    internal static int? Clamp(int? position, int count) =>
        position is not { } p || count == 0 ? null : Math.Clamp(p, 0, count - 1);

    protected override void Apply(HandlerContext<CarouselViewElement> context, ReactorCarouselView view, CarouselViewElement? previous, CarouselViewElement next)
    {
        var host = view.Host;
        host.Template = next.ItemTemplate;
        var currentKey = Clamp(next.Position, next.Items.Count) is { } at ? next.ItemKey(next.Items[at]) : null;
        host.StateOf = key => new ItemState(false) { IsCurrent = key == currentKey };

        if (previous is null)
        {
            // Mount, before the handler: the platform starts at the declared position (§5.1).
            Publish(context, view, next);
            context.WriteSuppressed(view, () => view.Position = Clamp(next.Position, next.Items.Count) ?? 0);
            return;
        }
        if (Fallback(view))
        {
            ApplyFallback(context, view, next);
            return;
        }
        if (ReferenceEquals(previous, next))
        {
            // A restore after a report: the answer's commit.
            view.Controller.Commit(transaction: false);
            return;
        }
        var transaction = Differs(view, next);
        // Without a transaction, syncing only re-renders cells; with one, cells re-render now and the
        // items are published at rest (rule 3).
        if (!transaction) context.WriteSuppressed(view, () => host.Sync(next.Items, next.ItemKey));
        else host.RenderRealized();
        view.Controller.Commit(transaction);
    }

    private static bool Differs(ReactorCarouselView view, CarouselViewElement next)
    {
        var published = view.Published!;
        return published.Loop != next.Loop
            || !Equals(published.PeekAreaInsets, next.PeekAreaInsets)
            || published.ItemSpacing != next.ItemSpacing
            || view.Host.Differs(next.Items, next.ItemKey);
    }

    /// <summary>Gives MAUI the element's items and layout, in the echo scope.</summary>
    private static void Publish(HandlerContext<CarouselViewElement> context, ReactorCarouselView view, CarouselViewElement element)
    {
        var previous = view.Published;
        context.WriteSuppressed(view, () =>
        {
            if (view.Loop != element.Loop) view.Loop = element.Loop;
            Props.Set(view, MauiCarouselView.PeekAreaInsetsProperty, previous?.PeekAreaInsets, element.PeekAreaInsets);
            // The default carousel layout is a shared static instance, so spacing gets a layout of its own.
            if (previous is null || previous.ItemSpacing != element.ItemSpacing)
            {
                view.ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Horizontal)
                {
                    SnapPointsType = SnapPointsType.MandatorySingle,
                    SnapPointsAlignment = SnapPointsAlignment.Center,
                    ItemSpacing = element.ItemSpacing,
                };
            }
            view.Host.Sync(element.Items, element.ItemKey);
        });
        view.Published = element;
    }

    public override void Unmount(ReactorCarouselView control)
    {
        control.Controller.Retire();
        control.Host.UnmountAll();
    }
}
