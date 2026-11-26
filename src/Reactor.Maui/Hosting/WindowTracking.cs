using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Maui.Hosting;

/// <summary>
/// Finds the window a tree is shown in for the window hooks (<c>UseWindowSize</c>, <c>UseDialogs</c>,
/// <c>UseWindowLifecycle</c>, spec 069 §5.4). A <c>ReactorWindow</c> provides itself; a
/// <c>ReactorPage</c> hosted by a non-Reactor app gets its window only when it is shown, so the page's
/// <c>Loaded</c> is watched, and a page moved to another window is followed there.
/// </summary>
internal static class WindowTracking
{
    /// <summary>The window known now: the provided one, else the hosted page's.</summary>
    public static Window? Current(Window? window, Page? page) => window ?? page?.Window;

    /// <summary>
    /// Calls <paramref name="attach"/> with the window now, or when the hosted page is loaded in one,
    /// and <paramref name="detach"/> for the previous one if the page moves. The returned cleanup stops
    /// watching and detaches. Runs on the UI thread, from an effect.
    /// </summary>
    public static Action Watch(Window? window, Page? page, Action<Window> attach, Action<Window>? detach = null)
    {
        Window? attached = null;
        void Attach()
        {
            var target = Current(window, page);
            if (target is null || ReferenceEquals(target, attached)) return;
            if (attached is not null) detach?.Invoke(attached);
            attached = target;
            attach(target);
        }
        void Loaded(object? sender, EventArgs e) => Attach();

        Attach();
        if (page is not null) page.Loaded += Loaded;
        return () =>
        {
            if (page is not null) page.Loaded -= Loaded;
            if (attached is not null) detach?.Invoke(attached);
            attached = null;
        };
    }
}

/// <summary>Finds a renderer hook's place in the tree.</summary>
internal static class HookNodes
{
    /// <summary>The node of the component rendering with <paramref name="context"/>.</summary>
    public static Node Current(RenderContext context, string hook) =>
        (context.Owner as RootRenderOwner)?.Rendering
        ?? throw new InvalidOperationException($"{hook} must be called while a MAUI Reactor component renders.");
}
