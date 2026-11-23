# Phase 1 gate: shared-core design and extraction spike (spec 067)

Phase 1 delivers the design for one Reactor core shared by the WinUI and MAUI renderers
(spec 067), plus a spike that moves the first real slice of the WinUI engine into it. The slice is
element identity (`ReactorElement.Key`) and the keyed child-reconciliation engine. Measured
against the phase 0 baselines (`2026-11-23-phase-0-baselines.md`) on PR #2 of the fork.

## What changed

- **`Microsoft.UI.Reactor.Core`** (`src/Reactor.Core`, `net10.0`, its own package, no UI framework
  references): `ReactorElement`, `ChildSlots` and `ChildReconciler<TElement, TNative, THost>`. The
  host is a struct type parameter, so the JIT specializes WinUI's instantiation.
- **WinUI**: `Element : ReactorElement`; `ChildReconciler` is a façade over the engine with a
  `WinUIChildHost`. `Microsoft.UI.Reactor` depends on exactly its own core version (`[V]`).
- **MAUI**: spec 065's core is renamed to a transitional `Microsoft.UI.Reactor.Maui.Core`, which
  phase 3 retires by re-hosting MAUI on the shared core.
- **Packaging and tooling**: release workflow, internal pipeline (build, sign, pack), `mur
  pack-local`/`clean-local`, build metrics, coverage, integration-test feed, the toolkit consumer
  harness and bootstrap all carry the core. The API index keeps `Element.Key`.

## WinUI test tiers (PR #2, CI run 45170690659, head `8573853f`)

| Tier | Phase 0 baseline | Phase 1 | Notes |
|---|--:|--:|---|
| Unit tests (all projects) | 16,574 passed, 23 skipped | 16,586 passed, 22 skipped, 0 failed | +11 new tests; the transforms-ledger PR check now has a base to diff and runs (passes) |
| Selftests (2 shards) | 1,149 + 1,149 passed | 1,149 + 1,149 passed | unchanged |
| Packaged selftests (MSIX, 2 shards) | 1,138 + 1,139 passed | 1,138 + 1,139 passed | see flake note |
| E2E, winapp ui (2 shards) | 43 + 232 passed | 43 + 232 passed | no retries needed |
| AOT selftests, AOT trim proof | passed | passed | |
| Integration tests | 7 | 7 | feed now packs the core |
| Docs build (snippet freshness, 497 pipeline tests) | 497 | 497 | 5 pages regenerated |
| Build solution (Release) | passed | passed | |
| Package (release.yml), Bootstrap | passed | passed | Bootstrap asserts the core nupkg |
| MAUI: core, headless, device (Mac Catalyst, iOS) | 44, 34, 25 + 25 | 44, 34, 25 + 25 | run 45175046249; renamed core project |

**Flake note.** On the first push (`d71a0c9e`), packaged selftest shard 2 failed
`TransformKeyframes_OpacityLoopStarted`/`…Retained` (a composition animation controller not yet
present after one render). The same fixture passed in both unpackaged shards of that run and in
both packaged shards of the next run; the spike doesn't touch transforms or keyframes. It's
recorded as a timing flake to watch, not waived.

## New guards

| Guard | Proves | Mutation check |
|---|---|---|
| `NeutralCoreBoundaryTests` | the core references only `System.*`; `Element` derives from it; its `InternalsVisibleTo` list is exactly Reactor's plus Reactor | Reactor.dll fails the boundary; dropping one IVT fails parity |
| `NeutralChildReconcilerTests` (6) | the engine runs and diffs correctly through a fake non-WinUI renderer | disabling the keyed path fails 3; disabling the slot walk fails 1 |
| `AdvancedInternalSurfaceTests.Advanced_TouchesNoNeutralCoreInternals` | Reactor.Advanced reaches no core internals | vacuity guards on both indexes |
| `ApiIndexGeneratorTests.PublicTypes_ListNeutralCoreProperties_…` | `Element.Key` stays in `reactor.api.txt`, not repeated on derived types | generator without lifting drops `Key` |
| release.yml "Check the framework pins its core" | the packed nuspec depends on the core at `[V]` | passes on `[V]`, fails on `V` and on a missing dependency |

## Source compatibility

- **API index**: one added line, `Equals(ReactorElement other)` on `Element` (compiler-generated
  record member). Nothing removed or changed.
- **Whole solution** compiles unchanged: every sample, the selftest host, ToolkitCompatibility and
  docs snippets (CI's Release solution build and docs build).
- **One visible edge** (open for this gate, spec 067 §9): `cref="Element.Key"` in a consumer's XML
  doc comment no longer resolves (CS1574, an error only with warnings as errors). 13 crefs in this
  repo moved to `ReactorElement.Key`. Documented in CHANGELOG.

## Performance

Rule (phase 0): a timing regression only if flagged on two independent `/perf` runs, or mean
> +5% with a CI excluding 0; allocations > 1% with a CI excluding 0.

| Leg | `/perf` #1 (head `d71a0c9e`) | `/perf` #2 (head `8573853f`) |
|---|---|---|
| StocksGrid 50% | renders +0.9%, reconcile +0.9%, alloc +0.1%: within noise | PERF2_STOCKS |
| Skip floor 0% | reconcile −0.4%: within noise | PERF2_FLOOR |
| KeyedList (the moved keyed engine) | reconcile +1.7% [−2.1, +5.5], alloc −0.1%: within noise | PERF2_KEYED |
| Flex, DataGrid | within noise | PERF2_OTHER |
| Micro-suite, allocations | no bench flagged | PERF2_MICRO |

All 55 compared rows of run #1 are within noise, and none is flagged. Run #2: PERF2_SUMMARY.

## Coverage and size

- Coverage (core now instrumented): line 88.70% → 88.70%, branch 45.68% → 45.92%.
- Reactor.dll 2.83 MB → 2.82 MB (−8.5 KB). Framework nupkg −10.5 KB; the new core nupkg is
  21.9 KB, so framework + core is +11.2 KB (+0.5%) for a consumer.

## Review

`pr-review` ran 7 dimensions on the default model (Claude) plus a GPT-family cross-check
(gpt-6.1-sol). It found 3 high, 8 medium and 11 low issues. All 3 highs were confirmed cross-family
and fixed: the integration-test feed missing the core, and two stale docs snippets. Every medium
was fixed, and all lows except two design suggestions, which are deferred to spec 067 step 4 and
recorded there: index the core assembly instead of lifting, and a shared IVT props file.

## Decisions for the gate

1. **Accept option B** (retained node tree, anchors optional) as the anchor strategy, to be
   confirmed by the WinUI node-tree prototype that opens step 2 (spec 067 §5).
2. **Accept the `cref="Element.Key"` edge**, or require a forwarding `Key` on `Element` (which
   duplicates `Key` in record `ToString()`).
3. **Separate `Microsoft.UI.Reactor.Core` package** pinned at `[V]` (versus bundling into the
   framework's `lib/`; spec 067 §9 records why).
