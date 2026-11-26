using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Primitives;
using LayoutAlignment = Microsoft.Maui.Primitives.LayoutAlignment;
using ControlsElement = Microsoft.Maui.Controls.Element;

namespace Microsoft.UI.Reactor.Maui.Direct;

/// <summary>
/// Base class for views that implement MAUI's Core view interfaces directly, the way Comet did.
/// MAUI's stock handlers render them; no <c>Microsoft.Maui.Controls</c> object or bindable
/// property is involved. Properties are plain fields: a write stores the value and asks the
/// handler to re-map that one property.
/// </summary>
public abstract class DirectView : IView, IVisualTreeElement
{
    private IViewHandler? _handler;
    private ControlsElement? _controlsAnchor;

    public IElement? Parent { get; internal set; }

    // Visual-tree tools (hot reload, inspectors, UI test drivers) walk this tree.
    IVisualTreeElement? IVisualTreeElement.GetVisualParent() => Parent as IVisualTreeElement;

    IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => VisualChildren;

    private protected virtual IReadOnlyList<IVisualTreeElement> VisualChildren => [];

    public IViewHandler? Handler
    {
        get => _handler;
        set
        {
            _handler = value;
            HandlerChanged?.Invoke();
        }
    }

    IElementHandler? IElement.Handler
    {
        get => _handler;
        set => Handler = (IViewHandler?)value;
    }

    /// <summary>Raised after a handler connects or disconnects.</summary>
    internal event Action? HandlerChanged;

    public string AutomationId { get; internal set; } = "";
    public LayoutAlignment HorizontalLayoutAlignment { get; private set; } = LayoutAlignment.Fill;
    public LayoutAlignment VerticalLayoutAlignment { get; private set; } = LayoutAlignment.Fill;
    public Thickness Margin { get; private set; }
    public double Width { get; private set; } = Dimension.Unset;
    public double Height { get; private set; } = Dimension.Unset;
    public double MinimumWidth { get; private set; } = Dimension.Minimum;
    public double MaximumWidth { get; private set; } = Dimension.Maximum;
    public double MinimumHeight { get; private set; } = Dimension.Minimum;
    public double MaximumHeight { get; private set; } = Dimension.Maximum;

    public Rect Frame { get; set; }
    public Size DesiredSize { get; private set; }
    public bool IsFocused { get; set; }

    public FlowDirection FlowDirection => FlowDirection.MatchParent;
    public Semantics? Semantics { get; private set; }
    public IShape? Clip => null;
    public IShadow? Shadow => null;
    public bool IsEnabled { get; private set; } = true;
    public Visibility Visibility { get; private set; } = Visibility.Visible;
    public double Opacity { get; private set; } = 1;
    public Paint? Background { get; private set; }
    public int ZIndex { get; private set; }
    public bool InputTransparent { get; private set; }

    public double TranslationX { get; private set; }
    public double TranslationY { get; private set; }
    public double Scale { get; private set; } = 1;
    public double ScaleX => 1;
    public double ScaleY => 1;
    public double Rotation { get; private set; }
    public double RotationX => 0;
    public double RotationY => 0;
    public double AnchorX => 0.5;
    public double AnchorY => 0.5;

    // Placement in a parent grid.
    internal int GridRow { get; private set; }
    internal int GridColumn { get; private set; }
    internal int GridRowSpan { get; private set; } = 1;
    internal int GridColumnSpan { get; private set; } = 1;

    public Size Measure(double widthConstraint, double heightConstraint) =>
        DesiredSize = this.ComputeDesiredSize(widthConstraint, heightConstraint);

    /// <summary>Whether a layout pass has arranged this view: its default <see cref="Frame"/> of zero can't tell (spec 070 §2.6.1).</summary>
    internal bool IsArranged { get; private set; }

    public Size Arrange(Rect bounds)
    {
        IsArranged = true;
        Frame = this.ComputeFrame(bounds);
        _handler?.PlatformArrange(Frame);
        return Frame.Size;
    }

    // The platform propagates the invalidation to ancestors (requestLayout on Android, the
    // superview walk on iOS), so there is no managed-side parent walk as in Controls.
    public void InvalidateMeasure() => _handler?.Invoke(nameof(IView.InvalidateMeasure));

    public void InvalidateArrange() { }

    /// <summary>Focuses the platform view through the handler's <c>Focus</c> command, as a Controls view does.</summary>
    public bool Focus()
    {
        if (_handler is null) return false;
        var request = new FocusRequest();
        _handler.Invoke(nameof(IView.Focus), request);
        // A handler that doesn't map the command leaves no result.
        request.TrySetResult(false);
        return request.Result;
    }

    public void Unfocus() => _handler?.Invoke(nameof(IView.Unfocus));

    /// <summary>
    /// The Controls element this direct tree is hosted under (the Grid wrapping it in a page or
    /// scroll view). Controls views inside the tree take it as their logical parent, so styles and
    /// resources from the page and the app still reach them.
    /// </summary>
    internal ControlsElement? ControlsAnchor
    {
        get
        {
            for (IElement? view = this; view is not null; view = (view as DirectView)?.Parent)
                if (view is DirectView { _controlsAnchor: { } anchor }) return anchor;
            return null;
        }
    }

    internal virtual void SetControlsAnchor(ControlsElement anchor) => _controlsAnchor = anchor;

