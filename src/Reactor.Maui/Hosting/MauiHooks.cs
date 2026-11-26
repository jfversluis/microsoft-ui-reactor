using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Maui.Hosting;

/// <summary>
/// Hooks for app-level MAUI state. Call them on the render context (<c>ctx.UseAppTheme()</c>) or,
/// in a component class, on <c>this</c> (<c>this.UseAppTheme()</c>).
/// </summary>
public static class MauiHooks
{
    /// <summary>The app's current theme (light or dark). Re-renders when it changes.</summary>
    public static AppTheme UseAppTheme(this RenderContext context)
    {
        var (theme, setTheme) = context.UseState(Application.Current?.RequestedTheme ?? AppTheme.Unspecified);
        context.UseEffect(() =>
        {
            if (Application.Current is not { } app) return static () => { };
            void Changed(object? sender, AppThemeChangedEventArgs e) => setTheme(e.RequestedTheme);
            app.RequestedThemeChanged += Changed;
            // The theme may have changed between the render and this subscription.
            setTheme(app.RequestedTheme);
            return () => app.RequestedThemeChanged -= Changed;
        });
        return theme;
    }

    /// <summary>
    /// The size of the window the tree is shown in, in device-independent units, or (0, 0) before
    /// it is known. Re-renders when the window is resized or rotated.
    /// </summary>
    public static Size UseWindowSize(this RenderContext context)
    {
        var window = context.UseContext(MauiContexts.Window);
        var page = context.UseContext(MauiContexts.Page);
        var (size, setSize) = context.UseState(Measure(WindowTracking.Current(window, page)));
        context.UseEffect(() =>
        {
            Window? subscribed = null;
            void Resized(object? sender, EventArgs e) => setSize(Measure(subscribed));
            return WindowTracking.Watch(window, page,
                attach: target =>
                {
                    subscribed = target;
                    target.SizeChanged += Resized;
                    setSize(Measure(target));
                },
                detach: target =>
                {
                    target.SizeChanged -= Resized;
                    subscribed = null;
                });
        }, window, page);
        return size;
    }

    /// <summary>
    /// MAUI's page dialogs (alert, action sheet, prompt) for the window the tree is shown in
    /// (spec 069 §5.1). The returned object is stable across renders: capture it in event handlers
    /// and effects (<c>if (await dialogs.Alert("Delete?", null, "Delete", "Cancel")) …</c>). Dialogs are
    /// shown one at a time per window, on its top modal or else its page.
    /// </summary>
    public static Dialogs UseDialogs(this RenderContext context) => Dialogs.Use(context);

    /// <summary>
    /// Registers a handler for Android's back key while this component is mounted (spec 069 §5.2;
    /// Windows' back button in 4g; iOS and Mac Catalyst have no back key). Return true when the
    /// handler handled the back. Only the handlers of the page the user sees, and of components that
    /// render the tree's root page, are asked: deepest
    /// first, then the most recently mounted. When none handles it, the back goes on to the
    /// navigation stack, Shell, and the platform. The latest handler is used on every render.
    /// </summary>
    public static void UseSystemBackButton(this RenderContext context, Func<bool> onBack)
    {
        var node = HookNodes.Current(context, nameof(UseSystemBackButton));
        var window = context.UseContext(MauiContexts.Window);
        var page = context.UseContext(MauiContexts.Page);
        var latest = context.UseRef(onBack);
        latest.Current = onBack;
        var registration = context.UseRef<SystemBack.Registration?>(null);
        registration.Current ??= SystemBack.Create(node, () => latest.Current());
        context.UseEffect(() =>
        {
            var handler = registration.Current;
            return WindowTracking.Watch(window, page, w => SystemBack.Join(handler, w), w => SystemBack.Leave(handler, w));
        }, window, page);
    }

    /// <summary>
    /// The system back key goes back in <paramref name="nav"/>, as on WinUI: whenever
    /// <c>nav.CanGoBack</c>, the key is consumed, whether or not a guard lets the back happen.
    /// </summary>
    public static void UseSystemBackButton<TRoute>(this RenderContext context, Navigation.NavigationHandle<TRoute> nav)
        where TRoute : notnull =>
        context.UseSystemBackButton(() =>
        {
            if (!nav.CanGoBack) return false;
            nav.GoBack();
            return true;
        });

