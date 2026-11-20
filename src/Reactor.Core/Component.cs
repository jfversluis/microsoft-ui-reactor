namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Base class for stateful components. Hook state lives in <see cref="Context"/> and is
/// tracked by call order, so hooks must be called unconditionally and in the same order
/// on every render.
/// </summary>
public abstract class Component
{
    internal RenderContext Context { get; } = new();

    /// <summary>Describes the UI. Return <c>null</c> to render nothing.</summary>
    public abstract Element? Render();

    /// <summary>
    /// Whether a component without props re-renders when its parent re-renders. Default
    /// <c>false</c>: it only re-renders from its own state changes.
    /// </summary>
    protected internal virtual bool ShouldUpdate() => false;

    protected (T Value, Action<T> Set) UseState<T>(T initialValue, bool threadSafe = false)
        => Context.UseState(initialValue, threadSafe);

    protected (T Value, Action<Func<T, T>> Update) UseReducer<T>(T initialValue, bool threadSafe = false)
        => Context.UseReducer(initialValue, threadSafe);

    protected (TState Value, Action<TAction> Dispatch) UseReducer<TState, TAction>(
        Func<TState, TAction, TState> reducer, TState initialValue)
        => Context.UseReducer(reducer, initialValue);

    protected void UseEffect(Action effect, params object?[] dependencies)
        => Context.UseEffect(effect, dependencies);

    protected void UseEffect(Func<Action> effectWithCleanup, params object?[] dependencies)
        => Context.UseEffect(effectWithCleanup, dependencies);

    protected T UseMemo<T>(Func<T> factory, params object?[] dependencies)
        => Context.UseMemo(factory, dependencies);

    protected TDelegate UseCallback<TDelegate>(TDelegate callback, params object?[] dependencies)
        where TDelegate : Delegate
        => Context.UseCallback(callback, dependencies);

    protected Ref<T> UseRef<T>(T initialValue = default!)
        => Context.UseRef(initialValue);
}

internal interface IPropsReceiver
{
    void SetProps(object? props);
    bool ShouldUpdate(object? oldProps, object? newProps);
}

/// <summary>A component that receives typed props from its parent.</summary>
public abstract class Component<TProps> : Component, IPropsReceiver
{
    public TProps Props { get; private set; } = default!;

    void IPropsReceiver.SetProps(object? props) => Props = (TProps)props!;

    bool IPropsReceiver.ShouldUpdate(object? oldProps, object? newProps)
        => ShouldUpdate((TProps?)oldProps, (TProps?)newProps);

    /// <summary>
    /// Whether to re-render when the parent re-renders. Default: the props differ by
    /// <see cref="object.Equals(object?, object?)"/>, which is structural for records.
    /// </summary>
    protected virtual bool ShouldUpdate(TProps? oldProps, TProps? newProps) => !Equals(oldProps, newProps);
}

/// <summary>A mutable box that survives re-renders without triggering one.</summary>
public sealed class Ref<T>(T initialValue)
{
    public T Current { get; set; } = initialValue;
}

public sealed class HookOrderException(string message) : InvalidOperationException(message);
