# Spec 069: MAUI navigation, typed routes in the shared core, and Shell (phase 4b)

Phase 4b makes navigation complete on MAUI. Six of the seven port targets use Shell as their root, and
five show alerts (spec 068 §2). WinUI Reactor already has typed, state-driven navigation:
`UseNavigation<TRoute>`, `NavigationHandle<TRoute>`, `NavigationHost`, and lifecycle hooks whose
`onNavigatingFrom` can cancel a navigation. MAUI gets the same API by moving that model into the
shared core, rather than by growing a second one.

4b lands in three gated PRs:

| PR | Scope |
|---|---|
| 4b-1 | The typed-navigation model moves to `Microsoft.UI.Reactor.Core`; WinUI's public API stays identical; MAUI gets the same handle API and `NavigationHost` |
| 4b-2 | Declarative Shell |
| 4b-3 | Page dialogs, the system back button, modal presentation style, app and window lifecycle hooks |

This spec details 4b-1. §4 and §5 outline 4b-2 and 4b-3; each is detailed in this spec before its
implementation starts.

## 1. What WinUI has (the source of truth)

All of these types are in namespace `Microsoft.UI.Reactor.Navigation`, in `src/Reactor/Core/Navigation`:

- **The model, with no WinUI dependencies:**
  - `NavigationStack<TRoute>` (internal): back stack, current route, forward stack, a lifecycle guard;
  - `NavigationState<TRoute>`: a serializable snapshot;
  - `DeepLinkMap<TRoute>`: maps a URI to a route and back;
  - `NavigationMode`, and the lifecycle contexts (`NavigatingFromContext`, `NavigatingToContext`,
    `NavigatedToContext`, `NavigatedFromContext`);
  - `NavigationContext<TRoute>`: the context that gives descendants the handle.
- **`NavigationHandle<TRoute>`.** It has no direct `Microsoft.UI` reference. It uses
  `UIThreadMarshal`, which is already in the core, and carries two pieces of WinUI view metadata: the
  pending transition override and the connected-animation key.
- **`NavigateOptions`** (`Transition`, `ConnectedAnimationKey`, `PushToBackStack`) and
  **`NavigationEventArgs<TRoute>`**, which exposes the options.
- **`NavigationTransition`**, an abstract record with factories (`Default`, `None`, `Entrance`,
  `Slide`, `Fade`, `DrillIn`, `Connected`, `Spring`) and sealed subtypes. Only `Slide`'s `easing`
  parameter and `SlideTransition.Easing` use a WinUI type (`CompositionEasingFunction`).
- **WinUI-only view work:** `NavigationCache`, `TransitionEngine`, connected animations, and the
  `NavigationHost` handler (a `Grid` whose child is swapped with a transition).
- **Hooks** (`RenderContext.WinUI.cs`, forwarded by `Component`):
  - `UseNavigation<TRoute>(initial)` (root mode) and `UseNavigation<TRoute>()` (from context);
  - `UseNavigationLifecycle(...)`;
  - `UseSystemBackButton(nav, Window)`, which uses WinUI key events.

## 2. 4b-1: a shared navigation model, a handle per renderer

### 2.1 The split

WinUI's public navigation API doesn't change at all: same types, same assembly, same members.
`NavigationTransition.Slide`, `NavigateOptions.Transition`, `NavigationDiagnostics` and
`NavigationEventArgs<TRoute>` stay as they are. The design review measured why moving the handle as
is doesn't work:

- A static extension member in place of the real `Slide` breaks `using static` calls,
  fully-qualified calls without the namespace imported, `nameof`, and XML `cref`s (CS0103, CS0117,
  CS8093, CS1574).
- `NavigateOptions.Transition`, `NavigationEventArgs.Options` and the diagnostics' transition
  events carry WinUI types.

So the **behaviour** moves to the core, and each renderer keeps a thin public **handle**:

