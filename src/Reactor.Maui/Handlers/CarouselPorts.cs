namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>The carousel's platform port (spec 070 §5.4): what each platform reads and how it waits.</summary>
internal static class CarouselPorts
{
    public static ICarouselPort? Create(ReactorCarouselView view)
    {
#if IOS || MACCATALYST
        return new UIKitCarouselPort(view);
#elif ANDROID
        return new AndroidCarouselPort(view);
#else
        _ = view;
        return null;
#endif
    }

#if IOS || MACCATALYST
    /// <summary>
    /// UIKit: MAUI's CarouselView pages its items in UIKit's private orthogonal section scroller, whose
    /// delegate isn't MAUI's (MAUI's <c>IsDragging</c> never turns true), so the port reads the scroller
    /// found above a cell, and watches its pan recognizer.
    /// </summary>
    private sealed class UIKitCarouselPort(ReactorCarouselView view) : ICarouselPort
    {
        private Action? _onInteraction;
        private UIKit.UIScrollView? _watched;
        private Foundation.NSObject? _watchToken;

        public object? Owner => view.Handler?.PlatformView;

        public CarouselSample? Sample()
        {
            if (view.Handler?.PlatformView is not UIKit.UIView root || root.Window is null || root.Bounds.Width <= 0) return null;
            var center = root.ConvertPointToView(new CoreGraphics.CGPoint(root.Bounds.Width / 2, root.Bounds.Height / 2), null);
            UIKit.UIView? best = null;
            ItemBox? bestBox = null;
            var bestDistance = double.MaxValue;
            foreach (var cell in view.Host.Cells)
            {
                if (cell.BindingContext is not ItemBox box || cell.Handler?.PlatformView is not UIKit.UIView native || !IsShown(native, root)) continue;
                var middle = native.ConvertPointToView(new CoreGraphics.CGPoint(native.Bounds.Width / 2, native.Bounds.Height / 2), null);
                var distance = middle.X - center.X;
                if (Math.Abs(distance) < Math.Abs(bestDistance))
                {
                    bestDistance = distance;
                    best = native;
                    bestBox = box;
                }
            }
            if (best is null || bestBox is null) return null;
            var index = view.Host.Items.IndexOf(bestBox);
            if (index < 0) return null;
            var scroller = Scroller(best);
            Watch(scroller);
            var moving = scroller is not null && (scroller.Tracking || scroller.Dragging || scroller.Decelerating);
            var user = scroller is not null && (scroller.Tracking || scroller.Dragging);
            return new CarouselSample(index, Math.Abs(bestDistance) < 1, bestDistance, moving, user);
        }

        // In the window, not hidden, and horizontally inside the carousel: UIKit keeps reusable cells around.
        private static bool IsShown(UIKit.UIView native, UIKit.UIView root)
        {
            if (native.Window is null) return false;
            for (var v = native; v is not null && !ReferenceEquals(v, root); v = v.Superview)
                if (v.Hidden) return false;
            var frame = native.ConvertRectToView(native.Bounds, root);
            return frame.Right > 0 && frame.Left < root.Bounds.Width;
        }

        private static UIKit.UIScrollView? Scroller(UIKit.UIView cell)
        {
            for (var v = cell.Superview; v is not null; v = v.Superview)
                if (v is UIKit.UIScrollView scroll) return scroll;
            return null;
        }

        private void Watch(UIKit.UIScrollView? scroller)
        {
            if (scroller is null || ReferenceEquals(scroller, _watched) || _onInteraction is not { } notify) return;
            if (_watched is not null && _watchToken is not null) _watched.PanGestureRecognizer.RemoveTarget(_watchToken, null);
            _watched = scroller;
            _watchToken = scroller.PanGestureRecognizer.AddTarget(() => notify());
        }

        public void ScrollTo(int index, bool animate) =>
            view.ScrollTo(index, position: ScrollToPosition.Center, animate: animate);

        public bool Barrier(Action completion)
        {
            if (Collection() is not { } native) return false;
            native.PerformBatchUpdates(null, _ => completion());
            return true;
        }

        private UIKit.UICollectionView? Collection()
        {
            if (view.Handler?.PlatformView is not UIKit.UIView root) return null;
            var pending = new Stack<UIKit.UIView>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var next = pending.Pop();
                if (next is UIKit.UICollectionView found) return found;
                foreach (var child in next.Subviews) pending.Push(child);
            }
            return null;
        }

