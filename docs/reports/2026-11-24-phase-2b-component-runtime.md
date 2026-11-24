# Phase 2b gate: the component runtime in the shared core (spec 067)

Phase 2b moves what makes a component a component into `Microsoft.UI.Reactor.Core`: the element
contracts, the component node, the skip gate, the render kernel and the re-render re-entrancy
bound. After this phase, WinUI renders, skips, retries and recovers class, function and memo
components through core code that a MAUI renderer can call. Results are measured on PR #4 of the
fork, against the phase 2a gate (`2026-11-24-phase-2a-foundations.md`). Design and steps: spec 067
§11.

## Scope change

Spec §4 sketched one generic `Reconciler<TNative>` that owns the tree walk for both renderers.
Measured against the code, WinUI and MAUI differ in more than types: WinUI anchors each component on
a `Border` and re-renders from the root with a self-triggered path; MAUI keeps a flattened node tree
and a dirty-node queue; context is a traversal scope in one and provider subscriptions in the other;
effects run inline after each render in WinUI and deepest-first after the commit in MAUI. Replacing
WinUI's walk would rewrite about 14.5k lines under a no-behavior-change rule.

2b therefore shares the **per-component runtime** and leaves the **walk** to each renderer (§11.1).
The generic reconciler of §4 and the retained-node prototype of §5 are superseded. A cross-family
design review (gpt-6.1-sol) endorsed the split before any code moved, and its findings set the
limits in §11.2 and the phase 3 contracts in §11.3.

## What changed

- **Moved into the core** (`src/Reactor.Core/Engine/`, all `internal`, reached through the core's
  `InternalsVisibleTo` list):
  - **Element contracts:** `IComponentElement`, `IRenderFunctionElement`, `IMemoElement`, and the
    props contracts `IPropsReceiver` and `IPropsComparable` (moved from WinUI unchanged).
    `ComponentElement` satisfies its contract through its existing `Props`; `FuncElement` and
    `MemoElement` implement theirs explicitly. No public member changes.
  - **`ComponentNode`**, typed to `ReactorComponent`/`ReactorElement`. WinUI's nested node derives
    from it and narrows the types.
  - **`ComponentGate`:** the skip decision (class `ShouldUpdate`, memo dependencies, consumed
    context) with its trace reason, and the skip path's props refresh (issue #151).
  - **`ComponentRenderKernel`** with explicit mount and update modes. It runs `BeginRender`, the
    render, the output transformation and the effect flush, retries a hook-order change during a
    hot-reload pass (update only), and classifies failures. WinUI supplies the per-site policy
    through a struct host (`WinUIComponentRenderHost`): log messages, trace reasons, the in-tree
    fallback, inline effects.
  - **`RerenderReentrancy`**, the TASK-059 bound.
- **Stays in WinUI:** anchors and the tree walk, error-boundary nodes, element equality, modifiers,
  pooling, the render-loop flags, the invalidation rule of the re-render wrapper, and (for now) the
  diff hints and memo-cell hooks.
- **Steps dropped or deferred** (§11.4): the render-loop gate (a flag and a compare-exchange
  interleaved with host-only work; extracting it gives MAUI nothing), and the diff hints with the
  memo-cell caches (their only producer is `UseMemoCellsByIndex`; they move with their first MAUI
  user, the virtualized grids).
- **Docs:** spec §11 (as built), the core README, a CHANGELOG entry under Changed. No public API
  or behavior change: the API index regenerates unchanged.

## WinUI test tiers (PR #4, CI run 45444343752 attempt 3, head `57a61e38`)

| Tier | Phase 2a final head (`8c458598`) | Phase 2b | Notes |
|---|--:|--:|---|
| Unit tests (all projects) | 16,599 passed, 22 skipped | 16,616 passed, 22 skipped, 0 failed | +17: neutral gate and kernel tests |
| Selftests (2 shards) | 1,150 + 1,150 | 1,153 + 1,152 | +5 lifecycle fixtures |
| Packaged selftests (MSIX, 2 shards) | 1,144 + 1,144 | 1,147 + 1,146 | +5 lifecycle fixtures |
| E2E, winapp ui (2 shards) | 43 + 234 | 43 + 234 | shard 2's built-in negative control fails as designed |
| AOT selftests (2 shards), AOT trim proof | passed | passed | |
| Integration tests | 7 | 7 | |
| Docs build, snippet analyzer, fuzz smoke | passed | passed | |
| Build solution (Release), vulnerable packages | passed | passed | |

**Flakes.** The final head needed two re-runs of AOT shard 2, each failing a different check, and
both passed elsewhere on the same head:

