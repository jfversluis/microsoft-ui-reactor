using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace Microsoft.UI.Reactor.Maui;

/// <summary>
/// Fluent modifiers. Each returns a copy of the element with one setting changed; the generic
/// ones keep the concrete element type, so they chain: <c>Label("hi").Bold().Margin(8)</c>.
/// </summary>
public static class ViewElementExtensions
{
    private static T Modify<T>(T element, Func<ViewModifiers, ViewModifiers> change) where T : ViewElement =>
        (T)(((ViewElement)element) with { Modifiers = change(element.Modifiers ?? ViewModifiers.Empty) });

    // ── Layout ──────────────────────────────────────────────────────────────

    public static T Margin<T>(this T element, double uniform) where T : ViewElement =>
        Modify(element, m => m with { Margin = new Thickness(uniform) });

    public static T Margin<T>(this T element, double horizontal, double vertical) where T : ViewElement =>
        Modify(element, m => m with { Margin = new Thickness(horizontal, vertical) });

    public static T Margin<T>(this T element, double left, double top, double right, double bottom) where T : ViewElement =>
        Modify(element, m => m with { Margin = new Thickness(left, top, right, bottom) });

    public static T Width<T>(this T element, double width) where T : ViewElement =>
        Modify(element, m => m with { Width = width });

    public static T Height<T>(this T element, double height) where T : ViewElement =>
        Modify(element, m => m with { Height = height });

    public static T Size<T>(this T element, double width, double height) where T : ViewElement =>
        Modify(element, m => m with { Width = width, Height = height });

    public static T MinSize<T>(this T element, double? width = null, double? height = null) where T : ViewElement =>
        Modify(element, m => m with { MinWidth = width ?? m.MinWidth, MinHeight = height ?? m.MinHeight });

    public static T MaxSize<T>(this T element, double? width = null, double? height = null) where T : ViewElement =>
        Modify(element, m => m with { MaxWidth = width ?? m.MaxWidth, MaxHeight = height ?? m.MaxHeight });

    public static T HorizontalOptions<T>(this T element, LayoutOptions options) where T : ViewElement =>
        Modify(element, m => m with { HorizontalOptions = options });

    public static T VerticalOptions<T>(this T element, LayoutOptions options) where T : ViewElement =>
        Modify(element, m => m with { VerticalOptions = options });

    public static T Center<T>(this T element) where T : ViewElement =>
        Modify(element, m => m with { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center });

    /// <summary>Places the element in a parent <c>Grid</c>.</summary>
    public static T Grid<T>(this T element, int row, int column, int rowSpan = 1, int columnSpan = 1) where T : ViewElement =>
        Modify(element, m => m with { GridRow = row, GridColumn = column, GridRowSpan = rowSpan, GridColumnSpan = columnSpan });

    /// <summary>Sets how the element grows, shrinks and sizes in a parent <c>FlexLayout</c>.</summary>
    public static T Flex<T>(this T element, float grow = 0, float shrink = 1, FlexBasis? basis = null, FlexAlignSelf? alignSelf = null) where T : ViewElement =>
        Modify(element, m => m with { FlexGrow = grow, FlexShrink = shrink, FlexBasis = basis, FlexAlignSelf = alignSelf });

    /// <summary>Positions the element in a parent <c>AbsoluteLayout</c>.</summary>
    public static T Absolute<T>(this T element, Rect bounds, AbsoluteLayoutFlags flags = AbsoluteLayoutFlags.None) where T : ViewElement =>
        Modify(element, m => m with { AbsoluteBounds = bounds, AbsoluteFlags = flags });

    // ── Appearance ──────────────────────────────────────────────────────────

    public static T BackgroundColor<T>(this T element, Color? color) where T : ViewElement =>
        Modify(element, m => m with { BackgroundColor = color });

    public static T Background<T>(this T element, Brush? brush) where T : ViewElement =>
        Modify(element, m => m with { Background = brush });

    public static T Opacity<T>(this T element, double opacity) where T : ViewElement =>
        Modify(element, m => m with { Opacity = opacity });

    public static T IsVisible<T>(this T element, bool visible) where T : ViewElement =>
        Modify(element, m => m with { IsVisible = visible });

    public static T IsEnabled<T>(this T element, bool enabled) where T : ViewElement =>
        Modify(element, m => m with { IsEnabled = enabled });

    public static T Rotation<T>(this T element, double degrees) where T : ViewElement =>
        Modify(element, m => m with { Rotation = degrees });

    public static T Scale<T>(this T element, double scale) where T : ViewElement =>
        Modify(element, m => m with { Scale = scale });

    public static T Translation<T>(this T element, double x, double y) where T : ViewElement =>
        Modify(element, m => m with { TranslationX = x, TranslationY = y });

