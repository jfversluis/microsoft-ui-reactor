# Phase 2a gate: foundations, the hook table in the shared core (spec 067)

Phase 2a moves `RenderContext`, which holds the hook table, into `Microsoft.UI.Reactor.Core`, together
with the hooks every renderer shares and the component base. After this phase, a MAUI component and
a WinUI component run on the same hook implementation. Results are measured on PR #3 of the fork,
against the phase 0 baselines and the phase 1 gate (`2026-11-23-phase-1-core-design.md`). Design and
seams: spec 067 §10.

## What changed

- **Moved into the core:**
  - `RenderContext` and the shared hooks: state, reducer, effect, memo, callback, ref, context,
    persisted, observable, collection and external store.
  - Their types: `Context<T>`, `ContextBase`, `Ref<T>`, `HookOrderException`, the persisted-state
    scopes and `LruCache`.
  - The neutral diagnostics: the `Microsoft-UI-Reactor` event source, `DiagnosticLog` and
    `ComponentPerfTally`.
  - The hot-reload state copier.
  - `Reactor.dll` type-forwards all 9 public types that moved.
- **Components:** `ReactorComponent` (core) declares `Render()`, `ShouldUpdate()` and the shared
  hooks' shortcuts. WinUI's `Component : ReactorComponent` narrows `Render()` to `Element` with a
  covariant override.
- **WinUI hooks** (windows, pickers, navigation, theme and accessibility, commands, `UseIntl`,
  `UseObservableTree`) are C# 14 extension members of `RenderContext` in `WinUIHooks`, so
  `ctx.UseWindow()` reads as before.
- **Seams:**
  - owner (`IRenderOwner`);
  - UI dispatcher (`RenderScheduler`);
  - perf state events (`CorePerfHooks`);
  - component ids;
  - hot-reload pass;
  - memory pressure (`ApplicationPersistedScope.MemoryPressureSource`).

  WinUI installs each of them through `WinUICoreServices`, not through a module initializer.
- **Tooling:**
  - The API index reads the core and lists extension members on their receiver.
  - The docs pipeline maps extension-block doc ids, so the hook reference pages regenerate
    byte-identical.
  - The source-map generator, four analyzers and the reviewer manifest follow the new declaring
    types (see Review).

## WinUI test tiers (PR #3, CI run 45362861634, head `5cf7f166`)

| Tier | Phase 1 gate | Phase 2a | Notes |
|---|--:|--:|---|
| Unit tests (all projects) | 16,586 passed, 22 skipped | 16,599 passed, 22 skipped, 0 failed | +13 new tests (see guards) |
| Selftests (2 shards) | 1,149 + 1,149 | 1,149 + 1,149 | see flake note |
| Packaged selftests (MSIX, 2 shards) | 1,138 + 1,139 | 1,138 + 1,139 | |
| E2E, winapp ui (2 shards) | 43 + 232 | 43 + 232 | shard 2's built-in negative control fails as designed |
| AOT selftests, AOT trim proof, Devtools trim | passed | passed | |
| Integration tests | 7 | 7 | |
| Docs build, snippet analyzer, tier drift | passed | passed | 4 pages regenerated (snippet paths, `ReactorComponent`) |
| Build solution (Release), Package, Bootstrap | passed | passed | |

**Flake note.** On the final head, selftest shard 1 failed `TransformKeyframes_OpacityLoopStarted`/
`…Retained` once and passed on re-run. The same fixture passed in both shards on `5efa0c72`, which
has identical runtime code. It is the timing flake recorded at the phase 1 gate (a composition
animation controller not yet present after one render), and phase 2a doesn't touch transforms. It is
seen twice now, so it's worth a fix of its own, outside this spec.

## New guards

| Guard | Proves | Checked by |
|---|---|---|
| `ReactorComponentHookAnchorTests` (4) | `REACTOR_HOOKS_001`, `LIFECYCLE_002`, `THREAD_002` and `PERSIST_001` fire on hooks declared on `ReactorComponent`, with stubs of the real hierarchy | all 4 fail with the four analyzers reverted (run in a scratch harness on macOS) |
| `PersistedMemoryPressureSourceTests` (5) | `Default` initializes; a scope registers when the source arrives, trims on pressure and unsubscribes on dispose; a disposed scope never registers; the reconciler installs WinUI's source | the init-order defect reproduced on the neutral core: `TypeInitializationException` before the fix, `Default` constructs after |
| `NeutralCoreBoundaryTests`: type forwards | `Reactor.dll` forwards exactly the core's exported types minus the two created there; a `Reactor`-qualified name resolves | exact-set equality |
| `NeutralCoreBoundaryTests`: bases | `Component : ReactorComponent`, both in the right assemblies | |
| `StaticInfoTests` (2) | the source map counts `UseElementRef` (Reactor.dll) as one slot, lists an extension-block WinUI hook, and counts an extension hook that takes the context | generated tables compared before and after the fix: without it, `size` and `c` drop out and slots shift |
| `ApiIndexGeneratorTests` | moved types keep their blocks, extension hooks list on `RenderContext`, pickers stay out of `## Hooks` | generator without the core lookup drops `PersistedScope` and `Context<T>` lines |
| `ExtensionBlockDocTests` (2) | extension-block doc ids map back to `RenderContext` crefs | mutation-checked |
| `AdvancedInternalSurfaceTests` | Reactor.Advanced reaches exactly 4 reviewed core internals | exact-set allowlist |

