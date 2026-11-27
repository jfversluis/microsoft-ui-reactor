using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiBorder = Microsoft.Maui.Controls.Border;
using MauiContentView = Microsoft.Maui.Controls.ContentView;
using MauiImage = Microsoft.Maui.Controls.Image;
using MauiImageButton = Microsoft.Maui.Controls.ImageButton;
using MauiLine = Microsoft.Maui.Controls.Shapes.Line;
using MauiPath = Microsoft.Maui.Controls.Shapes.Path;
using MauiPolygon = Microsoft.Maui.Controls.Shapes.Polygon;
using MauiPolyline = Microsoft.Maui.Controls.Shapes.Polyline;
using MauiRectangle = Microsoft.Maui.Controls.Shapes.Rectangle;
using MauiRoundRectangle = Microsoft.Maui.Controls.Shapes.RoundRectangle;
using Shape = Microsoft.Maui.Controls.Shapes.Shape;
using Shapes = Microsoft.Maui.Controls.Shapes;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>The census's claims (spec 070 §4.8): matrix cases first, then the few other kinds.</summary>
public static partial class MatrixCases
{
    // Before the cases: static fields initialize in order.
    private static readonly Point[] Triangle = [new(0, 0), new(10, 0), new(0, 10)];
    private static readonly Point[] BigTriangle = [new(0, 0), new(20, 0), new(0, 20)];

    public static readonly IReadOnlyList<PropertyCase> Properties =
        [.. ShapeCases(), .. ContainerCases(), .. ImageCases(), .. TextCases(), .. ControlCases(), .. LayoutCases(), .. RichTextCases(),
         .. PageCases(), .. ShellCases(), .. SwipeAndCollectionCases()];

    public static readonly IReadOnlyList<ControlledCase> Controlled = [.. ControlledCases(), .. PageControlled()];

    public static readonly IReadOnlyList<EventCase> Events =
    [
        new()
        {
            ElementType = typeof(ImageButtonElement),
            Property = nameof(ImageButtonElement.OnClick),
            Create = report => ImageButton("a.png", () => report("clicked")),
            Trigger = native => ((IButtonController)native).SendClicked(),
            Payload = "clicked",
        },
        .. ControlEvents(),
        .. PageEvents(),
    ];

    public static readonly IReadOnlyList<CoveredClaim> Covered =
    [
        new(typeof(BorderElement), nameof(BorderElement.Content), typeof(VisualsTests), nameof(VisualsTests.Border_Content_IsSetReplacedAndCleared),
            "set, update in place (state kept), replace, clear (unmounted), direct child hosted",
            "content not cleared on null (M25) and a remount on every update (M26): red (spec 070 §4.13)"),
        new(typeof(ContentViewElement), nameof(ContentViewElement.Content), typeof(VisualsTests), nameof(VisualsTests.ContentView_Content_IsSetReplacedAndCleared),
            "set, update in place (state kept), replace, clear (unmounted), direct child hosted",
            "content not cleared on null (M23) and a remount on every update (M24): red (spec 070 §4.13)"),
        .. ControlCovered(),
        .. PageCovered(),
    ];

    public static readonly IReadOnlyList<ExcludedClaim> Excluded = [];

    // ── Values ──────────────────────────────────────────────────────────────

    internal static Brush Solid(Color color) => new SolidColorBrush(color);

    internal static Brush Gradient(float offset = 1) =>
        LinearGradientBrush(new Point(0, 0), new Point(0, 1), (Colors.Red, 0), (Colors.Blue, offset));

    internal static Brush Radial() => RadialGradientBrush(new Point(0.25, 0.25), 0.5, Colors.White, Colors.Gold);

    private static bool SameBrush(object? x, object? y) => BrushComparer.Instance.Equals(x as Brush, y as Brush);

    private static string Join<T>(IEnumerable<T>? items) => items is null ? "" : string.Join(";", items);

