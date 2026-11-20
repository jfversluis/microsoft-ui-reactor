using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Per-component hook storage. Hooks are matched to their slot by call order, so the
/// sequence of hook calls must be identical on every render.
/// </summary>
public sealed class RenderContext
{
    private readonly List<object> _hooks = [];
    private int _hookIndex;
    private bool _hasRendered;
    private List<EffectHook>? _pendingEffects;

    // Wired by the reconciler when the owning node mounts.
    internal Action? RequestRerender;
    internal IRenderScheduler? Scheduler;
    internal Node? Owner;

    internal void BeginRender() => _hookIndex = 0;

    internal void EndRender()
    {
        if (_hasRendered && _hookIndex != _hooks.Count)
            throw new HookOrderException(
                $"This render called {_hookIndex} hooks but the previous render called {_hooks.Count}. " +
                "Hooks must be called unconditionally and in the same order on every render.");
        _hasRendered = true;
    }

    internal bool HasPendingEffects => _pendingEffects is { Count: > 0 };

    /// <summary>Runs effects scheduled during the last render: each one's previous cleanup first.</summary>
    internal void RunPendingEffects()
    {
        var pending = _pendingEffects;
        _pendingEffects = null;
        if (pending is null) return;
        foreach (var hook in pending)
        {
            hook.Cleanup?.Invoke();
            hook.Cleanup = null;
            if (hook.PendingEffect is { } effect) effect();
            else if (hook.PendingEffectWithCleanup is { } withCleanup) hook.Cleanup = withCleanup();
            hook.PendingEffect = null;
            hook.PendingEffectWithCleanup = null;
        }
    }

