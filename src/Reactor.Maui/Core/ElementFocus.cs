using Microsoft.Maui.Dispatching;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Reactor.Maui.Direct;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// The state behind <c>UseElementFocus()</c> (spec 070 §2.6.2): focuses the referenced view once it is
/// ready, meaning its handler is connected and its platform view is in a window.
/// </summary>
internal sealed class ElementFocus
{
    // How long a direct view's readiness is polled for (it has no Loaded event): 2 s at 16 ms.
    private const int MaxReadinessPolls = 120;

    private readonly IDispatcher? _dispatcher = Dispatcher.GetForCurrentThread();

    public ElementFocus() => Request = RequestFocus;

    public ElementRef<IView> Ref { get; } = new();

    public Action Request { get; }

    private void RequestFocus()
    {
        if (_dispatcher is { IsDispatchRequired: true } dispatcher) dispatcher.Dispatch(FocusWhenReady);
        else FocusWhenReady();
    }

    private void FocusWhenReady()
    {
        switch (Ref.Current)
        {
            case VisualElement visual when visual.IsLoaded:
                visual.Focus();
                break;
            case VisualElement visual:
                void OnLoaded(object? sender, EventArgs e)
                {
                    visual.Loaded -= OnLoaded;
                    // Dropped if the element unmounted (or another view took the ref) meanwhile.
                    if (ReferenceEquals(Ref.Current, visual)) visual.Focus();
                }
                visual.Loaded += OnLoaded;
                break;
            case DirectView direct:
                Poll(direct, 0);
                break;
            case { } other:
                other.Focus();
                break;
        }
    }

    private void Poll(DirectView view, int attempt)
    {
        if (!ReferenceEquals(Ref.Current, view)) return;
        if (IsInWindow(view))
        {
            view.Focus();
            return;
        }
        if (attempt >= MaxReadinessPolls || _dispatcher is null) return;
        _dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), () => Poll(view, attempt + 1));
    }

    private static bool IsInWindow(DirectView view) => view.Handler?.PlatformView switch
    {
#if ANDROID
        global::Android.Views.View native => native.IsAttachedToWindow,
#elif IOS || MACCATALYST
        global::UIKit.UIView native => native.Window is not null,
#elif WINDOWS
        Microsoft.UI.Xaml.FrameworkElement native => native.XamlRoot is not null,
#endif
        null => false,
        _ => true,
    };
}
