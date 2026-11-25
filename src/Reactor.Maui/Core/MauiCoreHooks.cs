using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// The MAUI renderer's hooks on the shared <see cref="RenderContext"/> (spec 067 §13). Component
/// classes reach them through <see cref="Component"/>'s shortcuts.
/// </summary>
public static class MauiCoreHooks
{
    extension(RenderContext context)
    {
        /// <summary>
        /// A callback of any delegate type that keeps its identity until a dependency changes (the
        /// core's <c>UseCallback</c> takes an <see cref="Action"/>).
        /// </summary>
        public TDelegate UseCallback<TDelegate>(TDelegate callback, params object?[] dependencies)
            where TDelegate : Delegate
        {
            // A ref rather than UseMemo(() => callback, …), which would allocate a closure every render.
            var slot = context.UseRef<CallbackSlot<TDelegate>?>(null);
            if (slot.Current is not { } current || !RenderContext.DepsEqual(current.Dependencies, dependencies!))
                slot.Current = current = new CallbackSlot<TDelegate>(callback, [.. dependencies]);
            return current.Callback;
        }

        /// <summary>
        /// Root mode: creates a navigation stack with the given initial route and returns a
        /// <see cref="Navigation.NavigationHandle{TRoute}"/> that is stable across re-renders. Render it
        /// with <c>NavigationHost</c>, which also provides it to descendants, so a child component can
        /// call <c>UseNavigation&lt;TRoute&gt;()</c> (parameterless) to reach it. The component re-renders
        /// after every navigation. Off-thread navigation is scheduled on this root's render thread.
        /// </summary>
        public Navigation.NavigationHandle<TRoute> UseNavigation<TRoute>(TRoute initial) where TRoute : notnull
        {
            var stackRef = context.UseRef<Navigation.NavigationStack<TRoute>?>(null);
            stackRef.Current ??= new Navigation.NavigationStack<TRoute>(initial);

            var handleRef = context.UseRef<Navigation.NavigationHandle<TRoute>?>(null);
            handleRef.Current ??= new Navigation.NavigationHandle<TRoute>(stackRef.Current, context.RenderThreadEnqueue);

            // The latest re-render callback, so a navigation always re-renders this component.
            stackRef.Current.OnChanged = context._requestRerender;
            return handleRef.Current;
        }

        /// <summary>
        /// Child mode: the <see cref="Navigation.NavigationHandle{TRoute}"/> that the nearest
        /// <c>NavigationHost</c> above this component renders. Throws if there is none.
        /// </summary>
        public Navigation.NavigationHandle<TRoute> UseNavigation<TRoute>() where TRoute : notnull
        {
            var handle = context.UseContext(Navigation.NavigationContext<TRoute>.Instance);
            if (handle is null)
                throw new InvalidOperationException(
                    $"UseNavigation<{typeof(TRoute).Name}>() (child mode) found no ancestor NavigationHost " +
                    $"providing NavigationContext<{typeof(TRoute).Name}>. " +
                    "Ensure a parent component calls UseNavigation<T>(initialRoute) and renders a NavigationHost.");
            return handle;
        }

        /// <summary>
        /// Registers callbacks that a <c>NavigationHost</c> calls while this component is part of the
        /// page it shows (spec 069 §2.3):
        /// <list type="bullet">
        /// <item><c>onNavigatingFrom</c> — before navigating away from the page. Call <c>Cancel()</c> to block.</item>
        /// <item><c>onNavigatingTo</c> — when the page is about to become active. Call <c>Cancel()</c> to keep the previous page.</item>
        /// <item><c>onNavigatedTo</c> — after the page became active and the platform's push or pop finished.</item>
        /// <item><c>onNavigatedFrom</c> — after the page stopped being active and the platform's push or pop finished.</item>
        /// </list>
        /// Callbacks are always updated to the latest references on every render.
        /// </summary>
        public void UseNavigationLifecycle(
            Action<Navigation.NavigatingToContext>? onNavigatingTo = null,
            Action<Navigation.NavigatedToContext>? onNavigatedTo = null,
            Action<Navigation.NavigatingFromContext>? onNavigatingFrom = null,
            Action<Navigation.NavigatedFromContext>? onNavigatedFrom = null)
            => NavigationLifecycleHooks.Use(context, onNavigatingTo, onNavigatedTo, onNavigatingFrom, onNavigatedFrom);

        /// <summary>A navigation stack in state, starting at <paramref name="root"/>.</summary>
        public RouteStack<T> UseRouteStack<T>(T root)
        {
            var (items, update) = context.UseReducer<IReadOnlyList<T>>([root]);
            var stack = context.UseRef<RouteStack<T>?>(null);
            if (stack.Current is null || !ReferenceEquals(stack.Current.Items, items)) stack.Current = new RouteStack<T>(items, update);
            return stack.Current;
        }

        /// <summary>
        /// Runs <paramref name="fetch"/> after the first render and again whenever a dependency changes
        /// or <see cref="Resource{T}.Reload"/> is called, and re-renders with its progress. A fetch that
        /// is superseded or unmounted is cancelled and its result is ignored. The previous value is kept
        /// while a reload is in flight.
        /// </summary>
        public Resource<T> UseResource<T>(Func<CancellationToken, Task<T>> fetch, params object?[] dependencies)
        {
            var (state, update) = context.UseReducer(new ResourceState<T>(true, default, null));
            var (version, setVersion) = context.UseReducer(0);
            var latestFetch = context.UseRef(fetch);
            latestFetch.Current = fetch;
            var scheduler = (context.Owner as RootRenderOwner)?.Scheduler;
            object[] effectDependencies = [.. dependencies!, version];

            context.UseEffect(() =>
            {
                var cancellation = new CancellationTokenSource();
                update(static s => s.IsLoading && s.Error is null ? s : s with { IsLoading = true, Error = null });
                _ = RunAsync();
                return cancellation.Cancel;

                // Results are applied on the render thread, where cancellation also happens, so a fetch
                // superseded by a newer one can never overwrite the newer result.
                void Apply(Func<ResourceState<T>, ResourceState<T>> change)
                {
                    void Run()
                    {
                        if (!cancellation.IsCancellationRequested) update(change);
                    }
                    if (scheduler is { } s && !s.CheckAccess()) s.Post(Run);
                    else Run();
                }

                async Task RunAsync()
                {
                    try
                    {
                        var value = await latestFetch.Current(cancellation.Token).ConfigureAwait(false);
                        Apply(_ => new ResourceState<T>(false, value, null));
                    }
                    catch (Exception ex)
                    {
                        // A superseded fetch's failure (often just cancellation) is dropped by Apply.
                        Apply(s => s with { IsLoading = false, Error = ex });
                    }
                }
            }, effectDependencies);

            return new Resource<T>(state.IsLoading, state.Value, state.Error, () => setVersion(static v => v + 1));
        }
    }

    private sealed record ResourceState<T>(bool IsLoading, T? Value, Exception? Error);

    private sealed class CallbackSlot<TDelegate>(TDelegate callback, object?[] dependencies)
    {
        public TDelegate Callback { get; } = callback;
        public object?[] Dependencies { get; } = dependencies;
    }
}
