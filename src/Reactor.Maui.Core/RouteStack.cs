namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// A navigation stack held in component state, root first. Render it with a navigation element and
/// pass <see cref="PopTo"/> as its pop callback, so back gestures update the stack.
/// </summary>
public sealed record RouteStack<T>(IReadOnlyList<T> Items, Action<Func<IReadOnlyList<T>, IReadOnlyList<T>>> Update)
{
    public T Top => Items[^1];
    public int Count => Items.Count;

    public void Push(T item) => Update(items => [.. items, item]);

    /// <summary>Removes the top item. The root is never removed.</summary>
    public void Pop() => Update(static items => items.Count > 1 ? items.Take(items.Count - 1).ToArray() : items);

    /// <summary>Keeps the bottom <paramref name="count"/> items (at least the root).</summary>
    public void PopTo(int count) => Update(items => items.Count > Math.Max(count, 1) ? items.Take(Math.Max(count, 1)).ToArray() : items);

    public void PopToRoot() => PopTo(1);

    /// <summary>Replaces the whole stack.</summary>
    public void Reset(params T[] items) => Update(_ => items);
}
