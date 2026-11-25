# Phase 3d gate: one handler lookup for both registries (spec 067 §15)

The spec 067 §12.3 registry gate says that state stays per renderer or per root, and that each renderer
owns its duplicate and override policy. What the two registries share is the lookup: an exact type
first, then the nearest base type registered for derived types, with a collectible-aware cache.
Phase 3d makes that lookup one core type, `HandlerTable<TValue>`. PR #10, stacked on #9.

## What changed

- **`Reactor.Core` `HandlerTable<TValue>`.**
  - The lookup is exact type, then the nearest base type registered for derived types.
  - Base-type results are cached, misses included. Collectible types are never cached.
  - Each cache entry is stamped with the registration generation it was resolved under, so a lookup
    that overlaps a registration can't leave a result that later lookups use.
  - An empty-table check uses a volatile flag instead of `ConcurrentDictionary.IsEmpty`, which takes
    every lock when the dictionary is empty.
- **WinUI `ControlRegistry`** keeps first-wins, now on top of the table. Its public surface is
  unchanged. It is reached on every dispatch of a composition primitive, so the cheaper empty check
  speeds up WinUI (see Performance).
- **MAUI `ElementHandlerRegistry`** keeps last-wins, so an app overrides a built-in handler by
  registering its own. It also gains `RegisterForDerivedTypes`, for a family of element records with
  a non-generic base.
- **Unchanged:** WinUI's per-host `V1HandlerRegistry` (single-threaded and on the hot path) and its
  registration latches.
- **Docs:**
  - spec 067 §15;
  - spec 065;
  - a new "custom handlers" section in the MAUI README;
  - the CHANGELOG.

## Tests

| Tier | Before (3c head) | Phase 3d | Notes |
|---|--:|--:|---|
| Neutral core (macOS harness) | 21 | 28 | 7 `NeutralHandlerTableTests` |
| MAUI engine (headless) | 62 | 62 | |
| MAUI renderer (headless) | 39 | 40 | derived-type registration, mount and update |
| MAUI device | 34 | 34 | CI on Mac Catalyst and iOS |
| MAUI Release build | 0 warnings | 0 warnings | |
| WinUI CI (unit incl. `ControlRegistryTests`, Advanced, selftests, packaged, E2E, AOT) | green | green (CI 45579493909, MAUI 45579493849) |

**Mutation checks**:

| Mutation | Fails |
|---|---|
| no cache invalidation on a registration for derived types | `ABaseRegisteredAfterAMissOrAHit_IsFoundOnTheNextLookup` |
| bases consulted before exact registrations | 2 tests |
| cached entries accepted regardless of generation | `ALookupThatOverlapsARegistration_LeavesNoStaleCacheEntry` (stale miss, stale hit) |
| collectible types cached | `ACollectibleTypesLookups_AreNotCached_SoItsLoadContextCanUnload` |

The race test is deterministic. An internal hook registers a new base after a lookup's walk and
before it caches what it found. The collectible test emits a `RunAndCollect` assembly and checks,
through a `WeakReference` to an emitted type, that the type is collected. Its first version held the
reference to the `AssemblyBuilder` wrapper instead and passed against the mutant. It was caught and
fixed during the mutation check.

## Performance

**WinUI `/perf`** against `main`, on run 45573698573:

| Leg | Result |
|---|---|
| StocksGrid 50%: reconcile | **−4.5% [−7.2, −1.9]**, an improvement |
| StocksGrid 50%: diff | **−3.3% [−6.1, −0.5]**, an improvement |
| StocksGrid 50%: alloc bytes per render | −0.5% [−1.2, +0.2] |
| StocksGrid 50%: working set | +0.7% [+0.3, +1.2], below the 1% threshold |
| Skip floor, keyed list, flex | within noise |

**MAUI benchmark** (Mac Catalyst, Release, 3c head against this branch, 3 runs each, alternating):
every scenario's time is within run-to-run ranges, and allocations are unchanged. The exception is
Controls `Reverse` allocations, which read +9.5%. That is the GC-placement artifact recorded in the
3b report. Both sides alternate between 8,097 and 10,657 KB per run (base: 1 high and 2 low; this
branch: 2 high and 1 low).

## Review

`pr-review` ran 7 dimensions on the default model (Claude), plus a cross-check on gpt-6.1-sol (a
blind scan, then reconciliation in two rounds).

- **2 high, both confirmed cross-family and fixed:**
  - `PatternAStaticLambdaTests` read the removed `s_entries` field through reflection, which would
    have failed CI. It now reflects `TryResolve`.
  - A lookup overlapping a registration for derived types could re-insert a stale result after the
    cache was cleared. The race existed in WinUI before this PR, and MAUI's new path exposed it too.
    The first fix (publish, then remove) still left a window, which the cross-check found. The
    final fix is generation-stamped entries, which the cross-check confirmed resolved.
- **Medium, fixed:**
  - The `RegisterForDerivedTypes` docs didn't state the non-generic base requirement.
  - The collectible path had no test.
  - The `IsEmpty` fast path took every lock in its common case.
- **Low, fixed:**
  - the miss message;
  - the README section;
  - the nearest-base oracle;
  - the MAUI update path;
  - the scope and dispatch wording in spec 067 §15, and the spec 048 reference.
- **Security and packaging:** nothing beyond the race above.

## Decisions for the gate

1. **Share the lookup, not the policy.** WinUI stays first-wins and process-wide; MAUI stays
   last-wins and per root. Recommendation: accept.
2. **WinUI's per-host `V1HandlerRegistry` keeps its own lookup.** It is single-threaded and lives as
   long as its host. Recommendation: accept.
3. **MAUI gains `RegisterForDerivedTypes`**, which is new public preview API. Recommendation: accept.
