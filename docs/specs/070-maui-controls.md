# Spec 070: MAUI controls (phase 4c)

Phase 4c closes the controls rows of spec 068's inventory. It lands in three gated PRs:

| PR | Scope |
|---|---|
| 4c-1 | Element refs, and the imperative commands they enable: focusing, scrolling and WebView commands from code; cancelling a WebView navigation; `Entry` cursor and selection; tests for `ScrollView` and `RefreshView` |
| 4c-2 | Collections and items: CollectionView layouts, spacing, sizing, header, footer, empty view, threshold, grouping and selection state in items; CarouselView and IndicatorView; SwipeView items and opening from code; Picker item display; RadioButton with view content; Label spans |
| 4c-3 | Shapes, gradient brushes, ContentView, image source equality, tests for every remaining 🧪 control and layout, and the §1 audit of every ✅ row of spec 068 |

This spec details 4c-1 (§2). §3 and §4 are written when 4c-2 and 4c-3 start. The bar for each item
is spec 068 §1.

## 1. Scope

- **4c-1, refs and imperative commands.** `UseElementRef<T>()` and `.Ref(...)` on MAUI; focusing
  from code (`ref.Current?.Focus()`); scrolling from code (`ScrollToAsync`); WebView `Reload`,
  `GoBack`, `GoForward` and `EvaluateJavaScriptAsync`; an `OnNavigating` callback that can cancel;
  `CursorPosition`, `SelectionLength` and `OnSelectionChanged` on `Entry`, on both registries;
  `Entry` return type tests; headless and device tests for `ScrollView`, `RefreshView` and `WebView`.
  Rows it closes in spec 068: Element refs, Focus and keyboard, Entry, ScrollView and RefreshView,
  WebView.
- **4c-2, collections and items** (§3, later). It reuses 4c-1's refs for `CollectionView.ScrollTo`,
  `CarouselView.ScrollTo` and `SwipeView.Open`/`Close`.
- **4c-3, shapes, brushes, ContentView, image equality, tests and the audit** (§4, later).

## 2. 4c-1: element refs and imperative commands

### 2.1 What WinUI has (the source of truth)

- **`ElementRef`** (`src/Reactor/Input/FocusManager.cs`, namespace `Microsoft.UI.Reactor.Input`): an
  untyped cell whose `Current` is a `FrameworkElement`. It carries an internal `ExpectedType`, an
  internal `CurrentChanged` event, and batching through `ReferenceDirtySet`, which exist for spec
  057's reference edges (`LabeledBy`, `XYFocus*`, descriptor `Reference(...)` entries).
- **`ElementRef<T> where T : FrameworkElement`**: a typed wrapper, `Current => inner.Current as T`,
  with an implicit conversion to `ElementRef`. Its constructor is internal; `TypedElementRef.Create<T>()`
  makes one outside a render.
- **`UseElementRef<T>()`** (`src/Reactor/Hooks/UseElementRef.cs`): `UseMemo` with no dependencies,
  so the same instance every render.
- **`.Ref(ElementRef)` and `.Ref<T, TElement>(ElementRef<TElement>)`** on any `Element`
  (`ElementExtensions.cs`), stored in `ElementModifiers.Ref`.
- **The reconciler** sets the cell on every mount and update (`ApplyModifiers`), clears the old cell
  when the element's ref changes, and on unmount clears it only while it still names the unmounting
  control (`CleanupReferenceStateForUnmount`). `ModifiersEqual` compares `Ref` by reference so a
  ref-only change is never shallow-skipped (`Element.cs`). A typed ref on the wrong control type is a
  `Debug.Fail` in DEBUG builds of Reactor and a silent `null` otherwise (`AssertTypedRefMatch`).
- **Focus helpers**: `FocusManager.Focus(ElementRef, FocusState)`, `FocusAsync`, and
  `UseElementFocus()`, which returns `(ElementRef, Action RequestFocus)` and enqueues the focus on the
  UI dispatcher captured at render.
- **WebView2** has `OnNavigationStarting(Action<Uri>)` and `OnNavigationCompleted(Action<Uri>)`;
  neither can cancel. **TextBox** has `SelectionStart`, `SelectionLength` (`int?`, written when the
  declared value changes) and `OnSelectionChanged(Action<string, int, int>)` (selected text, start,
  length), a fire-only event.

Found while reading: WinUI's update path clears the old cell without the owner check its unmount
path has, so a ref moved in one render from a later sibling to an earlier one may end `null`. It is
unverified and out of scope (§2.13).

### 2.2 The split: nothing moves to the core

WinUI's `ElementRef` can't move: its public `Current` is a `FrameworkElement`, and the public API is
fixed. What MAUI needs is small: a cell, an owner, and a type check. WinUI wouldn't call a shared
cell (its cell also feeds reference edges and the dirty set), so there is no second consumer, and
spec 069's rule applies: share only what both renderers use.

