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
}