- `TransformKeyframes_OpacityLoopStarted`/`…Retained`: the composition-animation timing flake from
  the phase 1 and 2a gates. It is now seen 5 times, including twice on runs of unchanged code
  (baseline branch). It needs its own fix, outside this spec.
- `MdSelection_ChildUnmountUpdatesMembership`, once. It passed in the unpackaged and packaged tiers
  of the same run and on the re-run. The membership update comes from the marker's `Unloaded`
  handler, which walks `VisualTreeHelper.GetParent` to find its scope; `Unloaded` is asynchronous,
  so whether the marker is still attached when it fires is timing-dependent. That path has no
  component-runtime code in it. Recorded as a new flake candidate, not investigated further here.

## New guards

| Guard | Proves | Checked by |
|---|---|---|
| `ComponentLifecycleCharacterization` goldens (`Lifecycle_Kinds`, `_Errors`, `_HotReload`, `_Rerender`) | one ordered log per step (renders, effects, cleanups, rendered output, host warnings and errors, component events), untraced and traced, equals the log of the unchanged reconciler: 21 steps × 2 modes, 42 entries | captured on the unchanged code (CI run 45433432782): unpackaged, packaged and NativeAOT gave identical logs; a separate run on unchanged code (45437043605) passed against them; then they ran unchanged at every step |
| `Lifecycle_RerenderDepthCap` | the re-entrancy bound reaches the real re-render wrapper: a state change 60 components deep hits the cap, one 10 deep renders | passes on unchanged code (baseline run 45442341075) and on the PR |
| `NeutralComponentGateTests` (7 methods, 8 cases) | the gate's decision and reason for class, memo and context cases, the `ShouldUpdate` evaluation order (always asked, before the context check), the #151 props refresh on skip | mutation-checked |
| `NeutralComponentRenderKernelTests` (9) | mount and update modes: where props are installed, the hot-reload retry (update only, once, and a retry that fails again), boundary rethrow, fatal rethrow, in-tree fallback, `OnUnhandled` | mutation-checked |

## Source compatibility

- **API index:** unchanged (regenerated and compared).
- **Public surface:** none added or removed. The moved interfaces were already internal.
- **Whole solution compiles unchanged**, including the samples, the selftest host and the
  ToolkitCompatibility checks.
- `Reactor.dll` is 1.5 KB smaller; the other packaged assemblies are unchanged within a few bytes.

## Performance

Rule (phase 0): a timing regression only if flagged on two independent `/perf` runs, or mean
> +5% with a CI excluding 0; allocations > 1% with a CI excluding 0.

**`/perf` against `main`** (two runs on `57a61e3`, workflow runs 45444433073 and 45457866307):

| Leg | Run #1 | Run #2 |
|---|---|---|
| StocksGrid 50%: reconcile, alloc | +1.6% [−2.0, +5.2], −1.0% | +0.2% [−2.5, +3.0], +0.2% |
| Skip floor 0%: reconcile, diff | +4.9% [−0.9, +10.8], +6.2% [−0.1, +12.4] | −1.7% [−6.0, +2.6], −0.4% [−5.3, +4.6] |
| KeyedList: reconcile, alloc | +1.4%, **+0.5% [+0.2, +0.7]** | +0.2%, +0.1% [−0.3, +0.5] |
| Flex: reconcile, alloc | −0.6%, −0.5% | −1.7%, +0.3% |
| DataGrid: reconcile, alloc | +2.2%, +0.3% | −0.2%, −0.3% |
| Keyed micro-benches KX1–KX4 (ns) | +0.5 / +0.5 / +0.1 / −0.3% | +1.5 / +1.1 / +0.8 / +0.9% |
| `M7` Update_NoChange (ns, alloc) | +2.8% [+2.4, +3.2], +2.5% [−6.2, +11.2] | +7.4% [−9.2, +23.9], +5.9% [−2.4, +14.2] |
| Memory rows flagged by the workflow | KeyedList +0.9%, Flex +1.7% | skip floor +0.5% |

Nothing is flagged on both runs, and no timing or allocation row meets the rule. KeyedList's
+0.5% allocation reading is below the 1% threshold, didn't repeat, and matches phase 2a's run #1
(+0.4% [+0.2, +0.6]). Working-set memory isn't a gate metric, and the three memory flags don't
repeat across runs. The skip floor's run #1 reading against `main` (+5–6%, CIs crossing 0)
repeats phase 2a's pattern and is gone on run #2.

**Isolation against phase 2a** (30 paired reps, same harness, scratch branch `perf/isolate-2b`,
run 45444536576):

