using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Hosting;
using MauiContentView = Microsoft.Maui.Controls.ContentView;
using MauiPath = Microsoft.Maui.Controls.Shapes.Path;

namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>
/// Brush equality (spec 070 §4.4): solid brushes by color, linear and radial gradients by their points
/// and stops, all compared exactly (an equivalence, so the installed brush is always the last
/// declaration's value); anything else by reference. <see cref="Brush.Default"/>, which MAUI converts a
/// <c>null</c> <see cref="Color"/> to, counts as no brush.
/// </summary>
internal sealed class BrushComparer : IEqualityComparer<Brush?>
{
    public static readonly BrushComparer Instance = new();

    /// <summary>The brush to write, or <see langword="null"/> to clear the property.</summary>
    public static Brush? Normalize(Brush? brush) => ReferenceEquals(brush, Brush.Default) ? null : brush;

    public bool Equals(Brush? x, Brush? y)
    {
        x = Normalize(x);
        y = Normalize(y);
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        return (x, y) switch
        {
            (SolidColorBrush a, SolidColorBrush b) => SameColor(a.Color, b.Color),
            (LinearGradientBrush a, LinearGradientBrush b) => a.GetType() == b.GetType()
                && SamePoint(a.StartPoint, b.StartPoint) && SamePoint(a.EndPoint, b.EndPoint) && SameStops(a.GradientStops, b.GradientStops),
            (RadialGradientBrush a, RadialGradientBrush b) => a.GetType() == b.GetType()
                && SamePoint(a.Center, b.Center) && a.Radius.Equals(b.Radius) && SameStops(a.GradientStops, b.GradientStops),
            _ => false,
        };
    }

    public int GetHashCode(Brush? brush)
    {
        brush = Normalize(brush);
        var hash = new HashCode();
        switch (brush)
        {
            case null:
                return 0;
            case SolidColorBrush solid:
                hash.Add(1);
                AddColor(ref hash, solid.Color);
                break;
            case LinearGradientBrush linear:
                hash.Add(linear.GetType());
                AddPoint(ref hash, linear.StartPoint);
                AddPoint(ref hash, linear.EndPoint);
                AddStops(ref hash, linear.GradientStops);
                break;
            case RadialGradientBrush radial:
                hash.Add(radial.GetType());
                AddPoint(ref hash, radial.Center);
                hash.Add(radial.Radius);
                AddStops(ref hash, radial.GradientStops);
                break;
            default:
                return RuntimeHelpers.GetHashCode(brush);
        }
        return hash.ToHashCode();
    }

    private static bool SameColor(Color? a, Color? b) =>
        a is null ? b is null
        : b is not null && a.Red.Equals(b.Red) && a.Green.Equals(b.Green) && a.Blue.Equals(b.Blue) && a.Alpha.Equals(b.Alpha);

    private static bool SamePoint(Point a, Point b) => a.X.Equals(b.X) && a.Y.Equals(b.Y);

    private static bool SameStops(GradientStopCollection? a, GradientStopCollection? b)
    {
        var countA = a?.Count ?? 0;
        if (countA != (b?.Count ?? 0)) return false;
        for (var i = 0; i < countA; i++)
        {
            var (sa, sb) = (a![i], b![i]);
            if (sa is null || sb is null)
            {
                if (!ReferenceEquals(sa, sb)) return false;
                continue;
            }
            if (!sa.Offset.Equals(sb.Offset) || !SameColor(sa.Color, sb.Color)) return false;
        }
        return true;
    }

    private static void AddColor(ref HashCode hash, Color? color)
    {
        if (color is null) { hash.Add(0); return; }
        hash.Add(color.Red);
        hash.Add(color.Green);
        hash.Add(color.Blue);
        hash.Add(color.Alpha);
    }

    private static void AddPoint(ref HashCode hash, Point point)
    {
        hash.Add(point.X);
        hash.Add(point.Y);
    }

    private static void AddStops(ref HashCode hash, GradientStopCollection? stops)
    {
        if (stops is null) return;
        foreach (var stop in stops)
        {
            if (stop is null) { hash.Add(0); continue; }
            hash.Add(stop.Offset);
            AddColor(ref hash, stop.Color);
        }
    }
}

/// <summary>Compares lists by their items in order; <see langword="null"/> and empty are the same (nothing declared).</summary>
internal sealed class SequenceComparer<T> : IEqualityComparer<IReadOnlyList<T>?>
{
    public static readonly SequenceComparer<T> Instance = new();

    public bool Equals(IReadOnlyList<T>? x, IReadOnlyList<T>? y)
    {
        if (ReferenceEquals(x, y)) return true;
        var count = x?.Count ?? 0;
        if (count != (y?.Count ?? 0)) return false;
        var items = EqualityComparer<T>.Default;
        for (var i = 0; i < count; i++)
            if (!items.Equals(x![i], y![i])) return false;
        return true;
    }

    public int GetHashCode(IReadOnlyList<T>? list)
    {
        var hash = new HashCode();
        if (list is not null) foreach (var item in list) hash.Add(item);
        return hash.ToHashCode();
    }
}

