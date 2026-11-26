using Microsoft.UI.Reactor.Maui.Handlers;
using MauiScrollView = Microsoft.Maui.Controls.ScrollView;

namespace Reactor.Maui.DeviceTests;

/// <summary>Platform readback and input for the spec 070 fixtures (focus, scrolling, selection, refresh).</summary>
public static class RefPlatform
{
    private static object? Native(IView view) => view.Handler?.PlatformView;

#if ANDROID
    private static double Density(Android.Views.View view) => view.Context?.Resources?.DisplayMetrics?.Density ?? 1;
#endif

    public static bool IsFocused(IView view) => Native(view) switch
    {
#if IOS || MACCATALYST
        UIKit.UIView native => native.IsFirstResponder,
#elif ANDROID
        Android.Views.View native => native.IsFocused,
#endif
        _ => false,
    };

    /// <summary>The scroll position in MAUI's cross-platform coordinates (the inset rest position is 0).</summary>
    public static Point ScrollOffset(IScrollView scroll)
    {
        switch (Native(scroll))
        {
#if IOS || MACCATALYST
            case UIKit.UIScrollView native:
                return new Point(native.ContentOffset.X + native.AdjustedContentInset.Left, native.ContentOffset.Y + native.AdjustedContentInset.Top);
#elif ANDROID
            case Android.Views.View native:
                var horizontal = FindHorizontal(native);
                var density = Density(native);
                return new Point((horizontal?.ScrollX ?? native.ScrollX) / density, native.ScrollY / density);
#endif
            default:
                throw new CheckFailedException("no platform scroll view");
        }
    }

    /// <summary>The native vertical offset, insets included, for comparing two scroll views.</summary>
    public static double NativeOffsetY(MauiScrollView scroll) => Native(scroll) switch
    {
#if IOS || MACCATALYST
        UIKit.UIScrollView native => native.ContentOffset.Y,
#elif ANDROID
        Android.Views.View native => native.ScrollY / Density(native),
#endif
        _ => throw new CheckFailedException("no platform scroll view"),
    };

    public static bool ShowsVerticalIndicator(IScrollView scroll) => Native(scroll) switch
    {
#if IOS || MACCATALYST
        UIKit.UIScrollView native => native.ShowsVerticalScrollIndicator,
#elif ANDROID
        Android.Views.View native => native.VerticalScrollBarEnabled,
#endif
        _ => throw new CheckFailedException("no platform scroll view"),
    };

    /// <summary>The top of <paramref name="target"/> in the scroll view's content, measured on the platform.</summary>
    public static double ContentY(MauiScrollView scroll, IView target)
    {
        var content = Native((IView)scroll.Content!) ?? throw new CheckFailedException("no platform content");
        switch (Native(target), content)
        {
#if IOS || MACCATALYST
            case (UIKit.UIView view, UIKit.UIView contentView):
                return view.ConvertRectToView(view.Bounds, contentView).Y + contentView.Frame.Y;
#elif ANDROID
            case (Android.Views.View view, Android.Views.View contentView):
                int[] a = new int[2], b = new int[2];
                view.GetLocationInWindow(a);
                contentView.GetLocationInWindow(b);
                return (a[1] - b[1]) / Density(view) + contentView.Top / Density(view);
#endif
            default:
                throw new CheckFailedException("no platform target");
        }
    }

    public static double ViewHeight(IView view) => Platform.Size(view).Height;

    /// <summary>UIKit's adjusted top content inset; 0 where the platform has none.</summary>
    public static double AdjustedTopInset(MauiScrollView scroll) => Native(scroll) switch
    {
#if IOS || MACCATALYST
        UIKit.UIScrollView native => native.AdjustedContentInset.Top,
#endif
        _ => 0,
    };

    public static double ViewportHeight(MauiScrollView scroll) => scroll.Height;

    /// <summary>Adds UIKit content insets, so the viewport differs from the frame. False where there is no such inset.</summary>
    public static bool SetContentInsets(MauiScrollView scroll, double top, double bottom)
    {
#if IOS || MACCATALYST
        if (Native(scroll) is UIKit.UIScrollView native)
        {
            native.ContentInset = new UIKit.UIEdgeInsets((nfloat)top, 0, (nfloat)bottom, 0);
            return true;
        }
#endif
        return false;
    }

    // ── Selection ───────────────────────────────────────────────────────────

    public static (int, int) Selection(IEntry entry) =>
        PlatformSelection.Read(entry) is { } state ? (state.Cursor, state.Length) : throw new CheckFailedException("no platform selection");

