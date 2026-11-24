using Microsoft.Extensions.Logging;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core;

public sealed partial class Reconciler
{
    /// <summary>Which of the reconciler's component paths a render belongs to (they report differently).</summary>
    private enum ComponentRenderSite : byte
    {
        MountClass,
        MountFunction,
        MountMemo,
        Update,
    }

    /// <summary>
    /// WinUI's side of a component render through the core's <see cref="ComponentRenderKernel"/>
    /// (spec 067 §11.2): the validation scope and its <c>Provide</c> transformation, effect timing,
    /// and the traces, logs and fallback each path reports. Held in a local and passed by reference,
    /// so what a failure arm records (<see cref="RenderReported"/>, <see cref="RenderedStart"/>) is
    /// visible to the caller afterwards.
    /// </summary>
    private struct WinUIComponentRenderHost : IComponentRenderHost
    {
        internal Reconciler Reconciler;
        internal ComponentNode Node;
        internal Element Element;
        internal ComponentRenderSite Site;
        /// <summary>The component's anchor when it already exists (update); null on mount.</summary>
        internal UIElement? Control;
        internal bool TraceRendered;
        internal long RenderedStart;
        /// <summary>The <c>ComponentRendered</c> reason for this render.</summary>
        internal string? RenderedReason;
        /// <summary>The component's name when the update computed it for the render-start event.</summary>
        internal string? ComponentName;
        /// <summary>A failure arm already reported <c>ComponentRendered</c>.</summary>
        internal bool RenderReported;

        public ReactorElement? Render(ReactorComponent? component, IRenderFunctionElement? function, RenderContext context)
        {
            using (ValidationRenderScope.Begin(Reconciler.ReadContext(ValidationContexts.Current)))
            {
                return ValidationRenderScope.ApplyProvide(component is not null
                    ? ((Component)component).Render()
                    : (Element)function!.Render(context)!);
            }
        }

        public void FlushEffects(RenderContext context)
        {
            if (Site == ComponentRenderSite.Update)
                FlushEffectsTraced(context, ComponentName);
            else
                context.FlushEffects();
        }

        public bool UnderBoundary => Reconciler._errorBoundaryDepth > 0;

        public bool IsPropagating(Exception error) => RenderErrorDispatch.IsPropagating(error);

        public void OnRethrow(Exception error)
        {
            // The render still happened: report it, as the trailing arm does for the rest.
            if (TraceRendered)
                EmitComponentRendered(Node, null, Element, RenderedReason!, RenderedStart);
            EmitRenderError(ComponentName ?? Diagnostics.ComponentNames.For(Node.Component, Element), error);
        }

        public ReactorElement OnFallback(Exception error, bool inEffects)
        {
            // Report before building the fallback: its time is not this render's, and a
            // fallback that throws must not swallow the event.
            if (TraceRendered)
            {
                EmitComponentRendered(Node, Control, Element, RenderedReason!, RenderedStart);
                RenderReported = true;
            }
            var failedName = ComponentName ?? Diagnostics.ComponentNames.For(Node.Component, Element);
            switch (Site)
            {
                case ComponentRenderSite.MountClass:
                    Reconciler._logger?.LogError(error, "Component Render() threw during mount: {ComponentName}", failedName);
                    break;
                case ComponentRenderSite.MountFunction:
                    Reconciler._logger?.LogError(error, "FuncComponent Render() threw during mount");
                    break;
                case ComponentRenderSite.MountMemo:
                    Reconciler._logger?.LogError(error, "MemoComponent Render() threw during mount");
                    break;
                default:
                    Reconciler._logger?.LogError(error, "Component Render() threw: {ComponentName}", failedName);
                    break;
            }
            // Before the fallback: the app's handler may call Propagate(), which throws.
            EmitRenderError(failedName, error);
            return Reconciler.BuildInTreeFallback(error, inEffects, Node.Component?.GetType().Name);
        }

        public readonly bool ObservesUnhandled => TraceRendered;

        // Every other exception propagates (to an enclosing ErrorBoundary, or a fatal one to the
        // host); the render still happened, so report it. A mount's wrapper is discarded, so no
        // registry entry.
        public void OnUnhandled(Exception error)
            => EmitComponentRendered(Node, null, Element, RenderedReason!, RenderedStart);

        public void OnHookOrderRetry(HookOrderException error)
        {
            // The aborted attempt still ran Render(); report it before logging, so a slow logger
            // is not charged to it.
            if (TraceRendered)
                EmitComponentRendered(Node, null, Element, Diagnostics.ComponentRenderTrace.Reasons.HotReload, RenderedStart);
            Reconciler._logger?.LogWarning(error,
                "Hot reload: hook order/type changed in child component — " +
                "resetting state and re-rendering: {ComponentName}",
                ComponentName ?? Diagnostics.ComponentNames.For(Node.Component, Element));
        }

        // Time the retry on its own (it reports itself after the render), starting after the
        // reset ran the effect cleanups.
        public void OnHookOrderReset()
        {
            if (TraceRendered)
                RenderedStart = global::System.Diagnostics.Stopwatch.GetTimestamp();
        }
    }
}
