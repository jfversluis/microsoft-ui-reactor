using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Hooks;

namespace Microsoft.UI.Reactor.Core;

// AI-HINT: RenderContext itself lives in the framework-neutral core (src/Reactor.Core, spec 067).
//   The hooks below need WinUI (windows, pickers, navigation, theme and accessibility settings,
//   commands, localization), so they are C# 14 extension members: call sites keep the
//   `ctx.UseWindow()` / `RenderContext.PickerService` spelling.

/// <summary>
/// The WinUI hooks on <see cref="RenderContext"/>: windowing, pickers, navigation, theme and
/// accessibility settings, commands and localization (spec 067).
/// </summary>
public static partial class WinUIHooks
{
    extension(RenderContext ctx)
    {
        /// <summary>
        /// The <see cref="Microsoft.UI.Reactor.ReactorWindow"/> whose host renders this context, as
        /// recorded by that host's reconciler at the last <c>BeginRender</c>; <c>null</c> when the
        /// content has no owning <c>ReactorWindow</c> (tray flyout, <c>ReactorHostControl</c>, a
        /// <c>ReactorHost</c> constructed over a raw <c>Window</c>) or after unmount. Read by the window
        /// hooks during render. Deliberately no fallback to <c>ReactorApp.ActiveHostInternal</c>, which
        /// would name an unrelated host there. The pickers instead resolve a window id from the owner
        /// itself, so they also work in content without a <c>ReactorWindow</c>.
        /// </summary>
        internal Microsoft.UI.Reactor.ReactorWindow? OwningReactorWindow => (ctx.Owner as IContentOwner)?.ReactorWindow;

        // Not in the core yet: ObservableTreeTracker marshals through the current thread's
        // DispatcherQueue, a seam the core doesn't have (spec 067).
        /// <summary>
        /// Observes an object and all nested INotifyPropertyChanged values
        /// reachable through its properties. Re-renders when any property
        /// at any depth changes. Automatically subscribes/unsubscribes as
        /// property values change.
        /// </summary>
        public T UseObservableTree<T>(T source) where T : global::System.ComponentModel.INotifyPropertyChanged
        {
            var (_, forceRender) = ctx.UseReducer(false);
            var trackerRef = ctx.UseRef<ObservableTreeTracker?>(null);

            ctx.UseEffect(() =>
            {
                var tracker = new ObservableTreeTracker(() => forceRender(v => !v));
                trackerRef.Current = tracker;
                tracker.SyncSubscriptions(source);
                return () => tracker.Dispose();
            }, source);

            return source;
        }

        // ════════════════════════════════════════════════════════════════
        //  Navigation hooks
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Root mode: creates a navigation stack with the given initial route.
        /// Returns a stable <see cref="Navigation.NavigationHandle{TRoute}"/> across re-renders.
        /// Wire this handle to a <c>NavigationHost</c> in the DSL to render route content.
        /// The handle is automatically provided to descendants via context so child components
        /// can call <c>UseNavigation&lt;TRoute&gt;()</c> (parameterless) to access it.
        /// </summary>
        public Navigation.NavigationHandle<TRoute> UseNavigation<TRoute>(TRoute initial) where TRoute : notnull
        {
            var stackRef = ctx.UseRef<Navigation.NavigationStack<TRoute>?>(null);
            if (stackRef.Current is null)
                stackRef.Current = new Navigation.NavigationStack<TRoute>(initial);

            var handleRef = ctx.UseRef<Navigation.NavigationHandle<TRoute>?>(null);
            if (handleRef.Current is null)
                handleRef.Current = new Navigation.NavigationHandle<TRoute>(stackRef.Current);

            // Capture the latest rerender callback every render so navigation mutations
            // that originate from event handlers always trigger a re-render of this component.
            stackRef.Current.OnChanged = ctx._requestRerender;

            return handleRef.Current;
        }

        /// <summary>
        /// Child mode: retrieves an ancestor's <see cref="Navigation.NavigationHandle{TRoute}"/>
        /// from context. Throws if no ancestor provides one (i.e., no root <c>UseNavigation</c>
        /// with a <c>NavigationHost</c> exists above this component in the tree).
        /// </summary>
        public Navigation.NavigationHandle<TRoute> UseNavigation<TRoute>() where TRoute : notnull
        {
            var handle = ctx.UseContext(Navigation.NavigationContext<TRoute>.Instance);
            if (handle is null)
                throw new InvalidOperationException(
                    $"ctx.UseNavigation<{typeof(TRoute).Name}>() (child mode) found no ancestor NavigationHost " +
                    $"providing NavigationContext<{typeof(TRoute).Name}>. " +
                    "Ensure a parent component calls ctx.UseNavigation<T>(initialRoute) and renders a NavigationHost.");
            return handle;
        }

        // ════════════════════════════════════════════════════════════════
        //  Navigation system back button
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Subscribes to Alt+Left and VirtualKey.GoBack keyboard events on the given window's content
        /// to call <see cref="Navigation.NavigationHandle{TRoute}.GoBack"/>. Unsubscribes on unmount.
        /// </summary>
        public void UseSystemBackButton<TRoute>(
            Navigation.NavigationHandle<TRoute> nav,
            Microsoft.UI.Xaml.Window window) where TRoute : notnull
        {
            ctx.UseEffect(() =>
            {
                void handler(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
                {
                    if (e.Key == global::Windows.System.VirtualKey.GoBack ||
                        (e.Key == global::Windows.System.VirtualKey.Left &&
                         Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(global::Windows.System.VirtualKey.Menu)
                             .HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down)))
                    {
                        if (nav.CanGoBack)
                        {
                            nav.GoBack();
                            e.Handled = true;
                        }
                    }
                }

                if (window.Content is Microsoft.UI.Xaml.UIElement rootElement)
                {
                    rootElement.KeyDown += handler;
                    return () => rootElement.KeyDown -= handler;
                }
                return () => { };
            }, nav, window);
        }

        // ════════════════════════════════════════════════════════════════
        //  Navigation lifecycle hooks
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Registers lifecycle callbacks that fire during navigation events.
        /// <list type="bullet">
        /// <item><c>onNavigatedTo</c> — fires after this page becomes active.</item>
        /// <item><c>onNavigatingFrom</c> — fires before navigating away. Call <c>ctx.Cancel()</c> to block.</item>
        /// <item><c>onNavigatedFrom</c> — fires after this page is no longer active.</item>
        /// </list>
        /// Callbacks are always updated to the latest references on every render.
        /// </summary>
        public void UseNavigationLifecycle(
            Action<Navigation.NavigatingToContext>? onNavigatingTo = null,
            Action<Navigation.NavigatedToContext>? onNavigatedTo = null,
            Action<Navigation.NavigatingFromContext>? onNavigatingFrom = null,
            Action<Navigation.NavigatedFromContext>? onNavigatedFrom = null)
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

        /// <summary>
        /// Returns the navigation lifecycle hook state if one was registered, or null.
        /// Used by the reconciler to collect lifecycle callbacks from a component tree.
        /// </summary>
        internal NavigationLifecycleHookState? GetNavigationLifecycleHook()
        {
            for (int i = 0; i < ctx._hooks.Count; i++)
            {
                if (ctx._hooks[i] is NavigationLifecycleHookState hook)
                    return hook;
            }
            return null;
        }

