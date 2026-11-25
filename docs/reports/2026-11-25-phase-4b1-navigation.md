# Phase 4b-1 gate: typed navigation shared by WinUI and MAUI (spec 069)

MAUI gets WinUI's typed navigation API: `UseNavigation<TRoute>`, `NavigationHandle<TRoute>`,
`UseNavigationLifecycle` and `NavigationHost`. The navigation model moves to the shared core. Each
renderer keeps a thin public handle, so WinUI's public API is unchanged. PR #13, stacked on #12.

## What changed

- **Spec 069**, the design for 4b. Before any code was written, it went through two review rounds by
  each of two model families (Claude and gpt-6.1-sol).
  - The first round rejected moving the handle itself to the core. Replacing `Slide` with a static
    extension member breaks `using static`, `nameof`, `cref`s and fully-qualified calls; the compiler
    spike confirmed it.
  - The design settled on a shared model with a handle per renderer.
- **The core** (`src/Reactor.Core/Navigation`, forwarded from `Reactor.dll`):
  - moved: `NavigationStack<TRoute>`, `NavigationState<TRoute>`, `DeepLinkMap<TRoute>` with
    `RouteArgs` and `DeepLinkResult<TRoute>`, `NavigationMode`, and the four lifecycle contexts;
  - new: an internal `NavigationLifecycleHooks` (the hook's state and lookup) and
    `NavigationStack.PopToIndex`;
  - changed: `UIThreadMarshal.MarshalOffUIThread` takes an optional queue.
- **WinUI:** no public change. `UseNavigationLifecycle` still lives in `WinUIHooks`, now over the
  shared hook state.
- **MAUI:**
  - `NavigationHandle<TRoute>`, with the same members as WinUI's. Its `NavigateOptions` has
    `Animated` and `PushToBackStack`. Off-thread calls go through the root's own scheduler.
  - `UseNavigation(initial)`, `UseNavigation()` and `UseNavigationLifecycle`.
  - `NavigationHost(nav, routeMap, stack)`:
    - The stack shows the back stack plus the current route.
    - An entry keeps its key while every entry below it is unchanged.
    - A destination is proposed and asked (`onNavigatingTo`) before it is shown.
    - A user's back becomes one guarded navigation to the depth reached.
    - The Android back key is consumed whenever the shown stack or the model can go back.
    - Completion waits for the platform operation.
    - Lifecycle callback errors go to the error boundaries.
  - `NavigationStackElement.Animated`, with the value actually passed to MAUI recorded per operation.
  - A `NavigationMode` alias in the package's implicit usings, because MAUI's own `NavigationMode`
    clashes.
- **AOT:** `tests/aot_trim_proof/Reactor.Navigation.AotSmoke`, a Native AOT console over the core.
  It resolves a deep link and round-trips a navigation snapshot through source-generated JSON. It
  runs in `maui.yml`.
- **Docs:** spec 069 (including §2.6 "as built"), spec 065, spec 068 (rows for typed navigation and
  the Android back button), the MAUI README and the CHANGELOG.

## Tests

| Tier | Before (4a head) | 4b-1 | Notes |
|---|--:|--:|---|
| WinUI unit (CI) | 16,346 | 16,346 | unchanged; the moved model's existing tests pass in their new assembly |
| MAUI renderer (headless) | 52 | 96 | handle, keys, guard, lifecycle order and supersession, back (user, key, several levels, model moved on), refused destinations, error routing, nested hosts, teardown and rebind, `Animated`, off-thread on two roots, allocation-free mutators, state, hot reload |
| MAUI engine (headless) | 63 | 73 | the shared model on the fake renderer: stack operations, guard order, `PopToIndex` bounds, the hook's order and lookup, the handle with an injected scheduler |
| MAUI device: Mac Catalyst, iOS 27, Android 37 | 42 | 54 | 54/54 on each. New: push, pop and replace reaching the platform stack; native back (`PopViewController`, the Android back dispatcher); the back key during a push; lifecycle order; unanimated operations and modals |
| AOT smoke (osx-arm64) | n/a | pass | no trim or AOT warnings; prints `PASS` |
| Release builds | 0 warnings | 0 warnings | `Reactor.Maui.slnx`; WinUI type-checks on macOS, and CI builds it |
| CI | green | green | CI 45677971642, MAUI 45677971408, Coverage 45678004339 |

**Perf** (`/perf`, interleaved against `main` on one runner, 12 paired runs):

- Renders per second, reconcile, diff, allocated bytes per render and Gen0 counts are within noise
  on all three workloads (StocksGrid, KeyedList, Flex).
- Working-set memory reads +0.5% (StocksGrid at `--percent 0`) and +0.9% (KeyedList), each with a
  CI excluding 0. Both are below the gate's 1% threshold.
- The baseline is `main`, so the comparison covers the whole stack. 4b-1 adds nothing to the render
  path.

**Mutation checks:** 37 in total.

- **Headless (30):** each new oracle failed with its code broken and passed once restored. They
  cover:
  - latest-only `onNavigatedTo`, and `onNavigatedFrom` for every departure;
  - proposals; the back-key paths (four of them); retrying a refused destination;
  - the boundary chosen after the component is replaced;
  - the host's animation default and the expiry of a navigation's own animation option;
  - the animation flag the navigator actually passes;
  - deferred completion; nested discovery; releasing the guard; stack configuration;
  - allocation-free mutators; the snapshot freeze; route-equality keys; the per-root scheduler.
- **Engine (3):** `PopToIndex` bounds, guard order, the injected queue.
- **Device (4):** without the back-key override, MAUI also pops; completing before the navigator is
  idle; a modal push forced to animate; a modal opened after an unanimated push.

## Review

`pr-review` ran 7 dimensions on Claude, plus a gpt-6.1-sol cross-check: a blind scan,
reconciliation, and two rounds checking the fixes.

- **High, all fixed and confirmed by the other family:**
  - After an accepted push, later changes to the pushed page were ignored.
  - Lifecycle callback exceptions skipped the error boundaries and the root.
  - A refused pop destination disabled later backs. It was raised as medium, and the cross-check
    raised it to high.
  - Found while checking the fixes:
    - the back key above the root was dropped while a push was pending;
    - a callback's error skipped a surviving boundary after its component was replaced.
- **Medium, fixed:**
  - the §2.5 gate items: the core harness tests and the AOT smoke (the cross-check rated this
    medium);
  - the `NavigationMode` clash;
  - the host's stack couldn't be configured (tab titles);
  - `Animated` only applied to `Navigate`, and a navigation's own option leaked into later modals;
  - weak test oracles (animation, back during a push, native back, several levels, nested hosts,
    teardown);
  - the Android back key decided from the model instead of what is shown.
- **Medium, documented instead:** the lifecycle policy is per renderer in 4b-1. MAUI's host and
  WinUI's handle discovery and completion differently, so each keeps its own, and the divergences
  are listed in §2.6. Moving the policy into the core and having WinUI call it is a recorded
  follow-up. The cross-check agreed it is medium.
- **Low, fixed:** duplicated marshal messages, `UseRouteStack`'s position in the docs, spec wording,
  and spec 068 rows.
- **Security and packaging:** clean. All 9 public types that moved are forwarded, the API index is
  unchanged, and the change is AOT-safe.

## Differences from WinUI, by design (spec 069 §2.6)

- `NavigateOptions` has `Animated` instead of WinUI's transitions.
- A throwing `onNavigatingFrom` or `onNavigatingTo` cancels the navigation and is reported to the
  page's boundary. On WinUI it propagates to whoever called `Navigate`; on MAUI a back gesture or
  the back key has no caller to throw to.
- `SetState` reports `Reset` and calls `onNavigatedFrom`. WinUI's host reports `Push` without it.
- Navigations that were overtaken complete together once the navigator is idle. WinUI completes
  each one at the moment it is overtaken.

## Decisions for the gate

1. **A shared model with a handle per renderer** (spec 069 §2.1, §3). Recommendation: accept.
2. **The lifecycle policy is per renderer for now**, and converging it is a follow-up.
   Recommendation: accept.
3. **The MAUI deviations listed above.** Recommendation: accept.
