using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Maui;
using static Microsoft.UI.Reactor.Maui.Factories;
using static Reactor.Maui.DeviceTests.FixtureContext;
using Shapes = Microsoft.Maui.Controls.Shapes;

namespace Reactor.Maui.DeviceTests;

// Spec 070 §4.10 (4c-3a): shapes, gradients, Border, ContentView and image sources on the platform.
public static partial class Fixtures
{
    private static IEnumerable<Fixture> VisualsFixtures() =>
        Renderers.SelectMany(r => new Fixture[]
        {
            new($"Shapes_Render [{r.Name}]", c => ShapesRender(c, r.Create())),
            new($"Shapes_Aspect [{r.Name}]", c => ShapesAspect(c, r.Create())),
            new($"Shapes_UpdateInPlace [{r.Name}]", c => ShapesUpdateInPlace(c, r.Create())),
            new($"Gradients_Render [{r.Name}]", c => GradientsRender(c, r.Create())),
            new($"Border_Geometry [{r.Name}]", c => BorderGeometry(c, r.Create())),
            new($"ContentView_Geometry [{r.Name}]", c => ContentViewGeometry(c, r.Create())),
            new($"Image_EqualSourceDoesNotReload [{r.Name}]", c => ImageEqualSource(c, r.Create())),
        });

    private static readonly Rgba Clear = new(0, 0, 0, 0);

    /// <summary>The view with <paramref name="id"/>, once it has a platform view and a size, after rendering settles.</summary>
    private static async Task<IView> Sized(FixtureContext c, string id)
    {
        var view = await Loaded<IView>(c, id);
        await c.WaitFor(() => view.Frame.Width > 0 && view.Frame.Height > 0, $"'{id}' to be laid out");
        await c.Settle(250);
        return view;
    }

    // Subjects sit below the top edge: Android lays a layout's children under the status bar otherwise.
    private static Element Stage(params Element?[] children) => VStack(12, children) with { Padding = new Thickness(16, 120, 16, 16) };

    private static async Task ShapesRender(FixtureContext c, ElementHandlerRegistry registry)
    {
        const double W = 80, H = 60;
        T Styled<T>(T shape, string id) where T : ShapeElement =>
            shape.Fill(Colors.Blue).Stroke(Colors.Red, 8).Size(W, H).HorizontalOptions(LayoutOptions.Start).AutomationId(id);

        c.Show(Stage(
            HStack(8,
                Styled(Rectangle(), "rect"),
                Styled(Ellipse(), "ellipse"),
                Styled(RoundRectangle(24), "round")),
            HStack(8,
                Styled(Polygon(new(0, 0), new(80, 0), new(0, 60)), "polygon").Aspect(Stretch.Fill),
                Styled(Path2D("M 0,0 L 80,0 L 0,60 Z"), "path").Aspect(Stretch.Fill)),
            HStack(8,
                Line(0, 30, 80, 30).Stroke(Colors.Red, 8).Size(W, H).HorizontalOptions(LayoutOptions.Start).AutomationId("line"),
                Polyline(new(0, 60), new(40, 0), new(80, 60)).Stroke(Colors.Red, 8).Aspect(Stretch.Fill).Size(W, H).HorizontalOptions(LayoutOptions.Start).AutomationId("polyline")),
            HStack(8,
                Line(0, 0, 80, 40).Stroke(Colors.Red, 2).HorizontalOptions(LayoutOptions.Start).VerticalOptions(LayoutOptions.Start).AutomationId("free-line"),
                Polyline(new(0, 0), new(30, 50), new(60, 0)).Stroke(Colors.Red, 2).HorizontalOptions(LayoutOptions.Start).VerticalOptions(LayoutOptions.Start).AutomationId("free-polyline"))), registry);

        // y = 4 / 60 lies on the top edge's stroke (MAUI insets the path by half the thickness).
        const double topEdge = 4 / H;
        foreach (var id in new[] { "rect", "ellipse", "round" })
        {
            var shot = Snapshot.Capture(await Sized(c, id));
            shot.Expect(0.5, 0.5, Colors.Blue, $"{id} fill");
            shot.Expect(0.5, topEdge, Colors.Red, $"{id} stroke");
        }
        Snapshot.Capture(await Sized(c, "rect")).Expect(0.02, 0.02, Colors.Red, "the rectangle's corner (positive control for the corners below)");
        Snapshot.Capture(await Sized(c, "ellipse")).Expect(0.03, 0.03, Clear, "outside the ellipse");
        Snapshot.Capture(await Sized(c, "round")).Expect(0.02, 0.02, Clear, "outside the rounded corner");

        foreach (var id in new[] { "polygon", "path" })
        {
            var shot = Snapshot.Capture(await Sized(c, id));
            shot.Expect(0.25, 0.25, Colors.Blue, $"{id} fill inside the triangle");
            shot.Expect(0.5, topEdge, Colors.Red, $"{id} stroke on the triangle's top edge");
            shot.Expect(0.85, 0.85, Clear, $"{id} outside the triangle");
        }

        // Open shapes: the stroke only.
        var line = Snapshot.Capture(await Sized(c, "line"));
        line.Expect(0.5, 0.5, Colors.Red, "on the line");
        line.Expect(0.5, 0.15, Clear, "beside the line");
        var polyline = Snapshot.Capture(await Sized(c, "polyline"));
        polyline.Expect(22 / W, 0.5, Colors.Red, "on the polyline's first segment");
        polyline.Expect(0.5, 0.85, Clear, "inside the open polyline (nothing fills it)");

        // Sized from their points when the layout gives no size (MAUI's Shape.MeasureOverride).
        var freeLine = Platform.Size(await Sized(c, "free-line"));
        var freePolyline = Platform.Size(await Sized(c, "free-polyline"));
        Console.WriteLine($"# shapes: unsized line {freeLine}, polyline {freePolyline}");
        Check(freeLine.Width >= 79 && freeLine.Height >= 39, $"the line is sized from its points: {freeLine}");
        Check(freePolyline.Width >= 59 && freePolyline.Height >= 49, $"the polyline is sized from its points: {freePolyline}");
    }

