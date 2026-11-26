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
        Bridge = new ScrollBridge(this, observesFinishes: true);
    }

    internal ScrollBridge Bridge { get; }

    /// <summary>How many finishes MAUI reported; for the device characterization (spec 070 §2.15).</summary>
    internal int FinishCount { get; private set; }

    void IScrollView.ScrollFinished()
    {
        FinishCount++;
        // The bookkeeping runs first; the bridge's tasks run their continuations asynchronously, so
        // one that starts another scroll can't have that request completed by this finish.
        Bridge.OnFinished();
        SendScrollFinished();
    }
}

/// <summary>
/// The scroll bridge's completion protocol (spec 070 §2.6.1): one outstanding request per scroll view,
/// confirmed only by a finish at its target. A <see cref="ReactorScrollView"/> reports every finish; on
/// any other scroll view the bridge sees only the finish that completes MAUI's task for its own
/// request (an earlier request's late finish completes it too), so it checks the position then. A
/// request whose finish never comes settles by position at <see cref="SettleBound"/>.
/// </summary>
internal sealed class ScrollBridge
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MauiScrollView, ScrollBridge> Foreign = new();

    /// <summary>
    /// A bound above the longest scroll the platform animates, after which a request whose finish never
    /// came settles by position (spec 070 §2.15): MAUI's Android animator runs 1000 ms, scaled by the
    /// system's animator duration scale; UIKit's animated <c>setContentOffset</c> was measured at about
    /// 0.3 s. It isn't a guess at when the scroll ends: a request still settles at its own finish, and
    /// one settled by the bound is <c>true</c> only if the scroll view is at its target then.
    /// </summary>
    internal static TimeSpan SettleBound
    {
        get
        {
#if ANDROID
            return TimeSpan.FromMilliseconds(1000 * Math.Max(0, AnimatorDurationScale()) + 1000);
#else
            return TimeSpan.FromMilliseconds(1500);
#endif
        }
    }

#if ANDROID
    private static float AnimatorDurationScale()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33)) return global::Android.Animation.ValueAnimator.DurationScale;
        var resolver = global::Android.App.Application.Context.ContentResolver;
        return resolver is null ? 1f : global::Android.Provider.Settings.Global.GetFloat(resolver, global::Android.Provider.Settings.Global.AnimatorDurationScale, 1f);
    }
