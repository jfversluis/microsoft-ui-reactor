using System.Collections;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Layouts;

namespace Microsoft.UI.Reactor.Maui.Direct;

/// <summary>
/// A vertical or horizontal stack measured and arranged by MAUI's own stack layout managers
/// and rendered by MAUI's stock <see cref="LayoutHandler"/>.
/// </summary>
public sealed class DirectStackLayout : DirectView, IStackLayout, IList<IView>
{
    private readonly List<IView> _children = [];
    private readonly ILayoutManager _layoutManager;

    public DirectStackLayout(StackOrientation orientation)
    {
        _layoutManager = orientation == StackOrientation.Vertical
            ? new VerticalStackLayoutManager(this)
            : new HorizontalStackLayoutManager(this);
    }

    public double Spacing { get; private set; }

    public Thickness Padding => default;
    public bool ClipsToBounds => false;
    // Nested Controls layouts leave safe-area insets to the page (SafeAreaEdges.Container, via the
    // internal ISafeAreaView2). Without this, iOS pads every direct stack by the inset.
    public bool IgnoreSafeArea => true;

    public Size CrossPlatformMeasure(double widthConstraint, double heightConstraint) =>
        _layoutManager.Measure(widthConstraint, heightConstraint);

    public Size CrossPlatformArrange(Rect bounds) => _layoutManager.ArrangeChildren(bounds);

    internal void SetSpacing(double spacing)
    {
        if (Spacing.Equals(spacing)) return;
        Spacing = spacing;
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

    private void Attach(IView child)
    {
        if (child is DirectView direct) direct.Parent = this;
    }

    private void Detach(IView child)
    {
        if (child is DirectView direct && direct.Parent == this) direct.Parent = null;
    }

    private void Notify(string action, int index, IView child)
    {
        Handler?.Invoke(action, new LayoutHandlerUpdate(index, child));
        InvalidateMeasure();
    }
}