| In the core (`src/Reactor.Core/Navigation`, namespace `Microsoft.UI.Reactor.Navigation` unless noted) | Per renderer |
|---|---|
| `NavigationStack<TRoute>` (internal, moved as is) is the model: the back, current and forward routes, the guard, and change notification. The handles delegate to it, as WinUI's already does | `NavigationHandle<TRoute>`: the public facade over the model. WinUI's is unchanged; MAUI's has the same names and members |
| `NavigationState<TRoute>`, `DeepLinkMap<TRoute>` and its helpers (public, moved: same namespace and names) | `NavigateOptions`: WinUI keeps `Transition`, `ConnectedAnimationKey` and `PushToBackStack`; MAUI has `Animated` and `PushToBackStack` |
| `NavigationMode` and the four lifecycle contexts (public, moved) | `NavigationEventArgs<TRoute>` and `INavigationHandle` |
| The lifecycle hook's state and registration (`NavigationLifecycleHooks`, internal, in namespace `Microsoft.UI.Reactor.Core`): the slot the hook stores, and the lookup a host uses to find a component's callbacks | `NavigationContext<TRoute>`, because it carries the renderer's handle type |
| | `UseNavigation<TRoute>(initial)`, `UseNavigation<TRoute>()` and `UseNavigationLifecycle(...)`, as thin public hooks. WinUI's `UseNavigationLifecycle` stays in `WinUIHooks`, so binaries built against it keep resolving |
| | `NavigationDiagnostics`: WinUI's public class is unchanged. The moved model types raise no diagnostics, so no sink is needed; MAUI's handle writes the same ETW events through `ReactorEventSource` |
| | Hosts, transitions, cache and the system back button |

- **No WinUI source change.** Shared components call `UseNavigation`, `Navigate`, `GoBack` and the
  lifecycle hook by the same names on both renderers. Code that sets `NavigateOptions.Transition`
  is WinUI-only, as transitions are.
- **The moved types keep their full names.** They are pure data and logic with no WinUI types in
  their signatures. Each gets a `[TypeForwardedTo]` entry in `src/Reactor/TypeForwards.cs`; the
  existing gate (`NeutralCoreBoundaryTests`) detects a missing one.
- **The `Microsoft.UI.Reactor.Core` IVT list** already includes `Microsoft.UI.Reactor.Maui`, so
  MAUI's handle and host use the internal model and lifecycle hooks.

### 2.2 The model

- **Thread marshaling, one transaction per call.** Navigation from another thread runs on the
  owning root's render thread. MAUI's handle captures `RenderContext.RenderThreadEnqueue` when
  `UseNavigation` creates it and falls back to the process dispatcher; WinUI's handle keeps using
  the process dispatcher, as today. Both go through the core's `UIThreadMarshal`.
  - Each facade mutator keeps today's shape: it checks the thread first, and if it's off the render
    thread, it schedules the whole call and returns. On the render thread it runs everything
    synchronously in WinUI's current order: pending options, the model operation, connected history,
    diagnostics, `Navigated` and `RouteChanged`.
  - The model's operations never marshal on their own, so a facade never mistakes "scheduled" for
    "done".
  - `SetState` keeps its synchronous validation and snapshot.
  - On the render thread there is no allocation and no closure; a focused allocation test covers
    the facade.
  - MAUI tests cover:
  - background-thread navigation;
  - two roots with separate schedulers;
  - a dispatcher that refuses work.
- **The guard** is invoked once per navigation, before the stack changes, with the target route and
  mode. A cancelled navigation changes nothing and is reported as cancelled.
- **State.** `GetState` and `SetState` behave as on WinUI: a snapshot the caller serializes, restored
  without running guards.

### 2.3 Lifecycle: shared hook state, policy per renderer in 4b-1

The core owns the hook state that `UseNavigationLifecycle` stores. The design put the invocation
policy in the core too, with each host driving it through a non-generic binding. **As built in
4b-1, the policy is per renderer**: WinUI's `NavigationHostLifecycle` is unchanged, and MAUI's
`NavigationHostBinding` (non-generic, no runtime generic construction) implements the same order and
supersession rule. Discovery and completion differ enough between the two hosts (a swapped child
and a transition on WinUI; a declared stack, proposals and an asynchronous navigator on MAUI) that
moving WinUI's policy would have meant changing WinUI's host in this PR, which 4b-1 avoids. The
known divergences are in §2.6; converging the policy into the core is listed there as not done.
Each host provides:

- **Discovery.** The lifecycle hooks of the active page's components only. Pages retained in the
  back stack and nested navigation hosts are excluded, as WinUI's reconciler does today
  (`Reconciler.cs` stops at nested hosts). WinUI discovers them through its component nodes and
  native children; MAUI through its logical node tree.
- **Completion.** "Navigated" callbacks run when the platform operation completes, not when the
  page mounts.
  - WinUI completes when the transition does.
  - MAUI completes when the push or pop task finishes. A push or pop without animation still awaits
    its task.
  - A pop the user started has no task: it completes when MAUI's stack no longer lists the page,
    which the adapter already tracks. If that hasn't happened within the platform-pop window (spec
    065 §7), it completes when the window expires. 4b-2 replaces the window with MAUI's
    navigation-completed signal.
