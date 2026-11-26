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
unverified and out of scope (§2.14).

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
  it when the page is discarded). A page discarded without it keeps its refs bound to its views.
  `ReactorWindow` unmounts on destruction.
- **A ref is a strong reference.** While a ref names a view, anything that holds the ref holds the
  view, and through MAUI's parent links its tree. A hook's ref is held by its component's hook state,
  so it goes with the tree. A ref held elsewhere (made with `TypedElementRef.Create<T>()` and stored
  in a long-lived object, or captured by a long-lived callback) keeps the view alive until its tree
  unmounts and releases it; a tree that is never unmounted (a discarded `ReactorPage`) is never
  released. Documented: tear such trees down explicitly.

- **No shallow-skip problem.** MAUI's engine skips only when the element instance is the same, and a
  different ref makes a different element record. A component that skips keeps its old elements and
  so its old, still correct, binding.
- **Threading.** The root renders on the UI thread, so `Current` is written there. Read it and call
  the view on the UI thread; from a background continuation, use `MainThread.BeginInvokeOnMainThread`.
  The ref does no marshaling (WinUI's doesn't either).
- **When the view can act.** `Current` is set at mount, which can be before MAUI has created the
  view's handler or the platform view is in a window (a page about to be pushed). A command then
  returns `false` or does nothing, as in plain MAUI. Call commands from events, or once the view is
  shown. Focusing on mount is decided per platform in §2.6.2.
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
| Scroll to an element | Reactor's `ScrollToAsync(ElementRef<T> target, position, animated)` extension (§2.6.1) |
| WebView | `Reload()`, `GoBack()`, `GoForward()`, `CanGoBack`, `CanGoForward`, `EvaluateJavaScriptAsync(script)` |

- **WebView `Url` and `Html`** aren't controlled values (§2.7).
- **`.Set<TView>(...)` stays the way to configure a view on every render.** A ref is for calls made
  outside render. Capturing the view from `.Set` into a field is stale after a remount and forces the
  Controls view.

#### 2.6.1 Scrolling to an element: a bridge

MAUI's `ScrollToAsync(Element, …)` sums `X` and `Y` up the target's `RealParent` chain
(`ScrollView.GetCoordinate`). In a direct layout, a Controls child's logical parent is the outer
Controls host (`DirectLayout.AnchorChild` sets it, so styles and resources reach it), and a direct
view has no Controls parent at all, so the sum skips every direct layout's offset. Reactor adds one
extension, which overload resolution never confuses with MAUI's (its parameter is a ref):

```csharp
public static Task<bool> ScrollToAsync<T>(this ScrollView scrollView, ElementRef<T> target,
    ScrollToPosition position, bool animated) where T : class, IView;
```

- **The visual chain.** From the target up to the scroll view's `Content`, each step goes to the
  visual parent: a direct view's `Parent`; for a Controls view inside a direct layout, the layout it
  was attached to; otherwise the Controls `Parent`. `DirectLayout.Attach` records a Controls child's
  layout in an internal weak table (its `Parent` must stay the anchor). `Detach` removes the entry
  only if it still names this layout, so a child moved to another layout in the same pass, attached
  there before it is detached here, keeps its new entry.
- **Ancestry is validated.** If the walk ends without reaching this scroll view's content (the target
  is in another scroll view, another page, or a detached subtree whose parent links remain), it
  throws `InvalidOperationException` naming the target and the scroll view: a programming error, not
  a timing one.
- **Readiness: the whole chain is arranged.** It returns `false` without scrolling when the ref is
  empty, or when the scroll view, the target or any view in the chain between them has no handler or
  hasn't been arranged. A Controls view is arranged once its `Width` and `Height` are no longer `-1`;
  a `DirectView` gets an internal flag set by its first `Arrange` (its default `Frame` of zero can't
  tell). Frames are read as they are: an ancestor invalidated but not yet re-arranged gives a stale
  position, so the documented call site is after layout (the target's `Loaded` or `SizeChanged`, or
  an event). A target inserted by a render into an already arranged scroll view therefore returns
  `false` when called in the same turn as that render, and scrolls correctly once the next layout
  pass has arranged it. MAUI instead stores a request until the scroll view gets a handler; the
  bridge doesn't defer, which would need a readiness signal for every view in the chain and a way to
  settle at teardown.