        // ════════════════════════════════════════════════════════════════
        //  Color scheme hooks
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns the app-global <see cref="ColorScheme"/> — <see cref="ColorScheme.Light"/>
        /// or <see cref="ColorScheme.Dark"/>, read from
        /// <c>Application.Current.RequestedTheme</c>.
        /// <para>
        /// The value is re-evaluated on every render — when the app theme changes,
        /// <see cref="Microsoft.UI.Reactor.Hosting.ReactorHost"/> triggers a re-render so this hook
        /// naturally picks up the new value.
        /// </para>
        /// <para>
        /// This hook does <b>not</b> observe per-element <c>RequestedTheme</c> overrides: it
        /// consults the application, not the calling component's position in the tree, so a
        /// component inside a <c>.RequestedTheme(Dark)</c> subtree still reports the app theme.
        /// It also never returns <see cref="ColorScheme.HighContrast"/> in a running app — use
        /// <see cref="UseHighContrast"/> for forced-colors mode.
        /// </para>
        /// </summary>
        public ColorScheme UseColorScheme()
        {
            // App-global only. Application.Current.RequestedTheme is Light or Dark, so
            // FromActualTheme never reaches its ElementTheme.Default branch (and therefore
            // never reports HighContrast) unless Application.Current is null. Reading the
            // calling component's FrameworkElement.ActualTheme instead would make this
            // subtree-aware, but RenderContext has no element reference at hook time.
            var theme = Microsoft.UI.Xaml.Application.Current?.RequestedTheme;
            var elementTheme = theme switch
            {
                Microsoft.UI.Xaml.ApplicationTheme.Dark => Microsoft.UI.Xaml.ElementTheme.Dark,
                Microsoft.UI.Xaml.ApplicationTheme.Light => Microsoft.UI.Xaml.ElementTheme.Light,
                _ => Microsoft.UI.Xaml.ElementTheme.Default,
            };
            return ColorSchemeContext.FromActualTheme(elementTheme);
        }

        /// <summary>
        /// Convenience wrapper — returns <c>true</c> when the app-global color
        /// scheme is <see cref="ColorScheme.Dark"/>. Exactly
        /// <c>UseColorScheme() == ColorScheme.Dark</c>, so it inherits that hook's
        /// semantics: it does not observe per-element <c>RequestedTheme</c>.
        /// </summary>
        public bool UseIsDarkTheme() => ctx.UseColorScheme() == ColorScheme.Dark;

        // ════════════════════════════════════════════════════════════════
        //  High contrast / accessibility display hooks
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns <c>true</c> when the system is in a High Contrast (forced colors) theme.
        /// Automatically re-renders the component when high contrast is toggled.
        /// <para>
        /// Use this to conditionally override custom styling (hardcoded backgrounds,
        /// foregrounds, border colors) that would ignore forced-colors mode.
        /// WinUI controls using ThemeResource brushes adapt automatically — this hook
        /// is for Reactor components that use explicit color values.
        /// </para>
        /// </summary>
        public bool UseHighContrast() => ctx.UseHighContrastState().IsHighContrast;

        /// <summary>
        /// Returns the high contrast scheme name (e.g., "High Contrast Black",
        /// "High Contrast White") or <c>null</c> if not in high contrast mode.
        /// Automatically re-renders the component when the scheme changes.
        /// <para>
        /// Must be called instead of (not in addition to) <see cref="UseHighContrast"/>
        /// because each consumes the same hook slots. Use one or the other.
        /// </para>
        /// </summary>
        public string? UseHighContrastScheme() => ctx.UseHighContrastState().HighContrastScheme;

        private HighContrastState UseHighContrastState()
        {
            var (state, _) = ctx.UseState(new HighContrastState());

            // Seeded during render for the same reason as UseReducedMotionState above: a value
            // first read inside UseEffect is not available to the frame the user sees, so the
            // first paint reported IsHighContrast=false / scheme=null to every caller —
            // including callers running under high contrast. Guarded so the WinRT settings
            // objects are constructed once per component rather than once per render.
            // Guarded for the same reason as the reduced-motion seed, and broad for the same
            // reason: the try holds only interop and field assignment.
            if (!state.Seeded)
            {
                try
                {
                    state.A11ySettings ??= new global::Windows.UI.ViewManagement.AccessibilitySettings();
                    state.IsHighContrast = state.A11ySettings.HighContrast;
                    state.HighContrastScheme = state.A11ySettings.HighContrast ? state.A11ySettings.HighContrastScheme : null;
                }
                catch (global::System.Exception) { state.A11ySettings = null; }
                state.Seeded = true;
            }

            // AccessibilitySettings.HighContrastChanged throws ERROR_NOT_FOUND
            // (0x80070490) in WinUI 3 desktop apps because it requires a CoreWindow.
            // Instead, use UISettings.ColorValuesChanged which fires reliably for
            // system theme changes including high contrast toggles.
            ctx.UseEffect(() =>
            {
                try
                {
                    state.A11ySettings ??= new global::Windows.UI.ViewManagement.AccessibilitySettings();
                    state.UiSettings ??= new global::Windows.UI.ViewManagement.UISettings();
                }
                catch (global::System.Exception) { state.UiSettings = null; }

                // No projection for the settings objects: report the default and skip the
                // subscription rather than throwing out of the effect flush, which is wrapped
                // in try/finally and would propagate.
                if (state.A11ySettings is not { } a11ySettings || state.UiSettings is not { } uiSettings)
                    return () => { };

                var rerender = ctx._requestRerender;

                void OnColorValuesChanged(global::Windows.UI.ViewManagement.UISettings sender, object args)
                {
                    state.IsHighContrast = a11ySettings.HighContrast;
                    state.HighContrastScheme = a11ySettings.HighContrast ? a11ySettings.HighContrastScheme : null;
                    rerender?.Invoke();
                }

                uiSettings.ColorValuesChanged += OnColorValuesChanged;

                // Re-read after subscribing: the window between the seed above and this
                // subscription has no listener, so a toggle inside it would otherwise persist
                // until an unrelated theme notification arrived.
                var hc = a11ySettings.HighContrast;
                var scheme = hc ? a11ySettings.HighContrastScheme : null;
                if (hc != state.IsHighContrast || scheme != state.HighContrastScheme)
                {
                    state.IsHighContrast = hc;
                    state.HighContrastScheme = scheme;
                    rerender?.Invoke();
                }
                return () => uiSettings.ColorValuesChanged -= OnColorValuesChanged;
            });

            return state;
        }

        // ════════════════════════════════════════════════════════════════
        //  Reduced-motion hook
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns <c>true</c> when the user or system prefers reduced motion
        /// (e.g., Windows "Show animations" is off, or <c>SPI_GETCLIENTAREAANIMATION</c>
        /// returns false). Automatically re-renders the component when the preference changes.
        /// <para>
        /// Use this to skip entrance/exit animations, disable pan inertia, terminate
        /// force-graph simulations immediately, and keep only ≤ 150 ms opacity fades
        /// (WCAG 2.3.3).
        /// </para>
        /// <para>
        /// The value is seeded during the first render and then tracked live through
        /// <c>UISettings.AnimationsEnabledChanged</c>. That event needs Windows 10 2004 (19041);
        /// on older builds the preference is re-read whenever a theme or palette change arrives
        /// instead, so it updates on the next such notification rather than immediately.
        /// </para>
        /// </summary>
        public bool UseReducedMotion() => ctx.UseReducedMotionState().IsReducedMotion;

