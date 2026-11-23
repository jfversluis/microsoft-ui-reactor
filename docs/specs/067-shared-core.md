# Spec 067 — A shared Reactor core for WinUI and MAUI

> Status: draft for the phase 1 gate. Plan: one framework-neutral core, extracted from the
> WinUI engine; WinUI re-platformed onto it with source compatibility; MAUI re-hosted on it
> (spec 065).

## 1. Goal and scope

**Goal.** A single engine, `Microsoft.UI.Reactor.Core` (`net10.0`, no `Microsoft.UI.*`, `Windows.*`
or `Microsoft.Maui.*` reference), contains everything that is not about a particular UI framework:

- the element model's identity: keys, records and equality;
- components, props and memoization;
- hooks;
- context, error boundaries and suspense-like resources;
- scheduling and batching;
- the child diff (keyed LIS);
- the handler protocol's shape (spec 047/048), registration and trimming;
- diagnostics events and hot reload.

Today's WinUI package and the MAUI renderer both implement that core's renderer interfaces.

**What "shared" means.** Shared *engine and programming model*: the same `Render()`, hooks, keys,
context, memo and error-boundary semantics, one diff algorithm and one test suite for it. Logic
written against the neutral types (custom hooks, view-model components, data and resource hooks)
compiles for both. UI trees are *not* shared: a WinUI `TextBlock` and a MAUI `Label` stay different
elements. Cross-renderer element sets are out of scope.

**Non-goals.** No binary compatibility for WinUI consumers (they rebuild). No change to WinUI
behavior or performance that a test, perf leg or screenshot can see.

## 2. What the WinUI engine looks like today

Measured at upstream `14172dce`:

| Area | Size | WinUI coupling |
|---|---|---|
| `src/Reactor/Core` | 174 files, 61k lines | 65 files (8.3k lines) reference no WinUI type; 42 (6.6k) reference 1–5; 67 (46k) are heavily coupled |
| `Reconciler*.cs` | ~14.5k lines | `Reconciler.cs` 9.5k lines and 764 WinUI references: orchestration interleaved with control behavior |
| `Element.cs` | 8.1k lines | The base `Element` record carries WinUI-typed modifier shims (`Thickness`, `Brush`, `FontWeight`…); most of the file is WinUI control records |
| `ChildReconciler.cs` | 1.5k lines | Algorithmically neutral (prefix/suffix strip, keyed map, allocation-free LIS) but typed to `UIElement` / `IChildCollection` of `UIElement` |
| `RenderContext.cs` | 3.2k lines | Hook storage (slot list and cursor) is neutral; setters marshal through `DispatcherQueue`; ~15 hooks take or return WinUI or host types (window, pickers, tray, displays) |
| `V1Protocol/` | ~8k lines | `IElementHandler<TElement, TControl>` is constrained to `TControl : UIElement`; `ControlDescriptor` to `FrameworkElement, new()`; children strategies use `UIElementCollection`; mount contexts expose WinUI types |

Four structural facts drive the design:

1. **No retained managed tree.** Per-node state hangs off native controls: `ReactorAttached.StateProperty`
   (an attached `DependencyProperty`, `Reconciler.cs:1655-1688`) plus reconciler dictionaries keyed by
   `UIElement` (`_componentNodes`, `_errorBoundaryNodes`, `Reconciler.cs:34-35`).
2. **Every component has a native anchor.** `MountComponent` creates a `Border` per component as its
   identity anchor (`Reconciler.Mount.cs:931-939`). That anchor is also why modifiers on a component
   (`Component<T>().Margin(8)`) work.
3. **The neutral algorithms already exist** (child diff, diff hints, child slots, hook slots, memo
   caches); they are typed to WinUI, not entangled with it.
4. **The public base types are WinUI-typed.** `Element`, `Component` and `RenderContext` expose WinUI
   members, and they live in the `Microsoft.UI.Reactor.Core` namespace.

## 3. Layering that keeps WinUI apps source compatible

A neutral base gets a new name; WinUI keeps today's names as subclasses in the same namespace:

