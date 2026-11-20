using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Maui.Handlers;

namespace Microsoft.UI.Reactor.Maui.Hosting;

/// <summary>Values the hosts provide to every component they render.</summary>
public static class MauiContexts
{
    /// <summary>The window the tree is shown in, when known.</summary>
    public static readonly Context<Window?> Window = new(null);

    /// <summary>The page hosting the tree, for a tree rendered by a <see cref="ReactorPage"/>.</summary>
    public static readonly Context<Page?> Page = new(null);
}

/// <summary>
/// A window whose page is a Reactor element tree. The root element can be a view (shown in a
/// content page), a <c>Page</c>, a <c>NavigationStack</c>, <c>Tabs</c> or a <c>Flyout</c>. The tree is
/// unmounted, running every effect cleanup, when the window is destroyed.
/// </summary>
public class ReactorWindow : Window
{
    private readonly ReactorRoot _root;
    private ContentPage? _viewHost;

    public ReactorWindow(Element root, ElementHandlerRegistry? registry = null)
    {
        Page = new ContentPage();
        _root = new ReactorRoot(registry ?? MauiHandlers.CreateRegistry(), new MauiRenderScheduler(Dispatcher), SetRootNative);
        Render(root);
        Destroying += (_, _) => _root.Unmount();
    }

    /// <summary>Raised for errors no error boundary caught, after the tree has been unmounted.</summary>
    public event Action<Exception>? UnhandledException
    {
        add => _root.UnhandledException += value;
        remove => _root.UnhandledException -= value;
    }

    /// <summary>Re-renders with a new root element, keeping the state of everything that still matches.</summary>
    public void Render(Element root) => _root.Render(MauiContexts.Window.Provide(this, root));

    private void SetRootNative(object? native)
    {
        if (native is Page page)
        {
            Page = page;
            return;
        }
        _viewHost ??= new ContentPage();
        _viewHost.Content = native is null ? null : Props.AsView(native);
        Page = _viewHost;
    }
}

/// <summary>
/// A <see cref="ContentPage"/> whose content is a Reactor element tree, for hosting Reactor inside an
/// existing app (a Shell route, a XAML navigation stack). The root element must render a view.
/// </summary>
public class ReactorPage : ContentPage
{
    private readonly ReactorRoot _root;

    public ReactorPage(Element root, ElementHandlerRegistry? registry = null)
    {
        _root = new ReactorRoot(
            registry ?? MauiHandlers.CreateRegistry(),
            new MauiRenderScheduler(Dispatcher),
            native =>
            {
                if (native is Page)
                    throw new InvalidOperationException("A ReactorPage hosts a view. Use ReactorWindow to host pages and navigation.");
                Content = native is null ? null : Props.AsView(native);
            });
        Render(root);
    }

    /// <summary>A Shell or navigation template that creates a <see cref="ReactorPage"/> for <paramref name="root"/>.</summary>
    public static DataTemplate Template(Func<Element> root, Func<ElementHandlerRegistry>? registry = null) =>
        new(() => new ReactorPage(root(), registry?.Invoke()));

    /// <summary>Raised for errors no error boundary caught, after the tree has been unmounted.</summary>
    public event Action<Exception>? UnhandledException
    {
        add => _root.UnhandledException += value;
        remove => _root.UnhandledException -= value;
    }

    /// <summary>Re-renders the page with a new root element, keeping the state of everything that still matches.</summary>
    public void Render(Element root) => _root.Render(MauiContexts.Page.Provide(this, root));

    /// <summary>
    /// Unmounts the tree and runs every effect cleanup. Call it when the page is discarded for good,
    /// not on <c>OnDisappearing</c>, which also fires when the page is merely covered.
    /// </summary>
    public void Unmount() => _root.Unmount();
}

internal sealed class MauiRenderScheduler(IDispatcher? dispatcher) : IRenderScheduler
{
    private readonly IDispatcher _dispatcher = dispatcher ?? throw new InvalidOperationException(
        "Reactor hosts must be created on a thread with a MAUI dispatcher (the UI thread).");

    public bool CheckAccess() => !_dispatcher.IsDispatchRequired;

    public void Post(Action work)
    {
        if (!_dispatcher.Dispatch(work))
            throw new InvalidOperationException("The MAUI dispatcher refused the render work; the app is probably shutting down.");
    }
}
