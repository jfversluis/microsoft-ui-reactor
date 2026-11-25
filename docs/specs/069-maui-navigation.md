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

This spec details 4b-1 (§2) and 4b-2 (§4); §5 outlines 4b-3, which is detailed here before its
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
    065 §7), it completes when the window expires. Inside Shell, 4b-2 replaces the window with MAUI's
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
    §7). Inside Shell, 4b-2 replaces that window with MAUI's navigation-completed signal; a
    `NavigationPage` keeps it.
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

## 4. 4b-2: declarative Shell

MAUI's Shell is a tree of `ShellItem`s (a `FlyoutItem` or a `TabBar`), each holding `ShellSection`s
(tabs), each holding `ShellContent`s (top tabs, each with a root page). Every section has its own
navigation stack. Reactor builds that tree from elements, holds the selection and every stack as
app state, and leaves MAUI's string routing to the deep-link boundary. The facts below are from
MAUI's source (`src/Controls/src/Core/Shell`), as cited in the 4b-2 research notes.

### 4.1 Elements

```csharp
var (tab, setTab) = UseState("home");
var homeNav = UseNavigation<HomeRoute>(new HomeRoute.List());
return Shell(tab, setTab,
    TabBar(
        ShellTab("home", "Home", NavigationHost(homeNav, MapHome), Icons.Home),
        ShellTab("settings", "Settings", Page("Settings", SettingsView()), Icons.Settings)))
    .FlyoutBehavior(FlyoutBehavior.Disabled);
```

(As built; §4.7 lists what changed from the reviewed design: the callback is `onSelectedChanged`, the
menu item is `ShellMenuItem`, icons are optional and last, and children may be null.)

- **`Shell(string? selected, Action<string>? onSelectedChanged, params Element?[] items)`** renders
  a `Shell`. `selected` is the key of the tab (or top tab) shown. Items are `TabBar(...)`,
  `FlyoutItem(...)` and `ShellMenuItem(...)` elements.
- **`TabBar(params ShellTabElement?[])`** and **`FlyoutItem(string? title, params
  ShellTabElement?[])`** (icon with `.Icon(...)`) render a `TabBar` and a `FlyoutItem`. A null tab
  is skipped, so a tab can be conditional.
- **`ShellTab(string key, string? title, Element? content, ImageSource? icon = null)`** renders a `Tab` (a
  `ShellSection`) with one `ShellContent`. `content` is a `Page(...)` or a `NavigationHost(...)`.
  - With a `NavigationHost`, the host's root route is the content's page, and the routes above it
    are pushed onto the section's stack (§4.2).
  - A section has exactly one stack owner.
- **Top tabs:** `ShellTab(key, title, params ShellContentElement?[])`, where each
  `ShellContent(key, title, page, icon = null)` holds a page. In 4b-2 top-tab contents are pages, not
  navigation hosts. A section has one stack, shared by its contents, so independent per-top-tab
  histories would need saving and restoring stacks on every switch. None of the seven ports uses
  top tabs with a stack; this waits for one that does.
- **Keys** identify items across renders and are the selection's vocabulary. Every `TabBar`,
  `FlyoutItem`, `ShellTab` and `ShellContent` takes a key (`TabBar` and `FlyoutItem` default to
  their position). Selectable keys (tabs and top tabs) are unique across the whole Shell, and a
  duplicate throws when the Shell renders. Reactor always builds the items itself and never relies
  on MAUI's implicit wrapping (a content added straight to `Shell.Items`), so it owns their
  identity.
- **Shell properties:**
  - `FlyoutBehavior`;
  - `FlyoutIsPresented` with `OnFlyoutIsPresentedChanged`;
  - `FlyoutHeader`, `FlyoutFooter` and `FlyoutContent`, as elements. MAUI binds the header and
    footer one time, so a changed header element is reconciled inside the same native header.
  - The appearance colors: `BackgroundColor`, `ForegroundColor`, `TitleColor`, and `TabBar*`.
- **Page properties** (on `PageElement`, applied as Shell's attached properties when the page is in
  a Shell):
  - `NavBarIsVisible`, `TabBarIsVisible`, `TitleView` and `SearchHandler` update live;
  - `PresentationMode` is read only when the page is pushed, so it is set before the push;
  - `BackButtonBehavior`: text and icon overrides, visibility, and `IsEnabled` (which disables the
    back button and the iOS swipe).