        private ReducedMotionState UseReducedMotionState()
        {
            var (state, _) = ctx.UseState(new ReducedMotionState());

            // Seed during render, not in the effect below. UseEffect does not run until after
            // the first render commits, so a value read there is invisible to the frame the
            // user actually sees: every consumer rendered its first frame with
            // IsReducedMotion=false, and a component that never re-renders keeps that value
            // forever. For an accessibility hook that is the worst possible direction — the
            // first frame animates for precisely the users who asked it not to. Guarded so
            // UISettings is constructed once per component, not once per render.
            //
            // UseExternalStore also closes the render-to-subscribe race, but it is a public hook
            // for app-authored stores with zero internal callers, it requires a stable subscribe
            // delegate (so it would need extra memoization here), and it re-reads the snapshot on
            // every render. Seed-then-subscribe is what every sibling environment hook in this
            // file does — UseWindowPosition seeds via UseState(win.Position) — so this stays
            // consistent with them.
            // Constructing the WinRT settings object is fallible — ReactorHost guards the same
            // constructor because a host may have no projection for it. Seeding during render
            // widens where that runs: the effect below only executes under a live reconciler.
            // Degrade to the default rather than failing the frame, and set Seeded either way so
            // a host that cannot provide it does not throw once per render.
            //
            // The catches are deliberately broad. What bounds them is the scope of the try —
            // only interop and field assignment, no application logic whose bug could be masked.
            // A narrower list would have to enumerate every way a projection can be absent, which
            // is not something this repo can test: measured in the headless host, both
            // constructors and every property read off them succeed.
            if (!state.Seeded)
            {
                try
                {
                    state.Settings ??= new global::Windows.UI.ViewManagement.UISettings();
                    state.IsReducedMotion = !state.Settings.AnimationsEnabled;
                }
                catch (global::System.Exception) { state.Settings = null; }
                state.Seeded = true;
            }

            ctx.UseEffect(() =>
            {
                try { state.Settings ??= new global::Windows.UI.ViewManagement.UISettings(); }
                catch (global::System.Exception) { state.Settings = null; }

                // No projection for the settings object: report the default and skip the
                // subscription rather than throwing out of the effect flush, which is wrapped
                // in try/finally and would propagate.
                if (state.Settings is not { } settings) return () => { };

                var rerender = ctx._requestRerender;
                void OnChanged(global::Windows.UI.ViewManagement.UISettings sender, object args)
                {
                    var value = !sender.AnimationsEnabled;
                    if (value == state.IsReducedMotion) return;
                    state.IsReducedMotion = value;
                    rerender?.Invoke();
                }

                // AnimationsEnabledChanged is the only event raised for this preference.
                // ColorValuesChanged is NOT — measured with a live subscription to both while
                // toggling Settings > Accessibility > Visual effects > Animation effects, it
                // fired zero times in either direction. It is kept as the pre-19041 fallback
                // because OnChanged re-reads the current value, so a theme or palette change
                // picks up a missed animation flip as a side effect.
                settings.ColorValuesChanged += OnChanged;
                if (UiSettingsCapabilities.HasAnimationsEnabledChanged)
                    settings.AnimationsEnabledChanged += OnChanged;

                // Re-read after subscribing: the preference can flip between the seed above and
                // this subscription, and that window has no listener, so the change would
                // otherwise be missed until some unrelated notification happened to arrive.
                var current = !settings.AnimationsEnabled;
                if (current != state.IsReducedMotion)
                {
                    state.IsReducedMotion = current;
                    rerender?.Invoke();
                }
                return () =>
                {
                    settings.ColorValuesChanged -= OnChanged;
                    // Called rather than captured: CA1416's flow analysis does not carry a
                    // guard's outcome across a closure boundary.
                    if (UiSettingsCapabilities.HasAnimationsEnabledChanged)
                        settings.AnimationsEnabledChanged -= OnChanged;
                };
            });

            return state;
        }

        // ════════════════════════════════════════════════════════════════
        //  Localization hooks
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns an IntlAccessor for the current locale. Re-renders this component
        /// when the locale changes via a parent LocaleProvider.
        /// If no LocaleProvider is present, returns a default accessor using the OS locale.
        /// Uses Context internally — the context system handles re-renders automatically.
        /// </summary>
        public Localization.IntlAccessor UseIntl()
        {
            var contextAccessor = ctx.UseContext(Localization.IntlContexts.Locale);
            return contextAccessor ?? _defaultAccessor.Value;
        }

        // ════════════════════════════════════════════════════════════════
        //  Command hooks
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Processes a Command for use in a component. Always consumes a <b>stable hook shape</b>
        /// (independent of whether the command is sync/async or debounced), so a command at a given
        /// call site can flip between sync↔async or <see cref="Command.DebounceMs"/> 0↔N across
        /// renders without ever reordering hook slots. For a pure sync command with no
        /// <see cref="Command.DebounceMs"/> the original command is returned unchanged (identity
        /// preserved). For async commands, wraps ExecuteAsync with automatic IsExecuting tracking and
        /// re-entrance guards. When <see cref="Command.DebounceMs"/> &gt; 0, wraps the dispatch with a
        /// leading-edge debounce: a fire within the window of the prior accepted fire is dropped and
        /// <see cref="Command.IsDebouncing"/> (hence <see cref="Command.IsEnabled"/>=false) reflects
        /// the window so the bound control disables, re-enabling when the window elapses. The returned
        /// command has a sync Execute action, ExecuteAsync = null, and preserves the authored
        /// DebounceMs value (re-passing it through UseCommand is a no-op — debounce is never applied
        /// twice).
        /// </summary>
        public Command UseCommand(Command command)
        {
            var (guardRef, debounceRef, isExecuting, setIsExecuting, isDebouncing, setIsDebouncing)
                = ctx.UseCommandState();

            var asyncAction = command.ExecuteAsync;
            var syncAction = command.Execute;
            int debounceMs = command.DebounceMs;

            var wrappedExecute = ctx.UseMemo<Action>(() => () =>
                ctx.DispatchCommand(asyncAction, syncAction, debounceMs, guardRef, debounceRef, setIsExecuting, setIsDebouncing),
                (object?)command.ExecuteAsync ?? NullDep, (object?)command.Execute ?? NullDep, debounceMs);

            // Branch on VALUES, never on hook calls: a pure sync, non-debounced, not-yet-wrapped
            // command is returned unchanged (preserves identity / Assert.Same and today's behavior).
            if (!CommandNeedsWrapping(command.ExecuteAsync, command.DebounceMs, command.DebounceHandled))
                return command;

            return command with
            {
                Execute = wrappedExecute,
                ExecuteAsync = null,
                IsExecuting = isExecuting,
                IsDebouncing = isDebouncing,
                DebounceHandled = true,
            };
        }

