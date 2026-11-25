using Microsoft.UI.Reactor.Maui.Handlers;

namespace Microsoft.UI.Reactor.Maui.Hosting;

/// <summary>
/// Sets a window's page for a host. On Android, MAUI crashes ("NavigationManager cannot be null
/// here", from <c>NavigationViewFragment.OnCreateView</c>) when <see cref="Window.Page"/> is replaced
/// while a navigation page's push is still running: the pending fragment transaction runs after the
/// outgoing page's navigation manager is gone. So while any navigation stack in the outgoing page
/// (the page itself, or one inside its tabs or flyout) is still running a platform operation, the new
/// page is set once those operations have finished (spec 065 §7). Other platforms set it at once.
/// </summary>
internal sealed class WindowPageSetter(Window window, Action<Exception> onError)
{
    private Page? _pending;
    private StackNavigator? _awaited;

    /// <summary>Whether a swap waits for the outgoing page's navigation; tests turn it off.</summary>
    internal bool WaitForNavigation { get; set; } = OperatingSystem.IsAndroid();

    /// <summary>Whether a swap is waiting for navigation to finish.</summary>
    internal bool IsPending => _pending is not null;

    /// <summary>Shows <paramref name="page"/>, now or once the outgoing page's navigation finishes.</summary>
    public void Set(Page page)
    {
        _pending = page;
        Apply();
    }

    /// <summary>Drops a waiting swap, for a host that is going away.</summary>
    public void Cancel()
    {
        _pending = null;
        Await(null);
    }

    private void Apply()
    {
        if (_pending is not { } page) return;
        if (WaitForNavigation && !ReferenceEquals(window.Page, page) && FindRunning(window.Page) is { } navigator)
        {
            Await(navigator);
            return;
        }
        Await(null);
        _pending = null;
        window.Page = page;
    }

    // Runs when a navigator finishes, inside its fire-and-forget loop: a failure is reported on the
    // dispatcher so it isn't lost with that task.
    private void OnNavigatorIdle()
    {
        try
        {
            Apply();
        }
        catch (Exception ex)
        {
            window.Dispatcher.Dispatch(() => onError(ex));
        }
    }

    private static StackNavigator? FindRunning(Page? page) => page switch
    {
        ReactorNavigationPage { Navigator: { IsIdle: false } navigator } => navigator,
        TabbedPage tabs => tabs.Children.Select(FindRunning).FirstOrDefault(n => n is not null),
        FlyoutPage flyout => FindRunning(flyout.Detail) ?? FindRunning(flyout.Flyout),
        _ => null,
    };

    private void Await(StackNavigator? navigator)
    {
        if (ReferenceEquals(_awaited, navigator)) return;
        if (_awaited is not null) _awaited.BecameIdle -= OnNavigatorIdle;
        _awaited = navigator;
        if (navigator is not null) navigator.BecameIdle += OnNavigatorIdle;
    }
}