    /// <summary>An asymmetric triangle in a non-square box: where Aspect puts it.</summary>
    private static async Task ShapesAspect(FixtureContext c, ElementHandlerRegistry registry)
    {
        PathElement Triangle(Stretch aspect, string id) =>
            Path2D("M 0,0 L 10,0 L 0,10 Z").Fill(Colors.Black).Aspect(aspect).Size(120, 60).HorizontalOptions(LayoutOptions.Start).AutomationId(id);
        c.Show(Stage(Triangle(Stretch.Uniform, "uniform"), Triangle(Stretch.Fill, "fill"), Triangle(Stretch.None, "none")), registry);
        var black = Rgba.Of(Colors.Black);

        // Uniform: a 60 x 60 triangle centered in 120 x 60 (x from 30 to 90).
        var uniform = Snapshot.Capture(await Sized(c, "uniform"));
        uniform.Expect(0.1, 0.1, Clear, "uniform: left of the centered triangle");
        uniform.Expect(0.3, 0.15, black, "uniform: inside the triangle's top-left corner");
        uniform.Expect(0.7, 0.8, Clear, "uniform: the triangle's empty half");

        // Fill: stretched over the whole box.
        var fill = Snapshot.Capture(await Sized(c, "fill"));
        fill.Expect(0.1, 0.1, black, "fill: the top-left corner");
        fill.Expect(0.9, 0.9, Clear, "fill: the empty bottom-right corner");

        // None: the geometry's own 10 x 10 extent.
        var none = Snapshot.Capture(await Sized(c, "none"));
        none.Expect(0.03, 0.05, black, "none: inside the 10-point triangle");
        none.Expect(0.3, 0.3, Clear, "none: beyond the geometry's extent");
    }

    private sealed class UpdatingShapes : Component
    {
        public static Action<int>? SetStep;

        public override Element Render()
        {
            var (step, setStep) = UseState(0);
            SetStep = setStep;
            var rotate = UseMemo(() => new Shapes.RotateTransform(90, 50, 50));
            return Stage(
                Ellipse().Fill(step == 0 ? Colors.Blue : Colors.Green).Size(80, 60).HorizontalOptions(LayoutOptions.Start).AutomationId("ellipse"),
                (step == 1 ? Rectangle().StrokeDashArray(1, 1) : Rectangle())
                    .Stroke(Colors.Red, 8).Size(160, 40).HorizontalOptions(LayoutOptions.Start).AutomationId("dashed"),
                Path2D("M 0,0 L 100,0 L 0,100 Z").Fill(Colors.Black).Size(100, 100).HorizontalOptions(LayoutOptions.Start)
                    .AutomationId("rotated") with { RenderTransform = step == 1 ? rotate : null });
        }
    }