| Leg | Phase 2a → phase 2b |
|---|---|
| Skip floor: reconcile, diff | +1.4% [−1.7, +4.4], +1.7% [−1.5, +4.8] |
| Skip floor: renders/sec, alloc, Gen0 | +1.0% [−0.8, +2.7], +0.1% [−0.4, +0.5], +0.6% |
| Micro-suite timing, every bench | within noise; largest `IH2` +5.9% [−4.2, +16.0], `OUpdate` +2.3% [−1.1, +5.7] |
| `M7` Update_NoChange | ns −0.3% [−0.6, 0.0]; **alloc +4.7% [+0.4, +9.0]** |
| `M12`, `M14` (phase 2a's small increases) | −1.1% [−2.0, −0.3], −1.0% [−2.2, +0.1] |

**The `M7` allocation reading is not a regression.** It is the only row over the rule, so it was
taken apart. `M7` updates 1,000 `TextBlock` elements against themselves: no component, so none of
this phase's code is on its path. Its allocation figure is the thread's allocated bytes over a
repetition, which includes mounting the 1,000-control fixture, and it is two-valued: every one of
the 64 repetitions allocated exactly 740,208 or 904,152 bytes, on both builds. Only the mix
differs (the larger value in 9 of 32 repetitions on phase 2a, 16 of 32 on phase 2b; Fisher exact
p = 0.12). The paired-CI method treats a two-valued metric as continuous, which is why it reports
an interval that excludes 0. `/perf`'s two runs read `M7` allocations at +2.5% and +5.9%, both with
CIs crossing 0.

**Standing item from phase 2a** (re-measure the skip floor at 30 reps at every step-2 gate): done,
no change. The `/perf` floor leg's ordering question stays open.

## Coverage and size

- Coverage (unit + selftest, against `main`): line 88.70% → 88.75%, branch 45.68% → 46.18%.
  Against phase 2a's gate (88.72%, 45.95%) the new guards add +0.03 pp line and +0.23 pp branch.
- `Reactor.dll` 2.75 MB, −1.5 KB against phase 2a; the framework nupkg −881 B.

## Review

`pr-review` ran 7 dimensions on the default model (Claude), plus a GPT-family cross-check
(gpt-6.1-sol, blind scan then reconciliation).

- **0 critical, 0 high.** The correctness reviewer walked the four rewritten paths (class, function
  and memo mount, and update) against the originals and found them equivalent line by line.
- **Fixed:** the spec as written didn't match what was built (namespace, `internal` rather than
  public, a props-only `IComponentElement`, bypasses applied by the caller, the dropped items not
  struck, step 0's claims overstated); the access decision for MAUI wasn't recorded; and three
  behaviors had no test: the re-entrancy cap through the real wrapper, a hot-reload retry that
  fails again, and the order in which the gate asks `ShouldUpdate` and checks context. Each now
  has one (above).

## Found along the way (pre-existing, not fixed)

- **The re-entrancy cap fires on deep trees with no loop.** Each component's re-render wrapper
  calls its parent's, and each call counts toward TASK-059's cap of 50. A state change in a
  component nested more than about 50 components deep throws "Render loop detected". The new
  `Lifecycle_RerenderDepthCap` pins the current behavior (throws at 60, not at 10), and it passes
  on unchanged code. Out of scope for a no-behavior-change phase; raised as a decision below.
- **Two flaky selftests** (see Flakes above).

## Decisions for the gate

1. **The scope change** (§11.1): share the per-component runtime, leave the walk to each renderer,
   and retire §4's generic reconciler and §5's prototype. Recommendation: accept. It is what the
   code supports without rewriting WinUI's walk, and MAUI keeps its node tree.
2. **Steps 3 and 4:** drop the render-loop gate; defer the diff hints and memo-cell caches to the
   virtualized-grid phase. Recommendation: accept.
3. **Access:** the contracts stay `internal`; phase 3 adds MAUI's assemblies to the core's
   `InternalsVisibleTo` list and relaxes `NeutralCoreBoundaryTests`. Making them public is the
   alternative once the shape settles. Recommendation: accept.
4. **Effect timing and the other §11.3 contracts** (identity, invalidation, recovery, deferred
   effects, hook count): decide at the phase 3 gate, through the conformance suite.
   Recommendation: accept.
5. **The deep-tree re-entrancy cap.** Options: fix it in a small standalone change (count only
   re-entrant calls, not the ancestor chain), fold it into phase 3 with the invalidation rules, or
   leave it. Recommendation: a standalone fix before phase 3, since MAUI's node tree will have the
   same depth and a real app can reach 50 nested components.
