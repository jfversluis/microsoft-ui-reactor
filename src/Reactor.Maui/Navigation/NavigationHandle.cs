using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;

namespace Microsoft.UI.Reactor.Navigation;

/// <summary>
/// Options for a single navigation action.
/// </summary>
public sealed record NavigateOptions
{
    /// <summary>
    /// Whether the platform animates the push or pop this <c>Navigate</c> call causes.
    /// <see langword="null"/> (the default) uses the host's: <c>NavigationHost</c>'s stack
    /// <c>Animated</c>, which is <see langword="true"/> unless configured. Other changes
    /// (<c>GoBack</c>, <c>Replace</c>, <c>Reset</c>, …) always use the host's. A back gesture the
    /// user is already making can't be made non-animated.
    /// </summary>
    public bool? Animated { get; init; }

    /// <summary>
    /// When true (default), the current route is pushed onto the back stack.
    /// When false, the current route is replaced (no back stack entry created).
    /// </summary>
    public bool PushToBackStack { get; init; } = true;
}

/// <summary>
/// Event args fired after a successful navigation.
/// </summary>
public sealed record NavigationEventArgs<TRoute>(
    TRoute Route,
    TRoute PreviousRoute,
    NavigationMode Mode,
    NavigateOptions? Options = null
) where TRoute : notnull;

/// <summary>
/// The non-generic view of a <see cref="NavigationHandle{TRoute}"/> that a <c>NavigationHost</c>
/// drives (spec 069 §2.3): route changes, the lifecycle guard and the last change's details.
/// </summary>
internal interface INavigationHandle
{
    object CurrentRoute { get; }
    bool CanGoBack { get; }
    bool GoBack();
    event Action? RouteChanged;

    /// <summary>
    /// Lifecycle guard set by NavigationHost to invoke component-level
    /// <c>onNavigatingFrom</c> callbacks before stack mutation.
    /// </summary>
    Action<NavigatingFromContext>? LifecycleGuard { get; set; }

    /// <summary>Incremented by every change of the stack, so a host can tell which ones it has shown.</summary>
    long Version { get; }

    /// <summary>The mode of the last change.</summary>
    NavigationMode LastMode { get; }

    /// <summary>The options of the last change, when it was a <c>Navigate</c> call that passed some.</summary>
    NavigateOptions? LastOptions { get; }

    /// <summary>The back stack's length plus one: the routes a host declares.</summary>
    int Depth { get; }

    /// <summary>The back stack's entries, root first, then the current route.</summary>
    object RouteAt(int index);

    /// <summary>A guarded back navigation to depth <paramref name="depth"/>, where the user went back to.</summary>
    bool PopToDepth(int depth);
}

/// <summary>
/// Public API for controlling navigation. Wraps the shared core's navigation stack with a safe,
/// read-heavy interface. Obtained via the <c>UseNavigation</c> hook; render it with
/// <c>NavigationHost</c>.
/// </summary>
/// <remarks>
/// <para><b>Thread safety.</b> The mutating methods
/// (<see cref="Navigate"/>, <see cref="GoBack"/>, <see cref="GoForward"/>,
/// <see cref="Replace"/>, <see cref="Reset"/>, <see cref="PopTo"/>,
/// <see cref="SetState"/>) are thread-safe by default. When called from the render
/// thread the cost is a single thread-id compare. When called off it (e.g.
/// from <c>Task.Run</c> or after <c>await … ConfigureAwait(false)</c>) the whole
/// call &#8212; stack edit, <see cref="Navigated"/> event, and the re-render &#8212; is
/// scheduled onto the render thread of the root whose component created the handle, as a
/// <c>UseState</c> setter is. An off-thread call throws <see cref="InvalidOperationException"/>
/// instead of corrupting the back/forward stacks in two cases: when no render thread is
/// available (the handle was not created under a root), and when the scheduler refuses the work.
/// The bool-returning mutators return <see langword="true"/> when an off-thread call is accepted
/// and scheduled; the actual guard/empty-stack outcome is then resolved on the render thread.
/// Off-thread callers must therefore not treat that <see langword="true"/> as confirmation that
/// the navigation happened &#8212; only that it was scheduled. (On the render thread the bool keeps
/// its meaning: whether the navigation succeeded.)</para>
/// </remarks>
public sealed class NavigationHandle<TRoute> : INavigationHandle where TRoute : notnull
{
    private readonly NavigationStack<TRoute> _stack;

