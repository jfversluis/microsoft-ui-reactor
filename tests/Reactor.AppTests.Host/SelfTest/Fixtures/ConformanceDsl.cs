using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;

namespace RendererConformanceKit;

/// <summary>The WinUI selftests' elements for the shared conformance scenarios (tests/_shared).</summary>
public static class ConformanceDsl
{
    public static Element Box(params Element?[] children) => Factories.VStack(children);

    public static Element Text(string text) => Factories.TextBlock(text);

    public static Element Keyed(Element element, string key) => element with { Key = key };

    public static Element Comp<TComponent, TProps>(TProps props) where TComponent : Component<TProps>, new() =>
        Factories.Component<TComponent, TProps>(props);

    public static Element Boundary(Element child, Func<Exception, Element> fallback) =>
        Factories.ErrorBoundary(child, fallback);
}
