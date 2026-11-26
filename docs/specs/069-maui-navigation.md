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

This spec details 4b-1 (§2), 4b-2 (§4) and 4b-3 (§5), each with an "as built" section.

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
  level's guards running once, as two back-key presses do for a `NavigationPage` (§2.6). A refused,
  cancelled or failed level drops the presses queued behind it; each level's outcome is tracked
  explicitly, so a pop that completes synchronously isn't taken for a refusal. A pop that throws
  discards its approval (the model is unchanged), leaves the navigator idle and reports the error
  through the handler error path (`HandlerContext.ReportError`). So Android has no preflighted platform pop to
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
- **A section is navigated only once it is in the Shell.** A tab mounts before its item joins the
  Shell, and MAUI can't push onto a detached section, so a host whose handle already has a history
  (a Shell mounted over it, a tab added later) waits; the Shell runs its navigators when it adds items.
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
- **Gate evidence.** Headless: `tests/Reactor.Maui.Tests/ShellTests.cs` (58 tests). Device: the 17
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

## 5. 4b-3: dialogs, the system back button, presentation style, lifecycle

### 5.1 Page dialogs

Five of the seven ports show alerts, action sheets or prompts (spec 068 §2), and they do it from
event handlers: `if (await DisplayAlertAsync(...)) Delete();`. Reactor offers that shape directly,
and a declarative one like WinUI's `ContentDialog`. Both feed the same queue.

- **Imperative: `UseDialogs()`** returns a `Dialogs` object bound to the component's window. It is
  stable across renders, so handlers and effects can capture it.
  - `Task Alert(string title, string? message, string close)`: an informational alert with one
    button (MAUI's one-button overload).
  - `Task<bool> Alert(string title, string? message, string accept, string cancel)`: true for
    `accept`.
  - `Task<string?> ActionSheet(string? title, string? cancel, string? destruction, params string[] buttons)`.
  - `Task<string?> Prompt(string title, string? message, string accept = "OK", string cancel = "Cancel", string? placeholder = null, int maxLength = -1, Keyboard? keyboard = null, string? initialValue = null)`.
- **Declarative: `Alert(...)`, `ActionSheet(...)` and `Prompt(...)` elements**, with `IsOpen` and
  `OnClosed(result)`, placed anywhere in the tree. They render nothing native. While `IsOpen` is
  true, the dialog is queued once; the answer is reported through `OnClosed`, and the app sets
  `IsOpen` to false. Re-opening (false, then true) queues a new dialog.
- **The presenter is resolved when a dialog is dequeued**, not when it is requested. It is the
  window's top modal if one is shown, otherwise the window's `Page`. Inside a `ReactorPage` hosted by
  a non-Reactor app, the window is the page's, followed as it changes (§5.4), as `UseWindowSize` does.
- **One native dialog at a time, per window.**
  - The next dialog is presented only once the previous one's native UI has gone, not when its
    answer arrives: MAUI completes the answer before UIKit finishes dismissing.
  - The barrier is the platform reporting no presented dialog:
    - iOS and Mac Catalyst: the presenting view controller has no `PresentedViewController` that is
      an alert;
    - Android: the dialog is dismissed. AppCompat's dialog dismisses itself in the looper turn after
      the button's callback (a list row's click, right after it), so the next dialog is shown from a
      turn the queue posts when the answer arrives;
    - on iOS and Mac Catalyst, checked once per frame.
  - A dialog is also held while the window's page or a modal is in a navigation transition: the
    queue waits for the running navigator's `BecameIdle`.
  - Queues are per window, so two windows don't block each other. Every queue operation runs on the
    window's dispatcher.
