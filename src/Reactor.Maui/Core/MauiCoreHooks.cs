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
            => context.UseMemo(() => callback, dependencies!);

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
}
