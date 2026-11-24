namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Bounds re-render request re-entrancy (TASK-059, spec 067 §11.2): a component that synchronously
/// requests its own re-render from inside <c>Render()</c> or an effect cleanup fails fast instead of
/// recursing to a stack overflow. The renderer brackets each request it forwards with
/// <see cref="Enter"/>/<see cref="Exit"/> and checks <see cref="ThrowIfTooDeep"/> first.
/// </summary>
internal static class RerenderReentrancy
{
    /// <summary>Hard depth cap.</summary>
    internal const int Max = 50;

    /// <summary>Active re-render request depth on this thread.</summary>
    [ThreadStatic]
    private static int t_depth;

    internal static void ThrowIfTooDeep()
    {
        if (t_depth >= Max)
            throw new InvalidOperationException(
                "Render loop detected: requestRerender re-entered more than " +
                $"{Max} times. Likely cause: setState called " +
                "synchronously inside Render() or effect cleanup.");
    }

    internal static void Enter() => t_depth++;

    internal static void Exit() => t_depth--;
}
