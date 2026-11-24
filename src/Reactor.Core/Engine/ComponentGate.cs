namespace Microsoft.UI.Reactor.Core;

/// <summary>Why a gated component renders, as the render traces report it.</summary>
internal enum ComponentGateReason : byte
{
    None,
    /// <summary>Its props (class components) or dependencies (memo components) changed.</summary>
    Props,
    /// <summary>A context value it read changed.</summary>
    Context,
    /// <summary>Its parent rendered and nothing could skip it (a propless component whose <c>ShouldUpdate()</c> is true).</summary>
    Parent,
}

/// <summary>The skip gate's decision for one component update.</summary>
internal readonly struct ComponentGateResult(bool gated, bool skip, ComponentGateReason reason)
{
    /// <summary>A gate applied: the component is a class component or a memo component.</summary>
    public bool Gated { get; } = gated;
    /// <summary>The gate lets the component skip this render.</summary>
    public bool Skip { get; } = skip;
    public ComponentGateReason Reason { get; } = reason;
}

/// <summary>
/// Decides whether a component re-renders when its parent does (spec 067 §11.2): class components
/// through <c>ShouldUpdate</c>, memo components through their dependencies, both declining the skip
/// when a context value they read changed. Function components have no gate. The caller handles the
/// bypasses (the component's own state change, a forced render).
/// </summary>
internal static class ComponentGate
{
    /// <param name="node">The mounted component.</param>
    /// <param name="newElement">The element its parent rendered for it this time.</param>
    /// <param name="scope">The context values in effect where the component renders.</param>
    internal static ComponentGateResult Evaluate(ComponentNode node, ReactorElement newElement, ContextScope scope)
    {
        if (node.Component is { } component && newElement is IComponentElement newComponentElement)
        {
            // Both are always evaluated: ShouldUpdate is app code, and the order is observable.
            bool propsChanged = component is IPropsReceiver
                ? ShouldUpdateWithProps(component, node.PreviousProps, newComponentElement.Props)
                : component.ShouldUpdate();
            bool contextChanged = HasConsumedContextChanged(node, scope);
            // Props components: a props change wins, else it was the context. Propless components
            // (ShouldUpdate() true or a context change): the context wins, else nothing could skip it.
            var reason = component is IPropsReceiver
                ? propsChanged ? ComponentGateReason.Props : ComponentGateReason.Context
                : contextChanged ? ComponentGateReason.Context : ComponentGateReason.Parent;
            return new(gated: true, skip: !propsChanged && !contextChanged, reason);
        }

        if (node.Context is not null && newElement is IMemoElement newMemo)
        {
            var oldDeps = node.MemoDependencies;
            var newDeps = newMemo.Dependencies;
            bool depsChanged = oldDeps is null && newDeps is null
                ? false // both null = render once, never re-render from parent
                : oldDeps is null || newDeps is null || !RenderContext.DepsEqual(oldDeps, newDeps);
            bool contextChanged = HasConsumedContextChanged(node, scope);
            return new(gated: true, skip: !depsChanged && !contextChanged,
                depsChanged ? ComponentGateReason.Props : ComponentGateReason.Context);
        }

        return default;
    }

    /// <summary>
    /// Stale-delegate guard (issue #151), for a class component the gate skipped: refreshes the live
    /// props on the instance so a handler that reads <c>Props.X</c> at dispatch time invokes the
    /// current delegate rather than the one captured when the component last rendered. That is what
    /// makes an always-equal callbacks slot (<c>Callbacks&lt;T&gt;</c>) safe: data fields drive the
    /// gate, but the latest callbacks are still reachable off <c>Props</c>. The skipped data fields are
    /// equal by construction, so only the excluded slots change. The new props become the baseline.
    /// </summary>
    internal static void RefreshSkippedProps(ComponentNode node, ReactorElement newElement)
    {
        if (node.Component is IPropsReceiver receiver
            && newElement is IComponentElement componentElement && componentElement.Props is { } props)
        {
            receiver.SetProps(props);
            node.PreviousProps = props;
        }
    }

    /// <summary>Whether a context value the component read in its last render has changed in <paramref name="scope"/>.</summary>
    internal static bool HasConsumedContextChanged(ComponentNode node, ContextScope scope)
    {
        var renderContext = node.Component?.Context ?? node.Context;
        if (renderContext is null) return false;

        foreach (var contextHook in renderContext.ContextHooks)
        {
            var currentValue = scope.Read(contextHook.Context);
            if (!Equals(currentValue, contextHook.LastValue))
                return true;
        }
        return false;
    }

    /// <summary>Calls ShouldUpdate(oldProps, newProps) on a Component&lt;TProps&gt; via interface dispatch.</summary>
    internal static bool ShouldUpdateWithProps(ReactorComponent component, object? oldProps, object? newProps)
    {
        if (component is IPropsComparable comparable)
            return comparable.CompareProps(oldProps, newProps);

        // Fallback: always re-render
        return true;
    }
}
