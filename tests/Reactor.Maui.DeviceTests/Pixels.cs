namespace Reactor.Maui.DeviceTests;

/// <summary>An RGBA color read from rendered pixels, each channel 0 to 1.</summary>
public readonly record struct Rgba(double R, double G, double B, double A)
{
    public static Rgba Of(Color color) => new(color.Red, color.Green, color.Blue, color.Alpha);

    /// <summary>The largest per-channel difference.</summary>
    public double Distance(Rgba other) =>
        Math.Max(Math.Max(Math.Abs(R - other.R), Math.Abs(G - other.G)), Math.Max(Math.Abs(B - other.B), Math.Abs(A - other.A)));

    public Rgba Mix(Rgba other, double t) =>
        new(R + (other.R - R) * t, G + (other.G - G) * t, B + (other.B - B) * t, A + (other.A - A) * t);

    public override string ToString() => $"rgba({R:0.000}, {G:0.000}, {B:0.000}, {A:0.000})";
}

/// <summary>
/// A view's rendered pixels (spec 070 §4.10): iOS and Mac Catalyst draw the view hierarchy into an
/// sRGB bitmap; Android draws the view into a bitmap. Samples are taken at normalized coordinates,
/// so the same oracle works at every density.
/// </summary>
public sealed class Snapshot
{
    private readonly byte[] _rgba;

    private Snapshot(byte[] rgba, int width, int height, string source)
    {
        _rgba = rgba;
        Width = width;
        Height = height;
        Source = source;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>How the pixels were captured (logged with every failure).</summary>
    public string Source { get; }

    /// <summary>The color at (x, y), each in 0 to 1 of the view's width and height.</summary>
    public Rgba At(double x, double y)
    {
        var px = Math.Clamp((int)Math.Round(x * (Width - 1)), 0, Width - 1);
        var py = Math.Clamp((int)Math.Round(y * (Height - 1)), 0, Height - 1);
        var i = (py * Width + px) * 4;
        double a = _rgba[i + 3] / 255.0;
        // Premultiplied: undo it, so a translucent sample compares with its declared color.
        return a == 0
            ? new Rgba(0, 0, 0, 0)
            : new Rgba(_rgba[i] / 255.0 / a, _rgba[i + 1] / 255.0 / a, _rgba[i + 2] / 255.0 / a, a);
    }

    public const double Tolerance = 0.06;

    /// <summary>Fails unless the sample at (x, y) is within <see cref="Tolerance"/> of <paramref name="expected"/>.</summary>
    public void Expect(double x, double y, Rgba expected, string what, double tolerance = Tolerance)
    {
        var actual = At(x, y);
        if (actual.Distance(expected) > tolerance)
            throw new CheckFailedException($"{what} at ({x:0.###}, {y:0.###}): expected {expected} ± {tolerance}, got {actual} ({Describe()})");
    }

    public void Expect(double x, double y, Color expected, string what, double tolerance = Tolerance) =>
        Expect(x, y, Rgba.Of(expected), what, tolerance);

    public string Describe() => $"{Width}x{Height} px, {Source}";

    public static Snapshot Capture(IView view)
    {
        var native = view.Handler?.PlatformView ?? throw new CheckFailedException($"{view.GetType().Name} has no platform view");
#if IOS || MACCATALYST
        var uiView = (UIKit.UIView)native;
        var scale = (double)(uiView.Window?.Screen?.Scale ?? UIKit.UIScreen.MainScreen.Scale);
        var width = Math.Max(1, (int)Math.Round(uiView.Bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Round(uiView.Bounds.Height * scale));
        var bytes = new byte[width * height * 4];
        using var space = CoreGraphics.CGColorSpace.CreateSrgb();
        using (var context = new CoreGraphics.CGBitmapContext(bytes, width, height, 8, width * 4, space, CoreGraphics.CGImageAlphaInfo.PremultipliedLast))
        {
            // UIKit draws top-down: flip, so memory row 0 is the view's top.
            context.TranslateCTM(0, height);
            context.ScaleCTM((nfloat)scale, (nfloat)(-scale));
            UIKit.UIGraphics.PushContext(context);
            var drawn = uiView.DrawViewHierarchy(uiView.Bounds, afterScreenUpdates: true);
            UIKit.UIGraphics.PopContext();
            if (!drawn)
            {
                uiView.Layer.RenderInContext(context);
                return new Snapshot(bytes, width, height, "layer rendered into sRGB (drawViewHierarchy refused)");
            }
        }
        return new Snapshot(bytes, width, height, $"view hierarchy drawn into sRGB at scale {scale}");
#elif ANDROID
        var androidView = (Android.Views.View)native;
        var width = Math.Max(1, androidView.Width);
        var height = Math.Max(1, androidView.Height);
        using var bitmap = Android.Graphics.Bitmap.CreateBitmap(width, height, Android.Graphics.Bitmap.Config.Argb8888!)!;
        using (var canvas = new Android.Graphics.Canvas(bitmap)) androidView.Draw(canvas);
        var pixels = new int[width * height];
        bitmap.GetPixels(pixels, 0, width, 0, 0, width, height);
        var bytes = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i++)
        {
            // GetPixels returns unpremultiplied ARGB; store premultiplied RGBA, as UIKit's capture is.
            var argb = pixels[i];
            var a = (argb >> 24) & 0xFF;
            bytes[i * 4] = (byte)(((argb >> 16) & 0xFF) * a / 255);
            bytes[i * 4 + 1] = (byte)(((argb >> 8) & 0xFF) * a / 255);
            bytes[i * 4 + 2] = (byte)((argb & 0xFF) * a / 255);
            bytes[i * 4 + 3] = (byte)a;
        }
        return new Snapshot(bytes, width, height, "View.Draw into an ARGB_8888 bitmap");
#else
        throw new SkipException("no pixel capture on this platform");
#endif
    }
}
