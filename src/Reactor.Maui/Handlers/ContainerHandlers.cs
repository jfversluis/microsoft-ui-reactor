using Microsoft.UI.Reactor.Hosting;
using MauiAbsoluteLayout = Microsoft.Maui.Controls.AbsoluteLayout;
using MauiFlexLayout = Microsoft.Maui.Controls.FlexLayout;
using MauiGrid = Microsoft.Maui.Controls.Grid;
using MauiRefreshView = Microsoft.Maui.Controls.RefreshView;
using MauiScrollView = Microsoft.Maui.Controls.ScrollView;
using MauiSwipeView = Microsoft.Maui.Controls.SwipeView;

namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>Layouts host their children in their own <see cref="Layout"/> list.</summary>
internal abstract class LayoutHandler<TElement, TLayout> : ViewHandler<TElement, TLayout>
    where TElement : LayoutElement
    where TLayout : Layout, new()
{
    protected sealed override TLayout Create(HandlerContext<TElement> context, TElement element) => new();

    protected sealed override void Apply(HandlerContext<TElement> context, TLayout view, TElement? previous, TElement next)
    {
        Props.Set(view, Layout.PaddingProperty, previous?.Padding, next.Padding);
        ApplyLayout(view, previous, next);
    }

    protected abstract void ApplyLayout(TLayout view, TElement? previous, TElement next);

    public sealed override IChildrenStrategy<TElement, TLayout> Children { get; } = new LayoutChildren();

    // Direct views added to a Controls layout take it as the logical parent of the Controls views
    // inside them, so styles and resources still reach those.
    private sealed class LayoutChildren : IChildrenStrategy<TElement, TLayout>
    {
        private readonly IChildrenStrategy<TElement, TLayout> _list =
            ChildrenStrategy.List<TElement, TLayout, IView>(static e => e.Children, static layout => layout);

        public IReadOnlyList<Element?> GetChildren(TElement element) => element.Children;

        public void Sync(TLayout control, IReadOnlyList<object> natives)
        {
            _list.Sync(control, natives);
            foreach (var native in natives)
                if (native is Direct.DirectView direct) direct.AttachToControlsParent(control);
        }
    }
}

internal sealed class VStackHandler : LayoutHandler<VStackElement, VerticalStackLayout>
{
    protected override void ApplyLayout(VerticalStackLayout view, VStackElement? previous, VStackElement next)
    {
        if (view.Spacing != next.Spacing) view.Spacing = next.Spacing;
    }
}

internal sealed class HStackHandler : LayoutHandler<HStackElement, HorizontalStackLayout>
{
    protected override void ApplyLayout(HorizontalStackLayout view, HStackElement? previous, HStackElement next)
    {
        if (view.Spacing != next.Spacing) view.Spacing = next.Spacing;
    }
}

internal sealed class GridHandler : LayoutHandler<GridElement, MauiGrid>
{
    protected override void ApplyLayout(MauiGrid view, GridElement? previous, GridElement next)
    {
        if (previous?.Rows != next.Rows)
        {
            view.RowDefinitions.Clear();
            foreach (var length in GridLengths.Parse(next.Rows)) view.RowDefinitions.Add(new RowDefinition(length));
        }
        if (previous?.Columns != next.Columns)
        {
            view.ColumnDefinitions.Clear();
            foreach (var length in GridLengths.Parse(next.Columns)) view.ColumnDefinitions.Add(new ColumnDefinition(length));
        }
        if (view.RowSpacing != next.RowSpacing) view.RowSpacing = next.RowSpacing;
        if (view.ColumnSpacing != next.ColumnSpacing) view.ColumnSpacing = next.ColumnSpacing;
    }
}

internal sealed class FlexLayoutHandler : LayoutHandler<FlexLayoutElement, MauiFlexLayout>
{
    protected override void ApplyLayout(MauiFlexLayout view, FlexLayoutElement? previous, FlexLayoutElement next)
    {
        Props.Set(view, MauiFlexLayout.DirectionProperty, previous?.Direction, next.Direction);
        Props.Set(view, MauiFlexLayout.WrapProperty, previous?.Wrap, next.Wrap);
        Props.Set(view, MauiFlexLayout.JustifyContentProperty, previous?.JustifyContent, next.JustifyContent);
        Props.Set(view, MauiFlexLayout.AlignItemsProperty, previous?.AlignItems, next.AlignItems);
        Props.Set(view, MauiFlexLayout.AlignContentProperty, previous?.AlignContent, next.AlignContent);
    }
}

