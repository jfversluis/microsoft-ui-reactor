namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// A value made available to a whole subtree without passing it through props. Provide it with
/// <see cref="Provide"/> and read it with <c>UseContext</c>; readers re-render when the provided
/// value changes, even if a component between them skipped re-rendering.
/// </summary>
public sealed class Context<T>(T defaultValue)
{
    /// <summary>The value readers get when no provider encloses them.</summary>
    public T DefaultValue { get; } = defaultValue;

    /// <summary>Makes <paramref name="value"/> the context value for <paramref name="child"/> and its descendants.</summary>
    public ContextProviderElement Provide(T value, Element? child) => new(this, value, child);
}

/// <summary>Supplies a <see cref="Context{T}"/> value to its subtree. Created by <see cref="Context{T}.Provide"/>.</summary>
public sealed record ContextProviderElement : Element
{
    internal ContextProviderElement(object context, object? value, Element? child)
    {
        Context = context;
        Value = value;
        Child = child;
    }

    internal object Context { get; }
    internal object? Value { get; }
    public Element? Child { get; init; }
}
