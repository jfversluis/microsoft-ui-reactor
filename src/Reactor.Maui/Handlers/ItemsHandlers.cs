using System.Collections.ObjectModel;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using MauiCarouselView = Microsoft.Maui.Controls.CarouselView;
using MauiCollectionView = Microsoft.Maui.Controls.CollectionView;

namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>One entry of the native items list: the item and the key it is matched by.</summary>
internal sealed record ItemBox(string Key, object? Item);

/// <summary>
/// Shared by the CollectionView and CarouselView handlers: keeps the native items list in step
/// with the element's items, and renders each realized cell through a child tree.
/// </summary>
internal sealed class ItemsHost
{
    private readonly Func<Action<object?>, ReactorRoot> _createRoot;
    private readonly HashSet<ReactorCell> _cells = [];

    public ItemsHost(Func<Action<object?>, ReactorRoot> createRoot)
    {
        _createRoot = createRoot;
    }

    public ObservableCollection<ItemBox> Items { get; } = [];
    public Func<object?, Element?> Template { get; set; } = static _ => null;
    public bool ReuseItemTrees { get; set; }

    public ReactorRoot CreateRoot(Action<object?> setRootNative) => _createRoot(setRootNative);

    public DataTemplate CreateDataTemplate() => new(() => new ReactorCell(this));

    internal void Register(ReactorCell cell) => _cells.Add(cell);

    /// <summary>
    /// Brings <see cref="Items"/> to <paramref name="items"/> with the fewest moves, inserts and
    /// removals, so the native list animates changes and keeps its scroll position. Items whose key
    /// stays but whose value changed are replaced, which re-binds their cell.
    /// </summary>
    public void Sync(IReadOnlyList<object?> items, Func<object?, string> keyOf)
    {
        var keys = new string[items.Count];
        var wanted = new HashSet<string>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            keys[i] = keyOf(items[i]);
            if (!wanted.Add(keys[i]))
                throw new InvalidOperationException($"Two items have the key '{keys[i]}'. Item keys must be unique.");
        }

        for (var i = Items.Count - 1; i >= 0; i--)
            if (!wanted.Contains(Items[i].Key)) Items.RemoveAt(i);

        var replaced = new HashSet<ItemBox>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < items.Count; i++)
        {
            var key = keys[i];
            if (i < Items.Count && Items[i].Key == key)
            {
                if (!Equals(Items[i].Item, items[i])) Replace(i, new ItemBox(key, items[i]), replaced);
                continue;
            }

            var found = -1;
            for (var j = i + 1; j < Items.Count; j++)
            {
                if (Items[j].Key == key)
                {
                    found = j;
                    break;
                }
            }

            if (found < 0)
            {
                var box = new ItemBox(key, items[i]);
                Items.Insert(i, box);
                replaced.Add(box);
            }
            else
            {
                Items.Move(found, i);
                if (!Equals(Items[i].Item, items[i])) Replace(i, new ItemBox(key, items[i]), replaced);
            }
        }

        // The template may read state that changed, so cells that kept their item re-render too.
        foreach (var cell in _cells)
            if (cell.BindingContext is ItemBox box && !replaced.Contains(box)) cell.Render();
    }

    private void Replace(int index, ItemBox box, HashSet<ItemBox> replaced)
    {
        Items[index] = box;
        replaced.Add(box);
    }

    public ItemBox? Find(string? key)
    {
        if (key is null) return null;
        foreach (var box in Items)
            if (box.Key == key) return box;
        return null;
    }

    public void UnmountAll()
    {
        foreach (var cell in _cells) cell.Unmount();
        _cells.Clear();
    }
}

/// <summary>A cell of a virtualized list. Its content is a child Reactor tree rendering the cell's item.</summary>
internal sealed class ReactorCell : ContentView
{
    private readonly ItemsHost _host;
    private ReactorRoot? _root;

    public ReactorCell(ItemsHost host)
    {
        _host = host;
        host.Register(this);
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        Render();
    }

    public void Render()
    {
        if (BindingContext is not ItemBox box) return;
        _root ??= _host.CreateRoot(native => Content = native is null ? null : Props.AsView(native));
        var element = _host.Template(box.Item) ?? EmptyElement.Instance;
        // Keyed by item, so a cell recycled for another item remounts instead of keeping that
        // item's component state.
        if (!_host.ReuseItemTrees) element = element with { Key = box.Key };
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
        _root ??= host.CreateRoot(native => assign(native is null ? null : Props.AsView(native)));
        _root.Render(element);
    }

    public void Unmount()
    {
        _root?.Unmount();
        _root = null;
    }
}

internal sealed class ReactorCollectionView : MauiCollectionView
{
    public ItemsHost Host = null!;
    public SlotRoot HeaderSlot = null!, FooterSlot = null!, EmptySlot = null!;
}