### 4.2 Navigation inside a tab

- **A native-neutral binding.** The 4b-1 binding talks to `ReactorNavigationPage` and
  `StackNavigator` directly. 4b-2 puts an internal surface interface between the host and the
  native stack, implemented by the `NavigationPage` navigator and by a new section adapter. The
  interface covers: declare pages with an animation flag, report user pops, report the back key,
  report idle, and record the last operation's animation flag. The host finds its surface through
  the element it renders. Inside a `ShellTab`, the host renders a section stack, never a
  `NavigationPage`, which MAUI doesn't allow inside Shell.
- **The section adapter** reconciles the declared pages against the section:
  - **The root is the content's page.** `ShellSection.Stack[0]` is a null sentinel, and the root
    belongs to `ShellContent.Content`. The adapter sets the declared root as the content's page,
    which also replaces the root on `Reset` or `Replace`. The pages above the root go through
    `ShellSection.Navigation`, which needs no registered route.
  - **Operations.** The adapter uses `PushAsync` and `PopAsync` (awaited; they complete after the
    platform animation) and `PopToRootAsync`. It never inserts or removes pages below the top
    (MAUI's `InsertPageBefore` and `RemovePage` complete synchronously in Shell's state but not on
    the platform). A change below the top pops to the deepest page that is still the same, then
    pushes the rest, so every operation it issues has a task to await.
  - **One operation at a time.** Each operation is awaited before the next one starts. MAUI's
    handler-based sections throw if two overlap, and rapid overlapping operations have left blank
    pages on iOS.
  - **Origin tagging.** Before each operation, the adapter records it as its own, by expected source
    and depth. The `Navigating` and `Navigated` events that match are its own, and are never
    treated as the user's.
- **A user's back: preflight in `Navigating`, commit on completion.** Shell reports every user back
  as a cancellable `Navigating`: the iOS back button and swipe, the Android toolbar back, and the
  Android back key. The source is `Pop`, or `PopToRoot` when the target is the content's root
  (`ShellNavigationManager.CalculateNavigationSource`). Both are handled the same way.
  - **Classification.** An event is a user back when:
    - it isn't the adapter's own (origin tagging above);
    - its target is a page in this section's stack;
    - it removes pages from the top.

    A modal dismissal and a root exit (an empty target) are not user backs.
  - **Preflight**, synchronously in `Navigating`:
    - the departing page's `onNavigatingFrom`;
    - the destination's `onNavigatingTo` (the destination is already mounted below).
    - If either refuses, the event is cancelled, and no page moves on any platform.
    - If both accept, the adapter records a **single-use approval**: the section, the identities of
      the departing and destination pages, the depth, and the model's version. The approval is
      checked against the model after the callbacks run, since a callback can navigate.
  - **The native operation is then busy** until the platform has finished the transition, whether
    the back completed or was cancelled:
    - **iOS and Mac Catalyst.** The adapter registers on the navigation controller's transition
      coordinator.
      - A cancelled interaction is noted at the interaction-changed callback.
      - Settlement waits for the transition-completed callback (`NotifyWhenTransitionCompleted`),
        after UIKit's rollback or completion animation. A swipe the user still holds is a transition
        in progress, so it stays busy.
    - **Android.** Back isn't interactive.
      - The adapter checks the same `ShellNavigatingEventArgs` on the next dispatcher turn, after
        every `Navigating` handler has run. If any handler cancelled it (`Cancelled`), the operation
        settles as cancelled. A deferral counts as pending until it completes.
      - Otherwise MAUI performs the pop, and the operation settles at `Navigated` for that section.
    - **No timeout releases the busy state.** If settlement hasn't arrived after a few seconds,
      Reactor logs a warning through `DiagnosticLog`. It then keeps waiting until the platform
      reports no transition in progress (the navigation controller's `TransitionCoordinator` is null
      on iOS) before it treats the operation as settled.

    While the operation is busy, app navigations are held and reconciled when it settles.
    `WindowPageSetter` waits on the same busy state.
  - **Commit** on completion. The approval is consumed once at both layers: the model moves back to
    the approved entry through an internal commit that skips its guard, and the host's lifecycle
    takes the destination as already approved, so its `onNavigatingTo` doesn't run again. The
    `onNavigatedTo` and `onNavigatedFrom` callbacks then run as usual.
    - If the model's version changed while the operation was busy, the approval is void, and the
      host reconciles to the model.
    - A cancel discards the approval; the model never moved.
  - **At the root**, Shell raises `Navigating(Pop)` with an empty target to offer the back key. The
    adapter leaves it alone, so the key reaches the app or the OS.
  - `BackButtonBehavior.Command` isn't used for interception, because MAUI doesn't run it for the
    Android back key.
