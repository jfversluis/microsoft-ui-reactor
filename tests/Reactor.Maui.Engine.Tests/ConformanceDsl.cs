using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Tests;

namespace RendererConformanceKit;

/// <summary>The MAUI engine suite's elements for the shared conformance scenarios (tests/_shared).</summary>
public static class ConformanceDsl
{
    public static Element Box(params Element?[] children) => new BoxElement(children);

    public static Element Text(string text) => new TextElement(text);

    public static Element Keyed(Element element, string key) => element with { Key = key };

    public static Element Comp<TComponent, TProps>(TProps props) where TComponent : Component<TProps>, new() =>
        CoreFactories.Component<TComponent, TProps>(props);

    public static Element Boundary(Element child, Func<Exception, Element> fallback) =>
        CoreFactories.ErrorBoundary(child, fallback);
}
