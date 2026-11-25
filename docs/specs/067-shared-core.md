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
compiles for both. *As of §13, that holds for code using the core's hooks; the renderers' own data
hooks and context providers still differ (§13.4).* UI trees are *not* shared: a WinUI `TextBlock` and a MAUI `Label` stay different
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
`Microsoft.Maui.Controls.Element`. *Settled in §13.1: MAUI keeps WinUI's full names,
`Microsoft.UI.Reactor.Core.Element`/`Component`, in its own assembly.*

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
noise. The engine takes the host as a struct type parameter (`where THost : struct, …`), so each
renderer gets its own specialization and host calls bind to the struct. Its element and native
types are reference types, though, so within that specialization the body is shared code
(`ChildReconciler<__Canon, __Canon, WinUIChildHost>`), and the spike measured what that costs:

- **Oracle.** The same engine source is compiled non-generically (type parameters replaced by
  concrete types, the shape of the pre-extraction WinUI code) and timed in the same process
  against the generic build, on a fake host that does almost no work per child, for a 500-child
  unchanged keyed pass. The first extraction ran +14–16% over the oracle. On WinUI that showed up
  as KeyedList reconcile +1.7% and +1.8% on two `/perf` runs, with the keyed micro-benches slower
  in all 8 measurements.
- **Rule.** Per-child helpers that need only `ReactorElement` live in non-generic classes
  (`ChildReconcilerShared.KeyMatch` and its siblings), and cheap per-child guards are checked
  inline before calling a generic helper (`dirty.IsEmpty || …`). That brought the oracle gap to
  +8–10%. The remainder is generic-dictionary type lookups (`ArrayPool<TNative>`,
  `new TNative?[]`), inherent to shared code.
- **Tried and rejected.** Typing the host's per-child members `ReactorElement`/`object` so their
  interface isn't generic measured +12%, no better. The runtime already resolves a constrained
  call on a struct host implementing a single instantiation of a generic interface, so the typed
  host contract stays.
- Step 2 repeats this measurement for every engine part it moves; `/perf` is the gate.

