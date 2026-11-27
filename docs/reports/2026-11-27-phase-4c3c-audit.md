# Phase 4c-3c gate: page, navigation, Shell, dialog and collection audit; Shell flyout presentation (spec 070 §4.16–§4.17)

This phase closes the census: every public element property except CarouselView's 8, which belong to 4c-4, now
has executable evidence. It fixes the Shell flyout's presentation under each behaviour, and makes a long run of
device tests stable on every platform. PR #20, stacked on #19.

## What changed

- **Characterization first** (gates C1–C3 on Mac Catalyst, the iPad simulator and Android):
  - **`Locked`:** MAUI forces the flyout open when the page appears. iOS and Mac Catalyst then always show the
    pane; Android follows the property.
  - **Every page change:** MAUI forces the flyout open again under `Locked` and closed under `Disabled`.
  - **Behaviour and item changes:** a change Reactor writes (behaviour, or a `FlyoutItem`↔`TabBar` switch)
    makes MAUI open or close the pane inside the write.
  - **Android after a first `Disabled`:** the pane stays hidden while the property says open.
- **Shell flyout fixes** (each with a test that failed first):
  - Reactor's whole Shell update is one echo scope, so MAUI's opens and closes caused by Reactor's own writes
    aren't reported as input.
  - The declaration is applied once, after items and selection are staged.
  - Under `Locked` and `Disabled`, the flyout state belongs to the platform: Reactor neither writes nor reports
    it.
  - On Android, the pane is shown again on the next turn after a first `Disabled`, through the engine's
    `PostReport`.
- **Template errors:** an exception in an item, group header or group footer template reaches the
  `ErrorBoundary` above the list. A recycled cell rebound to a throwing item unmounts its old tree.
- **`AutomationId`:**
  - **Warning:** a changed `AutomationId` on a page or a view now warns once.
  - **Mounting:** an explicit mount flag replaces "no previous modifiers". Before, an update with no
    modifiers took the mount path, which crashed the Controls renderer and silently overwrote the direct
    renderer's id. This bug predates 4c-3c.
- **The census:** pending 145 → 8.
  - **Evidence:** 57 property cases, 5 event cases and 2 controlled-value cases (4 keys) on both registries,
    plus 71 covered-by claims. Each claim names its test and its own mutant.
  - **Behaviour pinned:** spec 069's dialog behaviour is pinned by a dialog theory, and native prompt fields
    (keyboard, maximum length through real validation) by device fixtures.
- **Device-test stability.** Four flakes were found, each traced to its mechanism and fixed in test code:
  - **Entry selection (pre-existing):** the fixtures waited on a different main-thread queue than Reactor's
    work. `FixtureContext.Drain` now waits on Reactor's queue.
  - **Android back key:** a back key arriving late landed on the next fixture. `run.sh` now streams the log
    and answers each request at once, and the teardown brings the app back to the front.
  - **iPad `FlyoutPage_Split`:** a hit test ran during the flyout's animation, and rotations leaked into later
    fixtures. Rotations are now waited for, and portrait is restored at teardown.
  - **iPad `TwoStacks`:** a full-screen modal takes UIKit's tab bar out of the window, so a tab selected after
    that never loaded. The modal is now presented over full screen.
  - **CI iOS `WebView_CommandsThroughARef`:** every new `WKWebView` waits for WebKit to start a page process,
    which can take more than 10 s on a loaded runner. This was reproduced by killing the spare process
    under load. Every new web view's first page now gets 60 s, and the fixture runs under a 150 s limit.
- **Docs:** spec 070 §4.16 and §4.17 (characterization, rules, the mutant table, deviations), spec 068 rows
  (only CarouselView pending), spec 065, the README (per-platform flyout behaviour, `AutomationId`, template
  errors) and the CHANGELOG (Fixed).

## Tests