| Where | What |
|---|---|
| Core | Nothing |
| MAUI public (`Microsoft.UI.Reactor.Input`, as on WinUI) | `ElementRef<T> where T : class, IView` with `T? Current`; `UseElementRef<T>()` on `RenderContext` (in `MauiCoreHooks`) and on `Component`; `TypedElementRef.Create<T>()` for a ref made outside a render (a test, a field set up before the first render), as on WinUI; `.Ref(ElementRef<T>?)` on every `ViewElement` |
| MAUI internal (engine) | An untyped cell: the current native as `object?`, the node that bound it, and the expected type. `ViewModifiers` holds it in an internal `Ref` property; each `Node` records the cell it bound |

- **Same names, same shape.** Shared component source writes `var r = UseElementRef<X>(); … .Ref(r);
  r.Current?.…` on both renderers; only `X` differs (`TextBox` or `Entry`), as it must.
- **No untyped public `ElementRef` on MAUI.** WinUI's exists for `UseElementFocus` and reference
  edges, neither of which MAUI has. `ElementRef<IView>` is the untyped form.
- **The namespace** is WinUI's, so a shared file's `using Microsoft.UI.Reactor.Input;` compiles on
  both. It is added to the package's global usings; no MAUI type is named `ElementRef`.
- **The cell is renderer-neutral in shape** (`object?` native, engine node owner), so it moves with
  MAUI's engine if that engine ever moves to the core.

### 2.3 API sketch

```csharp
sealed class Search : Component
{
    public override Element Render()
    {
        var (query, setQuery) = UseState("");
        var (canGoBack, setCanGoBack) = UseState(false);
        var input = UseElementRef<Entry>();
        var results = UseElementRef<ScrollView>();
        var help = UseElementRef<WebView>();

        return VStack(8,
            Entry(query, setQuery, "Search").Ref(input),
            HStack(8,
                Button("Clear", () => { setQuery(""); input.Current?.Focus(); }),
                Button("Top", async () =>
                {
                    if (results.Current is { } scroll) await scroll.ScrollToAsync(0, 0, animated: true);
                }),
                Button("Back", () => help.Current?.GoBack()).IsEnabled(canGoBack)),
            ScrollView(ResultList(query)).Ref(results).Height(240),   // ResultList: the app's own content
            (WebView("https://learn.microsoft.com/dotnet/maui") with
            {
                // Runs synchronously while the platform waits for the answer; may be asked more than once.
                OnNavigating = e => e.Cancel = !e.Url.StartsWith("https://learn.microsoft.com/"),
                // Posted after MAUI has updated the history, so CanGoBack is current.
                OnNavigated = e => setCanGoBack(help.Current?.CanGoBack ?? false),
            }).Ref(help));
    }
}
```

### 2.4 Ref semantics

**Binding lives in the engine**, not in the handlers, and **the node owns what it bound**:

- After a host node mounts (once the handler has mounted it and its children are synchronized) or
  updates successfully, `ReactorRoot` compares the ref the element declares with the cell recorded
  on the node (`Node.BoundRef`). If they differ, it releases the recorded cell (clears it if the cell
  still names this node), type-checks and binds the declared one, and records it.
- Unmounting a node releases `Node.BoundRef`. It never reads `node.Element`, which the engine sets to
  the next element before the handler's update and child synchronization run
  (`ReactorRoot.Update`): a ref removed or swapped by an update that then throws is still the one
  recorded, and is released.
- Every path that drops a node goes through `Unmount(node)`: a mount that fails (the half-mounted
  node is unmounted), an update or child synchronization that throws (the boundary unmounts its
  subtree, or `FailRoot` unmounts the root), a replaced child, and root and window teardown. So
  every failure path releases.
- That covers every handler kind (Controls, descriptor, direct, hybrid, third-party), as removed-view
  release does (spec 067 §16). The engine reads the declared cell through an internal interface that
  `ViewElement` implements, so the engine tests' fake elements can implement it too.

