using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Maui.Hosting;

/// <summary>
/// Finds the window a tree is shown in for the window hooks (<c>UseWindowSize</c>, <c>UseDialogs</c>,
/// <c>UseWindowLifecycle</c>, <c>UseSystemBackButton</c>; spec 069 §5.4). A <c>ReactorWindow</c>
/// provides itself; a <c>ReactorPage</c> hosted by a non-Reactor app has a window only while it is in
/// one, so its <c>Window</c> property is followed: to a later window, to another one, and to none.
/// </summary>
internal static class WindowTracking
{
    /// <summary>The window known now: the provided one, else the hosted page's.</summary>
    public static Window? Current(Window? window, Page? page) => window ?? page?.Window;

    /// <summary>
    /// Calls <paramref name="attach"/> with the window now, or when the hosted page gets one, and
    /// <paramref name="detach"/> when the page leaves it (for another window, or none). The returned
    /// cleanup stops watching and detaches. Runs on the UI thread, from an effect.
    /// </summary>
    public static Action Watch(Window? window, Page? page, Action<Window> attach, Action<Window>? detach = null)
    {
        Window? attached = null;
        void Sync()
        {
            var target = Current(window, page);
            if (ReferenceEquals(target, attached)) return;
            if (attached is not null) detach?.Invoke(attached);
            attached = target;
            if (target is not null) attach(target);
        }
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == VisualElement.WindowProperty.PropertyName) Sync();
        }

        Sync();
        var follows = window is null && page is not null;
        if (follows) page!.PropertyChanged += Changed;
        return () =>
        {
            if (follows) page!.PropertyChanged -= Changed;
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

    /// <summary>Whether an error boundary is mounted above <paramref name="node"/> (across list cells).</summary>
    public static bool HasBoundaryAbove(Node node)
    {
        for (var n = node.Parent ?? node.Root.ContextParent; n is not null; n = n.Parent ?? n.Root.ContextParent)
            if (n.Kind == NodeKind.Boundary && !n.Unmounted) return true;
        return false;
    }
}