- **All-Controls chains use MAUI's own overload.** When the target and every view in the chain are
  Controls `VisualElement`s whose `Parent` is the next view in the chain (always so on the Controls
  registry), the bridge calls MAUI's `ScrollToAsync(Element, position, animated)` on the target, so
  it matches MAUI by construction, insets included.
- **Any non-Controls member makes the chain computed**, the target included: a `DirectEntry` (an
  `ElementRef<IEntry>`) directly inside a Controls-fallback stack is computed, as is a Controls view
  inside a direct layout. The bridge sums each view's `Frame` origin up to the content and adds the
  content's origin, which gives the target's rectangle in the coordinates MAUI's `GetCoordinate`
  produces. **The position math is then MAUI's own**: the reference is MAUI 11.0.0 (commit
  `4e6582f`), whose `ScrollView.GetScrollPositionForElement` applies the effective viewport insets
  and the content-coordinate insets before the `MakeVisible` containment test and the `Center` and
  `End` offsets, and returns content coordinates that the iOS handler translates to native offsets
  (`GetTargetContentOffset`). Those insets come from internal types (`IScrollViewportProvider`,
  `MauiScrollView.SafeAreaBakedIntoContent`), so instead of re-deriving them, the bridge calls that
  public method with a handler-less, parentless stand-in `View` arranged at the target's rectangle:
  `GetCoordinate` of a parentless element is its own `X` and `Y`, so MAUI's method runs on exactly
  the bridge's coordinates. The resulting point goes to `ScrollToAsync(x, y, animated)`; nothing
  subtracts an inset a second time. The gate fixture compares equivalent trees (the same geometry
  from Controls layouts and from direct layouts) with nonzero iOS insets, for all four positions,
  including `MakeVisible` from above, from below and already visible.
- **`Orientation.Neither`, and any case that requests nothing** (an empty ref, not ready),
  complete with `false` at once, without raising `ScrollToRequested`.
- **The task means "confirmed".** MAUI's `ScrollFinished` carries no request identity, and on
  Android each request runs its own animator, so an earlier request's finish can arrive while a later
  one animates. MAUI's own tasks can also stay pending (a newer request cancels the previous task only
  if its status is `Running`, which a `TaskCompletionSource` task never is). So the bridge returns its
  own task, one outstanding per scroll view, with these rules:
  - it completes `true` on a finish only when the scroll position (`ScrollX`, `ScrollY`) matches its
    requested target within one device pixel on each scrollable axis, or the axis is at the end of
    its range in the direction of a target beyond that end (the platform clamped it);
  - it completes `false` when superseded (a later bridge call, or any `ScrollToRequested` the scroll
    view raises for another request), when the scroll view unmounts or its handler disconnects, when
    dispatching the request throws (its state is removed first), or when a finish arrives at a
    non-matching position with no earlier request's finish still expected (the scroll ended
    elsewhere, for example under the user's finger);
  - a finish at a non-matching position while an earlier request's finish is still expected is that
    earlier request's, and is ignored. How many finishes overlapping requests produce is
    characterized per platform, and the count of expected finishes follows the measurement;
  - finishes are observed through Reactor's scroll view (`ReactorScrollView`, which the
    `ScrollView` element now renders), which re-implements `IScrollView.ScrollFinished` to note the
    finish before MAUI's own handling. On a scroll view Reactor didn't create, the bridge awaits
    MAUI's task once and applies the same position check.

  It never throws for supersession. Documented: MAUI's own overloads, called directly, may leave a
  superseded `await` pending; the bridge's `false` means "not confirmed at the requested position",
  not "didn't move".

MAUI's own element overload is documented as correct only when every layout between the target and
the scroll view is a Controls layout, which is always the case on the Controls registry.

#### 2.6.2 Focus on mount: a per-platform capability

A device fixture measures it, using **independent mounts**: one page whose entry is focused from its
component's first effect, and a separate page, mounted fresh, whose entry is focused from the view's
`Loaded` event, the positive control that must work everywhere. Neither page has another focusable
view, so one measurement can't help the other. §2.15 records the effect result per platform, and the
fixture asserts that recorded result, so a platform change is noticed.

