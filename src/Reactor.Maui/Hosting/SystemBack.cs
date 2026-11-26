using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Maui.Hosting;

/// <summary>
/// The <c>UseSystemBackButton</c> handlers and their dispatch (spec 069 §5.2). Handlers are kept per
/// window: a handler joins the window its tree is shown in (found as the window hooks find it). On
/// Android, a window with handlers gets one AndroidX <c>OnBackPressedCallback</c>, added after MAUI's
/// so it is asked first and enabled only while a handler is registered: it asks the handlers of the page
/// the user sees and, if none handles the back, passes the key on to MAUI (the navigation stack's or
/// Shell's guarded back, then the platform's default). Pages don't override <c>OnBackButtonPressed</c>
/// for this, so MAUI keeps the predictive back-to-home preview of a root without handlers.
/// </summary>
internal static class SystemBack
{
    private static long s_sequence;

    private static readonly BindableProperty StateProperty =
        BindableProperty.CreateAttached("ReactorSystemBack", typeof(WindowBack), typeof(SystemBack), null);

    internal sealed class Registration(Node node, Func<bool> handler, long sequence)
    {
        public Node Node { get; } = node;
        public Func<bool> Handler { get; } = handler;
        public long Sequence { get; } = sequence;
        public bool Removed { get; set; }
    }

    /// <summary>A handler for <paramref name="node"/>'s component; it is asked once it joins a window.</summary>
    public static Registration Create(Node node, Func<bool> handler) =>
        new(node, handler, Interlocked.Increment(ref s_sequence));

    public static void Join(Registration registration, Window window) => StateOf(window, create: true)!.Add(registration);

    public static void Leave(Registration registration, Window window) => StateOf(window, create: false)?.Remove(registration);

    /// <summary>
    /// The system back key in <paramref name="window"/>: the handlers first, then
    /// <paramref name="passOn"/> (MAUI's back handling) if none handled it.
    /// </summary>
    public static bool HandleBackKey(Window window, Func<bool> passOn) => Ask(window) || passOn();

    /// <summary>Whether the window's Android back callback is enabled (tests).</summary>
    internal static bool HasEnabledCallback(Window window) => StateOf(window, create: false)?.CallbackEnabled == true;

#if ANDROID
    /// <summary>The window's Android back callback, if it has one (device tests reorder callbacks).</summary>
    internal static AndroidX.Activity.OnBackPressedCallback? CallbackOf(Window window) => StateOf(window, create: false)?.Callback;
#endif

    private static WindowBack? StateOf(Window window, bool create)
    {
        var state = (WindowBack?)window.GetValue(StateProperty);
        if (state is null && create)
        {
            state = new WindowBack(window);
            window.SetValue(StateProperty, state);
        }
        return state;
    }

    /// <summary>Asks the eligible handlers once each; true when one handled the back.</summary>
    internal static bool Ask(Window window)
    {
        if (StateOf(window, create: false) is not { Count: > 0 } state) return false;
        // As MAUI routes the back: to the top modal, else the window's page.
        var modals = window.Navigation.ModalStack;
        var receiver = modals.Count > 0 ? modals[^1] : window.Page;
        var visible = new HashSet<object>(ReferenceEqualityComparer.Instance);
        AddVisible(receiver, visible);
        // A snapshot: each handler runs at most once, even if handlers mount or unmount others.
        var eligible = state.Registrations
            .Where(r => !r.Removed && !r.Node.Unmounted && (PageOf(r.Node) is not { } page || visible.Contains(page)))
            .OrderByDescending(r => Depth(r.Node))
            .ThenByDescending(r => r.Sequence)
            .ToList();
        foreach (var registration in eligible)
        {
            if (registration.Removed || registration.Node.Unmounted) continue;
            try
            {
                if (registration.Handler()) return true;
            }
            catch (Exception ex)
            {
                // Like a throwing navigation guard: the back is consumed, and the error goes to the
                // component's error boundary.
                registration.Node.Root.ReportCallbackError(registration.Node, ex);
                return true;
            }
        }
        return false;
    }