    /// <summary>
    /// A case from fresh factory calls: the element from <paramref name="make"/>, and A, B and the equal B
    /// each set with <paramref name="set"/> on a fresh element from a fresh value, so reference values
    /// (brushes, lists) are new instances every time and the expectations are their own instances too.
    /// </summary>
    internal static PropertyCase Case<TElement, TValue>(
        Func<TElement> make, string property, Func<TElement, TValue, TElement> set, Func<TValue> a, Func<TValue> b,
        Func<object, object?> read, BindableProperty native, Func<object?, object?, bool>? same = null,
        Func<TValue, object?>? expect = null, string? direct = null, bool clears = true)
        where TElement : Element
    {
        expect ??= static value => value;
        return new()
        {
            ElementType = make().GetType(),
            Property = property,
            Base = () => make(),
            WithA = () => set(make(), a()),
            WithB = () => set(make(), b()),
            WithBAgain = () => set(make(), b()),
            Read = read,
            A = expect(a()),
            B = expect(b()),
            NativeProperty = native,
            Same = same ?? ((x, y) => Equals(x, y)),
            DirectProperty = direct,
            Clears = clears,
        };
    }

    private static PropertyCase Case<TElement>(
        TElement @base, string property, Func<TElement, TElement> a, Func<TElement, TElement> b, Func<TElement, TElement>? bAgain,
        Func<object, object?> read, object? valueA, object? valueB, BindableProperty native,
        Func<object?, object?, bool>? same = null, string? direct = null, bool clears = true)
        where TElement : Element => new()
        {
            ElementType = @base.GetType(),
            Property = property,
            Base = () => @base,
            WithA = () => a(@base),
            WithB = () => b(@base),
            WithBAgain = () => (bAgain ?? b)(@base),
            Read = read,
            A = valueA,
            B = valueB,
            NativeProperty = native,
            Same = same ?? ((x, y) => Equals(x, y)),
            DirectProperty = direct,
            Clears = clears,
        };

    // ── Shapes (spec 070 §4.3) ──────────────────────────────────────────────

