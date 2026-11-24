namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Bounds re-render request re-entrancy (TASK-059, spec 067 §11.2): a component that synchronously
/// requests its own re-render from inside <c>Render()</c> or an effect cleanup fails fast instead of
/// recursing to a stack overflow. The renderer brackets each request it forwards with
/// <see cref="Enter"/>/<see cref="Exit"/> and checks <see cref="ThrowIfTooDeep"/> first.
/// </summary>
/// <remarks>
/// A request climbs from the component that asked through each ancestor's re-render wrapper to the
/// host. That climb is one request, not a re-entry, so it is counted once, where it starts: the
/// renderer names the next wrapper up with <see cref="BeginForward"/> before calling it, and that
/// wrapper recognises the hand-off with <see cref="IsForwardedTo"/> and neither checks nor counts.
/// Counting every wrapper instead made a state change more than <see cref="Max"/> components deep
/// throw with no loop at all.
/// </remarks>
internal static class RerenderReentrancy
{
    /// <summary>Hard depth cap.</summary>
    internal const int Max = 50;

    /// <summary>Active re-render request depth on this thread.</summary>
    [ThreadStatic]
    private static int t_depth;

    /// <summary>The wrapper a request is being handed to on this thread, until it takes it.</summary>
    [ThreadStatic]
    private static object? t_forwardTo;

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

    /// <summary>
    /// Marks the call the renderer is about to make to <paramref name="next"/> as the same request
    /// climbing. Returns the previous mark, for <see cref="EndForward"/>.
    /// </summary>
    internal static object? BeginForward(object next)
    {
        var previous = t_forwardTo;
        t_forwardTo = next;
        return previous;
    }

    /// <summary>Restores the mark <see cref="BeginForward"/> replaced.</summary>
    internal static void EndForward(object? previous) => t_forwardTo = previous;

    /// <summary>
    /// Whether this call to <paramref name="self"/> is a request handed up by a descendant's
    /// wrapper. Clears the mark, so a request <paramref name="self"/> triggers in turn (a
    /// synchronous render, say) counts as new.
    /// </summary>
    internal static bool IsForwardedTo(object self)
    {
        if (!ReferenceEquals(t_forwardTo, self)) return false;
        t_forwardTo = null;
        return true;
    }
}
