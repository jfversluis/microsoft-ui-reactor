# Phase 4a gate: MAUI feature inventory (spec 068), and the Android window-swap crash

Phase 4 starts with an inventory of every .NET MAUI 11 control and feature against Reactor's MAUI
renderer. It sets the bar each item must meet, and the order the remaining work lands in. The review
of the new window-swap fixture also turned it into a reliable reproduction of the open Android crash
from phase 3e, so 4a ships the workaround as well. PR #12, stacked on #11.

## What changed

- **Spec 068** (new), the phase 4 inventory:
  - The bar for "implemented" (§1). It now includes using WinUI's name and shape when WinUI already
    has the feature.
  - A status for every MAUI 11 control, page, layout and mechanism (§2). There is a new 🧪 status
    for code without tests; the test status comes from a census of the three MAUI test projects.
  - Proposed exclusions, each with the Reactor way to do the same thing (⛔):
    - Frame, ListView/TableView and the cells, BlazorWebView, the legacy StackLayout;
    - VisualStateManager, triggers, ControlTemplate, data binding, `DataTemplateSelector` /
      `BindableLayout`, effects, `ResourceDictionary`, Essentials.
  - The sub-phase order 4b–4g, driven by what the seven ports need (§4). Shell and page dialogs
    come first because six of the seven ports use Shell as their root.
- **Android window-swap workaround.** `WindowPageSetter`, used by `ReactorWindow`, waits before
  replacing `Window.Page` on Android while any navigation stack in the outgoing page is still running
  a platform push or pop. A deferred failure is reported through the root, and a waiting swap is
  dropped when the window is destroyed. `StackNavigator` raises `BecameIdle`.
- **Device fixtures (3 new).** The window's content is replaced through the same root, as
  `ReactorWindow` does:
  - during pops and pushes, both as the navigator starts and once MAUI's stack has changed;
  - repeatedly, which is the regression test;
  - with the stack inside `Tabs`.
- **Device runner:** teardown goes through the fixture's page setter, so a swap left waiting can't
  land on the next fixture.
- **Docs:** spec 065 §6 and §7, the CHANGELOG.

## The crash

| | |
|---|---|
| Symptom | `InvalidOperationException: NavigationManager cannot be null here`, in `NavigationViewFragment.OnCreateView`, on Android |
| When | A `ReactorWindow` whose root switches away from a `NavigationStack` while a push is running. Plain MAUI reproduced the same crash before (spec 065 §7). |
| Reproduction | `Window_ReplacingThePageDuringRepeatedPushesAndPops_DoesNotCrash` swaps once MAUI's stack has changed, in alternating pops and pushes. It crashed in round 7 (the fourth push) in **5 of 10 runs** with the wait turned off. A single push, or swaps swept across one push animation (14 delays, 0–208 ms), never crashed: the crash needs the earlier interrupted navigations. |
| After the fix | **0 of 18 runs** crashed (the three window fixtures, 10 runs, plus 8 earlier runs of the first two) |
| Why the earlier attempt failed | In phase 3e the fixture swapped through a *new* root, which skipped the reconciler's unmount of the stack. It also swapped at one fixed moment. It never reproduced the crash, so a deferral couldn't be shown to work and wasn't shipped. |
| Not covered | A platform back gesture in flight at the moment of the swap (the navigator isn't running then). The phase 3e "CurrentPage cannot be null" variant hasn't reproduced since. |
| The `Tabs` case | Didn't crash with the wait turned off (0 of 6). The setter waits for nested stacks anyway, because both review families asked for it and it costs nothing. It is documented as defensive. |

## Tests

| Tier | Before (3e head) | Phase 4a | Notes |
|---|--:|--:|---|
| MAUI renderer (headless) | 52 | 52 | |
| MAUI engine (headless) | 62 | 63 | a host error unmounts the tree before it is reported |
| MAUI device, Mac Catalyst | 39 | 42 | the three window fixtures |
| MAUI device, iOS 27 simulator | 39 | 42 | |
| MAUI device, Android API 37 emulator | 39 | 42 | 2 clean full runs, plus 10 runs of the window fixtures |
| MAUI Release build | 0 warnings | 0 warnings | |

**Mutation checks:**

| Mutation | Fails |
|---|---|
| the wait turned off | Android crashes in 5 of 10 runs; the "swap waits on Android" check is skipped, so the crash comparison is the evidence |
| the swap made through a new root (the original fixture) | not a mutation of product code: it never unmounted the stack (review finding, fixed) |
| fewer than 4 of 6 pushes still running at the swap | the regression fixture fails instead of passing without testing anything |
| `ReportHostError` reports without unmounting | `AHostError_UnmountsTheTreeBeforeItIsReported` |

## Review

`pr-review` ran 7 dimensions on Claude, plus a GPT-family cross-check (gpt-6.1-sol): a blind scan,
reconciliation, then a review of the new product code and a check of the fixes.

- **High, all fixed:**
  - The window fixture swapped through a new root and never unmounted the stack, so it didn't test
    `ReactorWindow`'s path. Confirmed cross-family. It now swaps through the same root.
  - Rows were marked ✅ without the tests §1 requires. Confirmed. There is now a 🧪 status, the rows
    are reclassified, and the gate counts 🧪.
  - Page dialogs and Toast/Snackbar were missing, although 5 of 7 ports use them. Confirmed. Rows
    added, along with triggers, behaviors, lifecycle, flow direction and the rest.
  - Shell was ordered after features no port uses. The cross-check downgraded this to medium, as an
    ordering issue. Reordered.
  - Nested stacks (`Tabs`, `Flyout`) bypassed the wait. Raised by both families. Fixed, with a
    fixture.
  - Raised by the cross-check while verifying the fixes: a deferred swap's failure was reported
    without unmounting the tree, unlike every other unhandled error. It now goes through the root's
    failure path, with a mutation-checked test.
- **Medium, fixed:**
  - WinUI names for the planned APIs: `.Animate` / `.Transition` / `WithAnimation`, `ThemeRef`,
    `UseElementRef` / `.Ref`, typed routes, the interop names, `UseService` vs `UseContext`;
  - CollectionView and CarouselView features listed as missing actually exist untested;
  - hot reload downgraded to 🟡;
  - gaps in SwipeView, RadioButton, WebView and Entry;
  - VisualStateManager states mapped to their replacements;
  - validation libraries chosen from what the ports use;
  - the crash-variant claims;
  - deferred-swap errors are reported, and a waiting swap is cancelled on destroy;
  - runner teardown goes through the setter;
  - the swap-timing oracle.
- **Medium, documented instead:** a user-initiated back pop followed by a root switch. Treating
  platform pops as busy could wait forever, because nothing re-checks them while the navigator is
  idle. It is recorded in spec 065 §7 and 068 §3.
- **Low, fixed:** the CI wording in spec 065, the BlazorWebView acceptance case, and the 4d names.
- **Security and packaging:** clean.

## Decisions for the gate

1. **The inventory and its exclusions (spec 068 §2).** Recommendation: accept the ⛔ rows.
2. **The order 4b–4g (spec 068 §4)**, driven by the ports, with Shell first. Recommendation: accept.
3. **The Android window-swap workaround.** It is an internal deferral, Android only, proven 0/18 vs
   5/10. Recommendation: accept, and file the plain-MAUI repro in dotnet/maui.