## Source compatibility

- **API index:** +10 lines, all additive: `ReactorComponent` and `ReactorElement` get their own
  blocks. The `RenderContext` block already listed the WinUI hooks, and still does, now as
  extension members. Nothing is removed or changed.
- **Whole solution compiles unchanged:** every sample, the selftest host, ToolkitCompatibility and
  the docs snippets.
- **Edges a recompile can see** (CHANGELOG, spec §10):
  - A WinUI hook called on a context whose type the file never names needs
    `using Microsoft.UI.Reactor.Core;` (CS1061). Every sample already has it.
  - `nameof(RenderContext.UseWindow)` gives CS8093.
  - `cref="RenderContext.UseWindow"` and `cref="Component.UseState"` give CS1574.
- **Binary:** a library compiled against an earlier `Reactor.dll` that calls a WinUI hook needs a
  rebuild. Types are forwarded, members are not.

## Performance

Rule (phase 0): a timing regression only if flagged on two independent `/perf` runs, or mean
> +5% with a CI excluding 0; allocations > 1% with a CI excluding 0.

**`/perf` against `main`** (run #1 on `e814a3e0`, run #2 on the final head; runtime code identical):

| Leg | Run #1 | Run #2 |
|---|---|---|
| StocksGrid 50%: reconcile, alloc | +1.7% [−5.3, +8.8], −0.6% | +4.7% [−0.7, +10.1], +1.7% [−0.2, +3.5] |
| **Skip floor 0%: reconcile, diff** | **+6.3% [+0.2, +12.4], +7.5% [+0.9, +14.2]** | **+5.6% [−0.1, +11.3], +6.8% [+0.8, +12.8]** |
| KeyedList: reconcile, alloc | −0.9%, +0.4% [+0.2, +0.6] | +0.8%, +0.1% |
| Flex, DataGrid: reconcile | within noise | within noise |
| Keyed micro-benches KX1–KX4 (ns) | +0.6 / +5.1 / +2.3 / +0.7% | +1.6 / +1.8 / +1.6 / +0.6% |
| `M7` Update_NoChange (ns) | +3.1% [+2.1, +4.1] | +4.3% [+2.0, +6.7] |
| Micro-suite allocations | none flagged | none flagged |

By the rule, the skip floor reads as a regression on both runs. Phase 2a changes nothing on that
path: the per-cell work is the reconciler's update walk, where this phase's only edits are a
one-time `EnsureInstalled()` and a rename. So it was isolated rather than accepted. The
**isolation runs** use the same harness, executables and paired interleaving as `/perf`'s floor
leg, with 30 reps instead of 12, on separate runners (scratch workflow on branch
`perf/isolate-2a`):

| StocksGrid at 0%, 30 paired reps | Reconcile | Diff | Alloc |
|---|---|---|---|
| A/A control (phase 2a vs itself) | +0.3% [−2.6, +3.2] | +0.5% [−2.4, +3.4] | −0.1% |
| main → phase 1 | +0.7% [−2.8, +4.2] | +0.8% [−2.9, +4.5] | +0.2% |
| main → phase 2a | +0.5% [−2.2, +3.3] | +1.3% [−1.7, +4.4] | +0.2% |
| phase 1 → phase 2a | −2.1% [−4.5, +0.3] | −0.9% [−3.4, +1.6] | +0.1% |

At 2.5× the sample size, none of the three comparisons resolves a change, and the control shows
±3%, matching phase 0's A/A. The `/perf` floor leg's +6–7% doesn't reproduce. A difference in
method could still explain it: `/perf` runs the floor leg after 14 pairs of the 50% leg, and the
isolation runs it first. The isolation shows that phase 2a adds nothing at the skip floor. It
doesn't explain the `/perf` reading. Absolute reconcile times varied from 20.9 to 36.2 ms between
runners, so only paired deltas on one runner mean anything.

**Micro-suite isolation** (`PerfBench.ControlModel`, 30 paired reps):

| Bench (ns) | A/A control | phase 1 → phase 2a |
|---|---|---|
| `M7` Update_NoChange | +0.2% [−0.7, +1.1] | **+0.2% [−0.2, +0.6]** |
| Keyed `KX1`–`KX4` | +0.3 / −0.3 / +0.3 / +0.1% | +0.4 / +1.2 / +1.0 / +0.3%, all within noise |
| `M12` Pool_Rent_HotPath | −0.4% [−1.5, +0.7] | +1.1% [+0.1, +2.0] |
| `M14` Dsl_Rebuild_Cascade | +0.2% [−0.5, +1.0] | +1.8% [+0.8, +2.8] |
| `M13` Setters_Suppression_Scope | +0.9% [−4.8, +6.7] | +8.3% [0.0, +16.6] |
| Allocations, every bench | 0.0% | 0.0% |

So M7's +3–4% against `main` doesn't come from this phase; phase 1's final head read +0.9% on it.
Three benches read small but consistent increases. M14 rebuilds element trees (no hooks), and it also
read +0.7% and +2.7% on `/perf`. M12 mounts and unmounts a pooled `TextBlock`. This phase edited
neither path. A layout shift in a 69 KB smaller `Reactor.dll` is a plausible cause, but not a
demonstrated one. M13's
interval is as wide as its control's. All three are below the +5% timing threshold and allocate
the same; they are recorded to re-measure, not explained away.

## Coverage and size

- Coverage (unit + selftest, against `main`): line 88.70% → 88.72%, branch 45.68% → 45.95%.
- `Reactor.dll` 2.82 MB → 2.75 MB (−69 KB) and the framework nupkg −31.6 KB against phase 1, since
  the moved code now ships in the core package.

## Review

`pr-review` ran 7 dimensions on the default model (Claude), plus a GPT-family cross-check
(gpt-6.1-sol, blind scan then reconciliation). It found:

- **1 critical,** confirmed cross-family (raised independently by correctness and the
  cross-check). `ApplicationPersistedScope`'s static fields were initialized after `Default`,
  whose constructor locks one of them. The type initializer faulted, so every app crashed at
  launch. CI's first run on that head showed it too: 59 unit tests, every selftest shard, E2E and
  bootstrap.
- **5 high, all confirmed cross-family:**
  - Four analyzers stopped recognising hooks declared on `ReactorComponent`, so conditional-hook,
    effect-cleanup, blocking-task and persisted-scope diagnostics went silent in class components.
  - Tests pinned to moved code: the Advanced internals allowlist, two source-scanning tests and
    the reviewer manifest.
- **Also fixed:** the picker methods leaking into the API index's `## Hooks`, a changed exception
  message, stale contributor docs, and the core package README.

CI then found what review missed. The source-map generator recognised hooks by declaring type
and assembly too: class components lost their hook tables, and WinUI hooks dropped out of the slot
numbering. Roslyn reports an extension-block call as declared on the block, with no `RenderContext`
parameter, a shape the generator had never seen. The swallowed-error ledger also needed its
`RenderContext.cs` section split per file.

A second cross-family review of the fixes (gpt-6.1-sol) found two more medium issues, both fixed:

- an extension hook on another receiver that takes the context was dropped;
- the memory-pressure tests could leave `Default` subscribed to a fake source.

Three design suggestions are deferred to the phases where MAUI plugs in, recorded in spec §10 "Seam
follow-ups":

- a per-owner dispatcher instead of the process-wide one;
- push-model memory pressure;
- a narrow slot-claim primitive for renderer hooks.

## Decisions for the gate

1. **The skip-floor reading.** `/perf` flags it twice against `main`. 30-rep isolation shows no
   change from either phase, with a ±3% control. Recommendation: treat it as unresolved rather
   than waived, and take it as a standing item. Re-measure the floor leg at 30 reps at every
   step-2 gate, and look into the `/perf` leg ordering (floor after the 50% leg) as a source of
   bias, since it affects every PR's numbers, not just this one.
2. **The `using Microsoft.UI.Reactor.Core` edge** for WinUI hooks on an unnamed context. Accept,
   documented. The alternative, a global using in the WinUI package's props, would put every
   `Microsoft.UI.Reactor.Core` name in scope in every consumer file.
3. **The binary break** for libraries calling WinUI hooks: accept, documented. Members can't be
   forwarded, and `RenderContext` in the core can't name WinUI types.
4. **The seam follow-ups** (per-owner dispatcher, push-model memory pressure, slot-claim primitive):
   defer to 2b–3 as recorded, or do the per-owner dispatcher now, before MAUI depends on the
   static. Recommendation: defer. It is behavior-preserving today, and MAUI's real needs will shape
   the API better.