    private static async Task ShapesUpdateInPlace(FixtureContext c, ElementHandlerRegistry registry)
    {
        c.Show(Component<UpdatingShapes>(), registry);
        var ellipse = await Sized(c, "ellipse");
        var platformEllipse = Platform.View(ellipse);
        Snapshot.Capture(ellipse).Expect(0.5, 0.5, Colors.Blue, "the first fill");

        // Samples along the top edge's outer half: all stroke when solid, stroke and gaps when dashed.
        static (int Stroke, int Other) TopEdge(Snapshot shot)
        {
            var (stroke, other) = (0, 0);
            for (var i = 1; i < 32; i++)
            {
                if (shot.At(i / 32.0, 2 / 40.0).Distance(Rgba.Of(Colors.Red)) <= Snapshot.Tolerance) stroke++;
                else other++;
            }
            return (stroke, other);
        }
        string Pattern(Snapshot s) =>
            $"TL {Dark(s.At(0.15, 0.15))} TR {Dark(s.At(0.85, 0.15))} BL {Dark(s.At(0.15, 0.85))} BR {Dark(s.At(0.85, 0.85))}";
        static string Dark(Rgba p) => p.A > 0.5 && p.R < 0.5 ? "fill" : "empty";

        var solid = TopEdge(Snapshot.Capture(await Sized(c, "dashed")));
        var original = Pattern(Snapshot.Capture(await Sized(c, "rotated")));
        Check(solid.Other == 0, $"a solid stroke covers the edge: {solid}");

        UpdatingShapes.SetStep!(1);
        await c.WaitFor(() => Snapshot.Capture(ellipse).At(0.5, 0.5).Distance(Rgba.Of(Colors.Green)) <= Snapshot.Tolerance, "the new fill to be drawn");
        Check(ReferenceEquals(platformEllipse, Platform.View(c.ById<IView>("ellipse"))), "the ellipse was updated in place");
        await c.Settle(250);
        var dashed = TopEdge(Snapshot.Capture(c.ById<IView>("dashed")));
        var rotated = Pattern(Snapshot.Capture(c.ById<IView>("rotated")));
        Check(dashed.Stroke > 0 && dashed.Other > 0, $"a dashed stroke alternates along the edge: {dashed}");
        Check(rotated != original, $"the rotation is drawn: {original} -> {rotated}");

        UpdatingShapes.SetStep!(2);
        await c.Settle(400);
        var cleared = TopEdge(Snapshot.Capture(c.ById<IView>("dashed")));
        var restored = Pattern(Snapshot.Capture(c.ById<IView>("rotated")));
        Console.WriteLine($"# update in place: dash solid {solid}, dashed {dashed}, cleared {cleared}; path {original} -> {rotated} -> {restored}");
        Check(cleared.Other == 0, $"clearing the dash array makes the stroke solid again: {cleared}");
        Equal(original, restored, "clearing the transform draws the path untransformed (MAUI would keep the rotation)");
    }

    private sealed class Gradients : Component
    {
        public static Action<bool>? SetChanged;

        public override Element Render()
        {
            var (changed, setChanged) = UseState(false);
            SetChanged = setChanged;
            return Stage(
                ContentView(null).Size(100, 100).HorizontalOptions(LayoutOptions.Start).AutomationId("linear")
                    .Background(LinearGradientBrush(new(0, 0), new(0, 1), (changed ? Colors.Lime : Colors.Red, 0), (Colors.Blue, 1))),
                ContentView(null).Size(100, 100).HorizontalOptions(LayoutOptions.Start).AutomationId("radial")
                    .Background(RadialGradientBrush(new(0.25, 0.25), 0.5, (Colors.White, 0), (Colors.Black, 1))),
                Border(null).Stroke(LinearGradientBrush(new(0, 0), new(0, 1), Colors.Red, Colors.Blue), 10).Size(100, 60)
                    .HorizontalOptions(LayoutOptions.Start).AutomationId("stroke"),
                VStack(Label(" ")).Size(100, 40).HorizontalOptions(LayoutOptions.Start).AutomationId("stack")
                    .Background(LinearGradientBrush(new(0, 0), new(1, 0), (Colors.Red, 0), (Colors.Blue, 1))));
        }
    }

