using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// A root as the shared core sees it (spec 067 §13): the owner its components render under, and the
/// render thread their off-thread state setters marshal to. MAUI's render thread is per root, so
/// each root installs it on the contexts it renders rather than in the core's process-wide slot.
/// </summary>
internal sealed class RootRenderOwner : IRenderOwner
{
    internal RootRenderOwner(IRenderScheduler scheduler)
    {
        Scheduler = scheduler;
        Enqueue = work =>
        {
            scheduler.Post(work);
            return true;
        };
    }

    internal IRenderScheduler Scheduler { get; }

    internal Func<Action, bool> Enqueue { get; }

    // No window-bounded persisted scope yet: PersistedScope.Window falls back to the application scope.
    public IPersistedStateScope? WindowPersistedScope => null;
}
