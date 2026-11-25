namespace Microsoft.UI.Reactor.Navigation;

/// <summary>
/// A snapshot of a <c>NavigationHandle&lt;TRoute&gt;</c>'s full state: the back stack,
/// the currently active route, and the forward stack. Obtained from
/// <c>NavigationHandle&lt;TRoute&gt;.GetState</c> and restored via
/// <c>NavigationHandle&lt;TRoute&gt;.SetState</c>.
/// </summary>
/// <remarks>
/// Reactor intentionally does not pick a serialization format for navigation state.
/// This record is a plain POCO with no serializer-specific attributes — callers can
/// persist it however they like: JSON via <c>System.Text.Json</c> (preferably with a
/// source-generated <c>JsonSerializerContext</c> for AOT-safety), MessagePack, or by
/// hand. Avoid insecure or obsolete serializers.
/// </remarks>
public sealed record NavigationState<TRoute>(
    IReadOnlyList<TRoute> BackStack,
    TRoute Current,
    IReadOnlyList<TRoute> ForwardStack)
    where TRoute : notnull;
