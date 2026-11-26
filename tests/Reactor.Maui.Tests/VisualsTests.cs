using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiBorder = Microsoft.Maui.Controls.Border;
using MauiButton = Microsoft.Maui.Controls.Button;
using MauiContentView = Microsoft.Maui.Controls.ContentView;
using MauiEllipse = Microsoft.Maui.Controls.Shapes.Ellipse;
using MauiImage = Microsoft.Maui.Controls.Image;
using MauiImageButton = Microsoft.Maui.Controls.ImageButton;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiPath = Microsoft.Maui.Controls.Shapes.Path;
using MauiPolygon = Microsoft.Maui.Controls.Shapes.Polygon;
using MauiPolyline = Microsoft.Maui.Controls.Shapes.Polyline;
using MauiSwipeView = Microsoft.Maui.Controls.SwipeView;
using Shape = Microsoft.Maui.Controls.Shapes.Shape;
using Shapes = Microsoft.Maui.Controls.Shapes;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>Spec 070 §4 (4c-3a): brushes, shapes, containers and image sources, beyond the property matrix.</summary>
public class VisualsTests
{
    private static TestHost Host(bool direct = false) => new(direct ? DirectHandlers.CreateRegistry() : MauiHandlers.CreateRegistry());

    private static LinearGradientBrush Linear(Point start, Point end, params (Color, double)[] stops) => LinearGradientBrush(start, end, stops);