**As built so far (§9).** The spike applies this to the child diff:
`ChildReconciler<TElement, TNative, THost>`, with the host contract split into
`IChildReconcileHost<TElement, TNative>` (mount, update, skip policy, exit transitions, keys,
ambient animation), `IChildList<TNative>` (today's `IChildCollection`, made generic) and
`IDirtyPathProbe<TNative>`. The element type is a parameter too, so the engine reads `Key` from
`ReactorElement` and hands each renderer its own element type without casts. `INativeHost<TNative>`
above is the union these pieces grow into as step 2 moves the rest of the reconciler. It stays a
set of narrow interfaces wherever that keeps each engine part's host small.

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
   behavior. Starts with the WinUI node-tree prototype that confirms §5's choice. *Superseded for 2b
   by §11.1: the component runtime is shared and each renderer keeps its own tree walk.*
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

## 10. Phase 2a: foundations

`RenderContext` moved into the core with its full name, along with the hooks every renderer shares:

- **Hooks:** state, reducer, effect, memo, callback, ref, context, persisted, observable, collection
  and external store.
- **Hook bookkeeping:** effect flushing, cleanups, hot-reload hook migration and the devtools
  snapshots.
- **Their types:** `Context<T>`, `ContextBase`, `ContextScope`, `Ref<T>`, `HookOrderException`, the
  persisted-state scopes and `LruCache`.
- **The neutral diagnostics:** `ReactorEventSource`, `DiagnosticLog`, `LogCategory` and
  `ComponentPerfTally`.

`Reactor.dll` type-forwards each public type that moved.

**WinUI hooks are extension members.** Windows, pickers, navigation, theme and accessibility,
commands, `UseIntl` and `UseObservableTree` are C# 14 extension members of `RenderContext` in
`WinUIHooks` (`src/Reactor/Core/RenderContext.WinUI.cs`). `ctx.UseWindow()` and the static
`RenderContext.PickerService` keep their spelling. A subclass would have kept them as instance
methods, but every app references both assemblies, so two `Microsoft.UI.Reactor.Core.RenderContext`
types would collide (CS0433). It would also break the far more common `cref="RenderContext.UseState"`
(a cref doesn't resolve inherited members) and rule out component libraries written against the
core alone.

**What a recompile can see:**

- A WinUI hook called on a context whose type the file never names, such as
  `Memo(ctx => ctx.UseWindow())`: CS1061 without `using Microsoft.UI.Reactor.Core;`. An instance
  member needed no import; an extension member resolves only through an imported namespace. Every
  sample already imports it, because it also holds `Element` and `Component`.
- `nameof(RenderContext.UseWindow)`: CS8093, since `nameof` takes no extension members. One repo
  test changed; write the string instead.
- `cref="RenderContext.UseWindow"`: CS1574. Write `cref="WinUIHooks.extension(RenderContext).UseWindow"`.
- A binary compiled against an earlier `Reactor.dll` that calls a WinUI hook needs a rebuild. Types
  are forwarded, members are not.

Every sample compiles unchanged. Code that hard-codes where a member is declared did need updating:

- Four analyzers (`HookRulesAnalyzer`, `EffectCleanupAnalyzer`, `BlockingTaskAnalyzer`,
  `UsePersistedScopeAnalyzer`) recognised a class component's hook by its declaring type being
  `Component`. The shared shortcuts are now declared on `ReactorComponent`, which they would have
  silently stopped matching; they accept it now. `ReactorComponentHookAnchorTests` stubs the real
  hierarchy, unlike the older analyzer suites, which declare the hooks on `Component`.
- The source-map generator, which numbers each component's hook slots for devtools, recognised a
  hook the same ways: declared on `RenderContext` or a `Component` subclass, taking a `RenderContext`,
  or declared in `RenderContext`'s assembly. It now also accepts `ReactorComponent`, reads an
  extension block's receiver (Roslyn reports `ctx.UseWindow()` as declared on the block, with no
  `RenderContext` parameter), and counts both the core and `Reactor.dll` as Reactor's own. Without
  that, class components lost their hook table and WinUI hooks dropped out of the slot numbering.
- Tests that read `src/Reactor/Core/RenderContext.cs` by path, `tools/reviewer/manifest.json`, and
  the swallowed-error ledger (spec 044), whose `RenderContext.cs` section is now one per file, point
  at the new files.
- Reactor.Advanced's reviewed InternalsVisibleTo pin: the four internals it reaches that moved
  (`ReactorEventSource`, `DiagnosticLog`, `LogCategory`, the component `Context`) are now pinned
  against the core instead of `Reactor.dll`.

**Seams.** Each is a core abstraction with WinUI's implementation, chosen so behavior is identical:

| Seam | Core | WinUI |
|---|---|---|
| Owner | `IRenderOwner` (the window's persisted scope) | `IContentOwner : IRenderOwner`; hooks cast to it |
| UI dispatcher | `RenderScheduler.TryEnqueueOnUIThread` | Pushed by the one `ReactorApp.UIDispatcher` setter |
| Perf state events | `CorePerfHooks` (two published gates + a writer) | `ReactorPerfTrace.ApplyControlCode`, the only place the session state changes, publishes them |
| Component ids | `ComponentIds` | `ComponentRenderTrace.NextId` delegates |
| Hot-reload pass | `HotReloadState` (`[ThreadStatic]`) | `HotReloadService` opens and closes passes |
| Memory pressure | `ApplicationPersistedScope.MemoryPressureSource`; scopes created earlier register when it is set | `WinUICoreServices.EnsureInstalled()` (Windows `MemoryManager`), called from the `Reconciler` constructor and the dispatcher setter |

`WinUICoreServices` replaces a module initializer, which the repo avoids because it is an
unconditional trimming root (`ReactorApp.BuiltIns.cs`).

**Seam follow-ups (from the phase 2a review).** These are behavior-preserving today and are revisited
when MAUI plugs into them (phases 2b–3):

- *The UI dispatcher is process-wide.* `RenderScheduler` reproduces the old behavior, where
  `ReactorApp.UIDispatcher` is the first host's queue, including its limit with hosts on separate UI
  threads (the reconciler already prefers the host's own `OwningDispatcher`). The better shape is a
  per-owner `TryEnqueue` on `IRenderOwner`, with the static as the fallback for ownerless contexts;
  that is also what MAUI's per-root scheduler wants.
- *Memory pressure is pulled.* Each scope subscribes through the installed source, so scopes created
  before it are held weakly until it arrives. A push model, where the renderer subscribes once and
  calls into the core, would drop that bookkeeping.
- *Renderer hooks touch the slot table directly.* WinUI's navigation-lifecycle hook claims its slot
  through `_hooks`/`_hookIndex`, now internal, and `HookKind` lists it. A narrow internal
  slot-claim primitive, with hook states naming themselves for diagnostics, would keep renderer hooks
  out of the core's fields; the handler protocol (2c) is where MAUI's hooks would first need it.

**Deferred on purpose.** `SourceLocation` and `ReactorPerfTrace` stay in WinUI: `SourceLocation`
reads the source-map generator's tables, which generated code calls by their WinUI name.
`UseObservableTree` stays too: its tracker marshals through the current thread's `DispatcherQueue`,
which is a different seam from the UI dispatcher.

**Components.** `ReactorComponent` (core) holds the context, `abstract ReactorElement? Render()`,
`ShouldUpdate()` and the shared hooks' shortcuts. WinUI's `Component : ReactorComponent`
redeclares `public abstract override Element Render()` (a covariant override, so user overrides
written as `Element Render()` compile unchanged) and keeps WinUI's shortcuts; `Component<TProps>`
is unchanged. One more compile-visible edge, the same kind as `Element.Key`: a doc comment
`cref="Component.UseState"` names an inherited member and needs `ReactorComponent.UseState`.

**Tooling.**

- The API index now indexes the core assembly and lists extension-block members on their receiver.
  The diff is additive: `ReactorElement` and `ReactorComponent` get their own blocks. `## Hooks`
  keeps its `Use*` gate for extension members too, so the pickers, which are extension members but
  not hooks, stay out of it and are listed on `RenderContext`.
- The docs pipeline reads `Microsoft.UI.Reactor.Core.xml` beside `Reactor.xml`. It maps the
  compiler's extension-block doc ids back to `RenderContext` crefs, so every hook reference page
  regenerates byte-identical.

## 11. Phase 2b: the component runtime

### 11.1 Scope change from §4, and why

§4 sketched one generic `Reconciler<TNative>` that owns the tree walk for both renderers. Measured
against the code, the two walks differ in more than types:

| | WinUI (`Reconciler*.cs`) | MAUI (spec 065, `ReactorRoot`) |
|---|---|---|
| Anchors | a `Border` per component; per-native state (attached DP, dictionaries keyed by `UIElement`) | none: a retained `Node` tree, natives flattened into the nearest host |
| Re-render | a root pass per batch; `SelfTriggered` marks the path through skippable ancestors | a dirty-node list, rendered parents first |
| Context | a traversal scope (`ContextScope`), consumers compare their last value | providers keep subscriber sets |
| Effects | flushed right after each component's `Render()`, before its children mount | after the commit, deepest first |

Replacing WinUI's walk would rewrite about 14.5k lines of traversal under a no-behavior-change rule.
2b instead extracts what makes a component a component, so both renderers get the same semantics
for it, and leaves the walk to the renderer. WinUI keeps its anchors (option A for WinUI); MAUI keeps
its flattened node tree (option B's goal for MAUI). The retained node tree is therefore a renderer
choice, not a core requirement, and §5's prototype is not needed to unblock MAUI.

**Effect timing** is the one hook semantic that differs between the renderers today. 2b makes it a
host policy (WinUI flushes inline, as now), so neither renderer changes. Whether MAUI moves to
WinUI's timing, or WinUI to React's, is a decision for the phase 3 gate.

### 11.2 What moves to the core

As built: all in `src/Reactor.Core/Engine/`, namespace `Microsoft.UI.Reactor.Core`, and all
internal; WinUI reaches them through the core's `InternalsVisibleTo` list. The design review
(gpt-6.1-sol) confirmed the split and shaped each item's limits below.

**Access for MAUI.** The contracts stay internal while their shape is provisional (§11.3). Phase 3
adds the MAUI renderer's assemblies to the core's `InternalsVisibleTo` list and relaxes
`NeutralCoreBoundaryTests`, which today pins that list to Reactor's grants. This matters: a MAUI
`Component<TProps>` that can't implement `IPropsReceiver` would be gated through the parameterless
`ShouldUpdate()` and would never get the issue #151 props refresh, silently. Making them public is
the alternative once the shape settles.

1. **Element contracts**: `IComponentElement` (its props; constructing the instance stays with the
   renderer), `IRenderFunctionElement` (renders against a `RenderContext`) and `IMemoElement` (adds
   dependencies), plus the props contracts components implement (`IPropsReceiver`,
   `IPropsComparable`, moved from WinUI unchanged). `ComponentElement` satisfies `IComponentElement`
   through its existing `Props`; `FuncElement` and `MemoElement` implement `Render` explicitly. No
   public member and no trimming annotation changes.
2. **`ComponentNode`**, today `Reconciler.ComponentNode`, typed to `ReactorComponent`/`ReactorElement`,
   keeping the volatile self-triggered flag and the lazily assigned ids. WinUI still finds nodes
   through its anchors; topology stays with the renderer.
3. **The skip gate**: class props (`ShouldUpdate(old, new)` or `ShouldUpdate()`, always asked, before
   the context check), memo dependencies and consumed context, returning the decision and the trace
   reason, plus the skip path's props refresh (issue #151). The renderer applies the bypasses before
   asking (the component's own state change, a forced render). Context detection reads the core's
   `ContextScope`: WinUI passes its traversal scope; MAUI will fill one with the effective ancestor
   values for each node it renders (phase 3). The function-component context probe stays in WinUI,
   conditional on tracing, as now.
4. **A render kernel with explicit mount and update modes**, not one transaction. The four paths
   differ on purpose and keep doing so: mount constructs the instance and installs props outside the
   render's catch, update inside it; only update retries a hook-order change during a hot-reload
   pass; update traces effects and render start/stop, mount doesn't; the log messages differ. The
   kernel runs `BeginRender`, the render call, the output transformation (WinUI's validation
   `ApplyProvide` returns a different element, so the kernel retains what the host returns) and the
   effect flush, and classifies a failure from inputs the renderer supplies (boundary depth,
   propagation identity). Construction, the subtree commit, registration and the perf span's
   lifetime (which covers the subtree) stay in the renderer. `IComponentRenderHost`'s failure and
   effect members encode WinUI's policy (an in-tree fallback, inline effects); they are provisional
   until phase 3 settles recovery and deferred effects (§11.3), which will reshape them.
5. **The re-render wrapper.** Only the re-entrancy bound (`RerenderReentrancy`) moves. The rest of
   the wrapper is WinUI's invalidation rule, which MAUI doesn't share (§11.3): marking every
   ancestor self-triggered, WinUI's dispatcher choice (`OwningDispatcher ?? ReactorApp.UIDispatcher`,
   inline when on it or when neither exists, refusals ignored) and `RenderPassRequests.InvokeAs`
   around the synchronous ancestor chain all stay put. Routing hook *writes* through a per-root
   scheduler is a separate contract (the shared setters marshal through the process-wide one); it
   lands with MAUI in phase 3, and the 2a follow-up on a per-owner dispatcher moves there too.
6. ~~**The render-loop gate**~~: dropped (§11.4, step 3).
7. ~~**Diff hints and memo-cell caches**~~: deferred (§11.4, step 4).

Stays in WinUI: anchors and the tree walk, error-boundary nodes (walk structure), element equality
(`ShallowEquals`, `SettersEqual`: a catalog of WinUI records), modifiers, pooling, the element
factory, the render-loop flags and, for now, the diff hints and memo-cell hooks.

### 11.3 What phase 3 inherits

Sharing the runtime doesn't by itself make the renderers behave the same. These are contracts for
phase 3, recorded now so they are designed rather than discovered:

- **Identity.** In a list mixing keyed and unkeyed children, WinUI matches an unkeyed child by its
  absolute position and MAUI by its position among unkeyed siblings, so reordering
  `[Counter, K]` to `[K, Counter]` resets `Counter`'s state on WinUI only. MAUI adopts the shared
  child diff (`ChildReconciler`), which makes WinUI's rule the contract.
- **Invalidation.** WinUI re-renders every component on the path to a state change; MAUI only the
  dirty node. Render counts are not part of the contract (a pure `Render()` gives the same output);
  state, context values and effects are.
- **Recovery.** WinUI renders an in-tree fallback for a component error outside every boundary;
  MAUI tears down the root. A boundary retries on every pass that reaches it (WinUI) or on a parent
  update (MAUI). Phase 3 picks one.
- **Deferred effects.** MAUI renders a node more than once before effects run. The shared
  `RenderContext.ScheduleEffect` moves `Cleanup` to `PendingCleanup` on each schedule, so a second
  schedule before a flush drops the first cleanup. Deferred mode needs coalescing, cancellation for
  discarded nodes and an effect-failure path that carries the node; WinUI's inline mode is unchanged.
- **Hook count.** MAUI's context validates the hook count at `EndRender`; the shared one has no such
  step.
- A **conformance suite** both renderers run decides these, through a fake retained-node host in the
  core's tests.

### 11.4 Steps, each with WinUI tiers green

0. **Characterization first.** `ComponentLifecycleCharacterization` selftests record one ordered log
   per step (renders, effects, cleanups, rendered output, host warnings and errors, component
   events), untraced and traced, and compare it with goldens captured on the unchanged reconciler:
   - mount and update for class, function and memo components, with the skip gate's decisions;
   - render and effect failures on mount and update, outside a boundary (the in-tree fallback, each
     path's log message) and under one (class and function components);
   - the hot-reload hook-order retry, and re-renders requested from another thread and batched on
     the UI thread;
   - the re-entrancy cap through the real wrapper (`Lifecycle_RerenderDepthCap`). As captured, a
     state change 60 components deep reached it, because every ancestor's wrapper counted the
     request. The fix after the gate walks the ancestors from the component that asked, so a
     request is counted once and climbs on a constant stack: the fixture now checks that a chain
     200 deep renders and that a real synchronous loop 200 deep stops after exactly `Max`
     requests.

   The goldens were identical across the unpackaged, packaged and NativeAOT tiers and passed on the
   unchanged code in a separate run. Existing fixtures cover the rest: `RenderError.Propagate()` and
   app handlers (`RenderErrorHandlerFixtures`), fatal errors in and out of boundaries
   (`RenderErrorNamesFixture`), the context reason (`ComponentRendered_ReasonsAndIdsFollowTheReconciler`),
   requester attribution (`PerfTraceFixtures`) and the props refresh (`CallbacksMemoSkipFixtures`).
1. Contracts, `ComponentNode` and the skip gate.
2. The render kernel and the re-render guard.
3. ~~The render-loop gate.~~ Dropped: measured against the code, the shared part is a volatile flag
   and a compare-exchange per host, interleaved with host-only work (the perf request note, the
   animation ambients, a test hook between the two checks, the synchronous first render, the
   priority choice). Extracting it adds indirection and gives MAUI nothing; MAUI's scheduler
   contract is defined in phase 3 with the invalidation rules (§11.3).
4. ~~Diff hints and memo-cell caches.~~ Deferred to the phase that gives MAUI virtualized grids. The
   hints' only producer is `UseMemoCellsByIndex`, so a core hint registry without the memo-cell
   hooks would have no second writer, and the hooks are 524 lines on the StocksGrid hot path with
   no MAUI consumer yet. They move with their first MAUI user.

**Guards.** The characterization tests, unchanged, at every step; no behavioral assertion is
weakened to fit the extraction. Neutral unit tests drive the gate and kernel through fake elements
and a fake host. **Perf:** every `/perf` leg (StocksGrid, skip floor, KeyedList, Flex, DataGrid, the
micro-suite) with allocations, plus the 30-rep skip floor and micro-suite isolation against the
previous phase. Avoid allocations on the gate path (no closure adapters, no boxed scopes, no eager
reason strings), keep element-neutral logic non-generic, and keep the cached hook setters.

## 12. Phase 2c: the handler protocol

§6 step 3 and §4 planned to make the handler protocol neutral before MAUI moves onto the core:
`IElementHandler` with `TControl : class`, contexts without WinUI members (those becoming renderer
extensions), generic children strategies, the registry and its latches, and a descriptor core
separate from WinUI's DP features. §1 lists the protocol's shape, registration and trimming among
the core's contents. Mapped against both renderers (WinUI at `9e85a6e0`, MAUI's spec 065
renderer), the plan meets three facts. A cross-family review (gpt-6.1-sol) checked them and the
options below.

### 12.1 What the code shows

**The public protocol is WinUI-shaped in its signatures, not just its constraints.**

| Public type | WinUI in the signature |
|---|---|
| `IElementHandler<TElement, TControl>` | `TElement : Element` (WinUI's), `TControl : UIElement`; members take `MountContext`/`UpdateContext`/`UnmountContext` |
| `MountContext`, `UpdateContext`, `UnmountContext` (`readonly ref struct`) | `Reconciler`; `MountChild`/`ReconcileChild` return `UIElement`; `BindFor(FrameworkElement, …)`; `AddRawRoutedHandler(UIElement, RoutedEvent, …)`; `RentControl`/`ReturnControl` (WinUI pooling) |
| `ChildrenStrategy<TElement, TControl>` and its 11 variants | `TControl : UIElement`; `Panel` exposes `UIElementCollection`, child callbacks take `UIElement` |
| `ControlDescriptor<TElement, TControl>` | `TControl : FrameworkElement, new()`; `Controlled` subscribes with `Action<FrameworkElement, …>`; `OneWay` and `Immediate` overloads take a `DependencyProperty`; `ImperativeBridged` takes the contexts; `PoolPolicy` |
| `PropEntry<TElement, TControl>` | `TControl : UIElement`; virtual `Mount`/`Update` overloads take the contexts |
| `IDecoratorElementHandler<TElement>`, `AttachedPropWriter<T>`, `ReactorBinding<T>` | return and take `UIElement`/`FrameworkElement` |

Relaxing a constraint is binary compatible. Removing instance members is not: if the contexts move
to the core, their WinUI members become extension members, and compiled code that calls
`ctx.MountChild`, `ctx.ReconcileChild` or `ctx.Reconciler` fails with `MissingMethodException`
until it is rebuilt. That code includes hand-written handlers and the wrappers spec 058 generates
for secondary element slots and custom lifecycles (`WrapperGenerator.cs:1692-1730`); ordinary
generated `.Controlled` entries don't call them. §1 accepts binary breaks (consumers rebuild), so
this is a cost to weigh, not a bar.

**The two protocols differ in structure, not just types.**

| | WinUI (`V1Protocol`) | MAUI (spec 065, `Reactor.Maui/Engine`, formerly `Reactor.Maui.Core/Hosting`) |
|---|---|---|
| Children | the strategy drives mounting: the reconciler diffs children straight into the native collection (`ChildReconciler`, incremental insert/move/remove) | the engine reconciles child nodes, collects the flattened natives, then the strategy's `Sync(control, natives)` reorders by reference; slotted strategies get one group per child |
| Context | per phase; reconciler, child mount, pooling, routed events, binding helper | one `HandlerContext`, retained by event handlers; the live element, echo guard, child roots, error reporting, restore-after-event |
| Lifecycle | items bind before props (`DescriptorHandler.cs:109-139`) | props, then children sync; `CanUpdate` decides remount |
| Registry | process-global, first registration wins, lazy self-registering latches (spec 048), per-reconciler overrides | an instance per root, last registration wins, so the hybrid set replaces Controls handlers with Direct ones (`DirectHandlers.cs:18-31`) |
| Echo | a counter, plus a value-diff arm kept on per-control state in an attached DP; the descriptor's typed arm is synchronous-only and drains only on a matching readback (`PropEntry.cs:295-341, 647-656`) | a reference set active during the synchronous write only |
| Props | mostly descriptors, a declarative entry table: 81 declarations (73 from `[GenerateReactorDescriptor]` and the wrapper generator, spec 058, plus 8 hand-written), and 18 hand-coded handlers | imperative `Mount`/`Update` code: 35 Controls handlers, plus 12 Direct or hybrid overrides |

**There is one consumer.** MAUI doesn't use any of WinUI's protocol today. A neutral protocol
extracted now would be shaped by WinUI alone, and phase 3 would reshape it, as §11.3 already
expects of the render kernel's host contract.

### 12.2 Options

- **A. Move the public types to the core (§4 as written).** One `IElementHandler` for both
  renderers, at the cost of the binary break above. The children model must also cover both the
  incremental and the collect-and-sync shape before MAUI has tried either.
- **A′. A new core contract, generic over the context.** Core interfaces take the renderer's context
  as a type parameter (`allows ref struct`), and WinUI's public interfaces and contexts stay as
  they are, adapted through the existing adapter shells. No break. It shares little beyond
  lifecycle dispatch, though: MAUI still needs a context it can retain, `CanUpdate` and flattening.
  Feasible, not worth it before MAUI uses it.
- **B. Extract the descriptor interpreter now, behind WinUI's unchanged public types.** Large, and
  shaped by one consumer: item-before-prop ordering, the synchronous-only typed arm and the
  context-taking entries are WinUI policy that MAUI would have to undo.
- **B-lite. Extract internal neutral decision helpers now:** the get/compare/set decision of a
  one-way entry (`PropEntry.cs:86-110`), the scalar echo decision
  (`ChangeEchoSuppressor.cs:161-188`), the registry's exact/base lookup and cache
  (`ControlRegistry.cs:317-353`). Cheap and no hot-path cost (static helpers over existing fields),
  but small payoff, and their signatures would still be guesses until MAUI calls them.
- **C. Extract each piece when MAUI adopts it, in phase 3 (recommended).** Phase 3 starts by
  re-hosting MAUI's components and hooks on the core (2a, 2b), which needs no protocol. Then the
  protocol pieces move with MAUI as their second consumer. §1's goal stands; it is reached with two
  consumers instead of one. WinUI's public types stay where they are until then; whether they
  become neutral public types (A or A′) is decided once both renderers implement the same pieces,
  at a phase 3 gate.

### 12.3 Phase 3 gates the protocol pieces must pass

Each extraction is gated like 2b (characterization first, WinUI tiers, `/perf` with the 30-rep
isolation), plus:

- **Echo.** Characterize before sharing anything: repeated writes before an event is delivered,
  event payload versus current readback, coerced and no-op writes, several controlled properties on
  one control, rejected user input (MAUI's restore-after-event) and teardown. Echo can land in
  MAUI's existing imperative handlers first; it doesn't need descriptors.
- **Registry.** State is per renderer or per root, and the renderer owns its duplicate and override
  policy (WinUI first-wins, MAUI last-wins with hybrid overrides). Latches may seed a lazy catalog;
  they don't populate roots.
- **Descriptors.** Gates for the order of children and props, live-element access in events,
  restore-after-event and hybrid remounts (`CanUpdate`).
- **Children.** After MAUI's flattening is settled against the shared child diff (§11.3, identity).

### 12.4 What 2c delivers

- The deep-tree re-entrancy fix (phase 2b gate decision 5): a request climbs its ancestors
  iteratively, so it is counted once and runs on a constant stack.
- This analysis and the phase 3 gates above.

Tooling (2d) follows the same rule. The analyzers already anchor on the core's types (2a), and the
API index and docs pipeline were kept current in each phase. What remains (MAUI analyzer coverage,
a MAUI wrapper generator) has no consumer until MAUI is on the core, so it moves to phases 3 and 4.

## 13. Phase 3a: MAUI on the shared core

Phase 3 starts by moving MAUI's components and hooks onto the core, which needs no protocol
(§12.2, option C). The transitional `Microsoft.UI.Reactor.Maui.Core` (spec 065) is deleted.

### 13.1 What MAUI now shares

| | Before (spec 065's core) | Now |
|---|---|---|
| Element base | its own `Element` record | `Microsoft.UI.Reactor.Core.Element : ReactorElement`, in the MAUI assembly |
| Components | its own `Component`, `Component<TProps>` | `Component : ReactorComponent`, with a covariant `Render()`, like WinUI's |
| Hooks | its own `RenderContext` | the core's `RenderContext`: `UseState`, `UseReducer`, `UseEffect`, `UseMemo`, `UseCallback`, `UseRef`, `UseContext`, and the core-only hooks MAUI lacked (`UsePersisted`, `UseObservable`, `UseObservableProperty`, `UseExternalStore`, `UseCollection`) |
| Context | its own `Context<T>` | the core's `Context<T>`; `Provide(value, child)` is an extension |
| Skip decision | inline in the engine | `ComponentGate` (§11.2) |
| Rendering a component | inline in the engine | `ComponentRenderKernel` (§11.2), including the hot-reload hook-order retry, which MAUI lacked |

The MAUI-only hooks are extensions of `RenderContext` in the MAUI assembly, as WinUI's are in its
own (§10): `UseRouteStack`, `UseResource` and `UseCallback<TDelegate>` in `MauiCoreHooks`, and
`UseAppTheme`/`UseWindowSize` in `MauiHooks`. `Component` has a shortcut for each.

**Names.** MAUI's element base and component keep the full names WinUI uses
(`Microsoft.UI.Reactor.Core.Element`, `Component`, `ComponentElement`, `FuncElement`, …) in their
own assembly, rather than §3's working `Microsoft.UI.Reactor.Maui.Element`. An app references one
renderer, so the names never meet, and code written against them with the core's hooks compiles for
either renderer. This settles §3's naming open point.

**The engine stays in the renderer.** MAUI's node tree (`ReactorRoot`, `Node`, the handler
contract) moves into `Reactor.Maui` (`Engine/`), per §11.1. Its 44 tests move with it
(`tests/Reactor.Maui.Engine.Tests`) and run against a fake renderer, as before; a test there checks
that MAUI's components and elements derive from the core's, and that the core references no UI
framework (this runs on macOS, where the Windows boundary test doesn't).

### 13.2 How MAUI plugs in

- **Context.** The core's `UseContext` reads a `ContextScope`. MAUI renders components out of tree
  order (a dirty-node queue), so it fills a per-root scope with the providers above a node just
  for that node's render or gate check. After a render it subscribes the node to the nearest
  provider of each context it read (`RenderContext.ContextHooks`), which keeps MAUI's rule that a
  reader re-renders when its provider changes even below a component that skipped. Each node
  caches its nearest provider, and each provider the next one up, so these lookups walk providers
  only and allocate nothing.
- **The kernel host** (`ReactorRoot.KernelHost`) rethrows every failure, because the engine routes
  errors to boundaries itself; flushes no effects inline (they run after the commit, deepest
  first); and checks the hook count after each render.
- **Setters off the render thread** marshal through the root's scheduler, which the root installs
  on each context (`RenderContext.RenderThreadEnqueue`). WinUI leaves it unset and keeps the
  process-wide dispatcher. This resolves the 2a follow-up on a per-owner dispatcher (§10) on the
  context rather than on `IRenderOwner`, because a context drops its owner when it unmounts, and a
  setter a background task calls after that still has to reach the render thread.

### 13.3 Changes to the core

All internal, and invisible to WinUI except where noted:

- `RenderContext.RenderThreadEnqueue`: read only when a setter is called off the render thread.
- `RenderContext.ValidateHookCount()`: the hook-count check (§11.3) as an opt-in step; MAUI calls it,
  WinUI doesn't.
- `RenderContext.HasPendingEffects`.
- **Deferred effects** (§11.3): an effect scheduled again before the flush keeps the cleanup the
  first schedule staged, instead of dropping it. WinUI flushes inline, so it only reached this in a
  render that failed and was retried, where it leaked the cleanup.
- **A cleanup that throws during the flush** is cleared before it runs, so unmount doesn't run it a
  second time. This changes WinUI too: before, a throwing cleanup ran again when the component
  unmounted.
- The core grants its internals to the MAUI renderer and its test assemblies (§11.2), and
  `NeutralCoreBoundaryTests` pins that list.

### 13.4 Behavior changes for MAUI apps

- A component's own state change renders it without asking `ShouldUpdate` (WinUI's rule). Before,
  MAUI asked it anyway and ignored the answer.
- A component's effects run their cleanups first, then the new effects, rather than each effect's
  cleanup just before that effect (React's order, and WinUI's).
- An unkeyed child among keyed siblings is matched by its absolute position (phase 3b, §13.6): moving
  it past a keyed sibling remounts it. Key it to keep its state.
- Code that calls `Provide` needs `Microsoft.UI.Reactor.Core` in scope; the package's global usings
  provide it. Both `context.Provide(value, child)` and WinUI's `element.Provide(context, value)` work;
  the second returns the provider, so it ends a modifier chain.

**Still renderer-specific** (to converge in phase 4, before the MAUI package leaves preview):
`UseResource` (MAUI returns `Resource<T>`, WinUI the `AsyncValue<T>` union with a query cache),
`UseWindowSize` (MAUI `Size`, WinUI a tuple), `UseRouteStack` and `UseCallback<TDelegate>`
(MAUI only), and `Provide` (WinUI's is a modifier that keeps the element's type).

### 13.5 What phase 3 still owns

The rest of §11.3 (identity, invalidation, recovery, effect timing) and the protocol pieces of §12.
Those are decided through a conformance suite that runs on both renderers; 3a doesn't change them.

### 13.6 Phase 3b: one child diff, and a conformance suite

**Identity** (§11.3) is settled by sharing the algorithm. MAUI's engine reconciles a node's children
through the core's `ChildReconciler`, the diff WinUI uses, with child nodes as its "native"
children (`ReactorRoot.ChildHost`). Keyed children match by key with the minimal moves for a
reorder (LIS); unkeyed children match by slot, so a conditional child that appears or disappears
doesn't shift its siblings. An unkeyed child among keyed ones matches by its absolute position and
type, which is WinUI's rule. MAUI used to match it by position among the unkeyed children, so
moving it past a keyed sibling kept its state; now it remounts, as on WinUI.

The engine's host answers the diff's questions with constants where MAUI has no equivalent:

- no exit transitions or ambient animation;
- no structural-skip hints;
- no theme re-resolution on a skip.

A child whose element is unchanged is skipped unless it is dirty itself; its dirty descendants
render in the engine's batch, as before. Duplicate sibling keys still throw on MAUI, where WinUI
reports them (§13.7). A slotted host still gets one group per declared child, including null and
empty ones, which the diff mounts no node for: a navigation stack finds its modals by position.

**The conformance suite** is one table of scenarios and expected results,
`tests/_shared/RendererConformanceScenarios.cs`, compiled into both renderers' suites: MAUI's
`RendererConformanceTests` (`tests/Reactor.Maui.Engine.Tests`) and WinUI's `Conformance_Identity`
and `Conformance_Lifecycle` selftests. Each project supplies a few element factories and a runner for
its own host. It runs on the two real renderers rather than the fake core host §11.3 sketched, so it
checks each renderer end to end. Each identity scenario mounts tallies, sets their state, checks
them, re-renders a different arrangement from the root's own state, and reads which tallies kept
theirs:

- keyed siblings swapped keep their state;
- an unkeyed child moved past a keyed one is a new child;
- a conditional child before an unkeyed one doesn't reset it;
- an unkeyed child that changes type is a new child;
- unkeyed siblings keep their state when one is appended.

Run on the previous MAUI engine, only the second fails. The lifecycle scenarios are in §13.7.

### 13.7 Recovery and effect timing: renderer policy

The remaining §11.3 contracts stay renderer policy. Invalidation: render counts were never part of
the contract (§11.3), so each renderer keeps its own (WinUI re-renders the path to a state change,
MAUI only the dirty node). Recovery and effect timing: §11.3 said phase 3 would pick one; it keeps
both behind the render kernel's host instead, because each renderer's current behavior is
defensible and WinUI's can't change without breaking apps:

| | WinUI | MAUI |
|---|---|---|
| A render error outside every boundary | an in-tree fallback in the component's place; the app keeps running | the root unmounts, then `UnhandledException` is raised (or the error rethrown), as React does |
| When effects run | right after the component's own render, before its children mount | after the commit, deepest first, as React's `useEffect` does |
| Duplicate sibling keys | a diagnostic; the diff continues and matches them to the wrong controls | `InvalidOperationException` before anything changes |

What both renderers guarantee is in the conformance suite (`Conformance_Lifecycle` on WinUI,
`RendererConformanceTests.Lifecycle` on MAUI):

- an effect runs once, then cleans up and runs again only when a dependency changes, and cleans up
  when its component unmounts;
- a boundary shows its fallback for an error below it, runs the failed subtree's cleanups, leaves
  its siblings and their state alone, and retries its child when it re-renders.

An app or library that depends on the order of effects across components, or on what an
unhandled error does, depends on its renderer. Duplicate keys throw on MAUI because the shared diff
would otherwise move one child's state to another silently; WinUI keeps reporting them, since
throwing would break apps that render with them today. The hook-count check (§13.3) is MAUI-only for the
same reason: turning it on for WinUI would throw in apps that call hooks conditionally today.

## 14. Phase 3c: echo

Spec §12.3 asks for echo to be characterized before anything is shared. The MAUI device tier now
does that for the eleven controlled form controls (`Entry`, `Editor`, `SearchBar`, `CheckBox`,
`Switch`, `RadioButton`, `Slider`, `Stepper`, `Picker`, `DatePicker`, `TimePicker`), on Mac
Catalyst, the iOS simulator and the Android emulator, for both renderers
(`tests/Reactor.Maui.DeviceTests/EchoFixtures.cs`):

| Case | What MAUI does |
|---|---|
| Reactor writes a controlled control | Its change event fires synchronously inside the write and is suppressed. Nothing is reported in the 400 ms after the render either. |
| Two renders in one batch, or a render that changes nothing | Nothing is reported, and the control shows the last value. (The number of writes isn't measured.) |
| A write the control coerces (slider or stepper value out of range, `Entry` longer than `MaxLength`, picker index past the last item) | The control shows the coerced value; nothing is reported; re-rendering the same value doesn't loop. For the slider and stepper the clamping is Reactor's own (`Range.Write`); the other two are the control's. |
| User input the app refuses | Reported once, with the input value; the control returns to the declared value, and the restore isn't reported. Checked on the platform view for `Entry` and `Switch`, on the virtual view for the check box, slider and picker. |
| Input after the element is unmounted | Was reported to the unmounted component's callbacks, on every platform and both renderers. Now dropped. |

**Not characterized yet**, and owned by phase 4's control inventory: the controlled properties of
`CarouselView` (`Position`, which the platform can update while an animated scroll settles, after the
write returns), `CollectionView` selection, `RefreshView.IsRefreshing`, tabs and the flyout's
`IsPresented`. Two §12.3 cases have no fixture either: an event's payload against the control's
readback, and several controlled properties on one control (the slider's range and value are written
together, but only the value is controlled).

**What this means for sharing.** WinUI needs its value-diff arm and suppress counter because some
WinUI controls raise their change event after the write returns (`RichEditBox`, `AutoSuggestBox`),
and because a write can raise several events. None of the controls above does either: a scope around
the write (`EchoGuard`, the equivalent of WinUI's non-consuming setter scope) is exact for them.
Moving WinUI's mechanism into the core would put a seam on WinUI's hot path for no proven MAUI
benefit, so echo stays in each renderer, as §12.2 option C allows. If one of the uncharacterized
controls echoes late, the value-diff decision is the piece to extract then, with that control as its
second consumer. What is shared is the contract:

- A programmatic write is never reported as input.
- A coerced write is not reported; the control shows the coerced value and the app keeps its own.
  WinUI does the same for its range controls (the counter path). Reporting it instead would loop:
  the restore after the report writes the declared value again, which is coerced again.
- Input after unmount reaches no callback. On MAUI, handlers invoke callbacks through
  `HandlerContext.Live`, which is `null` once the element is unmounted. `IsEchoing` is also true
  then, so a third-party change event that checks it first is covered even if it reads `Current`;
  any other third-party event must use `Live`. On WinUI this isn't verified yet: its trampolines
  read the element from the control's state, which the pool and the items factory clear, but other
  unmount paths may not. A WinUI fixture for it is a follow-up.

Two consequences for MAUI apps:

- **A page the app removes doesn't report `OnDisappearing`.** Reactor unmounts it before the platform
  pops it, and the component that rendered it may already have run its cleanups. A page that is
  covered, or that the user pops before the app's state drops it, still reports it. Release
  resources in an effect's cleanup, which runs on removal.
- **A list's cells stop rendering once the list is unmounted.** The platform can still realize cells
  while a page animates out; they no longer start Reactor trees, which nothing would unmount.

**Convergence item.** MAUI handlers repeat a three-step input pattern (`IsEchoing`, invoke through
`Live`, `RestoreAfterEvent`), where WinUI's bindings resolve the element in the framework
(`ReactorBinding<TElement>`). A single `HandlerContext` dispatch helper would make the pattern
correct by construction for third-party handlers; it is part of the shared handler protocol work
(§12.3, descriptors).

## 15. Phase 3d: the handler registry

Spec §12.3's registry gate: state stays per renderer or per root, and each renderer owns its
duplicate and override policy. What the two registries share is the lookup, now one core type,
`HandlerTable<TValue>` (`src/Reactor.Core/Engine/HandlerTable.cs`):

- an element type resolves to the value registered for exactly that type;
- otherwise to the value registered for its nearest base type "for derived types" (spec 048's implementation tasks, `docs/specs/tasks/048-control-registration-and-trimming-implementation.md` §3.4);
- base-type results, misses included, are cached per element type, except for collectible types,
  whose load context the cache would keep alive. A registration for derived types clears the cache.

The table has no duplicate policy: `TryAdd` keeps the first value, `Set` replaces it.

| | WinUI `ControlRegistry` | MAUI `ElementHandlerRegistry` |
|---|---|---|
| Scope | Process-wide, filled lazily by the factories' registration latches | One per top-level root (shared with its child roots); an app can pass one instance to several roots |
| Duplicates | First wins (`TryAdd`) | Last wins (`Set`), so an app overrides a built-in handler |
| Derived types | `RegisterForDerivedTypes` (existing) | `RegisterForDerivedTypes` (new) |
| Value | A factory for the per-host adapter | The adapter |

WinUI's per-host `V1HandlerRegistry` keeps its own `Dictionary`-based lookup. It is single-threaded
and lives as long as its host, so it caches collectible types too; it stays where it is. The reconciler
reaches `ControlRegistry` after both per-host arms miss (`_v1Handlers`, then `_typeRegistry`) and
caches only hits into `_v1Handlers`, so composition primitives (`ComponentElement`, `FuncElement`, …)
reach it on every dispatch. That is why the table tests a flag, not `ConcurrentDictionary.IsEmpty`,
before walking base types: `IsEmpty` takes every lock when the dictionary is empty, the common case.
A lookup that overlaps a registration for derived types can't leave a result that later lookups use:
each such registration bumps a generation, every cached result is stamped with the generation it
was resolved under, and lookups accept only the current one. Its registration latches stay WinUI's: MAUI's registries are built
explicitly (`MauiHandlers.CreateRegistry()`, `DirectHandlers.CreateRegistry()`), and nothing a
latch does populates a MAUI root.

## 16. Phase 3e: descriptors

Spec §12.3's descriptor gate covers the order of props and children, live-element access in
events, restore-after-event and hybrid remounts. §12.2 judged extracting WinUI's descriptor
interpreter "shaped by one consumer". Writing MAUI's confirms it. What WinUI's entries mostly do is
WinUI's:

- clear a `DependencyProperty` so value precedence falls back;
- subscribe routed or typed events through the per-control trampoline that pooling needs;
- arm the value-diff echo for late echoes;
- bridge flyouts through `MountContext`.

MAUI has none of those. Its entries are a bindable-property write or clear, an echo scope (§14), and
`HandlerContext.Live`. So descriptors converge on the authoring surface, not on shared code.

MAUI gets a public `ControlDescriptor<TElement, TView>` (`src/Reactor.Maui/Handlers/ControlDescriptor.cs`).
It is a handler built from entries, with WinUI's verbs:

| Verb | WinUI | MAUI |
|---|---|---|
| A value Reactor owns | `OneWay` (and `OneWay` with `Optional<T>`, which clears the DP) | `OneWay`; `Property(BindableProperty, get)`, which clears when the value is `null` |
| Written once | `Initial` / `InitialOnly` | `Initial` |
| A value the user changes and the element declares | `Controlled` | `Controlled(get, read, write, subscribe, callback)` |
| Anything else the control reports | `HandCodedEvent`, `Imperative` | `Event((context, view) => …)` |
| Teardown, remount | `WithUnmount`, the handler's `CanUpdate` | `WithUnmount`, `WithCanUpdate` |
| Children | children strategies | `WithChildren(IChildrenStrategy)` |

The gate's four points, on MAUI:

- **Order.** On mount, a descriptor creates the view, then every entry writes its initial value, then
  the element's modifiers are applied, and only then are events wired. This is WinUI's KD-1b rule:
  no initial write can be reported as input. Children are reconciled after the handler returns, so
  props come before children, as on WinUI.
- **Live element.** A controlled entry reports through `HandlerContext.Live`, and an `Event` entry
  gets the context to do the same, so nothing reaches an unmounted element (§14).
- **Restore-after-event.** A controlled entry compares the view's value with the declared one, not
  with the old element's. The restore after a refused input (an update with the same element on both
  sides) therefore puts the declared value back, inside the echo scope.
- **Hybrid remounts.** A descriptor is an ordinary handler, so `DirectHandlers`' hybrids wrap it as
  they did the hand-written handlers. `WithCanUpdate` gives third-party controls the same remount hook.

Eight built-in Controls handlers are now descriptors (`ControlDescriptors`): `BoxView`,
`ActivityIndicator`, `ProgressBar`, `CheckBox`, `Switch`, `RadioButton`, `Editor` and `SearchBar`.
Controls with coercion or several coupled values (`Entry`'s text and selection, `Slider`, `Stepper`,
the pickers) stay hand-written for now; the same applies to WinUI's hand-coded handlers. The
descriptor is what phase 4's MAUI wrapper generator emits for third-party controls.

