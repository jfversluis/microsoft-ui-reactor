using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Navigation;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// The navigation lifecycle hook's state and registration, shared by every renderer (spec 069
/// §2.3). Each renderer exposes the public <c>UseNavigationLifecycle</c> hook over
/// <see cref="Use"/>, and its navigation host finds the active page's callbacks with
/// <c>GetNavigationLifecycleHook</c>.
/// </summary>
internal static class NavigationLifecycleHooks
{
    /// <summary>Registers (or refreshes) the calling component's navigation lifecycle callbacks.</summary>
    public static void Use(
        RenderContext ctx,
        Action<NavigatingToContext>? onNavigatingTo = null,
        Action<NavigatedToContext>? onNavigatedTo = null,
        Action<NavigatingFromContext>? onNavigatingFrom = null,
        Action<NavigatedFromContext>? onNavigatedFrom = null)
    {
        if (ctx._hookIndex >= ctx._hooks.Count)
        {
            ctx._hooks.Add(new NavigationLifecycleHookState());
        }

        if (ctx._hooks[ctx._hookIndex] is not NavigationLifecycleHookState hook)
            throw new HookOrderException(
                $"Hook at index {ctx._hookIndex} is {ctx._hooks[ctx._hookIndex].GetType().Name}, expected NavigationLifecycleHookState. " +
                "Hooks must be called in the same order every render.");
        ctx._hookIndex++;

        // Always update to latest callbacks so closures capture current state
        hook.OnNavigatingTo = onNavigatingTo;
        hook.OnNavigatedTo = onNavigatedTo;
        hook.OnNavigatingFrom = onNavigatingFrom;
        hook.OnNavigatedFrom = onNavigatedFrom;
    }

    extension(RenderContext ctx)
    {
        internal NavigationLifecycleHookState? GetNavigationLifecycleHook()
        {
            for (int i = 0; i < ctx._hooks.Count; i++)
            {
                if (ctx._hooks[i] is NavigationLifecycleHookState hook)
                    return hook;
            }
            return null;
        }
    }
}

internal sealed class NavigationLifecycleHookState : RenderContext.HookState
{
    internal override HookKind DiagnosticKind => HookKind.NavigationLifecycle;
    public Action<NavigatingToContext>? OnNavigatingTo;
    public Action<NavigatedToContext>? OnNavigatedTo;
    public Action<NavigatingFromContext>? OnNavigatingFrom;
    public Action<NavigatedFromContext>? OnNavigatedFrom;
}