- **Visible intermediate steps.** A change below the top pops to the deepest page that is still the
  same, then pushes the rest. The user may see those steps. They are the adapter's own operations
  (tagged), and never become separate navigations in the model.
- **Completion** of the adapter's own operations is its awaited task. A user pop completes at
  `Navigated`. Inside Shell, this replaces the 3-second platform-pop window (spec 068 §3).
- **A deselected tab keeps its stack.** MAUI doesn't reset a section's stack when the selection
  changes, and neither does Reactor: the app's handle for that tab still holds its routes.
- **Window swaps.** `WindowPageSetter` treats a Shell whose section adapters are busy like a
  navigating `NavigationPage`, and waits before replacing the window's page.

### 4.3 Selection

- **The app holds the selected key.** From code, Reactor sets `Shell.CurrentItem` (a different
  item), `ShellItem.CurrentItem` (a tab) or `ShellSection.CurrentItem` (a top tab). MAUI raises
  `Navigating` with `CanCancel = false` for these, followed by `Navigated`. Reactor tags these as its
  own, as the adapter does.
- **A user's switch** (a tab tap, a flyout item, a top-tab swipe) is reported when it is committed:
  `onSelectedChanged` runs from `Navigated`, with the key of the selection now shown. Reactor doesn't
  report from `Navigating`, which is only a proposal.
- **A refused switch is restored**, like any controlled control (spec 067 §14). If the app doesn't
  change state, the latest declared selection is set back once state settles. Each restore has a
  generation, so rapid switches restore only to the latest declaration. Cancelling in `Navigating`
  isn't used, because Windows' `NavigationView` has already changed visually by then.
- **A tab switch isn't a navigation** in the model, so `UseNavigationLifecycle` guards don't run
  for it. An app that wants to refuse a switch refuses it in `onSelectedChanged`.
- **Structural changes are staged:** add new items, select, let the selection settle, then remove
  old items. MAUI's own fallback for a removed current item re-navigates asynchronously, which
  Reactor never relies on. A removed section's adapter stops.
- **Invalid or missing selection:**
  - A Shell with nothing selectable (empty, or menu items only) has no selection, and `onSelectedChanged`
    isn't called.
  - In a Shell with selectable items, a selected key that doesn't exist selects the first selectable
    item. `onSelectedChanged` reports that once, when the selection shown changes, not on every render.
- **Swapping `ShellItem`s is heavy** (a new native item renderer on iOS, a fragment swap on
  Android). Apps with tabs should use one `TabBar` and change its tabs, which MAUI updates
  incrementally.

### 4.4 Deep links and typed routes

- A deep link is a URI resolved by the app with `DeepLinkMap<TRoute>` into a route: the selected tab
  plus the routes for that tab's handle. The app then sets that state. Reactor never calls
  `GoToAsync` with a string.
- A query parameter is a field of the route record.

### 4.5 Not in 4b-2

- **Shell's search handler** is a class the app subclasses, so it maps poorly to elements. 4b-2
  exposes `SearchHandler` as an object the app passes, live. A declarative search element can follow
  if the ports need one; none of the seven uses it.
- **A modal pushed from a Shell page** goes through the section's modal path (MAUI turns it into
  `GoToAsync`). It is covered for `NavigationHost` modals, and MAUI's known modal issues (#14858,
  #7522, #22444) are worked around only if a fixture reproduces them.
- **Windows** is validated in 4g.

### 4.6 Gate for 4b-2

