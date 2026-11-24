namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Installs WinUI's implementations of the framework-neutral core's platform seams (spec 067):
/// the memory-pressure signal behind <see cref="ApplicationPersistedScope"/>. The UI dispatcher
/// is pushed separately, wherever <see cref="ReactorApp.UIDispatcher"/> is set. Called before any
/// context renders (from the <see cref="Reconciler"/> constructor and the dispatcher setter)
/// rather than from a module initializer, which would be an unconditional trimming root.
/// </summary>
internal static class WinUICoreServices
{
    private static int s_installed;

    internal static void EnsureInstalled()
    {
        if (Interlocked.Exchange(ref s_installed, 1) != 0) return;
        ApplicationPersistedScope.MemoryPressureSource = SubscribeToMemoryPressure;
    }

    // Trims when Windows reports the app's memory usage as high or over its limit. Throws where the
    // hosting model has no memory manager events (unpackaged apps, headless tests); the scope logs
    // that and carries on.
    private static IDisposable? SubscribeToMemoryPressure(Action onHighPressure)
    {
        EventHandler<object> handler = (_, _) =>
        {
            var level = global::Windows.System.MemoryManager.AppMemoryUsageLevel;
            if (level == global::Windows.System.AppMemoryUsageLevel.OverLimit ||
                level == global::Windows.System.AppMemoryUsageLevel.High)
            {
                onHighPressure();
            }
        };
        global::Windows.System.MemoryManager.AppMemoryUsageIncreased += handler;
        return new Unsubscriber(handler);
    }

    private sealed class Unsubscriber(EventHandler<object> handler) : IDisposable
    {
        public void Dispose() => global::Windows.System.MemoryManager.AppMemoryUsageIncreased -= handler;
    }
}