        public void NextFrame(Action callback)
        {
            CoreAnimation.CADisplayLink? link = null;
            link = CoreAnimation.CADisplayLink.Create(() =>
            {
                link?.Invalidate();
                callback();
            });
            link.AddToRunLoop(Foundation.NSRunLoop.Main, Foundation.NSRunLoopMode.Common);
        }

        public void Watch(Action onInteraction) => _onInteraction = onInteraction;
    }
#endif

#if ANDROID
    /// <summary>Android: the <c>RecyclerView</c>'s scroll state and its children's positions.</summary>
    private sealed class AndroidCarouselPort(ReactorCarouselView view) : ICarouselPort
    {
        private AndroidX.RecyclerView.Widget.RecyclerView? _watched;
        private Action? _onInteraction;

        public object? Owner => view.Handler?.PlatformView;

        private AndroidX.RecyclerView.Widget.RecyclerView? Recycler() => view.Handler?.PlatformView as AndroidX.RecyclerView.Widget.RecyclerView;

        public CarouselSample? Sample()
        {
            if (Recycler() is not { } rv || !rv.IsAttachedToWindow || rv.Width <= 0) return null;
            Watch(rv);
            var density = rv.Context?.Resources?.DisplayMetrics?.Density ?? 1;
            var at = new int[2];
            rv.GetLocationInWindow(at);
            var center = at[0] + rv.Width / 2f;
            ItemBox? bestBox = null;
            var bestDistance = double.MaxValue;
            foreach (var cell in view.Host.Cells)
            {
                if (cell.BindingContext is not ItemBox box || cell.Handler?.PlatformView is not global::Android.Views.View native) continue;
                if (!native.IsAttachedToWindow || !native.IsShown || native.Width <= 0) continue;
                native.GetLocationInWindow(at);
                if (at[0] + native.Width <= center - rv.Width / 2f || at[0] >= center + rv.Width / 2f) continue;
                var distance = (at[0] + native.Width / 2f - center) / density;
                if (Math.Abs(distance) < Math.Abs(bestDistance))
                {
                    bestDistance = distance;
                    bestBox = box;
                }
            }
            if (bestBox is null) return null;
            var index = view.Host.Items.IndexOf(bestBox);
            if (index < 0) return null;
            var state = rv.ScrollState;
            return new CarouselSample(index, Math.Abs(bestDistance) < 1, bestDistance,
                state != AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateIdle,
                state == AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateDragging);
        }

        private void Watch(AndroidX.RecyclerView.Widget.RecyclerView rv)
        {
            if (ReferenceEquals(rv, _watched) || _onInteraction is not { } notify) return;
            _watched = rv;
            rv.AddOnScrollListener(new Listener(notify));
        }

        public void ScrollTo(int index, bool animate) =>
            view.ScrollTo(index, position: ScrollToPosition.Center, animate: animate);

        // After MAUI's deferred handling of the change (its Dispatch, posted earlier), the layout, and
        // the RecyclerView's item animations: a ScrollTo during those lands one item off (§5.1).
        public bool Barrier(Action completion)
        {
            if (Recycler() is not { } rv) return false;
            void Step()
            {
                if (!ReferenceEquals(Recycler(), rv)) { completion(); return; }
                if (rv.IsLayoutRequested || rv.IsComputingLayout)
                {
                    rv.Post(Step);
                    return;
                }
                if (rv.GetItemAnimator() is { } animator && animator.InvokeIsRunning(new Finished(() => rv.Post(Step)))) return;
                completion();
            }
            view.Dispatcher.Dispatch(() => rv.Post(Step));
            return true;
        }

        public void NextFrame(Action callback) => global::Android.Views.Choreographer.Instance!.PostFrameCallback(new Frame(callback));

        public void Watch(Action onInteraction) => _onInteraction = onInteraction;

        private sealed class Listener(Action notify) : AndroidX.RecyclerView.Widget.RecyclerView.OnScrollListener
        {
            public override void OnScrollStateChanged(AndroidX.RecyclerView.Widget.RecyclerView recyclerView, int newState) => notify();

            public override void OnScrolled(AndroidX.RecyclerView.Widget.RecyclerView recyclerView, int dx, int dy) => notify();
        }

        private sealed class Finished(Action done) : Java.Lang.Object, AndroidX.RecyclerView.Widget.RecyclerView.ItemAnimator.IItemAnimatorFinishedListener
        {
            public void OnAnimationsFinished() => done();
        }

        private sealed class Frame(Action callback) : Java.Lang.Object, global::Android.Views.Choreographer.IFrameCallback
        {
            public void DoFrame(long frameTimeNanos) => callback();
        }
    }
#endif
}
