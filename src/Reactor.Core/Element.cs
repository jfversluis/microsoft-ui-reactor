using System.Diagnostics.CodeAnalysis;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// An immutable description of a UI node. Renderers derive their control elements
/// from this; the core only knows the composition primitives declared in this file.
/// </summary>
public abstract record Element
{
    /// <summary>Stable identity across re-renders, used to match children when a list reorders.</summary>
    public string? Key { get; init; }
}

/// <summary>Renders nothing. A <c>null</c> child is treated the same way.</summary>
public sealed record EmptyElement : Element
{
    public static readonly EmptyElement Instance = new();
}

/// <summary>Transparent grouping: the children are flattened into the parent (like a React fragment).</summary>
public sealed record GroupElement(Element?[] Children) : Element;

/// <summary>Embeds a <see cref="Component"/> class in the element tree.</summary>
public record ComponentElement(
    [param: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    [property: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    Type ComponentType,
    object? Props = null) : Element
{
    // A field rather than a positional member so it does not take part in record equality.
    internal Func<Component>? Factory;

    internal Component CreateInstance() =>
        Factory is not null ? Factory() : (Component)Activator.CreateInstance(ComponentType)!;
}

/// <summary>A <see cref="ComponentElement"/> whose props are strongly typed.</summary>
public sealed record ComponentElement<TProps> : ComponentElement
{
    public ComponentElement(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type componentType,
        TProps props) : base(componentType, props) { }

    public new TProps Props
    {
        get => (TProps)base.Props!;
        init => base.Props = value;
    }
}

/// <summary>An inline function component. It re-renders whenever its parent does.</summary>
public sealed record FuncElement(Func<RenderContext, Element?> RenderFunc) : Element;

/// <summary>
/// An inline function component that re-renders only when one of <see cref="Dependencies"/>
/// changes, or when its own state does.
/// </summary>
public sealed record MemoElement(Func<RenderContext, Element?> RenderFunc, object?[] Dependencies) : Element;

/// <summary>
/// Catches errors thrown while rendering, mounting or running effects in <see cref="Child"/>'s
/// subtree, and renders <see cref="Fallback"/> instead. The subtree is unmounted, so its effect
/// cleanups run. When the boundary itself re-renders it retries <see cref="Child"/>.
/// </summary>
public sealed record ErrorBoundaryElement(Element? Child, Func<Exception, Element?> Fallback) : Element;

public static class ElementExtensions
{
    /// <summary>Returns a copy of <paramref name="element"/> with <see cref="Element.Key"/> set.</summary>
    public static T WithKey<T>(this T element, string key) where T : Element =>
        (T)(((Element)element) with { Key = key });
}
