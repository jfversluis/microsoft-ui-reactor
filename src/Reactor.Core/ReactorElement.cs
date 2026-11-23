namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// The framework-neutral base of every Reactor element: an immutable description of a UI node that
/// the reconciler matches across renders. Each renderer derives its own element base from it
/// (WinUI's <c>Element</c>, MAUI's <c>Element</c>), adding that framework's modifiers.
/// </summary>
public abstract record ReactorElement
{
    /// <summary>
    /// Optional key for stable identity across re-renders (like React's key prop).
    /// When set, the reconciler uses it to match elements across list reorderings.
    /// </summary>
    public string? Key { get; init; }
}
