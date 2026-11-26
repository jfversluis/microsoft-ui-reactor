using Microsoft.UI.Reactor.Maui.Hosting;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Base class for stateful MAUI components. Hook state lives in a <see cref="RenderContext"/> and is
/// tracked by call order, so hooks must be called unconditionally and in the same order on every
/// render. The shared hooks (<c>UseState</c>, <c>UseEffect</c>, …) come from
/// <see cref="ReactorComponent"/>, the core base shared with WinUI (spec 067 §13); this class narrows
/// <see cref="Render"/> to the MAUI <see cref="Element"/> and adds the MAUI renderer's hooks.
/// </summary>
public abstract class Component : ReactorComponent
{
    /// <summary>Describes the UI. Return <c>null</c> to render nothing.</summary>
    public abstract override Element? Render();

    /// <summary>
    /// A callback of any delegate type that keeps its identity until a dependency changes, so a
    /// component can memoize an <c>Action&lt;int&gt;</c> or a <c>Func&lt;T&gt;</c> as well as an
    /// <see cref="Action"/>.
    /// </summary>
    protected TDelegate UseCallback<TDelegate>(TDelegate callback, params object?[] dependencies)
        where TDelegate : Delegate
        => Context.UseCallback(callback, dependencies);

    // Declaring UseCallback<TDelegate> here hides ReactorComponent's UseCallback overloads (C# drops
    // base candidates once a derived one applies), so they are forwarded: an Action callback keeps
    // binding to the core's typed, allocation-free forms, as in a WinUI component.
    protected new Action UseCallback(Action callback, params object[] dependencies)
        => Context.UseCallback(callback, dependencies);

    protected new Action UseCallback<T1>(Action callback, T1 d1)
        => Context.UseCallback(callback, d1);

    protected new Action UseCallback<T1, T2>(Action callback, T1 d1, T2 d2)
        => Context.UseCallback(callback, d1, d2);

    protected new Action UseCallback<T1, T2, T3>(Action callback, T1 d1, T2 d2, T3 d3)
        => Context.UseCallback(callback, d1, d2, d3);

    /// <summary>The app's current theme (light or dark). Re-renders when it changes.</summary>
    /// <inheritdoc cref="MauiCoreHooks.UseElementRef{T}(RenderContext)"/>
    protected Microsoft.UI.Reactor.Input.ElementRef<T> UseElementRef<T>() where T : class, IView => Context.UseElementRef<T>();

    /// <inheritdoc cref="MauiCoreHooks.UseElementFocus(RenderContext)"/>
    protected (Microsoft.UI.Reactor.Input.ElementRef<IView> Ref, Action RequestFocus) UseElementFocus() => Context.UseElementFocus();

    protected AppTheme UseAppTheme() => Context.UseAppTheme();

    /// <summary>The size of the window the tree is shown in, or (0, 0) before it is known.</summary>
    protected Size UseWindowSize() => Context.UseWindowSize();

    /// <summary>MAUI's page dialogs for this component's window, stable across renders.</summary>
    protected Maui.Dialogs UseDialogs() => Context.UseDialogs();

    /// <summary>Registers a handler for the system back key; return true when it handled the back.</summary>
    protected void UseSystemBackButton(Func<bool> onBack) => Context.UseSystemBackButton(onBack);

    /// <summary>The system back key goes back in <paramref name="nav"/> whenever it can go back.</summary>
    protected void UseSystemBackButton<TRoute>(Navigation.NavigationHandle<TRoute> nav) where TRoute : notnull
        => Context.UseSystemBackButton(nav);

    /// <summary>Subscribes to the window's lifecycle events while this component is mounted.</summary>
    protected void UseWindowLifecycle(
        Action? onActivated = null,
        Action? onDeactivated = null,
        Action? onStopped = null,
        Action? onResumed = null,
        Action? onDestroying = null)
        => Context.UseWindowLifecycle(onActivated, onDeactivated, onStopped, onResumed, onDestroying);

    protected RouteStack<T> UseRouteStack<T>(T root)
        => Context.UseRouteStack(root);

    protected Resource<T> UseResource<T>(Func<CancellationToken, Task<T>> fetch, params object?[] dependencies)
        => Context.UseResource(fetch, dependencies);

    /// <summary>Root mode: a navigation stack starting at <paramref name="initial"/>, stable across renders. Render it with <c>NavigationHost</c>.</summary>
    protected Navigation.NavigationHandle<TRoute> UseNavigation<TRoute>(TRoute initial) where TRoute : notnull
        => Context.UseNavigation(initial);

    /// <summary>Child mode: the navigation handle the nearest <c>NavigationHost</c> above provides.</summary>
    protected Navigation.NavigationHandle<TRoute> UseNavigation<TRoute>() where TRoute : notnull
        => Context.UseNavigation<TRoute>();

    /// <summary>Registers navigation lifecycle callbacks for the page this component is part of.</summary>
    protected void UseNavigationLifecycle(
        Action<Navigation.NavigatingToContext>? onNavigatingTo = null,
        Action<Navigation.NavigatedToContext>? onNavigatedTo = null,
        Action<Navigation.NavigatingFromContext>? onNavigatingFrom = null,
        Action<Navigation.NavigatedFromContext>? onNavigatedFrom = null)
        => Context.UseNavigationLifecycle(onNavigatingTo, onNavigatedTo, onNavigatingFrom, onNavigatedFrom);
}

/// <summary>A component that receives typed props from its parent.</summary>
public abstract class Component<TProps> : Component, IPropsReceiver, IPropsComparable
{
    public TProps Props { get; private set; } = default!;

    void IPropsReceiver.SetProps(object props) => Props = (TProps)props;

    object? IPropsReceiver.PropsBoxed => Props;

    Type IPropsReceiver.PropsType => typeof(TProps);

    bool IPropsComparable.CompareProps(object? oldProps, object? newProps)
        => ShouldUpdate((TProps?)oldProps, (TProps?)newProps);

    /// <summary>
    /// Whether to re-render when the parent re-renders. Default: the props differ by
    /// <see cref="object.Equals(object?, object?)"/>, which is structural for records.
    /// </summary>
    protected virtual bool ShouldUpdate(TProps? oldProps, TProps? newProps) => !Equals(oldProps, newProps);
}
