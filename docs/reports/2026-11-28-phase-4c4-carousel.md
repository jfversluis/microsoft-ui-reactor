# Phase 4c-4 gate: CarouselView and IndicatorView (spec 070 §5)

This phase adds a controlled `CarouselView` position and `IndicatorView`, the last 8 properties of the census.
The carousel is the hardest controlled value so far:
- the platform moves it by touch, fling, accessibility and its own animations;
- MAUI corrects it after items change;
- iOS reports no drag flag.

The position is therefore owned by an explicit state machine that reads the platform at rest instead of trusting
MAUI's events. PR #21, stacked on #20.

## What changed

- **Characterization first** (spec 070 §5.1, `CarouselCharacterization.cs`, on Mac Catalyst, the iPhone and iPad
  simulators and the Android emulator):
  - **iOS:** `IsDragging` is never set, and MAUI's `ScrollTo` does nothing with an `ItemSpacing` and no peek.
  - **Android:** it reports a position during a drag, `ScrollTo(item)` never updates `Position`, and a slow
    release rests between pages.
  - **Both:** MAUI re-positions after items change, and its initial positioning on iOS undoes an earlier scroll.
- **API** (§5.3):
  - `CarouselView(items, key, template, position, onPositionChanged)` with `ItemState.IsCurrent`, `Loop`,
    `PeekAreaInsets`, `ItemSpacing` and `IsScrollAnimated`.
  - `IndicatorView(count, position, onPositionChanged)` with colours, size, `IndicatorsShape`, `MaximumVisible`
    and `HideSingle`.
  - MAUI's names are kept. The WinUI counterparts (`FlipView`, `PipsPager`) are recorded in spec 068.
- **The controlled position** (§5.4, §5.4.1). `CarouselPositionController` has two regions:
  - **Motion:** `Resting`, `Watching{input}`, `OwnProjecting{target}` and `AwaitingAnswer{seq}`.
  - **Items:** published, deferred until rest, and the barrier window of an epoch.

  It runs against a per-platform port:
  - **UIKit:** the page scroller's offset and flags, a pan target, `CADisplayLink`, and a `PerformBatchUpdates`
    barrier shared with the selection verifier.
  - **Android:** `RecyclerView` scroll state, the snap helper's alignment, `Choreographer`, and the item-animator
    barrier.

  Rules:
  - Reports come from the item the platform rests on.
  - Reactor's own scrolls are never reported.
  - A user's move during an items change is reported first.
  - A projection that doesn't land is retried at most three times, then warns and gives up until the
    declaration changes.
  - The port says whether its own scroll is still pending, bounded by `ScrollBridge.SettleBound`.
- **Windows (no port until 4g):** falls back to MAUI's `Position` and `PositionChanged` in the echo scope, with a
  one-time diagnostic. It has no drag safety.
- **Device tests:**
  - Three gates (items barrier, rest detection, shown geometry) and nine carousel fixtures plus
    `IndicatorView_Controlled`, on both registries.
  - Drags hold for real, and a host drag fails if the finger lifts early.
  - A fling fixture.
  - `run.sh` now handles gesture requests only from the app's PID, as whole lines with integer fields. This fixes
    a host command-injection that review found.
- **Docs:** spec 070 §5 (characterization, design, the state machine, the as-built record with deviations and
  mutants), spec 068 rows (CarouselView 🟡 with gaps no port needs, IndicatorView ✅), specs 065 and 067, the README
  (Carousels section with per-platform caveats, factory table) and the CHANGELOG.

## Tests

| Tier | Before (4c-3c head) | 4c-4 | Notes |
|---|--:|--:|---|
| MAUI renderer (headless) | 1351 | 1436 | 61 controller tests on a stub port, IndicatorView, census, README snippet |
| MAUI engine (headless) | 80 | 80 | |
| MAUI device, Mac Catalyst | 218 + 18 skipped | 239 + 20 skipped | full run at the final product code |
| MAUI device, iPhone | 215 + 21 skipped | 236 + 23 skipped | full run; real-touch (Appium) carousel fixtures also pass |
| MAUI device, iPad | 225 + 11 skipped | 246 + 13 skipped | full run |
| MAUI device, Android 37 | 218 + 18 skipped | 241 + 18 skipped | full run |
| Census pending | 8 | 0 | ceiling 0 |
| Release build | 0 warnings | 0 warnings | |

- **New skips:** the two `CarouselView_Fling` fixtures on iOS and Mac Catalyst without real touches. They pass
  with Appium on the iPhone.
- **Mutation checks:**
  - Every transition guard of §5.4.1: 48 mutants, of which 47 are red. The survivor, the report's sequence check,
    is equivalent because only one report is ever in flight.
  - The verification round's ten new guards: all red.
  - Device: the six earlier mutants plus three on the port's pending signal and Android's settle, all red on the
    rest-detection gate.
  - Four mutants that survived the first pass marked logic that was redundant; it was removed.