    private static async Task GradientsRender(FixtureContext c, ElementHandlerRegistry registry)
    {
        c.Show(Component<Gradients>(), registry);
        var linear = Snapshot.Capture(await Sized(c, "linear"));
        var (red, blue) = (Rgba.Of(Colors.Red), Rgba.Of(Colors.Blue));
        Console.WriteLine($"# gradients: linear top {linear.At(0.5, 0.02)}, middle {linear.At(0.5, 0.5)}, bottom {linear.At(0.5, 0.98)}");
        linear.Expect(0.5, 0.02, red, "the linear gradient's start");
        linear.Expect(0.5, 0.98, blue, "the linear gradient's end");
        linear.Expect(0.5, 0.5, red.Mix(blue, 0.5), "halfway", tolerance: 0.12);

        var radial = Snapshot.Capture(await Sized(c, "radial"));
        Console.WriteLine($"# gradients: radial center {radial.At(0.25, 0.25)}, far corner {radial.At(0.98, 0.98)}, near the center {radial.At(0.35, 0.25)}");
        radial.Expect(0.25, 0.25, Colors.White, "the radial gradient's center (0.25, 0.25)");
        radial.Expect(0.98, 0.98, Colors.Black, "beyond the radius");

        var stroke = Snapshot.Capture(await Sized(c, "stroke"));
        var (top, bottom) = (stroke.At(0.5, 0.05), stroke.At(0.5, 0.95));
        Console.WriteLine($"# gradients: border stroke top {top}, bottom {bottom}");
        Check(top.R > top.B + 0.3 && bottom.B > bottom.R + 0.3, $"the border's gradient stroke runs from red to blue: top {top}, bottom {bottom}");

        var stack = Snapshot.Capture(await Sized(c, "stack"));
        stack.Expect(0.02, 0.5, red, "a stack's horizontal gradient, left");
        stack.Expect(0.98, 0.5, blue, "a stack's horizontal gradient, right");

        Gradients.SetChanged!(true);
        await c.WaitFor(() => Snapshot.Capture(c.ById<IView>("linear")).At(0.5, 0.02).Distance(Rgba.Of(Colors.Lime)) <= Snapshot.Tolerance,
            "the changed first stop to be drawn");
    }

    private static async Task BorderGeometry(FixtureContext c, ElementHandlerRegistry registry)
    {
        BorderElement Box(string id, double? radius)
        {
            var border = Border(BoxView(Colors.Blue).AutomationId(id + "-content")).Stroke(Colors.Red, 6).Padding(10).Size(120, 120)
                .HorizontalOptions(LayoutOptions.Start).AutomationId(id);
            return radius is { } r ? border.CornerRadius(r) : border;
        }
        c.Show(Stage(Box("square", null), Box("rounded", 30)), registry);
        var border = await Sized(c, "square");
        var content = await Sized(c, "square-content");
        var (outer, inner) = (Platform.WindowFrame(border), Platform.WindowFrame(content));
        Console.WriteLine($"# border: {outer} content {inner}");
        foreach (var (inset, side) in new[] { (inner.X - outer.X, "left"), (inner.Y - outer.Y, "top"), (outer.Right - inner.Right, "right"), (outer.Bottom - inner.Bottom, "bottom") })
            Close(16, inset, $"the content's {side} inset (padding 10 + stroke 6)");

        Snapshot.Capture(border).Expect(0.01, 0.01, Colors.Red, "a square corner is stroke (positive control)");
        var rounded = Snapshot.Capture(await Sized(c, "rounded"));
        rounded.Expect(0.01, 0.01, Clear, "outside the rounded corner");
        rounded.Expect(0.5, 0.01, Colors.Red, "the rounded border's top edge");
    }