| Case | Behaviour |
|---|---|
| Mount | `Current` is the native view, before any effect of the pass runs |
| Update, same ref | Nothing to do: the native is the same |
| Update, ref removed or swapped | The recorded cell is released (cleared if it still names this node); the new one is bound |
| Update or child synchronization throws during a removal or swap | The recorded (old) cell stays recorded and is released when the boundary or root unmounts the node; the new cell was never bound |
| Unmount | Released. Children unmount first, so in a parent component's cleanup `Current` is already `null` (as in React) |
| Ref moved to another element in one render | The owner check makes the visiting order irrelevant: `Current` ends as the new element's view |
| Remount in place (`CanUpdate` false: another element type, or the ref's type switching the hybrid's renderer, §2.5) | A child is mounted anew before the old one unmounts; a root's old tree unmounts first. Either way the owner check leaves the new binding |
| Keyed move | The node and native are kept, so `Current` doesn't change |
| One ref on several mounted elements | Unsupported and documented: the last one bound wins, and unmounting it clears the ref although others still declare it. Not detected (a bind-time check can't tell it from a move in progress) |
| A ref in an item template | Each cell is a child root, retired as described below. A ref created in the item's component belongs to that cell's tree. A ref created outside the list and attached inside the template is the "several elements" case |
| Error boundary fallback, root or window teardown | Unmount, so released |
| Hot reload, same hook shape | Hook state survives, so the ref keeps its identity; the re-render rebinds the same native, or a new one if the edit changed the element type |
| Hot reload that retypes the ref (`UseElementRef<IView>` → `<Entry>`) | The memo slot's type changes, so the hot-reload pass resets that component's hooks in place, state included (`RenderContext.AcquireMemoSlot` throws `HookOrderException`, `ComponentRenderKernel` resets and re-renders); descendants keep theirs. The render gets a new ref; the host's recorded cell differs, so the old ref is cleared and the new one bound. Documented: refs, like all hooks, survive only edits that keep the hook shape |
| Ref on a component | Doesn't compile: `.Ref` is on `ViewElement`. Pass the ref as a prop to the component, which attaches it to the view it means (React's forwarded-ref pattern). WinUI accepts `.Ref` on any `Element` |
| Ref on a page, Shell item, toolbar item or dialog | Not in 4c-1: these aren't `ViewElement`s, and their imperative APIs (dialogs, navigation, lifecycle) are already declarative. The engine binding is element-agnostic, so `.Ref` on `PageElement` later is a modifier, not a design change |

**Retirement: when a tree, and so its refs, goes away.**

- **List cells.** A cell is retired when MAUI clears or replaces its binding context with something
  that isn't an item (iOS and Mac Catalyst's `TemplatedCell2.Unbind` sets it to `null`): its child
  root unmounts, running cleanups and releasing refs. Today `ReactorCell.Render` returns early in that
  case and leaves the old tree mounted; 4c-1 fixes that. A later rebind mounts a fresh tree.
  `ReuseItemTrees` keeps a tree only across a direct rebind from one item to another (A→B), never
  across a retirement (A→null→B). A removed item's cell is retired or rebound by MAUI; either way its
  old tree goes. The list's unmount retires every cell (`ItemsHost.UnmountAll`, existing). A cell MAUI
  drops without unbinding it stays mounted until the list unmounts (documented).
- **`ReactorPage`.** Its tree unmounts only through `ReactorPage.Unmount()` (existing, documented: call
  it when the page is discarded). A page discarded without it keeps its refs bound; they are reachable
  only through that page, so they don't extend its lifetime. `ReactorWindow` unmounts on destruction.

- **No shallow-skip problem.** MAUI's engine skips only when the element instance is the same, and a
  different ref makes a different element record. A component that skips keeps its old elements and
  so its old, still correct, binding.
- **Threading.** The root renders on the UI thread, so `Current` is written there. Read it and call
  the view on the UI thread; from a background continuation, use `MainThread.BeginInvokeOnMainThread`.
  The ref does no marshaling (WinUI's doesn't either).
- **When the view can act.** `Current` is set at mount, which can be before MAUI has created the
  view's handler or the platform view is in a window (a page about to be pushed). A command then
  returns `false` or does nothing, as in plain MAUI. Call commands from events, or once the view is
  shown. Focusing on mount is decided per platform in §2.6.
- **Allocation.** Binding allocates nothing; an element without a ref costs one interface check per
  host mount and update. The MAUI benchmark (`tests/Reactor.Maui.Benchmark`) checks it is within
  noise.

### 2.5 Both registries: the ref's type picks the view

On the direct registry, an `Entry` element renders a `DirectEntry`, which is an `IEntry` but not a
Controls `Entry`. So:

- **A ref whose type the direct view doesn't satisfy renders that element as a Controls view.** The
  hybrid handler's choice gains one condition: use the direct view only when
  `expectedType.IsAssignableFrom(typeof(TDirect))`. `UseElementRef<Entry>()` gets a Controls `Entry`
  on both registries; `UseElementRef<IEntry>()` or `UseElementRef<IView>()` keeps the direct view.
  Only that element falls back, as with `.Set<TView>(...)` today. A ref whose type changes between
  renders (a different ref, or a hot-reload retype) flips the choice and remounts, as any hybrid
  switch does.
- **Direct views can be focused.** `DirectView.Focus()` returns `false` today. It becomes MAUI's own
  path: create a `FocusRequest`, invoke the handler's `Focus` command, return the request's result;
  `Unfocus()` invokes the `Unfocus` command. So `UseElementRef<IView>()` plus `Current?.Focus()`
  works on both registries without a fallback.
- **A type mismatch is an error in every build.** Binding `ElementRef<Entry>` to an element whose
  native isn't an `Entry` (a `Label`, or a third-party handler's view) throws an
  `InvalidOperationException` naming both types, before anything is bound; it goes to the error
  boundaries like a render error. WinUI only asserts in DEBUG builds of Reactor, which an app using
  the Release package never sees; a silently `null` ref is the bug the check exists for. The check
  is one `IsInstanceOfType` per bind.

### 2.6 Imperative commands: MAUI's own API through the ref

