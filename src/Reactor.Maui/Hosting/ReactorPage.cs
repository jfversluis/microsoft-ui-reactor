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
    private readonly WindowPageSetter _pages;
    private ContentPage? _viewHost;
    private List<Exception>? _destroyingErrors;

    public ReactorWindow(Element root, ElementHandlerRegistry? registry = null)
    {
        Page = new ContentPage();
        _root = new ReactorRoot(registry ?? MauiHandlers.CreateRegistry(), new MauiRenderScheduler(Dispatcher), SetRootNative);
        _pages = new WindowPageSetter(this, _root.ReportHostError);
        // Its queue subscribes to Destroying before the tree does, so a dialog a later Destroying
        // handler requests is settled at once instead of being shown on the dying window.
        DialogQueue.For(this);
        Render(root);
    }

    internal ReactorRoot Root => _root;

    /// <summary>
    /// Unmounts the tree, running every effect cleanup. MAUI calls this after the <c>Destroying</c>
    /// event's handlers, so <c>UseWindowLifecycle</c>'s <c>onDestroying</c> runs while the tree is
    /// still mounted (spec 069 §5.4). Errors those callbacks threw are reported after the teardown.
    /// </summary>
    protected override void OnDestroying()
    {
        base.OnDestroying();
        Exception? teardown = null;
        try
        {
            _root.Unmount();
        }
        catch (Exception ex)
        {
            // Unmount rethrows cleanup errors when nothing handles UnhandledException.
            teardown = ex;
        }
        finally
        {
            _pages.Cancel();
        }

        var held = _destroyingErrors;
        _destroyingErrors = null;
        if (held is null)
        {
            if (teardown is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(teardown).Throw();
            return;
        }
        if (teardown is not null) held.Add(teardown);
        _root.ReportUnhandled(held.Count == 1 ? held[0] : new AggregateException("Callbacks failed while the window was being destroyed.", held));
    }

    /// <summary>Holds an <c>onDestroying</c> callback's error until the tree has been unmounted.</summary>
    internal void HoldDestroyingError(Exception error) => (_destroyingErrors ??= []).Add(error);

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
            _pages.Set(page);
            return;
        }
        _viewHost ??= new ContentPage();
        _viewHost.Content = native is null ? null : Props.AsView(native);
        _pages.Set(_viewHost);
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
        // A back handler above every view of this tree belongs to this page (spec 069 §5.2).
        _root.HostPage = this;
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
