using Microsoft.UI.Reactor.Maui.Handlers;

namespace Microsoft.UI.Reactor.Maui.Hosting;

/// <summary>
/// Sets a window's page for a host. On Android, MAUI crashes ("NavigationManager cannot be null
/// here", from <c>NavigationViewFragment.OnCreateView</c>) when <see cref="Window.Page"/> is replaced
/// while a navigation page's push is still running: the pending fragment transaction runs after the
/// outgoing page's navigation manager is gone. So when the outgoing page is a navigation stack whose
/// navigator is still running a platform operation, the new page is set once that operation has
/// finished (spec 065 §7). Other platforms set it at once.
/// </summary>
internal sealed class WindowPageSetter(Window window)
{
    private Page? _pending;
    private StackNavigator? _awaited;

    /// <summary>Whether a swap waits for the outgoing stack's navigation; tests turn it off.</summary>
    internal bool WaitForNavigation { get; set; } = OperatingSystem.IsAndroid();

    /// <summary>Shows <paramref name="page"/>, now or once the outgoing stack's navigation finishes.</summary>
    public void Set(Page page)
    {
        _pending = page;
        Apply();
    }

    private void Apply()
    {
        if (_pending is not { } page) return;
        if (WaitForNavigation && !ReferenceEquals(window.Page, page)
            && window.Page is ReactorNavigationPage { Navigator: { IsIdle: false } navigator })
        {
            Await(navigator);
            return;
        }
        Await(null);
        _pending = null;
        window.Page = page;
    }

    private void Await(StackNavigator? navigator)
    {
        if (ReferenceEquals(_awaited, navigator)) return;
        if (_awaited is not null) _awaited.BecameIdle -= Apply;
        _awaited = navigator;
        if (navigator is not null) navigator.BecameIdle += Apply;
    }
}
