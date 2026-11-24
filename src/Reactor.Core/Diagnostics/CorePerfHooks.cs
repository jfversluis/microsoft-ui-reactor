using System.Runtime.CompilerServices;

namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>
/// What the framework-neutral core reports to the renderer's <c>Microsoft-UI-Reactor-Perf</c>
/// provider (spec 067). The provider (WinUI's <c>ReactorPerfTrace</c>) owns the ETW session state
/// and publishes the two gates here whenever a session changes them, so a core hook pays one
/// volatile read when nobody listens, as it did before the split.
/// </summary>
internal static class CorePerfHooks
{
    private static bool s_stateEnabled;
    private static bool s_stateSetsEnabled;
    private static Action<ulong, string, bool>? s_stateChange;

    /// <summary>The verbose <c>StateChange</c> event is on.</summary>
    internal static bool StateEnabled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref s_stateEnabled);
    }

    /// <summary>Render and effects spans are counting state writes (<see cref="ComponentPerfTally"/>).</summary>
    internal static bool StateSetsEnabled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref s_stateSetsEnabled);
    }

    /// <summary>Writes a <c>StateChange</c> event: component id, hook name, whether the value changed.</summary>
    internal static void StateChange(ulong componentId, string hook, bool changed)
        => Volatile.Read(ref s_stateChange)?.Invoke(componentId, hook, changed);

    /// <summary>Called by the provider whenever a session enables or disables it.</summary>
    internal static void Publish(bool stateEnabled, bool stateSetsEnabled, Action<ulong, string, bool> stateChange)
    {
        Volatile.Write(ref s_stateChange, stateChange);
        Volatile.Write(ref s_stateSetsEnabled, stateSetsEnabled);
        Volatile.Write(ref s_stateEnabled, stateEnabled);
    }
}
