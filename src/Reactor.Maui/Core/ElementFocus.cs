using Microsoft.Maui.Dispatching;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Reactor.Maui.Direct;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// The state behind <c>UseElementFocus()</c> (spec 070 §2.6.2): focuses the referenced view once it is
/// ready, meaning its handler is connected and its platform view is in a window. A request is dropped
/// when the ref stops naming the view it waited for (the element unmounted or another took the ref).
/// </summary>
internal sealed class ElementFocus
{
    // iOS and Mac Catalyst have no public event for a view joining a window, so readiness is checked
    // at growing intervals, up to this one, for as long as the request stands.
    private static readonly TimeSpan MaxPollInterval = TimeSpan.FromSeconds(1);

    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    private readonly Func<Action, bool>? _enqueue;
    private readonly IDispatcher? _dispatcher = Dispatcher.GetForCurrentThread();
    private int _generation;

    public ElementFocus(Func<Action, bool>? enqueue)
    {
        _enqueue = enqueue;
        Request = RequestFocus;
    }

    public ElementRef<IView> Ref { get; } = new();

    public Action Request { get; }

    private void RequestFocus()
    {
        if (Environment.CurrentManagedThreadId == _uiThreadId)
        {
            FocusWhenReady();
            return;
        }
        // From another thread: on the root's render thread (the UI thread), as a state setter is.
        if (_enqueue?.Invoke(FocusWhenReady) != true) _dispatcher?.Dispatch(FocusWhenReady);
    }

    private void FocusWhenReady()
    {
        // A newer request replaces a pending one.
        var generation = ++_generation;
        switch (Ref.Current)
        {
            case VisualElement visual when visual.IsLoaded:
                visual.Focus();
                break;
            case VisualElement visual:
                void OnLoaded(object? sender, EventArgs e)
                {
                    visual.Loaded -= OnLoaded;
                    if (Stands(visual, generation)) visual.Focus();
                }
                visual.Loaded += OnLoaded;
                break;
            case DirectView direct:
                WhenReady(direct, generation, TimeSpan.FromMilliseconds(16));
                break;
            case { } other:
                other.Focus();
                break;
        }
    }

    private bool Stands(IView view, int generation) => generation == _generation && ReferenceEquals(Ref.Current, view);

    private void WhenReady(DirectView view, int generation, TimeSpan poll)
    {
        if (!Stands(view, generation)) return;
        switch (view.Handler?.PlatformView)
        {
            case null:
                // MAUI sets a Core view's handler before it creates the platform view, so the handler's
                // arrival is followed by one more check on the next turn.
                void OnHandlerChanged()
                {
                    view.HandlerChanged -= OnHandlerChanged;
                    Post(() => WhenReady(view, generation, poll));
                }
                view.HandlerChanged += OnHandlerChanged;
                return;
#if ANDROID
            case global::Android.Views.View { IsAttachedToWindow: false } native:
                void OnAttached(object? sender, global::Android.Views.View.ViewAttachedToWindowEventArgs e)
                {
                    native.ViewAttachedToWindow -= OnAttached;
                    WhenReady(view, generation, poll);
                }
                native.ViewAttachedToWindow += OnAttached;
                return;
#elif IOS || MACCATALYST
            case global::UIKit.UIView { Window: null }:
                var next = TimeSpan.FromTicks(Math.Min(poll.Ticks * 2, MaxPollInterval.Ticks));
                _dispatcher?.DispatchDelayed(poll, () => WhenReady(view, generation, next));
                return;
#endif
            default:
                view.Focus();
                return;
        }
    }

    private void Post(Action action)
    {
        if (_enqueue?.Invoke(action) != true) _dispatcher?.Dispatch(action);
    }
}