- **Supersession**, as WinUI's `NavigationHostLifecycle` does today. Each navigation has a
  generation. When a newer navigation supersedes one in flight:
  - every page that was left still gets its `OnNavigatedFrom`;
  - only the stale destination's `OnNavigatedTo` is dropped.

  Interrupted-navigation tests assert both.

When the policy converges, WinUI's host calls the shared binding where its current code runs, so
there is no new virtual dispatch on the render path.

### 2.4 MAUI `NavigationHost`

```csharp
var nav = UseNavigation<Route>(new Route.Home());
return NavigationHost(nav, route => route switch
{
    Route.Home => Page("Home", ...),
    Route.Detail d => Page(d.Title, ...),
});
```

- **What it shows.** It renders MAUI's `NavigationStack` primitive, a `NavigationPage`, declaring
  `nav.BackStack` plus `nav.CurrentRoute`, each mapped by `routeMap`. The forward stack isn't shown,
  as on WinUI.
- **Keys.** The host keeps a stable string id per stack entry. An entry keeps its id while the route
  at its position is equal (`EqualityComparer<TRoute>.Default`), so a push or pop leaves the pages
  below mounted. A different route at a position gets a fresh id from a counter, so replacing the
  current route remounts its page. Tests cover:
  - unequal routes with the same string or hash;
  - equal routes at two positions;
  - replace;
  - push and pop keeping the pages below.
- **Animation, per operation.** `NavigationStackElement` gains `Animated` (default `true`, so the
  primitive keeps its behaviour). The navigator applies it to the operations that reconcile toward
  that declaration; a newer declaration replaces the flag. MAUI's `NavigateOptions.Animated = false`
  produces such a declaration. A platform gesture already running can't be made non-animated.
- **Back: one guarded back transaction, exactly once.**
  - **iOS back button and swipe, and the Android toolbar back.** MAUI pops the page first. The
    existing adapter reports the declared depth the user went back to (`IsUserPop`), which can be
    more than one level when the declaration was ahead of the platform. The host turns that into
    one guarded navigation to that entry. If it's accepted, the declaration matches what's shown.
    If it's refused, the declaration is unchanged and the existing restore path pushes the page
    back.
  - **Android hardware back.** `ReactorNavigationPage` overrides `OnBackButtonPressed`. With more
    than one page, it runs the guarded back itself and consumes the key, accepted or refused, so
    MAUI never pops the page as well. At the root it doesn't consume the key, so the app or the OS
    handles it. A device fixture checks exactly-once guard invocation, including a back press
    during a push.
  - **Restoring a refused pop.** It is bounded by today's 3-second platform-pop window (spec 065
    §7). 4b-2 replaces that window with MAUI's navigation-completed signal.