Reactor adds no wrappers except where MAUI's API is wrong for Reactor's trees (scrolling to an
element); the ref gives the Controls (or Core) view, and the app calls MAUI:

| Need | Call |
|---|---|
| Focus, unfocus | `Current?.Focus()` (returns whether it worked), `Current?.Unfocus()` |
| Scroll to a point | `ScrollView.ScrollToAsync(x, y, animated)`; `ScrollX`, `ScrollY` |
| Scroll to an element | Reactor's `ScrollView.ScrollToAsync(ElementRef<T> target, ScrollToPosition position, bool animated)` (below) |
| WebView | `Reload()`, `GoBack()`, `GoForward()`, `CanGoBack`, `CanGoForward`, `EvaluateJavaScriptAsync(script)` |

- **Scrolling to an element needs a bridge.** MAUI's `ScrollToAsync(Element, …)` sums `X` and `Y` up
  the target's `RealParent` chain (`ScrollView.GetCoordinate`). In a direct layout, a Controls child's
  logical parent is the outer Controls host (`DirectLayout.AnchorChild` sets it, so styles and
  resources reach it), and a direct view has no Controls parent at all, so the sum skips every direct
  layout's offset. Reactor therefore adds one extension, which instance-method resolution never
  confuses with MAUI's (its parameter is a ref, not an `Element`):
  - it walks the target's **visual** parents up to the scroll view, summing each one's `Frame`
    origin: a direct view's `Parent`; for a Controls view inside a direct layout, the layout it was
    attached to (recorded by `DirectLayout.Attach` in an internal weak table, since its `Parent` must
    stay the anchor); otherwise the Controls `Parent`;
  - it applies `ScrollToPosition` as MAUI's `GetScrollPositionForElement` does (`Start`, `Center`,
    `End`, `MakeVisible`), then calls `ScrollToAsync(x, y, animated)`;
  - it works for any target the ref names, Controls or direct, on both registries; with no handler or
    no layout yet it returns without scrolling, as MAUI does.

  MAUI's own element overload is documented as correct only when every layout between the target and
  the scroll view is a Controls layout, which is always the case on the Controls registry. It can't
  be intercepted, so the documentation points to the ref overload, and a device fixture shows why.
- **WebView `Url` and `Html`** aren't controlled values (§2.7).
- **Focus on mount is a per-platform capability, decided by measurement.** A device fixture focuses
  a newly shown page's entry from (a) the view's `Loaded` event, the positive control that must work
  on every platform, and (b) the component's first effect. The spec records (b)'s result per platform
  in §2.14, and the fixture asserts that recorded result, so a platform change is noticed. If (b)
  fails on any platform, MAUI gets WinUI's `UseElementFocus()` with its name and shape,
  `(ElementRef<IView> Ref, Action RequestFocus)`, whose `RequestFocus` focuses at once when the view
  is loaded and otherwise on its `Loaded`; if it works everywhere, no API is added. Not mirrored
  either way: `FocusManager.Focus(ref, state)`, whose `FocusState` is WinUI's; `Current?.Focus()` is
  the whole call on MAUI.
- **`.Set<TView>(...)` stays the way to configure a view on every render.** A ref is for calls made
  outside render. Capturing the view from `.Set` into a field is stale after a remount and forces the
  Controls view.

### 2.7 WebView: navigations, cancellation and the source

```csharp
public Action<WebNavigatingEventArgs>? OnNavigating { get; init; }   // new
public Action<WebNavigatedEventArgs>? OnNavigated { get; init; }     // was Action<string>
```

**Attempted and completed navigations are different events.**

- **`OnNavigating` is an attempt.** MAUI's arguments: the URL, the kind (`NewPage`, `Back`,
  `Forward`, `Refresh`) and `Cancel`. It is synchronous: MAUI raises it inside the platform's decision
  callback and reads `Cancel` when the handler returns, so setting it after an `await` does nothing.
  - It is **not exactly once**, because MAUI doesn't give that. On Android a link can be asked in
    `ShouldOverrideUrlLoading` and again in `OnPageStarted` (MAUI skips the second only when the first
    allowed it), and a navigation started by loading a URL is asked only in `OnPageStarted`, after
    loading began (a cancel there stops it). The callback must give the same answer for the same URL
    and must not count calls.
  - **Navigations started by Reactor** (a changed `Url`) are reported too, so one callback sees every
    navigation, with the guarantee above. Cancelling one leaves the previous page; the declared `Url`
    isn't loaded again until it changes.
  - After unmount the callback isn't called (`context.Live`), and the navigation is allowed.
  - A throwing callback cancels the navigation, and the exception goes to the error boundaries
    (`HandlerContext.ReportError`), as a throwing navigation guard does (spec 069 §2.6). It must not
    escape into the platform's callback, which on iOS would crash the app.