    private static IEnumerable<PropertyCase> ShapeCases()
    {
        Func<ShapeElement>[] shapes =
        [
            () => Rectangle(), () => Ellipse(), () => RoundRectangle(4), () => Line(0, 0, 10, 10),
            () => Polygon(Triangle), () => Polyline(Triangle), () => Path2D("M 0,0 L 10,0 L 0,10 Z"),
        ];
        foreach (var shape in shapes)
        {
            yield return Case(shape, nameof(ShapeElement.Fill), static (s, brush) => s.Fill(brush), static () => Solid(Colors.Red), static () => Gradient(),
                v => ((Shape)v).Fill, Shape.FillProperty, SameBrush);
            yield return Case(shape, nameof(ShapeElement.Stroke), static (s, brush) => s.Stroke(brush), static () => Gradient(), static () => Radial(),
                v => ((Shape)v).Stroke, Shape.StrokeProperty, SameBrush);
            yield return Case(shape, nameof(ShapeElement.StrokeThickness), static (s, t) => s.StrokeThickness(t), static () => 3d, static () => 5d,
                v => ((Shape)v).StrokeThickness, Shape.StrokeThicknessProperty);
            yield return Case(shape, nameof(ShapeElement.StrokeDashArray), static (s, dashes) => s.StrokeDashArray(dashes), static () => new double[] { 2, 1 }, static () => new double[] { 4, 2 },
                v => Join(((Shape)v).StrokeDashArray), Shape.StrokeDashArrayProperty, expect: static d => Join(d));
            yield return Case(shape, nameof(ShapeElement.StrokeDashOffset), static (s, o) => s.StrokeDashOffset(o), static () => 2d, static () => 3d,
                v => ((Shape)v).StrokeDashOffset, Shape.StrokeDashOffsetProperty);
            yield return Case(shape, nameof(ShapeElement.StrokeLineCap), static (s, cap) => s.StrokeLineCap(cap), static () => PenLineCap.Round, static () => PenLineCap.Square,
                v => ((Shape)v).StrokeLineCap, Shape.StrokeLineCapProperty);
            yield return Case(shape, nameof(ShapeElement.StrokeLineJoin), static (s, join) => s.StrokeLineJoin(join), static () => PenLineJoin.Round, static () => PenLineJoin.Bevel,
                v => ((Shape)v).StrokeLineJoin, Shape.StrokeLineJoinProperty);
            yield return Case(shape, nameof(ShapeElement.StrokeMiterLimit), static (s, limit) => s.StrokeMiterLimit(limit), static () => 4d, static () => 6d,
                v => ((Shape)v).StrokeMiterLimit, Shape.StrokeMiterLimitProperty);
            // Neither is a default: MAUI's rectangles and ellipse construct with Fill, the others with None.
            yield return Case(shape, nameof(ShapeElement.Aspect), static (s, aspect) => s.Aspect(aspect), static () => Stretch.UniformToFill, static () => Stretch.Uniform,
                v => ((Shape)v).Aspect, Shape.AspectProperty);
        }

        yield return Case(Rectangle(), nameof(RectangleElement.RadiusX), r => r with { RadiusX = 2 }, r => r with { RadiusX = 4 }, null,
            v => ((MauiRectangle)v).RadiusX, 2d, 4d, MauiRectangle.RadiusXProperty);
        yield return Case(Rectangle(), nameof(RectangleElement.RadiusY), r => r with { RadiusY = 2 }, r => r with { RadiusY = 4 }, null,
            v => ((MauiRectangle)v).RadiusY, 2d, 4d, MauiRectangle.RadiusYProperty);
        yield return Case(RoundRectangle(0), nameof(RoundRectangleElement.CornerRadius), r => r with { CornerRadius = new CornerRadius(4) },
            r => r with { CornerRadius = new CornerRadius(1, 2, 3, 4) }, null,
            v => ((MauiRoundRectangle)v).CornerRadius, new CornerRadius(4), new CornerRadius(1, 2, 3, 4), MauiRoundRectangle.CornerRadiusProperty, clears: false);
        yield return Case(Line(0, 0, 0, 0), nameof(LineElement.X1), l => l with { X1 = 5 }, l => l with { X1 = 7 }, null,
            v => ((MauiLine)v).X1, 5d, 7d, MauiLine.X1Property, clears: false);
        yield return Case(Line(0, 0, 0, 0), nameof(LineElement.Y1), l => l with { Y1 = 5 }, l => l with { Y1 = 7 }, null,
            v => ((MauiLine)v).Y1, 5d, 7d, MauiLine.Y1Property, clears: false);
        yield return Case(Line(0, 0, 0, 0), nameof(LineElement.X2), l => l with { X2 = 5 }, l => l with { X2 = 7 }, null,
            v => ((MauiLine)v).X2, 5d, 7d, MauiLine.X2Property, clears: false);
        yield return Case(Line(0, 0, 0, 0), nameof(LineElement.Y2), l => l with { Y2 = 5 }, l => l with { Y2 = 7 }, null,
            v => ((MauiLine)v).Y2, 5d, 7d, MauiLine.Y2Property, clears: false);

        yield return Case(Polygon(), nameof(PolygonElement.Points), p => p with { Points = Triangle }, p => p with { Points = BigTriangle },
            p => p with { Points = [.. BigTriangle] }, v => Join(((MauiPolygon)v).Points), Join(Triangle), Join(BigTriangle), MauiPolygon.PointsProperty);
        yield return Case(() => Polygon(Triangle), nameof(PolygonElement.FillRule), static (p, rule) => p.FillRule(rule), static () => FillRule.Nonzero, static () => FillRule.EvenOdd,
            v => ((MauiPolygon)v).FillRule, MauiPolygon.FillRuleProperty);
        yield return Case(Polyline(), nameof(PolylineElement.Points), p => p with { Points = Triangle }, p => p with { Points = BigTriangle },
            p => p with { Points = [.. BigTriangle] }, v => Join(((MauiPolyline)v).Points), Join(Triangle), Join(BigTriangle), MauiPolyline.PointsProperty);
        yield return Case(() => Polyline(Triangle), nameof(PolylineElement.FillRule), static (p, rule) => p.FillRule(rule), static () => FillRule.Nonzero, static () => FillRule.EvenOdd,
            v => ((MauiPolyline)v).FillRule, MauiPolyline.FillRuleProperty);

        yield return Case(Path2D(), nameof(PathElement.PathDataString), p => p with { PathDataString = "M 0,0 L 10,0 L 0,10 Z" },
            p => p with { PathDataString = "M 5,5 L 10,0 L 0,10 Z" }, p => p with { PathDataString = string.Concat("M 5,5 L 10,0", " L 0,10 Z") },
            v => PathSignature(((MauiPath)v).Data), "1 from 0,0", "1 from 5,5", MauiPath.DataProperty);
        var geometryA = new Shapes.EllipseGeometry(new Point(5, 5), 5, 5);
        var geometryB = new Shapes.RectangleGeometry(new Rect(0, 0, 10, 10));
        yield return Case(Path2D(), nameof(PathElement.Data), p => p with { Data = geometryA }, p => p with { Data = geometryB }, null,
            v => ((MauiPath)v).Data, geometryA, geometryB, MauiPath.DataProperty, ReferenceEquals);
        var rotate = new Shapes.RotateTransform(45);
        var scale = new Shapes.ScaleTransform(2, 2);
        yield return Case(Path2D("M 0,0 L 10,0 L 0,10 Z"), nameof(PathElement.RenderTransform), p => p with { RenderTransform = rotate },
            p => p with { RenderTransform = scale }, null,
            v => ((MauiPath)v).RenderTransform, rotate, scale, MauiPath.RenderTransformProperty, ReferenceEquals);
    }

