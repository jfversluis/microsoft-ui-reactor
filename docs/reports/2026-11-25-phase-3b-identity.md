# Phase 3b gate: one child diff, and a renderer conformance suite (spec 067 §13.6–13.7)

Phase 3b settles the identity contract of spec 067 §11.3 by sharing the algorithm. MAUI's engine
now reconciles children through the core's `ChildReconciler`, the diff WinUI uses. One table of
conformance scenarios runs on both renderers. The contracts that stay renderer policy (recovery,
effect timing, invalidation) are written down. PR #8, stacked on #7.

## What changed

- **MAUI's engine uses the core's child diff** (`ReactorRoot.ChildHost`):
  - Keyed children match by key, with the minimal moves for a reorder.
  - Unkeyed children match by slot, so a conditional child doesn't shift its siblings.
  - An unkeyed child among keyed ones matches by absolute position, which is WinUI's rule.
    MAUI used to keep its state when it moved past a keyed sibling; now it remounts, as on WinUI.
  - A slotted host (a navigation stack's pages, then its modals) still gets one group per declared
    child, including null and empty ones.
  - Teardown failures raised while a later mount throws are reported (to `UnhandledException`, or
    traced) instead of being lost.
- **Conformance suite.** `tests/_shared/RendererConformanceScenarios.cs` is compiled into MAUI's
  engine tests (`RendererConformanceTests`) and WinUI's selftest hosts (`Conformance_Identity`,
  `Conformance_Lifecycle`). Each project supplies five element factories and a runner for its own
  host. The suite covers:
  - five identity scenarios, each checked before and after the re-arrangement;
  - two lifecycle scenarios (effect dependencies and cleanup; error-boundary fallback, cleanup and
    retry), checked per phase.
- **Renderer policy, documented** (§13.7): unhandled-error recovery, effect timing, duplicate keys
  and render counts differ between the renderers, with the reason for each.
- **No WinUI product code changed.** The diff touches `src/Reactor.Maui`, tests and docs only.

## Tests

| Tier | Before (3a head) | Phase 3b | Notes |
|---|--:|--:|---|
| MAUI engine (headless) | 50 | 62 | +7 conformance cases, +5 child-diff tests |
| MAUI renderer (headless) | 34 | 34 | |
| MAUI device, Mac Catalyst | 25 | 25 | |
| MAUI device, iPhone 18 Pro simulator (iOS 27) | 25 | 25 | |
| MAUI device, Android API 37 emulator | 25 | 25 | |
| MAUI Release build (Android, iOS, Mac Catalyst) | 0 warnings | 0 warnings | |
| WinUI selftests | green | green, +2 fixtures | `Conformance_Identity` (10 checks) and `Conformance_Lifecycle` (14 checks) pass, including under NativeAOT; CI run 45552484455 |
| WinUI unit, packaged, E2E, AOT, integration, docs, Release build | green | green | CI run 45552484455 |

**Identity change, reproduced first.** Run on the 3a engine, the shared table fails only
`AnUnkeyedChildMovedPastAKeyedOne_IsANewChild` (MAUI kept the state; WinUI remounts).

**Mutation checks.** Each new behavior has a test that fails without it:

| Mutation | Fails |
|---|---|
| slots built from mounted nodes, not declared children | `ANullOrEmptyChildOfASlottedHost_KeepsItsSlot` |
| teardown errors dropped when a mount throws | `ACleanupThatFails_IsReported_WhenTheReplacementsMountFailsToo` |
| the conformance runner never sets state | all 5 identity scenarios (their `Before` checks) |

## Performance

**MAUI benchmark** (Mac Catalyst, Release, 1,000 keyed rows, median of 7 per run). The 3a head and
this branch were built and run alternately on one machine, 3 runs each:

| Scenario | Controls: time, alloc | Direct: time, alloc | Components (anchor): time, alloc |
|---|---|---|---|
| Mount | −0.4%, 0.0% | −0.3%, 0.0% | −1.0%, +0.1% |
| Update all | −0.5%, −4.3% | −0.5%, −6.9% | +0.2%, −7.3% |
| Update one | −9.6%\*, −26.9% | −10.2%\*, −16.8% | +0.7%, −18.6% |
| Reverse | +0.1%, **+25.1%** | −0.9%, −10.1% | −0.5%, −4.8% |

\* One of three runs; the other two are within noise.

Times are within run-to-run ranges, and the engine allocates less. `Reverse` on the Controls leg
allocates 2.1 MB more on every run, which was traced to where a garbage collection lands:

- In 3a, the one gen0 collection in the measured window falls in the render phase. In 3b, the
  render allocates 423 KB less (7,687 → 7,264 KB), so the collection falls in the layout phase.
  There, MAUI's Controls layout allocates 2.5 MB more on alternate iterations, and the median
  picks those.
- Headless, both versions make the same Controls changes: 999 adds, 999 removes, 1,998 `Parent`
  changes and no measure invalidations. 3b's render allocates 13% less.
- **Causal check:** padding 3b's render with 430 KB of short-lived arrays moves the collection back
  into render (all 7 samples). Layout then allocates 832 KB, the same as 3a, and the total is
  8,535 KB against 3a's 8,520.

This isn't a regression in the engine's work. It's recorded as a measurement artifact: totals that
include a collection depend on where it lands.

**WinUI:** no product code changed, so `/perf` was not run for this phase. The WinUI tiers in CI
cover the new fixtures.

## Review

`pr-review`: 7 dimensions on the default model (Claude), plus a GPT-family cross-check
(gpt-6.1-sol, blind scan then reconciliation).

- **3 high, all confirmed cross-family and fixed:**
  - A slotted host lost the slot of a null or empty child, so a navigation stack could mistake a
    page for a modal.
  - Teardown errors collected before a later mount threw were lost.
  - The WinUI lifecycle fixture counted the cleanups that host disposal runs, which MAUI's runner
    never observes; every check now happens before disposal.
- **Medium, fixed:**
  - The scenarios were duplicated between the two suites; they're now one shared table.
  - Identity scenarios didn't check that their state landed (now `Before`).
  - Node kinds are decided in one place (`KindOf`), used by mount, the root and the diff.
  - New tests for a dirty unchanged child, several failed cleanups, and a failing reorder under a
    boundary.
- **Low, fixed:** spec and README wording, and the test count in spec 065.
- **Security: clean.**

## Decisions for the gate

1. **MAUI adopts WinUI's identity rule** for an unkeyed child among keyed ones (it remounts when it
   moves past a keyed sibling). This changes MAUI behavior in that case only. Recommendation: accept.
2. **Recovery, effect timing, duplicate keys and render counts stay renderer policy** (§13.7), and
   the suite checks what both guarantee. Recommendation: accept.
3. **The Controls `Reverse` allocation reading is a GC-placement artifact**, per the causal check
   above. Recommendation: accept.