    /// <summary>
    /// Places this view directly in a Controls element: that element is both its visual parent
    /// (so MAUI can walk up to the window) and the logical anchor for Controls views inside it.
    /// </summary>
    internal void AttachToControlsParent(ControlsElement parent)
    {
        Parent = parent;
        SetControlsAnchor(parent);
    }

    /// <summary>Applies the shared settings. Returns whether anything that affects size changed.</summary>
    internal void ApplyModifiers(ViewModifiers? previous, ViewModifiers? next)
    {
        var p = previous ?? ViewModifiers.Empty;
        var n = next ?? ViewModifiers.Empty;
        if (ReferenceEquals(p, n)) return;
        if (previous is null && n.AutomationId is { } automationId) AutomationId = automationId;

        var measure = false;
        measure |= Set(Margin, n.Margin ?? default, v => Margin = v, nameof(IView.Margin));
        measure |= Set(Width, n.Width ?? Dimension.Unset, v => Width = v, nameof(IView.Width));
        measure |= Set(Height, n.Height ?? Dimension.Unset, v => Height = v, nameof(IView.Height));
        measure |= Set(MinimumWidth, n.MinWidth ?? Dimension.Minimum, v => MinimumWidth = v, nameof(IView.MinimumWidth));
        measure |= Set(MinimumHeight, n.MinHeight ?? Dimension.Minimum, v => MinimumHeight = v, nameof(IView.MinimumHeight));
        measure |= Set(MaximumWidth, n.MaxWidth ?? Dimension.Maximum, v => MaximumWidth = v, nameof(IView.MaximumWidth));
        measure |= Set(MaximumHeight, n.MaxHeight ?? Dimension.Maximum, v => MaximumHeight = v, nameof(IView.MaximumHeight));
        measure |= Set(HorizontalLayoutAlignment, ToAlignment(n.HorizontalOptions), v => HorizontalLayoutAlignment = v, null);
        measure |= Set(VerticalLayoutAlignment, ToAlignment(n.VerticalOptions), v => VerticalLayoutAlignment = v, null);
        measure |= Set(GridRow, n.GridRow ?? 0, v => GridRow = v, null);
        measure |= Set(GridColumn, n.GridColumn ?? 0, v => GridColumn = v, null);
        measure |= Set(GridRowSpan, n.GridRowSpan ?? 1, v => GridRowSpan = v, null);
        measure |= Set(GridColumnSpan, n.GridColumnSpan ?? 1, v => GridColumnSpan = v, null);
        var visibility = n.IsVisible == false ? Visibility.Collapsed : Visibility.Visible;
        if (Set(Visibility, visibility, v => Visibility = v, nameof(IView.Visibility)))
        {
            measure = true;
            (Parent as IView)?.InvalidateMeasure();
        }

        Set(Opacity, n.Opacity ?? 1, v => Opacity = v, nameof(IView.Opacity));
        Set(IsEnabled, n.IsEnabled ?? true, v => IsEnabled = v, nameof(IView.IsEnabled));
        Set(Rotation, n.Rotation ?? 0, v => Rotation = v, nameof(IView.Rotation));
        Set(Scale, n.Scale ?? 1, v => Scale = v, nameof(IView.Scale));
        Set(TranslationX, n.TranslationX ?? 0, v => TranslationX = v, nameof(IView.TranslationX));
        Set(TranslationY, n.TranslationY ?? 0, v => TranslationY = v, nameof(IView.TranslationY));
        Set(ZIndex, n.ZIndex ?? 0, v => ZIndex = v, nameof(IView.ZIndex));
        Set(InputTransparent, n.InputTransparent ?? false, v => InputTransparent = v, nameof(IView.InputTransparent));
        if (!Equals(p.BackgroundColor, n.BackgroundColor))
        {
            Background = n.BackgroundColor is { } color ? new SolidPaint(color) : null;
            Remap(nameof(IView.Background));
        }
        if (p.SemanticDescription != n.SemanticDescription || p.SemanticHint != n.SemanticHint || p.SemanticHeadingLevel != n.SemanticHeadingLevel)
        {
            Semantics = n.SemanticDescription is null && n.SemanticHint is null && n.SemanticHeadingLevel is null
                ? null
                : new Semantics
                {
                    Description = n.SemanticDescription,
                    Hint = n.SemanticHint,
                    HeadingLevel = n.SemanticHeadingLevel ?? SemanticHeadingLevel.None,
                };
            Remap(nameof(IView.Semantics));
        }

        if (measure) InvalidateMeasure();
    }

    private bool Set<T>(T current, T value, Action<T> assign, string? property)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return false;
        assign(value);
        if (property is not null) Remap(property);
        return true;
    }

    private static LayoutAlignment ToAlignment(LayoutOptions? options) => options?.Alignment switch
    {
        null or Microsoft.Maui.Controls.LayoutAlignment.Fill => LayoutAlignment.Fill,
        Microsoft.Maui.Controls.LayoutAlignment.Start => LayoutAlignment.Start,
        Microsoft.Maui.Controls.LayoutAlignment.Center => LayoutAlignment.Center,
        _ => LayoutAlignment.End,
    };

    /// <summary>Asks the handler to push one property to the platform view.</summary>
    protected void Remap(string property) => _handler?.UpdateValue(property);
}