#endif

    /// <summary>Schedules the bound's check; headless tests replace it.</summary>
    internal static readonly AsyncLocal<Action<TimeSpan, Action>?> DelayOverride = new();

    private readonly MauiScrollView _scrollView;
    private readonly bool _observesFinishes;
    private TaskCompletionSource<bool>? _request;
    private Point _target;
    private int _expectedFinishes;
    private bool _dispatching;
    private ScrollToRequestedEventArgs? _parkedForReplay;
    private ScrollToRequestedEventArgs? _parkedForGeometry;

    internal ScrollBridge(MauiScrollView scrollView, bool observesFinishes)
    {
        _scrollView = scrollView;
        _observesFinishes = observesFinishes;
        scrollView.ScrollToRequested += (_, e) => OnRequested(e);
        // Before the old handler disconnects: MAUI replaces a connected handler with another, or with
        // none, and either way the old one's animation will never report to the new one.
        scrollView.HandlerChanging += (_, e) =>
        {
            if (e.OldHandler is not null) Cancel();
        };
    }

    /// <summary>The bridge of any scroll view: Reactor's own, or one attached to a foreign scroll view.</summary>
    public static ScrollBridge For(MauiScrollView scrollView) =>
        scrollView is ReactorScrollView reactor ? reactor.Bridge
            : Foreign.GetValue(scrollView, static sv => new ScrollBridge(sv, observesFinishes: false));

    /// <summary>
    /// Whether overlapping animated requests each produce their own finish. Set from the per-platform
    /// characterization (spec 070 §2.15): all three platforms produce one per request.
    /// </summary>
    internal static bool FinishesAccumulate { get; set; } = true;

    /// <summary>How many finishes are still expected from requests MAUI dispatched; for tests.</summary>
    internal int ExpectedFinishes => _expectedFinishes;

    public Task<bool> Start(Point target, Func<Task> dispatch)
    {
        Supersede();
        var request = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _request = request;
        _target = target;
        // The bridge only dispatches what is ready, so MAUI sends it to the handler at once. Counted
        // first: UIKit ends an interrupted animation (an earlier request's finish) inside the dispatch.
        var expectedBefore = _expectedFinishes;
        Expect();
        _dispatching = true;
        Task maui;
        try
        {
            maui = dispatch();
        }
        catch (Exception ex)
        {
            // Nothing is outstanding for a request that never went out.
            _expectedFinishes = Math.Min(_expectedFinishes, expectedBefore);
            if (ReferenceEquals(_request, request)) _request = null;
            request.TrySetResult(false);
            System.Diagnostics.Trace.TraceError($"Reactor: a ScrollToAsync request failed: {ex}");
            return request.Task;
        }
        finally
        {
            _dispatching = false;
        }
        // However the platform ends the scroll (a user's drag can stop an animation without a finish),
        // the task settles by the bound at the latest.
        Delay(SettleBound, () =>
        {
            if (!ReferenceEquals(_request, request)) return;
            _request = null;
            request.TrySetResult(Matches(_scrollView, target));
        });
        if (!_observesFinishes)
        {
            // MAUI completes the task of the newest request on the next finish, on the UI thread.
            maui.ContinueWith(_ =>
            {
                if (!ReferenceEquals(_request, request)) return;
                _request = null;
                request.TrySetResult(Matches(_scrollView, target));
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        return request.Task;
    }

    private void Expect() => _expectedFinishes = FinishesAccumulate ? _expectedFinishes + 1 : 1;

    /// <summary>
    /// A request raised <c>ScrollToRequested</c>. Mirrors when MAUI 11 dispatches it to the handler: at
    /// once; when its element target has been laid out (parked); or, raised again, when a handler
    /// attaches (parked before one existed). Only dispatched requests produce finishes.
    /// </summary>
    private void OnRequested(ScrollToRequestedEventArgs e)
    {
        if (!_dispatching && ReferenceEquals(e, _parkedForReplay))
        {
            // The replay of a request parked before the handler attached: dispatched now.
            _parkedForReplay = null;
            Expect();
            return;
        }

        // Any newer request, the bridge's own included, drops whatever MAUI had parked; a request
        // dropped while parked for geometry never produces a finish the bridge sees.
        _parkedForReplay = null;
        if (_parkedForGeometry is not null)
        {
            _parkedForGeometry = null;
            _expectedFinishes = Math.Max(0, _expectedFinishes - 1);
        }
        if (_dispatching) return;

        Supersede();

        if (_scrollView.Handler is null) _parkedForReplay = e;
        else
        {
            if (e.Mode == ScrollToMode.Element && !IsGeometryReady(e.Element)) _parkedForGeometry = e;
            Expect();
        }
    }

    // MAUI 11's ScrollView.IsElementTargetGeometryReady.
    private bool IsGeometryReady(Microsoft.Maui.Controls.Element target) =>
        _scrollView.Width >= 0 && _scrollView.Height >= 0
        && (ReferenceEquals(target, _scrollView) || _scrollView.Content is { Width: >= 0, Height: >= 0 });

    public void OnFinished()
    {
        _parkedForGeometry = null;
        if (_expectedFinishes > 0) _expectedFinishes--;
        if (_request is not { } request) return;
        if (Matches(_scrollView, _target))
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
        // Otherwise most likely an earlier request's finish: the request waits for its own, or for the
        // bound armed when it started. The count is left as it is, so a late finish still arriving is
        // absorbed rather than taken for the next request's.
    }

    private void Delay(TimeSpan delay, Action action)
    {
        if (DelayOverride.Value is { } delayOverride) delayOverride(delay, action);
        else _scrollView.Dispatcher.DispatchDelayed(delay, action);
    }

    /// <summary>The scroll view went away (unmounted, or its handler disconnected).</summary>
    public void Cancel()
    {
        _expectedFinishes = 0;
        _parkedForReplay = null;
        _parkedForGeometry = null;
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
