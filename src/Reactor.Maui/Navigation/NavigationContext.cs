using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Navigation;

/// <summary>
/// Provides a static <see cref="Context{T}"/> instance per TRoute type for sharing a
/// <see cref="NavigationHandle{TRoute}"/> through the element tree. <c>NavigationHost</c> provides it;
/// a descendant reads it with <c>UseNavigation&lt;TRoute&gt;()</c>.
/// </summary>
internal static class NavigationContext<TRoute> where TRoute : notnull
{
    internal static readonly Context<NavigationHandle<TRoute>?> Instance =
        new(defaultValue: null, name: $"NavigationContext<{typeof(TRoute).Name}>");
}