/// <summary>Descriptor entries the visual elements share (spec 070 §4.3).</summary>
internal static class VisualEntries
{
    /// <summary>A brush property: compared by value (<see cref="BrushComparer"/>); <c>null</c> and <see cref="Brush.Default"/> clear it.</summary>
    public static ControlDescriptor<TElement, TView> BrushProperty<TElement, TView>(
        this ControlDescriptor<TElement, TView> descriptor, BindableProperty property, Func<TElement, Brush?> get)
        where TElement : ViewElement where TView : View =>
        descriptor.OneWay(get, (view, brush) => Props.WriteBrush(view, property, brush), BrushComparer.Instance);

    /// <summary>
    /// A list property: successive declarations are compared by sequence; a different one is converted
    /// (<paramref name="project"/>) and written, and <c>null</c> or empty clears the property.
    /// </summary>
    public static ControlDescriptor<TElement, TView> ListProperty<TElement, TView, T>(
        this ControlDescriptor<TElement, TView> descriptor, BindableProperty property,
        Func<TElement, IReadOnlyList<T>?> get, Func<IReadOnlyList<T>, object> project)
        where TElement : ViewElement where TView : View =>
        descriptor.OneWay(get, (view, list) =>
        {
            if (list is null || list.Count == 0) view.ClearValue(property);
            else view.SetValue(property, project(list));
        }, SequenceComparer<T>.Instance);

    /// <summary>The entries every shape shares.</summary>
    public static ControlDescriptor<TElement, TView> ShapeProperties<TElement, TView>(this ControlDescriptor<TElement, TView> descriptor)
        where TElement : ShapeElement where TView : Shape =>
        descriptor
            .BrushProperty(Shape.FillProperty, static e => e.Fill)
            .BrushProperty(Shape.StrokeProperty, static e => e.Stroke)
            .Property(Shape.StrokeThicknessProperty, static e => e.StrokeThickness)
            .ListProperty(Shape.StrokeDashArrayProperty, static e => e.StrokeDashArray, static list => new DoubleCollection([.. list]))
            .Property(Shape.StrokeDashOffsetProperty, static e => e.StrokeDashOffset)
            .Property(Shape.StrokeLineCapProperty, static e => e.StrokeLineCap)
            .Property(Shape.StrokeLineJoinProperty, static e => e.StrokeLineJoin)
            .Property(Shape.StrokeMiterLimitProperty, static e => e.StrokeMiterLimit)
            .Property(Shape.AspectProperty, static e => e.Aspect);
}

/// <summary>The shapes and the content view, as descriptors (spec 070 §4.3, §4.5).</summary>
internal static class VisualDescriptors
{
    public static ControlDescriptor<RectangleElement, Rectangle> Rectangle() =>
        ControlDescriptor.Create<RectangleElement, Rectangle>()
            .ShapeProperties()
            .Property(Microsoft.Maui.Controls.Shapes.Rectangle.RadiusXProperty, static e => e.RadiusX)
            .Property(Microsoft.Maui.Controls.Shapes.Rectangle.RadiusYProperty, static e => e.RadiusY);

    public static ControlDescriptor<EllipseElement, Ellipse> Ellipse() =>
        ControlDescriptor.Create<EllipseElement, Ellipse>().ShapeProperties();

    public static ControlDescriptor<RoundRectangleElement, RoundRectangle> RoundRectangle() =>
        ControlDescriptor.Create<RoundRectangleElement, RoundRectangle>()
            .ShapeProperties()
            .OneWay(static e => e.CornerRadius, static (v, radius) => v.CornerRadius = radius);

    public static ControlDescriptor<LineElement, Line> Line() =>
        ControlDescriptor.Create<LineElement, Line>()
            .ShapeProperties()
            .OneWay(static e => e.X1, static (v, x) => v.X1 = x)
            .OneWay(static e => e.Y1, static (v, y) => v.Y1 = y)
            .OneWay(static e => e.X2, static (v, x) => v.X2 = x)
            .OneWay(static e => e.Y2, static (v, y) => v.Y2 = y);

    public static ControlDescriptor<PolygonElement, Polygon> Polygon() =>
        ControlDescriptor.Create<PolygonElement, Polygon>()
            .ShapeProperties()
            .ListProperty(Microsoft.Maui.Controls.Shapes.Polygon.PointsProperty, static e => e.Points, static points => new PointCollection([.. points]))
            .Property(Microsoft.Maui.Controls.Shapes.Polygon.FillRuleProperty, static e => e.FillRule);

    public static ControlDescriptor<PolylineElement, Polyline> Polyline() =>
        ControlDescriptor.Create<PolylineElement, Polyline>()
            .ShapeProperties()
            .ListProperty(Microsoft.Maui.Controls.Shapes.Polyline.PointsProperty, static e => e.Points, static points => new PointCollection([.. points]))
            .Property(Microsoft.Maui.Controls.Shapes.Polyline.FillRuleProperty, static e => e.FillRule);