- **Headless tests:** building, updating and removing items, tabs and top tabs; selection from code
  and from the user, including a refused switch; per-tab stacks and their persistence across
  selection; a guarded user back, accepted and refused; page attached properties; flyout state.
- **Device fixtures on Mac Catalyst, iOS and Android**, asserting the native displayed page and
  depth, not only `ShellSection.Stack`:
  - a tab tap reported and restored when refused, including rapid switches;
  - push, pop, `Reset` and root replacement in a tab;
  - a restored multi-page history;
  - native back (the back button, the Android back dispatcher), accepted and refused, with the guard
    running exactly once and no page moving on refusal;
  - a refusal by the destination, and back at the root reaching the app or the OS;
  - stacks kept across tab switches;
  - rapid push, switch and back;
  - removing and reordering the selected tab;
  - `NavBarIsVisible` and `TabBarIsVisible` live;
  - a live change to the flyout header and custom flyout content;
  - the flyout opened by the user, reported;
  - a modal pushed from a Shell page;
  - a deep link selecting a tab and a stack;
  - the typed equivalents of an absolute reset (`//route`) and of a relative back with a query
    (`..?refresh=true`).
- **The interactive iOS swipe**, completed and cancelled, can't be driven from inside the app.
  - Its state machine is covered headlessly, by raising MAUI's events in the order the iOS renderer
    raises them: preflight, then completion, cancel or the fallback.
  - Before the 4b-2 gate, the real gesture is also driven on the iOS simulator through Appium: an
    edge swipe that completes, and one that is released early. The checks are the page shown, the
    model, the guard count, and an app navigation held during the gesture and applied after it.
  - A cancelled swipe followed by a window swap is included.
- Also covered: a modal dismissal that leaves the model alone, back to the root reported as
  `PopToRoot`, and two sections navigating at the same time without their origins colliding.
- Validation is pinned to the MAUI version the workload ships.
- **WinUI tiers** green (no WinUI change is expected), and `/perf` within noise.

### 4.7 As built (4b-2)

Built against `Microsoft.Maui.Controls` 11.0.0, the version the workload ships. Where the build
differs from §4.1–4.6, or §4 left a choice open:

- **Where the events are handled.** Reactor renders a `Shell` subclass, `ReactorShell`, and handles
  navigation in its `OnNavigating` and `OnNavigated` overrides, which run after every `Navigating` and
  `Navigated` subscriber, so a cancel by another handler is already visible.
- **The Android back key is taken in `OnBackButtonPressed`.** On MAUI 11.0.0 both the Android back
  key and the toolbar back reach `Shell.OnBackButtonPressed` (dotnet/maui#9095). `ReactorShell`
  overrides it on Android: a page that handles the key goes first; then, with the shown section's
  host showing its model, the guards run as in the preflight (`PreflightBack`) and the navigator pops
  as its own operation, holding the approval; the model moves (`CommitBack`) once Shell has popped. If
  another `Navigating` handler cancels that pop, the approval is discarded and the model is unchanged.
  A key pressed while that pop is outstanding is consumed and queued, and goes back one more level
  once the model shows the first (its guards run then), so two presses go back two levels with each
  level's guards running once, as two back-key presses do for a `NavigationPage` (§2.6). A refused or
  cancelled level drops the presses queued behind it. So Android has no preflighted platform pop to
  watch and no 16 ms polling. A back
  the host can't preflight is the guarded `GoBack` of §2.6; at the root the key is passed on. iOS and
  Mac Catalyst keep the `Navigating` preflight: the swipe never reaches `OnBackButtonPressed`.
- **The surface is one-directional.** The binding declares pages to `INativeStack`; a user's back
  reaches the binding through the navigator's `Binding` (`PreflightBack`, `CommitBack`,
  `DiscardBack`, `OnBackButton`), not through the interface.