    /// <summary>
    /// Subscribes to the lifecycle of the window the tree is shown in (spec 069 §5.4): MAUI's
    /// <c>Activated</c>, <c>Deactivated</c>, <c>Stopped</c>, <c>Resumed</c> and <c>Destroying</c>. The
    /// callbacks are refreshed every render and unsubscribed at unmount. An error in a callback goes to
    /// the component's error boundary, except in <paramref name="onDestroying"/> under a
    /// <c>ReactorWindow</c>: that window runs <paramref name="onDestroying"/> before it unmounts its tree,
    /// so such an error bypasses the boundaries and goes to the window's <c>UnhandledException</c> once
    /// the tree has been unmounted (rethrown when nothing handles it).
    /// </summary>
    public static void UseWindowLifecycle(
        this RenderContext context,
        Action? onActivated = null,
        Action? onDeactivated = null,
        Action? onStopped = null,
        Action? onResumed = null,
        Action? onDestroying = null)
    {
        var node = HookNodes.Current(context, nameof(UseWindowLifecycle));
        var window = context.UseContext(MauiContexts.Window);
        var page = context.UseContext(MauiContexts.Page);
        var holder = context.UseRef<WindowLifecycleCallbacks?>(null);
        var callbacks = holder.Current ??= new WindowLifecycleCallbacks(node);
        callbacks.Activated = onActivated;
        callbacks.Deactivated = onDeactivated;
        callbacks.Stopped = onStopped;
        callbacks.Resumed = onResumed;
        callbacks.Destroying = onDestroying;
        context.UseEffect(() => WindowTracking.Watch(window, page, callbacks.Attach, callbacks.Detach), window, page);
    }

    public static AppTheme UseAppTheme(this Component component) => component.Context.UseAppTheme();

    public static Size UseWindowSize(this Component component) => component.Context.UseWindowSize();

    public static Dialogs UseDialogs(this Component component) => component.Context.UseDialogs();

    private static Size Measure(Window? window) =>
        window is null || window.Width < 0 || double.IsNaN(window.Width) ? Size.Zero : new Size(window.Width, window.Height);
}

/// <summary>The callbacks of one <c>UseWindowLifecycle</c>, subscribed to the window it found.</summary>
internal sealed class WindowLifecycleCallbacks(Node owner)
{
    public Action? Activated;
    public Action? Deactivated;
    public Action? Stopped;
    public Action? Resumed;
    public Action? Destroying;

    public void Attach(Window window)
    {
        window.Activated += OnActivated;
        window.Deactivated += OnDeactivated;
        window.Stopped += OnStopped;
        window.Resumed += OnResumed;
        window.Destroying += OnDestroying;
    }

    public void Detach(Window window)
    {
        window.Activated -= OnActivated;
        window.Deactivated -= OnDeactivated;
        window.Stopped -= OnStopped;
        window.Resumed -= OnResumed;
        window.Destroying -= OnDestroying;
    }

    private void OnActivated(object? sender, EventArgs e) => Run(Activated);
    private void OnDeactivated(object? sender, EventArgs e) => Run(Deactivated);
    private void OnStopped(object? sender, EventArgs e) => Run(Stopped);
    private void OnResumed(object? sender, EventArgs e) => Run(Resumed);

    private void Run(Action? callback)
    {
        try
        {
            callback?.Invoke();
        }
        catch (Exception ex)
        {
            owner.Root.ReportCallbackError(owner, ex);
        }
    }

    private void OnDestroying(object? sender, EventArgs e)
    {
        try
        {
            Destroying?.Invoke();
        }
        catch (Exception ex)
        {
            // Boundary handling is posted, so it would run after the teardown: a ReactorWindow holds
            // the error until it has unmounted its tree, then reports it (spec 069 §5.4).
            if (sender is ReactorWindow window && ReferenceEquals(window.Root, owner.Root.TopRoot)) window.HoldDestroyingError(ex);
            else owner.Root.ReportCallbackError(owner, ex);
        }
    }
}
