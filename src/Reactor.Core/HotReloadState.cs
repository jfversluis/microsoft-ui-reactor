namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// The hot-reload pass flag the core's hook migration reads (spec 049, spec 067). The renderer's
/// hot-reload service opens and closes passes; see WinUI's <c>HotReloadService.BeginUpdatePass</c>.
/// </summary>
internal static class HotReloadState
{
    // [ThreadStatic]: Render runs on the UI thread and every child Render() runs synchronously on
    // that same thread within the pass, so the flag scopes exactly to the pass.
    [ThreadStatic] private static bool t_withinUpdatePass;

    /// <summary>True while a hot-reload whole-tree re-render is running on the current thread.</summary>
    internal static bool WithinUpdatePass
    {
        get => t_withinUpdatePass;
        set => t_withinUpdatePass = value;
    }
}