    public static ControlDescriptor<PathElement, MauiPath> Path() =>
        ControlDescriptor.Create<PathElement, MauiPath>()
            .ShapeProperties()
            .OneWay<(string? Markup, Geometry? Data)>(static e => (e.PathDataString, e.Data), static (v, data) => PathData.Write(v, data.Markup, data.Data), PathData.Comparer)
            .OneWay(static e => e.RenderTransform, static (v, transform) => PathData.WriteTransform(v, transform), ReferenceEqualityComparer<Transform?>.Instance);

    public static ControlDescriptor<ContentViewElement, MauiContentView> ContentView() =>
        ControlDescriptor.Create<ContentViewElement, MauiContentView>()
            .Property(MauiContentView.PaddingProperty, static e => e.Padding)
            .WithChildren(ChildrenStrategy.Single<ContentViewElement, MauiContentView, object>(
                static e => e.Content, static (view, content) => view.Content = content is null ? null : Props.AsView(content)));
}

/// <summary>Reference equality for any reference type, nullable included.</summary>
internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class?
{
    public static readonly ReferenceEqualityComparer<T> Instance = new();
    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
    public int GetHashCode(T obj) => obj is null ? 0 : RuntimeHelpers.GetHashCode(obj);
}

/// <summary>A path's data and transform, written as WinUI's <c>Path</c> writes them (spec 070 §4.3).</summary>
internal static class PathData
{
    public static readonly IEqualityComparer<(string? Markup, Geometry? Data)> Comparer = new DataComparer();

    private static readonly PathGeometryConverter Converter = new();

    /// <summary>
    /// The markup wins; markup MAUI rejects falls back to <paramref name="data"/>, or to no geometry,
    /// with a warning. Called only when the markup or the data changed.
    /// </summary>
    public static void Write(MauiPath view, string? markup, Geometry? data)
    {
        var geometry = data;
        if (markup is not null)
        {
            try
            {
                geometry = (Geometry?)Converter.ConvertFromInvariantString(markup);
            }
            catch (FormatException)
            {
                DiagnosticLog.Warning(LogCategory.Reactor, "Path.Data",
                    data is null
                        ? "The path markup was rejected; the path draws nothing."
                        : "The path markup was rejected; the path draws its Data instead.");
            }
        }
        if (geometry is null) view.ClearValue(MauiPath.DataProperty);
        else view.Data = geometry;
    }

    /// <summary>
    /// Clearing the transform first writes an identity one: MAUI's iOS and Android path handlers update
    /// the drawn matrix only for a non-null transform, so a plain clear would keep the old one drawn
    /// (characterized on all three platforms, spec 070 §4.13).
    /// </summary>
    public static void WriteTransform(MauiPath view, Transform? transform)
    {
        if (transform is not null)
        {
            view.RenderTransform = transform;
            return;
        }
        if (!view.IsSet(MauiPath.RenderTransformProperty)) return;
        view.RenderTransform = new MatrixTransform();
        view.ClearValue(MauiPath.RenderTransformProperty);
    }

    private sealed class DataComparer : IEqualityComparer<(string? Markup, Geometry? Data)>
    {
        public bool Equals((string? Markup, Geometry? Data) x, (string? Markup, Geometry? Data) y) =>
            string.Equals(x.Markup, y.Markup, StringComparison.Ordinal) && ReferenceEquals(x.Data, y.Data);

        public int GetHashCode((string? Markup, Geometry? Data) value) =>
            HashCode.Combine(value.Markup is null ? 0 : StringComparer.Ordinal.GetHashCode(value.Markup),
                value.Data is null ? 0 : RuntimeHelpers.GetHashCode(value.Data));
    }
}

/// <summary>
/// Image source equality (spec 070 §4.6): file, URI (with its caching settings) and font sources by
/// value; <c>StreamImage</c>s by key; anything else by reference.
/// </summary>
internal static class ImageSources
{
    private static readonly ConditionalWeakTable<ImageSource, string> Keys = new();

    public static ImageSource Keyed(string key, ImageSource source)
    {
        Keys.AddOrUpdate(source, key);
        return source;
    }

    public static string? KeyOf(ImageSource source) => Keys.TryGetValue(source, out var key) ? key : null;

    public static bool Same(ImageSource? a, ImageSource? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        var (keyA, keyB) = (KeyOf(a), KeyOf(b));
        if (keyA is not null || keyB is not null)
            return keyA is not null && keyB is not null && string.Equals(keyA, keyB, StringComparison.Ordinal);
        if (a.GetType() != b.GetType()) return false;
        return (a, b) switch
        {
            (FileImageSource fa, FileImageSource fb) => fa.File == fb.File,
            (UriImageSource ua, UriImageSource ub) => ua.Uri == ub.Uri && ua.CachingEnabled == ub.CachingEnabled && ua.CacheValidity == ub.CacheValidity,
            (FontImageSource x, FontImageSource y) => x.Glyph == y.Glyph && x.FontFamily == y.FontFamily && x.Size.Equals(y.Size)
                && Equals(x.Color, y.Color) && x.FontAutoScalingEnabled == y.FontAutoScalingEnabled,
            _ => false,
        };
    }
}
