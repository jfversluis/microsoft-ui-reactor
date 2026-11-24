# Phase 2c gate: the handler protocol (spec 067)

Phase 2c was planned to make the handler protocol neutral. Mapped against both renderers, that
work is better done in phase 3, with MAUI as a second consumer (spec 067 §12). This phase delivers
that analysis and the fix the phase 2b gate asked for. PRs: #5 (the fix, stacked on #4) and #6
(the analysis, stacked on #5).

## The deep-tree re-entrancy fix (PR #5)

**Bug.** A state change in a component nested more than 50 components deep threw "Render loop
detected" with no loop. Each component's re-render callback called its parent's, and each call
counted toward TASK-059's cap of 50.

**Reproduced first.** The final fixture, run on unchanged code (branch `ci/depthfix-baseline`, CI
run 45473268469), failed exactly as predicted, in the unpackaged, packaged and NativeAOT tiers:

- a state change 201 components deep threw "Render loop detected";
- a real synchronous loop 201 components deep was stopped before its first request reached the
  host (`requests=0`, instead of 50).

**Fix.** The callback is now a small class. The innermost one walks its ancestors' callbacks,
marking each self-triggered as before, and calls the host once. A request is counted once, climbs
on a constant stack, and is checked against the cap on the thread that does the work.

**Review shaped it.** The first version marked a hand-off and let each parent callback skip
counting it. A cross-family review (gpt-6.1-sol) found two high issues in it:

- a real loop deep in a tree still recursed through every ancestor, so it could exhaust the stack
  before the cap fired;
- a request marshalled to the UI thread was counted without being checked.

The iterative climb fixes both. A second pass found one medium issue: a multicast parent callback
(`before + ctx.RequestRerender` in a custom handler) reports its last target, so the climb skipped
`before`. Each link now keeps its exact callback and the climb compares by reference. Both
reviews then confirmed the fixture's arithmetic and found nothing further.

**Guards** (`Lifecycle_RerenderDepthCap`, replacing the check that pinned the bug):

| Check | Proves | Fails on |
|---|---|---|
| `LoopHitsTheCap`, `LoopCountsRequestsNotWrappers` | a real loop 201 deep still stops, after exactly 50 requests | the old counting (0 requests); a recursive climb (stack overflow) |
| `MulticastParentIsInvoked` | a non-component parent callback runs in full | recognising a link by `Delegate.Target` |
| `DeepChainIsNoLoop`, `DeepChainRenders` | a change 201 deep renders, after the loop threw | the old counting; a counter the throw didn't release |

**CI** on `9e85a6e0` (run 45474045283): every tier green on the first attempt. No unit test is
added or removed; the selftest count is unchanged (one fixture, rewritten).

**`/perf`** on `9e85a6e` against `main` (workflow run 45474054133): every timing and allocation
row within noise, for example StocksGrid reconcile −1.1% [−3.9, +1.8], skip floor +1.6%
[−4.0, +7.2], KeyedList +1.1% [−1.3, +3.4], allocations within ±0.3%, and the whole micro-suite
(`M7` +0.7% [−2.1, +3.4]). The only flag is StocksGrid working-set memory, +0.9% [+0.5, +1.3],
which isn't a gate metric. The callback still allocates one object and one delegate per
component, as the closure did, and the climb adds no allocation per ancestor.

## The protocol analysis (PR #6, spec 067 §12)

**Findings:**

- The public protocol is WinUI-shaped in its signatures (contexts expose `Reconciler`, `UIElement`,
  pooling and routed events; descriptors take `DependencyProperty` and `FrameworkElement`).
  Moving the contexts to the core breaks compiled handlers that call their members. §1 accepts
  binary breaks, so that is a cost, not a bar.
- The renderers differ in structure: WinUI's strategies drive incremental child mounting, MAUI
  collects flattened natives and syncs them; WinUI's registry is global and first-wins, MAUI's
  per root and last-wins (its hybrid set relies on that); echo and lifecycle ordering differ too.
- MAUI uses none of WinUI's protocol yet, so anything extracted now would be designed from one
  consumer.

**Options** (§12.2): A, move the public types; A′, a new core contract generic over the context;
B, extract the descriptor interpreter now; B-lite, extract neutral decision helpers now; C,
extract each piece when MAUI adopts it in phase 3. A cross-family review (gpt-6.1-sol) agreed
with C and added A′, B-lite, the phase 3 gates and inventory corrections, all applied.

**Tooling (2d)** follows the same rule: what remains (MAUI analyzer coverage, a MAUI wrapper
generator) has no consumer until MAUI is on the core.

## Decisions for the gate

1. **Accept option C:** no protocol extraction in phase 2; each piece moves in phase 3 with MAUI
   as its second consumer, through the gates in §12.3. WinUI's public protocol types are
   unchanged until a phase 3 gate decides A or A′. Recommendation: accept.
2. **Fold 2d into phases 3–4**, so phase 3 is next: re-host MAUI's components and hooks on the
   core first, then the protocol pieces. Recommendation: accept.
3. **Merge PR #5's fix** with this gate. Recommendation: accept.
