# Phase 3a gate: MAUI on the shared core (spec 067 §13)

Phase 3 starts by moving the MAUI renderer's components and hooks onto `Microsoft.UI.Reactor.Core`,
the core extracted from WinUI in phase 2. MAUI's `Element` and `Component` now derive from the
core's `ReactorElement` and `ReactorComponent`, and its components render through the core's
`RenderContext`, `Context<T>`, skip gate and render kernel. The transitional
`Microsoft.UI.Reactor.Maui.Core` package is gone. PR #7, stacked on #6.

## What changed

- **MAUI, now on the core:**
  - `Element : ReactorElement` and `Component : ReactorComponent`. They keep WinUI's full names, in
    the MAUI assembly, so code written with the core's hooks compiles for either renderer.
  - The core's `RenderContext` replaces MAUI's. MAUI components gain the core-only hooks
    (`UsePersisted`, `UseObservable`, `UseExternalStore`, `UseCollection`).
  - The core's skip gate and render kernel replace the engine's inline versions. MAUI gains the
    hot-reload hook-order retry.
  - MAUI-only hooks (`UseRouteStack`, `UseResource`, `UseCallback<TDelegate>`, `UseAppTheme`,
    `UseWindowSize`) are extensions of `RenderContext`, with shortcuts on `Component`.
- **The engine stays in the renderer** (`Reactor.Maui/Engine`), per §11.1. It fills the core's
  context scope from the providers above each node it renders, and keeps provider subscriptions,
  so a reader below a skipped component still re-renders.
- **Core additions,** all internal:
  - a per-context render-thread enqueue, which resolves the 2a per-owner dispatcher follow-up;
  - an opt-in hook-count check;
  - `HasPendingEffects`.
- **Core fixes, visible to WinUI in error paths only:**
  - an effect scheduled twice before the flush keeps the cleanup it staged;
  - a cleanup that throws during the flush doesn't run again at unmount.
- **Packaging:** `Microsoft.UI.Reactor.Maui` depends on exactly its own core version, as the WinUI
  package does, and the MAUI workflow checks the packed nuspec.

## Tests

| Tier | Before (2c head) | Phase 3a | Notes |
|---|--:|--:|---|
| MAUI engine (headless, was "core") | 44 | 50 | +6 for the new seams; one boundary test replaced |
| MAUI renderer (headless) | 34 | 34 | |
| MAUI device, Mac Catalyst | 25 | 25 | |
| MAUI device, iPhone 18 Pro simulator (iOS 27) | 25 | 25 | |
| MAUI device, Android API 37 emulator | 25 | 25 | the Pixel was not attached |
| Neutral core tests (macOS harness) | 17 | 21 | +4 for the core seams |
| MAUI Release build (Android, iOS, Mac Catalyst) | 0 warnings | 0 warnings | |
| WinUI unit tests (all projects) | 16,616 passed | 16,620 passed, 22 skipped, 0 failed | +4 neutral seam tests |
| WinUI selftests, packaged, E2E, AOT, integration, docs, Release build | green | green | CI run 45514659846; AOT shard 2 re-run once for the `MdSelection_ChildUnmountUpdatesMembership` flake (second sighting) |

**Mutation checks.** Each new behavior has a test that fails without it:

| Mutation | Fails |
|---|---|
| no effect coalescing | `SetStateDuringRender_KeepsTheLatestEffectSubscribed`, the neutral coalescing test |
| no providers pushed into the scope | 4 context tests |
| no provider subscriptions | 3 context tests |
| no hook-count check | `CallingADifferentNumberOfHooks_Throws` |
| the gate always skips | 5 tests |
| provider value refreshed only on change | `AReaderThatRendersForAnotherReason_SeesTheProvidersCurrentValue…` |
| no hot-reload flag in MAUI's pass | `AHotReloadThatChangesAComponentsHooks…` |
| null props not installed | `PropsThatBecomeNull_AreInstalled` |
| `UseResource` without the root scheduler | `AFetchThatCompletesAfterItWasSuperseded…` |
| `UseCallback` always returns the new delegate | both `UseCallback` identity tests |
| global instead of per-context enqueue | the neutral off-thread setter test |
| cleanup cleared after it runs | the neutral throwing-cleanup test |

## Performance

**MAUI benchmark** (Mac Catalyst, Release, 1,000 keyed rows, median of 7 per run). The previous
phase's head and this branch were built and run alternately on one machine, 3 runs each:

| Scenario | Controls leg | Direct leg | Rows as components (anchor mode) |
|---|---|---|---|
| Mount | +0.6% | 0.0% | −0.2%, alloc +0.9% |
| Update all | +1.0% | 0.0% | +1.2% |
| Update one | 0.0% | −0.4% | +1.8% |
| Reverse | +1.7% | +2.4% | +0.9% |

