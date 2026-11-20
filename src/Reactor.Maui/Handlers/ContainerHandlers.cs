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

internal sealed class ScrollViewHandler : ViewHandler<ScrollViewElement, MauiScrollView>
{
    protected override MauiScrollView Create(HandlerContext<ScrollViewElement> context, ScrollViewElement element)
    {
        var view = new MauiScrollView();
        view.Scrolled += (_, e) => context.Current.OnScrolled?.Invoke(e.ScrollX, e.ScrollY);
        return view;
    }

    protected override void Apply(HandlerContext<ScrollViewElement> context, MauiScrollView view, ScrollViewElement? previous, ScrollViewElement next)
    {
        Props.Set(view, MauiScrollView.OrientationProperty, previous?.Orientation, next.Orientation);
        Props.Set(view, MauiScrollView.VerticalScrollBarVisibilityProperty, previous?.VerticalScrollBarVisibility, next.VerticalScrollBarVisibility);
        Props.Set(view, MauiScrollView.HorizontalScrollBarVisibilityProperty, previous?.HorizontalScrollBarVisibility, next.HorizontalScrollBarVisibility);
    }

    public override IChildrenStrategy<ScrollViewElement, MauiScrollView> Children { get; } =
        ChildrenStrategy.Single<ScrollViewElement, MauiScrollView, object>(
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
            context.Current.OnRefresh?.Invoke();
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

internal sealed class SwipeViewHandler : ViewHandler<SwipeViewElement, MauiSwipeView>
{
    protected override MauiSwipeView Create(HandlerContext<SwipeViewElement> context, SwipeViewElement element) => new();

    protected override void Apply(HandlerContext<SwipeViewElement> context, MauiSwipeView view, SwipeViewElement? previous, SwipeViewElement next)
    {
        if (previous is null || !SameItems(previous.LeftItems, next.LeftItems))
            view.LeftItems = CreateItems(context, next.LeftItems, static e => e.LeftItems);
        if (previous is null || !SameItems(previous.RightItems, next.RightItems))
            view.RightItems = CreateItems(context, next.RightItems, static e => e.RightItems);
    }

    // Items are rebuilt only when what they show changes; their callbacks read the current element.
    private static SwipeItems CreateItems(HandlerContext<SwipeViewElement> context, SwipeAction[] actions, Func<SwipeViewElement, SwipeAction[]> select)
    {
        var items = new SwipeItems();
        for (var i = 0; i < actions.Length; i++)
        {
            var index = i;
            var action = actions[i];
            var item = new SwipeItem { Text = action.Text, IconImageSource = action.Icon };
            if (action.BackgroundColor is { } color) item.BackgroundColor = color;
            item.Invoked += (_, _) =>
            {
                var current = select(context.Current);
                if (index < current.Length) current[index].OnInvoked();
            };
            items.Add(item);
        }
        return items;
    }

    private static bool SameItems(SwipeAction[] a, SwipeAction[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i].Text != b[i].Text || !Equals(a[i].BackgroundColor, b[i].BackgroundColor) || !Props.SameImage(a[i].Icon, b[i].Icon))
                return false;
        }
        return true;
    }

    public override IChildrenStrategy<SwipeViewElement, MauiSwipeView> Children { get; } =
        ChildrenStrategy.Single<SwipeViewElement, MauiSwipeView, object>(
            e => e.Content, (swipeView, content) => swipeView.Content = content is null ? null : Props.AsView(content));
}
