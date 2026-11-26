using MauiImage = Microsoft.Maui.Controls.Image;
using Microsoft.Maui.Controls.Shapes;
using MauiPath = Microsoft.Maui.Controls.Shapes.Path;
using static Reactor.Maui.DeviceTests.FixtureContext;

namespace Reactor.Maui.DeviceTests;

/// <summary>Platform readback for the spec 070 §4 fixtures (shapes, brushes, images).</summary>
public static class VisualsPlatform
{
    /// <summary>The decoded platform image an Image or ImageButton shows, or null.</summary>
    public static object? PlatformImage(IView view) => view.Handler?.PlatformView switch
    {
#if IOS || MACCATALYST
        UIKit.UIImageView imageView => imageView.Image,
        UIKit.UIButton button => button.CurrentImage ?? button.ImageView?.Image,
#elif ANDROID
        Android.Widget.ImageView imageView => imageView.Drawable,
#endif
        _ => null,
    };

    /// <summary>The decoded image's size in pixels (iOS: points times scale), or (0, 0).</summary>
    public static (int Width, int Height) PlatformImageSize(IView view) => PlatformImage(view) switch
    {
#if IOS || MACCATALYST
        UIKit.UIImage image => ((int)Math.Round(image.Size.Width * image.CurrentScale), (int)Math.Round(image.Size.Height * image.CurrentScale)),
#elif ANDROID
        Android.Graphics.Drawables.Drawable drawable => (drawable.IntrinsicWidth, drawable.IntrinsicHeight),
#endif
        _ => (0, 0),
    };
}

public static partial class Fixtures
{
    // Characterizations of plain MAUI for spec 070 §4 (4c-3a): no Reactor code runs in them.
    private static IEnumerable<Fixture> VisualsCharacterizationFixtures() =>
    [
        new("Gate_Pixels_Sampler", PixelsSampler),
        new("Characterize_Path_RenderTransformClear", PathRenderTransformClear),
        new("Characterize_StreamImage_LoadCount", StreamImageLoadCount),
        new("Characterize_Border_ContentInset", BorderContentInset),
    ];

    private static async Task<T> NativeLoaded<T>(FixtureContext c, T view) where T : View
    {
        await c.WaitFor(() => view.Handler?.PlatformView is not null && view.Width > 0 && view.Height > 0, $"the {typeof(T).Name} to be laid out");
        await c.Settle(200);
        return view;
    }

    /// <summary>
    /// The instrument every pixel oracle uses: a known two-color box reads back as those colors at the
    /// right places (orientation), the page background reads outside it, and a changed color is seen.
    /// </summary>
    private static async Task PixelsSampler(FixtureContext c)
    {
        var top = new BoxView { Color = Colors.Red };
        var bottom = new BoxView { Color = Colors.Blue };
        var grid = new Grid
        {
            RowDefinitions = [new RowDefinition(40), new RowDefinition(40)],
            WidthRequest = 120,
            HeightRequest = 120,
            Padding = new Thickness(20),
            BackgroundColor = Colors.White,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            // Android lays a layout's children below the status bar when the layout touches it.
            SafeAreaEdges = SafeAreaEdges.None,
        };
        grid.Add(top, 0, 0);
        grid.Add(bottom, 0, 1);
        c.ShowNative(grid);
        await NativeLoaded(c, grid);

        var shot = Snapshot.Capture(grid);
        Console.WriteLine($"# frames: grid {Platform.WindowFrame(grid)} top {Platform.WindowFrame(top)} bottom {Platform.WindowFrame(bottom)} native {(grid.Handler?.PlatformView)?.GetType().Name}");
        Console.WriteLine($"# pixels: {shot.Describe()}; top {shot.At(0.5, 0.3)}, bottom {shot.At(0.5, 0.7)}, margin {shot.At(0.05, 0.05)}");
        shot.Expect(0.5, 0.3, Colors.Red, "top half");
        shot.Expect(0.5, 0.7, Colors.Blue, "bottom half");
        shot.Expect(0.05, 0.05, Colors.White, "padding (outside the boxes)");
        shot.Expect(0.05, 0.95, Colors.White, "padding, bottom left");

        top.Color = Colors.Lime;
        await c.Settle(200);
        var after = Snapshot.Capture(grid);
        after.Expect(0.5, 0.3, Colors.Lime, "top half after its color changed");
        after.Expect(0.5, 0.7, Colors.Blue, "bottom half, unchanged");
    }

    // An asymmetric right triangle: filled in the top-left half of its box.
    private const string TrianglePath = "M 0,0 L 100,0 L 0,100 Z";

