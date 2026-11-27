# Phase 4c-3b gate: control and layout audit, FlyoutPage split mode, toolbar items (spec 070 §4.14–§4.15)

This phase finishes the §1 audit for every control and layout property. It fixes a flyout that never
loaded on Mac Catalyst and iPad, and toolbar items that ran another item's action. PR #19, stacked on
#18.

## What changed

- **Characterization first,** on Mac Catalyst, an iPhone, an iPad simulator (rotated in process) and
  an Android phone:
  - phones never split;
  - Mac Catalyst and iPad split only with `Split` and `SplitOnPortrait`. Rotation never changes the
    mode, because MAUI reads an orientation that scene-based iOS apps always report as portrait;
  - while split, `IsPresented` is forced true, and writing `false` throws and leaves the setter
    stuck;
  - leaving split by a behaviour change makes MAUI set `false` inside the write;
  - a kept MAUI toolbar item ran the replacement's action after a rebuild, on all three platforms.
- **FlyoutPage split mode:**
  - **The rule:** while split, `IsPresented` belongs to the platform. Reactor never writes or reports
    it there.
  - **Mode changes:** Reactor wraps MAUI's `FlyoutBehavior` mapping for its own flyout pages, so a
    platform-driven change, including Android's forced drawer close on rotation, isn't input.
  - **Reapplying:** after every mode change the declaration is applied again, even when the property
    already matches, so a drifted platform state is corrected.
  - **One writer:** a single guarded writer on the page handles every write.
  - **Split state:** stays hidden from apps, which the README says.
- **Toolbar items:** a retired item invokes nothing, as 4c-2's swipe items.
- **The audit:**
  - **Claimed:** all 203 control and layout census properties: 143 matrix cases, 9 event cases, 13
    controlled-value cases (26 properties), and 25 claims to specific tests with their facets and
    mutation evidence.
  - **Pending:** 348 → 145, as exact `(type, property, phase)` keys frozen per phase. 137 are for
    4c-3c and 8 for 4c-4.
- **Bugs the audit found,** each fixed with a test that failed first:
  - `DirectCheckBox` rewrote its colour every render;
  - DatePicker, Stepper and Slider ranges moving later kept the old minimum (write order);
  - a DatePicker date outside its range, or with a time of day, was rewritten every render;
  - on Android, a cleared progress-bar colour stayed drawn. Reactor restores the bar's own or the
    theme's colour on its own bars only (tracked by an ownership record), so app progress bars and
    implicit `ProgressBar` styles are untouched.
- **Docs:** spec 070 §4.14 and §4.15 (as built, deviations), spec 068 rows (no ✅ with pending keys;
  unmodelled properties say no port needs them), spec 065, the README and the CHANGELOG (under
  Fixed).

## Tests

| Tier | Before (4c-3a head) | 4c-3b | Notes |
|---|--:|--:|---|
| MAUI renderer (headless) | 686 | 1085 | matrix and controlled-value cases, the flyout split rule with seeded forced values, toolbar identity, range ordering, census phase gates |
| MAUI engine (headless) | 80 | 80 | |
| MAUI device, Mac Catalyst | 174 + 7 skipped | 198 + 18 skipped | full run after the review round; new skips are iPad-only fixtures |
| MAUI device, iPhone | 174 + 7 skipped | 195 + 21 skipped | as above |
| MAUI device, Android 37 | 176 + 5 skipped | 198 + 18 skipped | as above |
| MAUI device, iPad | — | 33 + 4 skipped | flyout, toolbar and audit fixtures; `FlyoutPage_SplitRotated` skips visibly because rotation doesn't change the mode |
| Release build | 0 warnings | 0 warnings | |

- **Mutation checks:** every named mutant turned red, on device as well as headless. The two that
  survived in the build were a redundant scope, now removed, and an ordering-equivalent write.
- **Not measured on a device:** an Android tablet's rotation path; none could be booted here. It is
  covered by headless tests.

## CI

- **The first iOS run on the hosted runner was stopped at `run.sh`'s 600 s limit**, partway through the suite and with no failing fixture. The suite has grown to 216 fixtures and needs longer on the hosted simulator.
- **The limit now defaults to 1500 s, and the workflow sets 1800 s.** The job timeout stays at 60 minutes.
- **`run.sh` now says when it stopped a run at the limit.** It was checked with a 25 s limit, and a full Mac Catalyst run with the new script passes (198 + 18 skipped).

## Performance

`/perf` ran on PR #19. This phase changes no file under `src/Reactor`, `src/Reactor.Core` or the
benchmark, so the measured code is identical to 4c-3a's. One Mac Catalyst MAUI benchmark pair was
within noise.

## Review

`pr-review` ran 7 dimensions on Claude plus a gpt-6.1-sol cross-check, then two verification rounds.
There were no highs in the first round; security and the cross-check were clean.

- **Medium, fixed:**
  - the controlled-value runner's unmount check was a no-op for bools;
  - the Android progress-bar fix applied app-wide;
  - the toolbar icon was never checked on the native item;
  - the flyout's split guard in the re-apply path was untested and duplicated;
  - the split-rotation fixture passed without a mode change;
  - the date picker's equal-render check couldn't fail;
  - docs: CHANGELOG buckets, README, spec 068 cells.
- **High in verification, fixed:** gpt-6.1-sol reproduced that the first scoping fix, a
  `ReactorProgressBar` subclass, broke apps' implicit `ProgressBar` styles. An ownership record
  replaced the subclass.
- **Low, fixed:** a date with a time of day, a static test hook running in parallel, `DirectCheckBox`
  using the shared change helper, and a test comment.

## Decisions for the gate

1. **Split 4c-3 further:** 4c-3c takes the 137 page, navigation, Shell, dialog, collection and swipe
   properties; 4c-4 takes CarouselView's 8. Recommendation: accept.
2. **Split state stays hidden**; a split-changed callback (after WinUI's `DisplayModeChanged`) is a
   possible follow-up. Recommendation: accept.
3. **The Android tablet rotation path is tested headlessly only.** Recommendation: accept for now;
   cover it when a tablet is available (phase 5 or 4g).

## Open issues and follow-ups

- **To file in dotnet/maui:**
  - iOS reads orientation from `StatusBarOrientation`;
  - Mac Catalyst reports portrait for every window;
  - the `IsPresented` setter sticks after a throw;
  - Android's progress-bar colour clear.
- **Windows (4g):** check MAUI's Windows progress-bar clear.
- **4c-3c:** the remaining 137 census properties.