- **If the effect fails on any platform**, MAUI gets WinUI's `UseElementFocus()` with its name and
  shape, `(ElementRef<IView> Ref, Action RequestFocus)`. `RequestFocus` focuses at once when the view
  is ready, and otherwise when it becomes ready. Ready means: the view's handler is connected and its
  platform view is in a window. A Controls view signals it with `Loaded`. A `DirectView` has no
  `Loaded`, so it gets an internal readiness notification raised from its handler being set and its
  platform view reaching a window (Android `ViewAttachedToWindow`; iOS and Mac Catalyst the platform
  view's `Window` becoming non-null, observed through its move to a window). A request pending at
  unmount is dropped.
- **If it works everywhere**, no API is added.
- **Not mirrored either way:** `FocusManager.Focus(ref, state)`, whose `FocusState` is WinUI's;
  `Current?.Focus()` is the whole call on MAUI.

### 2.7 WebView: navigations, cancellation and the source

```csharp
public Action<WebNavigatingEventArgs>? OnNavigating { get; init; }   // new
public Action<WebNavigatedEventArgs>? OnNavigated { get; init; }     // was Action<string>
```

**Attempted and completed navigations are different events.**

- **`OnNavigating` is an attempt.** MAUI's arguments: the URL, the kind (`NewPage`, `Back`,
  `Forward`, `Refresh`) and `Cancel`. It is synchronous: MAUI raises it inside the platform's decision
  callback and reads `Cancel` when the handler returns, so setting it after an `await` does nothing.
  - **Not exactly once**, because MAUI doesn't give that: on Android a link can be asked in
    `ShouldOverrideUrlLoading` and again in `OnPageStarted` (MAUI skips the second only when the
    first allowed it). The callback must give the same answer for the same URL and must not count
    calls.
  - **Navigations started by Reactor** (a changed `Url`) are reported too.
  - After unmount the callback isn't called (`context.Live`), and the navigation is allowed.
  - A throwing callback cancels the navigation, and the exception goes to the error boundaries
    (`HandlerContext.ReportError`), as a throwing navigation guard does (spec 069 §2.6). It must not
    escape into the platform's callback, which on iOS would crash the app.
- **What a cancel leaves on screen depends on when the platform asks** (from MAUI's sources; the
  device fixture confirms each row):

  | Platform, navigation | When it's asked | After a cancel |
  |---|---|---|
  | iOS and Mac Catalyst, any | before the load (`DecidePolicy`) | no load begins; the previous page stays |
  | Android, a declared `Url` (a `UrlWebViewSource` write) | before the load: `MauiWebView.LoadUrl` asks `NavigatingCanceled(url)` and only then calls the native `LoadUrl` | no load begins; the previous page stays |
  | Android, a link | before the load (`ShouldOverrideUrlLoading`) | no load begins; the previous page stays |
  | Android, a script navigation (`location.href = …`) | as characterized: `ShouldOverrideUrlLoading` where Android calls it, else `OnPageStarted` | per the row that applies |
  | Android, `Html` (`LoadDataWithBaseURL`), `Reload`, `GoBack`, `GoForward` | after the load started (`OnPageStarted`; MAUI then calls `StopLoading`) | not specified: the previous page, partly loaded content or a blank page, and the history may already hold the entry |

  Reactor promises that a cancelled navigation is reported as cancelled, and that the previous page
  stays only in the rows that say so. The declared source isn't loaded again until it changes.
- **`OnNavigated` is a completion, with its result.** It passes MAUI's `WebNavigatedEventArgs`,
  whose `Result` is `Success`, `Cancel`, `Failure` or `Timeout`: Android reports a cancelled
  navigation as a completion with `Cancel`, and both platforms report failures. A breaking change to
  a preview API, for one callback in MAUI's type, symmetric with `OnNavigating`. It is **posted**
  (§2.9), never invoked inside the platform callback, because Android updates `CanGoBack` and
  `CanGoForward` after raising `Navigated` (`MauiWebViewClient.OnPageFinished`).

