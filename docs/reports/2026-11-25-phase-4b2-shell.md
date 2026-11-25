# Phase 4b-2 gate: declarative Shell (spec 069 §4)

Reactor builds MAUI's Shell from elements. The selected tab is app state, and each tab's stack is a
`NavigationHost` over a typed handle. A user's back is guarded before anything moves. Six of the
seven port targets use Shell as their root (spec 068 §2). PR #14, stacked on #13.

## What changed

- **Design (spec 069 §4).** It was reviewed in four rounds by both model families before any code was
  written, and was built on a source study of MAUI's Shell (`src/Controls/src/Core/Shell`). Those
  rounds settled:
  - the root sentinel;
  - the back preflight in `Navigating`, committed when the back completes;
  - a single-use approval;
  - settlement at the end of the platform transition, never at a timeout;
  - `PopToRoot` classification;
  - selection keys;
  - the no-selection state.
- **Elements:**
  - `Shell(selected, onSelectedChanged, items…)`;
  - `TabBar`, `FlyoutItem`, `ShellTab` and `ShellContent` (top tabs), and `ShellMenuItem`;
  - optional icons, and nullable tabs;
  - keys that are unique across the Shell;
  - flyout behaviour, state, header, footer and content;
  - appearance colors;
  - page properties: `NavBarIsVisible`, `TabBarIsVisible`, `TitleView`, `SearchHandler`,
    `PresentationMode` and `ShellBackButton`.
- **A section adapter (`ShellSectionNavigator`).**
  - The root is set as the content's page. Pages above it are pushed and popped one at a time,
    each awaited, and the adapter never inserts or removes pages below the top.
  - It tags its own operations and holds page changes while its own modal is shown.
  - It waits for a host it replaced, through any chain of replacements.
  - An operation another `Navigating` handler cancels is not retried.
  - It stays busy through a window swap until it settles.
- **A user's back.**
  - **iOS and Mac Catalyst:** guarded in `Navigating`, and committed on `Navigated` or the transition
    coordinator. A cancelled swipe leaves the model alone, and app navigations are held during the
    gesture.
  - **Android:** the Shell's back-button override runs the guards, then performs the pop as its own
    and commits it when the pop completes. Presses queue, one level each.
- **Shared changes.**
  - The `NavigationHost` binding talks to an internal `INativeStack`, implemented by the
    `NavigationPage` navigator and by the section adapter.
  - A shared `OwnedModals` helper.
  - `WindowPageSetter` waits on a busy Shell.
  - Core, internal only: `NavigationStack.CanPopToIndex` and an approved `PopToIndex` that skips the
    guards for an approval already given.
- **Docs:** spec 069 §4 and §4.7 ("as built"), spec 065, spec 068 (the Shell row), the README's
  Shell section (its snippet is mirrored by a test), and the CHANGELOG.

## Tests

| Tier | Before (4b-1 head) | 4b-2 | Notes |
|---|--:|--:|---|
| MAUI renderer (headless) | 96 | 154 | 58 Shell tests. MAUI's events are raised in the order its renderers raise them |
| MAUI engine (headless) | 73 | 73 | |
| MAUI device: Mac Catalyst, iOS 27, Android 37 | 54 | 71 | 71/71 on each. The 17 Shell fixtures drive MAUI's real input paths and assert the native page and depth shown |
| Release build | 0 warnings | 0 warnings | |

- **Device input paths:**
  - tab taps go through `ProposeSection`;
  - the iOS back goes through `ShellSectionRenderer.ShouldPopItem`;
  - the Android back goes through `OnBackPressedDispatcher`.
- **CI:** green on `a47544ea`. CI 45715952326, MAUI 45715952317, Coverage 45715952174, Package,
  Build metrics, Bootstrap and Node projects.
- **Perf** (`/perf` 45716186567, interleaved against `main`):
  - **Headline StocksGrid:** within noise on every metric. Allocated bytes per render −0.5%.
  - **Low-mutation leg:** renders per second read −3.7% (CI excluding 0), while reconcile and diff
    were 4% *faster* in the same run. The two contradict each other, which points to runner noise,
    and the reading is below the gate's 5% threshold.
  - **KeyedList allocated bytes:** +0.8%, below the 1% threshold.
  - **Why noise is likely:** 4b-2 changes nothing on WinUI's render path. Its only core change is an
    internal `NavigationStack` overload.
- **Mutation checks:** 50 or more across the build and four review rounds, each killed. The ones
  that survived at first were strengthened until they failed.
- **The interactive iOS swipe (Appium).**
  - **Completed swipes:** these passed on the simulator, including with an app navigation held during
    the gesture.
  - **Released-early swipes:** synthesized edge drags always completed, so a cancelled swipe couldn't
    be produced. The cancel path is covered headlessly, in the order the iOS renderer raises its
    events.
  - **Next step:** phase 5's Appium harness, which has to cover this anyway.

## Review

`pr-review` ran 7 dimensions on Claude, plus a gpt-6.1-sol cross-check: a blind scan, then
reconciliation, then four rounds of checking the fixes.

- **Security and packaging:** clean.
- **High, all fixed and confirmed by the other family:**
  - **A root swap missed busy sections.** It hid a busy section's navigators from the window-swap
    check (cross-check and correctness).
  - **Modal route pages crashed** (rejected now, with a clear error).
  - **A 3-second fallback released a pending back on Android.** It broke deferrals.
  - **Replacing a deep host with a page** left stale pages on screen.
  - **A page change under a modal** dismissed it as if the user had.
  - **The Android back's model commit came too early.** It made a pop that another handler cancelled
    retry forever.
  - **A chained host replacement** skipped a pending operation.
  - **A key-back pop that threw** left the section busy for good.
- **Medium, fixed:**
  - a replaced host popping alongside its replacement;
  - two back presses running the guards twice;
  - three presses landing only two levels back;
  - a refusal blocking a newer declaration;
  - the shared modal helper;
  - the Android back moving to the Shell's override;
  - documenting the host's `stack` options that don't apply in a Shell tab;
  - optional icons; nullable tabs;
  - the "back with a result" idiom;
  - eight test gaps that mutations had survived.
- **Low, fixed:**
  - the renames `onSelectedChanged` and `ShellMenuItem`;
  - origin tags cleared on the first matching event;
  - the `ModalPopped` handler kept in a field;
  - spec wording.
- **Found while testing a fix:** mounting a Shell over a handle that already has history crashed.
  Fixed, with a headless test and a device fixture.

## Not in 4b-2 (spec 069 §4.5, §4.7)

- Top tabs with their own stacks. No port uses them.
- A declarative search element. `SearchHandler` is passed through.
- Modal route pages inside a Shell tab: use the host's `Modals`.
- Windows: validated in 4g.

## Decisions for the gate

1. **The Shell design and its deviations recorded in §4.7.** Recommendation: accept.
2. **Rejecting modal route pages in a Shell tab**, in favour of `Modals`. Recommendation: accept.
3. **The cancelled iOS swipe is covered headlessly only until phase 5.** Recommendation: accept.