- **`OnNavigated` is a completion, with its result.** It now passes MAUI's `WebNavigatedEventArgs`,
  whose `Result` is `Success`, `Cancel`, `Failure` or `Timeout`: Android reports a cancelled
  navigation as a completion with `Cancel`, and both platforms report failures. Keeping
  `Action<string>` would have to either hide those or report them as successes. A breaking change to
  a preview API, chosen for one callback in MAUI's type, symmetric with `OnNavigating`.
  - **Posted** to the root's scheduler, never invoked inside the platform callback, because Android
    updates `CanGoBack` and `CanGoForward` after raising `Navigated` (`MauiWebViewClient.OnPageFinished`).
    A callback that reads the history reads the updated values; dropped if the element unmounted
    before it runs.

**The source.** `Html`, when set, wins over `Url` (as today). Neither is a controlled value: a link the
user follows, or `GoBack`, isn't undone. Reactor loads the declared source when it changes, except
for one echo:

- **A declared `Url` equal to the page the view has settled on isn't loaded again.** An app that
  stores the completed URL in state and renders it back would otherwise reload the page it shows.
  The handler keeps, per native view, the URL of the last `Success` completion and whether a
  navigation is pending (an attempt allowed and not yet completed). A changed `Url` is skipped only
  when `Html` is `null` in both the previous and the new element, no navigation is pending, and the declared string is ordinally equal to that
  settled URL as MAUI reported it; anything else navigates. So:
  - A shown, B pending, declaration changes to A: B is pending, so A is loaded;
  - a redirect from A to A′ settles on A′; rendering A′ back is skipped, rendering A again loads A;
  - a cancelled or failed navigation doesn't change the settled URL;
  - `Html` → `Url` always loads: the rule applies only when the previous declaration was a `Url` too;
  - a remounted view has no settled URL, so it loads.

### 2.8 `Entry` cursor and selection

```csharp
public int? CursorPosition { get; init; }
public int? SelectionLength { get; init; }
public Action<string, int, int>? OnSelectionChanged { get; init; }   // selected text, cursor, length
```

- **Names.** The properties keep MAUI's names (`CursorPosition`, not WinUI's `SelectionStart`), as
  `EntryElement` already does throughout (`Text`, `OnTextChanged`, `Placeholder` against WinUI's
  `Value`, `OnChanged`, `PlaceholderText`). The event has no MAUI counterpart, so it takes WinUI's
  name and shape.
- **`null` means uncontrolled**: Reactor never writes the selection, and the user moves it freely.
  `OnSelectionChanged` still reports.
- **A value is controlled**, per the echo contract (spec 067 §14): Reactor's writes aren't reported, a
  coerced write (a position past the text, clamped by the platform) isn't reported and doesn't loop,
  a user's change is reported and restored if the app doesn't take it (so an app that controls the
  cursor updates it from `OnSelectionChanged`; typing moves the caret), and nothing is reported after
  unmount.

**Characterize first.** MAUI's selection notifications don't all arrive inside Reactor's write:

- Android posts `SetSelection` when the field is focused (`EditTextExtensions.UpdateCursorSelection`),
  so its notification arrives after a synchronous echo scope has closed, clamped to the text length
  at that time;
- Android's keyboard, password and input-type updates restore the previous selection
  (`SetInputType` ends with `SetSelection`), and a `MaxLength` update can truncate the text; these
  are written outside the text write today (`ControlHandlers.cs`, `EntryHandler.Apply`);
- iOS writes the cursor before the text when the user types, Android the text before the cursor, and
  both write `CursorPosition` and `SelectionLength` as two separate property changes.

