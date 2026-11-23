using Microsoft.UI.Reactor.Core;

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
        var (size, setSize) = context.UseState(Measure(window ?? page?.Window));
        context.UseEffect(() =>
        {
            Window? subscribed = null;
            void Resized(object? sender, EventArgs e) => setSize(Measure(subscribed));
            void Attach()
            {
                var target = window ?? page?.Window;
                if (target is null || ReferenceEquals(target, subscribed)) return;
                if (subscribed is not null) subscribed.SizeChanged -= Resized;
                subscribed = target;
                subscribed.SizeChanged += Resized;
                setSize(Measure(subscribed));
            }
            void Loaded(object? sender, EventArgs e) => Attach();

            Attach();
            // A hosted page gets its window when it is shown.
            if (page is not null) page.Loaded += Loaded;
            return () =>
            {
                if (page is not null) page.Loaded -= Loaded;
                if (subscribed is not null) subscribed.SizeChanged -= Resized;
            };
        }, window, page);
        return size;
    }

    public static AppTheme UseAppTheme(this Component component) => component.Context.UseAppTheme();

    public static Size UseWindowSize(this Component component) => component.Context.UseWindowSize();

    private static Size Measure(Window? window) =>
        window is null || window.Width < 0 || double.IsNaN(window.Width) ? Size.Zero : new Size(window.Width, window.Height);
}
