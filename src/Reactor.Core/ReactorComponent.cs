namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// The framework-neutral base of every stateful component (spec 067). A component holds a
/// <see cref="RenderContext"/> that tracks its hook state across re-renders. A renderer derives
/// its own component base from it (WinUI's <c>Component</c>, whose <c>Render</c> returns its
/// <c>Element</c>), adding that framework's hook shortcuts.
/// </summary>
// <snippet:render-loop>
public abstract class ReactorComponent
{
    // Settable so the reconciler can transfer a live RenderContext (hooks +
    // cleanups) onto a freshly-constructed instance when a Hot Reload edit
    // mints a new component Type token (spec 049 §7 subtree migration). Outside
    // that path the value is the per-instance context created here.
    internal RenderContext Context { get; set; } = new();

    /// <summary>
    /// Override to describe the UI. Use UseState, UseEffect, etc. from the context.
    /// Must call hooks in the same order every render.
    /// </summary>
    public abstract ReactorElement? Render();

    /// <summary>
    /// Controls whether this propless component should re-render when its parent re-renders.
    /// Default: false — propless components only re-render from their own state changes or context changes.
    /// Override and return true to always re-render when the parent re-renders.
    /// </summary>
    protected internal virtual bool ShouldUpdate() => false;
// </snippet:render-loop>

    // ── Hook convenience methods (delegate to Context) ─────────────

    protected (T Value, Action<T> Set) UseState<T>(T initialValue, bool threadSafe = false)
        => Context.UseState(initialValue, threadSafe);

    protected (T Value, Action<Func<T, T>> Update) UseReducer<T>(T initialValue, bool threadSafe = false)
        => Context.UseReducer(initialValue, threadSafe);

    /// <summary>
    /// Redux-style reducer: takes a reducer function (state, action) => newState.
    /// Returns (currentState, dispatch) where dispatch sends an action through the reducer.
    /// </summary>
    protected (TState Value, Action<TAction> Dispatch) UseReducer<TState, TAction>(
        Func<TState, TAction, TState> reducer, TState initialValue, bool threadSafe = false)
        => Context.UseReducer(reducer, initialValue, threadSafe);

    protected void UseEffect(Action effect, params object[] dependencies)
        => Context.UseEffect(effect, dependencies);

    /// <summary>Typed-arity <see cref="UseEffect(Action, object[])"/> — passes one dependency
    /// positionally, avoiding the <c>params object[]</c> allocation and value-type boxing.</summary>
    protected void UseEffect<T1>(Action effect, T1 d1)
        => Context.UseEffect(effect, d1);

    /// <inheritdoc cref="UseEffect{T1}(Action, T1)"/>
    protected void UseEffect<T1, T2>(Action effect, T1 d1, T2 d2)
        => Context.UseEffect(effect, d1, d2);

    /// <inheritdoc cref="UseEffect{T1}(Action, T1)"/>
    protected void UseEffect<T1, T2, T3>(Action effect, T1 d1, T2 d2, T3 d3)
        => Context.UseEffect(effect, d1, d2, d3);

    protected void UseEffect(Func<Action> effectWithCleanup, params object[] dependencies)
        => Context.UseEffect(effectWithCleanup, dependencies);

    /// <inheritdoc cref="UseEffect{T1}(Action, T1)"/>
    protected void UseEffect<T1>(Func<Action> effectWithCleanup, T1 d1)
        => Context.UseEffect(effectWithCleanup, d1);

    /// <inheritdoc cref="UseEffect{T1}(Action, T1)"/>
    protected void UseEffect<T1, T2>(Func<Action> effectWithCleanup, T1 d1, T2 d2)
        => Context.UseEffect(effectWithCleanup, d1, d2);

    /// <inheritdoc cref="UseEffect{T1}(Action, T1)"/>
    protected void UseEffect<T1, T2, T3>(Func<Action> effectWithCleanup, T1 d1, T2 d2, T3 d3)
        => Context.UseEffect(effectWithCleanup, d1, d2, d3);

    protected T UseMemo<T>(Func<T> factory, params object[] dependencies)
        => Context.UseMemo(factory, dependencies);

    /// <summary>Typed-arity <see cref="UseMemo{T}(Func{T}, object[])"/> — passes one dependency
    /// positionally, avoiding the <c>params object[]</c> allocation and value-type boxing.</summary>
    protected T UseMemo<T, T1>(Func<T> factory, T1 d1)
        => Context.UseMemo(factory, d1);

    /// <inheritdoc cref="UseMemo{T, T1}(Func{T}, T1)"/>
    protected T UseMemo<T, T1, T2>(Func<T> factory, T1 d1, T2 d2)
        => Context.UseMemo(factory, d1, d2);

    /// <inheritdoc cref="UseMemo{T, T1}(Func{T}, T1)"/>
    protected T UseMemo<T, T1, T2, T3>(Func<T> factory, T1 d1, T2 d2, T3 d3)
        => Context.UseMemo(factory, d1, d2, d3);

    protected Action UseCallback(Action callback, params object[] dependencies)
        => Context.UseCallback(callback, dependencies);

    /// <summary>Typed-arity <see cref="UseCallback(Action, object[])"/> — passes one dependency
    /// positionally, avoiding the <c>params object[]</c> allocation and value-type boxing.</summary>
    protected Action UseCallback<T1>(Action callback, T1 d1)
        => Context.UseCallback(callback, d1);

    /// <inheritdoc cref="UseCallback{T1}(Action, T1)"/>
    protected Action UseCallback<T1, T2>(Action callback, T1 d1, T2 d2)
        => Context.UseCallback(callback, d1, d2);

    /// <inheritdoc cref="UseCallback{T1}(Action, T1)"/>
    protected Action UseCallback<T1, T2, T3>(Action callback, T1 d1, T2 d2, T3 d3)
        => Context.UseCallback(callback, d1, d2, d3);

    protected Ref<T> UseRef<T>(T initialValue = default!)
        => Context.UseRef(initialValue);

    protected T UseObservable<T>(T source) where T : global::System.ComponentModel.INotifyPropertyChanged
        => Context.UseObservable(source);

    protected TSnapshot UseExternalStore<TSnapshot>(
        Func<Action, Action> subscribe,
        Func<TSnapshot> getSnapshot,
        IEqualityComparer<TSnapshot>? comparer = null)
        => Context.UseExternalStore(subscribe, getSnapshot, comparer);

    protected TProp UseObservableProperty<T, TProp>(T source, Func<T, TProp> selector, string propertyName)
        where T : global::System.ComponentModel.INotifyPropertyChanged
        => Context.UseObservableProperty(source, selector, propertyName);

    protected IReadOnlyList<T> UseCollection<T>(global::System.Collections.ObjectModel.ObservableCollection<T> collection)
        => Context.UseCollection(collection);

    protected T UseContext<T>(Context<T> context)
        => Context.UseContext(context);

    protected (T Value, Action<T> Set) UsePersisted<T>(string key, T initialValue)
        => Context.UsePersisted(key, initialValue);

    /// <summary>
    /// Persisted-state hook with explicit scope (spec 033 §2). Prefer this over
    /// the two-arg overload in new code so the call site documents whether the
    /// state is per-window or process-wide.
    /// </summary>
    protected (T Value, Action<T> Set) UsePersisted<T>(string key, T initialValue, PersistedScope scope)
        => Context.UsePersisted(key, initialValue, scope);
}
