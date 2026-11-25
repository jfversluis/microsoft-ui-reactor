namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>
/// The modals one navigation stack presented. The window's modal stack is shared by every stack in
/// it (a <c>NavigationPage</c>'s or a Shell section's), so each manages only the modals it pushed,
/// pops only one that is on top, and, once its element unmounts, dismisses its modals through the
/// window it presented them in, top down while they are on top; a modal covered by another stack's
/// modal goes when that one is dismissed (spec 065 §6, spec 069 §4.7).
/// </summary>
internal sealed class OwnedModals
{
    private readonly HashSet<Page> _pages = new(ReferenceEqualityComparer.Instance);

    // The window the modals were presented in. Cleanup goes through it: by then the stack's page may
    // have left the window, and its own Navigation no longer reaches the modals.
    private Window? _window;
    private bool _cleaning;
    private Application? _cleanupApp;
    private Func<INavigation?>? _fallback;

    /// <summary>The modal this stack is popping, so its <c>ModalPopped</c> isn't taken for the user's.</summary>
    public Page? Popping { get; private set; }

    public int Count => _pages.Count;

    public bool Contains(Page page) => _pages.Contains(page);

    /// <summary>The modals of <paramref name="stack"/> this stack presented, bottom first.</summary>
    public List<Page> In(IReadOnlyList<Page> stack) => [.. stack.Where(_pages.Contains)];

    /// <summary>A modal left the window's stack, whoever popped it.</summary>
    public void Forget(Page modal) => _pages.Remove(modal);

    public Task Push(INavigation navigation, Window? window, Page modal, bool animated)
    {
        _pages.Add(modal);
        _window = window ?? _window;
        return navigation.PushModalAsync(modal, animated);
    }

    public async Task Pop(INavigation navigation, Page top, bool animated)
    {
        Popping = top;
        try
        {
            await navigation.PopModalAsync(animated);
        }
        finally
        {
            Popping = null;
        }
    }

    /// <summary>
    /// Dismisses the modals this (stopped) stack presented, through the window it presented them in
    /// or, without one, <paramref name="fallback"/>. One covered by another stack's modal is left until
    /// that modal goes: the stack keeps listening to <see cref="Application.ModalPopped"/> until all of
    /// its modals are gone.
    /// </summary>
    public async void CleanUp(Func<INavigation?> fallback)
    {
        _fallback = fallback;
        if (_cleaning) return;
        _cleaning = true;
        INavigation? navigation = null;
        try
        {
            // No window left: the modals went with it.
            navigation = _window?.Navigation ?? fallback();
            if (navigation is null) _pages.Clear();
            while (navigation is not null && navigation.ModalStack.Count > 0 && _pages.Contains(navigation.ModalStack[^1]))
            {
                var top = navigation.ModalStack[^1];
                _pages.Remove(top);
                await navigation.PopModalAsync(animated: false);
            }
            if (navigation is not null) _pages.RemoveWhere(m => !navigation.ModalStack.Contains(m));
        }
        catch (Exception)
        {
            // The window is closing or another owner is mid-transition; there is no tree left to report to.
        }
        finally
        {
            _cleaning = false;
        }

        if (_pages.Count > 0)
        {
            if (_cleanupApp is null && Application.Current is { } app)
            {
                _cleanupApp = app;
                app.ModalPopped += OnModalPoppedWhileStopped;
            }
        }
        else if (_cleanupApp is { } subscribed)
        {
            subscribed.ModalPopped -= OnModalPoppedWhileStopped;
            _cleanupApp = null;
        }
    }

    private void OnModalPoppedWhileStopped(object? sender, ModalPoppedEventArgs e)
    {
        _pages.Remove(e.Modal);
        if (_fallback is { } fallback) CleanUp(fallback);
    }
}