| Neutral core (`Microsoft.UI.Reactor.Core.dll`) | WinUI (`Reactor.dll`) | MAUI (`Microsoft.UI.Reactor.Maui.dll`) |
|---|---|---|
| `Microsoft.UI.Reactor.Core.ReactorElement` (abstract record: `Key`, call site, diff metadata) | `Microsoft.UI.Reactor.Core.Element : ReactorElement` (unchanged members, including modifier shims) | `Microsoft.UI.Reactor.Maui.Element : ReactorElement` (`ViewModifiers`) |
| `ReactorComponent` (`abstract ReactorElement? Render()`, neutral hooks) | `Component : ReactorComponent` (`abstract override Element? Render()`, WinUI hooks such as `UseWindow`) | `Component : ReactorComponent` (`UseAppTheme`, `UseWindowSize`) |
| `RenderContext` (sealed, neutral hooks); moved with its full name | WinUI-only hooks become extension methods on `RenderContext` in the same namespace | MAUI hooks are extension methods |
| `Context<T>`, `RenderError`, resources, commands without WinUI data, persisted state, observables: moved with their full names | Types with WinUI members split the same way as `Element` | — |

A two-assembly experiment confirmed the C# rules this relies on: a covariant `abstract override` of
`Render()` across assemblies; user overrides declaring `Element Render()`; `with` on records layered
across assemblies, including base-record `init` members; `where T : Element` modifiers; and moved
hooks resolving as `ctx.UseX()` through extension methods. The phase 1 spike repeats the check at
full scale through the source-compatibility corpus (§7).

**Naming open point.** `ReactorElement`/`ReactorComponent` are working names. The MAUI renderer then
exposes `Microsoft.UI.Reactor.Maui.Element`/`Component`, symmetric with WinUI. A MAUI app still
needs the `Element` alias (spec 065), because MAUI's implicit usings import
`Microsoft.Maui.Controls.Element`.

## 4. Engine: generic over the native type

The extracted engine is `Reconciler<TNative> where TNative : class`. WinUI instantiates it with
`UIElement`, MAUI with `object` (its natives mix Controls views and direct `IView`s). A renderer
supplies a host:

- **`INativeHost<TNative>`:** create and destroy anchors, child-collection operations (the current
  `IChildCollection` made generic), dispatcher and scheduler, and per-native state storage.
  - WinUI keeps its attached DP, so its hot path is unchanged.
  - MAUI uses a field on Reactor-owned views, with a `ConditionalWeakTable` for foreign ones.
- **Renderer services:** pooling (WinUI's `ElementPool` stays WinUI-side), modifier application,
  gestures, drag and drop, animation, focus, theming. These are optional per renderer.
- **The handler protocol:** `IElementHandler<TElement, TControl>` with `TControl : class`. Mount,
  update and unmount contexts lose their WinUI members: those move to renderer context extensions,
  so handler code keeps its spelling. Children strategies are generic over the native type; DP-backed
  prop entries remain WinUI descriptor features.

**Performance rule.** The hot paths named in the reconciler map are the LIS scratch buffers, the
per-leaf state allocation avoidance, pooled rents, cached render delegates and the update fast
paths. They must not gain interface dispatch or allocations without a `perf-compare` run within
noise. The engine takes the host as a struct type parameter (`where THost : struct, …`), but its
element and native types are reference types, so the JIT still compiles **one shared body**
(`ChildReconciler<__Canon, __Canon, WinUIChildHost>`). The spike measured what that means:

- A call through an interface **generic over the element or native type** can't be bound to the
  host struct's method in shared code. It becomes a runtime-resolved indirect call that is never
  inlined. With the whole host contract generic, the keyed path measured +1.7% and +1.8% reconcile
  on two `/perf` runs, and the four keyed micro-benches were slower in all 8 measurements.
- **Rule:** per-child host members live on a non-generic interface typed `ReactorElement`/`object`
  (`IChildReconcileHost`), which binds directly to the struct and inlines; hosts reinterpret with
  `Unsafe.As`. Only per-pass members that take the child list or element arrays stay generic.
  Small helpers that need only `ReactorElement` live in non-generic classes, so they inline too.
- **Oracle:** the same engine source compiled non-generically (type parameters replaced by concrete
  types, the shape of the pre-extraction WinUI code) and timed against the generic build on a fake
  host. On a 500-child unchanged keyed pass, the generic build cost +43% before the rule and +10–14%
  after. The remainder is generic-dictionary type lookups (`ArrayPool<TNative>`, `new TNative?[]`),
  inherent to shared code. The fake host does almost no work per child, so WinUI sees a fraction of
  it; `/perf` is the gate. Step 2 repeats this measurement for every engine part it moves.

**As built so far (§9).** The spike applies this to the child diff:
`ChildReconciler<TElement, TNative, THost>`, with the host contract in four pieces:

- `IChildReconcileHost`: per-child mount, update, skip policy, exit state, keys and ambient
  animation, typed `ReactorElement`/`object`.
- `IChildReconcileHost<TElement, TNative>`: per-pass calls taking the child list or element arrays.
- `IChildList<TNative>`: today's `IChildCollection`, made generic.
- `IDirtyPathProbe<TNative>`.

`INativeHost<TNative>` above is the union these pieces grow into as step 2 moves the rest of the
reconciler. It stays a set of narrow interfaces wherever that keeps each engine part's host small.

## 5. The central decision: component anchors

WinUI gives every component a `Border`. MAUI would pay one native view per component (a `UIView` or
Android `View`, measured and arranged) if the engine required anchors. That is real cost on phones:
a 1,000-row list of row components adds 1,000 views.

| Option | WinUI | MAUI | Risk |
|---|---|---|---|
| **A. Native anchors everywhere** | Unchanged | One lightweight pass-through view per component (a direct layout with a single child) | Lowest engine risk; MAUI pays per component, measured in the spike |
| **B. Retained node tree, anchors optional (recommended if A costs more than ~10% on the MAUI benchmark)** | The host policy keeps `Border` anchors, so behavior is identical | Components are wrapper-less; natives are flattened into the parent's collection (as in spec 065's core) | State keyed by managed nodes instead of `UIElement`: a deep WinUI change, gated by all 13k+ tests and perf |
| C. Anchors only where needed | Unchanged | Anchor only components with modifiers or boundaries | The complexity of B with less benefit |