**The source is requested, not controlled.** `Html`, when set, wins over `Url` (as today). Reactor
loads the declared source when the declaration changes (`Url` or `Html` differs from the previous
element's), and at mount; re-declaring the same source does nothing. A link the user follows, or
`GoBack`, isn't undone, and nothing compares the declaration with the page shown. So **`Url` is the
source the app requests, not the page the view shows**: keep the current page in separate state, and
don't render `OnNavigated`'s URL back into `Url`, which makes Reactor load it again. The README's
WebView snippet shows the pattern:

```csharp
var (requested, setRequested) = UseState("https://learn.microsoft.com/dotnet/maui");
var (current, setCurrent) = UseState<string?>(null);   // what the view shows; never fed back into Url
WebView(requested) with { OnNavigated = e => { if (e.Result == WebNavigationResult.Success) setCurrent(e.Url); } }
```

### 2.8 `Entry` cursor and selection: WinUI's semantics

**Gate decision (review round 2): selection isn't controlled.** Both review families found that a
controlled caret can't be made exact on MAUI: Android writes the cursor and the length as two property
changes, clamps a posted `SetSelection` when it runs rather than when it is queued, and a queue of
expected writes can swallow a genuine move or let an old posted write overwrite accepted input. No
port needs a controlled caret. So MAUI takes WinUI's semantics; controlled selection can be revisited
later with the characterization data below.

```csharp
public int? CursorPosition { get; init; }
public int? SelectionLength { get; init; }
public Action<string, int, int>? OnSelectionChanged { get; init; }   // selected text, cursor, length
```

- **Names.** The properties keep MAUI's names (`CursorPosition`, not WinUI's `SelectionStart`), as
  `EntryElement` already does throughout (`Text`, `OnTextChanged`, `Placeholder` against WinUI's
  `Value`, `OnChanged`, `PlaceholderText`). The event takes WinUI's name and shape; MAUI has none.
- **Writes: when the declaration changes.** A declared value is written at mount and whenever it
  differs from the previous element's, as WinUI's `SelectionStart` is; `null` is never written. It is
  never restored after user input: moving the caret twice to the same declared position needs the
  app to have taken the user's position in between, as on WinUI.
- **One echo scope for the whole apply.** Keyboard, password, `MaxLength`, text, cursor and length are
  written in one `WriteSuppressed`, in that order, so Android's input-type updates (which end with
  `SetSelection`) and a `MaxLength` truncation happen inside it.
- **Text is unchanged**: `OnTextChanged` is reported synchronously and followed by a restore, as
  today. There is no text-and-selection transaction, so selection handling can never report text.

**`OnSelectionChanged`: one posted flush per burst, against one baseline.**

