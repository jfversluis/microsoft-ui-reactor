namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Providing a <see cref="Context{T}"/> (the core's, shared with WinUI) in a MAUI tree. Read it with
/// <c>UseContext</c>; readers re-render when the provided value changes, even if a component between
/// them skipped re-rendering.
/// </summary>
public static class ContextProviderExtensions
{
    extension<T>(Context<T> context)
    {
        /// <summary>Makes <paramref name="value"/> the context value for <paramref name="child"/> and its descendants.</summary>
        public ContextProviderElement Provide(T value, Element? child) => new(context, value, child);
    }
}

/// <summary>Supplies a <see cref="Context{T}"/> value to its subtree. Created by <c>Context&lt;T&gt;.Provide</c>.</summary>
public sealed record ContextProviderElement : Element
{
    internal ContextProviderElement(ContextBase context, object? value, Element? child)
    {
        Context = context;
        Value = value;
        Child = child;
    }

    internal ContextBase Context { get; }
    internal object? Value { get; }
    public Element? Child { get; init; }
}