All are within run-to-run ranges. The default benchmark's rows are plain elements, so the
component-heavy anchor mode (every row a `Component`) is the one that exercises this change. Its
Mount allocates +0.9% (about 240 bytes per component, from the larger shared node and context).
That is below the 1% threshold and recorded as a known cost. Updates allocate the same.

**WinUI**, since the core's `RenderContext` changed. `/perf` against `main`, two runs on `d3514dc`
(workflow runs 45516989035 and 45526095918):

| Leg | Run #1 | Run #2 |
|---|---|---|
| StocksGrid 50%: reconcile, alloc | **+6.7% [+2.6, +10.8]**, +1.6% [−0.4, +3.6] | **+2.0% [+0.5, +3.4]**, +0.4% [−0.1, +0.9] |
| Skip floor: reconcile | −1.6% [−6.5, +3.3] | −0.1% [−5.0, +4.8] |
| KeyedList: reconcile, alloc | +1.1% [+0.1, +2.1], +0.5% [+0.1, +1.0] | +1.0% [−0.5, +2.4], 0.0% |
| Flex, DataGrid | within noise | within noise |
| `M7` Update_NoChange (ns) | +3.5% [+3.0, +4.0] | +4.5% [+1.6, +7.3] |

StocksGrid at 50% is flagged on both runs, and run #1 is above +5% with a CI excluding 0. So it was
isolated with the same harness, 30 paired reps, on separate runners (scratch branch
`perf/isolate-3a`):

| StocksGrid 50%, 30 paired reps | Reconcile | Diff | Alloc |
|---|---|---|---|
| A/A control (phase 3a vs itself) | +0.5% [−1.5, +2.5] | +1.1% [−0.9, +3.0] | 0.0% |
| phase 2c → phase 3a | −0.7% [−2.2, +0.8] | −0.7% [−2.3, +0.9] | −0.1% |
| `main` → phase 3a | **−2.3% [−3.5, −1.0]** | −1.4% [−2.8, −0.1] | +0.3% [0.0, +0.7] |

Neither this phase nor the whole stack resolves a slowdown at 2.5× the sample size; against `main`,
the stack is slightly faster. The micro-suite isolation (phase 2c → 3a, 30 reps) has every bench
within noise except `M3` Mount_Leaf_ThreeCallbacks, +2.2% [+0.5, +3.9], below the threshold and on a
path this phase didn't touch. `M7`'s +3–5% against `main` again doesn't come from this phase
(−0.3% [−2.1, +1.6] in isolation); phase 2a's report traced the same reading. The `/perf` 50% leg
reads high against `main` more often than isolation confirms, which is worth a look at its method,
as the floor leg's was at phase 2a.

## Review

`pr-review`: 7 dimensions on the default model (Claude), plus a GPT-family cross-check
(gpt-6.1-sol, blind scan then reconciliation).

- **2 high, both confirmed cross-family and fixed:**
  - A provider replaced with an equal but distinct value kept serving the old instance to readers
    that rendered for another reason.
  - The MAUI package didn't pin the core version while binding its internals.
- **Medium, fixed:**
  - the ancestor walk per render (now cached, no allocation);
  - `UseCallback<TDelegate>` hiding the core's typed overloads;
  - `Provide`'s shape and the portability claims, now narrowed with the divergences listed;
  - four untested paths, now with tests.
- **Low, fixed:** documentation wording, and the workflow's .NET 10 runtime.
- **Security: clean.**

## Flakes

- MAUI device tests on CI: `NavigationStack_TwoStacks_ShareTheWindowsModalStack` once (iOS, timed out
  after 5 s waiting for a tab under a modal to load). It passed 5 of 5 locally and on re-run. The wait
  is lengthened in phase 3b.
- WinUI: `MdSelection_ChildUnmountUpdatesMembership` once more (AOT); unrelated code, first seen in
  phase 2b.

## Decisions for the gate

1. **Keep WinUI's full names for MAUI's `Element` and `Component`** (settles spec §3's open point).
   Recommendation: accept.
2. **The engine stays in `Reactor.Maui`** and its tests become `Reactor.Maui.Engine.Tests`, instead
   of a neutral engine package. Recommendation: accept.
3. **The two core fixes change WinUI in error paths** (a cleanup no longer leaks or runs twice).
   Recommendation: accept.
4. **Renderer-specific hooks** (`UseResource`'s result type, `UseWindowSize`'s type, `Provide`'s
   shape) converge in phase 4, before the MAUI package leaves preview. Recommendation: accept.
