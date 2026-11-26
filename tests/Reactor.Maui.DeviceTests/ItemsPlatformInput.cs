using System.Reflection;
using MauiRadioButton = Microsoft.Maui.Controls.RadioButton;
using MauiSwipeView = Microsoft.Maui.Controls.SwipeView;

namespace Reactor.Maui.DeviceTests;

/// <summary>Input and readback for the spec 070 §3 behavior fixtures.</summary>
public static partial class ItemsPlatform
{
#if ANDROID
    private static double Density(Android.Views.View view) => view.Context?.Resources?.DisplayMetrics?.Density ?? 1;

    private static Android.Views.View? ItemView(IView list, int position) =>
        Recycler(list)?.FindViewHolderForAdapterPosition(position)?.ItemView;
#endif

    /// <summary>
    /// Selects the item at <paramref name="index"/> the way a tap does: iOS and Mac Catalyst select the
    /// native item and call the delegate's ItemSelected; Android clicks the item view.
    /// </summary>
    public static bool TapItem(IView list, int index)
    {
#if IOS || MACCATALYST
        if (CollectionView(list) is not { } native) return false;
        var path = Foundation.NSIndexPath.FromItemSection(index, 0);
        native.SelectItem(path, false, UIKit.UICollectionViewScrollPosition.None);
        native.Delegate?.ItemSelected(native, path);
        return true;
#elif ANDROID
        return ItemView(list, index)?.PerformClick() ?? false;
#else
        return false;
#endif
    }

    /// <summary>Whether a view is on screen: in a window and not hidden (a removed cell's view is not).</summary>
    public static bool IsOnScreen(IView view)
    {
#if IOS || MACCATALYST
        if (view.Handler?.PlatformView is not UIKit.UIView native || native.Window is null) return false;
        for (var v = native; v is not null; v = v.Superview)
            if (v.Hidden || v.Alpha == 0) return false;
        return native.Frame.Height > 0;
#elif ANDROID
        return view.Handler?.PlatformView is Android.Views.View { IsShown: true, IsAttachedToWindow: true, Height: > 0 };
#else
        return false;
#endif
    }

    /// <summary>Whether the platform draws its own selection highlight on the item at <paramref name="index"/>.</summary>
    public static string Highlight(IView list, int index)
    {
#if IOS || MACCATALYST
        if (CollectionView(list)?.CellForItem(Foundation.NSIndexPath.FromItemSection(index, 0)) is not { } cell) return "no cell";
        var color = cell.SelectedBackgroundView?.BackgroundColor;
        if (color is null) return "none";
        color.GetRGBA(out _, out _, out _, out var alpha);
        return alpha > 0 ? "shown" : "none";
#elif ANDROID
        if (ItemView(list, index) is not { } view) return "no cell";
        return view.Background is null ? "none" : "shown";
#else
        return "unsupported";
#endif
    }

    /// <summary>The content's x offset in its swipe view's platform coordinates, in device-independent units.</summary>
    public static double ContentOffsetX(MauiSwipeView swipe)
    {
        if (swipe.Content?.Handler?.PlatformView is not { } content) return double.NaN;
#if IOS || MACCATALYST
        var view = (UIKit.UIView)content;
        // The content may sit in a wrapper; its frame in the swipe view is what moves.
        return view.ConvertRectToView(view.Bounds, (UIKit.UIView)swipe.Handler!.PlatformView!).X;
#elif ANDROID
        var view = (Android.Views.View)content;
        var parent = (Android.Views.View)swipe.Handler!.PlatformView!;
        var a = new int[2];
        var b = new int[2];
        view.GetLocationInWindow(a);
        parent.GetLocationInWindow(b);
        return (a[0] - b[0]) / Density(view);
#else
        return double.NaN;
#endif
    }

