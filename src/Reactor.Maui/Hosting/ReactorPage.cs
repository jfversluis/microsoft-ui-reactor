using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Maui.Handlers;

namespace Microsoft.UI.Reactor.Maui.Hosting;

/// <summary>A <see cref="ContentPage"/> whose content is a Reactor element tree.</summary>
public class ReactorPage : ContentPage
{
    private readonly ReactorRoot _root;

    public ReactorPage(Element root, ElementHandlerRegistry? registry = null)
    {
        var dispatcher = Dispatcher ?? throw new InvalidOperationException(
            "ReactorPage must be created on a thread with a MAUI dispatcher (the UI thread).");
        _root = new ReactorRoot(
            registry ?? MauiHandlers.CreateRegistry(),
            new MauiRenderScheduler(dispatcher),
            native => Content = native is null ? null : MauiHandlers.AsView(native));
        _root.Render(root);
    }

    /// <summary>Re-renders the page with a new root element, keeping the state of everything that still matches.</summary>
    public void Render(Element root) => _root.Render(root);

    /// <summary>
    /// Unmounts the tree and runs every effect cleanup. Call it when the page is discarded for good,
    /// not on <c>OnDisappearing</c>, which also fires when the page is merely covered.
    /// </summary>
    public void Unmount() => _root.Unmount();
}

internal sealed class MauiRenderScheduler(IDispatcher dispatcher) : IRenderScheduler
{
    public bool CheckAccess() => !dispatcher.IsDispatchRequired;

    public void Post(Action work)
    {
        if (!dispatcher.Dispatch(work))
            throw new InvalidOperationException("The MAUI dispatcher refused the render work; the app is probably shutting down.");
    }
}