    // Captured at construction. UseNavigation creates the handle during render on the render
    // thread, so this is the thread id used by the marshal gate.
    private readonly int _uiThreadId = global::System.Environment.CurrentManagedThreadId;

    // The owning root's render-thread queue (MAUI's render thread is per root, spec 067 §13), or
    // null to fall back to the process-wide dispatcher.
    private readonly Func<Action, bool>? _enqueue;

    private long _version;
    private NavigationMode _lastMode;
    private NavigateOptions? _lastOptions;

    internal NavigationHandle(NavigationStack<TRoute> stack, Func<Action, bool>? enqueue = null)
    {
        _stack = stack;
        _enqueue = enqueue;
    }

    private bool IsOffUIThread => UIThreadMarshal.IsOffUIThread(_uiThreadId);

    // The off-thread paths build their closures in these helpers, never in the mutators: a lambda
    // over a mutator's parameters would make the mutator allocate its closure on every call.
    private bool Schedule(string operation, Action work) =>
        UIThreadMarshal.MarshalOffUIThread(_uiThreadId, $"NavigationHandle.{operation}", work, _enqueue);

    private bool ScheduleNavigate(TRoute route, NavigateOptions? options) => Schedule(nameof(Navigate), () => Navigate(route, options));
    private bool ScheduleReplace(TRoute route) => Schedule(nameof(Replace), () => Replace(route));
    private bool ScheduleReset(TRoute route) => Schedule(nameof(Reset), () => Reset(route));
    private bool SchedulePopTo(Func<TRoute, bool> predicate) => Schedule(nameof(PopTo), () => PopTo(predicate));
    private bool ScheduleSetState(NavigationState<TRoute> state) => Schedule(nameof(SetState), () => SetState(state));

    /// <summary>
    /// Non-generic route change notification for NavigationHost.
    /// Fires after every successful navigation (alongside typed <see cref="Navigated"/> event).
    /// </summary>
    internal event Action? RouteChanged;

    event Action? INavigationHandle.RouteChanged
    {
        add => RouteChanged += value;
        remove => RouteChanged -= value;
    }

    object INavigationHandle.CurrentRoute => _stack.Current;
    bool INavigationHandle.CanGoBack => _stack.CanGoBack;
    bool INavigationHandle.GoBack() => GoBack();
    long INavigationHandle.Version => _version;
    NavigationMode INavigationHandle.LastMode => _lastMode;
    NavigateOptions? INavigationHandle.LastOptions => _lastOptions;
    int INavigationHandle.Depth => _stack.Depth;
    object INavigationHandle.RouteAt(int index) => index < _stack.BackStack.Count ? _stack.BackStack[index] : _stack.Current;

    Action<NavigatingFromContext>? INavigationHandle.LifecycleGuard
    {
        get => _stack.LifecycleGuard;
        set => _stack.LifecycleGuard = value;
    }

    bool INavigationHandle.PopToDepth(int depth)
    {
        var previous = _stack.Current;
        if (!_stack.PopToIndex(depth - 1))
            return false;

        Changed(NavigationMode.Pop, null);
        Navigated?.Invoke(new NavigationEventArgs<TRoute>(_stack.Current, previous, NavigationMode.Pop));
        RouteChanged?.Invoke();
        return true;
    }

    private void Changed(NavigationMode mode, NavigateOptions? options)
    {
        _version++;
        _lastMode = mode;
        _lastOptions = options;
    }

    /// <summary>The currently active route.</summary>
    public TRoute CurrentRoute => _stack.Current;

    /// <summary>True if there are entries in the back stack.</summary>
    public bool CanGoBack => _stack.CanGoBack;

    /// <summary>True if there are entries in the forward stack.</summary>
    public bool CanGoForward => _stack.CanGoForward;

    /// <summary>Readonly view of the back stack.</summary>
    public IReadOnlyList<TRoute> BackStack => _stack.BackStack;

    /// <summary>Readonly view of the forward stack.</summary>
    public IReadOnlyList<TRoute> ForwardStack => _stack.ForwardStack;

    /// <summary>Total depth: back stack count + 1 (current).</summary>
    public int Depth => _stack.Depth;