    internal static string? PathSignature(Shapes.Geometry? geometry) =>
        geometry is Shapes.PathGeometry path && path.Figures.Count > 0
            ? $"{path.Figures.Count} from {path.Figures[0].StartPoint.X:0},{path.Figures[0].StartPoint.Y:0}"
            : null;

    // ── Containers: Border and ContentView ──────────────────────────────────

    private static IEnumerable<PropertyCase> ContainerCases()
    {
        var border = Border(null);
        yield return Case(border, nameof(BorderElement.Stroke), b => b with { Stroke = Solid(Colors.Red) }, b => b with { Stroke = Gradient() }, null,
            v => ((MauiBorder)v).Stroke, Solid(Colors.Red), Gradient(), MauiBorder.StrokeProperty, SameBrush);
        yield return Case(border, nameof(BorderElement.StrokeThickness), b => b with { StrokeThickness = 3 }, b => b with { StrokeThickness = 5 }, null,
            v => ((MauiBorder)v).StrokeThickness, 3d, 5d, MauiBorder.StrokeThicknessProperty);
        yield return Case(border, nameof(BorderElement.Padding), b => b with { Padding = new Thickness(4) }, b => b with { Padding = new Thickness(1, 2, 3, 4) }, null,
            v => ((MauiBorder)v).Padding, new Thickness(4), new Thickness(1, 2, 3, 4), MauiBorder.PaddingProperty);
        yield return Case(border, nameof(BorderElement.CornerRadius), b => b with { CornerRadius = new CornerRadius(4) }, b => b with { CornerRadius = new CornerRadius(1, 2, 3, 4) }, null,
            v => ((MauiBorder)v).StrokeShape is MauiRoundRectangle r ? r.CornerRadius : ((MauiBorder)v).StrokeShape?.GetType().Name,
            new CornerRadius(4), new CornerRadius(1, 2, 3, 4), MauiBorder.StrokeShapeProperty);

        var content = ContentView(null);
        yield return Case(content, nameof(ContentViewElement.Padding), c => c with { Padding = new Thickness(4) }, c => c with { Padding = new Thickness(1, 2, 3, 4) }, null,
            v => ((MauiContentView)v).Padding, new Thickness(4), new Thickness(1, 2, 3, 4), MauiContentView.PaddingProperty);
    }

