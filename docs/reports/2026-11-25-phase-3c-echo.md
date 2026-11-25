# Phase 3c gate: echo (spec 067 §14)

Spec 067 §12.3 requires echo to be characterized before any echo code is shared between the renderers.
Phase 3c does that on device for MAUI. The result is that echo code stays per renderer, and
spec 067 §14 writes down the contract both renderers follow. The characterization also found a bug:
input a control raised after its element was unmounted still reached the unmounted component. This
phase fixes it. PR #9, stacked on #8.

## What changed

- **Echo characterization** (`tests/Reactor.Maui.DeviceTests/EchoFixtures.cs`). It covers the
  eleven controlled form controls on both renderers, on Mac Catalyst, iOS and Android, and checks
  five cases:
  - programmatic writes;
  - batched and no-op renders;
  - coerced writes;
  - refused input;
  - input after unmount.

  Every change event fires synchronously inside the write, so MAUI's write scope (`EchoGuard`) is
  exact for these controls. WinUI's value-diff arm and counter exist for late and multiple echoes,
  which none of these controls produce.
- **No callbacks after unmount.**
  - `HandlerContext.Live` is the current element, or `null` once it is unmounted. `IsMounted` is
    new, and `IsEchoing` is also true after unmount.
  - Every built-in event site invokes callbacks through `Live`, about 40 sites across Controls,
    direct views, pages, items and gestures.
  - Before the fix, typing, toggling and sliding after unmount reached the component on every
    platform and both renderers.
- **List cells after unmount.** A `CollectionView`/`CarouselView` that has been unmounted no longer
  starts Reactor trees for cells the platform binds later, for example while a page animates out.
- **Page lifecycle contract.** A page the app removes doesn't report `OnDisappearing`, because its
  component may already have run its cleanups. A covered page still does. This is documented on
  `PageElement.OnDisappearing`.
- **Docs.** Spec 067 §14 covers what was characterized, what wasn't (`CarouselView.Position`,
  `CollectionView` selection, `RefreshView`, tabs, flyout), and two §12.3 cases still without a
  fixture. Spec 065 and the CHANGELOG are updated.
- **WinUI:** unchanged.

## Tests

| Tier | Before (3b head) | Phase 3c | Notes |
|---|--:|--:|---|
| MAUI engine (headless) | 62 | 62 | |
| MAUI renderer (headless) | 34 | 39 | after-unmount input on both renderers (change, completed, click, drag), toolbar and swipe actions, a third-party handler, a list cell bound after unmount |
| MAUI device, Mac Catalyst | 25 | 34 | 8 echo fixtures, 1 page-lifecycle fixture |
| MAUI device, iPhone 18 Pro simulator (iOS 27) | 25 | 34 | |
| MAUI device, Android API 37 emulator | 25 | 34 | see Flakes |
| MAUI Release build | 0 warnings | 0 warnings | |
| WinUI CI | green | CI_STATUS | no WinUI code changed |

**Reproduced first.** On the 3b head, the after-unmount fixture logged
`entry after, switch True, slider 0.7` on all three platforms and both renderers.

**Mutation checks:**

| Mutation | Fails |
|---|---|
| `Live` returns `Current` | `InputAfterTheControlIsUnmounted_IsNotReported` (both renderers), `ToolbarAndSwipeActions_AreNotInvokedAfterUnmount` |
| `IsEchoing` without the unmount clause | `AHandlerThatChecksIsEchoing_DoesNotReportInputAfterItsControlIsUnmounted` |
| cells render after the list is stopped | `ACellBoundAfterTheListIsUnmounted_StartsNoTree` |
| page `Disappearing` through `Current` | `Page_Disappearing_IsReportedWhileDeclared_NotAfterTheAppRemovesIt` (Mac Catalyst: got `disappearing`) |

Each "nothing reported" check has a positive control in the same fixture: the same input, made
while the element is mounted, is reported.

## Performance

The change touches event dispatch (one flag read per event) and the items host (one flag per cell
bind). It doesn't touch the render, reconcile or layout paths the benchmark measures, so the MAUI
benchmark was not re-run. WinUI is unchanged.

## Review

`pr-review` ran seven dimensions on the default model (Claude) plus a GPT-family cross-check
(gpt-6.1-sol: blind scan, then reconciliation).

- **1 high, downgraded to medium by the cross-check and fixed.** Routing `Disappearing` through
  `Live` dropped it for pages the app removes. The cross-check judged delivering it through
  `Current` unsafe, because the page's component may already have cleaned up. The contract was
  therefore documented and pinned instead.
- **Medium, fixed:**
  - list cells bound after unmount started live trees;
  - `Current`'s doc still recommended it for events;
  - §14 and spec 065 claimed "every MAUI control" when eleven were characterized;
  - the §12.3 coverage gaps weren't stated;
  - the after-unmount fixture discarded callbacks raised during teardown and had no positive control;
  - `Live`-only events had no tests.
- **Low:**
  - fixed: the batch row overstated what was measured; the platform-level restore was checked for
    two controls only, now stated; the device-tier platforms.
  - recorded as a convergence item: `IsEchoing` now also carries a lifetime meaning; a single
    dispatch helper.
- **Security and packaging:** clean.

## Flakes

- **CI (Mac Catalyst and iOS), first version of the page-lifecycle fixture.** Its positive control
  pushed a modal through MAUI directly, outside Reactor, and the app's later pop then didn't happen
  within 5 s on either runner. It now covers the page with a declared page instead and waits up to
  20 s, as the other navigation fixtures on CI need. It passes locally on all three platforms and
  still fails against the `Current` mutant.
- **Android emulator.** Under host load (load average 17–20) three navigation fixtures timed out
  once and passed on re-run, 10/10. The full 34-fixture run also exceeded the runner's 600 s limit
  once. The remaining three fixtures passed when run separately. CI runs only Mac Catalyst and iOS.

## Decisions for the gate

1. **Echo stays per renderer.** The shared part is the contract (spec 067 §14). The value-diff
   decision is extracted only if a MAUI control turns out to echo late. Recommendation: accept.
2. **`HandlerContext.Live` is the way handlers invoke callbacks**, and `Current` stays for
   update-time reads. Recommendation: accept.
3. **A page the app removes doesn't report `OnDisappearing`.** Use an effect's cleanup instead.
   Recommendation: accept.
4. **The uncharacterized controls and the two remaining §12.3 cases move to phase 4's control
   inventory.** Recommendation: accept.