- **Failing first:** every review probe is a headless test. Ten were red on `d7f8d28f` and five more on
  `cac03cbd`; all pass now.

## Performance

No file under `src/Reactor`, `src/Reactor.Core` or the benchmark changed.

The benchmark binary is therefore the same as 4c-3c's.

- **`/perf` on `cac03cbd`:** the Flex/Yoga workload flagged Avg Reconcile +5.5% and Avg Diff +6.6%, with
  allocations per render at +0.9%. Everything else was within noise.
- **Rerun on the final head** ([run 46105423506](https://github.com/jfversluis/microsoft-ui-reactor/actions/runs/46105423506)):
  every time and allocation metric is within noise in every workload, Flex included. The Flex flag didn't
  repeat.
- **Avg Memory (working set):** flagged at +0.4% to +1.3% in both runs. It isn't caused by this phase:
  - The measured code is identical to 4c-3c's.
  - Every PR in the stack, measured against upstream `main`, shows the same +0.2% to +0.9% (PRs #18–#20,
    sometimes flagged).

  It is a stack-level working-set cost, most likely from the phase 2 core extraction. It is tracked as a
  follow-up to pin down before release.

## Review

`pr-review` ran 7 dimensions on Claude plus a gpt-6.1-sol cross-check, followed by two verification rounds with
both families. Before any code, the design had a review round by both families: it moved to deferred
transactions, own projections, epochs, alignment and a frame poll.

**Code review of `d7f8d28f`.** The controller was restructured into an explicit state machine rather than
patched.

- **Highs:**
  - (gpt, reproduced) A stale own target after the barrier.
  - (gpt, reproduced) An accessibility settle in the window was overwritten.
  - (gpt, reproduced) A drag interrupting an own projection was lost while items were pending.
  - (Claude) Items arriving while the view isn't laid out were never published.
  - (security, reproduced end to end) Host command injection through `run.sh`'s logcat parsing.
  - (tests) Real-touch holds didn't hold.
  - (tests) The user-during-own path was untested.
- **Mediums:**
  - Windows was inert.
  - Any commit answered a report.
  - A stale position was reported after a portless transaction.
  - The retry cap re-armed.
  - Rest was inferred from frame counts.
  - Ref, alignment, vacuous and fling coverage gaps.
  - The docs.

All fixed, each with a regression test.

**Verification round on `cac03cbd`:**
- Security: clean, with a positive control on the old script.
- Claude correctness: all five of its findings fixed.
- gpt reproduced four new highs and one medium, all fixed failing-first:
  - V1: a give-up survived a handler swap.
  - V2: the answer projected into a ref animation with no motion flag.
  - V3: a give-up stranded a transaction and a refused move.
  - V4: a second transaction erased the first window's input.
  - V5: a port that stays pending forever.

**Verification round on `fa5a60a5`:** both families report 0 findings. gpt ran 14 new adversarial probes plus the
61 regressions.

## Decisions for the gate

1. **MAUI's names** (`Position`, `Count`), with WinUI's counterparts recorded. Recommendation: accept.
2. **`Position` is written only at mount.** After that, projections go through the port and reports come from
   the resting item, not MAUI's events. A ref reading `Position` can see MAUI's stale value. Recommendation:
   accept; documented.
3. **The items-change window.** A jump with no motion between a transaction and its barrier is overwritten.
   Motion, user input and ref commands in the window are always reported. Recommendation: accept; documented as a
   limitation.
4. **The position is an index, not a key**, as in MAUI and WinUI. A key-based position stays an open question.
   Recommendation: accept.
5. **Windows falls back to MAUI's `Position` until the 4g port.** Recommendation: accept; noted in spec 068 and
   the README.
6. **`IndicatorTemplate` and vertical carousels are out of scope.** Recommendation: accept.

## Open issues and follow-ups

- **To file in dotnet/maui** (§5.11):
  - Two removals in one turn crash under `KeepScrollOffset` (Android).
  - `ScrollTo` lands one item off during item animations (Android).
  - `ScrollTo(item)` doesn't update `Position` (Android).
  - A slow release rests between pages (Android).
  - `IsDragging` is never set on iOS.
  - `ScrollTo` does nothing with spacing and no peek (iOS).
- **Not automated:** real VoiceOver and TalkBack (accessibility actions stand in for them), and real pointer
  input on Mac Catalyst.
- **Windows (4g):** a carousel port with drag safety.
- **Stack working set:** `/perf`'s Avg Memory is about +0.5% against upstream `main` across the stack. Before
  release, attribute it to a phase with a same-runner A/B, then fix it or accept it.
- **Phase 4 continues with 4d:** animations, theming, styling direct views, flow direction, accessibility
  properties.
