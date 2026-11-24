namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// What the component runtime needs from a renderer's class-component element (spec 067 §11.2):
/// the props its instance receives. The renderer's record implements it explicitly.
/// </summary>
internal interface IComponentElement
{
    object? Props { get; }
}

/// <summary>A renderer's function-component element: renders against a <see cref="RenderContext"/>.</summary>
internal interface IRenderFunctionElement
{
    ReactorElement? Render(RenderContext context);
}

/// <summary>
/// A memoized function component: re-renders from its parent only when <see cref="Dependencies"/>
/// change (<c>null</c>: render once, then only on its own state changes).
/// </summary>
internal interface IMemoElement : IRenderFunctionElement
{
    object?[]? Dependencies { get; }
}

/// <summary>
/// Interface for setting props without reflection.
/// </summary>
internal interface IPropsReceiver
{
    void SetProps(object props);

    /// <summary>Boxed current props, for diagnostics (no reflection).</summary>
    object? PropsBoxed { get; }

    /// <summary>The declared props type (<c>TProps</c>), for diagnostics.</summary>
    Type PropsType { get; }
}

/// <summary>
/// Interface for comparing props without reflection (avoids per-reconcile GetMethod/Invoke overhead).
/// </summary>
internal interface IPropsComparable
{
    bool CompareProps(object? oldProps, object? newProps);
}