    private static async Task ContentViewGeometry(FixtureContext c, ElementHandlerRegistry registry)
    {
        c.Show(Stage(ContentView(Label("content").AutomationId("inner")).Padding(12).Size(160, 80)
            .HorizontalOptions(LayoutOptions.Start).AutomationId("outer")), registry);
        var outer = Platform.WindowFrame(await Sized(c, "outer"));
        var inner = Platform.WindowFrame(await Sized(c, "inner"));
        Console.WriteLine($"# content view: {outer} content {inner} ({c.ById<IView>("inner").GetType().Name})");
        Close(12, inner.X - outer.X, "the content's left inset");
        Close(12, inner.Y - outer.Y, "the content's top inset");
        Equal("content", Platform.Text(c.ById<IView>("inner")), "the content is realized");
    }

    private sealed class ImageScreen : Component<(string Key, string Glyph, int Renders, Func<Stream> Open)>
    {
        public override Element Render() => Stage(
            Image(StreamImage(Props.Key, Props.Open)).Size(48, 24).HorizontalOptions(LayoutOptions.Start).AutomationId("stream"),
            Image(new FontImageSource { Glyph = Props.Glyph, Size = 30, Color = Colors.Black }).Size(40, 40).HorizontalOptions(LayoutOptions.Start).AutomationId("font"),
            new ImageButtonElement(StreamImage(Props.Key, Props.Open)).Size(48, 24).HorizontalOptions(LayoutOptions.Start).AutomationId("button"),
            Label($"render {Props.Renders}"));
    }

    private static async Task ImageEqualSource(FixtureContext c, ElementHandlerRegistry registry)
    {
        var pngs = new Dictionary<string, byte[]> { ["red"] = TestPng.Solid(24, 12, 220, 20, 20), ["green"] = TestPng.Solid(30, 10, 20, 200, 20) };
        var opens = 0;
        var key = "red";
        Element Screen(string glyph, int renders) =>
            Component<ImageScreen, (string, string, int, Func<Stream>)>((key, glyph, renders, () => { Interlocked.Increment(ref opens); return new MemoryStream(pngs[key]); }));

        c.Show(Screen("A", 0), registry);
        var stream = await Sized(c, "stream");
        var font = await Sized(c, "font");
        var button = await Sized(c, "button");
        await c.WaitFor(() => VisualsPlatform.PlatformImageSize(stream) == (24, 12) && VisualsPlatform.PlatformImageSize(button) == (24, 12),
            "both stream images to decode");
        await c.WaitFor(() => VisualsPlatform.PlatformImage(font) is not null, "the font image");
        await c.Settle();
        var atMount = Volatile.Read(ref opens);
        var (streamImage, fontImage, buttonImage) = (VisualsPlatform.PlatformImage(stream), VisualsPlatform.PlatformImage(font), VisualsPlatform.PlatformImage(button));
        Console.WriteLine($"# images: opened {atMount} at mount, decoded {VisualsPlatform.PlatformImageSize(stream)}, font image {VisualsPlatform.PlatformImageSize(font)}");
        Check(atMount > 0, "the streams were opened (positive witness)");
        Check(VisualsPlatform.PlatformImageSize(font).Width > 0, "the font image was drawn (positive witness)");

        for (var i = 1; i <= 3; i++) c.Render(Screen("A", i));
        await c.WaitFor(() => Platform.Text(c.Find<ILabel>(l => l.Text == "render 3")) == "render 3", "the third re-render");
        await c.Settle();
        Equal(atMount, Volatile.Read(ref opens), "opens after three re-renders with equal sources");
        Check(ReferenceEquals(streamImage, VisualsPlatform.PlatformImage(stream)), "the stream image wasn't reloaded");
        Check(ReferenceEquals(buttonImage, VisualsPlatform.PlatformImage(button)), "the image button's image wasn't reloaded");
        Check(ReferenceEquals(fontImage, VisualsPlatform.PlatformImage(font)), "the font image wasn't reloaded");

        // Positive controls: a new key and a new glyph are new images.
        key = "green";
        c.Render(Screen("B", 4));
        await c.WaitFor(() => Volatile.Read(ref opens) > atMount, "the new key's stream to be opened");
        await c.WaitFor(() => VisualsPlatform.PlatformImageSize(stream) == (30, 10), "the new key's image to decode");
        await c.WaitFor(() => !ReferenceEquals(fontImage, VisualsPlatform.PlatformImage(font)) && VisualsPlatform.PlatformImage(font) is not null, "the new glyph's image");
        await c.WaitFor(() => VisualsPlatform.PlatformImageSize(button) == (30, 10), "the image button's new image");
    }
}