    /// <summary>
    /// Drags a swipe view's content horizontally by <paramref name="dx"/> and lifts, through MAUI's own
    /// touch processing: Android's touch events, iOS and Mac Catalyst's pan handling (reflected, as a
    /// pan recognizer can't be driven in process).
    /// </summary>
    public static async Task Swipe(MauiSwipeView swipe, double dx)
    {
        var native = swipe.Handler?.PlatformView ?? throw new CheckFailedException("no platform swipe view");
#if IOS || MACCATALYST
        var handle = native.GetType().GetMethod("HandleTouchInteractions", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new CheckFailedException("MauiSwipeView.HandleTouchInteractions not found");
        var view = (UIKit.UIView)native;
        var y = view.Bounds.Height / 2;
        var x = view.Bounds.Width / 2;
        handle.Invoke(native, [GestureStatus.Started, new CoreGraphics.CGPoint(x, y)]);
        const int steps = 8;
        for (var i = 1; i <= steps; i++)
        {
            handle.Invoke(native, [GestureStatus.Running, new CoreGraphics.CGPoint(x + dx * i / steps, y)]);
            await Task.Delay(16);
        }
        handle.Invoke(native, [GestureStatus.Completed, new CoreGraphics.CGPoint(x + dx, y)]);
#elif ANDROID
        var view = (Android.Views.View)native;
        var density = Density(view);
        var y = view.Height / 2f;
        var x = view.Width / 2f;
        var down = Android.OS.SystemClock.UptimeMillis();
        void Send(Android.Views.MotionEventActions action, float px)
        {
            using var e = Android.Views.MotionEvent.Obtain(down, Android.OS.SystemClock.UptimeMillis(), action, px, y, 0)!;
            view.DispatchTouchEvent(e);
        }
        Send(Android.Views.MotionEventActions.Down, x);
        const int steps = 8;
        for (var i = 1; i <= steps; i++)
        {
            await Task.Delay(16);
            Send(Android.Views.MotionEventActions.Move, x + (float)(dx * density) * i / steps);
        }
        Send(Android.Views.MotionEventActions.Up, x + (float)(dx * density));
#else
        await Task.CompletedTask;
#endif
    }

    /// <summary>Taps a radio button: Android's native button is clicked; a templated one is tapped through MAUI's recognizer.</summary>
    public static void TapRadio(MauiRadioButton radio)
    {
#if ANDROID
        if (radio.Handler?.PlatformView is Android.Widget.CompoundButton native)
        {
            native.PerformClick();
            return;
        }
#endif
        var recognizer = radio.GestureRecognizers.OfType<TapGestureRecognizer>().FirstOrDefault()
            ?? throw new CheckFailedException("the templated radio button has no tap recognizer");
        recognizer.SendTapped(radio);
    }

    /// <summary>What the platform's accessibility exposes for a view: its name, role and checked state.</summary>
    public static string Accessibility(IView view)
    {
#if IOS || MACCATALYST
        if (view.Handler?.PlatformView is not UIKit.UIView native) return "<no view>";
        return $"label={native.AccessibilityLabel ?? "-"}; traits={native.AccessibilityTraits}; value={native.AccessibilityValue ?? "-"}; element={native.IsAccessibilityElement}";
#elif ANDROID
        if (view.Handler?.PlatformView is not Android.Views.View native) return "<no view>";
#pragma warning disable CA1422 // the obsolete APIs are what API 24 has
        var info = Android.Views.Accessibility.AccessibilityNodeInfo.Obtain()!;
        native.OnInitializeAccessibilityNodeInfo(info);
        var result = $"label={info.ContentDescription ?? info.Text ?? "-"}; class={info.ClassName}; checkable={info.Checkable}; checked={info.Checked}";
#pragma warning restore CA1422
        return result;
#else
        return "unsupported";
#endif
    }

    /// <summary>The attributed runs a label shows: per range, its text, color, size and decorations.</summary>
    public static string Runs(IView label)
    {
#if IOS || MACCATALYST
        if (label.Handler?.PlatformView is not UIKit.UILabel native || native.AttributedText is not { } text) return "<none>";
        var runs = new List<string>();
        text.EnumerateAttributes(new Foundation.NSRange(0, text.Length), Foundation.NSAttributedStringEnumeration.None,
            (Foundation.NSDictionary attrs, Foundation.NSRange range, ref bool stop) =>
            {
                var part = text.Substring(range.Location, range.Length).Value;
                var font = attrs[UIKit.UIStringAttributeKey.Font] as UIKit.UIFont;
                var underline = attrs[UIKit.UIStringAttributeKey.UnderlineStyle] is Foundation.NSNumber u && u.Int32Value != 0;
                var color = attrs[UIKit.UIStringAttributeKey.ForegroundColor] as UIKit.UIColor;
                var hex = "-";
                if (color is not null)
                {
                    color.GetRGBA(out var r, out var g, out var b, out _);
                    hex = $"{(int)(r * 255):X2}{(int)(g * 255):X2}{(int)(b * 255):X2}";
                }
                runs.Add($"'{part}' size={font?.PointSize ?? 0:0} color={hex}{(underline ? " underline" : "")}");
            });
        return string.Join(" | ", runs);
#elif ANDROID
        if (label.Handler?.PlatformView is not Android.Widget.TextView native || native.TextFormatted is not Android.Text.ISpanned text) return "<none>";
        var density = Density(native);
        var runs = new List<string>();
        var length = text.Length();
        var at = 0;
        while (at < length)
        {
            var next = text.NextSpanTransition(at, length, Java.Lang.Class.FromType(typeof(Java.Lang.Object)));
            var spans = text.GetSpans(at, next, Java.Lang.Class.FromType(typeof(Java.Lang.Object))) ?? [];
            var color = spans.OfType<Android.Text.Style.ForegroundColorSpan>().LastOrDefault();
            var underline = spans.OfType<Android.Text.Style.UnderlineSpan>().Any();
            var hex = color is null ? "-" : $"{color.ForegroundColor & 0xFFFFFF:X6}";
            // MAUI's own span types set the size: apply every metric span to a paint and read it back.
            using var paint = new Android.Text.TextPaint(native.Paint);
            foreach (var metric in spans.OfType<Android.Text.Style.MetricAffectingSpan>()) metric.UpdateMeasureState(paint);
            var scale = native.Resources?.Configuration?.FontScale ?? 1;
            var points = paint.TextSize / density / scale;
            runs.Add($"'{text.ToString()!.Substring(at, next - at)}' size={points:0} color={hex}{(underline ? " underline" : "")}");
            at = next;
        }
        return string.Join(" | ", runs);
#else
        return "unsupported";
#endif
    }

    /// <summary>A point inside a label where MAUI's span hit test returns <paramref name="target"/>, or null.</summary>
    public static Point? PointOn(Microsoft.Maui.Controls.Label label, Span target)
    {
        // The center of the points that hit-test to the span, so a tap lands well inside it.
        // MAUI computes span regions in the platform's units: Android pixels (its tap handler passes the
        // touch's pixel position to GetChildElements), UIKit points.
#if ANDROID
        var scale = Density((Android.Views.View)label.Handler!.PlatformView!);
#else
        var scale = 1.0;
#endif
        var hits = new List<Point>();
        for (var y = 1.0; y < label.Height * scale; y += 2)
            for (var x = 1.0; x < label.Width * scale; x += 2)
                if (label.GetChildElements(new Point(x, y)).Contains(target)) hits.Add(new Point(x, y));
        if (hits.Count == 0) return null;
        var center = new Point(hits.Average(p => p.X), hits.Average(p => p.Y));
        return hits.MinBy(p => Math.Abs(p.X - center.X) + Math.Abs(p.Y - center.Y));
    }

    /// <summary>
    /// Taps a label at <paramref name="point"/>: Android dispatches the touch to the native view (MAUI's
    /// gesture handling then hit-tests the spans); iOS and Mac Catalyst run MAUI's tap dispatch for each
    /// recognizer installed on the label.
    /// </summary>
    public static async Task TapLabel(Microsoft.Maui.Controls.Label label, Point point)
    {
#if ANDROID
        // Through the window's root, in window coordinates, as a real touch arrives.
        var native = (Android.Views.View)label.Handler!.PlatformView!;
        var at = new int[2];
        native.GetLocationInWindow(at);
        // The point is in MAUI's span-region units, Android pixels.
        var x = at[0] + (float)point.X;
        var y = at[1] + (float)point.Y;
        var root = native.RootView!;
        var down = Android.OS.SystemClock.UptimeMillis();
        using (var e = Android.Views.MotionEvent.Obtain(down, down, Android.Views.MotionEventActions.Down, x, y, 0)!)
            root.DispatchTouchEvent(e);
        await Task.Delay(30);
        using (var e = Android.Views.MotionEvent.Obtain(down, Android.OS.SystemClock.UptimeMillis(), Android.Views.MotionEventActions.Up, x, y, 0)!)
            root.DispatchTouchEvent(e);
#else
        // MAUI's own tap dispatch, over every recognizer its gesture manager installed on the label (the
        // label's own and the spans' child recognizers), as a UITapGestureRecognizer firing at the point
        // would run it. A recognizer installed in the wrong place therefore runs too.
        var manager = typeof(View).GetField("_gestureManager", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(label)
            ?? throw new CheckFailedException("View._gestureManager not found");
        var platformManager = manager.GetType().GetProperty("GesturePlatformManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(manager);
        if (platformManager is null)
        {
            // No recognizers anywhere on the label: MAUI installed nothing that a tap could run.
            FixtureContext.Check(!label.GestureRecognizers.Any() && label.FormattedText?.Spans.All(s => s.GestureRecognizers.Count == 0) != false,
                "a label with recognizers has a gesture platform manager");
            return;
        }
        var installed = platformManager.GetType().GetField("_gestureRecognizers", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(platformManager) as System.Collections.IDictionary
            ?? throw new CheckFailedException("GesturePlatformManager._gestureRecognizers not found");
        var process = platformManager.GetType().GetMethod("ProcessRecognizerHandlerTap", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new CheckFailedException("GesturePlatformManager.ProcessRecognizerHandlerTap not found");
        foreach (var recognizer in installed.Keys.Cast<IGestureRecognizer>().ToList())
            if (recognizer is TapGestureRecognizer || recognizer.GetType().Name == "ChildGestureRecognizer")
                process.Invoke(null, [new WeakReference(platformManager), new WeakReference(recognizer), new CoreGraphics.CGPoint(point.X, point.Y), 1, null]);
        await Task.CompletedTask;
#endif
    }
}