**Spike evidence.** The MAUI benchmark (`tests/Reactor.Maui.Benchmark`, `REACTOR_BENCH_MODE=anchor`)
renders 1,000 rows as components, each wrapped in a pass-through anchor, against the same rows with
no anchor (phase 0 report, `docs/reports/2026-11-23-phase-0-baselines.md`):

| Platform | Mount | Update all | Reverse |
|---|---|---|---|
| Mac Catalyst | +13% | +18% | +9% |
| iOS simulator | +16% | +17% | +12% |

Android emulator timings varied 6× run to run and are not evidence; a Pixel measurement is pending.
Anchors cost 9–18% on the bulk legs, above the 10% threshold on five of the six, so **option B is
recommended**: a
retained node tree where WinUI keeps `Border` anchors as a host policy and MAUI flattens. The WinUI
node-tree prototype on the StressPerf legs is the first task of step 2 (§6), and the decision is
confirmed there. If keying state by managed nodes can't hold WinUI perf, per-native state stays
behind the host (WinUI's attached DP) and only the component structure is retained.

## 6. Migration steps (phase 2 sub-PRs)

1. **Foundations:** neutral types and the WinUI subclasses; hook storage; the scheduler seam
   (`DispatcherQueue` behind `IRenderScheduler`); context, errors and hot reload.
2. **Engine:** the generic `Reconciler<TNative>`; diff hints generic (the child diff and
   `ChildSlots` moved in the phase 1 spike, §9); WinUI orchestration split from WinUI control
   behavior. Starts with the WinUI node-tree prototype that confirms §5's choice.
3. **Protocol:** `TControl : class`; generic strategies; registry and latches; descriptor core
   versus WinUI DP features.
4. **Tooling:** analyzers, the wrapper generator, the API index, docs and AOT proofs.

Each step keeps every WinUI tier green and perf within noise, and stops at a gate.

## 7. Verification

- **Source-compatibility corpus:** ReactorGallery, every `samples/` app, the selftest host, the
  ToolkitCompatibility app and `docs/` snippets compile unchanged against each sub-PR. Any edit
  needed to compile is a failure.
- **API index:** `skills/reactor.api.txt` before vs after. New lines are allowed (neutral bases);
  removed or changed lines need a reviewed justification.
- **The neutral boundary:** a test that fails if `Microsoft.UI.Reactor.Core.dll` references anything
  outside `System.*`.
- **Tests:** all WinUI tiers, the 44 neutral core tests (ported from spec 065's core onto the
  extracted engine through a fake renderer), MAUI headless and device tiers.
- **Perf:** the `perf-compare` legs (StocksGrid, the skip floor, KeyedList, the reconciler
  micro-suite) within the noise measured on an A/A run in phase 0; startup perf; AOT size.

## 8. Risks

- `Reconciler.cs` interleaves orchestration and WinUI behavior. Splitting it is mostly mechanical,
  but each move must preserve call order. The mitigation is small commits, the full suite at each
  sub-PR, and perf at each sub-PR.
- Option B changes how WinUI keys state; if the spike shows it can't hold WinUI perf, choose A.
- Hidden source breaks (extension-method ambiguity between the neutral and WinUI layers, inference
  changes on `Render()` covariance) are caught by the corpus.

## 9. Phase 1 spike: what moved and what it found

The spike moves the smallest slice that proves the split end to end:

- **`Microsoft.UI.Reactor.Core`** (`src/Reactor.Core`, `net10.0`, its own package) holds
  `ReactorElement` (declares `Key`), `ChildSlots`, and the keyed child-reconciliation engine as
  `ChildReconciler<TElement, TNative, THost> where THost : struct, IChildReconcileHost<TElement,
  TNative>`. The struct host makes the JIT specialize each renderer's instantiation, so WinUI calls
  stay direct. The engine body is a line-for-line transliteration of the WinUI one.
- **WinUI** keeps `ChildReconciler` as a façade: it reads the ambient-animation state and calls the
  engine with a `WinUIChildHost`. `Element : ReactorElement`, and `IChildCollection :
  IChildList<UIElement>`.

Seams the spike found, each now guarded:

- **Doc-comment crefs.** `cref="Element.Key"` stops resolving once `Key` is declared on the base
  (CS1574, an error in Release). Thirteen repo crefs moved to `ReactorElement.Key`; consumer doc
  comments that use the old spelling need the same edit (CHANGELOG).
- **Internals.** Engine internals used to be visible to every assembly Reactor grants
  `InternalsVisibleTo`. The core grants the same list plus Reactor; `NeutralCoreBoundaryTests` fails
  if they drift, and fails if the core references anything outside `System.*`.
- **The API index.** The generator lists members a type declares, so `Element.Key` would have left
  `reactor.api.txt`. It now lists properties a type inherits directly from the core on that type;
  the index diff is one added line (`Equals(ReactorElement other)` on `Element`, a compiler-generated
  record member).
- **Roslyn test compilations.** `TestCompilation` excluded `Reactor*` assemblies so stubs are the
  only Reactor types; it now also excludes `Microsoft.UI.Reactor*`, so the reference set doesn't
  depend on whether the core had loaded. Tests that bind against the real `Reactor.dll` reference
  the core too.

Packaging: `Microsoft.UI.Reactor.Core` is its own package, and the framework depends on exactly
its own version (`[V]`, written by `_PinReactorCoreDependency` and checked by `release.yml`),
because Reactor.dll binds the core's internals. `release.yml`, the internal pipeline (build, sign,
pack), `mur pack-local`/`clean-local`, build metrics, coverage instrumentation, the integration
tests' feed, the toolkit package-consumer harness and bootstrap's feed check all carry it.
Bundling the DLL into the framework's `lib/` (as `Reactor.Wrappers.Abstractions` is) was
considered: it saves the extra package now, but the MAUI renderer needs the same core in phase 3,
and two packages each bundling it would put two copies of the same assembly in one app's restore
graph.

Tests: `NeutralChildReconcilerTests` drives the engine through a fake renderer with no WinUI types
(keyed rotation by one move, keyed middle replacement, skip, slot-aligned conditional child, and a
host whose natives carry no key); each fails when the keyed path or the slot walk is disabled.
`AdvancedInternalSurfaceTests` pins Reactor.Advanced's reach into the core's internals to none.

Not yet checked by the spike and owned by step 4 (§6): analyzers and the source-map generator, which
match Reactor types by the `Microsoft.UI.Reactor.Core` namespace string; nothing they match has
moved yet. Step 4 also indexes the core assembly in `reactor.api.txt` directly, once types such as
`RenderContext` move with their full names; lifting properties onto `Element` only covers the
spike's one moved member.

**Open for the phase 1 gate.** `Key` moving to the base breaks one thing a recompile can see:
`cref="Element.Key"` in a consumer's XML doc comment (CS1574, an error only with warnings as
errors). A forwarding `new Key` on `Element` would hide it but makes record `ToString()` print `Key`
twice, so the spike documents the break (CHANGELOG) instead; the gate decides whether that is
acceptable under the source-compatibility rule.