- **Origin tagging** is by kind (push or pop) and section, not by depth: a pop's target is `..`, which
  carries no depth. A relative target (`..`, or a pushed page's route) belongs to the section shown;
  an absolute one names the section's route or, since Shell trims implicit item and section routes from
  the location, its content's route. Each of the navigator's operations claims one event, and the
  claim clears the tag, so it covers only the part of the call before MAUI raises `Navigating` (its
  first await, or a pending navigation of the section), never the animation. A user's back must
  target the shown section too.
- **The iOS and Mac Catalyst coordinator.** UIKit has no `NotifyWhenTransitionCompleted`; the
  navigator registers `AnimateAlongsideTransition` with an empty animation, whose completion UIKit
  runs after the completion or rollback animation, and `NotifyWhenInteractionChanges`, which logs a
  cancelled swipe. A cancelled transition settles the back as cancelled; a completed one settles at
  `Navigated` for the section, when MAUI has removed the page. After 3 s without either, Reactor logs a
  warning through `DiagnosticLog`. Nothing settles on time alone: only a transition the coordinator
  reported, and that has ended without a callback, is settled, by whether the page is still in the
  section's stack. Before the transition starts (a `Navigating` deferral holding the pop) the back keeps
  waiting for `Navigated` or a cancel. On MAUI 11.0.0 the
  iOS back button also arrives as `Navigating`: `ShellSectionRenderer.SendPop` goes through
  `Shell.OnBackButtonPressed`, which pops through `ShellSection.Navigation`.
- **Cancels after the preflight.** MAUI's deferral state isn't public, so while a preflighted back is
  pending the navigator re-reads the same `ShellNavigatingEventArgs.Cancelled` every dispatcher turn
  (16 ms): a cancel by a later handler, or a deferral that ends cancelled, is seen on the next turn.
  On Android this applies only to a back MAUI offers without the key (the key itself is taken above).
- **A back that can't be preflighted** (the host isn't showing the model, or the navigator is in the
  middle of its own operation) is cancelled and becomes a guarded back of the model, which the
  navigator then shows, as the back key does for a `NavigationPage` (§2.6).
- **Approval.** `NavigationHostBinding.PreflightBack` runs the handle's guards without navigating
  (`NavigationStack.CanPopToIndex`, a new internal core method), then the destination's
  `onNavigatingTo`; the version is checked after each. The navigator checks the page identities and
  depth when the platform finishes, and `CommitBack` moves the model through
  `NavigationStack.PopToIndex(index, approved: true)`, an internal overload that skips the guards.
  The host's next declaration takes the transition the preflight built, so the destination isn't
  asked again. A cancel, or a commit after the model moved, re-renders the host, which shows the model.
- **Selection** is detected by object, not by key: a `Navigated` after which Shell shows a section or
  top tab other than the one Reactor last selected is the user's switch. Reactor's own writes need no
  tag. A tab key with top tabs keeps the section's current top tab; the key reported for a section
  with several top tabs is the top tab's.
- **Staged changes** run in one pass: new items are added, the selection applied (skipping the items
  about to go), old items removed, then the order fixed by removing and re-inserting every item but
  the current one. Inner syncs (a tab's content) apply the selection too, so a removed current item
  is replaced before it goes even when the tab bar itself didn't change. MAUI's model applies the
  selection synchronously, so "let the selection settle" needed no extra wait on the three platforms.
- **A new root** (`Reset`, `Replace` at the root) pops the section to its root without animation and
  then sets `ShellContent.Content`; the device fixtures check the platform shows the new page.
- **A navigator outlives its section until it settles.** When its tab goes, its content changes or
  the Shell's tree unmounts (a root swap: the engine unmounts the old tree before the window's page is
  replaced), a navigator that is running an operation or waiting on a user's back stays listed by its
  Shell (`ReactorShell.Lingering`) until that finishes, so `WindowPageSetter` keeps waiting. A tab whose
  content changes from a host to a page or to top tabs is then popped to its root without animation, as
  the navigator's own operation. The stopped navigator then dismisses its modals.
