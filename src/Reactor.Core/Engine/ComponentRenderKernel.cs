using System.Runtime.CompilerServices;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// What the render kernel needs from the renderer for one component render (spec 067 §11.2): how a
/// render is wrapped and its output transformed, when effects run, and what each failure reports.
/// A struct, so each renderer's kernel is specialized for it.
/// </summary>
internal interface IComponentRenderHost
{
    /// <summary>
    /// Renders and applies the renderer's output transformation. Exactly one of <paramref name="component"/>
    /// (a class component) and <paramref name="function"/> (a function or memo element) is non-null.
    /// </summary>
    ReactorElement? Render(ReactorComponent? component, IRenderFunctionElement? function, RenderContext context);

    /// <summary>Runs the effects the render scheduled (WinUI: inline, before the subtree mounts).</summary>
    void FlushEffects(RenderContext context);

    /// <summary>An error boundary encloses this render: a failure goes to it rather than to an in-tree fallback.</summary>
    bool UnderBoundary { get; }

    /// <summary>The app already declined to handle <paramref name="error"/> where it was thrown; it passes through unreported.</summary>
    bool IsPropagating(Exception error);

    /// <summary>A failure that rethrows to the boundary or the host: report it.</summary>
    void OnRethrow(Exception error);

    /// <summary>A failure outside every boundary: report it and return the element shown in the component's place.</summary>
    ReactorElement OnFallback(Exception error, bool inEffects);

    /// <summary>Whether <see cref="OnUnhandled"/> wants to see the remaining failures before they propagate.</summary>
    bool ObservesUnhandled { get; }

    /// <summary>Any other failure (fatal, or propagating), before it is rethrown.</summary>
    void OnUnhandled(Exception error);

    /// <summary>A hook-order change during a hot-reload pass, before the context's hook state is reset.</summary>
    void OnHookOrderRetry(HookOrderException error);

    /// <summary>After the reset, before the render is retried.</summary>
    void OnHookOrderReset();
}

/// <summary>How a component render is entered.</summary>
internal enum ComponentRenderMode : byte
{
    /// <summary>The first render. The renderer installed the props; a failure is not retried.</summary>
    Mount,
    /// <summary>A re-render. Props are installed inside the render, and a hook-order change during a
    /// hot-reload pass resets the component's hooks and renders once more.</summary>
    Update,
}

/// <summary>
/// Renders one component (spec 067 §11.2): begins the render on its context, renders, flushes
/// effects, recovers from a hot-reload hook-order change, and sorts a failure into rethrow (a boundary
/// or the host handles it) or the in-tree fallback. Construction, mounting the output and the
/// renderer's traces around the whole render stay with the renderer.
/// </summary>
internal static class ComponentRenderKernel
{
    /// <summary>
    /// Renders <paramref name="node"/> for <paramref name="element"/>. Returns false, rendering
    /// nothing, when the node has neither a component nor a function element to render.
    /// </summary>
    internal static bool TryRender<THost>(
        ref THost host,
        ComponentNode node,
        ReactorElement element,
        ComponentRenderMode mode,
        Action requestRerender,
        ContextScope contextScope,
        IRenderOwner? owner,
        out ReactorElement? rendered)
        where THost : struct, IComponentRenderHost
    {
        var component = node.Component;
        var function = component is null ? element as IRenderFunctionElement : null;
        var context = component?.Context ?? (function is not null ? node.Context : null);
        if (context is null)
        {
            rendered = null;
            return false;
        }

        bool hotReloadRetried = false;
        while (true)
        {
            bool inEffects = false;
            try
            {
                // Mount installed the props before the render; an update installs them here, inside
                // the render's error handling.
                if (mode == ComponentRenderMode.Update && component is IPropsReceiver receiver
                    && element is IComponentElement { Props: { } props })
                {
                    receiver.SetProps(props);
                }

                context.BeginRender(requestRerender, contextScope, owner);
                rendered = host.Render(component, function, context);
                inEffects = true;
                host.FlushEffects(context);
            }
            // Tree-wide hot-reload hook-order recovery: an edit that reorders or retypes a
            // non-root component's hooks makes its render throw here. Inside a hot-reload pass the
            // component's hook state is reset and it renders once more, so the edit applies and the
            // subtree keeps its descendants' state instead of showing the error fallback.
            catch (HookOrderException ex) when (mode == ComponentRenderMode.Update
                && !hotReloadRetried && HotReloadState.WithinUpdatePass)
            {
                host.OnHookOrderRetry(ex);
                hotReloadRetried = true;
                context.ResetForHotReload();
                host.OnHookOrderReset();
                continue;
            }
            // Inside an error boundary the exception propagates to it, which renders its fallback.
            // Outside one, OutOfMemory/StackOverflow skip the fallback and propagate to the host,
            // which also recovers. Either way it is reported here, where the failing component is
            // known. An exception the app declined to handle was reported where it was thrown.
            catch (Exception ex) when ((host.UnderBoundary || IsFatal(ex)) && !host.IsPropagating(ex))
            {
                host.OnRethrow(ex);
                throw;
            }
            catch (Exception ex) when (!host.UnderBoundary && !IsFatal(ex) && !host.IsPropagating(ex))
            {
                rendered = host.OnFallback(ex, inEffects);
            }
            catch (Exception ex) when (host.ObservesUnhandled)
            {
                host.OnUnhandled(ex);
                throw;
            }
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsFatal(Exception error) => error is OutOfMemoryException or StackOverflowException;
}