    /// <summary>
    /// Spec 070 §4.3: MAUI's PathHandler.MapRenderTransform updates the drawable only for a non-null
    /// transform. Records what clearing shows, and whether writing an identity transform first fixes it.
    /// </summary>
    private static async Task PathRenderTransformClear(FixtureContext c)
    {
        MauiPath NewPath() => new()
        {
            Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(TrianglePath)!,
            Fill = Colors.Black,
            WidthRequest = 100,
            HeightRequest = 100,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
        };
        string Pattern(Snapshot s) =>
            $"TL {Dark(s.At(0.15, 0.15))} TR {Dark(s.At(0.85, 0.15))} BL {Dark(s.At(0.15, 0.85))} BR {Dark(s.At(0.85, 0.85))}";
        static string Dark(Rgba p) => p.A > 0.5 && p.R < 0.5 ? "fill" : "empty";

        var path = NewPath();
        var host = new Grid { WidthRequest = 100, HeightRequest = 100, BackgroundColor = Colors.White, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start, SafeAreaEdges = SafeAreaEdges.None };
        host.Add(path);
        c.ShowNative(host);
        await NativeLoaded(c, path);
        var original = Pattern(Snapshot.Capture(host));
        Console.WriteLine($"# path frames: host {Platform.WindowFrame(host)} path {Platform.WindowFrame(path)}");

        path.RenderTransform = new RotateTransform(90, 50, 50);
        await c.Settle(200);
        var rotated = Pattern(Snapshot.Capture(host));

        path.ClearValue(MauiPath.RenderTransformProperty);
        await c.Settle(200);
        var plainClear = Pattern(Snapshot.Capture(host));

        path.RenderTransform = new RotateTransform(90, 50, 50);
        await c.Settle(200);
        path.RenderTransform = new MatrixTransform();
        path.ClearValue(MauiPath.RenderTransformProperty);
        await c.Settle(200);
        var identityThenClear = Pattern(Snapshot.Capture(host));

        Console.WriteLine($"# path transform: original [{original}], rotated [{rotated}], plain clear [{plainClear}], identity then clear [{identityThenClear}]");
        Check(original != rotated, $"the rotation changes the drawing (positive control): {original} vs {rotated}");
        Equal(original, identityThenClear, "identity transform then clear restores the original drawing");
    }

    /// <summary>Spec 070 §4.6: how many times one mount of a stream source calls its delegate.</summary>
    private static async Task StreamImageLoadCount(FixtureContext c)
    {
        var png = TestPng.Solid(24, 12, 200, 30, 30);
        var opens = 0;
        var image = new MauiImage
        {
            Source = ImageSource.FromStream(() => { Interlocked.Increment(ref opens); return new MemoryStream(png); }),
            WidthRequest = 48,
            HeightRequest = 24,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
        };
        c.ShowNative(image);
        await NativeLoaded(c, image);
        await c.WaitFor(() => VisualsPlatform.PlatformImage(image) is not null, "the decoded image");
        await c.Settle();
        var atMount = Volatile.Read(ref opens);
        var size = VisualsPlatform.PlatformImageSize(image);
        Console.WriteLine($"# stream image: opened {atMount} time(s) at mount; decoded {size.Width}x{size.Height}");
        Check(atMount > 0, "the stream was opened");
        Check(size.Width > 0 && size.Height > 0, "the platform image was decoded");

        var first = VisualsPlatform.PlatformImage(image);
        image.Source = ImageSource.FromStream(() => { Interlocked.Increment(ref opens); return new MemoryStream(png); });
        await c.WaitFor(() => Volatile.Read(ref opens) > atMount, "a new source instance to be opened (positive control)");
        await c.Settle();
        Console.WriteLine($"# stream image: a new instance opened {Volatile.Read(ref opens) - atMount} more; new platform image {!ReferenceEquals(first, VisualsPlatform.PlatformImage(image))}");
    }

    /// <summary>Spec 070 §4.10: where MAUI places a Border's content (padding and stroke).</summary>
    private static async Task BorderContentInset(FixtureContext c)
    {
        var content = new BoxView { Color = Colors.Blue };
        var border = new Border
        {
            Stroke = Colors.Red,
            StrokeThickness = 6,
            Padding = new Thickness(10),
            Content = content,
            WidthRequest = 120,
            HeightRequest = 120,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
        };
        c.ShowNative(border);
        await NativeLoaded(c, content);
        var outer = Platform.WindowFrame(border);
        var inner = Platform.WindowFrame(content);
        Console.WriteLine($"# border: frame {outer}, content {inner}; inset left {inner.X - outer.X:0.##}, top {inner.Y - outer.Y:0.##}, right {outer.Right - inner.Right:0.##}, bottom {outer.Bottom - inner.Bottom:0.##}");
        var shot = Snapshot.Capture(border);
        Console.WriteLine($"# border: stroke sample {shot.At(0.5, 0.01)}, padding sample {shot.At(0.5, 0.09)}, content {shot.At(0.5, 0.5)}");
        Check(inner.X > outer.X && inner.Width < outer.Width, "the content is inset");
    }
}