    /// <summary>The page the user sees through <paramref name="page"/>, and the containers it is shown in.</summary>
    internal static void AddVisible(Page? page, HashSet<object> visible)
    {
        if (page is null || !visible.Add(page)) return;
        switch (page)
        {
            case Shell shell:
                if (shell.CurrentItem is { } item)
                {
                    visible.Add(item);
                    if (item.CurrentItem is { } section)
                    {
                        visible.Add(section);
                        if (section.CurrentItem is { } content) visible.Add(content);
                    }
                }
                AddVisible(shell.CurrentPage, visible);
                break;
            case NavigationPage navigation:
                AddVisible(navigation.CurrentPage, visible);
                break;
            case FlyoutPage flyout:
                AddVisible(flyout.Detail, visible);
                if (flyout.IsPresented) AddVisible(flyout.Flyout, visible);
                break;
            case MultiPage<Page> tabs:
                AddVisible(tabs.CurrentPage, visible);
                break;
        }
    }

    /// <summary>
    /// The page a component belongs to: the page it renders (a route's component that returns
    /// <c>Page(…)</c>, or one that returns a <c>NavigationHost</c>), else the nearest page (or Shell
    /// item, tab or content) above it, across list cells, else the <c>ReactorPage</c> hosting its tree.
    /// A component that renders the tree's root page is above every page: <see langword="null"/> in a
    /// <c>ReactorWindow</c>'s tree (always asked, even while a modal is shown).
    /// </summary>
    internal static object? PageOf(Node node)
    {
        var top = node.Root.TopRoot;
        var rendered = node;
        while (rendered.Kind != NodeKind.Host && rendered.Children.Count == 1) rendered = rendered.Children[0];
        if (rendered.Kind == NodeKind.Host && rendered.Native is Page or BaseShellItem)
            return ReferenceEquals(rendered.Native, top.RootNative) ? top.HostPage : rendered.Native;
        for (var n = Up(node); n is not null; n = Up(n))
            if (n.Kind == NodeKind.Host && n.Native is Page or BaseShellItem) return n.Native;
        return top.HostPage;
    }

    private static Node? Up(Node node) => node.Parent ?? node.Root.ContextParent;

    private static int Depth(Node node) => node.Depth + (node.Root.ContextParent is { } owner ? Depth(owner) + 1 : 0);

    /// <summary>The handlers of one window, and on Android its back callback.</summary>
    private sealed class WindowBack
    {
        private readonly Window _window;
        private readonly List<Registration> _registrations = [];

        public WindowBack(Window window)
        {
            _window = window;
#if ANDROID
            // An activity recreated: the callback goes to the new one, after MAUI's (which MAUI adds at
            // the end of OnCreate, after the window's handler changes), so the add is posted.
            window.HandlerChanged += (_, _) =>
            {
                if (window.Dispatcher is not { } dispatcher || !dispatcher.Dispatch(Update)) Update();
            };
#endif
        }

        public int Count => _registrations.Count;

        public IReadOnlyList<Registration> Registrations => _registrations;

        public void Add(Registration registration)
        {
            if (!_registrations.Contains(registration)) _registrations.Add(registration);
            Update();
        }

        public void Remove(Registration registration)
        {
            _registrations.Remove(registration);
            Update();
        }

#if ANDROID
        private AndroidX.Activity.ComponentActivity? _activity;
        private BackCallback? _callback;
        private bool _passingOn;

        public AndroidX.Activity.OnBackPressedCallback? Callback => _callback;

        public bool CallbackEnabled => _callback?.Enabled == true;

        private void Update()
        {
            var activity = _window.Handler?.PlatformView as AndroidX.Activity.ComponentActivity;
            if (!ReferenceEquals(activity, _activity))
            {
                _callback?.Remove();
                _callback = null;
                _activity = activity;
            }
            if (_registrations.Count > 0 && _callback is null && activity is not null)
            {
                _callback = new BackCallback(this);
                activity.OnBackPressedDispatcher.AddCallback(activity, _callback);
            }
            if (_callback is not null && !_passingOn) _callback.Enabled = _registrations.Count > 0;
        }

        private void OnBack()
        {
            if (Ask(_window) || _activity is not { } activity || _callback is not { } callback) return;
            // Nothing handled it: MAUI's callback (or the platform's default) gets the key, as without Reactor.
            _passingOn = true;
            callback.Enabled = false;
            try
            {
                activity.OnBackPressedDispatcher.OnBackPressed();
            }
            finally
            {
                _passingOn = false;
                Update();
            }
        }

        private sealed class BackCallback(WindowBack owner) : AndroidX.Activity.OnBackPressedCallback(true)
        {
            public override void HandleOnBackPressed() => owner.OnBack();
        }
#else
        public bool CallbackEnabled => false;

        private static void Update()
        {
        }
#endif
    }
}