    public static T ZIndex<T>(this T element, int zIndex) where T : ViewElement =>
        Modify(element, m => m with { ZIndex = zIndex });

    public static T Shadow<T>(this T element, Shadow? shadow) where T : ViewElement =>
        Modify(element, m => m with { Shadow = shadow });

    public static T Clip<T>(this T element, Geometry? clip) where T : ViewElement =>
        Modify(element, m => m with { Clip = clip });

    public static T InputTransparent<T>(this T element, bool transparent = true) where T : ViewElement =>
        Modify(element, m => m with { InputTransparent = transparent });

    /// <summary>Applies a MAUI <see cref="Microsoft.Maui.Controls.Style"/>.</summary>
    public static T Style<T>(this T element, Style? style) where T : ViewElement =>
        Modify(element, m => m with { Style = style });

    /// <summary>Applies MAUI style classes from the app's resources.</summary>
    public static T StyleClass<T>(this T element, params string[] classes) where T : ViewElement =>
        Modify(element, m => m with { StyleClass = classes });

    // ── Accessibility ───────────────────────────────────────────────────────

    /// <summary>Sets the automation id UI tests find the element by. Applied once, at mount.</summary>
    public static T AutomationId<T>(this T element, string automationId) where T : ViewElement =>
        Modify(element, m => m with { AutomationId = automationId });

    public static T Semantics<T>(this T element, string? description = null, string? hint = null, SemanticHeadingLevel? headingLevel = null) where T : ViewElement =>
        Modify(element, m => m with { SemanticDescription = description, SemanticHint = hint, SemanticHeadingLevel = headingLevel });

    // ── Gestures ────────────────────────────────────────────────────────────

    public static T OnTap<T>(this T element, Action onTap, int taps = 1) where T : ViewElement =>
        Modify(element, m => m with { OnTap = onTap, TapCount = taps });

    public static T OnSwipe<T>(this T element, Action<SwipeDirection> onSwipe) where T : ViewElement =>
        Modify(element, m => m with { OnSwipe = onSwipe });

    public static T OnPan<T>(this T element, Action<PanUpdatedEventArgs> onPan) where T : ViewElement =>
        Modify(element, m => m with { OnPan = onPan });

    /// <summary>
    /// Attaches <paramref name="target"/>, so its <c>Current</c> is this element's view while the element
    /// is mounted; <see langword="null"/> detaches. With the direct renderer, a ref whose type the direct
    /// view isn't (<c>ElementRef&lt;Entry&gt;</c>) renders this element as a Controls view.
    /// </summary>
    public static TElement Ref<TElement, TView>(this TElement element, Microsoft.UI.Reactor.Input.ElementRef<TView>? target)
        where TElement : ViewElement where TView : class, IView =>
        Modify(element, m => m with { Ref = target?.Cell });

    /// <summary>
    /// Runs <paramref name="configure"/> against the native MAUI view after every mount and update.
    /// The escape hatch for settings no modifier models; keep it idempotent.
    /// </summary>
    public static T Set<T, TView>(this T element, Action<TView> configure) where T : ViewElement where TView : class =>
        Modify(element, m => m with { Configure = view => { if (view is TView typed) configure(typed); } });

    // ── Text ────────────────────────────────────────────────────────────────

    public static T FontSize<T>(this T element, double size) where T : TextViewElement =>
        (T)(((TextViewElement)element) with { FontSize = size });

    public static T Bold<T>(this T element) where T : TextViewElement =>
        (T)(((TextViewElement)element) with { FontAttributes = (element.FontAttributes ?? Microsoft.Maui.Controls.FontAttributes.None) | Microsoft.Maui.Controls.FontAttributes.Bold });

    public static T Italic<T>(this T element) where T : TextViewElement =>
        (T)(((TextViewElement)element) with { FontAttributes = (element.FontAttributes ?? Microsoft.Maui.Controls.FontAttributes.None) | Microsoft.Maui.Controls.FontAttributes.Italic });

    public static T FontFamily<T>(this T element, string family) where T : TextViewElement =>
        (T)(((TextViewElement)element) with { FontFamily = family });

    public static T TextColor<T>(this T element, Color? color) where T : TextViewElement =>
        (T)(((TextViewElement)element) with { TextColor = color });

    public static T CharacterSpacing<T>(this T element, double spacing) where T : TextViewElement =>
        (T)(((TextViewElement)element) with { CharacterSpacing = spacing });

    public static LabelElement Strikethrough(this LabelElement element, bool enabled = true) =>
        element with { TextDecorations = enabled ? Microsoft.Maui.TextDecorations.Strikethrough : null };

    public static LabelElement Underline(this LabelElement element, bool enabled = true) =>
        element with { TextDecorations = enabled ? Microsoft.Maui.TextDecorations.Underline : null };