internal sealed class AbsoluteLayoutHandler : LayoutHandler<AbsoluteLayoutElement, MauiAbsoluteLayout>
{
    protected override void ApplyLayout(MauiAbsoluteLayout view, AbsoluteLayoutElement? previous, AbsoluteLayoutElement next) { }
}

internal sealed class ScrollViewHandler : ViewHandler<ScrollViewElement, ReactorScrollView>
{
    protected override ReactorScrollView Create(HandlerContext<ScrollViewElement> context, ScrollViewElement element)
    {
        var view = new ReactorScrollView();
        view.Scrolled += (_, e) => context.Live?.OnScrolled?.Invoke(e.ScrollX, e.ScrollY);
        return view;
    }

    protected override void Apply(HandlerContext<ScrollViewElement> context, ReactorScrollView view, ScrollViewElement? previous, ScrollViewElement next)
    {
        Props.Set(view, MauiScrollView.OrientationProperty, previous?.Orientation, next.Orientation);
        Props.Set(view, MauiScrollView.VerticalScrollBarVisibilityProperty, previous?.VerticalScrollBarVisibility, next.VerticalScrollBarVisibility);
        Props.Set(view, MauiScrollView.HorizontalScrollBarVisibilityProperty, previous?.HorizontalScrollBarVisibility, next.HorizontalScrollBarVisibility);
    }

    public override void Unmount(ReactorScrollView control) => control.Bridge.Cancel();

    public override IChildrenStrategy<ScrollViewElement, ReactorScrollView> Children { get; } =
        ChildrenStrategy.Single<ScrollViewElement, ReactorScrollView, object>(
            e => e.Content, (scrollView, content) => scrollView.Content = content is null ? null : Props.AsView(content));
}

internal sealed class RefreshViewHandler : ViewHandler<RefreshViewElement, MauiRefreshView>
{
    protected override MauiRefreshView Create(HandlerContext<RefreshViewElement> context, RefreshViewElement element)
    {
        var view = new MauiRefreshView();
        view.Refreshing += (_, _) =>
        {
            if (context.IsEchoing(view)) return;
            context.Live?.OnRefresh?.Invoke();
            context.RestoreAfterEvent();
        };
        return view;
    }

    protected override void Apply(HandlerContext<RefreshViewElement> context, MauiRefreshView view, RefreshViewElement? previous, RefreshViewElement next)
    {
        Props.SetRef(view, MauiRefreshView.RefreshColorProperty, previous?.RefreshColor, next.RefreshColor);
        if (view.IsRefreshing != next.IsRefreshing) context.WriteSuppressed(view, () => view.IsRefreshing = next.IsRefreshing);
    }

    public override IChildrenStrategy<RefreshViewElement, MauiRefreshView> Children { get; } =
        ChildrenStrategy.Single<RefreshViewElement, MauiRefreshView, object>(
            e => e.Content, (refreshView, content) => refreshView.Content = content is null ? null : Props.AsView(content));
}

/// <summary>The swipe view Reactor renders: per side, the items it shows and their content trees.</summary>
internal sealed class ReactorSwipeView : MauiSwipeView
{
    public readonly SwipeSide[] Sides = [new(), new(), new(), new()];
}

internal sealed class SwipeSide
{
    public SwipeItems? Items;
    public readonly List<ReactorRoot?> Roots = [];
}

internal sealed class SwipeViewHandler : ViewHandler<SwipeViewElement, ReactorSwipeView>
{
    private static readonly Func<SwipeViewElement, SwipeItemData[]>[] Data =
        [static e => e.LeftItems, static e => e.RightItems, static e => e.TopItems, static e => e.BottomItems];

    private static readonly Func<SwipeViewElement, SwipeMode>[] Modes =
        [static e => e.LeftItemsMode, static e => e.RightItemsMode, static e => e.TopItemsMode, static e => e.BottomItemsMode];

    private static readonly BindableProperty[] Properties =
        [MauiSwipeView.LeftItemsProperty, MauiSwipeView.RightItemsProperty, MauiSwipeView.TopItemsProperty, MauiSwipeView.BottomItemsProperty];