    /// <summary>
    /// Runs every effect cleanup. Called when the owning node unmounts. A throwing cleanup does
    /// not stop the others; its exception is added to <paramref name="errors"/>.
    /// </summary>
    internal void RunCleanups(List<Exception> errors)
    {
        _pendingEffects = null;
        foreach (var slot in _hooks)
        {
            if (slot is EffectHook { Cleanup: { } cleanup } hook)
            {
                hook.Cleanup = null;
                try
                {
                    cleanup();
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }
        }
    }

    public (T Value, Action<T> Set) UseState<T>(T initialValue, bool threadSafe = false)
    {
        var hook = Acquire(() => new ValueHook<T>(initialValue, threadSafe), "UseState");
        hook.StateSetter ??= value => Set(hook, value);
        return (Read(hook), hook.StateSetter);
    }

    public (T Value, Action<Func<T, T>> Update) UseReducer<T>(T initialValue, bool threadSafe = false)
    {
        var hook = Acquire(() => new ValueHook<T>(initialValue, threadSafe), "UseReducer");
        hook.Updater ??= update => Apply(hook, update);
        return (Read(hook), hook.Updater);
    }

    public (TState Value, Action<TAction> Dispatch) UseReducer<TState, TAction>(
        Func<TState, TAction, TState> reducer, TState initialValue)
    {
        var hook = Acquire(() => new ReducerHook<TState, TAction>(initialValue), "UseReducer");
        hook.Reducer = reducer;
        hook.Dispatch ??= action => Apply(hook.State, state => hook.Reducer(state, action));
        return (hook.State.Value, hook.Dispatch);
    }

    /// <summary>
    /// Runs <paramref name="effect"/> after the render commits, and again whenever a
    /// dependency changes. No dependencies means "run once after the first render".
    /// </summary>
    public void UseEffect(Action effect, params object?[] dependencies)
    {
        var hook = Acquire(() => new EffectHook(), "UseEffect");
        if (hook.Dependencies is null || !DepsEqual(hook.Dependencies, dependencies))
        {
            Schedule(hook);
            hook.Dependencies = [.. dependencies];
            hook.PendingEffect = effect;
            hook.PendingEffectWithCleanup = null;
        }
    }

    /// <summary>Like <see cref="UseEffect(Action, object?[])"/>, but the effect returns its cleanup.</summary>
    public void UseEffect(Func<Action> effectWithCleanup, params object?[] dependencies)
    {
        var hook = Acquire(() => new EffectHook(), "UseEffect");
        if (hook.Dependencies is null || !DepsEqual(hook.Dependencies, dependencies))
        {
            Schedule(hook);
            hook.Dependencies = [.. dependencies];
            hook.PendingEffect = null;
            hook.PendingEffectWithCleanup = effectWithCleanup;
        }
    }

    // A component can render more than once before effects run (a setter called during
    // render); its hook is queued once and keeps only the latest effect.
    private void Schedule(EffectHook hook)
    {
        if (hook.PendingEffect is null && hook.PendingEffectWithCleanup is null)
            (_pendingEffects ??= []).Add(hook);
    }

    public T UseMemo<T>(Func<T> factory, params object?[] dependencies)
    {
        var hook = Acquire(() => new MemoHook<T>(), "UseMemo");
        if (hook.Dependencies is null || !DepsEqual(hook.Dependencies, dependencies))
        {
            hook.Value = factory();
            hook.Dependencies = [.. dependencies];
        }
        return hook.Value;
    }

    public TDelegate UseCallback<TDelegate>(TDelegate callback, params object?[] dependencies)
        where TDelegate : Delegate
        => UseMemo(() => callback, dependencies);

    public Ref<T> UseRef<T>(T initialValue = default!)
        => Acquire(() => new Ref<T>(initialValue), "UseRef");

    /// <summary>
    /// Reads the value of the nearest enclosing <see cref="Context{T}.Provide"/>, or the context's
    /// default. The component re-renders when that provider's value changes.
    /// </summary>
    public T UseContext<T>(Context<T> context)
    {
        Acquire(static () => new ContextHook(), "UseContext");
        return Owner is null ? context.DefaultValue : Owner.ReadContext(context);
    }

    /// <summary>
    /// Runs <paramref name="fetch"/> after the first render and again whenever a dependency changes
    /// or <see cref="Resource{T}.Reload"/> is called, and re-renders with its progress. A fetch that
    /// is superseded or unmounted is cancelled and its result is ignored. The previous value is kept
    /// while a reload is in flight.
    /// </summary>
    public Resource<T> UseResource<T>(Func<CancellationToken, Task<T>> fetch, params object?[] dependencies)
    {
        var (state, update) = UseReducer(new ResourceState<T>(true, default, null), threadSafe: true);
        var (version, setVersion) = UseReducer(0);
        var latestFetch = UseRef(fetch);
        latestFetch.Current = fetch;

        UseEffect(() =>
        {
            var cancellation = new CancellationTokenSource();
            update(static s => s.IsLoading && s.Error is null ? s : s with { IsLoading = true, Error = null });
            _ = RunAsync();
            return cancellation.Cancel;

            async Task RunAsync()
            {
                try
                {
                    var value = await latestFetch.Current(cancellation.Token).ConfigureAwait(false);
                    if (!cancellation.IsCancellationRequested) update(_ => new ResourceState<T>(false, value, null));
                }
                catch (Exception ex) when (!cancellation.IsCancellationRequested)
                {
                    update(s => s with { IsLoading = false, Error = ex });
                }
                catch (OperationCanceledException)
                {
                }
            }
        }, [.. dependencies, version]);

        return new Resource<T>(state.IsLoading, state.Value, state.Error, () => setVersion(static v => v + 1));
    }

    private THook Acquire<THook>(Func<THook> create, string hookName) where THook : class
    {
        var index = _hookIndex++;
        if (index >= _hooks.Count)
        {
            if (_hasRendered)
                throw new HookOrderException(
                    $"{hookName} was called as hook #{index + 1}, but the previous render only called {_hooks.Count} hooks. " +
                    "Hooks must be called unconditionally and in the same order on every render.");
            var created = create();
            _hooks.Add(created);
            return created;
        }

        return _hooks[index] as THook ?? throw new HookOrderException(
            $"Hook #{index + 1} was {_hooks[index].GetType().Name} on the previous render but {hookName} on this one. " +
            "Hooks must be called unconditionally and in the same order on every render.");
    }

    private static T Read<T>(ValueHook<T> hook)
    {
        if (!hook.ThreadSafe) return hook.Value;
        lock (hook) return hook.Value;
    }

    private void Set<T>(ValueHook<T> hook, T value) => Apply(hook, _ => value);

    // Non-thread-safe state is only ever touched on the render thread: off-thread writes
    // are posted there. Thread-safe state is written in place under the hook's lock.
    private void Apply<T>(ValueHook<T> hook, Func<T, T> update)
    {
        bool changed;
        if (hook.ThreadSafe)
        {
            lock (hook) changed = Assign(hook, update(hook.Value));
        }
        else
        {
            if (Scheduler is { } scheduler && !scheduler.CheckAccess())
            {
                scheduler.Post(() => Apply(hook, update));
                return;
            }
            changed = Assign(hook, update(hook.Value));
        }

        if (changed) RequestRerender?.Invoke();
    }

    private static bool Assign<T>(ValueHook<T> hook, T value)
    {
        if (EqualityComparer<T>.Default.Equals(hook.Value, value)) return false;
        hook.Value = value;
        return true;
    }

    private static bool DepsEqual(object?[] previous, object?[] next)
    {
        if (previous.Length != next.Length) return false;
        for (var i = 0; i < previous.Length; i++)
            if (!Equals(previous[i], next[i])) return false;
        return true;
    }

    private sealed class ValueHook<T>(T value, bool threadSafe)
    {
        public T Value = value;
        public readonly bool ThreadSafe = threadSafe;
        public Action<T>? StateSetter;
        public Action<Func<T, T>>? Updater;
    }

    private sealed class ReducerHook<TState, TAction>(TState initialValue)
    {
        public readonly ValueHook<TState> State = new(initialValue, threadSafe: false);
        public Func<TState, TAction, TState> Reducer = (state, _) => state;
        public Action<TAction>? Dispatch;
    }

    private sealed class EffectHook
    {
        public object?[]? Dependencies;
        public Action? PendingEffect;
        public Func<Action>? PendingEffectWithCleanup;
        public Action? Cleanup;
    }

    private sealed class ContextHook;

    private sealed record ResourceState<T>(bool IsLoading, T? Value, Exception? Error);

    private sealed class MemoHook<T>
    {
        public T Value = default!;
        public object?[]? Dependencies;
    }
}
