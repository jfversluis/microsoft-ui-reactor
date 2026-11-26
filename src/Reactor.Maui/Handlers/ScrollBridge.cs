using MauiScrollView = Microsoft.Maui.Controls.ScrollView;

namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>
/// The scroll view the <c>ScrollView</c> element renders. It sees every <c>ScrollFinished</c>, which
/// MAUI's <c>ScrollToAsync</c> tasks can't report reliably (spec 070 §2.6.1). An implicit style for
/// <c>ScrollView</c> needs <c>ApplyToDerivedTypes</c>, as for the CollectionView Reactor renders.
/// </summary>
internal sealed class ReactorScrollView : MauiScrollView, IScrollView
{
    public ReactorScrollView()
    {
        ScrollToRequested += (_, _) => Bridge.OnRequested();
        HandlerChanged += (_, _) =>
        {
            if (Handler is null) Bridge.Cancel();
        };
    }

    internal ScrollBridge Bridge { get; } = new();

    /// <summary>How many finishes MAUI reported; for the device characterization (spec 070 §2.15).</summary>
    internal int FinishCount { get; private set; }

    void IScrollView.ScrollFinished()
    {
        FinishCount++;
        Bridge.OnFinished(this);
        SendScrollFinished();
    }
}

/// <summary>
/// The scroll bridge's completion protocol (spec 070 §2.6.1): one outstanding request per scroll view,
/// confirmed only by a finish at its target.
/// </summary>
internal sealed class ScrollBridge
{
    private TaskCompletionSource<bool>? _request;
    private Point _target;
    private int _expectedFinishes;
    private bool _dispatching;

    /// <summary>
    /// Whether overlapping animated requests each produce their own finish. Set from the per-platform
    /// characterization (spec 070 §2.15); where they don't, at most one finish is expected at a time.
    /// </summary>
    internal static bool FinishesAccumulate { get; set; } = true;

    public Task<bool> Start(MauiScrollView scrollView, Point target, Func<Task> dispatch)
    {
        Supersede();
        var request = new TaskCompletionSource<bool>();
        _request = request;
        _target = target;
        _expectedFinishes = FinishesAccumulate ? _expectedFinishes + 1 : 1;
        _dispatching = true;
        try
        {
            _ = dispatch();
        }
        catch (Exception ex)
        {
            // Nothing is outstanding for a request that never went out.
            _expectedFinishes = Math.Max(0, _expectedFinishes - 1);
            if (ReferenceEquals(_request, request)) _request = null;
            request.TrySetResult(false);
            System.Diagnostics.Trace.TraceError($"Reactor: a ScrollToAsync request failed: {ex}");
        }
        finally
        {
            _dispatching = false;
        }
        return request.Task;
    }

    /// <summary>Another request (not this bridge's own dispatch) supersedes the outstanding one.</summary>
    public void OnRequested()
    {
        if (_dispatching) return;
        _expectedFinishes = FinishesAccumulate ? _expectedFinishes + 1 : 1;
        Supersede();
    }

    public void OnFinished(MauiScrollView scrollView)
    {
        if (_expectedFinishes > 0) _expectedFinishes--;
        if (_request is not { } request) return;
        if (Matches(scrollView, _target))
        {
            _request = null;
            request.TrySetResult(true);
        }
        else if (_expectedFinishes == 0)
        {
            // No earlier request's finish is still expected: this scroll ended elsewhere.
            _request = null;
            request.TrySetResult(false);
        }
    }

    /// <summary>The scroll view went away (unmounted, or its handler disconnected).</summary>
    public void Cancel()
    {
        _expectedFinishes = 0;
        Supersede();
    }

    private void Supersede()
    {
        if (_request is not { } previous) return;
        _request = null;
        previous.TrySetResult(false);
    }

    /// <summary>
    /// Whether the scroll position is at <paramref name="target"/> within one device pixel on each
    /// scrollable axis, or as far toward it as the content allows.
    /// </summary>
    internal static bool Matches(MauiScrollView scrollView, Point target)
    {
        var pixel = PixelInDips(scrollView);
        var vertical = scrollView.Orientation is ScrollOrientation.Vertical or ScrollOrientation.Both;
        var horizontal = scrollView.Orientation is ScrollOrientation.Horizontal or ScrollOrientation.Both;
        return (!vertical || AxisMatches(scrollView, scrollView.ScrollY, target.Y, pixel, verticalAxis: true))
            && (!horizontal || AxisMatches(scrollView, scrollView.ScrollX, target.X, pixel, verticalAxis: false));
    }

    private static bool AxisMatches(MauiScrollView scrollView, double position, double target, double pixel, bool verticalAxis)
    {
        if (Math.Abs(position - target) <= pixel) return true;
        // Clamped at the start: cross-platform scroll coordinates start at 0 on every platform.
        if (target < 0 && position <= pixel) return true;
        return target > position && AtRangeEnd(scrollView, verticalAxis, pixel);
    }

    private static double PixelInDips(MauiScrollView scrollView)
    {
#if ANDROID
        if (scrollView.Handler?.PlatformView is global::Android.Views.View view && view.Context?.Resources?.DisplayMetrics is { } metrics)
            return 1 / metrics.Density;
#elif IOS || MACCATALYST
        if (scrollView.Handler?.PlatformView is global::UIKit.UIView view)
            return 1 / (view.Window?.Screen ?? global::UIKit.UIScreen.MainScreen).Scale;
#endif
        return 1;
    }

    /// <summary>Whether the platform can't scroll further toward the end of the axis.</summary>
    private static bool AtRangeEnd(MauiScrollView scrollView, bool verticalAxis, double pixel)
    {
#if ANDROID
        if (scrollView.Handler?.PlatformView is global::Android.Views.View view)
        {
            if (verticalAxis) return !view.CanScrollVertically(1);
            // MAUI nests a horizontal scroll view for horizontal scrolling.
            return FindHorizontal(view) is not { } horizontal || !horizontal.CanScrollHorizontally(1);
        }
#elif IOS || MACCATALYST
        if (scrollView.Handler?.PlatformView is global::UIKit.UIScrollView view)
        {
            var inset = view.AdjustedContentInset;
            var bounds = view.Bounds;
            return verticalAxis
                ? view.ContentOffset.Y >= view.ContentSize.Height + inset.Bottom - bounds.Height - pixel
                : view.ContentOffset.X >= view.ContentSize.Width + inset.Right - bounds.Width - pixel;
        }
#endif
        // No platform view (headless): the content's measured or arranged size.
        var content = scrollView.ContentSize;
        if (scrollView.Content is { } contentView)
            content = new Size(Math.Max(content.Width, contentView.Frame.Width), Math.Max(content.Height, contentView.Frame.Height));
        return verticalAxis
            ? scrollView.ScrollY >= content.Height - scrollView.Height - pixel
            : scrollView.ScrollX >= content.Width - scrollView.Width - pixel;
    }

#if ANDROID
    private static global::Android.Widget.HorizontalScrollView? FindHorizontal(global::Android.Views.View view)
    {
        if (view is global::Android.Widget.HorizontalScrollView horizontal) return horizontal;
        if (view is not global::Android.Views.ViewGroup group) return null;
        for (var i = 0; i < group.ChildCount; i++)
            if (group.GetChildAt(i) is { } child && FindHorizontal(child) is { } found) return found;
        return null;
    }
#endif
}
