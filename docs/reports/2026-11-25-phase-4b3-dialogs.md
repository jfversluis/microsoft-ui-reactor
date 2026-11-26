# Phase 4b-3 gate: page dialogs, the system back button, presentation style, window lifecycle (spec 069 §5)

This phase completes app navigation on MAUI: alerts, action sheets and prompts (five of the seven
ports use them), a back-key hook, iOS sheet presentation for modals, and window lifecycle hooks.
PR #15, stacked on #14.

## What changed

- **Design (spec 069 §5).** Reviewed in three rounds by both model families before code. Those
  rounds corrected several points:
  - MAUI's window back entry point can't be overridden, so the hook intercepts at the page;
  - the system-key-only scope (iOS passes back straight on);
  - the dialog dismissal barrier;
  - terminal outcomes;
  - `Destroying` ordering.
- **Dialogs.**
  - `UseDialogs()` returns awaitable `Alert` (one and two buttons), `ActionSheet` and `Prompt`.
    The tasks never fault: on window destruction or a presentation failure they complete with the
    cancel answer, and a failure is logged and reported to the caller's boundary.
  - Declarative `Alert`, `ActionSheet` and `Prompt` elements, with `IsOpen` and `OnClosed`. The
    answer goes to the `OnClosed` of the content shown, so a dialog asking "Delete A?" can't run
    `Delete(B)`.
  - One queue per window:
    - the presenter is resolved when a dialog is dequeued (the top modal, or the window's page);
    - a dismissal barrier: on iOS, the presented alert is gone; on Android, a turn posted after the
      answer;
    - dialogs wait for a running navigation (`BecameIdle`);
    - a destroyed window keeps a tombstone until it is created again;
    - a refused dispatch ends the queue cleanly;
    - a dialog terminated during presentation is settled exactly once.
- **`UseSystemBackButton(Func<bool>)` and `UseSystemBackButton(nav)`**, Android only (Windows in
  4g):
  - **The callback.** One AndroidX `OnBackPressedCallback` per window, plus one on each MAUI modal's
    own dialog. It is enabled only while a handler is registered, so apps without the hook keep
    Android's predictive-back preview.
  - **Eligibility.** Only the visible page's handlers are asked (and those above every page): the
    deepest first, then the most recent, each asked once per back, including across Reactor's and
    Shell's entry points.
  - **Fall-through.** An unhandled back goes on to `NavigationHost`, then Shell, then MAUI.
  - **The `nav` overload** consumes a refused back, as WinUI's does.
- **`PageElement.ModalPresentationStyle`** is applied on iOS before a modal is presented. It isn't
  applied on Mac Catalyst, where MAUI can't dismiss a sheet.
- **`UseWindowLifecycle(onActivated, onDeactivated, onStopped, onResumed, onDestroying)`.**
  - It finds a window that arrives late, and follows the page's `Window` property, including when
    the page detaches.
  - `ReactorWindow` now tears down in `OnDestroying`, after the `Destroying` subscribers.
  - `onDestroying` errors are held and reported after teardown.
- **Also fixed:** a pre-existing flaky Shell test. MAUI's process-wide Shell routing was used by two
  test classes at once; they now share a collection.
- **Docs:** spec 069 §5 and §5.6 ("as built"), spec 065 (including the open issues for §7 on Mac
  Catalyst sheets), spec 068 rows, the README (dialogs, back key and lifecycle sections, with
  snippets that tests mirror), and the CHANGELOG.

## Tests

| Tier | Before (4b-2 head) | 4b-3 | Notes |
|---|--:|--:|---|
| MAUI renderer (headless) | 154 | 213 | dialogs (queue, barrier, outcomes, staleness, threading), back (eligibility, order, once per back, modal and Shell paths, list cells, hosted pages), lifecycle |
| MAUI engine (headless) | 73 | 73 | |
| MAUI device, Android 37 | 71 | 89 + 5 skipped | full run at the final head; the skips are iOS-only dialog fixtures. The back key is sent as a real key event through `adb shell input keyevent 4` |
| MAUI device, Mac Catalyst | 71 | 86 + 7 skipped | full run at the final head; the skips are the Android-only back-key and backgrounding fixtures, reported as TAP `# SKIP` |
| MAUI device, iOS 27 | 71 | 86 + 7 skipped | as above; full run at the final head, after three earlier full runs in a row were green following the test-harness fix (below) |
| Release build | 0 warnings | 0 warnings | |

- **Native buttons:** dialogs are answered through their native buttons in process on every
  platform. A device oracle checks that no native dialog is on screen when the next one is
  presented.
- **Mutation checks:** more than 40 across the build and three review rounds, each killed.
- **Known gap:** the order where Shell's own back callback runs before Reactor's is covered
  headlessly only. On the emulator, Reactor's callback always came first.

## CI

At `d5b53b71` every workflow passed: CI, MAUI (including device tests), Coverage, Package,
Build metrics, Bootstrap and Node projects ([MAUI run 45765358125](https://github.com/jfversluis/microsoft-ui-reactor/actions/runs/45765358125),
[CI run 45765358267](https://github.com/jfversluis/microsoft-ui-reactor/actions/runs/45765358267)).
The report commit after it changes only this file.

## Performance

`/perf` ([run 45752173137](https://github.com/jfversluis/microsoft-ui-reactor/actions/runs/45752173137)),
StocksGrid, 12 paired runs against `main`:

| Metric | `main` | 4b-3 | Δ (95% CI) |
|---|--:|--:|--:|
| Renders/sec ↑ | 3.22 | 3.13 | -1.3% [-5.1, +2.4] |
| Avg Reconcile (ms) ↓ | 118.7 | 115.5 | -1.7% [-4.0, +0.5] |
| Avg Diff (ms) ↓ | 106.8 | 106.2 | -0.4% [-2.9, +2.0] |
| Avg Memory (MB) ↓ | 289.9 | 291.8 | +0.5% [+0.1, +1.0] |

- All 27 reconciler micro-benchmarks are within noise, with allocations unchanged.
- The workflow flags memory at +0.5%, under the gate's +5% threshold. This phase changes no file
  under `src/Reactor` or `src/Reactor.Core`, so the WinUI code measured is unchanged from 4b-2.

## The iOS CI failure, and a product bug it found

CI's first iOS device run failed four dialog fixtures:

- **The cause was the test harness.** It answered an alert in process, UIKit ignored the dismissal,
  and every later dialog correctly waited behind the alert left on screen.
- **The harness now answers through UIKit's own tap path.** It waits until the alert is gone, and
  fails clearly if it isn't. Every fixture's teardown also closes leftover alerts, so one failure
  can't cascade.
- **The product bug.** The investigation found that the iOS barrier held Reactor's dialogs behind
  *any* alert on screen, including one Reactor didn't show. It now tracks only the alerts its
  queue presented.
- **Two new fixtures** cover it, and each fails when its mutation is applied:
  - an answer arriving before the alert has gone holds the next dialog;
  - an alert outside the queue doesn't hold Reactor's dialogs back.
- **Three follow-ups from the gpt-6.1-sol checks**, each reproduced on Mac Catalyst first, fixed,
  and confirmed by that model's probe:
  - the queue held its own alert weakly, so a collection could release the barrier early. It's
    now held strongly;
  - an alert another component opened from `OnClosed` was counted as the queue's own. Ownership is
    now settled right after presentation;
  - a foreign alert with the same title was also counted as owned. The queue now marks exactly the
    one alert its presentation created (a snapshot just before MAUI presents, compared just after).
  - Each has a device fixture that fails under its mutation.

## Review

`pr-review` ran 7 dimensions on Claude plus a gpt-6.1-sol cross-check: a blind scan, reconciliation,
and three rounds checking the fixes. Packaging was clean.

- **High, all fixed and confirmed by the other family:**
  - **Dialog queue:**
    - a late window's drain presented a dialog that the same render had closed;
    - a destroyed window's queue was dropped, so later requests escaped cancellation;
    - a refused dispatch stranded a request, and later wedged the queue;
    - Android's focus barrier stalled while a modal was open;
    - a confirmation could act on a different target than the one shown (raised by security);
    - a failed report could leave a task pending;
    - a dialog terminated during presentation was missed.
  - **Back key:**
    - the Android back key inside a MAUI modal bypassed the handlers. Both families found it, and
      it was confirmed on the emulator with a real key event.
    - a per-turn mark dropped a distinct second back.
- **Medium, fixed:**
  - awaiting a dialog in an `async void` handler could crash the app when the task faulted;
  - every app lost the Android predictive-back preview at the root;
  - Windows was enabled before being validated;
  - several test gaps (a background-thread request, a hosted `ReactorPage`, list cells, a positive
    control for the navigation hold, and skip reporting).
- **Low, fixed:** docs for the Mac Catalyst and `onDestroying` behaviour, the presentation-style
  alias snippet, the queue waiting on `BecameIdle`, window tracking following detach, and the
  registry moving to an attached property.

## Decisions for the gate

1. **Imperative dialog tasks never fault.** Destruction or a failure completes them with the cancel
   answer, and the failure is reported. Recommendation: accept.
2. **The Android back hook is a callback enabled only while handlers exist**, so apps without the
   hook keep the predictive-back preview. Recommendation: accept.
3. **`ModalPresentationStyle` isn't applied on Mac Catalyst**, because of a MAUI sheet-dismissal
   hang. Recommendation: accept, and file it in dotnet/maui.