        /// <summary>
        /// Processes a parameterized Command for use in a component. Consumes the same
        /// <b>stable hook shape</b> as the non-generic <see cref="WinUIHooks.extension(RenderContext).UseCommand(Command)"/> and applies
        /// the same async tracking and leading-edge <see cref="Command{T}.DebounceMs"/> debounce.
        /// </summary>
        public Command<T> UseCommand<T>(Command<T> command)
        {
            var (guardRef, debounceRef, isExecuting, setIsExecuting, isDebouncing, setIsDebouncing)
                = ctx.UseCommandState();

            var asyncAction = command.ExecuteAsync;
            var syncAction = command.Execute;
            int debounceMs = command.DebounceMs;

            var wrappedExecute = ctx.UseMemo<Action<T>>(() => (arg) =>
                ctx.DispatchCommand(
                    asyncAction is null ? null : () => asyncAction(arg),
                    syncAction is null ? null : () => syncAction(arg),
                    debounceMs, guardRef, debounceRef, setIsExecuting, setIsDebouncing),
                (object?)command.ExecuteAsync ?? NullDep, (object?)command.Execute ?? NullDep, debounceMs);

            if (!CommandNeedsWrapping(command.ExecuteAsync, command.DebounceMs, command.DebounceHandled))
                return command;

            return command with
            {
                Execute = wrappedExecute,
                ExecuteAsync = null,
                IsExecuting = isExecuting,
                IsDebouncing = isDebouncing,
                DebounceHandled = true,
            };
        }

        /// <summary>A command's execution needs wrapping when it is async, or it declares a debounce
        /// window that <see cref="WinUIHooks.extension(RenderContext).UseCommand(Command)"/> hasn't already consumed. Pure sync,
        /// non-debounced commands (and already-wrapped commands) pass through untouched.</summary>
        private static bool CommandNeedsWrapping(object? executeAsync, int debounceMs, bool debounceHandled)
            => !debounceHandled && (executeAsync is not null || debounceMs > 0);

        /// <summary>
        /// Allocates the stable hook shape shared by both <see cref="WinUIHooks.extension(RenderContext).UseCommand(Command)"/> overloads:
        /// the IsExecuting / IsDebouncing state, the re-entrance guard and debounce-slot refs, and an
        /// unmount effect that disposes any live re-enable timer so it cannot fire
        /// <c>setIsDebouncing</c> / request a re-render against a torn-down context. These hooks are
        /// allocated unconditionally so the hook order is identical regardless of the command's shape.
        /// </summary>
        private (Ref<AsyncReentryGuard?> GuardRef, Ref<DebounceSlot?> DebounceRef,
                 bool IsExecuting, Action<bool> SetIsExecuting,
                 bool IsDebouncing, Action<bool> SetIsDebouncing) UseCommandState()
        {
            var (isExecuting, setIsExecuting) = ctx.UseState(false, threadSafe: true);
            var (isDebouncing, setIsDebouncing) = ctx.UseState(false, threadSafe: true);
            var guardRef = ctx.UseRef<AsyncReentryGuard?>(null);
            var debounceRef = ctx.UseRef<DebounceSlot?>(null);

            // Mount-once effect: on unmount, dispose the live re-enable timer (if any) so a pending
            // window can't fire its callback against a context that no longer exists.
            ctx.UseEffect(() => () =>
            {
                var slot = debounceRef.Current;
                if (slot is null) return;
                global::System.Threading.ITimer? timer;
                lock (slot.Gate)
                {
                    timer = slot.Timer;
                    slot.Timer = null;
                    slot.InWindow = false;
                }
                timer?.Dispose();
            });

            return (guardRef, debounceRef, isExecuting, setIsExecuting, isDebouncing, setIsDebouncing);
        }