    /// <summary>
    /// Fired after every successful navigation with details about the transition.
    /// </summary>
    public event Action<NavigationEventArgs<TRoute>>? Navigated;

    /// <summary>
    /// Navigate to a new route. By default pushes the current route onto the back stack.
    /// If <see cref="NavigateOptions.PushToBackStack"/> is false, replaces the current route instead.
    /// </summary>
    /// <returns>
    /// On the render thread, <see langword="true"/> if the navigation succeeded, or
    /// <see langword="false"/> if a guard cancelled it. When called off the render thread,
    /// <see langword="true"/> means the call was scheduled onto the render thread &#8212; not that
    /// it succeeded (see the type-level thread-safety remarks).
    /// </returns>
    public bool Navigate(TRoute route, NavigateOptions? options = null)
    {
        if (IsOffUIThread) return ScheduleNavigate(route, options);

        var previous = _stack.Current;
        var mode = options is { PushToBackStack: false } ? NavigationMode.Replace : NavigationMode.Push;
        if (!(mode == NavigationMode.Replace ? _stack.Replace(route) : _stack.Push(route)))
            return false;

        Changed(mode, options);
        MauiNavigationDiagnostics.Completed(route);
        Navigated?.Invoke(new NavigationEventArgs<TRoute>(route, previous, mode, options));
        RouteChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Go back to the previous route.
    /// </summary>
    /// <returns>
    /// On the render thread, <see langword="true"/> if navigation succeeded, or
    /// <see langword="false"/> if the back stack is empty or a guard cancelled it. When called off
    /// the render thread, <see langword="true"/> means the call was scheduled &#8212; not that it
    /// succeeded (see the type-level thread-safety remarks).
    /// </returns>
    public bool GoBack()
    {
        if (IsOffUIThread) return Schedule(nameof(GoBack), () => GoBack());

        var previous = _stack.Current;
        if (!_stack.Pop())
            return false;

        Changed(NavigationMode.Pop, null);
        Navigated?.Invoke(new NavigationEventArgs<TRoute>(_stack.Current, previous, NavigationMode.Pop));
        RouteChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Go forward to the next route in the forward stack.
    /// </summary>
    /// <returns>
    /// On the render thread, <see langword="true"/> if navigation succeeded, or
    /// <see langword="false"/> if the forward stack is empty or a guard cancelled it. When called
    /// off the render thread, <see langword="true"/> means the call was scheduled &#8212; not that
    /// it succeeded (see the type-level thread-safety remarks).
    /// </returns>
    public bool GoForward()
    {
        if (IsOffUIThread) return Schedule(nameof(GoForward), () => GoForward());

        var previous = _stack.Current;
        if (!_stack.Forward())
            return false;

        Changed(NavigationMode.Forward, null);
        Navigated?.Invoke(new NavigationEventArgs<TRoute>(_stack.Current, previous, NavigationMode.Forward));
        RouteChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Replace the current route without modifying back/forward stacks.
    /// </summary>
    /// <returns>
    /// On the render thread, <see langword="true"/> if the route was replaced, or
    /// <see langword="false"/> if a guard cancelled it. When called off the render thread,
    /// <see langword="true"/> means the call was scheduled &#8212; not that it succeeded (see the
    /// type-level thread-safety remarks).
    /// </returns>
    public bool Replace(TRoute route)
    {
        if (IsOffUIThread) return ScheduleReplace(route);

        var previous = _stack.Current;
        if (!_stack.Replace(route))
            return false;

        Changed(NavigationMode.Replace, null);
        Navigated?.Invoke(new NavigationEventArgs<TRoute>(route, previous, NavigationMode.Replace));
        RouteChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Reset the entire stack to a single root route. Clears back and forward stacks.
    /// </summary>
    /// <returns>
    /// On the render thread, <see langword="true"/> if the stack was reset, or
    /// <see langword="false"/> if a guard cancelled it. When called off the render thread,
    /// <see langword="true"/> means the call was scheduled &#8212; not that it succeeded (see the
    /// type-level thread-safety remarks).
    /// </returns>
    public bool Reset(TRoute route)
    {
        if (IsOffUIThread) return ScheduleReset(route);

        var previous = _stack.Current;
        if (!_stack.Reset(route))
            return false;

        Changed(NavigationMode.Reset, null);
        Navigated?.Invoke(new NavigationEventArgs<TRoute>(route, previous, NavigationMode.Reset));
        RouteChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Pop entries from the back stack until the predicate matches.
    /// </summary>
    /// <returns>
    /// On the render thread, <see langword="true"/> if a matching entry was popped to, or
    /// <see langword="false"/> if no back-stack entry matches the predicate or a guard cancelled
    /// it. When called off the render thread, <see langword="true"/> means the call was scheduled
    /// &#8212; not that it succeeded (see the type-level thread-safety remarks).
    /// </returns>
    public bool PopTo(Func<TRoute, bool> predicate)
    {
        if (IsOffUIThread) return SchedulePopTo(predicate);

        var previous = _stack.Current;
        if (!_stack.PopTo(predicate))
            return false;

        Changed(NavigationMode.Pop, null);
        Navigated?.Invoke(new NavigationEventArgs<TRoute>(_stack.Current, previous, NavigationMode.Pop));
        RouteChanged?.Invoke();
        return true;
    }

    // ════════════════════════════════════════════════════════════════
    //  State snapshot / restore
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns a snapshot of the full navigation state — back stack, current route,
    /// and forward stack — as a plain POCO. Persist it however you like (JSON,
    /// MessagePack, hand-rolled binary): Reactor intentionally does not pick a
    /// serialization format for you.
    /// </summary>
    /// <remarks>
    /// For JSON persistence, declare a <c>JsonSerializerContext</c> covering
    /// <see cref="NavigationState{TRoute}"/> and your route type so the call is
    /// AOT-safe. For polymorphic route hierarchies, annotate the base route type
    /// with <c>[JsonPolymorphic]</c> and <c>[JsonDerivedType]</c>.
    /// </remarks>
    public NavigationState<TRoute> GetState() => new(
        // Arrays, so a caller can't cast the snapshot's lists back to IList<TRoute> and mutate them.
        BackStack: _stack.BackStack.ToArray(),
        Current: _stack.Current,
        ForwardStack: _stack.ForwardStack.ToArray());

    /// <summary>
    /// Restores a previously captured <see cref="NavigationState{TRoute}"/>. Replaces
    /// the back stack, current route, and forward stack without running guards, then fires
    /// <see cref="Navigated"/> with <see cref="NavigationMode.Reset"/>.
    /// </summary>
    public void SetState(NavigationState<TRoute> state)
    {
        ArgumentNullException.ThrowIfNull(state);
        // Validated on the caller's thread, so an invalid snapshot fails at the call site rather
        // than later on the render thread, where the caller can't observe it.
        if (state.Current is null)
            throw new ArgumentException("Navigation state must include a non-null Current route.", nameof(state));
        if (state.BackStack is null)
            throw new ArgumentException("Navigation state must include a non-null BackStack.", nameof(state));
        if (state.ForwardStack is null)
            throw new ArgumentException("Navigation state must include a non-null ForwardStack.", nameof(state));
        if (IsOffUIThread)
        {
            // Frozen before the hop, so the caller mutating its lists meanwhile can't change what is applied.
            ScheduleSetState(new NavigationState<TRoute>(state.BackStack.ToArray(), state.Current, state.ForwardStack.ToArray()));
            return;
        }

        var previous = _stack.Current;
        _stack.RestoreState(state.BackStack, state.Current, state.ForwardStack);

        Changed(NavigationMode.Reset, null);
        Navigated?.Invoke(new NavigationEventArgs<TRoute>(state.Current, previous, NavigationMode.Reset));
        RouteChanged?.Invoke();
    }
}

/// <summary>
/// The MAUI navigation's ETW events, written where WinUI's <c>NavigationDiagnostics</c> writes them
/// (spec 044 §4.2). PII: only the destination route's type name, never the route instance.
/// </summary>
internal static class MauiNavigationDiagnostics
{
    public static void Completed(object route)
    {
        if (ReactorEventSource.Log.IsEnabled())
            ReactorEventSource.Log.NavigationCompleted(route.GetType().FullName ?? "(none)", 0d);
    }

    public static void Cancelled(object route, string reason)
    {
        if (ReactorEventSource.Log.IsEnabled())
            ReactorEventSource.Log.NavigationCancelled(route.GetType().FullName ?? "(none)", reason);
    }
}