    private static int CountWrites(BindableObject view, BindableProperty property, Action render)
    {
        var writes = 0;
        void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == property.PropertyName) writes++;
        }
        view.PropertyChanged += OnChanged;
        try { render(); }
        finally { view.PropertyChanged -= OnChanged; }
        return writes;
    }

    // ── Brush equality (§4.4) ───────────────────────────────────────────────

    public static TheoryData<string, Brush?, Brush?, bool> BrushRules() => new()
    {
        { "both null", null, null, true },
        { "null and a brush", null, new SolidColorBrush(Colors.Red), false },
        { "null and Brush.Default (a null color)", null, Brush.Default, true },
        { "solid, same color", new SolidColorBrush(Colors.Red), new SolidColorBrush(Colors.Red), true },
        { "an immutable brush and a solid brush of its color", Colors.Red, new SolidColorBrush(Colors.Red), true },
        { "solid, colors one float step apart", new SolidColorBrush(new Color(0.5f, 0, 0)), new SolidColorBrush(new Color(BitIncrement(0.5f), 0, 0)), false },
        { "linear, same", Linear(new(0, 0), new(0, 1), (Colors.Red, 0), (Colors.Blue, 1)), Linear(new(0, 0), new(0, 1), (Colors.Red, 0), (Colors.Blue, 1)), true },
        { "linear, another end point", Linear(new(0, 0), new(0, 1), (Colors.Red, 0), (Colors.Blue, 1)), Linear(new(0, 0), new(1, 1), (Colors.Red, 0), (Colors.Blue, 1)), false },
        { "linear, another start point", Linear(new(0, 0), new(0, 1), (Colors.Red, 0), (Colors.Blue, 1)), Linear(new(0.5, 0), new(0, 1), (Colors.Red, 0), (Colors.Blue, 1)), false },
        { "linear, stops reordered", Linear(new(0, 0), new(0, 1), (Colors.Red, 0), (Colors.Blue, 1)), Linear(new(0, 0), new(0, 1), (Colors.Blue, 1), (Colors.Red, 0)), false },
        { "linear, offsets 1e-6 apart (MAUI's GradientStop.Equals calls them equal)", Linear(new(0, 0), new(0, 1), (Colors.Red, 0), (Colors.Blue, 1)), Linear(new(0, 0), new(0, 1), (Colors.Red, 0.000001), (Colors.Blue, 1)), false },
        { "linear, a stop color one float step apart", Linear(new(0, 0), new(0, 1), (new Color(0.5f, 0, 0), 0)), Linear(new(0, 0), new(0, 1), (new Color(BitIncrement(0.5f), 0, 0), 0)), false },
        { "linear, a stop fewer", Linear(new(0, 0), new(0, 1), (Colors.Red, 0), (Colors.Blue, 1)), Linear(new(0, 0), new(0, 1), (Colors.Red, 0)), false },
        { "radial, same", RadialGradientBrush(new(0.25, 0.25), 0.5, Colors.White, Colors.Gold), RadialGradientBrush(new(0.25, 0.25), 0.5, Colors.White, Colors.Gold), true },
        { "radial, another radius", RadialGradientBrush(new(0.25, 0.25), 0.5, Colors.White, Colors.Gold), RadialGradientBrush(new(0.25, 0.25), 0.6, Colors.White, Colors.Gold), false },
        { "radial, another center", RadialGradientBrush(new(0.25, 0.25), 0.5, Colors.White, Colors.Gold), RadialGradientBrush(new(0.5, 0.25), 0.5, Colors.White, Colors.Gold), false },
        { "linear and radial with the same stops", new LinearGradientBrush([new GradientStop(Colors.Red, 0)]), new RadialGradientBrush([new GradientStop(Colors.Red, 0)]), false },
        { "an app's own brush, two instances", new OwnBrush(), new OwnBrush(), false },
        { "a gradient subclass and its base with the same stops", new LinearGradientBrush([new GradientStop(Colors.Red, 0)]), new DerivedGradient([new GradientStop(Colors.Red, 0)]), false },
        { "solid and linear", new SolidColorBrush(Colors.Red), new LinearGradientBrush([new GradientStop(Colors.Red, 0)]), false },
    };

    private static float BitIncrement(float value) => MathF.BitIncrement(value);

    private sealed class DerivedGradient(GradientStopCollection stops) : LinearGradientBrush(stops);

    private sealed class OwnBrush : Brush
    {
        public override bool IsEmpty => false;
    }

    [Theory]
    [MemberData(nameof(BrushRules))]
    public void BrushComparer_FollowsTheRules(string name, Brush? a, Brush? b, bool equal)
    {
        Assert.True(BrushComparer.Instance.Equals(a, b) == equal, name);
        Assert.True(BrushComparer.Instance.Equals(b, a) == equal, name + " (reversed)");
        if (equal) Assert.Equal(BrushComparer.Instance.GetHashCode(a), BrushComparer.Instance.GetHashCode(b));
    }

    [Fact]
    public void CumulativeSmallChanges_AreAllWritten_SoTheInstalledBrushIsTheLastDeclaration()
    {
        Brush Stops(double offset) => Linear(new(0, 0), new(0, 1), (Colors.Red, offset), (Colors.Blue, 1));
        var host = Host().Render(Ellipse().Fill(Stops(0)));
        var ellipse = (MauiEllipse)host.RootView!;

        foreach (var offset in new[] { 0.000008, 0.000016 })
        {
            Assert.Equal(1, CountWrites(ellipse, Shape.FillProperty, () => host.Render(Ellipse().Fill(Stops(offset)))));
            Assert.Equal((float)offset, ((GradientBrush)ellipse.Fill).GradientStops[0].Offset);
        }

        var labelHost = Host().Render(Label("x").Background(Stops(0)));
        var label = (MauiLabel)labelHost.RootView!;
        labelHost.Render(Label("x").Background(Stops(0.000008)));
        labelHost.Render(Label("x").Background(Stops(0.000016)));
        Assert.Equal(0.000016f, ((GradientBrush)label.Background).GradientStops[0].Offset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Background_ComparesBrushesByValue(bool direct)
    {
        var host = Host(direct).Render(Label("x").Background(Linear(new(0, 0), new(0, 1), (Colors.Red, 0), (Colors.Blue, 1))));
        var label = (MauiLabel)host.RootView!;
        var installed = label.Background;

        Assert.Equal(0, CountWrites(label, VisualElement.BackgroundProperty,
            () => host.Render(Label("x").Background(Linear(new(0, 0), new(0, 1), (Colors.Red, 0), (Colors.Blue, 1))))));
        Assert.Same(installed, label.Background);
        Assert.Equal(1, CountWrites(label, VisualElement.BackgroundProperty,
            () => host.Render(Label("x").Background(Linear(new(0, 0), new(0, 1), (Colors.Red, 0), (Colors.Green, 1))))));
        Assert.Equal(1, CountWrites(label, VisualElement.BackgroundProperty, () => host.Render(Label("x").Background(Colors.Red))));
        Assert.Equal(1, CountWrites(label, VisualElement.BackgroundProperty,
            () => host.Render(Label("x").Background(Linear(new(0, 0), new(0, 1), (Colors.Red, 0))))));
        host.Render(Label("x"));
        if (direct) Assert.IsType<DirectLabel>(host.RootView);
        else Assert.False(label.IsSet(VisualElement.BackgroundProperty));

        // A null color is no brush: it doesn't need a Controls view.
        Color? none = null;
        var plain = Host(direct).Render(Label("x").Background(none));
        if (direct) Assert.IsType<DirectLabel>(plain.RootView);
        else Assert.False(((MauiLabel)plain.RootView!).IsSet(VisualElement.BackgroundProperty));
    }

    [Fact]
    public void ABrush_IsImmutableOnceRendered_AnEqualOneIsNeverWritten()
    {
        var first = Linear(new(0, 0), new(0, 1), (Colors.Red, 0), (Colors.Blue, 1));
        var host = Host().Render(Ellipse().Fill(first));
        var ellipse = (MauiEllipse)host.RootView!;

        var equal = Linear(new(0, 0), new(0, 1), (Colors.Red, 0), (Colors.Blue, 1));
        host.Render(Ellipse().Fill(equal));
        Assert.Same(first, ellipse.Fill);

        // The documented consequence: mutating the brush that wasn't written changes nothing.
        ((GradientBrush)equal).GradientStops[0].Color = Colors.Green;
        Assert.Same(first, ellipse.Fill);
        Assert.Equal(Colors.Red, ((GradientBrush)ellipse.Fill).GradientStops[0].Color);

        // A new brush with a new value is written (positive control). (Green would not be: the mutated
        // brush is the retained declaration, which is why mutating a rendered brush is unsupported.)
        host.Render(Ellipse().Fill(Linear(new(0, 0), new(0, 1), (Colors.Gold, 0), (Colors.Blue, 1))));
        Assert.Equal(Colors.Gold, ((GradientBrush)ellipse.Fill).GradientStops[0].Color);
    }

    [Fact]
    public void GradientFactories_CopyTheirStops_AndSpaceColorsEvenly()
    {
        (Color, double)[] stops = [(Colors.Red, 0), (Colors.Blue, 0.25)];
        var linear = LinearGradientBrush(new(0, 0), new(1, 0), stops);
        stops[1] = (Colors.Green, 0.9);
        Assert.Equal([(Colors.Red, 0f), (Colors.Blue, 0.25f)], linear.GradientStops.Select(s => (s.Color, s.Offset)));
        Assert.Equal((new Point(0, 0), new Point(1, 0)), (linear.StartPoint, linear.EndPoint));

        var even = LinearGradientBrush(new(0, 0), new(0, 1), Colors.Red, Colors.Green, Colors.Blue);
        Assert.Equal([0f, 0.5f, 1f], even.GradientStops.Select(s => s.Offset));
        Assert.Equal([0f], RadialGradientBrush(new(0.5, 0.5), 0.5, Colors.Red).GradientStops.Select(s => s.Offset));

        var radial = RadialGradientBrush(new(0.25, 0.75), 0.4, (Colors.White, 0), (Colors.Gold, 1));
        Assert.Equal((new Point(0.25, 0.75), 0.4), (radial.Center, radial.Radius));
    }

    // ── Color overloads and null (§4.3, §4.4) ───────────────────────────────

    [Fact]
    public void ANullColor_ClearsTheBrush_SoAStyleApplies()
    {
        var style = new Style(typeof(MauiEllipse));
        style.Setters.Add(new Setter { Property = Shape.FillProperty, Value = Colors.Orange });
        style.Setters.Add(new Setter { Property = Shape.StrokeProperty, Value = Colors.Orange });
        var host = Host().Render(Ellipse().Fill(Colors.Blue).Stroke(Colors.Blue).Style(style));
        var ellipse = (MauiEllipse)host.RootView!;
        Assert.True(BrushComparer.Instance.Equals(Colors.Blue, ellipse.Fill));

        host.Render(Ellipse().Fill(null).Stroke(null).Style(style));
        Assert.True(BrushComparer.Instance.Equals(Colors.Orange, ellipse.Fill), $"the style's fill applies, got {ellipse.Fill}");
        Assert.True(BrushComparer.Instance.Equals(Colors.Orange, ellipse.Stroke), $"the style's stroke applies, got {ellipse.Stroke}");

        Color? none = null;
        host.Render(Ellipse().Fill(Colors.Blue).Stroke(Colors.Blue).Style(style));
        host.Render(Ellipse().Fill(none).Stroke(none).Style(style));
        Assert.True(BrushComparer.Instance.Equals(Colors.Orange, ellipse.Fill) && BrushComparer.Instance.Equals(Colors.Orange, ellipse.Stroke));

        // Without a style: no local value left, including for Brush.Default (what MAUI converts a null color to).
        var plain = Host().Render(Ellipse().Fill(Colors.Blue));
        var unstyled = (MauiEllipse)plain.RootView!;
        plain.Render(Ellipse().Fill(null));
        Assert.False(unstyled.IsSet(Shape.FillProperty));
        plain.Render(Ellipse().Fill(Colors.Blue));
        plain.Render(Ellipse() with { Fill = none });
        Assert.False(unstyled.IsSet(Shape.FillProperty));

        // A brush keeps the current thickness, as on shapes; a color defaults to 1, as before.
        var withBrush = Border(null).Stroke(Colors.Blue, 4).Stroke(MatrixCases.Gradient());
        Assert.Equal(4, withBrush.StrokeThickness);
        Assert.Equal(1, Border(null).Stroke(Colors.Blue, 4).Stroke(Colors.Red).StrokeThickness);
        Assert.Equal(3, Border(null).Stroke(MatrixCases.Gradient(), 3).StrokeThickness);

        var borderHost = Host().Render(Border(null).Stroke(Colors.Blue));
        var border = (MauiBorder)borderHost.RootView!;
        borderHost.Render(Border(null).Stroke(null));
        Assert.False(border.IsSet(MauiBorder.StrokeProperty));
        borderHost.Render(Border(null).Stroke(color: Colors.Red, thickness: 2));
        Assert.True(BrushComparer.Instance.Equals(Colors.Red, border.Stroke));
        borderHost.Render(Border(null).Stroke(none));
        Assert.False(border.IsSet(MauiBorder.StrokeProperty));
    }

    // ── Collections are values (§4.3) ───────────────────────────────────────

    [Fact]
    public void PointAndDashArrays_AreCopied_SoTheAppCanMutateOrReuseThem()
    {
        Point[] points = [new(0, 0), new(10, 0), new(0, 10)];
        double[] dashes = [2, 1];
        var host = Host().Render(Polygon(points).StrokeDashArray(dashes));
        var polygon = (MauiPolygon)host.RootView!;

        // The retained declaration is a copy: a new array equal to the mutated one is a change.
        points[1] = new Point(20, 0);
        dashes[0] = 4;
        host.Render(Polygon([.. points]).StrokeDashArray([.. dashes]));
        Assert.Equal(new Point(20, 0), polygon.Points[1]);
        Assert.Equal([4d, 1d], polygon.StrokeDashArray);

        // One array instance reused across renders, mutated in between: each render's content is written.
        Point[] reused = [new(0, 0), new(5, 0), new(0, 5)];
        host.Render(Polygon(reused));
        Assert.Equal(new Point(5, 0), polygon.Points[1]);
        reused[1] = new Point(6, 0);
        host.Render(Polygon(reused));
        Assert.Equal(new Point(6, 0), polygon.Points[1]);

        // Mutating it after the last render changes nothing.
        reused[1] = new Point(7, 0);
        Assert.Equal(new Point(6, 0), polygon.Points[1]);

        var line = (MauiPolyline)Host().Render(Polyline(reused)).RootView!;
        Assert.Equal(new Point(7, 0), line.Points[1]);
    }

    // ── Paths (§4.3) ────────────────────────────────────────────────────────

    [Fact]
    public void ClearingRenderTransform_WritesAnIdentityFirst_SoTheDrawableResets()
    {
        var rotate = new Shapes.RotateTransform(90, 50, 50);
        var host = Host().Render(Path2D("M 0,0 L 100,0 L 0,100 Z") with { RenderTransform = rotate });
        var path = (MauiPath)host.RootView!;
        var seen = WatchTransform(path);

        host.Render(Path2D("M 0,0 L 100,0 L 0,100 Z"));
        Assert.Equal(["RotateTransform", "unset", "identity"], seen);
        Assert.False(path.IsSet(MauiPath.RenderTransformProperty));

        // Nothing set: clearing writes nothing.
        seen.Clear();
        host.Render(Path2D("M 0,0 L 100,0 L 0,100 Z"));
        Assert.Empty(seen);
    }

    private static Style TransformStyle(Shapes.Transform transform)
    {
        var style = new Style(typeof(MauiPath));
        style.Setters.Add(new Setter { Property = MauiPath.RenderTransformProperty, Value = transform });
        return style;
    }

    /// <summary>
    /// The transform each change of <c>RenderTransform</c> leaves, read as it starts: a value set and
    /// cleared again inside another change's notification is seen here even when a later observer of
    /// the notification no longer could.
    /// </summary>
    private static List<string> WatchTransform(MauiPath path)
    {
        var left = new List<string>();
        path.PropertyChanging += (_, e) =>
        {
            if (e.PropertyName == nameof(MauiPath.RenderTransform))
                left.Add(path.RenderTransform switch { null => "unset", Shapes.MatrixTransform m when m.Matrix.IsIdentity => "identity", var t => t.GetType().Name });
        };
        return left;
    }

    [Fact]
    public void RemovingAStyleThatProvidedTheTransform_ResetsTheDrawable()
    {
        var style = TransformStyle(new Shapes.RotateTransform(90, 50, 50));
        var host = Host().Render(Path2D("M 0,0 L 100,0 L 0,100 Z").Style(style));
        var path = (MauiPath)host.RootView!;
        Assert.IsType<Shapes.RotateTransform>(path.RenderTransform);
        var seen = WatchTransform(path);

        // Both declarations have no transform of their own: only the style's goes away.
        host.Render(Path2D("M 0,0 L 100,0 L 0,100 Z"));
        Assert.Equal(["RotateTransform", "unset", "identity"], seen);
        Assert.Null(path.RenderTransform);
        Assert.False(path.IsSet(MauiPath.RenderTransformProperty));
    }

    private static Element ClassedPath(string styleClass) =>
        ContentView(Path2D("M 0,0 L 100,0 L 0,100 Z").StyleClass(styleClass))
            .Set<ContentViewElement, MauiContentView>(view =>
            {
                if (view.Resources.ContainsKey("spin-style")) return;
                var spin = new Style(typeof(MauiPath)) { Class = "spin" };
                spin.Setters.Add(new Setter { Property = MauiPath.RenderTransformProperty, Value = new Shapes.RotateTransform(90, 50, 50) });
                // MAUI re-resolves class styles only when the new class has one: "calm" sets no transform.
                var calm = new Style(typeof(MauiPath)) { Class = "calm" };
                calm.Setters.Add(new Setter { Property = Shape.FillProperty, Value = Colors.Black });
                view.Resources.Add("spin-style", spin);
                view.Resources.Add(spin);
                view.Resources.Add(calm);
            });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AClassStyleTransform_ResolvedOnAttachment_IsResetWhenTheClassChanges(bool direct)
    {
        // The class style resolves only once the path is attached, after its mount's apply.
        var host = Host(direct).Render(ClassedPath("spin"));
        var path = (MauiPath)((MauiContentView)host.RootView!).Content;
        Assert.IsType<Shapes.RotateTransform>(path.RenderTransform);
        var seen = WatchTransform(path);

        host.Render(ClassedPath("calm"));
        Assert.Equal(["RotateTransform", "unset", "identity"], seen);
        Assert.Null(path.RenderTransform);
    }

    [Fact]
    public void ATransformWrittenThroughARef_IsResetWhenARenderClearsIt()
    {
        var reference = Microsoft.UI.Reactor.Input.TypedElementRef.Create<MauiPath>();
        var host = Host().Render(Path2D("M 0,0 L 100,0 L 0,100 Z").Ref(reference));
        var path = reference.Current!;
        path.RenderTransform = new Shapes.RotateTransform(90, 50, 50);
        var seen = WatchTransform(path);

        host.Render(Path2D("M 0,0 L 100,0 L 0,100 Z").Ref(reference)
            .Set<PathElement, MauiPath>(p => { if (p.RenderTransform is Shapes.RotateTransform) p.ClearValue(MauiPath.RenderTransformProperty); }));
        Assert.Equal(["RotateTransform", "unset", "identity"], seen);
        Assert.Null(path.RenderTransform);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AParentsReplacedResources_ClearingTheTransformOutsideARender_ResetTheDrawable(bool direct)
    {
        var host = Host(direct).Render(ClassedPath("spin"));
        var parent = (MauiContentView)host.RootView!;
        var path = (MauiPath)parent.Content;
        Assert.IsType<Shapes.RotateTransform>(path.RenderTransform);
        var seen = WatchTransform(path);

        // No render: the app replaces the parent's resources, and "spin" no longer rotates.
        var plainSpin = new Style(typeof(MauiPath)) { Class = "spin" };
        plainSpin.Setters.Add(new Setter { Property = Shape.FillProperty, Value = Colors.Black });
        parent.Resources = new ResourceDictionary { plainSpin };

        Assert.Equal(["RotateTransform", "unset", "identity"], seen);
        Assert.Null(path.RenderTransform);
        Assert.False(path.IsSet(MauiPath.RenderTransformProperty));
    }

    [Fact]
    public void ATransformSetToNullThroughARef_IsReset_AndLeavesNoLocalValue()
    {
        var reference = Microsoft.UI.Reactor.Input.TypedElementRef.Create<MauiPath>();
        Host().Render(Path2D("M 0,0 L 100,0 L 0,100 Z").Ref(reference));
        var path = reference.Current!;
        path.RenderTransform = new Shapes.RotateTransform(90, 50, 50);
        var seen = WatchTransform(path);

        // A write of null runs inside MAUI's setter, which queues the identity write behind it.
        path.RenderTransform = null;
        Assert.Equal(["RotateTransform", "unset", "identity"], seen);
        Assert.Null(path.RenderTransform);
        Assert.False(path.IsSet(MauiPath.RenderTransformProperty));
    }

    [Fact]
    public void EqualRenders_WriteNoTransform()
    {
        var rotate = new Shapes.RotateTransform(90, 50, 50);
        var host = Host().Render(Path2D("M 0,0 L 100,0 L 0,100 Z") with { RenderTransform = rotate });
        var path = (MauiPath)host.RootView!;
        var seen = WatchTransform(path);
        host.Render(Path2D("M 0,0 L 100,0 L 0,100 Z") with { RenderTransform = rotate });
        host.Render(Path2D("M 0,0 L 100,0 L 0,100 Z") with { RenderTransform = rotate });
        Assert.Empty(seen);

        var classed = Host().Render(ClassedPath("spin"));
        var classedPath = (MauiPath)((MauiContentView)classed.RootView!).Content;
        var classedSeen = WatchTransform(classedPath);
        classed.Render(ClassedPath("spin"));
        Assert.Empty(classedSeen);
    }

    [Fact]
    public void ATransformAStyleStillProvides_IsLeftAlone()
    {
        var style = TransformStyle(new Shapes.RotateTransform(90, 50, 50));
        var host = Host().Render(Path2D("M 0,0 L 100,0 L 0,100 Z").Style(style) with { RenderTransform = new Shapes.ScaleTransform(2, 2) });
        var path = (MauiPath)host.RootView!;
        var seen = WatchTransform(path);

        host.Render(Path2D("M 0,0 L 100,0 L 0,100 Z").Style(style));
        Assert.Equal(["ScaleTransform"], seen);
        Assert.IsType<Shapes.RotateTransform>(path.RenderTransform);
    }

    [Fact]
    public void PathData_TheStringWins_AndIsConvertedOnlyWhenItChanges()
    {
        var geometry = new Shapes.EllipseGeometry(new Point(5, 5), 5, 5);
        var host = Host().Render(Path2D("M 0,0 L 10,0 Z") with { Data = geometry });
        var path = (MauiPath)host.RootView!;
        Assert.Equal("1 from 0,0", MatrixCases.PathSignature(path.Data));

        Assert.Equal(0, CountWrites(path, MauiPath.DataProperty, () => host.Render(Path2D(string.Concat("M 0,0 ", "L 10,0 Z")) with { Data = geometry })));
        Assert.Equal(1, CountWrites(path, MauiPath.DataProperty, () => host.Render(Path2D("M 3,3 L 10,0 Z") with { Data = geometry })));
        Assert.Equal("1 from 3,3", MatrixCases.PathSignature(path.Data));

        host.Render(Path2D(geometry));
        Assert.Same(geometry, path.Data);
        host.Render(Path2D());
        Assert.False(path.IsSet(MauiPath.DataProperty));
    }

    // ── Border and ContentView children (§4.4, §4.5) ────────────────────────

    private sealed class Probe : Component<(string Name, List<string> Log)>
    {
        public static readonly Dictionary<string, Action<int>> Set = [];

        public override Element Render()
        {
            var (count, setCount) = UseState(0);
            Set[Props.Name] = setCount;
            UseEffect(() => () => Props.Log.Add($"cleanup {Props.Name}"));
            return Label($"{Props.Name} {count}");
        }
    }

    private static void AssertContentSlot(bool direct, Func<Element?, Element> container, Func<object, View?> content)
    {
        var log = new List<string>();
        var name = $"probe-{direct}-{Guid.NewGuid():N}";
        Element ProbeElement(string suffix = "") => Component<Probe, (string, List<string>)>((name + suffix, log)).WithKey("p");
        var host = Host(direct).Render(container(ProbeElement()));
        var root = host.RootView!;
        var first = content(root);
        Assert.NotNull(first);
        if (direct) Assert.IsType<DirectViewHost>(first);

        Probe.Set[name](5);
        host.Scheduler.Drain();
        host.Render(container(ProbeElement()));
        Assert.Same(first, content(root));
        Assert.Empty(log);

        host.Render(container(Label("other")));
        Assert.Equal([$"cleanup {name}"], log);
        Assert.NotNull(content(root));

        host.Render(container(null));
        Assert.Null(content(root));
        host.Render(container(ProbeElement()));
        Assert.NotNull(content(root));
        Assert.Same(root, host.RootView);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Border_Content_IsSetReplacedAndCleared(bool direct) =>
        AssertContentSlot(direct, c => Border(c), v => ((MauiBorder)v).Content);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContentView_Content_IsSetReplacedAndCleared(bool direct) =>
        AssertContentSlot(direct, c => ContentView(c), v => ((MauiContentView)v).Content);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARefToTheNewElements_GetsTheControlsView(bool direct)
    {
        var content = Microsoft.UI.Reactor.Input.TypedElementRef.Create<MauiContentView>();
        var ellipse = Microsoft.UI.Reactor.Input.TypedElementRef.Create<MauiEllipse>();
        var host = Host(direct).Render(VStack(ContentView(Label("x")).Ref(content), Ellipse().Ref(ellipse)));
        Assert.NotNull(content.Current);
        Assert.NotNull(ellipse.Current);
    }

    // ── Image sources (§4.6) ────────────────────────────────────────────────

    private static ImageSource Font(string glyph = "A", string? family = null, double size = 20, Color? color = null, bool autoScaling = false) =>
        new FontImageSource { Glyph = glyph, FontFamily = family, Size = size, Color = color ?? Colors.Black, FontAutoScalingEnabled = autoScaling };

    public static TheoryData<string, ImageSource?, ImageSource?, bool> ImageRules()
    {
        var shared = ImageSource.FromStream(() => new MemoryStream());
        return new()
        {
            { "both null", null, null, true },
            { "null and a source", null, ImageSource.FromFile("a.png"), false },
            { "files, same path", ImageSource.FromFile("a.png"), ImageSource.FromFile("a.png"), true },
            { "files, other paths", ImageSource.FromFile("a.png"), ImageSource.FromFile("b.png"), false },
            { "URIs, same", ImageSource.FromUri(new Uri("https://x/a.png")), ImageSource.FromUri(new Uri("https://x/a.png")), true },
            { "URIs differing only in user info (Uri's == ignores it)", ImageSource.FromUri(new Uri("https://alice@x/a.png")), ImageSource.FromUri(new Uri("https://bob@x/a.png")), false },
            { "URIs differing only in the fragment", ImageSource.FromUri(new Uri("https://x/a.png#one")), ImageSource.FromUri(new Uri("https://x/a.png#two")), false },
            { "URIs, caching off", ImageSource.FromUri(new Uri("https://x/a.png")), new UriImageSource { Uri = new Uri("https://x/a.png"), CachingEnabled = false }, false },
            { "URIs, another validity", ImageSource.FromUri(new Uri("https://x/a.png")), new UriImageSource { Uri = new Uri("https://x/a.png"), CacheValidity = TimeSpan.FromHours(1) }, false },
            { "fonts, same", Font(), Font(), true },
            { "fonts, another glyph", Font(), Font(glyph: "B"), false },
            { "fonts, another family", Font(), Font(family: "Serif"), false },
            { "fonts, another size", Font(), Font(size: 21), false },
            { "fonts, another color", Font(), Font(color: Colors.Red), false },
            { "fonts, auto scaling", Font(), Font(autoScaling: true), false },
            { "a file and a font", ImageSource.FromFile("A"), Font(), false },
            { "a font source subclass and its base, same values", new DerivedFont { Glyph = "A", Size = 20, Color = Colors.Black }, Font(), false },
            { "stream images, same key", StreamImage("k", () => new MemoryStream()), StreamImage("k", () => new MemoryStream()), true },
            { "stream images, other keys", StreamImage("k", () => new MemoryStream()), StreamImage("j", () => new MemoryStream()), false },
            { "a stream image and an unkeyed stream", StreamImage("k", () => new MemoryStream()), ImageSource.FromStream(() => new MemoryStream()), false },
            { "unkeyed streams, two instances", ImageSource.FromStream(() => new MemoryStream()), ImageSource.FromStream(() => new MemoryStream()), false },
            { "an unkeyed stream, itself", shared, shared, true },
        };
    }

    private sealed class DerivedFont : FontImageSource;

    [Theory]
    [MemberData(nameof(ImageRules))]
    public void ImageSources_FollowTheRules(string name, ImageSource? a, ImageSource? b, bool equal)
    {
        Assert.True(ImageSources.Same(a, b) == equal, name);
        Assert.True(ImageSources.Same(b, a) == equal, name + " (reversed)");
    }

    [Fact]
    public void StreamImage_IsMauisStreamSource_AndEqualityNeverOpensIt()
    {
        var opened = 0;
        var source = StreamImage("avatar:1", () => { opened++; return new MemoryStream(); });
        Assert.IsType<StreamImageSource>(source);
        Assert.Equal("avatar:1", ImageSources.KeyOf(source));
        Assert.True(ImageSources.Same(source, StreamImage("avatar:1", (CancellationToken _) => Task.FromResult<Stream>(new MemoryStream()))));
        Assert.Equal(0, opened);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnEqualImageSource_IsNeverWritten_ByAnyConsumer(bool direct)
    {
        // Image: Controls, or the direct image whose remaps a stub handler counts.
        var host = Host(direct).Render(Image(Font()));
        var view = host.RootView!;
        var sourceWrites = 0;
        if (view is DirectImage directImage)
            directImage.Handler = new StubViewHandler { OnUpdateValue = name => { if (name == nameof(IImageSourcePart.Source)) sourceWrites++; } };
        else
            ((MauiImage)view).PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MauiImage.Source)) sourceWrites++; };
        var installed = ((Microsoft.Maui.IImage)view).Source;
        host.Render(Image(Font()));
        host.Render(Image(StreamImage("k", () => new MemoryStream())));
        Assert.Equal(1, sourceWrites);
        host.Render(Image(StreamImage("k", () => new MemoryStream())));
        Assert.Equal(1, sourceWrites);
        host.Render(Image(StreamImage("j", () => new MemoryStream())));
        Assert.Equal(2, sourceWrites);
        host.Render(Image(Font()));
        host.Render(Image(Font()));
        Assert.Equal(3, sourceWrites);
        Assert.NotSame(installed, ((Microsoft.Maui.IImage)view).Source);

        // ImageButton and a button's image.
        var buttonHost = Host(direct).Render(new ImageButtonElement(Font()));
        var imageButton = (MauiImageButton)buttonHost.RootView!;
        Assert.Equal(0, CountWrites(imageButton, MauiImageButton.SourceProperty, () => buttonHost.Render(new ImageButtonElement(Font()))));
        Assert.Equal(1, CountWrites(imageButton, MauiImageButton.SourceProperty, () => buttonHost.Render(new ImageButtonElement(Font(glyph: "B")))));

        var textButtonHost = Host(direct).Render(Button("b") with { ImageSource = Font() });
        var button = (MauiButton)textButtonHost.RootView!;
        Assert.Equal(0, CountWrites(button, MauiButton.ImageSourceProperty, () => textButtonHost.Render(Button("b") with { ImageSource = Font() })));
        Assert.Equal(1, CountWrites(button, MauiButton.ImageSourceProperty, () => textButtonHost.Render(Button("b") with { ImageSource = Font(glyph: "B") })));
    }

    [Fact]
    public void AnEqualIcon_RebuildsNoToolbarItem_SwipeItem_OrBackButton()
    {
        PageElement Screen(string glyph) => Page("t", Label("x")) with
        {
            Icon = Font(glyph),
            ToolbarItems = [new ToolbarAction("Save", () => { }) { Icon = Font(glyph) }],
            BackButtonBehavior = new ShellBackButton("Back", Font(glyph)),
        };
        var host = Host().Render(Screen("A"));
        var page = (ContentPage)host.RootView!;
        var (icon, item, back) = (page.IconImageSource, page.ToolbarItems[0], Microsoft.Maui.Controls.Shell.GetBackButtonBehavior(page));

        host.Render(Screen("A"));
        Assert.Same(icon, page.IconImageSource);
        Assert.Same(item, page.ToolbarItems[0]);
        Assert.Same(back, Microsoft.Maui.Controls.Shell.GetBackButtonBehavior(page));

        host.Render(Screen("B"));
        Assert.NotSame(icon, page.IconImageSource);
        Assert.NotSame(item, page.ToolbarItems[0]);
        Assert.NotSame(back, Microsoft.Maui.Controls.Shell.GetBackButtonBehavior(page));

        // The back button follows its other values with an equal icon.
        host.Render(Screen("B") with { BackButtonBehavior = new ShellBackButton("Return", Font("B")) });
        Assert.Equal("Return", Microsoft.Maui.Controls.Shell.GetBackButtonBehavior(page).TextOverride);
        host.Render(Screen("B") with { BackButtonBehavior = new ShellBackButton("Return", Font("B"), IsVisible: false) });
        Assert.False(Microsoft.Maui.Controls.Shell.GetBackButtonBehavior(page).IsVisible);
        host.Render(Screen("B") with { BackButtonBehavior = new ShellBackButton("Return", Font("B"), IsVisible: false, IsEnabled: false) });
        Assert.False(Microsoft.Maui.Controls.Shell.GetBackButtonBehavior(page).IsEnabled);

        SwipeViewElement Row(string glyph) => SwipeView(Label("row")) with { RightItems = [new SwipeItemData("Delete") { Icon = Font(glyph) }] };
        var swipeHost = Host().Render(Row("A"));
        var swipe = (MauiSwipeView)swipeHost.RootView!;
        var swipeItem = swipe.RightItems[0];
        swipeHost.Render(Row("A"));
        Assert.Same(swipeItem, swipe.RightItems[0]);
        swipeHost.Render(Row("B"));
        Assert.NotSame(swipeItem, swipe.RightItems[0]);
    }
}

/// <summary>Path markup MAUI rejects (spec 070 §4.3); a collection of its own, since the warnings are process-wide.</summary>
[Collection("PathDataWarnings")]
public class PathDataWarningTests
{
    [Fact]
    public void RejectedMarkup_FallsBackToData_AndWarnsOncePerChange()
    {
        using var warnings = new WarningListener("Path.Data");
        var geometry = new Shapes.EllipseGeometry(new Point(5, 5), 5, 5);
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Path2D("M 0,0 L 10,0 Z") with { Data = geometry });
        var path = (MauiPath)host.RootView!;
        Assert.Empty(warnings.Warnings);

        for (var i = 0; i < 3; i++) host.Render(Path2D("M 0,0 L oops") with { Data = geometry });
        Assert.Same(geometry, path.Data);
        Assert.Single(warnings.Warnings);

        host.Render(Path2D("Q nope"));
        Assert.Equal(2, warnings.Warnings.Count);
        Assert.False(path.IsSet(MauiPath.DataProperty));
    }
}