        /// <summary>
        /// Shared dispatch core for both <see cref="WinUIHooks.extension(RenderContext).UseCommand(Command)"/> overloads. Applies the
        /// re-entrance guard (async only, checked BEFORE arming the window so a fire blocked because
        /// the async action is still running doesn't arm the window for nothing) and the leading-edge
        /// debounce, then runs the action. Returns true if the fire was accepted.
        /// </summary>
        private bool DispatchCommand(
            Func<Task>? asyncAction,
            Action? syncAction,
            int debounceMs,
            Ref<AsyncReentryGuard?> guardRef,
            Ref<DebounceSlot?> debounceRef,
            Action<bool> setIsExecuting,
            Action<bool> setIsDebouncing)
        {
            if (asyncAction is not null)
            {
                var guard = guardRef.Current ??= new AsyncReentryGuard();
                // Atomic test-and-set re-entrance guard, checked BEFORE arming the window so a fire
                // blocked because the async action is still running doesn't arm the window for nothing.
                // The guard is acquired here (the dispatcher/UI thread) and released in the Task.Run
                // finally (a threadpool thread); Interlocked.CompareExchange + Volatile.Write give the
                // cross-thread release proper visibility, so the next invoke always observes a completed
                // action's release (a plain field could in principle read a stale "busy").
                if (global::System.Threading.Interlocked.CompareExchange(ref guard.Busy, 1, 0) != 0) return false;
                if (!ctx.TryEnterDebounceWindow(debounceMs, debounceRef, setIsDebouncing))
                {
                    global::System.Threading.Volatile.Write(ref guard.Busy, 0);
                    return false;
                }
                setIsExecuting(true);
                // try/finally — NOT try/catch. The user's command throw becomes a faulted Task; the
                // framework's reentry-guard and IsExecuting state still get restored. We deliberately
                // let the exception surface via Task.UnobservedTaskException rather than swallowing,
                // so a buggy command is visible to the developer (matches the dispose / lifecycle
                // policy elsewhere — don't hide user bugs).
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await asyncAction();
                    }
                    finally
                    {
                        global::System.Threading.Volatile.Write(ref guard.Busy, 0);
                        setIsExecuting(false);
                    }
                });
                return true;
            }

            // Sync command + debounce: dispatch stays synchronous (no Task.Run hop), so the action
            // keeps running on the UI thread.
            if (!ctx.TryEnterDebounceWindow(debounceMs, debounceRef, setIsDebouncing)) return false;
            syncAction?.Invoke();
            return true;
        }

        /// <summary>
        /// Leading-edge gate. Returns true if this fire is accepted; arms the window for
        /// <paramref name="debounceMs"/> and schedules a re-enable that clears
        /// <see cref="DebounceSlot.InWindow"/> and re-renders so the control comes back. Returns
        /// false only while a prior accepted fire is genuinely still inside its window.
        /// <para>Acceptance is <b>time-based</b>: it is decided against the absolute
        /// <see cref="DebounceSlot.WindowEndsAt"/> deadline (via the injected <see cref="TimeProvider"/>),
        /// not purely on the <see cref="DebounceSlot.InWindow"/> flag. The one-shot timer only exists to
        /// trigger the re-render/re-enable; if it is delayed (threadpool starvation/suspension) a fire
        /// arriving after the deadline is still accepted and re-arms, honoring the fixed-duration
        /// semantics rather than dropping until the callback eventually runs.</para>
        /// </summary>
        private bool TryEnterDebounceWindow(int debounceMs, Ref<DebounceSlot?> slotRef, Action<bool> setIsDebouncing)
        {
            if (debounceMs <= 0) return true;

            var slot = slotRef.Current ??= new DebounceSlot();
            var now = ctx.TimeProvider.GetUtcNow();
            global::System.Threading.ITimer? prior;
            long epoch;
            lock (slot.Gate)
            {
                // Drop only while we are genuinely still inside the window. If the re-enable timer
                // callback is delayed past the deadline, InWindow may still be set — but the window has
                // logically expired, so fall through and accept + re-arm rather than wrongly drop.
                if (slot.InWindow && now < slot.WindowEndsAt) return false;
                slot.InWindow = true;
                slot.WindowEndsAt = now + TimeSpan.FromMilliseconds(debounceMs);
                epoch = ++slot.Epoch;
                prior = slot.Timer;
                slot.Timer = null;
                // setIsDebouncing(true) is called under the gate so it is atomic with arming the
                // window: a stale timer's setIsDebouncing(false) (below) can never interleave between
                // here and the InWindow check, so the control can't be left wrongly enabled.
                setIsDebouncing(true);
            }
            // Dispose the superseded timer (the re-enable callback normally disposes its own when it
            // clears the window, so on a clean re-arm this is already null; covers the expiry/race path).
            prior?.Dispose();

            var timer = ctx.TimeProvider.CreateTimer(
                _ =>
                {
                    lock (slot.Gate)
                    {
                        // Only the timer that still owns the current epoch may clear the window.
                        // A stale timer whose window was superseded by a newer accepted fire is a
                        // no-op. setIsDebouncing(false) runs under the gate so it is atomic with the
                        // clear and cannot race a concurrent accepted fire's setIsDebouncing(true).
                        if (slot.Epoch != epoch || !slot.InWindow) return;
                        slot.InWindow = false;
                        setIsDebouncing(false);
                        // Dispose this now-inert one-shot timer immediately (atomic with the clear, so
                        // slot.Timer is still us) rather than retaining it until the next accepted fire
                        // or unmount — otherwise a fire-once debounced command on a long-lived component
                        // would keep a dead timer object alive indefinitely.
                        slot.Timer?.Dispose();
                        slot.Timer = null;
                    }
                },
                null,
                TimeSpan.FromMilliseconds(debounceMs),
                global::System.Threading.Timeout.InfiniteTimeSpan);

            lock (slot.Gate)
            {
                // Install only if we still own the window; otherwise this timer is already stale.
                if (slot.Epoch == epoch)
                    slot.Timer = timer;
                else
                    timer.Dispose();
            }
            return true;
        }

        // ════════════════════════════════════════════════════════════════
        //  Responsive layout hooks
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns (width, height) of the given window and re-renders when the window resizes.
        /// </summary>
        public (double Width, double Height) UseWindowSize(Microsoft.UI.Xaml.Window window)
        {
            var (size, setSize) = ctx.UseState((window.Bounds.Width, window.Bounds.Height));

            ctx.UseEffect(() =>
            {
                void handler(object sender, Microsoft.UI.Xaml.WindowSizeChangedEventArgs args)
                {
                    setSize((args.Size.Width, args.Size.Height));
                }
                window.SizeChanged += handler;
                return () => window.SizeChanged -= handler;
            }, window);

            return size;
        }

        /// <summary>
        /// Parameterless overload — resolves the window whose host renders this
        /// component and re-renders on resize. Returns
        /// <c>(0, 0)</c> when the component has no owning window (tray-flyout content,
        /// <c>ReactorHostControl</c> embeds); no implicit fallback to <c>PrimaryWindow</c>
        /// or to another open window. (spec 036 §5.2 / §7.1)
        /// </summary>
        public (double Width, double Height) UseWindowSize()
        {
            var hostWindow = ctx.OwningReactorWindow;
            if (hostWindow is null)
            {
                // Reserve the same hook slots as the live branch (UseState +
                // UseEffect) and use the same tuple-shaped state so a render that
                // crosses the no-window → window transition doesn't trip the
                // hook-state type check.
                _ = ctx.UseState((0.0, 0.0));
                ctx.UseEffect(() => { /* no-op */ }, ctx);
                return (0, 0);
            }
            return ctx.UseWindowSize(hostWindow.NativeWindow);
        }

        /// <summary>
        /// Resolves the <see cref="Microsoft.UI.Reactor.ReactorWindow"/> whose host renders this
        /// component — including nested components and virtualized rows realized outside the
        /// host's render pass — or <c>null</c> when it has no owning window (tray-flyout content,
        /// <c>ReactorHostControl</c> embeds). O(1) field read; no subscription, no re-render
        /// trigger. (spec 036 §7 / §7.1)
        /// </summary>
        public Microsoft.UI.Reactor.ReactorWindow? UseWindow()
            => ctx.OwningReactorWindow;

        /// <summary>
        /// Subscribes to the host window's <c>PositionChanged</c> event and re-renders
        /// on change. Returns <c>(0, 0)</c> outside a window. (spec 054 §5.5)
        /// </summary>
        public (double X, double Y) UseWindowPosition()
        {
            var win = ctx.OwningReactorWindow;
            if (win is null)
            {
                _ = ctx.UseState((0.0, 0.0));
                ctx.UseEffect(() => { /* no-op */ }, ctx);
                return (0, 0);
            }

            var (position, setPosition) = ctx.UseState(win.Position);
            ctx.UseEffect(() =>
            {
                void handler(object? sender, Microsoft.UI.Reactor.WindowDipPositionChangedEventArgs args)
                    => setPosition(args.Position);
                win.PositionChanged += handler;
                return () => win.PositionChanged -= handler;
            }, win);
            return position;
        }

        /// <summary>
        /// Returns a covered hint from the host window's z-order transitions and
        /// re-renders on change. The value is not pixel-accurate occlusion state.
        /// </summary>
        public bool UseIsCovered()
        {
            var win = ctx.OwningReactorWindow;
            if (win is null)
            {
                _ = ctx.UseState(false);
                ctx.UseEffect(() => { /* no-op */ }, ctx);
                return false;
            }

            var (isCovered, setIsCovered) = ctx.UseState(false);
            ctx.UseEffect(() =>
            {
                void handler(object? sender, Microsoft.UI.Reactor.WindowZOrderChangedEventArgs args)
                {
                    if (args.MovedToTop) setIsCovered(false);
                    else if (args.IsCovered) setIsCovered(true);
                }
                win.ZOrderChanged += handler;
                return () => win.ZOrderChanged -= handler;
            }, win);
            return isCovered;
        }

        /// <summary>
        /// Returns the current display snapshot and re-renders when the display layout changes.
        /// </summary>
        public IReadOnlyList<Microsoft.UI.Reactor.DisplayInfo> UseDisplays()
        {
            var (displays, setDisplays) = ctx.UseState(Microsoft.UI.Reactor.ReactorDisplay.Displays);
            ctx.UseEffect(() =>
            {
                void handler(object? sender, EventArgs args)
                    => setDisplays(Microsoft.UI.Reactor.ReactorDisplay.Displays);
                Microsoft.UI.Reactor.ReactorDisplay.DisplayLayoutChanged += handler;
                return () => Microsoft.UI.Reactor.ReactorDisplay.DisplayLayoutChanged -= handler;
            }, ctx);
            return displays;
        }

        /// <summary>
        /// Applies a lifetime-bound width/height aspect lock to the owning window.
        /// Last mounted hook wins; unmounting restores the previous writer.
        /// </summary>
        public void UseWindowAspectRatio(double? widthOverHeight)
        {
            var win = ctx.OwningReactorWindow;
            ctx.UseEffect(() =>
            {
                if (win is null) return () => { };
                var token = win.RegisterAspectRatioOverride(widthOverHeight);
                return () => token.Dispose();
            }, win!, widthOverHeight ?? double.NaN);
        }

        /// <summary>
        /// Returns a stable callback that starts a framework-managed window
        /// drag/move loop (cursor polling + <c>AppWindow.Move</c> at ~60Hz
        /// until the left mouse button is released).
        /// </summary>
        /// <remarks>
        /// The loop is framework-managed (not OS-managed) because WinUI 3
        /// routes pointer input through a child input-site HWND, so
        /// synthesizing <c>WM_NCLBUTTONDOWN</c> against the top-level HWND
        /// silently falls back to keyboard/cursor-track Move mode rather
        /// than mouse-driven click-drag. The polling approach is reliable
        /// but trades away OS Aero Snap during the drag. See spec 054 §5.3.
        /// </remarks>
        public Action UseWindowDragMove()
        {
            var win = ctx.OwningReactorWindow;
            return ctx.UseMemo<Action>(() => win is null ? static () => { } : win.BeginDragMove, win!);
        }

        internal static IPickerService PickerService { get => s_pickerService; set => s_pickerService = value; }

        /// <summary>Opens a single-file picker owned by this component's window.</summary>
        /// <remarks>
        /// Not a hook: call from a UI-thread event handler, not during Render. The owner is the window
        /// whose host renders this component (for ReactorHostControl, the window it is loaded in). Throws <see cref="InvalidOperationException"/> once the component unmounts, its window closes or its host is disposed, or while its ReactorHostControl is not loaded. Cancellation returns null.
        /// </remarks>
        public Task<Microsoft.Windows.Storage.Pickers.PickFileResult?> PickFileAsync(FilePickerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            PickerOptionsValidation.Validate(options);
            var owner = ctx.GetPickerOwner("PickFileAsync");
            return s_pickerService.PickFileAsync(owner, options);
        }

        /// <summary>Opens a multiple-file picker owned by this component's window.</summary>
        /// <remarks>
        /// Not a hook: call from a UI-thread event handler, not during Render. The owner is the window
        /// whose host renders this component (for ReactorHostControl, the window it is loaded in). Throws <see cref="InvalidOperationException"/> once the component unmounts, its window closes or its host is disposed, or while its ReactorHostControl is not loaded. Cancellation returns an empty list.
        /// </remarks>
        public Task<IReadOnlyList<Microsoft.Windows.Storage.Pickers.PickFileResult>> PickMultipleFilesAsync(FilePickerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            PickerOptionsValidation.Validate(options);
            var owner = ctx.GetPickerOwner("PickMultipleFilesAsync");
            return s_pickerService.PickMultipleFilesAsync(owner, options);
        }

        /// <summary>Opens a single-folder picker owned by this component's window.</summary>
        /// <remarks>
        /// Not a hook: call from a UI-thread event handler, not during Render. The owner is the window
        /// whose host renders this component (for ReactorHostControl, the window it is loaded in). Throws <see cref="InvalidOperationException"/> once the component unmounts, its window closes or its host is disposed, or while its ReactorHostControl is not loaded. Cancellation returns null.
        /// </remarks>
        public Task<Microsoft.Windows.Storage.Pickers.PickFolderResult?> PickFolderAsync(FolderPickerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var owner = ctx.GetPickerOwner("PickFolderAsync");
            return s_pickerService.PickFolderAsync(owner, options);
        }

        /// <summary>Opens a multiple-folder picker owned by this component's window.</summary>
        /// <remarks>
        /// Not a hook: call from a UI-thread event handler, not during Render. The owner is the window
        /// whose host renders this component (for ReactorHostControl, the window it is loaded in). Throws <see cref="InvalidOperationException"/> once the component unmounts, its window closes or its host is disposed, or while its ReactorHostControl is not loaded. Cancellation returns an empty list.
        /// </remarks>
        public Task<IReadOnlyList<Microsoft.Windows.Storage.Pickers.PickFolderResult>> PickMultipleFoldersAsync(FolderPickerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var owner = ctx.GetPickerOwner("PickMultipleFoldersAsync");
            return s_pickerService.PickMultipleFoldersAsync(owner, options);
        }

        /// <summary>Opens a save-file picker owned by this component's window.</summary>
        /// <remarks>
        /// Not a hook: call from a UI-thread event handler, not during Render. The owner is the window
        /// whose host renders this component (for ReactorHostControl, the window it is loaded in). Throws <see cref="InvalidOperationException"/> once the component unmounts, its window closes or its host is disposed, or while its ReactorHostControl is not loaded. Cancellation returns null. Selecting a target does not
        /// create or write the file; that is the caller's responsibility.
        /// </remarks>
        public Task<Microsoft.Windows.Storage.Pickers.PickFileResult?> PickSaveFileAsync(SaveFilePickerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            PickerOptionsValidation.Validate(options);
            var owner = ctx.GetPickerOwner("PickSaveFileAsync");
            return s_pickerService.PickSaveFileAsync(owner, options);
        }

        private Microsoft.UI.WindowId GetPickerOwner(string operation)
        {
            var owner = ctx.Owner as IContentOwner ?? throw new InvalidOperationException(ctx.OwnerReleased
                ? $"{operation} was called from a component that is no longer mounted."
                : $"{operation} requires a component rendered by a Reactor host (a ReactorWindow, ReactorHostControl or ReactorHost).");
            return owner.TryGetWindowId(out var id) switch
            {
                ContentOwnerStatus.Available => id,
                ContentOwnerStatus.Closed => throw new InvalidOperationException(
                    $"{operation} was called after the component's window closed or its host was disposed."),
                ContentOwnerStatus.Detached => throw new InvalidOperationException(
                    $"{operation} was called while the component's ReactorHostControl is not loaded in a window."),
                _ => throw new InvalidOperationException($"{operation} must be called on the owning window's UI thread."),
            };
        }

        /// <summary>
        /// Subscribes to the host window's <c>StateChanged</c> event and re-renders
        /// on change. Returns <see cref="Microsoft.UI.Reactor.WindowState.Normal"/>
        /// outside a window. (spec 036 §7)
        /// </summary>
        public Microsoft.UI.Reactor.WindowState UseWindowState()
        {
            var win = ctx.OwningReactorWindow;
            if (win is null)
            {
                // Same slots as the live branch (UseState + UseEffect) so a context can cross the
                // no-window ↔ window transition without a hook-order mismatch.
                _ = ctx.UseState(Microsoft.UI.Reactor.WindowState.Normal);
                ctx.UseEffect(() => { /* no-op */ }, ctx);
                return Microsoft.UI.Reactor.WindowState.Normal;
            }
            var (state, setState) = ctx.UseState(win.State);
            ctx.UseEffect(() =>
            {
                void handler(object? sender, Microsoft.UI.Reactor.WindowState newState) => setState(newState);
                win.StateChanged += handler;
                return () => win.StateChanged -= handler;
            }, win);
            return state;
        }

        /// <summary>
        /// Subscribes to the host window's activation events and re-renders on
        /// change. Returns <c>true</c> outside a window (the surface is "active"
        /// while shown). (spec 036 §7)
        /// </summary>
        public bool UseIsActive()
        {
            var win = ctx.OwningReactorWindow;
            if (win is null)
            {
                _ = ctx.UseState(true);
                ctx.UseEffect(() => { /* no-op */ }, ctx);
                return true;
            }
            var (active, setActive) = ctx.UseState(win.IsActive);
            ctx.UseEffect(() =>
            {
                void onAct(object? s, EventArgs e) => setActive(true);
                void onDeact(object? s, EventArgs e) => setActive(false);
                win.Activated += onAct;
                win.Deactivated += onDeact;
                return () =>
                {
                    win.Activated -= onAct;
                    win.Deactivated -= onDeact;
                };
            }, win);
            return active;
        }

        /// <summary>
        /// Registers a synchronous "can the window close right now?" predicate.
        /// Multiple guards stack — any returning <c>false</c> cancels the close.
        /// Runs on the UI thread; for async confirmation, return <c>false</c> and
        /// re-trigger <see cref="Microsoft.UI.Reactor.ReactorWindow.Close"/> from
        /// the dialog callback. No-op outside a window. (spec 036 §7 / §13.4)
        /// </summary>
        public void UseClosingGuard(Func<bool> canClose)
        {
            ArgumentNullException.ThrowIfNull(canClose);
            var win = ctx.OwningReactorWindow;
            if (win is null) { ctx.UseEffect(() => { /* no-op */ }, canClose); return; }
            ctx.UseEffect(() =>
            {
                var token = win.RegisterClosingGuard(canClose);
                return () => token.Dispose();
            }, win, canClose);
        }

        /// <summary>
        /// Returns the current per-window DPI (<see cref="Microsoft.UI.Reactor.ReactorWindow.Dpi"/>)
        /// and re-renders on DPI change. Returns the system primary-monitor DPI when
        /// called outside a window. (spec 036 §5.2)
        /// </summary>
        public uint UseDpi()
        {
            var win = ctx.OwningReactorWindow;
            if (win is null)
            {
                _ = ctx.UseState((uint)0);
                ctx.UseEffect(() => { /* no-op */ }, ctx);
                return DpiHelpers.GetSystemDpiSafe();
            }

            var (dpi, setDpi) = ctx.UseState(win.Dpi);

            ctx.UseEffect(() =>
            {
                void handler(object? sender, uint newDpi) => setDpi(newDpi);
                win.DpiChanged += handler;
                return () => win.DpiChanged -= handler;
            }, win);

            return dpi == 0 ? win.Dpi : dpi;
        }

        /// <summary>
        /// Open or reuse a secondary window keyed by <paramref name="key"/>. Renders
        /// that pass the same <paramref name="key"/> share the same
        /// <see cref="Microsoft.UI.Reactor.ReactorWindow"/>; if the spec changes
        /// across renders the live window is updated via
        /// <see cref="Microsoft.UI.Reactor.ReactorWindow.Update"/>. The returned
        /// handle is identity-stable across renders so long as the key is stable.
        /// </summary>
        /// <remarks>
        /// <para>Unmount semantics: when the calling component unmounts, the opened
        /// window stays open. Components that want the inverse behavior must close
        /// the window explicitly — e.g. by registering a <c>UseEffect</c> cleanup
        /// that calls <see cref="Microsoft.UI.Reactor.ReactorWindow.Close"/> on the
        /// returned handle. (spec 036 §4.3 / §15.6)</para>
        /// <para><b>Note — asymmetry with <see cref="UseTrayIcon"/>:</b> tray icons
        /// are component-scoped and close on unmount; opened windows are app-scoped
        /// and survive unmount. The asymmetry is deliberate (a window is normally
        /// expected to outlive the menu item that opened it, while a tray icon
        /// belongs to the component that declared it), so the two hooks behave
        /// inversely despite the matching naming.</para>
        /// <para>Returns <c>null</c> when no UI dispatcher has been captured —
        /// happens in unit-test contexts where no <c>ReactorApp.Run</c> is in
        /// flight. In production this is unreachable.</para>
        /// </remarks>
        public Microsoft.UI.Reactor.ReactorWindow? UseOpenWindow(
            Microsoft.UI.Reactor.WindowKey key,
            Microsoft.UI.Reactor.WindowSpec spec,
            Func<Component> factory)
        {
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(factory);

            // Capture an identity-stable handle slot. Renders that pass the same
            // key reuse this slot; rekeying clears the handle so a fresh window
            // opens for the new key.
            var handleRef = ctx.UseRef<Microsoft.UI.Reactor.ReactorWindow?>(null);
            var lastKeyRef = ctx.UseRef<Microsoft.UI.Reactor.WindowKey?>(null);

            // Resolve the live window for this key. Three cases:
            //   1. Slot already holds a non-disposed window with the matching key — reuse.
            //   2. The key changed since last render — drop the slot (the old window
            //      stays open per spec §15.6) and look up by new key.
            //   3. No window matches — open one, save the handle.
            if (handleRef.Current is { } prior && lastKeyRef.Current is { } priorKey && priorKey.Equals(key))
            {
                // Reuse — but if the underlying window has been closed externally,
                // drop the stale reference so the next branch reopens.
                var snapshot = Microsoft.UI.Reactor.ReactorApp.Windows;
                bool stillOpen = false;
                for (int i = 0; i < snapshot.Count; i++)
                    if (ReferenceEquals(snapshot[i], prior)) { stillOpen = true; break; }
                if (!stillOpen) handleRef.Current = null;
            }
            else
            {
                // Key changed (or first render) — clear the slot. We do NOT close
                // the previous window; the spec calls for explicit close-on-cleanup.
                handleRef.Current = null;
            }

            // Slot is empty — try a process-wide lookup by key first (the user may
            // have already opened a window with this key from elsewhere) and only
            // fall back to OpenWindow when no live window owns it.
            if (handleRef.Current is null)
            {
                var existing = Microsoft.UI.Reactor.ReactorApp.FindWindow(key);
                if (existing is not null)
                {
                    handleRef.Current = existing;
                }
                else if (Microsoft.UI.Reactor.ReactorApp.UIDispatcher is not null)
                {
                    // Stamp the key onto the spec so FindWindow / FindTrayIcon /
                    // shutdown-policy bookkeeping work without an explicit
                    // WindowSpec.Key on the caller.
                    var stamped = spec.Key is null ? spec with { Key = key } : spec;
                    try
                    {
                        handleRef.Current = Microsoft.UI.Reactor.ReactorApp.OpenWindow(stamped, factory);
                    }
                    catch (Exception ex) when ((ex is InvalidOperationException || ex is global::System.Runtime.InteropServices.COMException)
                        && !RenderErrorDispatch.IsPropagating(ex))
                    {
                        // No XAML application or UI dispatcher available. Hooks
                        // must not crash the calling render; the live multi-
                        // window path is exercised in selftest fixtures. A render
                        // error the app asked to propagate (and then declined) is
                        // not that case: it keeps going out (issue #1291).
                        handleRef.Current = null;
                    }
                }
                else
                {
                    // No UI dispatcher captured — unit-test contexts. Hook slot
                    // count must stay stable so the hook-order check passes on
                    // subsequent renders.
                    handleRef.Current = null;
                }
            }
            lastKeyRef.Current = key;

            // If the spec changed since the last render, push it through Update so
            // chrome stays in sync. Effect dependency is the spec record's
            // value-equality so we only call Update on real changes.
            var win = handleRef.Current;
            if (win is not null)
            {
                ctx.UseEffect(() =>
                {
                    try { win.Update(spec.Key is null ? spec with { Key = key } : spec); }
                    catch { /* best effort — disposed windows / threading races */ }
                }, win, spec);
            }
            else
            {
                // Keep the hook slot count stable across the no-window branch.
                ctx.UseEffect(() => { /* no-op */ }, key, spec);
            }

            return win;
        }

        /// <summary>
        /// Open (or reuse-by-<see cref="Microsoft.UI.Reactor.TrayIconSpec.Key"/>) a
        /// system-tray icon scoped to the calling component. The icon closes
        /// automatically on unmount — that's the only difference from
        /// <see cref="Microsoft.UI.Reactor.ReactorApp.OpenTrayIcon"/>, which is
        /// app-scoped and keeps the icon alive until explicit
        /// <see cref="Microsoft.UI.Reactor.ReactorTrayIcon.Close"/>.
        /// </summary>
        /// <remarks>
        /// Returns <c>null</c> when no UI dispatcher has been captured (test
        /// contexts) or when the spec change cannot be reconciled — callers
        /// should null-check before subscribing to events. Identity-stable across
        /// re-renders so the same handle wires through subsequent
        /// <c>UseEffect</c> dependencies.
        /// (spec 036 §11.4)
        /// </remarks>
        public Microsoft.UI.Reactor.ReactorTrayIcon? UseTrayIcon(Microsoft.UI.Reactor.TrayIconSpec spec)
        {
            ArgumentNullException.ThrowIfNull(spec);

            var handleRef = ctx.UseRef<Microsoft.UI.Reactor.ReactorTrayIcon?>(null);

            // Open on first render; reuse on subsequent renders if a key match
            // already exists. The hook owns the lifetime — UseEffect cleanup
            // closes the icon on unmount.
            if (handleRef.Current is null)
            {
                if (spec.Key is { } key)
                {
                    handleRef.Current = Microsoft.UI.Reactor.ReactorApp.FindTrayIcon(key);
                }

                if (handleRef.Current is null && Microsoft.UI.Reactor.ReactorApp.UIDispatcher is not null)
                {
                    try
                    {
                        handleRef.Current = Microsoft.UI.Reactor.ReactorApp.OpenTrayIcon(spec);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException || ex is global::System.Runtime.InteropServices.COMException)
                    {
                        // Shell COM unavailable in test/headless contexts.
                        handleRef.Current = null;
                    }
                }
            }

            var icon = handleRef.Current;

            // Spec-change diff: re-apply only when the value-equal record
            // signature changes. Effect dependency carries the spec record.
            if (icon is not null)
            {
                ctx.UseEffect(() =>
                {
                    try { icon.Update(spec); }
                    catch { /* best effort */ }
                }, icon, spec);
            }
            else
            {
                // Keep slot count stable so the hook-order check passes on
                // subsequent renders even when the open failed.
                ctx.UseEffect(() => { /* no-op */ }, spec);
            }

            // Component-scoped lifetime — close on unmount. UseEffect with
            // empty deps runs once. Read the icon via handleRef inside the
            // cleanup so a late-bound icon (e.g. UIDispatcher captured after the
            // first render) still gets closed.
            ctx.UseEffect(() =>
            {
                return () =>
                {
                    try { handleRef.Current?.Close(); } catch { /* best effort */ }
                };
            });

            return icon;
        }

        /// <summary>
        /// Returns true when the given window's width is >= minWidth.
        /// Re-renders when the window resizes across the breakpoint.
        /// </summary>
        public bool UseBreakpoint(Microsoft.UI.Xaml.Window window, double minWidth)
        {
            var (width, _) = ctx.UseWindowSize(window);
            return width >= minWidth;
        }

        /// <summary>
        /// Window-inferring overload — takes only <paramref name="minWidth"/> and
        /// resolves the window whose host renders this component (see the parameterless
        /// <see cref="WinUIHooks.extension(RenderContext).UseWindowSize()"/>). Returns false when the component has no owning
        /// window. (spec 036 §5.2)
        /// </summary>
        public bool UseBreakpoint(double minWidth)
        {
            var (width, _) = ctx.UseWindowSize();
            return width >= minWidth;
        }

    }

    private static IPickerService s_pickerService = new DefaultPickerService();

    private sealed class HighContrastState
    {
        public global::Windows.UI.ViewManagement.AccessibilitySettings? A11ySettings;
        public global::Windows.UI.ViewManagement.UISettings? UiSettings;
        public bool IsHighContrast;
        public string? HighContrastScheme;
        public bool Seeded;
    }

    private sealed class ReducedMotionState
    {
        public global::Windows.UI.ViewManagement.UISettings? Settings;
        public bool IsReducedMotion;
        public bool Seeded;
    }

    private static readonly Lazy<Localization.IntlAccessor> _defaultAccessor = new(() =>
    {
        var osLocale = global::System.Globalization.CultureInfo.CurrentUICulture.Name;
        if (string.IsNullOrEmpty(osLocale)) osLocale = "en-US";
        var cache = new Localization.MessageCache();
        var provider = new Localization.ReswResourceProvider(osLocale);
        return new Localization.IntlAccessor(osLocale, provider, cache, osLocale);
    });

    /// <summary>
    /// Per-command leading-edge debounce state: the in-window flag, the absolute window deadline,
    /// an epoch token, and the one-shot re-enable timer. Lives in a <see cref="RenderContext.UseRef{T}"/> slot so
    /// it persists across renders (the <see cref="Command"/> record itself is reconstructed every
    /// render).
    /// </summary>
    private sealed class DebounceSlot
    {
        public readonly Lock Gate = new();
        public bool InWindow;
        /// <summary>Absolute time (per the context's <see cref="TimeProvider"/>) at which the
        /// current window expires. Acceptance is decided against this deadline rather than purely on
        /// <see cref="InWindow"/>, so a delayed re-enable timer callback can never drop a fire that
        /// is genuinely past <c>DebounceMs</c>.</summary>
        public DateTimeOffset WindowEndsAt;
        /// <summary>Incremented for every accepted fire. The re-enable timer captures the epoch
        /// it was created for and only clears the window if it still owns the current epoch — so a
        /// stale timer (a newer accepted fire already re-armed the window) is a no-op.</summary>
        public long Epoch;
        public global::System.Threading.ITimer? Timer;
    }

    /// <summary>Atomic re-entrance guard for an async command's in-flight window. Stored in a
    /// <see cref="RenderContext.UseRef{T}"/> slot; <see cref="Busy"/> is mutated via <c>Interlocked</c>/
    /// <c>Volatile</c> because it is acquired on the dispatcher thread and released on the Task.Run
    /// worker thread.</summary>
    private sealed class AsyncReentryGuard
    {
        public int Busy;
    }

    /// <summary>Stable non-null stand-in for a null delegate in a <see cref="RenderContext.UseMemo{T}"/>
    /// dependency array (keeps the comparison meaningful without tripping nullable warnings).</summary>
    private static readonly object NullDep = new();
}

internal sealed class NavigationLifecycleHookState : RenderContext.HookState
{
    internal override HookKind DiagnosticKind => HookKind.NavigationLifecycle;
    public Action<Navigation.NavigatingToContext>? OnNavigatingTo;
    public Action<Navigation.NavigatedToContext>? OnNavigatedTo;
    public Action<Navigation.NavigatingFromContext>? OnNavigatingFrom;
    public Action<Navigation.NavigatedFromContext>? OnNavigatedFrom;
}
