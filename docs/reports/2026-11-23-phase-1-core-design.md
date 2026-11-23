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

| Leg | #1 (`d71a0c9e`) | #2 (`8573853f`) | #3 (`497b6b2b`) | Final head `46acb9c7`: runs A, B |
|---|---|---|---|---|
| StocksGrid 50%: reconcile, alloc | +0.9%, +0.1% | within noise | +2.9% [−2.8, +8.5], +0.9% | A: within noise · B: −1.7% [−3.6, +0.2], alloc +0.3% |
| Skip floor 0%: reconcile | −0.4% | −2.2% | −1.2% | A: within noise · B: +1.3% [−5.3, +7.9] |
| **KeyedList: reconcile** | +1.7% [−2.1, +5.5] | **+1.8% [+0.5, +3.0] flagged** | +1.3% [−0.3, +2.8] | A: +1.0% [−0.3, +2.3] · B: **+0.1% [−1.5, +1.6]** |
| **Keyed micro-benches KX1/KX3/KX4 (ns)** | +1.2 / +0.8 / +2.1% | +2.0 / +2.0 / +2.6% | +0.8 / +0.9 / +0.9% | A: +3.3 / +2.0 / +2.1% · B: +1.0 / +1.6 / +2.2% (all within noise) |
| Flex, DataGrid | within noise | within noise | DataGrid working set +0.4% flagged; alloc −0.2% | A: **DataGrid reconcile +3.8% [+0.9, +6.7] flagged**, Flex alloc −1.0% · B: DataGrid +0.5% [−3.3, +4.3] |
| Micro-suite allocations | none flagged | none flagged | none flagged | none flagged in either |

**The keyed-path finding.** Runs #1 and #2 agreed on about +1.7% on KeyedList, the leg that
exercises the moved engine, and the keyed micro-benches were slower in all 8 measurements. Under
the rule that counts as noise (flagged once, mean under 5%), but the direction was consistent,
so it was traced instead of waived, using an oracle (spec 067 §4). The same engine source is
compiled non-generically and timed in the same process against the generic build, 500 unchanged
keyed children on a fake host:

| Engine | Generic over oracle |
|---|--:|
| First extraction (`8573853f`) | +14–16% |
| Key helpers non-generic, empty-dirty check inline (**final**) | **+8–10%** |
| Same, plus host per-child members typed `ReactorElement`/`object` (`497b6b2b`) | +12% |

`497b6b2b` first shipped the third row. The cross-family review asked whether the untyped host
was what helped, and the ablation above showed it wasn't, so the final head keeps the typed host
contract (compile-time checked) and only the helper changes. An earlier unpaired figure of +43%
in that commit's message was wrong and is superseded by this table. The remaining gap is
generic-dictionary type lookups that shared code can't avoid; the fake host magnifies it, since
WinUI does far more work per child.

**Gate verdict on the final head (`46acb9c7`, runs A and B).** No leg is flagged on both runs, and
no mean exceeds +5%, so by the phase 0 rule there is no regression: run B has 55 of 55 rows within
noise. Run A flagged DataGrid reconcile (+3.8%), which run B didn't repeat (+0.5%). That leg's
earlier results were +0.2%, +1.8% and +0.7%, and its baseline varied from 58 to 120 ms between
runners. Candidly, though, the keyed micro-benches sit **+1% to +3% (ns) in nearly every run**,
each within noise but consistently positive. That is the residual cost of shared generic code
the oracle shows; a CPU difference this small can't be resolved per run. Decision 4 below
covers it.

**DataGrid working set.** The tool marks DataGrid working set as informational: the workload's
re-render is dispatcher-debounced, and its allocations, the authoritative signal, are −0.2% and
within noise. It doesn't touch the keyed engine. Recorded, not waived; the final runs re-measure it.

## Coverage and size

- Coverage (core now instrumented): line 88.70% → 88.70%, branch 45.68% → 45.92%.
- Reactor.dll 2.83 MB → 2.82 MB (−8.5 KB). Framework nupkg −10.5 KB; the new core nupkg is
  21.9 KB, so framework + core is +11.2 KB (+0.5%) for a consumer.

## Review

`pr-review` ran 7 dimensions on the default model (Claude) plus a GPT-family cross-check
(gpt-6.1-sol). It found 3 high, 8 medium and 11 low issues. All 3 highs were confirmed cross-family
and fixed: the integration-test feed missing the core, and two stale docs snippets. Every medium
was fixed, and all lows except two design suggestions, which are deferred to spec 067 step 4 and
recorded there: index the core assembly instead of lifting, and a shared IVT props file. The
keyed-path fix had its own review, on the default model and cross-family (gpt-6.1-sol). Both found
it correct; the cross-family reviewer's challenge to the mechanism led to the ablation above.

## Decisions for the gate

1. **Accept option B** (retained node tree, anchors optional) as the anchor strategy, to be
   confirmed by the WinUI node-tree prototype that opens step 2 (spec 067 §5).
2. **Accept the `cref="Element.Key"` edge**, or require a forwarding `Key` on `Element` (which
   duplicates `Key` in record `ToString()`).
3. **Separate `Microsoft.UI.Reactor.Core` package** pinned at `[V]` (versus bundling into the
   framework's `lib/`; spec 067 §9 records why).
4. **The residual generic-engine cost.** Either accept it: macro legs within noise, keyed
   micro-benches about +1–3% ns, measured again at every step 2 move. Or remove it entirely by
   compiling the engine per renderer from one source (concrete types, as the oracle does), at the
   cost of shipping engine code in each renderer assembly rather than in the core package.
   Recommendation: accept for now, and decide at step 2, when the full reconciler (not just the
   child diff) shows whether the gap grows.
