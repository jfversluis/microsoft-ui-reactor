using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Primitives;
using ControlsAlignment = Microsoft.Maui.Controls.LayoutAlignment;
using LayoutAlignment = Microsoft.Maui.Primitives.LayoutAlignment;

namespace Microsoft.UI.Reactor.Maui.Direct;

/// <summary>
/// Base class for views that implement MAUI's Core view interfaces directly, the way Comet did.
/// MAUI's stock handlers render them; no <c>Microsoft.Maui.Controls</c> object or bindable
/// property is involved. Properties are plain fields: a write stores the value and asks the
/// handler to re-map that one property.
/// </summary>
public abstract class DirectView : IView
{
    private IViewHandler? _handler;

    public IElement? Parent { get; internal set; }

    public IViewHandler? Handler
    {
        get => _handler;
        set => _handler = value;
    }

    IElementHandler? IElement.Handler
    {
        get => _handler;
        set => _handler = (IViewHandler?)value;
    }

    public string AutomationId { get; internal set; } = "";
    public LayoutAlignment HorizontalLayoutAlignment { get; private set; } = LayoutAlignment.Fill;
    public LayoutAlignment VerticalLayoutAlignment => LayoutAlignment.Fill;
    public Thickness Margin { get; private set; }
    public double Width { get; private set; } = Dimension.Unset;
    public double Height => Dimension.Unset;
    public double MinimumWidth => Dimension.Minimum;
    public double MaximumWidth => Dimension.Maximum;
    public double MinimumHeight => Dimension.Minimum;
    public double MaximumHeight => Dimension.Maximum;

    public Rect Frame { get; set; }
    public Size DesiredSize { get; private set; }
    public bool IsFocused { get; set; }

    public FlowDirection FlowDirection => FlowDirection.MatchParent;
    public Semantics? Semantics => null;
    public IShape? Clip => null;
    public IShadow? Shadow => null;
    public bool IsEnabled => true;
    public Visibility Visibility => Visibility.Visible;
    public double Opacity => 1;
    public Paint? Background => null;
    public int ZIndex => 0;
    public bool InputTransparent => false;

    public double TranslationX => 0;
    public double TranslationY => 0;
    public double Scale => 1;
    public double ScaleX => 1;
    public double ScaleY => 1;
    public double Rotation => 0;
    public double RotationX => 0;
    public double RotationY => 0;
    public double AnchorX => 0.5;
    public double AnchorY => 0.5;

    public Size Measure(double widthConstraint, double heightConstraint) =>
        DesiredSize = this.ComputeDesiredSize(widthConstraint, heightConstraint);

    public Size Arrange(Rect bounds)
    {
        Frame = this.ComputeFrame(bounds);
        _handler?.PlatformArrange(Frame);
        return Frame.Size;
    }

    // The platform propagates the invalidation to ancestors (requestLayout on Android, the
    // superview walk on iOS), so there is no managed-side parent walk as in Controls.
    public void InvalidateMeasure() => _handler?.Invoke(nameof(IView.InvalidateMeasure));

    public void InvalidateArrange() { }

    public bool Focus() => false;

    public void Unfocus() { }

    internal void SetMargin(Thickness? margin)
    {
        var value = margin ?? default;
        if (Margin == value) return;
        Margin = value;
        Remap(nameof(IView.Margin));
        InvalidateMeasure();
    }

    internal void SetWidth(double? width)
    {
        var value = width ?? Dimension.Unset;
        if (Width.Equals(value)) return;
        Width = value;
        Remap(nameof(IView.Width));
        InvalidateMeasure();
    }

    internal void SetHorizontalOptions(LayoutOptions? options)
    {
        var value = options?.Alignment switch
        {
            null or ControlsAlignment.Fill => LayoutAlignment.Fill,
            ControlsAlignment.Start => LayoutAlignment.Start,
            ControlsAlignment.Center => LayoutAlignment.Center,
            _ => LayoutAlignment.End,
        };
        if (HorizontalLayoutAlignment == value) return;
        HorizontalLayoutAlignment = value;
        InvalidateMeasure();
    }

    /// <summary>Asks the handler to push one property to the platform view.</summary>
    protected void Remap(string property) => _handler?.UpdateValue(property);
}