    protected override ReactorSwipeView Create(HandlerContext<SwipeViewElement> context, SwipeViewElement element)
    {
        var view = new ReactorSwipeView();
        view.SwipeStarted += (_, e) => context.Live?.OnSwipeStarted?.Invoke(e.SwipeDirection);
        view.SwipeChanging += (_, e) => context.Live?.OnSwipeChanging?.Invoke(e.SwipeDirection, e.Offset);
        view.SwipeEnded += (_, e) => context.Live?.OnSwipeEnded?.Invoke(e.SwipeDirection, e.IsOpen);
        return view;
    }

    protected override void Apply(HandlerContext<SwipeViewElement> context, ReactorSwipeView view, SwipeViewElement? previous, SwipeViewElement next)
    {
        Props.Set(view, MauiSwipeView.ThresholdProperty, previous?.Threshold, next.Threshold);
        for (var side = 0; side < 4; side++)
        {
            var items = Data[side](next);
            var state = view.Sides[side];
            if (previous is null || Modes[side](previous) != Modes[side](next) || !SameStructure(Data[side](previous), items))
            {
                Rebuild(context, view, side, items, Modes[side](next));
                continue;
            }
            // Same structure: only content trees re-render, so what MAUI shows (an open swipe) stays.
            for (var i = 0; i < items.Length; i++)
                if (items[i].Content is { } content) state.Roots[i]?.Render(content);
        }
    }

    private static void Rebuild(HandlerContext<SwipeViewElement> context, ReactorSwipeView view, int side, SwipeItemData[] data, SwipeMode mode)
    {
        var state = view.Sides[side];
        UnmountRoots(state);
        var items = new SwipeItems { Mode = mode };
        for (var i = 0; i < data.Length; i++)
        {
            var index = i;
            var item = data[i];
            Microsoft.Maui.Controls.ISwipeItem created;
            if (item.Content is { } content)
            {
                var itemView = new SwipeItemView();
                SemanticProperties.SetDescription(itemView, item.Text);
                var root = context.CreateChildRoot(native => itemView.Content = native is null ? null : Props.AsView(native));
                state.Roots.Add(root);
                root.Render(content);
                itemView.Invoked += (_, _) => Invoke(context, view, side, index, itemView);
                created = itemView;
            }
            else
            {
                var swipeItem = new SwipeItem { Text = item.Text, IconImageSource = item.Icon };
                if (item.BackgroundColor is { } color) swipeItem.BackgroundColor = color;
                swipeItem.Invoked += (_, _) => Invoke(context, view, side, index, swipeItem);
                state.Roots.Add(null);
                created = swipeItem;
            }
            items.Add(created);
        }
        state.Items = items;
        view.SetValue(Properties[side], items);
    }

    // An item invokes only while it is the one at its index on its side of what the view shows, and the
    // element is mounted (spec 070 §3.5): an item a rebuild replaced may still be held by the platform.
    private static void Invoke(HandlerContext<SwipeViewElement> context, ReactorSwipeView view, int side, int index, Microsoft.Maui.Controls.ISwipeItem item)
    {
        if (context.Live is not { } live) return;
        var state = view.Sides[side];
        if (state.Items is not { } items || !ReferenceEquals(view.GetValue(Properties[side]), items)) return;
        if (index >= items.Count || !ReferenceEquals(items[index], item)) return;
        var current = Data[side](live);
        if (index < current.Length) current[index].OnInvoked?.Invoke();
    }

    private static bool SameStructure(SwipeItemData[] a, SwipeItemData[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
        {
            if ((a[i].Content is null) != (b[i].Content is null)) return false;
            if (a[i].Text != b[i].Text || !Equals(a[i].BackgroundColor, b[i].BackgroundColor) || !Props.SameImage(a[i].Icon, b[i].Icon))
                return false;
        }
        return true;
    }

    private static void UnmountRoots(SwipeSide state)
    {
        foreach (var root in state.Roots) root?.Unmount();
        state.Roots.Clear();
    }

    public override void Unmount(ReactorSwipeView control)
    {
        foreach (var side in control.Sides) UnmountRoots(side);
    }

    public override IChildrenStrategy<SwipeViewElement, ReactorSwipeView> Children { get; } =
        ChildrenStrategy.Single<SwipeViewElement, ReactorSwipeView, object>(
            e => e.Content, (swipeView, content) => swipeView.Content = content is null ? null : Props.AsView(content));
}