- **Modals.** `OwnedModals` is shared with the `NavigationPage` navigator: each stack pops only its own
  modals when they are on top and, once unmounted, dismisses them through the window it presented them
  in, waiting for a covered one to be uncovered. A page change in a section while one of its modals is
  shown is held until the modal is gone (Shell would dismiss the modal first and report it as the
  user's); the host's lifecycle completes once the page change is shown.
- **An own operation another handler cancels isn't retried.** After each of its operations the
  navigator compares the section's stack and modal stack with what they were; if Shell didn't move
  (another `Navigating` handler cancelled it, at once or after a deferral) and the app hasn't declared
  anything new meanwhile, the declaration is left as refused: a warning is logged through `DiagnosticLog`, the navigator goes idle without retrying, and
  the host's lifecycle waits (`IsHolding`) until the app declares something else.
- **A host replacing another in the same tab** takes the section over: the replaced navigator doesn't
  pop (the new one reconciles the section to its own pages, popping to its root once), and the new one
  starts nothing until the replaced one has finished any operation still running, so they never
  overlap. A navigator waiting for the one it replaced counts as busy, so a chain of replacements
  (A, then B, then C) waits for A.
- **Modal route pages are rejected.** A route page of a Shell tab's host with a modal
  `PresentationMode` throws, pointing at the host's `Modals`: Shell would put it on the window's modal
  stack, outside the section's stack the navigator reconciles.
- **A host in a `ShellTab` ignores the stack's title, icon and bar colors** (the tab's come from
  `ShellTab`, the colors from the Shell's appearance) and logs a warning once if they are set.
- **`TitleView`** is an element in a second slot of the page; `SearchHandler` is the app's object.
  Page properties are applied whether or not the page is in a Shell (nothing else reads them), and
  `PresentationMode` is set at mount, before the page can be pushed.
- **`ShellMenuItem`** is built: Reactor wraps a `MenuItem` into MAUI's `MenuShellItem` itself. The
  factory is named `ShellMenuItem`, leaving `MenuItem` for the menus of 4e.
- **API after review.** `onSelected` became `onSelectedChanged` (like `OnSelectedIndexChanged`); icons
  are an optional last argument (`ShellTab`, `ShellContent`) or `.Icon(...)` (`FlyoutItem`, the top-tab
  `ShellTab`); `TabBar`, `FlyoutItem` and the top-tab `ShellTab` take nullable children.
- **A relative back with a result** (`..?refresh=true`) is a result in app state or context that the
  destination reads, then `GoBack`: the destination stays mounted. Putting the result in the
  destination's route (`PopTo` and `Replace`) also works but remounts the page.
- **Naming.** With `using static Microsoft.UI.Reactor.Maui.Factories;`, `Shell` names the factory, as
  `Label` and `Button` do, so MAUI's type needs `Microsoft.Maui.Controls.Shell` (or an alias).
- **Tests.** Headless Shell raises `Navigated` only for a window that belongs to an app, and only once
  the destination page has appeared, so the headless tests put the window in an `Application` and make
  the shown page appear each turn, as the platform does. The iOS swipe's state machine is driven
  headlessly through the calls MAUI's renderer makes (`ProposeNavigation` from the gesture's
  `ShouldBegin`, `SendPopping` on completion) and a fake coordinator. On device, a tab tap goes through
  `IShellItemController.ProposeSection`, which the tab bars call; the native back is
  `ShellSectionRenderer.ShouldPopItem` (the back button's path; a direct `PopViewController` bypasses
  Shell) on iOS and Mac Catalyst, and the activity's back dispatcher on Android.
- **Gate evidence.** Headless: `tests/Reactor.Maui.Tests/ShellTests.cs` (55 tests). Device: the 17
  `Shell_*` fixtures pass on Mac Catalyst, the iOS simulator and the Android emulator, with the rest
  of the suite. Each key oracle was mutation-checked (the code broken, the test seen to fail).
- **The real iOS edge swipe** was driven through Appium (XCUITest, iPhone 18 Pro simulator, iOS 27)
  against opt-in `ManualSwipe_*` fixtures, which run only when the filter names them and wait for the
  gesture. A completed swipe passed, with and without an app navigation made while it was in flight:
  the guard ran once, the page left, the model followed, and the held navigation was shown after the
  gesture. A swipe released early could not be produced: every
  synthesized edge drag, even 38 pt over 3 s, completed, so the cancelled path is covered headlessly
  only (the coordinator's cancelled completion), and those two fixtures wait for a human release.
- **Not done in 4b-2:** a declarative search element and top tabs with their own stacks, which wait
  for a port that needs them (§4.1, §4.5).

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
