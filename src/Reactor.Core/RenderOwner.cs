namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// The host that renders a <see cref="RenderContext"/>'s content, as the framework-neutral core
/// sees it (spec 067). A renderer's owner type extends it with framework members (WinUI's
/// <c>IContentOwner</c> adds the owning window); hooks that need those cast to the renderer's type.
/// </summary>
internal interface IRenderOwner
{
    /// <summary>
    /// The persisted-state scope bounded by the owning window's lifetime, or null when the content
    /// has no owning window (<see cref="PersistedScope.Window"/> then falls back to the application scope).
    /// </summary>
    IPersistedStateScope? WindowPersistedScope { get; }
}

/// <summary>
/// The renderer's UI thread, as the framework-neutral core sees it (spec 067): where a hook setter
/// called off that thread is marshaled.
/// </summary>
internal static class RenderScheduler
{
    private static Func<Action, bool>? _tryEnqueueOnUIThread;

    /// <summary>
    /// Queues work on the UI thread, returning false when the dispatcher refused it (shutting
    /// down); null when no UI dispatcher has been captured (a test or headless render). The
    /// renderer sets this wherever it captures its UI dispatcher (WinUI: <c>ReactorApp.UIDispatcher</c>).
    /// </summary>
    internal static Func<Action, bool>? TryEnqueueOnUIThread
    {
        get => Volatile.Read(ref _tryEnqueueOnUIThread);
        set => Volatile.Write(ref _tryEnqueueOnUIThread, value);
    }
}
