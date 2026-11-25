# Phase 3e gate: descriptors, and removed views released (spec 067 §16)

This phase passes spec 067 §12.3's descriptor gate. MAUI gets a public `ControlDescriptor` with verbs
modeled on WinUI's. The device gate found an Android crash: a theme change hit the released native
views of a removed `SearchBar`. The engine now releases removed views. PR #11, stacked on #10.

## What changed

- **`ControlDescriptor<TElement, TView>`** (public, MAUI).
  - Entries: `Property` (a bindable property, cleared when unset), `OneWay`, `Initial`, `Controlled`,
    `Event`, plus `WithChildren`, `WithUnmount` and `WithCanUpdate`. `ControlDescriptor.Create<,>()`
    builds one.
  - Mount order: create the view, write the initial values, apply modifiers, then wire events.
  - `Controlled` takes WinUI's parameter order and names: `get`, `set`, `subscribe`, `callback`,
    `readBack`.
  - `Property` rejects a value of the wrong type when the entry is added. `TextProperties()` needs a
    text view.
- **Eight built-in Controls handlers are now descriptors:** BoxView, ActivityIndicator, ProgressBar,
  CheckBox, Switch, RadioButton, Editor and SearchBar. Controls with coercion or coupled values stay
  hand-written; WinUI covers that case with `CoercingOneWay`, which MAUI doesn't have yet.
- **Removed views are released**, for every handler kind.
  - The engine records the top-most native of each subtree it unmounts: child removal, root
    replacement, `Unmount`, `FailRoot`, a subtree an error boundary replaces, and a half-mounted node.
  - After the pass syncs that native's host, the engine hands it to the registry's release hook.
  - MAUI's hook disconnects the view's handlers together with its subtree. If the view is still
    loaded, it waits for the `Unloaded` event first.
  - Pages, and so the content of a popped page, are left to MAUI's own disconnect after the pop.
  - `HandlerDisconnectPolicy.Manual` opts a view out.
- **Docs:** spec 067 §16, spec 065, the README's custom-handler section, the CHANGELOG.
- **Device runner:** the filter accepts `a|b` and is quoted for the Android shell.

## The crash

| | |
|---|---|
| Symptom | SIGSEGV on Android, in `Drawable.SetTint`, reached from `SearchBarHandler.MapPlaceholderColor` ← `InputView.OnRequestedThemeChanged` ← `Application.set_UserAppTheme` |
| When | 2 of 4 full device runs before the fix, in the theme-hooks fixture that runs after the echo fixtures |
| Cause | Reactor removed views but left their MAUI handlers connected, so a removed input view stayed subscribed to theme changes after its native views were released |
| Reproduced in isolation | No (0 of 4 targeted stress runs). The stack trace and MAUI's source establish the mechanism. |
| Mechanism oracle | Device fixture: the handlers of a removed `SearchBar` and `Entry` become `null`, on both renderers. Fails against a mutant without the release. |
| After the fix | 7 full Android runs with no SIGSEGV or fatal exception (counts below) |

## Tests

| Tier | Before (3d head) | Phase 3e | Notes |
|---|--:|--:|---|
| MAUI renderer (headless) | 40 | 52 | descriptor API, entries, migrated controls' props and events, input after unmount, release |
| MAUI engine (headless) | 62 | 62 | |
| MAUI device, Mac Catalyst | 34 | 39 | removal, theme, a sibling stays, a page popped with animation |
| MAUI device, iOS 27 | 34 | 39 | |
| MAUI device, Android API 37 emulator | 34 | 39 | 2 clean full runs at the end; see Flakes |
| MAUI Release build | 0 warnings | 0 warnings | |
| CI: WinUI tiers; MAUI incl. device on Mac Catalyst and iOS | green | green (CI 45614130424, MAUI 45614130748) | re-ran 1 WinUI selftest flake (`ModifierTransform_Transitions`, transform-animation family; no WinUI code changed) and 1 iOS simulator crash before any test ran (`Mach error -308`) |

**Mutation checks:**

| Mutation | Fails |
|---|---|
| no echo check in `Controlled` | descriptor refused-input test, plus an existing test |
| `Controlled` compares with the old element | 2 tests |
| events wired before the initial writes | 3 tests |
| `OneWay` never updates; `Property` never clears | 3 tests |
| Editor `Completed` and `MaxLength` entries dropped | 2 tests |
| release before the host syncs | `RemovedControlTests` (both renderers) |
| child removal not recorded | `RemovedControlTests` |
| error-boundary removal not recorded | `ASubtreeAnErrorBoundaryReplaces_IsReleased…` |
| no release at all | the device theme fixture, both renderers |
| a removed page's content disconnected immediately | `Removal_APageTheAppPops_KeepsItsContentWhileItAnimatesOut` |

## Review

`pr-review` ran 7 dimensions on Claude, plus a GPT-family cross-check (gpt-6.1-sol: a blind scan,
then reconciliation).

- **High, all fixed:**
  - The first fix posted one disconnect per view to the dispatcher. That would have blanked a popped
    page's content while it animated out. Confirmed cross-family; the engine-level release replaces it.
  - The disconnect was checked only on the descriptor path, and the hand-written, direct and
    third-party handlers weren't covered. Confirmed; the engine-level release covers every handler,
    and new tests check it.
  - `OneWay` had no tests. The cross-check downgraded this to medium.
  - Raised by the cross-check during reconciliation: a subtree an error boundary replaces was never
    released. Fixed, with a regression test.
- **Medium, fixed:**
  - the README snippet didn't compile and never registered its descriptor;
  - several WinUI claims in §16 were wrong (KD-1b's scope, items binding before props, declarative
    coercion, the remount cell);
  - `Controlled`'s shape differed from WinUI's;
  - `TextProperties` threw on a non-text view;
  - migrated properties, events and `WithChildren` had no tests;
  - a deferred disconnect could throw on the dispatcher.
- **Low, fixed:**
  - a `'` in the Android test filter is now rejected;
  - the `MainActivity` comment;
  - CHANGELOG wording;
  - the `Property` type check;
  - the `Create` factory.
- **Security and packaging:** clean.

## Flakes and findings for later

- **MAUI Android navigation crash, left for phase 4.** `CurrentPage cannot be null`, in
  `StackNavigationManager` / `NavigationViewFragment.OnCreateView`, when the window's page is replaced
  while a NavigationPage pop is still animating. The new popped-page fixture reproduces it when it
  tears down straight after the pop; it now waits for the pop to finish, as the other navigation
  fixtures do. This is the plan's known "replace a window's page mid-navigation" item: an upstream
  issue plus a `ReactorWindow` workaround.
- **Android emulator, full-run results:**
  - 3 clean runs (36/36 once, 39/39 twice), with no SIGSEGV;
  - 1 run that died in the new popped-page fixture on the navigation crash above;
  - 2 runs where the theme fixture took ~29 s and hit the runner's 30 s per-fixture limit, once
    with the known `NavigationStack_ModalIsShown` timeout under load. The fixture now runs 4 rounds
    instead of 12 and takes ~7 s.
  - 1 run where `Theme_…` [direct] timed out waiting for its handlers right after the [controls]
    run's 30 s timeout.

## Decisions for the gate

1. **Descriptors converge on the authoring surface, not on shared code.** Recommendation: accept.
2. **The engine releases removed natives through a registry hook.** MAUI disconnects them and leaves
   pages to MAUI. Recommendation: accept.
3. **The MAUI Android navigation crash moves to phase 4.** Recommendation: accept.