    /// <summary>Selects on the platform view, as a tap or a drag does.</summary>
    public static void Select(IEntry entry, int start, int length)
    {
        switch (Native(entry))
        {
#if IOS || MACCATALYST
            case UIKit.UITextField field:
                var from = field.GetPosition(field.BeginningOfDocument, start)!;
                var to = field.GetPosition(from, length)!;
                field.SelectedTextRange = field.GetTextRange(from, to);
                break;
#elif ANDROID
            case Android.Widget.EditText edit:
                edit.SetSelection(start, start + length);
                break;
#endif
            default:
                throw new CheckFailedException("cannot select");
        }
    }

    /// <summary>Types at the caret, as the keyboard does: the text changes and the caret moves past it.</summary>
    public static void TypeAtCaret(IEntry entry, string text)
    {
        switch (Native(entry))
        {
#if IOS || MACCATALYST
            case UIKit.UITextField field:
                field.InsertText(text);
                break;
#elif ANDROID
            case Android.Widget.EditText edit:
                edit.EditableText?.Insert(Math.Max(0, edit.SelectionStart), new Java.Lang.String(text));
                break;
#endif
            default:
                throw new CheckFailedException("cannot type");
        }
    }

    /// <summary>Replaces the platform text, as a paste would.</summary>
    public static void SetText(IEntry entry, string text) => Platform.Type(entry, text);

    public static string ReturnKey(IEntry entry) => Native(entry) switch
    {
#if IOS || MACCATALYST
        UIKit.UITextField field => field.ReturnKeyType.ToString(),
#elif ANDROID
        Android.Widget.EditText edit => (edit.ImeOptions & Android.Views.InputMethods.ImeAction.ImeMaskAction).ToString(),
#endif
        _ => throw new CheckFailedException("no platform entry"),
    };

    public static string History(IView web)
    {
#if ANDROID
        if (Native(web) is Android.Webkit.WebView native && native.CopyBackForwardList() is { } list)
        {
            var urls = Enumerable.Range(0, list.Size).Select(i => list.GetItemAtIndex(i)?.Url ?? "?");
            return $"native history [{string.Join(", ", urls)}] at {list.CurrentIndex}, canGoBack {native.CanGoBack()}";
        }
#elif IOS || MACCATALYST
        if (Native(web) is WebKit.WKWebView native)
            return $"native back list {native.BackForwardList.BackList.Length}, canGoBack {native.CanGoBack}";
#endif
        return "no history";
    }

    // ── Refresh ─────────────────────────────────────────────────────────────

    public static bool? IsSpinning(IRefreshView refresh) => Native(refresh) switch
    {
#if IOS || MACCATALYST
        Microsoft.Maui.Platform.MauiRefreshView native => native.RefreshControl.Refreshing,
#elif ANDROID
        AndroidX.SwipeRefreshLayout.Widget.SwipeRefreshLayout native => native.Refreshing,
#endif
        _ => null,
    };

    /// <summary>Starts a refresh the way a pull does; returns how, or null if the platform offers none.</summary>
    public static async Task<string?> PullToRefresh(IRefreshView refresh, FixtureContext c)
    {
        switch (Native(refresh))
        {
#if IOS || MACCATALYST
            case Microsoft.Maui.Platform.MauiRefreshView native:
                if (native.RefreshControl.Superview is null) return null;
                native.RefreshControl.BeginRefreshing();
                native.RefreshControl.SendActionForControlEvents(UIKit.UIControlEvent.ValueChanged);
                return "the UIRefreshControl's value-changed action";
#elif ANDROID
            case AndroidX.SwipeRefreshLayout.Widget.SwipeRefreshLayout native:
                var x = native.Width / 2f;
                var time = Android.OS.SystemClock.UptimeMillis();
                void Send(Android.Views.MotionEventActions action, float y, long at)
                {
                    using var e = Android.Views.MotionEvent.Obtain(time, at, action, x, y, 0)!;
                    native.DispatchTouchEvent(e);
                }
                Send(Android.Views.MotionEventActions.Down, 20, time);
                for (var i = 1; i <= 20; i++)
                {
                    Send(Android.Views.MotionEventActions.Move, 20 + i * 40, time + i * 16);
                    await Task.Delay(16);
                }
                Send(Android.Views.MotionEventActions.Up, 820, time + 21 * 16);
                await Task.Yield();
                return "a pull gesture on the SwipeRefreshLayout";
#endif
            default:
                await Task.Yield();
                return null;
        }
    }

#if ANDROID
    private static Android.Widget.HorizontalScrollView? FindHorizontal(Android.Views.View view)
    {
        if (view is Android.Widget.HorizontalScrollView horizontal) return horizontal;
        if (view is not Android.Views.ViewGroup group) return null;
        for (var i = 0; i < group.ChildCount; i++)
            if (group.GetChildAt(i) is { } child && FindHorizontal(child) is { } found) return found;
        return null;
    }
#endif
}
