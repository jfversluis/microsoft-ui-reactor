using System.Collections;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Layouts;
using ControlsElement = Microsoft.Maui.Controls.Element;

namespace Microsoft.UI.Reactor.Maui.Direct;

/// <summary>
/// Base of the direct layouts: a child list that notifies MAUI's stock <see cref="LayoutHandler"/>,
/// measured and arranged by one of MAUI's own layout managers.
/// </summary>
public abstract class DirectLayout : DirectView, Microsoft.Maui.ILayout, IList<IView>
{
    private readonly List<IView> _children = [];
    private ILayoutManager? _layoutManager;
    private Thickness _padding;

    public Thickness Padding => _padding;
    public bool ClipsToBounds => false;

    // Nested Controls layouts leave safe-area insets to the page (SafeAreaEdges.Container, via the
    // internal ISafeAreaView2). Without this, iOS pads every direct layout by the inset.
    public bool IgnoreSafeArea => true;

    protected abstract ILayoutManager CreateLayoutManager();

    public Size CrossPlatformMeasure(double widthConstraint, double heightConstraint) =>
        (_layoutManager ??= CreateLayoutManager()).Measure(widthConstraint, heightConstraint);

    public Size CrossPlatformArrange(Rect bounds) => (_layoutManager ??= CreateLayoutManager()).ArrangeChildren(bounds);

    internal void SetPadding(Thickness? padding)
    {
        var value = padding ?? default;
        if (_padding == value) return;
        _padding = value;
        Remap(nameof(IPadding.Padding));
        InvalidateMeasure();
    }

    public IView this[int index]
    {
        get => _children[index];
        set
        {
            Detach(_children[index]);
            _children[index] = value;
            Attach(value);
            Notify(nameof(ILayoutHandler.Update), index, value);
        }
    }

    public int Count => _children.Count;
    public bool IsReadOnly => false;

    public void Add(IView item) => Insert(_children.Count, item);

    public void Insert(int index, IView item)
    {
        _children.Insert(index, item);
        Attach(item);
        Notify(nameof(ILayoutHandler.Insert), index, item);
    }

    public void RemoveAt(int index)
    {
        var item = _children[index];
        _children.RemoveAt(index);
        Detach(item);
        Notify(nameof(ILayoutHandler.Remove), index, item);
    }

    public bool Remove(IView item)
    {
        var index = _children.IndexOf(item);
        if (index < 0) return false;
        RemoveAt(index);
        return true;
    }

    public void Clear()
    {
        foreach (var child in _children) Detach(child);
        _children.Clear();
        Handler?.Invoke(nameof(ILayoutHandler.Clear));
        InvalidateMeasure();
    }

    public bool Contains(IView item) => _children.Contains(item);
    public int IndexOf(IView item) => _children.IndexOf(item);
    public void CopyTo(IView[] array, int arrayIndex) => _children.CopyTo(array, arrayIndex);
    public IEnumerator<IView> GetEnumerator() => _children.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _children.GetEnumerator();

    internal override void SetControlsAnchor(ControlsElement anchor)
    {
        base.SetControlsAnchor(anchor);
        foreach (var child in _children) AnchorChild(child, anchor);
    }

    private void Attach(IView child)
    {
        if (child is DirectView direct) direct.Parent = this;
        if (ControlsAnchor is { } anchor) AnchorChild(child, anchor);
    }

    private static void AnchorChild(IView child, ControlsElement anchor)
    {
        switch (child)
        {
            // A Controls view needs a logical parent for implicit styles, resources and theme.
            case ControlsElement element when element.Parent is null || element.Parent == anchor:
                element.Parent = anchor;
                break;
            case DirectLayout layout:
                foreach (var grandchild in layout) AnchorChild(grandchild, anchor);
                break;
        }
    }

    private void Detach(IView child)
    {
        if (child is DirectView direct && direct.Parent == this) direct.Parent = null;
        if (child is ControlsElement element && element.Parent is not null && element.Parent == ControlsAnchor) element.Parent = null;
    }

    private void Notify(string action, int index, IView child)
    {
        Handler?.Invoke(action, new LayoutHandlerUpdate(index, child));
        InvalidateMeasure();
    }
}

/// <summary>A vertical or horizontal stack, laid out by MAUI's stack layout managers.</summary>
public sealed class DirectStackLayout(StackOrientation orientation) : DirectLayout, IStackLayout
{
    private double _spacing;

    public StackOrientation Orientation { get; } = orientation;
    public double Spacing => _spacing;

    protected override ILayoutManager CreateLayoutManager() => Orientation == StackOrientation.Vertical
        ? new VerticalStackLayoutManager(this)
        : new HorizontalStackLayoutManager(this);

    internal void SetSpacing(double spacing)
    {
        if (_spacing.Equals(spacing)) return;
        _spacing = spacing;
        InvalidateMeasure();
    }
}

/// <summary>A grid, laid out by MAUI's <see cref="GridLayoutManager"/>.</summary>
public sealed class DirectGrid : DirectLayout, IGridLayout
{
    private sealed record Definition(GridLength Length) : IGridRowDefinition, IGridColumnDefinition
    {
        public GridLength Height => Length;
        public GridLength Width => Length;
    }

    private string _rows = "", _columns = "";
    private IReadOnlyList<IGridRowDefinition> _rowDefinitions = [];
    private IReadOnlyList<IGridColumnDefinition> _columnDefinitions = [];
    private double _rowSpacing, _columnSpacing;

    public IReadOnlyList<IGridRowDefinition> RowDefinitions => _rowDefinitions;
    public IReadOnlyList<IGridColumnDefinition> ColumnDefinitions => _columnDefinitions;
    public double RowSpacing => _rowSpacing;
    public double ColumnSpacing => _columnSpacing;

    protected override ILayoutManager CreateLayoutManager() => new GridLayoutManager(this);

    // Direct children keep their placement on themselves; Controls children use the attached properties.
    public int GetRow(IView view) => view is DirectView d ? d.GridRow : (int)((BindableObject)view).GetValue(Microsoft.Maui.Controls.Grid.RowProperty);
    public int GetRowSpan(IView view) => view is DirectView d ? d.GridRowSpan : (int)((BindableObject)view).GetValue(Microsoft.Maui.Controls.Grid.RowSpanProperty);
    public int GetColumn(IView view) => view is DirectView d ? d.GridColumn : (int)((BindableObject)view).GetValue(Microsoft.Maui.Controls.Grid.ColumnProperty);
    public int GetColumnSpan(IView view) => view is DirectView d ? d.GridColumnSpan : (int)((BindableObject)view).GetValue(Microsoft.Maui.Controls.Grid.ColumnSpanProperty);

    internal void Apply(GridElement element)
    {
        var changed = false;
        if (_rows != element.Rows)
        {
            _rows = element.Rows;
            _rowDefinitions = [.. GridLengths.Parse(element.Rows).Select(static l => new Definition(l))];
            changed = true;
        }
        if (_columns != element.Columns)
        {
            _columns = element.Columns;
            _columnDefinitions = [.. GridLengths.Parse(element.Columns).Select(static l => new Definition(l))];
            changed = true;
        }
        if (_rowSpacing != element.RowSpacing || _columnSpacing != element.ColumnSpacing)
        {
            _rowSpacing = element.RowSpacing;
            _columnSpacing = element.ColumnSpacing;
            changed = true;
        }
        if (changed) InvalidateMeasure();
    }
}