The first implementation step is a device characterization fixture, on all three platforms and
both registries: focused and unfocused; cursor only, length only, both; a position past the end;
two writes in one batch and in consecutive renders; a text and selection write together; keyboard,
password and `MaxLength` changes while focused. Its oracle is the final platform selection **and
zero callbacks after settling** (400 ms, as spec 067 §14's fixtures, then one more dispatcher turn).
§2.14 records the table. The mechanism below is the design for what reading MAUI predicts; the
characterization confirms or corrects it before the handler is written.

**Reactor's writes.**

- **The whole selection-affecting apply is one echo scope**: keyboard, password, `MaxLength`, text,
  cursor and length are written inside one `WriteSuppressed`, in that order, each compared with the
  view.
- **Expected writes survive deferral.** Each selection write records what the platform will report
  for it (cursor and length clamped to the text length then) in a short per-control queue. A later
  notification that matches a queued expectation is Reactor's echo: it is consumed, with every older
  expectation, and not reported. Two consecutive writes while focused post two `SetSelection`s; the
  first one's notification matches the older expectation, so neither is reported. A user's change
  matches nothing and is reported. An expectation left unmatched is harmless: the report it could
  swallow would equal a value Reactor just declared.
- This is the late-echo case spec 067 §14 anticipated ("the value-diff decision is the piece to
  extract then"). 4c-1 builds it inside MAUI's `EntryHandler` and `DirectEntry` only; whether its
  shape matches WinUI's `PendingEchoMatch` closely enough to extract is assessed in §2.14, after
  characterization. WinUI isn't changed in 4c-1.

**User input: one transaction, reported before anything can overwrite it.**

- When the element uses the selection (`CursorPosition`, `SelectionLength` or `OnSelectionChanged`
  set), text and selection changes that aren't echoes are captured into one pending **input
  transaction** per control: the view's text, cursor and length, re-read at each notification.
- The first notification posts the transaction's flush to the root's scheduler. Until it runs, the
  handler's apply **leaves text and selection unwritten**, so no render (a restore, or another state
  change) can overwrite the input before it is reported.
- The flush reports `OnTextChanged` if the text differs from the declared text, then
  `OnSelectionChanged` if the selection differs from the declared selection, then requests one
  restore. The platform's notification order doesn't matter, and there is never a report with the
  new cursor and the old length. An app that accepts both values through its callbacks ends with the
  platform showing the user's values and no restore write; one that refuses gets the declared values
  back, and the restore isn't reported.
- Dropped if the element unmounted before the flush runs.
- An `Entry` that doesn't use the selection keeps today's synchronous text report.

**Both renderers.** `DirectEntry` implements `IEntry.CursorPosition` and `SelectionLength` as plain
properties the platform handler writes and Reactor ignores. Their setters raise a notification as
`Text`'s does, and Reactor writes through a `SetSelection` that remaps both properties inside the
same scope.

**Rejected: WinUI's write-when-the-declaration-changes.** Writing only when the declared value differs
from the previous element means setting the same position twice does nothing, and a refused move
isn't restored, so it isn't controlled (decision 4).

### 2.9 Tests

Each oracle names the mutation that must turn it red; the implementation PR runs each mutation once
and records it in §2.14. Counts are positive witnesses: a test that asserts something was cleared
also asserts that the unmount, retirement or recycling it depends on happened.

**Headless (`tests/Reactor.Maui.Tests`), both registries where the element is hybrid:**

| Test | Oracle | Mutation |
|---|---|---|
| Ref set at mount, released at unmount | `Current` is the host's view (same instance), then `null` after a conditional removal | skip the bind; skip the release |
| Set before effects | a mount effect reads a non-null `Current` | bind after the pass's effects |
| Update throws during a ref removal, and during a swap; child synchronization throws during each | under a boundary: the fallback shows, the old ref is `null`, the new ref was never set; at the root likewise after `FailRoot` | release from `node.Element` instead of `Node.BoundRef` |
| Boundary fallback with a bound ref | the boundary's subtree unmounted (count) and the ref is `null` | skip the release on unmount |
| Ref moved between siblings, both orders | `Current` is the new element's view | drop the owner check |
| Ref removed, and swapped for another ref, on a mounted element | old ref `null`, new ref the view | skip the release |
| Renderer switch by ref type, at the root and as a child | swapping `ElementRef<IView>` for `ElementRef<Entry>` on the direct registry remounts: the new ref names a Controls `Entry`, the old one is `null`; and back to a `DirectEntry` | drop the hybrid condition; drop the owner check |
| Keyed reorder | `Current` is the same instance | none needed (the keyed tests cover the move) |
| Type mismatch | the boundary receives an error naming both types, and no ref is bound | skip the check (`Current` silently `null`) |
| Hot reload, same shape | same ref, same view | — |
| Hot reload that retypes the ref | the component's hooks reset (its state is the initial value); the old ref is `null`, the new one names the view; a child component keeps its state | release keyed on the element instead of the node |
| `TypedElementRef.Create<T>()` | a ref made outside render binds and releases like a hook's | — |
| Cell retirement: A→`null`→B, item removal, list unmount, with and without `ReuseItemTrees` | the item component's cleanup ran (count ≥ 1) and its ref is `null`; B mounts a fresh tree (a new ref); with `ReuseItemTrees`, A→B directly keeps the tree and A→`null`→B doesn't | return early on a `null` context (today's code) |
| `ReactorPage.Unmount` | the tree's cleanups ran (count ≥ 1) and its refs are `null` | — |
| `DirectView.Focus` | invokes the handler's `Focus` command and returns its result (stub handler) | return `false` |
| Scroll-to-element bridge | for a target in a direct `Grid` (nonzero row, margin, padding) inside a direct `VStack` inside the scroll view, with frames the test assigns (headless views have no platform layout), the computed point equals the summed frames for each `ScrollToPosition`; MAUI's `GetScrollPositionForElement` on the same Controls target differs (the reason for the bridge) | walk `Parent` instead of the visual parent |
| WebView `OnNavigating` | `IWebView.Navigating(...)` (what the platform handlers call) returns cancel as decided; not called and allowed after unmount; a throw cancels and reaches the boundary | ignore `Cancel`; invoke through `Current` instead of `Live`; let the exception escape |
| WebView `OnNavigated` | each `Result` is passed through; the callback runs after the scheduler turn, not inside `IWebView.Navigated` | invoke synchronously |
| WebView source and its echo | `Url`, `Html` set, replaced, cleared; a `Url` equal to the settled URL isn't loaded; with a navigation pending, it is; after a cancelled or failed completion, the settled URL is unchanged; a redirect's settled URL is the redirected one; `Html` → `Url` loads; a remounted view loads | drop the pending check; compare with the last attempt instead of the last success |
| `Entry` selection writes, both registries | written at mount and update, never when `null`; the whole apply is one scope (a keyboard change that moves the selection isn't reported); a queued expectation consumes a deferred notification (simulated by a handler stub that notifies after the scope), including two consecutive writes; a non-matching notification is reported | write without the scope; drop the expectation queue; keep only the newest expectation |
| `Entry` input transaction, both orders | text then cursor then length (Android), and cursor then text (iOS), each with callbacks that accept both values: one `OnTextChanged`, then one `OnSelectionChanged` with both final values, the view keeps the user's values, and zero callbacks after the scheduler settles; a render requested between the notification and the flush doesn't overwrite them; refused: declared values restored, restore not reported; nothing after unmount | report per notification; let apply write while a transaction is pending; read the values at flush instead of at notification |
| `Entry` without selection use | the text report stays synchronous | — |
| `Entry` return type, both registries | set, updated and cleared to `Default`; `OnCompleted` reported | — |
| `ScrollView` | orientation and scroll bar visibility set, updated, cleared; `OnScrolled` reported from `SetScrolledPosition` | — |
| `RefreshView` | mount with `IsRefreshing` true; toggling not reported; `RefreshColor` set and cleared; a handler-style refresh (`IRefreshView.IsRefreshing = true`) reported once, kept when the app accepts, restored and not re-reported when it refuses; nothing after unmount; content set, replaced, cleared (a direct view hosted in the wrapper on the direct registry) | write outside the echo scope; skip the restore |

**Engine (`tests/Reactor.Maui.Engine.Tests`):** the binding rows above that don't need MAUI (move,
remount in place, keyed move, a throwing update or child sync, boundary) on the fake renderer, with a
fake element that implements the ref interface, so they hold for any handler.

**Device (`tests/Reactor.Maui.DeviceTests`), Mac Catalyst, iOS and Android, `[controls]` and
`[direct]` unless noted.** Every fixture that checks for "no report" waits until it settles (400 ms
and a dispatcher turn) and asserts a callback count of zero, not only the final state.

| Fixture | Oracle |
|---|---|
| `Ref_FocusFromAnEvent_FocusesThePlatformView` | `ElementRef<Entry>` and, on direct, `ElementRef<IView>`: after a tap, the platform view is first responder (iOS, Catalyst) or `IsFocused` (Android), and `Focus()` returned `true` |
| `Ref_FocusOnMount_PerPlatform` | on a newly shown page, focus from `Loaded` works on every platform (positive control); focus from the first effect matches the result §2.14 records for this platform |
| `ScrollView_ScrollToFromARef_ReachesThePlatform` | content several viewports tall (no clamping): after `ScrollToAsync(0, 300, false)` the platform offset (`ContentOffset.Y`, or `ScrollY` in device-independent units) is 300 ± 1 and `OnScrolled` reported it; horizontal likewise on x |
| `ScrollView_ScrollToAnElement_ThroughNestedDirectLayouts [direct]` | a target in a direct `Grid` (nonzero row offset, margins, padding) in a direct `VStack`, far from both ends: after the bridge with `Start`, the target's top measured on the platform (its platform frame converted to the scroll view's viewport) is 0 ± 1; `Center` and `End` likewise. MAUI's own element overload is also run, and the fixture records its error, showing why the bridge exists |
| `ScrollView_ScrollBars_ReachThePlatform` | the platform's indicator flags follow the declared visibility |
| `RefreshView_UserRefresh_IsReported_AndRefusalStopsTheSpinner` | a refresh triggered through the platform control (`UIRefreshControl` value-changed action; the `SwipeRefreshLayout` listener) is reported once; the platform spinner follows the declaration; refused, it stops, and zero further callbacks after settling. If Catalyst has no pull-to-refresh, the fixture records that and the inventory says so |
| `WebView_Navigations_AreAttemptedAndCompleted` | bundled HTML pages (no network). Positive control: a link from page 1 to page 2 is allowed, completes with `Success`, `document.title` is page 2's, and `CanGoBack` read in `OnNavigated` is `true`. Cancelled, each started a different way (a link, `EvaluateJavaScriptAsync("location.href=…")`, `Reload()`, `GoBack()`): the page stays (`document.title` unchanged), any completion reported carries `Result.Cancel`, and none carries `Success` |
| `WebView_CommandsThroughARef` | `GoBack` and `GoForward` move between the two pages; `Reload` re-runs page 1's script (a `sessionStorage` counter reads 2); `EvaluateJavaScriptAsync` returns a value |
| `WebView_SettledUrlRenderedBack_IsNotReloaded` | storing the completed URL in state and rendering it back causes no second load (the page's load counter stays 1); declaring page 1 again while page 2 is pending loads page 1 |
| `Entry_Selection_Characterization` | the matrix of §2.8; per case, the final platform selection and zero callbacks after settling |
| `Entry_Selection_UserMoves_AreReportedOnce` | a selection set on the platform view (what a user's tap does) is reported once with final values; typing reports text, then selection; an app that accepts both keeps the platform at the user's values; refused, the platform selection returns; zero further callbacks after settling |
| `Entry_ReturnType_ReachesThePlatform` | `ReturnKeyType` (iOS, Catalyst) and `ImeOptions` (Android) follow the declaration |
| `CollectionView_RefsInItemComponents_FollowTheirCells [controls]` | scroll until at least one cell instance has rendered two different item keys (identity reuse, asserted before anything else) and at least one item component has unmounted (count ≥ 1); then every live item component's ref names a cell showing its item, and every unmounted one's ref is `null` |

### 2.10 Gate for 4c-1

- **WinUI** isn't changed: `skills/reactor.api.txt` is byte-identical, and its tiers stay green.
- **MAUI**: headless, engine and device suites green on Mac Catalyst, iOS and Android; each §2.9
  mutation turns its oracle red; the characterization table and the focus-on-mount results are in
  §2.14; the MAUI benchmark within noise.
- **Docs**: `src/Reactor.Maui/README.md` documents refs (including multi-attach, retirement and hot
  reload), the commands and the scroll bridge, the WebView events, the URL echo rule and the
  selection properties; spec 068's rows (Element refs, Focus and keyboard, Entry, ScrollView and
  RefreshView, WebView) are updated with their evidence.

### 2.11 Decisions to review

1. **Nothing moves to the core**; MAUI gets WinUI's names in WinUI's namespace (§2.2). Rejected:
   moving WinUI's `ElementRef` (its public `Current` is a `FrameworkElement`); a neutral core cell
   that only MAUI calls (no second consumer).
2. **`T : class, IView`, not `VisualElement`.** `IView` covers Controls views and direct views, and a
   Controls-typed ref makes that one element a Controls view (§2.5). Rejected: `VisualElement`,
   which would force every referenced direct element to Controls, including a plain focus target.
3. **A type mismatch throws in every build**, where WinUI asserts in DEBUG only (§2.5).
4. **`Entry` selection is controlled when set**, where WinUI writes `SelectionStart` only when its
   declaration changes and never restores (§2.8). Same shape, a stricter meaning, chosen because
   the phase's bar is the echo contract.
5. **Native commands, no wrappers**, except the scroll-to-element bridge, which exists because
   MAUI's overload computes the wrong position through direct layouts (§2.6). Rejected: requiring
   Controls layouts above every scroll target, which would make the direct registry's layouts
   unusable inside a scroll view that scrolls to elements.
6. **WebView events are MAUI's argument types** (§2.7), and `OnNavigated` changes to
   `WebNavigatedEventArgs` (breaking, preview) so cancelled and failed completions are visible.
   Rejected: `Func<string, bool>` for navigating (unclear polarity, loses the kind); a Reactor
   context type (a second type for the same thing); filtering `OnNavigated` to successes (an app
   can't tell a failed load from a slow one).
7. **Binding in the engine, owned by the node** (§2.4), rather than in each handler or read from the
   element.
8. **The settled-URL echo rule** (§2.7): ordinal equality with the last successful completion, only
   with no navigation pending. Rejected: URL normalization (a false match skips a load the app
   asked for; a false miss only reloads).
9. **The selection echo is a value queue inside MAUI's handlers** (§2.8), not shared with WinUI in
   4c-1; extraction is assessed after characterization.

### 2.12 Open questions, resolved in review round 1

1. Focus on mount: measured per platform, an explicit decision recorded in §2.14 and asserted by a
   fixture; `UseElementFocus()` only if a platform needs it (§2.6).
2. A WebView URL mirrored into state: deduplicated against the settled URL (§2.7).
3. One ref on several elements: documented, not detected (§2.4).
4. `OnNavigating` sees navigations Reactor's `Url` starts, without an exactly-once guarantee where
   MAUI can't give one (§2.7).

Still open: **Label spans** are placed in 4c-2 as text items; confirm, or move them to 4c-3.

### 2.13 Out of scope, follow-ups

- **WinUI ref move.** WinUI's update path (`Reconciler.cs`, `ApplyModifiers`) clears the old cell
  without the owner check its unmount path has, so a ref moved in one render from a later sibling to
  an earlier one may end `null`. Verify with a WinUI unit test; fix in a WinUI PR if it fails.
- **Cells MAUI drops without unbinding** stay mounted until the list unmounts (§2.4).

### 2.14 As built (4c-1)

To be written when 4c-1 lands, including the selection characterization table, the focus-on-mount
result per platform, the mutation runs and the echo-extraction assessment.

## 3. 4c-2: collections and items

To be written when 4c-2 starts.

## 4. 4c-3: shapes, brushes, ContentView, image equality, tests and the audit

To be written when 4c-3 starts.