    // ── Images (spec 070 §4.6) ──────────────────────────────────────────────

    internal static FontImageSource Glyph(string glyph) => new() { Glyph = glyph, Size = 20, Color = Colors.Black };

    private static bool SameImage(object? x, object? y) => ImageSources.Same(x as ImageSource, y as ImageSource);

    private static object? ImageSourceOf(object view) => view switch
    {
        MauiImage image => image.Source,
        MauiImageButton button => button.Source,
        DirectImage direct => ((Microsoft.Maui.IImage)direct).Source as ImageSource,
        _ => throw new InvalidOperationException(view.GetType().Name),
    };

    private static object? AspectOf(object view) => view switch
    {
        MauiImage image => image.Aspect,
        MauiImageButton button => button.Aspect,
        DirectImage direct => ((Microsoft.Maui.IImage)direct).Aspect,
        _ => throw new InvalidOperationException(view.GetType().Name),
    };

    private static IEnumerable<PropertyCase> ImageCases()
    {
        var image = new ImageElement(null);
        yield return Case(image, nameof(ImageElement.Source), i => i with { Source = Glyph("A") }, i => i with { Source = Glyph("B") }, null,
            ImageSourceOf, Glyph("A"), Glyph("B"), MauiImage.SourceProperty, SameImage, direct: nameof(IImageSourcePart.Source));
        yield return Case(image, nameof(ImageElement.Aspect), i => i with { Aspect = Aspect.Fill }, i => i with { Aspect = Aspect.AspectFill }, null,
            AspectOf, Aspect.Fill, Aspect.AspectFill, MauiImage.AspectProperty, direct: nameof(Microsoft.Maui.IImage.Aspect));
        yield return Case(image, nameof(ImageElement.IsAnimationPlaying), i => i with { IsAnimationPlaying = true }, i => i with { IsAnimationPlaying = false }, null,
            v => v is MauiImage m ? m.IsAnimationPlaying : ((IImageSourcePart)v).IsAnimationPlaying, true, false, MauiImage.IsAnimationPlayingProperty,
            direct: nameof(IImageSourcePart.IsAnimationPlaying));

        var button = new ImageButtonElement(null);
        yield return Case(button, nameof(ImageButtonElement.Source), b => b with { Source = Glyph("A") }, b => b with { Source = Glyph("B") }, null,
            ImageSourceOf, Glyph("A"), Glyph("B"), MauiImageButton.SourceProperty, SameImage);
        yield return Case(button, nameof(ImageButtonElement.Aspect), b => b with { Aspect = Aspect.Fill }, b => b with { Aspect = Aspect.AspectFill }, null,
            AspectOf, Aspect.Fill, Aspect.AspectFill, MauiImageButton.AspectProperty);
        yield return Case(button, nameof(ImageButtonElement.CornerRadius), b => b with { CornerRadius = 3 }, b => b with { CornerRadius = 6 }, null,
            v => ((MauiImageButton)v).CornerRadius, 3, 6, MauiImageButton.CornerRadiusProperty);
        yield return Case(button, nameof(ImageButtonElement.Padding), b => b with { Padding = new Thickness(4) }, b => b with { Padding = new Thickness(1, 2, 3, 4) }, null,
            v => ((MauiImageButton)v).Padding, new Thickness(4), new Thickness(1, 2, 3, 4), MauiImageButton.PaddingProperty);
    }
}
