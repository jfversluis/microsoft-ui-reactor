using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor;

/// <summary>
/// The renderer-neutral factories. Partial classes cannot span assemblies, so each
/// renderer's own <c>Factories</c> class re-exposes these next to its control factories.
/// </summary>
public static class CoreFactories
{
    public static ComponentElement Component<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>()
        where T : Component, new() =>
        new(typeof(T)) { Factory = static () => new T() };

    public static ComponentElement<TProps> Component<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T, TProps>(TProps props)
        where T : Component<TProps>, new() =>
        new(typeof(T), props) { Factory = static () => new T() };

    public static GroupElement Group(params Element?[] children) => new(children);

    public static EmptyElement Empty() => EmptyElement.Instance;

    /// <summary>An inline function component with its own hooks.</summary>
    public static FuncElement RenderEachTime(Func<RenderContext, Element?> render) => new(render);

    /// <summary>
    /// An inline function component that re-renders only when a dependency changes or its own state
    /// does. With no dependencies it renders once, then only for its own state.
    /// </summary>
    public static MemoElement Memo(Func<RenderContext, Element?> render, params object?[] dependencies) =>
        new(render, dependencies);

    /// <summary>Renders <paramref name="fallback"/> instead of <paramref name="child"/> when its subtree throws.</summary>
    public static ErrorBoundaryElement ErrorBoundary(Element? child, Func<Exception, Element?> fallback) =>
        new(child, fallback);

    /// <summary>
    /// One keyed element per item. Keys let the reconciler keep each item's state and native
    /// views when the list is reordered, inserted into or filtered.
    /// </summary>
    public static GroupElement ForEach<T>(IEnumerable<T> items, Func<T, string> key, Func<T, Element?> render) =>
        new([.. items.Select(item => (render(item) ?? EmptyElement.Instance) with { Key = key(item) })]);
}