- **Outcomes.**
  - **Answered:** an imperative task completes with the answer.
  - **The imperative tasks never fault and are never cancelled**, so `if (await dialogs.Alert(…))` is
    safe in an `async void` event handler. A dialog the user can't answer completes with the cancel
    answer: `false` for a two-button alert, `null` for an action sheet or a prompt.
  - **Window destroyed:** every queued or showing dialog of that window completes with the cancel
    answer (a declarative one is dropped); MAUI unsubscribes its alert manager then, and a request
    without a subscriber is silently dropped. The window's queue stays destroyed, so a dialog
    requested later (even from a later `Destroying` handler) is settled at once, until MAUI creates
    the window again (an Android activity recreated).
  - **Presentation failed** (no presenter, or the platform threw): an imperative dialog completes
    with the cancel answer, the error is logged (`DiagnosticLog`), and it goes to the calling
    component's error boundary if there is one; a declarative dialog reports the error to its
    element's boundary. The queue moves on.
  - **The dispatcher refused the work** (the app is shutting down): the request is settled where it
    is, and so is everything the queue holds (the dialog shown included); the queue is then terminal,
    settling every later request at once, until MAUI creates the window again. Nothing is left waiting.
  - **A dialog being presented when the queue terminates** (the presentation destroyed the window, say)
    is settled too: it is reserved as the dialog shown, under the queue's lock, before it is presented.
  - **A failure report that can't be delivered** (the render thread refuses it) is logged; the task
    has already completed, and the queue goes on.
  - **Declarative `OnClosed`** runs at most once per open, and only while that open is still the
    element's current one. Unmounting, turning `IsOpen` off (from the render that does it, before
    the window can take a waiting request), or re-opening makes an earlier open stale. A stale answer
    is dropped, and a stale dialog that hasn't been shown is removed from the queue.
  - **The answer goes to the `OnClosed` of the content shown.** The element's latest title and
    buttons are read when the dialog is dequeued, and the `OnClosed` of that same render receives the
    answer. A dialog showing "Delete A?" never runs a later render's callback for B.
  - MAUI can't dismiss a dialog from code. A dialog made stale while it is showing stays on screen
    until the user answers, and that answer is dropped.
  - An exception from `OnClosed` goes to the element's error boundary.

### 5.2 The system back button

- **`UseSystemBackButton(Func<bool> onBack)`** registers a handler for the window's back request:
  the Android back key, and Windows' back button in 4g. The handler returns true when it handled
  the back.
- **`UseSystemBackButton(NavigationHandle<TRoute> nav)`** is WinUI's shape, without the `Window`. Like
  WinUI's, it consumes the back whenever `nav.CanGoBack` is true, whether or not a guard lets
  `GoBack` happen. A refused back must not fall through to the page or the OS.