- **Lifecycle** follows §2.3's order and supersession rule, so the contexts and order match WinUI
  (as built, in MAUI's own binding; §2.6 lists the divergences).
- **`NavigationStack(pages)`** stays as the lower-level primitive, and `UseRouteStack` as a plain
  list in state for it. `NavigationHost` with `UseNavigation` is the recommended API, the same as on
  WinUI.

### 2.5 Gate for 4b-1

- **WinUI:** every tier is green: unit (including the ~190 navigation tests, unchanged), selftests,
  packaged, E2E and AOT. `/perf` is within noise, and `skills/reactor.api.txt` is byte-identical.
  The compatibility corpus compiles every `Slide` form: qualified, `using static`, `nameof`, `cref`
  and a method group.
- **Core:** the neutral harness drives the model on the fake renderer: operations, the guard,
  lifecycle order with superseded generations, state, deep links, and marshaling with an injected
  scheduler.
- **MAUI, headless and device fixtures on three platforms:**
  - push, pop, replace, reset and `PopTo` through the handle;
  - the back button, the gesture and the Android key reaching the guard exactly once;
  - a refused back, which restores the page;
  - back during a push;
  - lifecycle order and superseded completions;
  - `Animated = false`;
  - restoring state into a mounted host;
  - hot reload keeping the handle and its history.
- **AOT:** a trimmed MAUI build that resolves a deep link and round-trips a snapshot.
- **The WinUI and perf gates apply to 4b-2 and 4b-3 as well.**
- **The `Slide` compatibility corpus isn't needed as built**: WinUI's handle, `NavigateOptions` and
  transitions didn't move (§2.1), so no `Slide` form can break; only model types moved, with type
  forwards.

### 2.6 As built (4b-1, MAUI side)

- **`NavigationHost` is a built-in component**, not a host element: it returns a `ComponentElement`
  that renders `NavigationStack(pages)` under the `NavigationContext<TRoute>` provider. The engine's
  restore path doesn't re-sync children, so only a component can re-render the declaration; it
  subscribes to the handle's `RouteChanged`, so a host under a parent that skipped still updates.
  The route map is `Func<TRoute, Element?>`; a route mapped to `null` keeps its position as an empty
  page, so a declared depth is always an entry index. An optional third argument configures the
  stack (`s => s with { Title = …, Modals = …, Animated = false }`); the host sets its pages and
  `OnPopped`.
- **Animation.** The configured stack's `Animated` is the host's default. `NavigateOptions.Animated`
  is a `bool?`: `null` (the default) uses the host's, and a value overrides it for that `Navigate`
  call only, and ends when that navigation completes. Every other change (`GoBack`, `GoForward`,
  `Replace`, `Reset`, `PopTo`, `SetState`, the user's back, a later modal or page replacement) uses
  the host's default. It applies to pushes, pops and modal presentations.
- **Keys use a prefix rule**: an entry keeps its key while it *and every entry below it* equal the
  declared ones. Keeping a page above a replaced one would make MAUI reorder pages; the cases §2.4
  lists behave as specified.
- **A destination that must be mounted to be asked** (push, forward, replace, a new route from
  `SetState`) is first declared as a *proposal*: the pages shown plus the destination on top,
  without telling the navigator. Its `onNavigatingTo` runs once it has mounted. Accepted, the final
  declaration follows (for a push the proposal itself becomes it); refused, the destination unmounts
  and the pages shown were never touched, so a replaced page keeps its state, as WinUI keeps the old
  page. A destination already mounted (a pop) is asked before the declaration changes. As on WinUI,
  a refusing destination leaves the model at the new route while the host keeps the previous page,
  and the next render of the host asks it again. A user's back (or the back key) to that route while
  the host still shows the page above it is another attempt to arrive there.
- **A user's pop** maps to `NavigationStack.PopToIndex`, a new internal core method that `PopTo`
  now delegates to. It runs only while the model is the one the host shows (or one whose destination
  refused) and still holds the declared pages below that depth; otherwise the pop is refused, the
  page restored, and the host goes on to show the model.
- **The back key** (`ReactorNavigationPage.OnBackButtonPressed`) gives a nested current page the key
  first, as `NavigationPage` does. Under a `NavigationHost`, when the model holds a change the host
  hasn't shown yet (a pending push, at any depth) and can go back, the key is the guarded `GoBack`,
  which supersedes that change, so the pushed page never appears. Otherwise, with more than one page
  shown, it is one guarded back to the page below (or, for a refused destination, another attempt to
  arrive). Either way it is consumed; only when neither the model nor the pages shown have anywhere
  to go is it passed on to the app or the OS. A plain `NavigationStack` keeps MAUI's behaviour.
- **Errors in lifecycle callbacks** go to the error boundaries above the component that registered
  them, posted like a handler's asynchronous failure (`ReactorRoot.ReportCallbackError`). For a
  component unmounted by then (a page just left, or a component replaced) the search still starts at
  the component and skips unmounted ancestors, so a boundary that survived catches it; with no
  mounted ancestor the error goes to `UnhandledException`. A
  throwing `onNavigatingFrom` or `onNavigatingTo` cancels the navigation. (On WinUI the exception
  propagates to the caller of `Navigate` or to the reconciler.)
- **Lifecycle divergences from WinUI's policy**, which stays in `NavigationHostLifecycle` (§2.3):
  - the previous route in the contexts is the route the host showed;
  - `SetState` runs no guard, as on WinUI, but its departure is reported with mode `Reset` and gets
    `onNavigatedFrom` (WinUI reports it as `Push` without `onNavigatedFrom`);
  - completion runs, posted, when the navigator is idle, no user pop is pending (MAUI no longer
    lists the page, or its window expired), and every change of the model has been shown.
    Navigations then complete together, in order: every page left gets `onNavigatedFrom`, and only
    the latest generation's destination gets `onNavigatedTo`. WinUI completes an interrupted
    navigation when it is interrupted. A navigation is tracked from the render that commits it, so
    one replaced by a newer render before the navigator saw it still reports its departure.
- **Diagnostics** are written where WinUI's are: `NavigationCompleted` from `Navigate`, and
  `NavigationCancelled` ("destination guard") for a refusing destination.
- **`NavigationMode`** would be ambiguous with `Microsoft.Maui.ApplicationModel.NavigationMode` in a
  file that imports `Microsoft.UI.Reactor.Navigation`, so the package's global usings declare a
  `NavigationMode` alias for Reactor's, beside the `Element` one (opt out with
  `ReactorImplicitUsings=false`).
- **Gate evidence.** Core: `tests/Reactor.Maui.Engine.Tests/NavigationModelTests.cs` (operations,
  guard order, `PopToIndex` bounds, state, the hook's order and lookup, a handle over an injected
  scheduler). AOT: `tests/aot_trim_proof/Reactor.Navigation.AotSmoke`, a Native AOT console over
  `Reactor.Core` that resolves a deep link and round-trips a `NavigationState` through a
  source-generated `JsonSerializerContext`; published for osx-arm64 with no trim or AOT warning, it
  prints `PASS`, and CI runs it (`maui.yml`). It covers the shared model, not a trimmed MAUI app.
- **Not done in 4b-1:** converging the lifecycle policy into the core and having WinUI's host call
  it; a trimmed MAUI app build (the AOT gate runs on the model alone).

## 3. Decisions to review

1. **Share the model, keep a handle per renderer.** WinUI's API is unchanged, and MAUI gets the
   same names. The cost: a component that sets WinUI-only options doesn't compile on MAUI.
   Rejected alternatives:
   - moving the handle, which breaks `Slide` usages, as measured above;
   - a neutral easing type, which changes a public property's type;
   - mirroring everything, which gives two copies of the guard and lifecycle semantics.

   As built, the model and the hook state are shared, but the lifecycle policy is still per
   renderer (§2.3, §2.6), so 4b-1 has two copies of the lifecycle semantics until it converges.
2. **A platform pop becomes one depth-aware guarded navigation.** A refused pop restores the page,
   as today.
3. **Android hardware back is consumed by the host when there is a page to go back to,** and passed
   on at the root.

## 4. Outline: 4b-2, declarative Shell

These are detailed in this spec before implementation starts.

- **Elements:**
  - `Shell(items...)`, with the selected item as state;
  - `TabBar(...)`, `FlyoutItem(...)`, `ShellTab(...)` and `ShellContent(route, page)`;
  - flyout header, footer and custom content; `FlyoutBehavior` and `FlyoutIsPresented`;
  - per-page `NavBarIsVisible`, `TabBarIsVisible`, presentation mode, `BackButtonBehavior` and the
    search handler.
- **Navigation inside a tab** uses the same model and lifecycle, but a separate adapter over the
  tab's `ShellSection` navigation, because MAUI doesn't allow a `NavigationPage` inside Shell. 4b-2
  specifies:
  - which navigations `Shell.Navigating` guards, and when cancelling it means no page moves;
  - whether a tab keeps its stack when it is deselected;
  - absolute routes, which reset a tab or select another, and relative ones.
- **Typed routes.** The selected tab is a value the app holds. A query parameter is a field of a
  route record. Deep links go through `DeepLinkMap`, and only at the app boundary.
- **Platform pops** are tracked with MAUI's navigation-completed signal, which retires the
  3-second cutoff (spec 068 §3).
- **Scope** follows the ports' census (spec 068 §2, Shell row).

## 5. Outline: 4b-3, dialogs, back button, lifecycle

- **Page dialogs** are declarative, like WinUI's `ContentDialog`: `Alert(...)`, `ActionSheet(...)`
  and `Prompt(...)` elements with `IsOpen` and `OnClosed(result)`. 4b-3 specifies:
  - which page presents a dialog (the active page of the element's window);
  - one dialog at a time, with later ones queued;
  - a queue per window;
  - stale results dropped by generation after unmount, navigation or reopening. So `OnClosed` runs
    at most once per open, and exactly once for an open that is still live when the user answers.

  MAUI can't close an alert from code, so turning `IsOpen` off while one is showing takes effect
  when the user answers.
- **System back button:** `UseSystemBackButton(nav)` (the WinUI name, without the `Window`), plus an
  overload taking `Func<bool>` for back handling outside navigation. It is wired through the window's
  back handling on Android.
- **Modal presentation style:** a `PageElement` property mapped to MAUI's iOS-specific presentation
  style, ignored elsewhere.
- **App and window lifecycle:** a hook raising `Activated`, `Deactivated`, `Stopped`, `Resumed` and
  `Destroying` from the window. Its name converges with whatever WinUI has, or is proposed for both
  renderers.