internal sealed class CollectionViewHandler : ViewHandler<CollectionViewElement, ReactorCollectionView>
{
    protected override ReactorCollectionView Create(HandlerContext<CollectionViewElement> context, CollectionViewElement element)
    {
        var view = new ReactorCollectionView();
        var host = new ItemsHost(context.CreateChildRoot);
        view.Host = host;
        view.HeaderSlot = new SlotRoot(host, header => view.Header = header);
        view.FooterSlot = new SlotRoot(host, footer => view.Footer = footer);
        view.EmptySlot = new SlotRoot(host, empty => view.EmptyView = empty);
        view.ItemsSource = host.Items;
        view.ItemTemplate = host.CreateDataTemplate();

        view.SelectionChanged += (_, e) =>
        {
            if (context.IsEchoing(view)) return;
            var keys = e.CurrentSelection.OfType<ItemBox>().Select(static b => b.Key).ToArray();
            context.Live?.OnSelectionChanged?.Invoke(keys);
            context.RestoreAfterEvent();
        };
        view.RemainingItemsThresholdReached += (_, _) => context.Live?.OnRemainingItemsThresholdReached?.Invoke();
        return view;
    }

    protected override void Apply(HandlerContext<CollectionViewElement> context, ReactorCollectionView view, CollectionViewElement? previous, CollectionViewElement next)
    {
        var host = view.Host;
        host.Template = next.ItemTemplate;
        host.ReuseItemTrees = next.ReuseItemTrees;

        if (previous is null || previous.Arrangement != next.Arrangement || previous.Span != next.Span || previous.ItemSpacing != next.ItemSpacing)
            view.ItemsLayout = CreateLayout(next);
        if (view.SelectionMode != next.SelectionMode) view.SelectionMode = next.SelectionMode;
        if (view.RemainingItemsThreshold != next.RemainingItemsThreshold) view.RemainingItemsThreshold = next.RemainingItemsThreshold;
        Props.Set(view, MauiCollectionView.ItemSizingStrategyProperty, previous?.ItemSizingStrategy, next.ItemSizingStrategy);

        context.WriteSuppressed(view, () =>
        {
            // Restoring a controlled value re-applies the same element; the items did not change then.
            if (!ReferenceEquals(previous, next)) host.Sync(next.Items, next.ItemKey);
            ApplySelection(view, next);
        });

        view.HeaderSlot.Render(next.Header);
        view.FooterSlot.Render(next.Footer);
        view.EmptySlot.Render(next.EmptyView);
    }

    public override void Unmount(ReactorCollectionView control)
    {
        control.Host.UnmountAll();
        control.HeaderSlot.Unmount();
        control.FooterSlot.Unmount();
        control.EmptySlot.Unmount();
    }

    private static void ApplySelection(ReactorCollectionView view, CollectionViewElement element)
    {
        switch (element.SelectionMode)
        {
            case SelectionMode.Single:
            {
                var wanted = view.Host.Find(element.SelectedKey);
                if (!ReferenceEquals(view.SelectedItem, wanted)) view.SelectedItem = wanted;
                break;
            }
            case SelectionMode.Multiple:
            {
                var wanted = (element.SelectedKeys ?? []).Select(view.Host.Find).OfType<ItemBox>().ToList();
                var current = view.SelectedItems?.OfType<ItemBox>().ToList() ?? [];
                if (!wanted.SequenceEqual(current)) view.SelectedItems = [.. wanted];
                break;
            }
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
}

internal sealed class CarouselViewHandler : ViewHandler<CarouselViewElement, ReactorCarouselView>
{
    protected override ReactorCarouselView Create(HandlerContext<CarouselViewElement> context, CarouselViewElement element)
    {
        var view = new ReactorCarouselView();
        var host = new ItemsHost(context.CreateChildRoot);
        view.Host = host;
        view.ItemsSource = host.Items;
        view.ItemTemplate = host.CreateDataTemplate();
        view.PositionChanged += (_, e) =>
        {
            if (context.IsEchoing(view)) return;
            context.Live?.OnPositionChanged?.Invoke(e.CurrentPosition);
            context.RestoreAfterEvent();
        };
        return view;
    }

    protected override void Apply(HandlerContext<CarouselViewElement> context, ReactorCarouselView view, CarouselViewElement? previous, CarouselViewElement next)
    {
        view.Host.Template = next.ItemTemplate;
        if (view.Loop != next.Loop) view.Loop = next.Loop;
        Props.Set(view, MauiCarouselView.PeekAreaInsetsProperty, previous?.PeekAreaInsets, next.PeekAreaInsets);
        // The default carousel layout is a shared static instance, so spacing gets a layout of its own.
        if (previous is null || previous.ItemSpacing != next.ItemSpacing)
        {
            view.ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Horizontal)
            {
                SnapPointsType = SnapPointsType.MandatorySingle,
                SnapPointsAlignment = SnapPointsAlignment.Center,
                ItemSpacing = next.ItemSpacing,
            };
        }

        context.WriteSuppressed(view, () =>
        {
            if (!ReferenceEquals(previous, next)) view.Host.Sync(next.Items, next.ItemKey);
            if (view.Position != next.Position) view.Position = next.Position;
        });
    }

    public override void Unmount(ReactorCarouselView control) => control.Host.UnmountAll();
}