- **Where it is wired (as built, §5.6).** On Android each window with a registered handler gets one
  AndroidX `OnBackPressedCallback`, added after MAUI's so it is asked first, and enabled only while a
  handler is registered for a tree shown in that window. It routes the back as MAUI's window does
  (`IWindow.BackButtonClicked`: the top modal, else the window's `Page`), asks the handlers, and if
  none handles it, disables itself and dispatches the key again, so MAUI's own handling runs
  unchanged. A MAUI modal on Android is a dialog with its own back dispatcher, which takes the key
  while the modal is shown and never reaches the activity's, so each modal's dialog gets the same
  callback. A Shell adds a back callback of its own, which can be asked before Reactor's, so the
  Shell's `OnBackButtonPressed` asks the handlers too; a back asks them at most once whichever path it
  takes. Reactor's pages don't override `OnBackButtonPressed` otherwise; the navigation page's and the
  Shell's overrides from §2.4 and §4.2 stay.
- **Only for the system back key, on Android.** Windows' back button joins in 4g. On iOS and Mac
  Catalyst there is no callback: MAUI calls `SendBackButtonPressed` only for Shell's navigation-bar
  back (`Shell.cs`, 11.0.0), which goes through the stack's guards (§4.2).
- **Only the visible page's handlers are eligible.** A handler belongs to the page its component
  renders (a route's component returning `Page(…)`), else the nearest page above its component. A
  back asks only the handlers of the page the user sees, and of the components above every page
  (those that render the tree's root page):
  - for a navigation stack, its top page;
  - for a Shell, the current tab's top page;
  - for tabs, the selected tab;
  - for a flyout, the detail page (and the flyout while it is presented);
  - for a modal, the modal itself.

  Pages kept in a back stack and deselected tabs never intercept. Among eligible handlers, the
  deepest component goes first; between siblings at the same depth, the one mounted most recently.
  The handler list is snapshotted when the back arrives, and each handler runs at most once per
  back.
- **Fall-through.** If no handler handles it, the back continues as before: a `NavigationHost`'s or
  Shell's guarded back, then MAUI and the OS.
- **The root on Android.** Android shows the predictive back-to-home preview only while no back
  callback is enabled. Reactor's callback is enabled only while a `UseSystemBackButton` handler is
  registered in the window, so a `Page` or `Tabs` root without one keeps the preview (MAUI itself
  keeps its callback enabled for a page that overrides `OnBackButtonPressed`, as the navigation page
  and the Shell do since 4b-1 and 4b-2).
  - When nothing handles the back at the root, the key goes on to MAUI and then to the platform's
    default: the activity moves to the back, as without Reactor.
  - Device fixtures check a handler at the root, the default without one, and the callback's state
    for a `Page` and a `Tabs` root with and without a handler (spec 065 §7).
- **iOS** has no back key, so the hook doesn't fire there. A stack's back button and swipe go
  through the stack's guards (§2.4, §4.2).

### 5.3 Modal presentation style

`PageElement.ModalPresentationStyle` maps to MAUI's iOS-specific `ModalPresentationStyle`
(`FullScreen`, `PageSheet`, `FormSheet`, `OverFullScreen`, …). It is set before the page is
presented, so it applies to modals in `NavigationStack.Modals`, `NavigationHost` modals and Shell
modals. Other platforms ignore it, and it is not applied on Mac Catalyst (§5.6).

### 5.4 App and window lifecycle

- **`UseWindowLifecycle(onActivated, onDeactivated, onStopped, onResumed, onDestroying)`**, all
  optional, subscribes to the component's window, using MAUI's `Window` events of the same names. The
  callbacks are refreshed every render, like `UseNavigationLifecycle`, and the subscriptions end at
  unmount.
- **A window that arrives late** (a `ReactorPage` in a non-Reactor app) is found when the page gets
  one; the resolver follows the page's `Window` property, so a page moved to another window, or
  removed from its window, stops following the old one. `UseWindowSize`, `UseDialogs`,
  `UseWindowLifecycle` and `UseSystemBackButton` share it.
- **`Destroying` runs before teardown.** `ReactorWindow` unmounts its tree in its `OnDestroying`
  override, which MAUI calls after the `Destroying` event's subscribers. So the hook's callbacks run
  while the tree and its error boundaries are still mounted.
- **Naming.** WinUI has no such hook today (only `UseWindow()` and the window's own events). The
  name `UseWindowLifecycle` is proposed for both renderers. When WinUI adopts it, it can map it to
  its window's `Activated` and `Closed` events and to visibility.
- **Errors** in a callback go to the component's error boundary. An error in `onDestroying` is
  different: the tree is about to be unmounted, and boundary handling is posted, so it would run
  after teardown. Such an error is held:
  - every `onDestroying` callback still runs;
  - the tree is unmounted in `OnDestroying`;
  - the held errors are then reported to the root's `UnhandledException`, or rethrown without a
    subscriber.

  A throw therefore never stops MAUI's `Destroying` before the teardown has run.

### 5.5 Gate for 4b-3

- **Headless tests:**
  - **The dialog queue:** order, one at a time, per window, and the dismissal barrier. Also
    presentation failure, window destruction cancelling queued tasks, and the presenter resolved at
    dequeue (top modal, a late window).
  - **Declarative dialogs:** stale answers (unmount, `IsOpen` off, re-open) and errors from
    `OnClosed`.
  - **Back handlers:** only the visible page's are eligible (back stack and deselected tabs
    excluded), depth and recency order, each run once per back, and fall-through. The handle
    overload consumes a refused back, with the guard running once.
  - **Lifecycle:** subscriptions, unsubscription, a late window, and `Destroying` running before
    teardown.
  - **`ModalPresentationStyle`** written before the modal is presented.
- **Device fixtures on Mac Catalyst, iOS and Android:**
  - an alert (one and two buttons), an action sheet and a prompt, answered through their native
    buttons, asserting that the native dialog disappears. On Android the button is clicked in
    process; on iOS the gate drives the button through Appium if it can't be reached in process.
  - two queued dialogs never shown at once, and a dialog requested during a navigation;
  - the Android back key reaching a `UseSystemBackButton` handler before a `NavigationHost`, at the
    root, and with a refusing guard;
  - window `Stopped` and `Resumed` when the app is backgrounded, where the test can background it;
  - a page sheet modal on iOS.
- **WinUI tiers** green, and `/perf` within noise.

### 5.6 As built (4b-3)

- **The declarative dialogs are records over a built-in component.** `AlertElement`, `ActionSheetElement`
  and `PromptElement` derive from `DialogElement : ComponentElement`, so the engine needs no new node
  kind: each renders `DialogElementComponent`, which renders nothing and keeps its open on the
  window's queue. Their properties write through to the component's props, so they are set with
  `with { IsOpen = …, OnClosed = …, InitialValue = … }`. The factories are
  `Alert(title, message, close)`, `Alert(title, message, accept, cancel)`,
  `ActionSheet(title, cancel, destruction, buttons…)` and `Prompt(title, message)`. An alert's
  `OnClosed` takes a `bool` for both shapes (a one-button alert reports `false`); the sheet's and the
  prompt's take a `string?`.
- **The Android barrier is a posted turn.** MAUI keeps no reference to the `AlertDialog` it shows,
  and Android has no public "a dialog is on screen" query; but AppCompat's dialog runs a button's
  callback from one posted message and posts its dismissal right after (a list row's click dismisses
  synchronously after its callback). The queue awaits MAUI's task with `ConfigureAwait(false)`, so
  the answer arrives inside that callback, and shows the next dialog from a turn it posts then: after
  the dismissal. Nothing waits on the window focus, so an earlier dialog never delays a later one (a
  MAUI modal, itself a focused dialog window on Android, shows the next dialog promptly). On iOS and
  Mac Catalyst the barrier is as designed (no `UIAlertController` in the window's presentation
  chain), checked before every dialog and then once per frame (`DispatchDelayed(16 ms)`).
- **A navigation transition is one of Reactor's navigators running**: the window page's navigation
  stack, those in its tabs or flyout, a Shell section's, or one in a modal page, as
  `WindowPageSetter` finds them. The queue waits for that navigator's `BecameIdle` (posted), then
  checks again. A push or pop the app makes through MAUI directly isn't seen.
- **Imperative requests.** Arguments are validated when the method is called (an empty `cancel`
  throws `ArgumentException`). A request made before a hosted page's window is known waits for it
  (and while the page is out of any window); if the component unmounts first, its task completes with
  the cancel answer. A request already queued on a window outlives its component, as MAUI's
  `DisplayAlertAsync` outlives its page. A `ReactorWindow` creates its queue when it is constructed,
  and `UseDialogs` when it first knows the window, so the queue sees `Destroying` before the tree's
  own handlers do.
- **The back key is an AndroidX callback per window**, not a page override (review M3): every page
  overriding `OnBackButtonPressed` made MAUI keep its back callback enabled for every Reactor root
  (`HasBackButtonPressedOverride` is a per-type reflection check), losing Android's predictive
  back-to-home preview even without a handler. Handlers join the window their tree is shown in,
  through the late-window resolver, and the window keeps them in an attached property (no static
  table). The callback is added on the window's activity after MAUI's, and again (posted, after
  MAUI's `OnCreate` has added its own) when the activity is recreated. When no handler handles the
  back, it disables itself and calls `OnBackPressedDispatcher.OnBackPressed()` again, so MAUI's
  callback (or the platform's default) gets the key, then re-enables.
- **Modals and Shell on Android (verification round).** MAUI shows a modal in a `DialogFragment`
  whose `ComponentDialog` has its own back dispatcher, with MAUI's callback forwarding to the window:
  a real back key while a modal was shown never reached the activity's dispatcher, so it skipped
  every handler and MAUI popped the modal. Reactor now finds each modal's dialog among the activity's
  fragments (the dialog whose window holds the modal page's view, looked for again on `ModalPushed`
  until it exists) and adds its callback to that dispatcher, after MAUI's, enabled with the window's.
  MAUI's Shell adds a back callback when an item's view is created; `ReactorShell.OnBackButtonPressed`
  asks the handlers (on Android only: iOS calls it for the navigation-bar back), in case that callback
  is asked first. A back asks the handlers at most once, and every back asks them: the mark is scoped
  to one back's dispatch, not to a turn of the UI thread. While Reactor's callback passes a back on to
  MAUI, the Shell doesn't ask; when the Shell's path asked and passed the back on (it didn't handle
  it), Reactor's callback, which that same back reaches next, takes the mark instead of asking (a
  posted turn clears a mark nothing took). On the emulator Reactor's callback was asked before the
  Shell's in every order tried, so the Shell path is covered headlessly.
- **A handler belongs to the page it renders, else the nearest page above it.** A route's component
  usually returns `Page(…)`, so the page is below the component: the walk first follows the
  component's rendered chain (components, providers and boundaries with one child) down to a host,
  and takes it if it is a page; a component that renders a `NavigationHost` belongs to its navigation
  page. A component that renders the tree's root page is above every page, so it is always asked,
  even while a modal is shown (an app's `UseSystemBackButton(nav)` at its root). Otherwise the nearest
  page above counts, and so do Shell items, tabs and contents, so a component inside a deselected
  `ShellTab` is never asked. A component in a list cell (a child root) continues at the list's node.
  A `ReactorPage`'s top-level components belong to that page, so a hosted page in a MAUI back stack
  isn't asked. The visible chain is built from the top modal, else the window's page: a navigation
  page's `CurrentPage`, the selected tab, a flyout's detail (and its flyout while presented), a
  Shell's current item, section, content and `CurrentPage`.
- **A throwing back handler consumes the back**, like a throwing navigation guard, and its error goes
  to the component's error boundary.
- **`ModalPresentationStyle` is not applied on Mac Catalyst.** There MAUI's dismissal of a modal
  presented as a sheet never completes (Mac Catalyst 26.7: `DismissViewControllerAsync` doesn't
  return, for the app's own pop and for the cleanup at unmount alike), which blocks every later modal
  of the window. So the property is iOS-only, as MAUI documents the platform-specific, and Mac
  Catalyst keeps full-screen modals. It is written at mount and updated live; an update applies to
  the next presentation.
- **`onDestroying` in a hosted `ReactorPage`.** Only a `ReactorWindow` holds the errors until its tree
  is unmounted (they bypass the boundaries). A `ReactorPage` is not torn down by its window's
  `Destroying`, so the error goes to the component's boundary like any other callback's.
- **Engine.** Hooks find their node through the root's render owner (`RootRenderOwner.Rendering`, set
  while a component renders). `ReactorRoot.ReportUnhandled` reports without unmounting, `TopRoot`
  reaches a list cell's owning tree, `RootNative` is the tree's root control and `HostPage` the
  `ReactorPage` hosting it. No change in `Reactor.Core` or the WinUI renderer.
- **Tests.** Headless: `DialogTests`, `SystemBackTests` and `WindowLifecycleTests`, driving the queue
  through a fake platform (presentation, dismissal, the posted turn, navigation and frames), the back
  key through `SystemBack.HandleBackKey` with MAUI's `IWindow.BackButtonClicked` as the pass-on, and
  the window's lifecycle through `IWindow`. `SystemBackTests` builds a Shell, so it shares
  `ShellTests`' collection: MAUI's Shell routes are process-wide, and building Shells on two threads
  at once failed a Shell test now and then. Device: the dialogs are answered through their native
  buttons in process on every platform: on Android the alert's window is found with
  `WindowInspector` (a window with an alert's buttons; a MAUI modal is a dialog window too) and its
  button clicked; on iOS and Mac Catalyst the alert is dismissed and the chosen `UIAlertAction`'s own
  handler block runs (UIKit exposes no public tap), so no Appium run was needed. UIKit occasionally
  ignores that in-process dismissal when it lands as another transition finishes (2 of about 15 iOS
  runs), which a tap never meets, so the helper dismisses once more if the alert is still on screen,
  not dismissing, after 2 s. A recording platform
  checks that no native dialog is on screen when the next is presented (on Android no frame could
  show two at once even without the barrier). The back key on a modal and in a Shell is a real key
  event: an app can't inject one into itself (`INJECT_EVENTS`), so the fixture logs
  `REACTOR-KEY-BACK` and `run.sh` sends `adb shell input keyevent 4`. The other Android back fixtures put a stand-in
  `OnBackPressedCallback` below MAUI's and Reactor's for the platform's default, so the app stays in
  front. `Stopped` and `Resumed` are produced on Android by a second activity of the test app
  covering the main one. The fixtures that can't run on a platform (the back key and backgrounding on
  iOS and Mac Catalyst) report TAP's `# SKIP`. Each key oracle was mutation-checked (the code broken,
  the test seen to fail).
