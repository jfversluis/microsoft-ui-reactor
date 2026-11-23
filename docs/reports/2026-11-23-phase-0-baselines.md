# Phase 0 baselines (Reactor everywhere, spec 067)

Pinned upstream commit: **`14172dce`** (`microsoft/microsoft-ui-reactor` `main`, 2026-11-22). Every
later phase compares against these numbers until the next upstream merge, which re-takes them.

## WinUI test tiers

From a `workflow_dispatch` CI run on the fork's `main` at the pinned commit
([run 45152168185](https://github.com/jfversluis/microsoft-ui-reactor/actions/runs/45152168185)),
GitHub-hosted `windows-latest`. All jobs succeeded.

| Tier (job) | Total | Passed | Skipped | Failed |
|---|--:|--:|--:|--:|
| Unit tests | 16,597 | 16,574 | 23 | 0 |
| Selftests (shard 1, shard 2) | 1,150 + 1,150 | 1,149 + 1,149 | 1 + 1 | 0 |
| Packaged selftests (MSIX, 2 shards) | 1,144 + 1,144 | 1,138 + 1,139 | 6 + 5 | 0 |
| E2E, winapp ui (2 shards) | 43 + 237 | 43 + 232 | 0 + 2 | 0 after retry |
| AOT selftests, AOT trim proof | passed | 5/5 proof checks | | 0 |
| Integration tests | 7 | 7 | 0 | 0 |
| Build solution (Release) | 94 checks | 90 | 4 | 0 |
| Docs build | 497 | 497 | 0 | 0 |

**Flake ledger.** E2E shard 2 failed and then passed on retry for three tests:
`Selection_RoutedInputAndExternalEvents`, `ReleaseUiTurn_IsAcceptedBecauseTheWorkflowIdReachesWinApp` and
`WinAppRejectsAYieldWithNoWorkflowId`. These are pre-existing and are tracked across phases; a test
that fails without passing on retry is a regression.

## Coverage and size

- Merged unit + selftest coverage: **88.70% line, 45.68% branch** (18,380/40,239).
- Package sizes: `Microsoft.UI.Reactor` 2.09 MB, `.Advanced` 528.7 KB, `.Devtools` 287.8 KB;
  `Reactor.dll` 2.83 MB.

## WinUI performance and its noise

`/perf` on PR #1 ([run 45152170075](https://github.com/jfversluis/microsoft-ui-reactor/actions/runs/45152170075))
compared a head with **no WinUI change** against `main`. It is therefore an A/A run, and it shows the
noise:

| Leg | Headline values (`main`) | A/A spread |
|---|---|---|
| StocksGrid 50% | 4.96 renders/s, reconcile 72.7 ms, diff 65.1 ms, 289.4 MB | all within ±2% |
| Skip floor 0% | 14.73 renders/s, reconcile 21.4 ms | within ±3% |
| KeyedList | 15.90 renders/s, reconcile 8.6 ms | **reconcile +3.0% flagged "regression" (CI +0.3..+5.6)** |
| Flex/Yoga, DataGrid | 2.21 and 7.70 renders/s | within ±1% |
| Micro-suite (20 benches) | `M1`…`M10`, `KX1`…`KX4`, `IH1`…`IH6` | individual Δ up to ±7%; `M3` CI excluded 0 (+5.7%) |
| Allocations (all legs) | e.g. 4,876,341 B/render StocksGrid | **0.0% ± <1%**: deterministic |

**Gate rule derived from it.** Timing: a leg regresses only if it is flagged on two independent
`/perf` runs, or its mean Δ is above +5% with a 95% CI that excludes 0. Allocation: any increase above
1% whose CI excludes 0 is a regression.

## MAUI renderer benchmark

`tests/Reactor.Maui.Benchmark/run.sh` (Release; 1,000 keyed rows; median of 7; the two legs
interleaved). Each cell is the time in ms and the allocations in MB (Controls → direct).

| Scenario | Mac Catalyst (macOS 26.7, Apple silicon) | iPhone 18 Pro simulator (iOS 27.0) |
|---|---|---|
| Mount | 3,579 → 1,971 ms; 57.5 → 23.0 MB | 3,451 → 2,430 ms; 44.2 → 14.0 MB |
| Update all | 832 → 338 ms; 11.4 → 7.1 MB | 702 → 480 ms; 6.4 → 3.9 MB |
| Update one | 43.0 → 39.3 ms; 1.8 → **2.9** MB | 69.4 → 68.2 ms; 1.3 → **2.4** MB |
| Reverse | 679 → 591 ms; 8.3 → 4.1 MB | 641 → 545 ms; 8.7 → 4.5 MB |

All samples produced identical layout across the two legs.
- **New lead:** on a one-row update the direct renderer allocates 60–84% *more* than Controls, at the
  same time. To be investigated in phase 3.
- **Android:** the Pixel 10 was not attached for this run. A run on an API 37 emulator was not
  reproducible: the same tree's mount time varied 6× between runs, and the direct leg came out 50%
  slower than Controls, against −27% on the device. The emulator is therefore used for functional runs
  only, and the Android perf baseline is still to be taken on the device.

## Component anchor cost (spec 067 §5)

`REACTOR_BENCH_MODE=anchor`: both legs use the direct renderer, with rows as components; the second
leg wraps each component in a single-child pass-through layout, as a native anchor would.

| Scenario | Mac Catalyst | iPhone simulator |
|---|--:|--:|
| Mount | **+13%** time, +7% alloc | **+16%**, +3% |
| Update all | **+18%**, +16% | **+17%**, −18% |
| Update one | −1% | −22% (noise at ~50 ms) |
| Reverse | **+9%**, +13% | **+12%**, +18% |

Layout was identical in every sample. A native anchor per component costs 9–18% on bulk work, which
is above spec 067's 10% threshold for option A.

## MAUI tiers

| Tier | Result |
|---|---|
| Core headless (spec 065 core) | 44/44 |
| Renderer headless | 34/34 |
| Device tests | 25/25 on Mac Catalyst and the iOS simulator, locally and on the `xcode-27` CI runners; 25/25 on the Pixel 10 before it was detached |

## Infrastructure

- **Fork CI.** Hosted runners run every upstream WinUI tier, including the interactive ones (selftests,
  packaged, E2E). `maui.yml` now runs on `xcode-27` runners and selects the exact Xcode the workload
  pins (27.2).
- **Not running on the fork.** `docs.yml` (Pages deploy) only triggers on guide changes. Scheduled
  workflows are disabled on forks by default.
- **Dev Box: deferred.** Its MCP server is not loaded in this session, and the Azure CLI here is
  signed in to a different tenant. Windows UI evidence comes from hosted runners until the Dev Box
  runner exists. That runner is needed for MAUI on Windows (phase 3) and Appium on Windows (phase 5).
- **Local WinUI type-check loop.** On macOS, `dotnet build src/Reactor -p:EnableWindowsTargeting=true
  -p:EnableDefaultPageItems=false -p:EnableDefaultApplicationDefinition=false` compiles everything
  except the one XAML-generated `InitializeComponent`, so C# errors surface without Windows.