    public static LabelElement Padding(this LabelElement element, double uniform) => element with { Padding = new Thickness(uniform) };

    public static LabelElement Padding(this LabelElement element, double horizontal, double vertical) =>
        element with { Padding = new Thickness(horizontal, vertical) };

    /// <summary>The line height, as a multiple of the font's (MAUI's <c>LineHeight</c>).</summary>
    public static LabelElement LineHeight(this LabelElement element, double multiple) => element with { LineHeight = multiple };

    public static LabelElement TextTransform(this LabelElement element, TextTransform transform) => element with { TextTransform = transform };

    public static LabelElement MaxLines(this LabelElement element, int lines) =>
        element with { MaxLines = lines, LineBreakMode = Microsoft.Maui.LineBreakMode.TailTruncation };

    public static LabelElement TextAlignment(this LabelElement element, TextAlignment horizontal, TextAlignment? vertical = null) =>
        element with { HorizontalTextAlignment = horizontal, VerticalTextAlignment = vertical ?? element.VerticalTextAlignment };

    // ── Border ──────────────────────────────────────────────────────────────

    public static BorderElement Padding(this BorderElement element, double uniform) => element with { Padding = new Thickness(uniform) };

    public static BorderElement Padding(this BorderElement element, double horizontal, double vertical) =>
        element with { Padding = new Thickness(horizontal, vertical) };

    /// <summary>A solid border, 1 unit thick unless given; <c>null</c> removes the stroke.</summary>
    public static BorderElement Stroke(this BorderElement element, Color? color, double thickness = 1) =>
        element with { Stroke = color is null ? null : (Brush)color, StrokeThickness = thickness };

    /// <summary>
    /// A border painted with <paramref name="brush"/>, compared by value; <c>null</c> removes the stroke.
    /// A <c>null</c> thickness keeps the current one, as for shapes.
    /// </summary>
    public static BorderElement Stroke(this BorderElement element, Brush? brush, double? thickness = null) =>
        element with { Stroke = brush, StrokeThickness = thickness ?? element.StrokeThickness };

    public static BorderElement CornerRadius(this BorderElement element, double radius) => element with { CornerRadius = new CornerRadius(radius) };

    // ── Content view ────────────────────────────────────────────────────────

    public static ContentViewElement Padding(this ContentViewElement element, double uniform) => element with { Padding = new Thickness(uniform) };

    public static ContentViewElement Padding(this ContentViewElement element, double horizontal, double vertical) =>
        element with { Padding = new Thickness(horizontal, vertical) };

    // ── Shapes (spec 070 §4.3) ──────────────────────────────────────────────

    /// <summary>Fills the shape with <paramref name="brush"/>, compared by value; <c>null</c> removes the fill.</summary>
    public static T Fill<T>(this T shape, Brush? brush) where T : ShapeElement =>
        (T)(((ShapeElement)shape) with { Fill = brush });

    /// <summary>Fills the shape with <paramref name="color"/>; <c>null</c> removes the fill.</summary>
    public static T Fill<T>(this T shape, Color? color) where T : ShapeElement =>
        (T)(((ShapeElement)shape) with { Fill = color is null ? null : (Brush)color });

    /// <summary>Strokes the shape with <paramref name="brush"/>, compared by value; <c>null</c> removes the stroke. A <c>null</c> thickness keeps the current one.</summary>
    public static T Stroke<T>(this T shape, Brush? brush, double? thickness = null) where T : ShapeElement =>
        (T)(((ShapeElement)shape) with { Stroke = brush, StrokeThickness = thickness ?? shape.StrokeThickness });

    /// <summary>Strokes the shape with <paramref name="color"/>; <c>null</c> removes the stroke. A <c>null</c> thickness keeps the current one.</summary>
    public static T Stroke<T>(this T shape, Color? color, double? thickness = null) where T : ShapeElement =>
        (T)(((ShapeElement)shape) with { Stroke = color is null ? null : (Brush)color, StrokeThickness = thickness ?? shape.StrokeThickness });

    public static T StrokeThickness<T>(this T shape, double thickness) where T : ShapeElement =>
        (T)(((ShapeElement)shape) with { StrokeThickness = thickness });

    /// <summary>Dash and gap lengths, in multiples of the stroke thickness (copied).</summary>
    public static T StrokeDashArray<T>(this T shape, params double[] dashes) where T : ShapeElement =>
        (T)(((ShapeElement)shape) with { StrokeDashArray = dashes });

    /// <summary>Where the dash pattern starts, in multiples of the stroke thickness.</summary>
    public static T StrokeDashOffset<T>(this T shape, double offset) where T : ShapeElement =>
        (T)(((ShapeElement)shape) with { StrokeDashOffset = offset });

