# Reactor on .NET MAUI: proof of concept

> **Experimental.** Nothing here ships, nothing here is in `Reactor.slnx` or CI, and nothing
> under `src/` depends on it. Delete the folder and the repo is unchanged.

This proof of concept answers one question: can Reactor's programming model (components,
hooks, keyed reconciliation) run on a renderer-neutral core, with .NET MAUI as a second
renderer next to WinUI?

It is structured the way a real product would be, so a "yes" doesn't mean a rewrite:

| Project | Target | Role |
|---|---|---|
| `src/Reactor.Core` | `net10.0` | Renderer-neutral core: `Element`, `Component`, hooks, keyed reconciler, batched scheduler, handler contract. No UI framework reference. |
| `src/Reactor.Maui` | `net11.0-android/ios/maccatalyst/windows` | Two MAUI renderers over the same element records, and the `ReactorPage` host. See [Two renderers](#two-renderers-controls-and-direct). |
| `samples/Reactor.Maui.Sample` | MAUI app | Counter, background-timer effect, todo list with keyed rows. Set `REACTOR_RENDERER=direct` to run it on the direct renderer. |
| `benchmarks/Reactor.Maui.Benchmark` | MAUI app | Renders the same tree through both renderers and reports timings and allocations. |
| `tests/Reactor.Core.Tests` | `net10.0` | Headless core tests against an in-memory fake renderer. |

`Reactor.Core` targets `net10.0` so the existing WinUI renderer (`net10.0-windows`) could
consume it too. The MAUI projects target `net11.0` because that is what the MAUI workload
ships for SDK 11.

## Build, test, run

Everything in this folder builds in isolation: its own `Directory.Build.props`/`.targets`
and `Directory.Packages.props` stop MSBuild from picking up the repo's WinUI build logic.

```bash
cd experimental/maui

# Core tests (any OS)
dotnet test tests/Reactor.Core.Tests

# Sample app (needs the maui workload; Mac Catalyst shown)
dotnet build samples/Reactor.Maui.Sample -t:Run -f net11.0-maccatalyst

# Renderer benchmark: build Release, launch, and read the REACTOR_BENCH lines from the
# app's output (stderr on Mac Catalyst, `xcrun simctl launch --console-pty` on iOS,
# `adb logcat` on Android). The results are also shown on screen when the run finishes.
dotnet build benchmarks/Reactor.Maui.Benchmark -c Release -f net11.0-maccatalyst
open --stderr /tmp/bench.log "benchmarks/Reactor.Maui.Benchmark/bin/Release/net11.0-maccatalyst/Reactor Benchmark.app"
```

## Two renderers: Controls and direct

Both renderers consume the same element records and the same `Reactor.Core`; only the
handler registry differs.

- **Controls renderer** (`Handlers/MauiHandlers.cs`). Reactor handlers create and update
  `Microsoft.Maui.Controls` objects (`Label`, `Entry`, `VerticalStackLayout`…), which MAUI's
  platform handlers then render. This is MauiReactor's architecture.
- **Direct renderer** (`Direct/`). Reactor owns small view classes that implement MAUI's Core
  interfaces (`ILabel`, `ITextButton`, `IEntry`, `IStackLayout`) as plain fields. MAUI's stock
  `LabelHandler`, `ButtonHandler`, `EntryHandler` and `LayoutHandler` render them, so there is no
  Controls object, bindable property, binding, style or logical tree involved. This is Comet's
  architecture. Register it with `builder.UseReactorDirectViews()` and pass
  `DirectHandlers.CreateRegistry()` to `ReactorPage`.

The direct renderer covers Label, Button, Entry and the two stacks; every other element falls
back to the Controls renderer in the same tree. Mixing works in both directions because
Controls layouts accept any `IView` child; the one boundary is a Controls slot typed as
`View` (page or scroll view content), where a direct view is hosted in a reused `Grid`. The
sample runs unchanged on either renderer, with identical geometry.

Two bugs found while building it show the cost of skipping Controls: every default Controls
supplies has to be re-derived. Text was larger until the direct views used the font manager's
default size, as Controls does; and on iOS every direct stack was pushed down by the safe-area
inset until it stopped opting in to safe areas, which nested Controls layouts do through an
internal interface (`ISafeAreaView2`).

### Benchmark results

1,000 keyed rows of `HStack(Label, Label, Button)`. Each operation is timed from the Reactor
render through a forced, complete native measure and arrange at the on-screen size. Values are
medians of 7 samples after 2 warm-ups, with the two renderers interleaved. Allocations are
managed bytes on the UI thread. Every sample is validated outside the timer (expected text,
arranged frames, row order and retained native views after Reverse), and both renderers
produced identical layout in every sample.

| Scenario | Pixel 10 (Android 16) | iPhone 18 Pro simulator (iOS 27) | Mac Catalyst (Apple silicon) |
|---|---|---|---|
| Mount 1,000 rows | 1,042 → 764 ms (−27%) | 3,514 → 2,394 ms (−32%) | 4,047 → 2,217 ms (−45%) |
| Update every row | 93 → 46 ms (−50%) | 693 → 478 ms (−31%) | 947 → 393 ms (−58%) |
| Update one row | 8.6 → 7.5 ms (−13%) | 48.8 → 46.4 ms (−5%) | 40.0 → 38.8 ms (−3%) |
| Reverse (keyed) | 160 → 84 ms (−47%) | 636 → 556 ms (−13%) | 713 → 629 ms (−12%) |
| Mount allocations | 42.4 → 10.5 MB (−75%) | 42.3 → 17.4 MB (−59%) | 58.4 → 21.7 MB (−63%) |
| Reverse allocations | 10.6 → 1.4 MB (−87%) | 8.7 → 3.3 MB (−62%) | 8.3 → 2.9 MB (−65%) |

Each cell is Controls → direct. What the numbers say:

- **Skipping Controls is a real win on bulk work**: 27–45% on mount, 31–58% on update-all, and
  60–87% fewer allocations on mount and reorder. Much of it is in layout, where Controls updates
  bindable `X`/`Y`/`Width`/`Height` and raises change events for every arranged view.
- **It doesn't matter for small updates.** Updating one row costs the same on every platform;
  native work dominates.
- **Part of the gap is features, not overhead.** The direct views have no styles, visual states,
  gestures, effects, bindings or semantics. Adding those back costs something.
- **Limits.** The iOS numbers come from a simulator, not a device. Drawing and compositing are not
  timed. Seven samples support rough comparisons, not small percentages.

Getting stable numbers on Android needed two methodology fixes, both in `BenchmarkPage.cs`.
Forced layout must use the real on-screen size: measuring at another size makes the forced and
real passes invalidate each other's native measure caches. And after forcing a .NET GC the
benchmark must wait. On Android every .NET GC also starts a concurrent Java GC, and the UI
thread blocks on it for about 600 ms at its next JNI call. A Perfetto trace attributed that
600 ms to ART's GC thread while the UI thread slept. The same effect hits real apps: a .NET GC
with thousands of live views can stall the next UI update. The runs also logged about 46,000
outstanding JNI global references, which is worth investigating separately.

## What the core owns, and what a renderer supplies

```mermaid
flowchart LR
  subgraph Core["Reactor.Core (no UI framework)"]
    C[Component + hooks] --> R[ReactorRoot<br/>retained node tree,<br/>keyed diff, batching, effects]
    R --> H[IElementHandler contract]
    R --> S[IRenderScheduler]
  end
  subgraph Maui["Reactor.Maui"]
    MH[Handlers: Label, Button, Entry,<br/>CheckBox, VStack, HStack, ScrollView]
    MS[Dispatcher-backed scheduler]
    P[ReactorPage]
  end
  H -.implemented by.-> MH
  S -.implemented by.-> MS
  P --> R
```

A renderer implements, per control, `Mount`, `Update`, optional `Unmount`, and a children
strategy (`ChildrenStrategy.List` for panels, `ChildrenStrategy.Single` for content hosts).
The core does everything else.

Design choices that differ from today's WinUI Reactor, and why:

- **The core keeps its own node tree** instead of storing state on native controls through
  an attached property. That is what removes the dependency on any UI framework. Handlers
  wire events once at mount and read `context.Current` when the event fires.
- **Native controls are `object` to the core.** Each handler is strongly typed; the
  registry erases the types. Natives are always reference types, so nothing is boxed.
- **Components and groups produce no native control.** After reconciling, the core
  flattens each host's descendants into the ordered native children it should have and
  hands that list to the children strategy, which moves only what changed.
- **Echo suppression is a core primitive** (`HandlerContext.WriteSuppressed` /
  `IsEchoing`), so a programmatic write's change event is not reported as user input.

## What has been proven

Core, headless (`tests/Reactor.Core.Tests`, 21 tests): mounting through components and
groups; keyed reorder keeps component state *and* the same native controls; removal
unmounts and runs effect cleanups; duplicate keys fail loudly; changing the root's key
remounts it; setters are batched into one posted flush, across components, and hosts are
re-synced only after the whole batch has rendered; equal values don't re-render; off-thread
setters are posted to the render thread; children with equal props skip re-rendering but
still receive the latest props; effects run after commit, descendants before ancestors, and
re-run only on dependency changes, even when a setter runs during render; hook order
violations throw; controlled input doesn't echo. A boundary test fails if the core ever
references anything but `System.*`. Each of these behaviours was confirmed to have a test
that fails when the code under test is broken.

MAUI, at runtime on Mac Catalyst (driven through real input, verified through the
accessibility tree): clicks update state; a `System.Threading.Timer` updates state every
second from a background thread; typing into the controlled draft `Entry` keeps every
character in order while that timer re-renders; Add clears the draft through state;
Reverse moves rows *with* the text typed into each row's own note `Entry`, so keyed state
follows the row; toggling and removing rows update the derived count. The same pass gives the
same results on the direct renderer (`REACTOR_RENDERER=direct`), with identical control
geometry. The sample also builds for iOS and Android.

## Findings to resolve before this becomes real

- **Type-name collisions with MAUI.** MAUI declares its own `Microsoft.Maui.IElementHandler<,>`
  and `Microsoft.Maui.Controls.Element`, and MAUI projects import those namespaces implicitly.
  The renderer qualifies the handler interface, and both the renderer and the sample add a
  global `using Element = Microsoft.UI.Reactor.Core.Element` alias (a package would ship the
  sample's through `buildTransitive`). Renaming the core's handler interface is worth considering.
- **Namespace and branding.** Core types keep the namespaces they have in WinUI Reactor today
  (`Microsoft.UI.Reactor.Core` for `Element`, `Component`, `RenderContext`), so component code
  keeps compiling when they move into this assembly. That name is WinUI-flavoured; a neutral
  name would be a deliberate, source-breaking product decision.
- **Components never own a native control.** WinUI Reactor today can wrap a component in a
  control so modifiers such as `Component<T>().Margin(8)` apply to it. This core treats
  components as purely compositional, so moving WinUI onto it needs a renderer-owned wrapper
  extension point, not just handlers. Prototype that before committing to the migration.
- **No error recovery.** A render, effect or cleanup that throws leaves the root in an undefined
  state: unprocessed dirty components are not re-queued, a half-mounted subtree is not rolled
  back, and teardown stops at the first failing cleanup. This belongs with error boundaries.
- **Controlled values only re-assert on re-render.** If an `OnTextChanged` callback rejects
  input *without* changing state, nothing re-renders, so the control keeps the rejected text.
  React re-asserts the value anyway; this core does not yet.
- **Host lifetime is explicit.** `ReactorPage.Unmount()` runs every cleanup; the sample calls it
  when its window is destroyed. Navigation would need to decide when a page is gone for good.
- **Choose a renderer strategy.** The benchmark favours the direct renderer for bulk work. The
  catch is fidelity, since each Controls default must be re-derived, plus the controls that only
  exist in Controls (`CollectionView`, Shell, the third-party ecosystem). The proven middle path is
  direct for primitives and layouts, Controls for the rest, in one tree.
- **Not built yet:** hot reload, context (`UseContext`), error boundaries, navigation and
  multiple pages, virtualized lists (`CollectionView`), styling and theming, layout modifiers
  beyond margin/width/horizontal options, element pooling, and diagnostics.

## The path to a real product

1. Grow the MAUI renderer against this core until it covers a realistic app (navigation,
   `CollectionView`, context, hot reload), and measure it against XAML and MauiReactor.
2. Move WinUI Reactor onto `Reactor.Core`: its reconciler becomes handlers plus a children
   strategy, gated by the existing unit, selftest and E2E suites, and by `perf-compare`.
3. Decide where the MAUI renderer lives long-term (this repo, dotnet/maui, or its own repo).
   It ships on MAUI's cadence and needs Android and iOS CI, which this repo doesn't run.
