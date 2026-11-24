namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>
/// Issues component diagnostic ids: the <c>componentId</c> of <c>ReactorEventSource.ComponentRendered</c>
/// and of the <c>Microsoft-UI-Reactor-Perf</c> spans. One counter per process, shared by every renderer.
/// </summary>
internal static class ComponentIds
{
    private static long s_nextId;

    internal static long Next() => Interlocked.Increment(ref s_nextId);
}