    /// <summary>How far a miter join may extend, relative to half the stroke thickness.</summary>
    public static T StrokeMiterLimit<T>(this T shape, double limit) where T : ShapeElement =>
        (T)(((ShapeElement)shape) with { StrokeMiterLimit = limit });

    /// <summary>How the polygon's interior is determined for its fill.</summary>
    public static PolygonElement FillRule(this PolygonElement polygon, FillRule rule) => polygon with { FillRule = rule };

    /// <summary>How the polyline's interior is determined for its fill.</summary>
    public static PolylineElement FillRule(this PolylineElement polyline, FillRule rule) => polyline with { FillRule = rule };

    public static T StrokeLineCap<T>(this T shape, PenLineCap cap) where T : ShapeElement =>
        (T)(((ShapeElement)shape) with { StrokeLineCap = cap });

    public static T StrokeLineJoin<T>(this T shape, PenLineJoin join) where T : ShapeElement =>
        (T)(((ShapeElement)shape) with { StrokeLineJoin = join });

    /// <summary>How the geometry stretches into the shape's box.</summary>
    public static T Aspect<T>(this T shape, Stretch aspect) where T : ShapeElement =>
        (T)(((ShapeElement)shape) with { Aspect = aspect });

    /// <summary>Rounds the rectangle's corners; <paramref name="y"/> defaults to <paramref name="x"/>.</summary>
    public static RectangleElement Radius(this RectangleElement rectangle, double x, double? y = null) =>
        rectangle with { RadiusX = x, RadiusY = y ?? x };

    // ── Layout containers ───────────────────────────────────────────────────

    public static T Padding<T>(this T element, double uniform) where T : LayoutElement =>
        (T)(((LayoutElement)element) with { Padding = new Thickness(uniform) });

    public static T Padding<T>(this T element, double horizontal, double vertical) where T : LayoutElement =>
        (T)(((LayoutElement)element) with { Padding = new Thickness(horizontal, vertical) });
}

/// <summary>Fluent modifiers for the runs of a formatted label; they keep the run's type.</summary>
public static class RichTextInlineExtensions
{
    public static T FontSize<T>(this T inline, double size) where T : RichTextInline =>
        (T)(((RichTextInline)inline) with { FontSize = size });

    public static T Bold<T>(this T inline) where T : RichTextInline =>
        (T)(((RichTextInline)inline) with { FontAttributes = (inline.FontAttributes ?? Microsoft.Maui.Controls.FontAttributes.None) | Microsoft.Maui.Controls.FontAttributes.Bold });

    public static T Italic<T>(this T inline) where T : RichTextInline =>
        (T)(((RichTextInline)inline) with { FontAttributes = (inline.FontAttributes ?? Microsoft.Maui.Controls.FontAttributes.None) | Microsoft.Maui.Controls.FontAttributes.Italic });

    public static T FontFamily<T>(this T inline, string family) where T : RichTextInline =>
        (T)(((RichTextInline)inline) with { FontFamily = family });

    public static T TextColor<T>(this T inline, Color? color) where T : RichTextInline =>
        (T)(((RichTextInline)inline) with { TextColor = color });

    public static T BackgroundColor<T>(this T inline, Color? color) where T : RichTextInline =>
        (T)(((RichTextInline)inline) with { BackgroundColor = color });

    public static T CharacterSpacing<T>(this T inline, double spacing) where T : RichTextInline =>
        (T)(((RichTextInline)inline) with { CharacterSpacing = spacing });

    public static T LineHeight<T>(this T inline, double multiple) where T : RichTextInline =>
        (T)(((RichTextInline)inline) with { LineHeight = multiple });

    public static T TextTransform<T>(this T inline, TextTransform transform) where T : RichTextInline =>
        (T)(((RichTextInline)inline) with { TextTransform = transform });

    /// <summary>Turns the underline on or off, keeping any other decoration (a hyperlink starts underlined).</summary>
    public static T Underline<T>(this T inline, bool enabled = true) where T : RichTextInline =>
        Decorate(inline, Microsoft.Maui.TextDecorations.Underline, enabled);

    /// <summary>Turns the strikethrough on or off, keeping any other decoration.</summary>
    public static T Strikethrough<T>(this T inline, bool enabled = true) where T : RichTextInline =>
        Decorate(inline, Microsoft.Maui.TextDecorations.Strikethrough, enabled);

    private static T Decorate<T>(T inline, TextDecorations flag, bool enabled) where T : RichTextInline
    {
        var current = inline.TextDecorations ?? (inline is RichTextHyperlink ? Microsoft.Maui.TextDecorations.Underline : Microsoft.Maui.TextDecorations.None);
        return (T)(((RichTextInline)inline) with { TextDecorations = enabled ? current | flag : current & ~flag });
    }
}