| Tier | Before (4c-3b head) | 4c-3c | Notes |
|---|--:|--:|---|
| MAUI renderer (headless) | 1085 | 1351 | 72 Shell presentation cases, structure, navigation, dialog and page audits, template errors |
| MAUI engine (headless) | 80 | 80 | |
| MAUI device, Mac Catalyst | 198 + 18 skipped | 218 + 18 skipped | full run on the final product code |
| MAUI device, iPhone | 195 + 21 skipped | 215 + 21 skipped | full run on the final product code |
| MAUI device, Android 37 | 198 + 18 skipped | 218 + 18 skipped | full run on the final product code |
| MAUI device, iPad | 33 + 4 (subset) | 225 + 11 skipped | full run at the final head |
| Release build | 0 warnings | 0 warnings | |

- **Mutation checks:**
  - About 100 product mutants, all red except one equivalent: removing only `SyncSlots`' own scope, which
    `Stage` covers.
  - The Shell-rule mutants are defined in a table in §4.17.
  - Device mutants DM10 and DM11 are red.
- **Not tested first:** the xUnit collection fix and the removed commit flag have no failing-first test.
  Neither is reproducible on demand; both are recorded in §4.17.

## Performance

No file under `src/Reactor`, `src/Reactor.Core` or the benchmark changed, so the measured code is identical to
4c-3b's.

- **First `/perf`:** flagged Avg Diff at +8.2% in one section, with a wide interval (CI [+0.4, +16.0]). The same
  run showed improvements of +7.0% and +6.4% elsewhere, which looks like runner noise.
- **Rerun on the final head** ([run 46048493870](https://github.com/jfversluis/microsoft-ui-reactor/actions/runs/46048493870)):
  Avg Diff +1.7% [-3.2, +6.6] and Avg Reconcile +2.3% [-4.0, +8.5], both within noise. The only flag is
  allocations per render at +0.4%, under the 1% threshold. The +8.2% didn't repeat.

## Review

`pr-review` ran 7 dimensions on Claude plus a gpt-6.1-sol cross-check, then two verification rounds. The
design took one review round by both families before code.

- **Design round (both families):** Reactor's own behaviour and selection writes cause MAUI presentation
  changes, so a Locked-only filter was insufficient. This led to the full-projection echo scope and the
  post-staging apply. Android's history-dependent `Disabled` was measured. Template errors escaping during cell
  realization were found and fixed.
- **Code review (no highs), medium, fixed:**
  - Shell test classes were outside the `MauiShell` collection, which races MAUI's process-wide routing tables;
  - the Android re-show bypassed `PostReport`;
  - the uncontrolled flyout and the `Stage` re-entry were untested;
  - a recycled cell kept its tree after a template error.
- **Verification round 1 (both families, reproduced):** the `AutomationId` mount detection bug. Claude also
  found the policy ran more than once per Shell render. Both are fixed and mutation-checked.
- **Low, fixed:** the effective-behaviour precedence in the docs, the XML docs, undefined mutant IDs, weak
  oracles, and per-test probe state.

## Decisions for the gate

1. **Under `Locked` and `Disabled`, the Shell's flyout state belongs to the platform:** no write and no report.
   Characterization showed MAUI forces it at every page change, so a disagreeing declaration couldn't be held.
   Recommendation: accept; documented per platform.
2. **A changed `AutomationId` warns once and is otherwise ignored**, matching MAUI's set-once rule.
   Recommendation: accept.
3. **Colours stay headless-only, and spec 069's dialog behaviour is pinned rather than changed.**
   Recommendation: accept.

## Open issues and follow-ups

- **To file in dotnet/maui:**
  - Shell `Locked` forces `FlyoutIsPresented` at every page change.
  - On Android, the pane stays hidden after a first `Disabled`.
- **Windows (4g):** check the Shell flyout presentation.
- **Mac Catalyst:** `TwoStacks` keeps full-screen presentation there, because Reactor doesn't apply the style on
  Catalyst. It has passed every run.
- **Phase 4 continues with 4c-4:** CarouselView and IndicatorView, the last 8 census properties.