The only state is a **baseline**: the selection Reactor last knew the platform to show. There is no
record of expected writes, because any such record can swallow a genuine move (a write to 8, a text
write that moves the caret to 20 before the flush, then a user's move back to 8). When a change is
ambiguous, Reactor reports it rather than risk swallowing input.

- **The baseline** is initialised at mount, after the mount's writes, from the platform. A
  programmatic selection write sets it to the write's effective value: the declared cursor and
  length clamped to the text's length at that moment. After every flush it becomes the selection the
  flush observed, reported or not.
- **Notifications start a burst.** A selection notification outside an apply posts one flush (§2.9)
  if none is pending. Notifications inside an apply's echo scope don't post; instead, an apply that
  wrote the text or the selection, or during which any selection notification arrived (a
  property-only apply such as a `MaxLength` or keyboard change counts), posts the flush when it ends, after any write the platform deferred
  during the apply (Android's focused `SetSelection` is posted to the same looper first), so the
  flush observes the settled result. The characterization confirms that order per platform.
- **The flush reads the actual platform selection**: Android `EditText.SelectionStart` and
  `SelectionEnd`, iOS and Mac Catalyst the `UITextField`'s `SelectedTextRange` offsets, through the
  view's handler. Not the virtual view's `CursorPosition` and `SelectionLength`: a `DirectEntry`'s are
  plain fields (`DirectControls.cs`), and a Controls `Entry`'s follow the platform only through the
  handler's two separate writes. With no platform view, the flush reports nothing and leaves the
  baseline.
- **Reported if it differs from the baseline**, with the selected text taken from the platform's
  text; then the baseline becomes it. So split cursor and length notifications coalesce into one
  report; a programmatic write whose platform result equals its effective value reports nothing; a
  write that Android clamps differently when its posted `SetSelection` runs (the text grew or shrank
  in between) is reported as the real resulting selection; and a caret moved by a text write (a
  restore after refused typing, or a new declared text) is reported, as WinUI's
  `TextBox.SelectionChanged` is.

**Characterize first.** The first implementation step is a device fixture on all three platforms
and both registries: focused and unfocused; cursor only, length only, both; a position past the
end; two writes in one batch and in consecutive renders; a text and selection write together;
keyboard, password and `MaxLength` changes while focused; text growing and shrinking between a
write, Android's execution of it and the flush. Per case it records whether the notification
arrives inside the write or late, the final platform selection, and the exact callback sequence
after settling (400 ms, as spec 067 §14's fixtures, then one more dispatcher turn). §2.15
keeps the table.

**Both renderers.** `DirectEntry` implements `IEntry.CursorPosition` and `SelectionLength` as plain
properties the platform handler writes and Reactor ignores. Their setters raise a selection
notification (the flush still reads the platform), and Reactor writes through a `SetSelection` that
remaps both properties inside the apply's scope. The characterization also records which write
cases notify late on each platform and registry.

### 2.9 Posted callbacks

Two reports in 4c-1 are posted to the root's scheduler rather than invoked in the platform callback:
`OnSelectionChanged` (§2.8) and `OnNavigated` (§2.7). Both go through one internal helper with these
rules:

- **Detach before dispatch.** The flush takes the pending state into a local and clears it before
  invoking anything, so a notification raised by a callback (or by a render it causes) starts a new
  burst instead of being lost or re-reported.
- **Recheck `Live` before each callback**, not once per flush: a first callback whose state change
  unmounts the element stops the second.
- **Exceptions** from a callback are caught and routed through `HandlerContext.ReportError`, to the
  error boundaries, never thrown into the dispatcher.
- **Restoration in `finally`**, where the report kind has one. Neither 4c-1 posted report restores
  (selection isn't controlled, and `Url` isn't either); synchronous `OnTextChanged` keeps its own
  restore. The helper supports it so a later controlled posted report can't skip it on an exception.

### 2.10 Tests

Each oracle names the mutation that must turn it red; the implementation PR runs each mutation once
and records it in §2.15. Counts are positive witnesses: a test that asserts something was cleared
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
| Scroll bridge: coordinates and dispatch | a target in a direct `Grid` (nonzero row, margin, padding) inside a direct `VStack` inside the scroll view, with frames the test assigns: the point passed to `ScrollToAsync(x, y, …)` equals `GetScrollPositionForElement` applied to the summed rectangle, for each position; an `ElementRef<IEntry>` naming a `DirectEntry` directly in a Controls stack is computed too; an all-Controls chain calls MAUI's element overload instead (`ScrollToRequested` in element mode, with the target); `Orientation.Neither` completes `false` at once with no `ScrollToRequested` | walk `Parent` instead of the visual parent; delegate when only the layouts are Controls; let `Neither` through |
| Scroll bridge: validation and readiness | an unrelated target (another scroll view's child) and a detached subtree throw; an empty ref, and a chain where the target, an intermediate direct layout or the scroll view has no handler or isn't arranged, return `false` and request nothing | skip the ancestry check; check only the scroll view's readiness |
| Weak-table ownership | a Controls child moved between two direct layouts in one pass (attached to the new before detached from the old) keeps the new layout as its visual parent | remove the entry without the owner check |
| Scroll bridge: completion protocol | a stub drives finishes and positions. A second bridge call completes the first with `false` at once, and so do a direct `ScrollToAsync(x, y, …)` call (through `ScrollToRequested`), a handler disconnect and unmount; staggered animations: A's late finish while B animates, at A's position, doesn't complete B, and B completes `true` only on a finish at its target; a finish elsewhere with no earlier finish expected completes `false`; a target beyond the range completes `true` at the range end; a dispatch that throws completes `false` and leaves no outstanding state (the next call works) | complete on any finish; drop the expected-finish count; skip the cleanup on a throw |
| WebView `OnNavigating` | `IWebView.Navigating(...)` (what the platform handlers call) returns cancel as decided; not called and allowed after unmount; a throw cancels and reaches the boundary | ignore `Cancel`; invoke through `Current` instead of `Live`; let the exception escape |
| WebView `OnNavigated` | each `Result` is passed through, after the scheduler turn, not inside `IWebView.Navigated` | invoke synchronously |
| WebView source | `Url`, `Html` set, replaced, cleared; re-declaring the same `Url` (a new element, equal source) writes nothing; a changed `Url` writes once; `Html` wins over `Url` | compare by element reference instead of the source |
| `Entry` selection writes, both registries | written at mount and when the declaration changes, never when `null`, never after user input; the whole apply is one scope (a keyboard change that moves the selection is a suppressed echo) | write when it differs from the view instead of the previous element; write outside the scope |
| `Entry` `OnSelectionChanged` | a stub platform selection drives these. Split notifications (cursor, then length) give one report with both final values; a programmatic write whose platform result equals its effective value gives zero; the review's sequence: write 8, a text write moves the caret to 20 before the flush (one report, 20), then a user's move to 8 (one report, 8); text that grows and that shrinks between the write, the deferred execution (a stub that applies the write on the next turn, clamped then) and the flush reports the actual resulting selection once, or nothing when it equals the effective value; notifications inside an apply post nothing until the apply ends; a text restore that moves the caret gives one report with the real selection; nothing after unmount | read the virtual view instead of the platform; report per notification; keep an expected-write record; post from inside the apply |
| Posted-callback helper | the pending state is cleared before the callback (a notification raised inside it starts a new burst); a callback that unmounts the element stops the next one; a throw reaches the boundary and the next posted report still runs | clear after dispatch; check `Live` once; let the exception escape |
| `Entry` text | still synchronous, restored when refused, unchanged by selection use | — |
| `Entry` return type, both registries | set, updated and cleared to `Default`; `OnCompleted` reported | — |
| `ScrollView` | orientation and scroll bar visibility set, updated, cleared; `OnScrolled` reported from `SetScrolledPosition` | — |
| `RefreshView` | mount with `IsRefreshing` true; toggling not reported; `RefreshColor` set and cleared; a handler-style refresh (`IRefreshView.IsRefreshing = true`) reported once, kept when the app accepts, restored and not re-reported when it refuses; nothing after unmount; content set, replaced, cleared (a direct view hosted in the wrapper on the direct registry) | write outside the echo scope; skip the restore |

**Engine (`tests/Reactor.Maui.Engine.Tests`):** the binding rows above that don't need MAUI (move,
remount in place, keyed move, a throwing update or child sync, boundary) on the fake renderer, with a
fake element that implements the ref interface, so they hold for any handler.

**Device (`tests/Reactor.Maui.DeviceTests`), Mac Catalyst, iOS and Android, `[controls]` and
`[direct]` unless noted.** Every fixture that checks callbacks waits until it settles (400 ms and a
dispatcher turn) and asserts the exact callback sequence, including zero where none is expected, not
only the final state.

| Fixture | Oracle |
|---|---|
| `Ref_FocusFromAnEvent_FocusesThePlatformView` | `ElementRef<Entry>` and, on direct, `ElementRef<IView>`: after a tap, the platform view is first responder (iOS, Catalyst) or `IsFocused` (Android), and `Focus()` returned `true` |
| `Ref_FocusOnMount_PerPlatform` | two independent fresh pages (§2.6.2): focus from `Loaded` works on every platform; focus from the first effect matches the result §2.15 records for this platform |
| `ScrollView_ScrollToFromARef_ReachesThePlatform` | content several viewports tall (no clamping): after `ScrollToAsync(0, 300, false)` the platform offset (`ContentOffset.Y`, or `ScrollY` in device-independent units) is 300 ± 1 and `OnScrolled` reported it; horizontal likewise on x |
| `ScrollView_ScrollToAnElement_ThroughNestedDirectLayouts [direct]` | a target in a direct `Grid` (nonzero row offset, margins, padding) in a direct `VStack`, far from both ends, content several viewports tall: for each of the four positions the bridge's resulting platform offset matches the target's position measured on the platform (its platform frame converted to the content view), adjusted by the position and the inset adapter. MAUI's own element overload is also run, and the fixture records its error, showing why the bridge exists |
| `ScrollView_ScrollToAnElement_EquivalentTrees_WithInsets [iOS, Catalyst]` | the gate for the inset adapter: a scroll view under a navigation bar with nonzero adjusted content insets, holding the same geometry once from Controls layouts and once from direct layouts; for all four positions, including `MakeVisible` from above, from below and already visible, the bridge's platform offset on the direct tree equals MAUI's own overload's on the Controls tree. Android runs it too, with no adapter, and records whether any inset appears |
| `ScrollView_ScrollToAnElement_NotReady` | called before the page is shown, it returns `false` and the offset stays 0; a target inserted by a render into an arranged scroll view returns `false` when called in that render's turn, and after the next layout pass scrolls to the right offset; an unrelated target throws |
| `ScrollView_ScrollToAnElement_Superseded` | characterization first: how many finishes two overlapping animated requests produce on each platform (recorded in §2.15). Then two animated bridge scrolls in a row: the first task completes `false` within one second, the second completes `true` only once the platform offset is at its target, and the final offset is the second's |
| `ScrollView_ScrollBars_ReachThePlatform` | the platform's indicator flags follow the declared visibility |
| `RefreshView_UserRefresh_IsReported_AndRefusalStopsTheSpinner` | a refresh triggered through the platform control (`UIRefreshControl` value-changed action; the `SwipeRefreshLayout` listener) is reported once; the platform spinner follows the declaration; refused, it stops, and the sequence after settling is exactly that one report. If Catalyst has no pull-to-refresh, the fixture records that and the inventory says so |
| `WebView_Navigations_AreAttemptedAndCompleted` | bundled HTML pages (no network). Positive control: a link from page 1 to page 2 is allowed, completes with `Success`, `document.title` is page 2's, and `CanGoBack` read in `OnNavigated` is `true`. Cancelled, each started a different way (a link, `EvaluateJavaScriptAsync("location.href=…")`, `Reload()`, `GoBack()`): any completion reported carries `Result.Cancel` and none `Success`; the previous page's `document.title` is asserted only where §2.7's table promises it stays |
| `WebView_CommandsThroughARef` | `GoBack` and `GoForward` move between the two pages; `Reload` re-runs page 1's script (a `sessionStorage` counter reads 2); `EvaluateJavaScriptAsync` returns a value |
| `WebView_DeclaredUrlCancelled_LoadsNothing` | with page 1 shown, a changed `Url` to page 2 whose `OnNavigating` cancels: on every platform no load begins (page 2's load counter, kept in `localStorage` by its script, stays 0), `document.title` is page 1's, and any completion carries `Cancel`. Positive control: the same change allowed loads page 2 once |
| `WebView_RedeclaredUrl_DoesNothing` | rendering the same `Url` again doesn't load (page 1's load counter stays 1); a changed `Url` loads once |
| `Entry_Selection_Characterization` | the matrix of §2.8; per case, whether the notification was late, the final platform selection and the exact callback sequence |
| `Entry_Selection_Sequences` | a programmatic selection write, focused and unfocused: zero callbacks; a selection set on the platform view (what a user's tap does): exactly one, with both final values; typing a character: one `OnTextChanged`, then the selection reports the characterization recorded for that platform; refused typing: the characterized per-platform sequence, and, when that sequence contains at least one `OnSelectionChanged`, the last one equal to the final platform selection; zero callbacks after settling |
| `Entry_ReturnType_ReachesThePlatform` | `ReturnKeyType` (iOS, Catalyst) and `ImeOptions` (Android) follow the declaration |
| `CollectionView_RefsInItemComponents_FollowTheirCells [controls]` | scroll until at least one cell instance has rendered two different item keys (identity reuse, asserted before anything else) and at least one item component has unmounted (count ≥ 1); then every live item component's ref names a cell showing its item, and every unmounted one's ref is `null` |

### 2.11 Gate for 4c-1

- **WinUI** isn't changed: `skills/reactor.api.txt` is byte-identical, and its tiers stay green.
- **MAUI**: headless, engine and device suites green on Mac Catalyst, iOS and Android; each §2.10
  mutation turns its oracle red; the selection characterization table and the focus-on-mount results
  are in §2.15; the MAUI benchmark within noise.
- **Selection isn't controlled** (§2.8): recorded in spec 068's Entry row as WinUI parity.
- **Docs**: `src/Reactor.Maui/README.md` documents refs (multi-attach, retirement, hot reload, refs
  held outside a tree), the commands and the scroll bridge (validation, readiness, insets,
  supersession, and MAUI's overload's limits), the WebView events (attempts, results, per-platform
  cancel), `Url` as the requested source with the snippet of §2.7, and the selection properties; spec 068's rows (Element refs, Focus and keyboard, Entry, ScrollView and RefreshView,
  WebView) are updated with their evidence.

### 2.12 Decisions to review

1. **Nothing moves to the core**; MAUI gets WinUI's names in WinUI's namespace (§2.2). Rejected:
   moving WinUI's `ElementRef` (its public `Current` is a `FrameworkElement`); a neutral core cell
   that only MAUI calls (no second consumer).
2. **`T : class, IView`, not `VisualElement`.** `IView` covers Controls views and direct views, and a
   Controls-typed ref makes that one element a Controls view (§2.5). Rejected: `VisualElement`,
   which would force every referenced direct element to Controls, including a plain focus target.
3. **A type mismatch throws in every build**, where WinUI asserts in DEBUG only (§2.5).
4. **`Entry` selection has WinUI's semantics** (gate decision, review round 2, §2.8): written when the
   declaration changes, never restored, reported from one posted flush per burst. Rejected:
   controlled selection, which MAUI's split, deferred and execution-time-clamped notifications make
   inexact; revisit with the characterization data.
5. **Native commands, no wrappers**, except the scroll-to-element bridge (§2.6.1). It delegates to
   MAUI's overload on all-Controls chains, computes (with an inset adapter gated against MAUI) only
   when a direct layout is in the chain, returns `false` until the whole chain is arranged, and
   always completes its task. Rejected: requiring Controls layouts above every scroll target; MAUI's
   deferral, which needs a readiness signal for every view in the chain.
6. **WebView events are MAUI's argument types** (§2.7), and `OnNavigated` changes to
   `WebNavigatedEventArgs` (breaking, preview) so cancelled and failed completions are visible.
   Rejected: `Func<string, bool>` for navigating; a Reactor context type; filtering `OnNavigated` to
   successes.
7. **Binding in the engine, owned by the node** (§2.4).
8. **No URL echo rule** (review round 3): a source loads when its declaration changes, as today, and
   `Url` is documented as the requested source (§2.7). Rejected: deduplicating against the page
   shown, because a completion carries only a URL, which can't tie it to the start that caused it
   (delayed `OnPageStarted` attempts, overlapping navigations to one URL).
9. **Selection reporting has one state, a baseline** read from the platform (§2.8), and prefers
   reporting an ambiguous change to swallowing input. No echo mechanism is shared with WinUI:
   nothing late-echoing remains controlled, so spec 067 §14's extraction trigger isn't reached.

### 2.13 Open questions, resolved in review rounds 1 to 3

1. Focus on mount: measured per platform with independent mounts, an explicit decision recorded in
   §2.15 and asserted by a fixture; `UseElementFocus()` only if a platform needs it (§2.6.2).
2. A WebView URL mirrored into state: not deduplicated; documented as a reload, with the pattern
   that avoids it (§2.7).
3. One ref on several elements: documented, not detected (§2.4).
4. `OnNavigating` sees navigations Reactor's `Url` starts, without an exactly-once guarantee where
   MAUI can't give one; on Android a declared `Url` is asked before it loads (§2.7).
5. Controlled selection: dropped for WinUI's semantics (§2.8).

6. Label spans: 4c-2.

### 2.14 Out of scope, follow-ups

- **WinUI ref move.** WinUI's update path (`Reconciler.cs`, `ApplyModifiers`) clears the old cell
  without the owner check its unmount path has, so a ref moved in one render from a later sibling to
  an earlier one may end `null`. Verify with a WinUI unit test; fix in a WinUI PR if it fails.
- **Cells MAUI drops without unbinding** stay mounted until the list unmounts (§2.4).

### 2.15 As built (4c-1)

To be written when 4c-1 lands, including the selection characterization table, the focus-on-mount
result per platform and the mutation runs.

## 3. 4c-2: collections and items

To be written when 4c-2 starts.

## 4. 4c-3: shapes, brushes, ContentView, image equality, tests and the audit

To be written when 4c-3 starts.
