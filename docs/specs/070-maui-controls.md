# Spec 070: MAUI controls (phase 4c)

Phase 4c closes the controls rows of spec 068's inventory. It lands in four gated PRs:

| PR | Scope |
|---|---|
| 4c-1 | Element refs, and the imperative commands they enable: focusing, scrolling and WebView commands from code; cancelling a WebView navigation; `Entry` cursor and selection; tests for `ScrollView` and `RefreshView` |
| 4c-2 | Collections and items: CollectionView layouts, spacing, sizing, header, footer, empty view, threshold, grouping and selection state in items; SwipeView items and opening from code; Picker item display; RadioButton with view content and Reactor-owned groups; Label spans |
| 4c-3 | Shapes, gradient brushes, ContentView, image source equality, tests for every remaining 🧪 control and layout, and the §1 audit of every ✅ row of spec 068 |
| 4c-4 | CarouselView and IndicatorView (moved out of 4c-2 in its design review) |

This spec details 4c-1 (§2), 4c-2 (§3) and 4c-3 (§4, two PRs: 4c-3a and 4c-3b). §5 is
written when 4c-4 starts. The bar for each item is spec 068 §1.

## 1. Scope

- **4c-1, refs and imperative commands.** `UseElementRef<T>()` and `.Ref(...)` on MAUI; focusing
  from code (`ref.Current?.Focus()`); scrolling from code (`ScrollToAsync`); WebView `Reload`,
  `GoBack`, `GoForward` and `EvaluateJavaScriptAsync`; an `OnNavigating` callback that can cancel;
  `CursorPosition`, `SelectionLength` and `OnSelectionChanged` on `Entry`, on both registries;
  `Entry` return type tests; headless and device tests for `ScrollView`, `RefreshView` and `WebView`.
  Rows it closes in spec 068: Element refs, Focus and keyboard, Entry, ScrollView and RefreshView,
  WebView.
- **4c-2, collections and items** (§3). It reuses 4c-1's refs for `CollectionView.ScrollTo` and
  `SwipeView.Open`/`Close`.
- **4c-3, shapes, brushes, ContentView, image equality, tests and the audit** (§4): split into
  4c-3a (the new elements, brush and image source equality, and a property census that becomes the
  audit's measure) and 4c-3b (the remaining 🧪 rows and the audit of every ✅ row through that census).
- **4c-4, CarouselView and IndicatorView** (§5). Moved out of 4c-2 in its design review: the
  carousel's position echo must never write or report during a drag and must not guess an
  operation's provenance (§5 lists the requirements).

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
                OnNavigating = e => e.Cancel = !(Uri.TryCreate(e.Url, UriKind.Absolute, out var uri)
                    && uri.Scheme == Uri.UriSchemeHttps
                    && string.Equals(uri.IdnHost, "learn.microsoft.com", StringComparison.OrdinalIgnoreCase)),
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

- **If the effect fails on any platform** (it does on Android, §2.15), MAUI gets WinUI's
  `UseElementFocus()` with its name and shape, `(ElementRef<IView> Ref, Action RequestFocus)`. `RequestFocus` focuses at once when the view
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

  A refused navigation never completes with `Success`. One refused before loading (the first three
  rows) reports no completion at all; one Android refuses late completes with `Cancel` (§2.15). The
  previous page stays only in the rows that say so. The declared source isn't loaded again until it
  changes. Subresources and frames aren't asked, so `OnNavigating` is a navigation policy, not a
  security boundary.
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
- **The selection write in an echo scope; every notification during the apply deferred.** The text
  keeps its own scope and keyboard, password and `MaxLength` stay outside one, exactly as without
  selection tracking, so a text the platform changes (a `MaxLength` truncation) still reaches
  `OnTextChanged`. Selection notifications raised anywhere in the apply (Android's input-type updates
  end with `SetSelection`) wait for the observation posted after it. (As built; the first design put
  the whole apply in one scope, see §2.15 deviation 8.)
- **Text is unchanged**: `OnTextChanged` is reported synchronously and followed by a restore, as
  today. There is no text-and-selection transaction, so selection handling can never report text.

**`OnSelectionChanged`: one posted flush per burst, against one baseline.**

The only state is a **baseline**: the selection Reactor last knew the platform to show. There is no
record of expected writes, because any such record can swallow a genuine move (a write to 8, a text
write that moves the caret to 20 before the flush, then a user's move back to 8). When a change is
ambiguous, Reactor reports it rather than risk swallowing input.

- **The baseline** is initialised from the platform once its view exists (for a direct entry, posted:
  MAUI sets its handler before creating the platform view). A
  programmatic selection write sets it to the write's effective value: the declared cursor and
  length clamped to the text's length at that moment. After every flush it becomes the selection the
  flush observed, reported or not.
- **Notifications start a burst.** A selection notification outside an apply posts one flush (§2.9)
  if none is pending. Notifications inside an apply's echo scope don't post; instead, an apply that
  wrote the text or the selection, or during which any selection notification arrived (a
  property-only apply such as a `MaxLength` or keyboard change counts; as built, any keyboard,
  password or `MaxLength` change does too, §2.15), posts the flush when it ends, after any write the platform deferred
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
| `UseElementFocus` | a request before the direct view has a handler focuses it on the turn after the handler arrives, once; a view not in a window is checked again and focused once it joins one; a pending check for a view that never joins one doesn't keep the tree alive (weak references, collected); two pending requests focus once; a request is dropped when the element unmounts and when the ref moves to another view (no `Focus` command); a request from another thread runs only when the root's render queue drains; the hook returns the same pair every render | no wait for the handler; no generation; no drop; run on the calling thread; a poll that holds the request strongly; a poll that never rechecks |
| Scroll bridge: coordinates and dispatch | a target in a direct `Grid` (nonzero row, margin, padding) inside a direct `VStack` inside the scroll view, with frames the test assigns: the point passed to `ScrollToAsync(x, y, …)` equals `GetScrollPositionForElement` applied to the summed rectangle, for each position; an `ElementRef<IEntry>` naming a `DirectEntry` directly in a Controls stack is computed too; an all-Controls chain calls MAUI's element overload instead (`ScrollToRequested` in element mode, with the target); `Orientation.Neither` completes `false` at once with no `ScrollToRequested` | walk `Parent` instead of the visual parent; delegate when only the layouts are Controls; let `Neither` through |
| Scroll bridge: validation and readiness | an unrelated target (another scroll view's child) and a target whose subtree was detached from the scroll view throw; an empty ref, and a chain where the target, an intermediate direct layout or the scroll view has no handler or isn't arranged, return `false` and request nothing | skip the ancestry check; check only the scroll view's readiness |
| Weak-table ownership | a Controls child moved between two direct layouts in one pass (attached to the new before detached from the old) keeps the new layout as its visual parent | remove the entry without the owner check |
| Scroll bridge: completion protocol | a stub drives finishes and positions. A second bridge call completes the first with `false` at once, and so do a direct `ScrollToAsync(x, y, …)` call (through `ScrollToRequested`), a handler disconnect and unmount; staggered animations: A's late finish while B animates, at A's position, doesn't complete B, and B completes `true` only on a finish at its target; a finish elsewhere with no earlier finish expected completes `false`; a target beyond the range completes `true` at the range end; a dispatch that throws completes `false` and leaves no outstanding state (the next call works) | complete on any finish; drop the expected-finish count; skip the cleanup on a throw |
| Scroll bridge: protocol details (pr-review) | a continuation of a confirmed task that starts another MAUI scroll isn't completed by the finish that confirmed the first; a request MAUI parked before the handler and replayed (it raises `ScrollToRequested` twice) is one expected finish, and a later request that ends elsewhere then settles `false`; a request parked for geometry and dropped by a newer one (the bridge's own included) is no expected finish; a finish an interrupted animation produces inside the next request's dispatch doesn't settle that request; a request whose finish never comes settles at the bound, by position (`false` where it stopped, `true` at its target), and an earlier request's bound doesn't settle a later one; a late finish after its request settled at the bound doesn't settle the next request; a handler replaced by another (no `null` in between) completes the outstanding request `false`, and a request before any handler is still replayed; an orientation changed while a request runs (to `Neither`, to another axis) doesn't confirm an unreached target, at the bound or at a finish; a negative target is confirmed at 0, a horizontal one on x | run continuations inline; count a parked request and its replay; keep a dropped geometry-parked request; skip the drop for the bridge's own request; count after dispatch; no bound; the bound settles `true`; the bound zeroes the expected count; cancel only when the handler goes to `null`; check the current orientation instead of the requested axes; no start clamp; ignore x |
| Scroll bridge on a scroll view Reactor didn't create | a plain `ScrollView`: a finish at the target confirms, one elsewhere doesn't; a later request, a handler disconnect and a throwing dispatch complete `false`; the extension scrolls it to a Reactor target | confirm without checking the position; no supersession; no disconnect; let the throw escape |
| WebView `OnNavigating` | `IWebView.Navigating(...)` (what the platform handlers call) returns cancel as decided; not called and allowed after unmount; a throw cancels and reaches the boundary | ignore `Cancel`; invoke through `Current` instead of `Live`; let the exception escape |
| WebView `OnNavigated` | each `Result` is passed through, after the scheduler turn, not inside `IWebView.Navigated` | invoke synchronously |
| WebView source | `Url`, `Html` set, replaced, cleared; re-declaring the same `Url` (a new element, equal source) writes nothing; a changed `Url` writes once; `Html` wins over `Url` | compare by element reference instead of the source |
| `Entry` selection writes, both registries | written at mount and when the declaration changes, clamped to zero, never when `null`, never after user input; only the selection write is in the echo scope, so a text the platform changes during the apply (a `MaxLength` truncation) reaches `OnTextChanged` exactly as without selection tracking; a selection notification anywhere in the apply waits for the observation after it | write when it differs from the view instead of the previous element; the whole apply in the echo scope; no clamp |
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
| `Ref_FocusOnMount_PerPlatform` | two independent fresh pages (§2.6.2): focus from `Loaded` works on every platform; focus from the first effect matches the result §2.15 records for this platform; `UseElementFocus`'s request from the first effect focuses, a Controls entry exactly once (`Focused` events) |
| `ScrollView_ScrollToFromARef_ReachesThePlatform` | content several viewports tall (no clamping): after `ScrollToAsync(0, 300, false)` the platform offset (`ContentOffset.Y`, or `ScrollY` in device-independent units) is 300 ± 1 and `OnScrolled` reported it; horizontal likewise on x |
| `ScrollView_ScrollToAnElement_ThroughNestedDirectLayouts [direct]` | a target in a direct `Grid` (nonzero row offset, margins, padding) in a direct `VStack`, far from both ends, content several viewports tall: for each of the four positions the bridge's resulting platform offset matches the target's position measured on the platform (its platform frame converted to the content view), adjusted by the position; `MakeVisible` from above, from below and already visible; the content has a margin, so its origin isn't zero |
| `ScrollView_ScrollToAnElement_EquivalentTrees_WithInsets [iOS, Catalyst]` | the gate for the inset adapter: a scroll view under a navigation bar with nonzero adjusted content insets, holding the same geometry once from Controls layouts and once from direct layouts (content with a nonzero origin); the adjusted top inset is asserted ≥ 50 and the bridge's result `true` on every iteration; for all four positions, including `MakeVisible` from above, from below and already visible, the bridge's platform offset on the direct tree equals MAUI's own overload's on the Controls tree. Android runs it too, with no adapter, and records whether any inset appears |
| `ScrollView_ScrollToAnElement_NotReady` | called before the page is shown, it returns `false` and the offset stays 0; a target inserted by a render into an arranged scroll view returns `false` when called in that render's turn, and after the next layout pass scrolls to the right offset; an unrelated target throws |
| `ScrollView_ScrollToAnElement_Superseded`, both registries | the number of finishes two overlapping animated requests produce equals what the bridge expects (recorded in §2.15). Then two animated bridge scrolls in a row: the first task completes `false` within one second, the second completes `true` only once the platform offset is at its target, and the final offset is the second's |
| `ScrollView_ScrollToAnElement_Interrupted`, both registries | an uninterrupted animated scroll is confirmed by its own finish within the settle bound (its duration recorded); interrupted the way a touch does (UIKit: a non-animated offset write; Android: a dispatched drag), the task completes within the bound and is `true` exactly when the scroll view is at the target, read in the first check that sees it complete; the offset doesn't move afterwards (Android's drag is held still before the lift, so no fling starts) |
| `ScrollView_ScrollBars_ReachThePlatform` | the platform's indicator flags follow the declared visibility |
| `RefreshView_UserRefresh_IsReported_AndRefusalStopsTheSpinner` | a refresh triggered through the platform control (`UIRefreshControl` value-changed action; the `SwipeRefreshLayout` listener) is reported once; the platform spinner follows the declaration (still spinning 200 ms into an accepted refresh); refused, it stops, and the sequence after settling is exactly that one report. If Catalyst has no pull-to-refresh, the fixture records that and the inventory says so |
| `WebView_Navigations_AreAttemptedAndCompleted` | bundled HTML pages (no network). Positive control: a link from page 1 to page 2 is allowed, completes with `Success`, `document.title` is page 2's, and `CanGoBack` read in `OnNavigated` is `true`. Cancelled, each started a different way (a link, `EvaluateJavaScriptAsync("location.href=…")`, `Reload()`, `GoBack()` with `CanGoBack` asserted first): each was asked about its page; no `Success`; the exact completions §2.15 records (none, or Android's late `Cancel`); refused before loading, the previous page and its script state stay; refused late, the document is gone. Positive control: the same script navigation allowed is asked and completes with `Success` |
| `WebView_CommandsThroughARef` | `GoBack` and `GoForward` move between the two pages; `Reload` re-runs page 1's script (a `sessionStorage` counter reads 2); `EvaluateJavaScriptAsync` returns a value |
| `WebView_DeclaredUrlCancelled_LoadsNothing` | with page 1 shown, a changed `Url` to page 2 whose `OnNavigating` cancels: on every platform no load begins (page 2's load counter, kept in `localStorage` by its script, stays 0), `document.title` is page 1's, and any completion carries `Cancel`. Positive control: the same change allowed loads page 2 once |
| `WebView_RedeclaredUrl_DoesNothing` | rendering the same `Url` again doesn't load (page 1's load counter stays 1); a changed `Url` loads once |
| `Entry_Selection_Characterization` | the matrix of §2.8; per case, whether the notification was late, the final platform selection and the exact callback sequence |
| `Entry_Selection_Sequences` | a programmatic selection write, focused and unfocused: zero callbacks; a selection set on the platform view (what a user's tap does): exactly one, with both final values; typing a character: one `OnTextChanged`, then the selection reports the characterization recorded for that platform; refused typing: the characterized per-platform sequence, and, when that sequence contains at least one `OnSelectionChanged`, the last one equal to the final platform selection; zero callbacks after settling. The exact recorded sequences are asserted (typing, refused typing, text shrinking after a write), and a `MaxLength` truncation's text is reported |
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

**What was built** (commits `4099ed54`, `260b4d84`, `18404ce4`, `fdf743f3`, `0b4555ab`, and the
pr-review fixes after it):

- **Refs.** `ElementRef<T> where T : class, IView`, `TypedElementRef.Create<T>()`, `UseElementRef<T>()`
  (on `RenderContext` and `Component`) and `.Ref(ElementRef<T>?)` on every `ViewElement`, in
  `Microsoft.UI.Reactor.Input`, which the package's global usings now import. The engine binds a host
  node's ref after its mount or update (`ReactorRoot.BindRef`) and releases the cell the node recorded
  (`Node.BoundRef`) in `Unmount(node)`, which every removal and failure path goes through; the cell
  (`ElementRefCell`) clears only for its owner. A wrong-typed ref throws, naming both types, before
  binding. `HybridHandler` uses the direct view only when the ref's type accepts it.
  `DirectView.Focus()`/`Unfocus()` go through the handler's commands. `ReactorCell` unmounts its tree
  when its binding context stops being an item.
- **Focus on mount** (§2.6.2): the effect fails on Android, so **`UseElementFocus()`** was added with
  WinUI's name and shape, `(ElementRef<IView> Ref, Action RequestFocus)` (`Core/ElementFocus.cs`).
  `RequestFocus` runs on the root's render queue when called from another thread (as a state setter
  does), focuses a loaded Controls view at once or on its `Loaded` (one-shot), and waits for a direct
  view's readiness signal: its `HandlerChanged` (then one more turn, since MAUI sets a Core view's
  handler before it creates the platform view), then on Android the platform view's
  `ViewAttachedToWindow` (one-shot); UIKit has no public event for a view joining a window, so iOS and
  Mac Catalyst check `Window` at doubling intervals up to 1 s, with no cap while the request stands. A
  newer request replaces a pending one; a request is dropped when the ref stops naming its view.
- **WebView.** `OnNavigating(WebNavigatingEventArgs)` (a throw cancels and goes to the boundary);
  `OnNavigated(WebNavigatedEventArgs)`, posted. The source still loads only when the declaration
  changes.
- **Posted reports** go through `HandlerContext.PostReport` (detach first, `Live` per callback, errors
  to `ReportHandlerError`, optional restore in `finally`).
- **Entry selection** (`Handlers/EntrySelection.cs`): `SelectionTracker` per view, created only when
  the element uses the selection; `PlatformSelection.Read` reads `EditText`/`UITextField`/`TextBox`.
  Both handlers apply through one `SelectionTracker.Apply`: everything but the selection is applied
  as without tracking (the text in its own scope), only the selection write is suppressed, and any
  selection notification during the apply waits for the observation posted when it ends.
- **Scroll bridge** (`ScrollViewExtensions`, `ScrollBridge`): `ReactorScrollView` re-implements
  `IScrollView.ScrollFinished`; `DirectLayout` records Controls children's visual parent. The computed
  path arranges a handler-less `BoxView` stand-in at the target's rectangle and calls MAUI's
  `GetScrollPositionForElement` on it. `ScrollBridge.For` gives any other `ScrollView` a bridge too
  (a conditional weak table), which settles on MAUI's task by position. The bridge mirrors MAUI 11's
  dispatch: a request parked before the handler and replayed counts one expected finish, one parked
  for geometry and dropped by a newer request counts none; the expected finish is counted before the
  dispatch; tasks run continuations asynchronously. Every request settles at a bound at the latest
  (`SettleBound`, armed when it starts), by position, without touching the expected count; a handler
  replaced by another, or removed, cancels outstanding work in `HandlerChanging`. Completion checks use
  the axes the request was made on, not the orientation at completion.

**Deviations from §2, and why:**

1. **`UseElementFocus()` exists** — the conditional in §2.6.2 triggered (Android, both registries).
2. **Input-type and `MaxLength` changes are observed too.** The characterization showed platforms move
   the selection themselves when the keyboard, password mode or `MaxLength` changes, and UIKit's
   select-all on a password toggle arrives without a notification MAUI forwards, so a baseline fed
   only by notifications went stale. Such an apply now posts the observation (`InputChanged`), and the
   change is reported as the resulting selection.
3. **A direct entry's baseline is observed once its platform view exists.** MAUI sets a Core view's
   `Handler` before it creates the platform view (`ToHandler`), so the observation on `HandlerChanged`
   found nothing; it is posted once (found on Android, where the first user selection went
   unreported).
4. **The not-ready fixture** probes "no handler" with an off-window root, because a fresh `Show` on
   Mac Catalyst is laid out in the same turn. A target inserted by a render was not laid out in that
   turn on any of the three platforms (the bridge returned `false`, then scrolled after layout).
5. **WebView history fixtures navigate through declared sources.** Android's WebView marks entries
   added by a script `click()` (no user gesture) as skippable, so `CanGoBack` stays `false` after one;
   the declared `Url` load keeps page 1 as a back entry.
6. **The posted helper's own unmount check is redundant with `Live`** (an equivalent mutant: removing
   it alone changes nothing; removing it and invoking through `Current` turns the test red). Kept as
   defense in depth.
7. **The selection write's echo scope is redundant too** (an equivalent mutant): every selection
   notification inside an apply already waits for the observation, which compares with the baseline
   the write set. Kept, so a platform that raises something else from a selection write stays quiet.
8. **The echo scope covers only the selection write** (pr-review M2), not the whole apply as §2.8
   first said: wrapping the apply swallowed a text the platform changed (`MaxLength` truncating
   through `setText`), so `OnTextChanged` never ran and the app's text diverged. Measured on all three
   platforms: a truncation now reports the text, then the selection.
9. **Scroll views Reactor didn't create get a bridge** (pr-review M1), so the extension's task always
   completes; such a bridge sees only the finish that completes MAUI's task, and checks the position
   then.
10. **A settle bound** (pr-review H2, then the verification round): every bridge request settles by
    position once `SettleBound` passes after it starts, if its own finish hasn't settled it first. The
    bound lies above the longest platform animation, so it doesn't guess when the scroll ends: MAUI's
    Android animator runs 1000 ms × the animator duration scale (bound: that + 1 s); UIKit's animated
    offset write was measured at about 0.3 s (bound: 1.5 s). It covers a finish that never comes (a
    request MAUI dropped in a way the bridge doesn't mirror, or an animation a touch stops without one)
    and replaces the first fix's grace period after a stale finish, which zeroed the expected count:
    a slow animation's late finish was then taken for the next request's. The count is now left as it
    is, so a late finish is absorbed.
11. **The nested-layouts fixture doesn't run MAUI's own overload**: its target is a direct view, which
    MAUI's `ScrollToAsync(Element, …)` can't take. The equivalent-trees fixture is the comparison with
    MAUI.
12. **UIKit readiness is checked, not observed** (§2.6.2 said "through its move to a window"):
    `MovedToWindow` is a `UIView` override, so observing it would need Reactor's own subclass of
    every platform view. iOS and Mac Catalyst check `Window` at doubling intervals up to 1 s, with no
    cap while the request stands; Android observes `ViewAttachedToWindow` as designed. The scheduled
    check holds the request and the view weakly, so a tree discarded without an unmount, whose view
    never joins a window, is collected and the checks stop.
13. **A handler replaced by another cancels** (verification round): MAUI's `SetHandler` swaps a
    connected handler for another without a `null` in between, so the bridge cancels in
    `HandlerChanging` whenever there was an old handler.

**Characterization (device, both registries unless noted):**

| | Mac Catalyst | iOS | Android |
|---|---|---|---|
| Focus from the first effect of a new tree | works | works | **fails** (Controls and direct); `Loaded` and `UseElementFocus` work |
| A selection write's platform effect | in the render | in the render | in the render when unfocused; **after it** when focused (MAUI posts `SetSelection`) |
| Callbacks after a programmatic selection write (cursor, length, both, past the end, two in a batch, with a text write, consecutive renders) | 0 | 0 | 0 |
| Text replaced right after a write | caret at the new end (3, 0), reported once | same | caret (0, 0), reported once |
| Typing a character | text, then `selection 11,0` | same | same |
| Refused typing | text, `selection 11,0`, `selection 10,0` | same | text only (the caret ends where it was) |
| Keyboard change, selection (12, 2) | kept, nothing reported | kept | Controls (12, 0), direct (16, 0), reported once |
| Password toggle | unfocused: select-all (0, 16); focused: (16, 0); reported once | same | Controls (12, 0), direct (16, 0), reported once |
| `MaxLength` 5 truncating | text `abcde`, then (5, 0); each reported once | same | same |
| Finishes from two overlapping animated `ScrollToAsync` | 2 | 2 | 2 (so `FinishesAccumulate = true` everywhere) |
| When an interrupted animation's finish arrives | inside the next request's dispatch (found when the expected finish was counted after it) | same | later (two finishes after the first task completed) |
| An animated scroll of 1600 DIPs | about 310 ms | about 310 ms | about 1030 ms (duration scale 1) |
| A scroll interrupted as by a touch (UIKit: non-animated offset write; Android: dispatched drag) | one finish at once, where it stopped: `false` | same | the animator ignores the drag and finishes at the target: `true` |
| Adjusted top inset in the equivalent-trees gate | ≥ 50 | ≥ 50 | 0 (no content inset to add) |
| A target inserted in this turn | not laid out | not laid out | not laid out |
| Cancelled link, script navigation | asked before load, no completion, page kept | same | same |
| Cancelled `Reload`, `GoBack` | asked before load, no completion, page kept | same | asked in `OnPageStarted`; completion `Cancel`; page state lost (back landed on page 1) |
| Cancelled declared `Url` | no load | no load | no load (preflight in `LoadUrl`) |
| A script navigation after a refused `GoBack` | asked as `NewPage` | same | asked as `Back`: MAUI keeps the last command's kind |
| A new web view's first page load (shown to `Success`) | about 0.46 s for the process's first, 0.16 s later | first in the process on a freshly erased simulator: 8.4 s and 3.6 s in two runs (WebKit logged its WebContent and GPU processes taking 6.4 s and 1.4 s to launch); later 0.5–2.5 s, almost all of it before `OnNavigating` is asked | 0.8 s first, about 0.1 s later; asked twice (the `LoadUrl` preflight, then the page start) |

**Tests.** Headless `Reactor.Maui.Tests`: 328 (213 before 4c-1), in `ElementRefTests.cs`,
`ControlRefTests.cs`, `ScrollBridgeTests.cs`, including the README's snippets. Engine
`Reactor.Maui.Engine.Tests`: 80 (73 before), `RefBindingTests.cs`. Device: 28 new fixtures in
`ControlRefFixtures.cs` (`RefPlatform.cs` reads the platform; `Resources/Raw/page1.html`, `page2.html`).
Full device suites: Mac Catalyst 114 passed, 7 skipped (no back key, no in-process backgrounding);
iOS simulator 114 passed, 7 skipped; Android emulator 117 passed, 4 skipped; none failed.

**Mutation checks** (each broke the code, ran the named suite, restored): all red — owner check,
release from the element instead of `BoundRef`, no release on unmount, bind after effects, no hybrid
condition, no type check, no cell retirement, `DirectView.Focus` returning `false`, walking `Parent`,
delegating when only the layouts are Controls, letting `Neither` through, readiness of the scroll view
only, no ancestry check, an unconditional weak-table removal, completing on any finish, no
expected-finish count, no cleanup on a failed dispatch, not tracking the request, a throwing
`OnNavigating` allowing, `OnNavigating` through `Current`, its exception escaping, a synchronous
`OnNavigated`, a posted report's exception escaping, a posted report through `Current` without the
unmount check, selection writes compared with the view, no baseline on a write, a synchronous flush
per notification, reading the virtual view, posting from inside the apply, clearing the pending flag
after dispatch, no observation after an input-type change, no posted direct baseline, focusing a
direct view before it is ready, `IsRefreshing` outside the echo scope, no restore after a refresh, and
loading the web source on every render. After the pr-review: inline continuations, counting a parked
request and its replay, no settle bound, the bound settling `true`, the bound zeroing the expected
count, cancelling only when the handler goes to `null`, checking the current orientation (at the bound
and at a finish), a focus poll holding the request strongly, a
poll that never rechecks, keeping a dropped geometry-parked request, not dropping it
for the bridge's own request, counting after dispatch, a throwing dispatch escaping, a foreign finish
confirming anywhere, no foreign supersession or disconnect, no start clamp, ignoring x, the whole
apply in the echo scope, no clamp of a negative selection, no wait for a direct view's handler, no
focus generation, no drop, focusing on the calling thread, selection compared with the view. Two
equivalent mutants (deviations 6 and 7). A hanging mutant
(`Neither` let through) first read green when its hung test host was killed; the bridge's headless
awaits now time out, and it reads red.

**Benchmark** (Mac Catalyst, `tests/Reactor.Maui.Benchmark`, one interleaved pair against `cc5fe28e`):
total time within ±2% in every scenario except `UpdateOne` (−35%, a 60 ms scenario, noise);
allocations +0.0–0.2%.

**Open issues.**

- On Android an input-type change leaves the Controls entry's cursor and moves the direct entry's to
  the end (MAUI's mapping order differs for a Core view). Reported correctly either way.
- Android reports a script navigation that follows a refused `GoBack` as `Back` (MAUI keeps the
  last command's kind).
- A real user drag on iOS and Mac Catalyst can't be driven in process; the interruption fixture uses
  the offset write a touch makes, which UIKit ends with a finish. If a real drag ends an animation
  without one, the settle bound completes the task.
- On a hosted CI iOS simulator the process's first web view page timed out at 10 s
  (`WebView_Navigations_AreAttemptedAndCompleted`, the first WebView fixture; later ones passed, one
  taking 5.9 s). Locally, on a freshly erased simulator, the first load took 8.4 s and 3.6 s, with
  `OnNavigating` and `OnNavigated` both raised, just late: the time is WebKit launching its WebContent
  and GPU processes (its own log: 6.4 s and 1.4 s), not a missed event, a rejected completion or an
  unresolved asset URL. The fixtures now give the process's first web view page 60 s and later ones
  the ordinary 10 s, note each load's time, and on a timeout list every event with its time and URL.
- `WebView_Navigations_AreAttemptedAndCompleted` failed intermittently in full Android runs (2 of 3
  on the 4c-2 branch, never alone). Two fixture races, both reproduced on purpose:
  - **The recovery after a late cancel.** Where Android's late-cancelled reload landed early, it left
    a blank page (title `""`; a probe got it every time with no delay in `OnNavigating`, and the
    reloaded page 2 with 150 ms or more), and the fixture then went back to page 2 by declaring page 1
    and running a script navigation. Android marks an entry that navigates away without a user
    gesture as one its back skips, so the refused back then went to page 2 and asked `Back page2`,
    not `Back page1`. Forcing that recovery path fails the old fixture every time with the CI
    message; the fixture now starts the next case from a new web view (page 1, then page 2 declared)
    after any late cancel.
  - **A script poll lost to a navigation.** MAUI's Android `EvaluateJavaScriptAsync` never completes
    when a navigation replaces the document before the script answers: 5 of 17 polls of
    `document.title` during navigations went unanswered (a probe). The fixture awaited it without a
    bound, so a poll racing the positive control's navigation hung it past the runner's 30 s. Every
    script call now has a bound: polls retry, reads fail with a clear message, and scripts that
    navigate don't wait for their own answer.

  With both fixed, three consecutive full Android runs passed (156 passed, 5 skipped each).
- The WebView fixture once waited for a page 1 completion its shared log already held from an
  earlier show, so on a slow CI simulator it read the title before the new web view had loaded;
  `ShowPage1` now counts only completions after its own show, and the refused cases wait for the
  attempt (and Android's late completion) instead of a fixed delay.
- MAUI's Android `EvaluateJavaScriptAsync` can stay pending forever when a navigation replaces the
  document before the script answers: MAUI behaviour to file upstream; await it with a timeout.
- Android can't keep the previous page when it cancels a reload or a back navigation (§2.7's table);
  MAUI's own overloads can leave a superseded `ScrollToAsync` pending; both are MAUI behaviour to
  file upstream.
- The iOS simulator shut down twice between runs (environment); each rerun on a booted simulator was
  green.
- Not in 4c-1, as designed: `.Ref` on pages; cells MAUI drops without unbinding stay mounted until
  the list unmounts; a discarded `ReactorPage` needs `Unmount()`; Windows (4g).

## 3. 4c-2: collections and items

### 3.1 Scope, and what moved out

4c-2 closes these rows of spec 068: CollectionView, SwipeView, RadioButton, Picker and Label. Ordered
by the ports (spec 068 §4), it lands as:

1. **Port-driven**: Label spans (PointOfSale), CollectionView layouts, spacing, sizing, header,
   footer, empty view and threshold tests (3 ports), selection state in items (the ports' `Selected`
   visual state), RadioButton with view content (WeatherTwentyOne, PointOfSale), Picker item
   display (DeveloperBalance).
2. **No port**: CollectionView grouping and SwipeView's missing parts.

**CarouselView and IndicatorView moved to 4c-4 (§5)** (design review round 1). The round-0 design
reported the carousel position once at rest, but it still wrote under the user's finger (the
handler's apply reasserts the declared position during a drag, MAUI's iOS `ScrollToPosition` has no
drag guard, and the settle bound flushed regardless), and its conditional in-flight flag guessed
whether a change came from Reactor, which MAUI's carousel code gives no operation identity to tell.
Neither has a port; §5 lists what their design must meet.

Smaller parts not in 4c-2, each with its reason, are in §3.11.

**MAUI sources.** Behavior claims cite MAUI 11.0.0 (commit `4e6582f`): `TemplatedCell2`,
`SelectableItemsViewController2` (iOS and Mac Catalyst), `SelectableViewHolder` and
`TemplatedItemViewHolder` (Android), `SelectableItemsView`, `RadioButton`, `RadioButtonGroup` and
the Android radio mapper, `SwipeView`, and `Span`, `FormattedString` and `Label`. The device
characterizations confirm or correct them.

### 3.2 What WinUI has (the source of truth for names)

| Need | WinUI Reactor | Used for MAUI |
|---|---|---|
| Item templates | `ListView<T>(items, key, (item, index) => …)`, `ItemsView<T>`; an item's selection is app state the template closes over, or `ItemContainerElement.IsSelected` (ItemsView) | the template-closes-over-state model; no index parameter (§3.3) |
| Grouping | none | MAUI's names (`IsGrouped`, group header and footer) |
| Swipe | `SwipeControl` with `LeftItems`, `RightItems` (`SwipeItemData(Text, OnInvoked, IconSource, Background, Foreground, BehaviorOnInvoked)`), `LeftItemsMode`, `RightItemsMode` | `SwipeItemData`, `LeftItems`, `RightItems`, `…ItemsMode` |
| Radio with content | `RadioButton(Element content, isChecked, onIsCheckedChanged, groupName)`, stored in `ContentElement`; checking one reports the others' unchecks | `ContentElement`, the same factory shape, and the same reports (§3.6) |
| Item display | `ComboBox(string[] Items, SelectedIndex)`, `ItemElements` | strings computed by the factory (§3.8) |
| Rich text | `RichTextBlock(Paragraphs)` of `RichTextInline`s: `RichTextRun` (`Run(text)`), `RichTextHyperlink` (`Hyperlink(text, Action onClick)` or a `Uri`), `RichTextLineBreak`; run modifiers `FontSize`, `FontFamily`, `FontWeight`, `Foreground`, `TextDecorations`, … | the inline types and factories; MAUI's property names on them (§3.9) |

The MAUI renderer's established rule applies throughout: element and control names are MAUI's,
properties keep MAUI's names (`TextColor`, `FontAttributes`), and where WinUI has the feature and
MAUI has no API of its own (an event, a structural shape), WinUI's name wins.

### 3.3 Selection state in items

**The gap.** The ports style a selected item through the `Selected` visual state. In Reactor the item
template already re-renders whenever the list renders (`ItemsHost.Sync` re-renders every realized
cell), so a template that closes over the app's selection shows it. Two things are missing: a
direct way to get the state, and a way to turn off the platform's own highlight, which MAUI draws
unless the item's root view has a `Selected` visual state that sets a background
(`TemplatedCell2.UpdateSelectionColor` clears iOS's gray `SelectedBackgroundView`;
`TemplatedItemViewHolder.UseDefaultSelectionColor` skips Android's activated-highlight drawable).
Without that, a template that draws its own selection gets a second highlight on top.

**API.** An overload whose template takes the item's state:

```csharp
public readonly record struct ItemState(bool IsSelected);

public static CollectionViewElement CollectionView<T>(
    IReadOnlyList<T> items, Func<T, string> key, Func<T, ItemState, Element?> itemTemplate);
```

`ItemState` is a struct so it can grow without new overloads: `IsCurrent` arrives with the carousel
in 4c-4 (§5).

- **`IsSelected` is the declared selection**, not the platform's, by the rules the selection write
  uses (one function serves both, so they can't diverge):
  - `SelectionMode.None`: `false` for every item, whatever `SelectedKey` or `SelectedKeys` declares;
  - `Single`: the item's key equals `SelectedKey` (`SelectedKeys` is ignored);
  - `Multiple`: `SelectedKeys` contains the key (a set built once per apply; `SelectedKey` is ignored);
  - a `null` key, a `null` or empty `SelectedKeys`, or a declared key no item has: nothing selected.

  Selection is controlled (§3.4), so a tap the app refuses never renders the item selected; the
  platform's own `Selected` flag (and its accessibility trait) follows the tap until the restore.
- **The platform highlight is off** when the overload is used: the element's
  `ShowsSelectionHighlight` (default `true`) is set `false` by it, and the app can set it either way.
  When `false`, every `ReactorCell` gets a visual state group whose `Selected` state sets the cell's
  `BackgroundColor` to transparent, which is exactly the condition MAUI checks on both platforms; the
  item's own content is untouched. Changing it remounts the list (`CanUpdate`), because iOS clears
  the gray highlight once and never restores it.
- **No remount on a selection change.** A cell's element is keyed by its item key (unkeyed with
  `ReuseItemTrees`), and the key doesn't change, so a changed `IsSelected` updates the tree in place:
  component state, refs and native views stay.
- **Recycling.** `ReactorCell.Render` computes the state for the item it is bound to, at render. A
  cell rebound from a selected item A to item B renders B's state; with `ReuseItemTrees` that is an
  update, without it the keyed remount it already is. Nothing is cached per cell.
- **Echo.** The template is called from Reactor's render (the `Sync` re-render loop), never inside
  the platform's `SelectionChanged` callback, and the selection write keeps its echo scope (§3.4).

The element's `ItemTemplate` becomes `Func<object?, ItemState, Element?>`; the item-only factory
adapts its template. That changes the record's primary constructor (a preview break; the factories,
which every sample and test uses, are unchanged).

### 3.4 CollectionView

```csharp
sealed class Menu : Component
{
    public override Element Render()
    {
        var (selected, setSelected) = UseState<string?>(null);
        var list = UseElementRef<CollectionView>();

        return VStack(12,
            (CollectionView(Products, p => p.Id, (p, state) =>
                Border(VStack(8, Image(p.Photo).Height(96), Label(p.Name)))
                    .Stroke(state.IsSelected ? Colors.White : Palette.Secondary, 1)
                    .CornerRadius(8)) with
            {
                Arrangement = ItemsArrangement.VerticalGrid,
                Span = 2,
                ItemSpacing = 12,
                SelectionMode = SelectionMode.Single,
                SelectedKey = selected,
                OnSelectionChanged = keys => setSelected(keys.Count > 0 ? keys[0] : null),
                Header = Label(Run("Menu ").FontSize(32), Run($"{Products.Count} items").TextColor(Palette.Gray)),
                EmptyView = Label("Nothing on the menu today"),
                RemainingItemsThreshold = 4,
                OnRemainingItemsThresholdReached = LoadMore,
            }).Ref(list),
            Button("Top", () => list.Current?.ScrollTo(0, position: ScrollToPosition.Start)),
            Button("Show the special", () => list.Current?.ScrollToKey(SpecialId)));
    }
}
```

**Untested parts get tests; these behaviors change:**

- **The selection mode is written in the echo scope.** It is written outside it today, and MAUI
  raises `SelectionChanged` when a mode change changes the effective selection (single to none with
  an item selected), which is reported as a user's deselection.
- **Item lookup by key is a dictionary** kept by `Sync`, so `Find` (selection, `ScrollToKey`) is O(1)
  and spans groups.
- **`ScrollToKey`**, an extension:
  `bool ScrollToKey(this CollectionView view, string key, ScrollToPosition position = MakeVisible, bool animate = true)`.
  MAUI's `ScrollTo(object item, …)` can't take the app's item (Reactor's items source holds boxes),
  and calling it with a key string compiles and silently does nothing, hence a separate name. It finds
  the item (and its group) and calls MAUI's `ScrollTo(item, group, position, animate)`; it returns
  `false` when no item has the key, and throws on a `CollectionView` Reactor didn't create. MAUI's
  `ScrollTo(int index, int groupIndex, …)` works as is through the ref: Reactor's items keep the
  declared order.

**Grouping.**

```csharp
public sealed record ItemGroup(string Key, object? Group, IReadOnlyList<object?> Items);

public static CollectionViewElement CollectionView<TGroup, T>(
    IReadOnlyList<TGroup> groups, Func<TGroup, string> groupKey, Func<TGroup, IReadOnlyList<T>> groupItems,
    Func<T, string> key, Func<T, ItemState, Element?> itemTemplate,
    Func<TGroup, Element?>? groupHeader = null, Func<TGroup, Element?>? groupFooter = null);
// and the same with Func<T, Element?> itemTemplate
```

- The element gains `Groups` (`IReadOnlyList<ItemGroup>?`, built by the factory),
  `GroupHeaderTemplate` and `GroupFooterTemplate`; `IsGrouped` is `Groups is not null`. Switching
  between grouped and flat remounts (`CanUpdate`).
- The native source is an observable list of group boxes, each an observable list of item boxes, as
  MAUI's grouped sources expect. `Sync` diffs the groups by key, then each group's items by key, with
  the existing fewest-moves algorithm.
- **Keys are unique across the list**, not per group, because selection, `ScrollToKey` and the
  dictionary are by key; group keys are unique among groups. A duplicate throws, as today.
- **A move to another group is a removal and an insertion** (MAUI has no cross-section move), so
  the item gets a new box. Its tree keeps its state only when MAUI rebinds the same cell directly from
  the old box to the new one: the cell's element keeps its key, so the rebind is an update. Any other
  path (another cell realizes the new box, or the cell is retired in between) mounts a fresh tree.
  Nothing more is promised; the test uses real cell instances (§3.10).
- Group headers and footers are `ReactorCell`s with a role, created by their own `DataTemplate`s,
  bound to the group box; their tree is keyed by the group key, so a header cell rebound to another
  group remounts unless `ReuseItemTrees`. They retire like item cells (§2.4).
- Not in MAUI: sticky headers. Group headers aren't selectable (MAUI's behavior).

**Selection: reports, writes and the native selection** (spec 067 §14 left it uncharacterized).

- **A report is captured at notification.** The keys come from the `SelectionChanged` arguments'
  `CurrentSelection`, the selection MAUI raised, never from a later read of the view.
- **It reaches the app before any reconcile can overwrite it.** MAUI raises `SelectionChanged`
  synchronously when a tap selects (`SetValueFromRenderer` from the platform's delegate), so the
  callback runs inside that notification, before any queued render applies. Renders compute their
  elements when they run, so a render that was queued before the tap reads the state the callback
  set. The test makes that order observable (§3.10). If the characterization finds any
  `SelectionChanged` raised after a Reactor write has returned (a late echo), this design stops there
  and is revised, as the carousel's was: telling a late echo from input would mean guessing which
  write caused it.
- **Multiple selection is compared as key sets**, not sequences: a native order that differs from
  the declared order isn't a difference, so it writes nothing.
- **The native selection is verified after batch completion (iOS and Mac Catalyst).** MAUI's only
  deferral of a native selection is in `SelectableItemsViewController2.SelectItem`: when the
  collection view isn't loaded, it calls `PerformBatchUpdates(null, completion)` and selects in the
  completion (`ValidateAndSelectItem`), which checks that the item is still in the source but not
  that it is still the selected item. So a write of A, then B, before completion can leave A selected
  natively while `SelectedItem` says B, and a deferred A can also land after the user selected B and
  the app accepted it, when B's render writes nothing (the virtual selection is already B) and so
  would schedule no repair. The verification:
  - **Scheduled by every commit** that applies a `CollectionView` whose mode isn't `None`, accepting
    or refusing, whether or not it wrote anything, by Reactor's mount, and by a commit that **enters
    `None`**: MAUI's `UpdatePlatformSelection` returns early for `None` and its mode mapper deselects
    nothing, so native selected paths (and a deferred selection still pending) would survive; the
    effective selection in `None` is empty, so the verification deselects them. Each schedule bumps
    the list's verification generation, invalidating older work.
  - **The barrier** is Reactor's own `PerformBatchUpdates(null, completion)` on the same collection
    view. MAUI's deferral is issued synchronously inside the property write (the handler maps
    `SelectedItem` as it changes), so it is queued before Reactor's barrier, issued at the end of the
    apply. That UIKit runs the completions of queued batches in order is checked first, on device
    (§3.10); the design depends on it, and is revised if it fails.
  - **One barrier outstanding.** A schedule while one is outstanding marks it stale. A completion
    takes exactly one of three branches:
    1. **invalid owner**: the list is unmounted, or the handler (or its platform view) isn't the one
       that issued the barrier: the work is retired, nothing is issued;
    2. **stale generation**: the work is retired and the current work is issued as a fresh barrier
       (behind every batch queued since);
    3. **current generation**: it compares (below).
  - **A user's selection notification invalidates pending corrections**: it bumps the generation and
    marks the outstanding barrier stale, so no correction computed before the input is applied
    after it.
  - **The current completion compares** the native `IndexPathsForSelectedItems`, mapped to keys, with
    the declared effective selection of the live element, as sets. Equal: done. Different: it selects
    and deselects natively inside the echo scope (UIKit's programmatic selection calls no delegate, so
    nothing is reported), then verifies once more with a fresh barrier; a mismatch on that second
    check logs a `DiagnosticLog` warning and stops, so a platform that never converges can't loop.
  - **Android** isn't verified unless its characterization shows a divergence; then the same
    protocol runs with the adapter's selected positions and the next layout pass as its barrier.
- **Characterize first**, on all three platforms, reading the **native** selection (iOS
  `IndexPathsForSelectedItems`, Android the adapter's selected positions) as well as MAUI's: single
  and multiple writes; overlapping writes during realization (before the list is loaded) and during
  a batch (with an items change in the same apply); removing the selected item (Reactor's `Find` gives
  `null` and writes no selection, suppressed); changing the mode with a selection; a user tap
  accepted and refused; in multiple mode a tap that adds and one that removes. Per case: whether
  `SelectionChanged` arrived inside the write or late, the final native and virtual selections, and
  the exact callback sequence after settling (400 ms and a dispatcher turn). If Android diverges too,
  the same reconcile runs there from the adapter's selected positions.

### 3.5 SwipeView

```csharp
sealed class TaskRow : Component<TaskItem>
{
    public override Element Render()
    {
        var swipe = UseElementRef<SwipeView>();
        var row = HStack(8,
            Label(Props.Title).Padding(12),
            Button("More", () => swipe.Current?.Open(OpenSwipeItem.RightItems)));
        return (SwipeView(row) with
        {
            LeftItems = [new SwipeItemData("Done", Props.Complete) { Content = DoneBadge(Props) }],
            LeftItemsMode = SwipeMode.Execute,
            RightItems =
            [
                new SwipeItemData("Delete", Props.Delete) { BackgroundColor = Colors.Red, Icon = Icons.Trash },
                new SwipeItemData("Close", () => swipe.Current?.Close()),
            ],
            Threshold = 80,
            OnSwipeEnded = (direction, isOpen) => Props.SetOpen(isOpen),
        }).Ref(swipe);
    }
}
```

- **Four sides**: `LeftItems`, `RightItems`, `TopItems`, `BottomItems`, each with its mode
  (`LeftItemsMode`, …; MAUI's `SwipeMode`, `Reveal` or `Execute`, the same names as WinUI's).
  `Threshold` (`double?`, cleared to MAUI's default when `null`) is MAUI-only: WinUI's
  `SwipeControl` has none.
- **`SwipeItemData`** replaces `SwipeAction` (WinUI's name; a preview break, decided in review):
  `SwipeItemData(string Text, Action? OnInvoked = null)` with `BackgroundColor` and `Icon` as today,
  and `Content` (`Element?`). With `Content`, the item is a MAUI `SwipeItemView` showing a child tree
  (`CreateChildRoot`), and `Text` becomes its semantic description, so the item keeps an accessible
  name.
- **Items are rebuilt only when their structure changes** (a count, a kind, a text, a color, an icon
  or a mode). A changed `Content` re-renders its child tree in place, so re-rendering an open swipe
  view doesn't rebuild what MAUI shows. Content trees are unmounted when their item is removed, at a
  rebuild and with the swipe view.
- **An item invokes only while it is current.** Callbacks read the live element at the item's index
  (existing), so after a rebuild an old `SwipeItem` (still held by the platform, or by anything else)
  would invoke the replacement's action at its index. Each item's handler therefore checks that its
  sender is still the item at that index in the side's current `SwipeItems`, and that the element is
  live; a retired item invokes nothing. No generation counter: the identity check reads what the view
  holds.
- **Events, fire-only, through `Live`**: `OnSwipeStarted(SwipeDirection)`,
  `OnSwipeChanging(SwipeDirection, double offset)` and `OnSwipeEnded(SwipeDirection, bool isOpen)`.
- **Opening and closing are MAUI's API through the ref** (§2.6): `Open(OpenSwipeItem, animated)`
  and `Close(animated)` on the Controls `SwipeView`, which both registries render. No wrapper: they
  are public and complete. Before the view has a handler they do nothing, as in plain MAUI
  (documented, §2.4 "When the view can act").
- **Open isn't a controlled value.** WinUI's `SwipeControl` exposes only `Close()`, MAUI raises no
  event for every way a swipe view closes (a tap elsewhere closes it on iOS), and no port declares
  it. `OnSwipeEnded`'s `isOpen` tells the app what the user left.
- **The threshold is MAUI's behavior, tested as such**: below it nothing is invoked; above it a
  `Reveal` side stays open without invoking, and an `Execute` side invokes exactly once (MAUI's iOS
  `ValidateSwipeThreshold` invokes the first visible item), the current render's callback, for a text
  item and a content item alike.

### 3.6 RadioButton with view content, and Reactor-owned groups

```csharp
HStack(12,
    (RadioButton(ThemeCard("Light", Icons.Sun, theme == AppTheme.Light), theme == AppTheme.Light,
        on => { if (on) setTheme(AppTheme.Light); }, "theme") with { IsIndicatorVisible = false })
        .Semantics(description: "Light theme"),
    (RadioButton(ThemeCard("Dark", Icons.Moon, theme == AppTheme.Dark), theme == AppTheme.Dark,
        on => { if (on) setTheme(AppTheme.Dark); }, "theme") with { IsIndicatorVisible = false })
        .Semantics(description: "Dark theme"));
```

**Content.**

- **`ContentElement`** (`Element?`, WinUI's name), taking precedence over the text `Content`, and
  WinUI's factory `RadioButton(Element content, bool isChecked, Action<bool>? onIsCheckedChanged = null, string? groupName = null)`.
  The content is a child of the radio button, so it renders through the registry: direct views in
  it are hosted by `AsView` on the direct registry.
- **The template.** MAUI shows view content only through a `ControlTemplate` (`ContentAsString`
  otherwise; on Android the platform view is a `ContentViewGroup` only when a template resolves). With
  `ContentElement`, Reactor sets MAUI's `RadioButton.DefaultTemplate` (the indicator, then the
  content), as WinUI shows its glyph next to the content; with `IsIndicatorVisible = false` (decided in
  review), a template that is only a `ContentPresenter`, so the content is the whole radio button: the
  ports' cards. `IsIndicatorVisible` is MAUI-only: the ports replace the whole look with a
  `ControlTemplate`, which Reactor excludes (components are the templates), and WinUI needs a style
  for the same.
- **The checked look is the content's props from state**, replacing the `Checked` visual state: the
  app renders the content from the declared `IsChecked` (controlled), so a refused tap never shows it
  checked.
- **Remount** when `ContentElement` switches between `null` and an element, or `IsIndicatorVisible`
  changes (`CanUpdate`): Android picks its platform view when the handler is created. Content updates
  in place otherwise.
- **Accessibility.** With a template, MAUI's semantic description falls back to
  `Content.ToString()`, a type name for a view. Reactor sets the description from the text `Content`
  when it isn't empty. For view content the docs require `.Semantics(description: …)`, and a radio
  button with view content and no description logs one `DiagnosticLog` warning (deduplicated: once
  per native radio button, not per render). The device fixture asserts the accessible name and
  the radio role with its checked state on each platform; where MAUI's templated path lacks the role
  or the state, the handler supplies it before the gate (characterized first).

**Groups are Reactor's, on both registries** (design review round 1). MAUI's grouping can't work in
direct layouts: `RadioButtonGroup.UncheckOtherRadioButtonsInScope` searches `LogicalChildren` (from
the page for a named group, from the parent for an unnamed one), and a Controls view inside a direct
layout gets a logical parent (`DirectLayout.AnchorChild`) without being registered as its logical
child. So radio buttons in direct stacks never uncheck their peers. Reactor takes grouping over:

- **MAUI's grouping is neutralized.** Each native radio button gets a unique internal `GroupName`
  (`reactor-` plus a GUID), set once at creation; the element's `GroupName` is never written to the
  view. MAUI then finds no peer, on either registry. Side effects at 11.0.0, from a source search
  that the implementation's first step repeats at `4e6582f`: `GroupName` is read only by the grouping
  code (`RadioButtonGroup`, `RadioButtonGroupController`) and by the Windows handler, which already
  gives each native button its own GUID (MAUI issue 11418); nothing on iOS, Mac Catalyst or Android
  exposes it to accessibility. The cost is one walk of the page's logical tree per check, which finds
  nothing. Not supported: MAUI's `RadioButtonGroup.GroupName` and `SelectedValue` attached properties
  on an ancestor (set through `.Set`), whose controller rewrites the descendants' names; documented.
- **Peers come from Reactor's tree**, through a table per top-level root (shared with its child
  roots), keyed by a scope and a name. Registration follows mount, a `GroupName` change and unmount;
  the handler is hand-written for it (it needs the engine node, spec 067 §16).
  - **A named group is scoped to its page.** A top-level root holds several pages at once (a
    navigation stack's pages and modals, tabs), so a root-wide name would join groups across pages.
    The scope is the radio button's page, found as `SystemBack.PageOf` finds it: up through host
    nodes and across child roots (cells, slots, swipe item content) to the nearest `Page` (or Shell
    item), else the root's host page. That is MAUI's scope too (its named search starts at the
    page).
  - **An unnamed group is scoped to the nearest host ancestor within the radio button's own root**
    (the layout that holds it, skipping components and groups): MAUI's "same parent" rule. A child
    root is a boundary: an unnamed radio button at the top of a cell, slot or swipe item tree has the
    child root itself as its scope, never the list or swipe view that owns it. So separate cells never
    share an unnamed group.
- **A user's check is one transaction, with the native group settled before any callback**, in
  MAUI's own order (`OnIsCheckedPropertyChanged` unchecks the peers, then raises the checked
  button's `CheckedChanged`):
  1. on `CheckedChanged(true)` outside a write, the table bumps the generation of that group (per
     scope and name: only a check in the same group supersedes a transaction) and snapshots the
     participants: the checked button and its group's natively checked peers;
  2. it unchecks each peer natively, inside that peer's echo scope;
  3. it dispatches the captured reports, the peers' unchecks (`false`) first, then the checked
     button's (`true`), as WinUI reports both. Before each callback it rechecks that the participant
     is mounted (`Live`) and still a member of the group, and that the generation is still its own;
  4. an exception from a callback goes to `ReportError`, and dispatching continues;
  5. in `finally` it requests a restore for every participant still mounted, so if the app refuses,
     the declarations restore every button.

  All of it happens inside the tap's notification, before any render drains, and a callback that
  reads the native state sees the peers already unchecked.

  **Transactions never run reentrantly** (confirmation round). MAUI's `BindableObject.SetValue`
  queues a write to a property that is already being set and applies it after the original
  notification returns (`DelayedSetters`), and `RadioButton` raises `CheckedChanged` during that
  notification. So a check started from a callback (B's transaction reports A's uncheck, A's callback
  checks C through a ref, C's transaction unchecks B while B's own setter is still active) would get
  B's write queued, and its notification would arrive after the echo scope that should cover it had
  closed. Therefore a `CheckedChanged` outside a write that arrives while a transaction is
  dispatching (or while Reactor is inside any radio button's notification) is not processed there:
  it is queued, in arrival order, and the queue is drained on a posted turn, after the current
  transaction and MAUI's setters have unwound. Each queued entry is re-read when drained: a check
  whose button is still natively checked runs its own transaction from the native state then; an
  uncheck whose button is still natively unchecked is reported and restored; anything else is
  dropped (a later write superseded it). Queueing a check bumps its group's generation, so a
  same-group outer transaction dispatches nothing more; a transaction in another group or page
  isn't affected and delivers all its reports. The outer transaction's restores still run.
- **Conflicting declarations are applied verbatim**: state is the source of truth, so two buttons in
  one group declared checked are both shown checked. Once per render pass, after the pass (posted
  and coalesced, so a replacement that mounts before the old button unmounts doesn't count), the
  table checks each group that changed; two or more declared checked logs one `DiagnosticLog`
  warning per group (deduplicated until the group stops conflicting). Documented.
- **Reactor's own writes don't cascade**: each button is written to its declaration in its own echo
  scope, and MAUI finds no peer, so the round-0 cascade problem (a declared change, or a restore,
  reporting the peers' unchecks) is gone. That replaces round 0's thread-wide radio-write scope.
- **Characterize first**: a tap in a named and an unnamed group, the declared change in both tree
  orders, a refused tap; the exact callback sequences, and the native checked states. Every
  `CheckedChanged` a Reactor write causes must be raised inside that write (MAUI raises it from the
  property change); one raised later stops this design for revision rather than being classified.
  The reentrant sequence above is characterized on plain MAUI radio buttons too (B's handler
  unchecks A, A's handler checks C, C's handler unchecks B): when B's `false` is raised relative to
  B's handler returning, which confirms the queueing premise.

### 3.7 Both registries

| Element | Controls handler | Direct registry |
|---|---|---|
| CollectionView | hand-written (`ItemsHost`, child roots per cell) | the Controls view; cells render through the root's registry, so their content is direct |
| SwipeView | hand-written (item child roots) | the Controls view; content and item views render direct |
| RadioButton | hand-written (the group table needs the engine node) | the Controls view; its content renders direct; groups work across direct and Controls layouts (§3.6) |
| Picker | hand-written (existing) | the Controls view |
| Label | hand-written (existing), plus inlines | the hybrid falls back to the Controls label when `Inlines` is set (`ILabel` has no formatted text); switching remounts, as any hybrid switch does |

A ref to any of these but `Label` gets the Controls view on both registries, so
`UseElementRef<CollectionView>` and `<SwipeView>` need no fallback rule.

### 3.8 Picker item display

```csharp
Picker(Projects, p => p.Name, index, setIndex, title: "Project")
```

- **A factory overload**, `Picker<T>(IReadOnlyList<T> items, Func<T, string> display, int selectedIndex, Action<int>? onSelectedIndexChanged = null, string? title = null)`,
  that computes the display strings. The element is unchanged: its `Items` are those strings, compared
  by sequence (existing), so a re-render with the same texts writes nothing, and a changed text
  rewrites the items and the index in one echo scope (existing).
- Selection stays an index (WinUI's `ComboBox`, MAUI's `SelectedIndex`). Two items may share a text.
- Rejected: MAUI's `ItemDisplayBinding` (Reactor has no bindings) and a `Func` on the element, whose
  delegate differs every render and would force a rewrite.

### 3.9 Label spans

```csharp
Label(
    Run("Total ").FontSize(14).TextColor(Palette.Secondary),
    Run($"{order.Total:C}").FontSize(32).Bold(),
    Run(" incl. tax").Italic().LineHeight(1.2),
    Hyperlink(" details", () => setShowDetails(true)))
```

- **`LabelElement.Inlines`** (`RichTextInline[]?`, WinUI's name for a text's runs), set by
  `Label(params RichTextInline[] inlines)`; when set it wins over `Text` (MAUI also nulls one when the
  other is set). Label-level settings (alignment, line breaking, `MaxLines`, the label's font as the
  runs' default) apply as in MAUI.
- **Inline types and factories are WinUI's**: `RichTextInline`, `RichTextRun` (`Run(text)`) and
  `RichTextHyperlink` (`Hyperlink(text, Action onClick)`), in the MAUI namespace. Their properties use
  MAUI's names, as every MAUI text element does: `FontSize`, `FontFamily`, `FontAttributes`,
  `TextColor`, `BackgroundColor`, `CharacterSpacing`, `TextDecorations`, `LineHeight`, `TextTransform`;
  the text modifiers (`Bold`, `Italic`, `FontSize`, `FontFamily`, `TextColor`, `CharacterSpacing`,
  `Underline`, `Strikethrough`, and `LineHeight`, `BackgroundColor` for runs) get
  `where T : RichTextInline` overloads, so a run is styled like a label.
- **Hyperlink** is a span with a `TapGestureRecognizer`, which MAUI supports on spans. It is
  underlined unless it declares `TextDecorations`, as WinUI underlines hyperlinks; its color is the
  run's or the label's.
- **A recognizer invokes only while it is current.** Its handler reads the live element's inline
  at its index, so a new lambda each render rewires nothing. It first checks that its span is still at
  that index in the label's current `FormattedString` and that it is the recognizer currently
  installed on that span (recorded per span when installed). A span whose inline goes from a
  hyperlink to a run loses its recognizer, and gets a new one if it becomes a hyperlink again, so a
  retained old recognizer, of a replaced span or of a reused one, invokes nothing; nothing runs after
  unmount either.
- **Updates in place.** When the number of inlines is unchanged, each `Span`'s changed properties are
  set or cleared (MAUI propagates span changes to the label), and the span instances stay; a span
  gains or loses its recognizer as its inline becomes or stops being a hyperlink. A changed count
  rebuilds the `FormattedString`. Unchanged inlines (by value, ignoring callbacks) write nothing.
- Not mirrored: WinUI's paragraphs (a MAUI label is one paragraph; a `"\n"` in a run breaks the
  line), `RichTextLineBreak`, `InlineUI`, and the `Uri` hyperlink (call `Launcher` from `onClick`).

### 3.10 Tests

Each oracle names the mutation that must turn it red; the implementation records each run in §3.13.
Counts are positive witnesses (a test that asserts something didn't happen also asserts the thing
that would have caused it did).

**Headless (`tests/Reactor.Maui.Tests`), both registries where a cell, content, radio button or
label can be in a direct layout:**

| Test | Oracle | Mutation |
|---|---|---|
| Item state, single and multiple | each realized cell's template sees `IsSelected` for its key from the declared selection; a selection change re-renders cells without remounting (an item component's mount count stays 1, its state survives); a refused tap renders no cell selected (renders with `true`: 0) while the platform's `SelectedItem` did change (positive witness) | read the platform's selection; key the element by `(key, IsSelected)` |
| Item state rules | `None` with a declared `SelectedKey` and `SelectedKeys`: no item selected; `Single` ignores `SelectedKeys`, `Multiple` ignores `SelectedKey`; `null`, empty and unknown keys select nothing; the selection write applies the same rules (the same cases, read from `SelectedItem(s)`) | a separate rule for the template |
| Item state and recycling | a cell rebound from selected A to unselected B renders B unselected, with and without `ReuseItemTrees` | cache the state per cell |
| Highlight | with the state overload every cell (created before and after the change) has a `Selected` state setting `BackgroundColor`; with the item-only factory none has; toggling `ShowsSelectionHighlight` remounts | skip the group on cells created later; update in place |
| Selection report order | tap B (`SelectedItem` set from the "renderer") with an unrelated render already queued: the callback log has `["b"]` before that render's apply runs, and the queued render (reading the accepted state) leaves B; the same in multiple mode with a key set; a refusal then restores A | post the report; read the view after the render instead of the arguments |
| Native selection verification | a stub port (barrier issue, completion, native read, native select) drives completions by hand: an old MAUI completion selecting A runs between the user's B notification and the render accepting B, and the next current completion restores B natively, with nothing reported; an obsolete completion issues exactly one replacement barrier (counted) and compares nothing, and the replacement then compares; completions delivered out of order apply only the current generation's; a refused selection with no virtual write is verified too; entering `None` (from single and from multiple, with a pending deferred selection) is verified against an empty selection and deselects; a fix triggers exactly one more verification, and a second mismatch warns and stops; unmount, and a handler change, between issue and completion: retired, nothing issued | schedule only on a virtual write; skip `None`; let a notification keep the pending correction; a stale completion that compares, or issues nothing; drop the generation; no handler guard; verify forever |
| Selection echo | per the characterization (§3.4): a mode change with a selection reports nothing; a write in a batch with an items change reports nothing; multiple selection declared in another order than the view's writes nothing | write the mode outside the scope; compare sequences |
| Layout | each `Arrangement` gives MAUI's layout with orientation, `Span` and spacing; changing one writes a new layout, an unchanged render writes none; `ItemSizingStrategy` set and cleared | compare the element instead of the layout's values |
| Header, footer, empty view | set, replaced (same tree updated: state kept), cleared (cleanup ran, slot `null`), on the direct registry hosted | skip the unmount on clear |
| Threshold | `RemainingItemsThreshold` set and back to `-1`; `SendRemainingItemsThresholdReached` reported once; not after unmount (positive control: before it) | invoke through `Current` |
| Grouping | groups and items keyed (moves keep boxes); duplicate keys across groups throw; header and footer cells render their templates with the group and remount when rebound to another group; selection and `ScrollToKey` find an item in any group; grouped↔flat remounts | per-group key uniqueness; a flat dictionary not rebuilt on group moves |
| Cross-group move, by cell instance | cell X bound to item k in group 1 (state set to 5); k moves to group 2; X rebound directly to the new box keeps 5; a different cell Y bound to the new box starts fresh; X retired (`null`) and rebound starts fresh | key the tree by box instead of key; keep trees across retirement |
| `ScrollToKey` | finds the item and group and raises MAUI's `ScrollToRequested` with them; an unknown key returns `false` and requests nothing; a foreign `CollectionView` throws | pass the key instead of the box |
| SwipeView items | four sides with their modes; `Threshold` set and cleared; a text, color or icon change rebuilds that side; a `Content` change keeps the `SwipeItems` instance and the content component's state; a removed content item's tree unmounts (cleanup count); invoking reaches the live callback, not after unmount; events reported through `ISwipeViewController` | rebuild every render; compare the content element |
| Retired swipe items | keep a reference to a side's item, replace the side's structure, invoke the old item: no callback; the new item at the same index invokes (positive control) | read only the index |
| RadioButton content | the content is the radio button's `Content` (a hosted direct view on the direct registry); the default template with the indicator, the content-only template without; switching content to text or the indicator flag remounts; content updates keep state; no description logs one warning across renders, a description none | set the template after the handler; skip `CanUpdate`; warn per render |
| Radio groups | each native button has a unique internal `GroupName`; immediately after a native check (`IsChecked` set from the "handler"), before any render drains: the checked button reported `true`, every checked peer unchecked natively and reported `false`, once each; for a named group spanning a direct `VStack` inside a Controls `Grid` inside a direct `HStack`, and an unnamed group in a direct stack (positive witness of the bug: with MAUI's grouping instead of Reactor's, a test switch, the peers in direct stacks stay checked); two unnamed groups in separate direct stacks don't affect each other; a declared change in both tree orders reports nothing; refused, every button returns to its declaration and nothing more is reported | write the element's `GroupName` to the view; peers by parent node instead of nearest host; uncheck outside the peer's scope; report only the checked one |
| Radio scope | two pages of one navigation stack (and two tabs), each with a group named "size": checking in one leaves the other page's checked button checked and unreported; unnamed radio buttons at the top of two cells of one list don't affect each other, nor do unnamed ones in the header and footer slots; a named group spanning two cells of one page does join (positive control) | a root-wide name scope; walk `ContextParent` for the unnamed scope |
| Radio transaction | the checked button's callback reads the native state and sees every peer already unchecked; the reports arrive peers first, then the checked one; a throwing callback reaches the boundary and the next callback still runs, and the restores run; a callback that unmounts a later peer skips that peer's callback | dispatch before unchecking; no membership recheck; restore outside `finally` |
| Radio reentrancy | the confirmation round's sequence on real MAUI radio buttons (their `DelayedSetters` included): B's transaction reports A's uncheck, A's callback checks C through a ref, C's transaction is queued and runs on a later turn and unchecks B; every `CheckedChanged` raised outside a Reactor write is one the test caused (none escapes an echo scope: the log of unsuppressed notifications is exactly the user's and the ref's), only C ends checked, and the reports are exact; the outer transaction stops after the queued same-group check; nested checks in an independent group and on another page both deliver their checked reports | run the nested transaction inline; one generation per table |
| Conflicting declarations | two buttons of one group mounted checked: both shown checked, one warning after the pass (and none on the next render); a `GroupName` change that merges two groups with a checked button each warns once; replacing a checked button with another checked one in one render warns nothing (positive control: the two-checked mount above) | check during the pass; warn per render |
| Picker display | the platform items are the display strings; the same strings write nothing; a changed text rewrites and keeps the index | — |
| Label inlines | spans with each property set, updated in place (same `Span` instances), cleared; a count change rebuilds; `Inlines` and `Text` replace each other; a hyperlink is underlined unless declared; its tap (`SendTapped` on the span's recognizer) invokes the current render's callback, not after unmount; on the direct registry a label with inlines is a Controls label | rebuild on every change; capture the first render's callback |
| Retired span recognizers | keep a reference to a hyperlink span's recognizer, rebuild the `FormattedString` (a count change) so another hyperlink sits at that index, tap the old recognizer: no callback; at an unchanged count, `Hyperlink` (R1) → `Run` → `Hyperlink` (R2) on the same span, tapping the retained R1: no callback; the current recognizer invokes (positive control) in both | read only the index; check the span but not the recognizer |

**Device (`tests/Reactor.Maui.DeviceTests`), Mac Catalyst, iOS and Android, `[controls]` and
`[direct]` unless noted.** Fixtures that check callbacks wait until they settle (400 ms and a
dispatcher turn) and assert the exact sequence, including zero where none is expected.

| Fixture | Oracle |
|---|---|
| `CollectionView_Layouts_Geometry` | platform frames: a vertical list stacks cells with a gap equal to `ItemSpacing` (± 1 device pixel); a horizontal list puts the second cell right of the first; a 2-span grid puts cells 0 and 1 on one row (same y, different x) and cell 2 below; each case also asserts the cells exist (count ≥ 3) |
| `CollectionView_SizingStrategy` | items whose content differs in height: under `MeasureAllItems` their cells differ (positive control); under `MeasureFirstItem` they equal the first's |
| `CollectionView_HeaderFooterEmpty` | the header's frame is above the first cell and the footer's below the last; with no items the empty view is shown and no cell exists, and with items it is gone |
| `CollectionView_Threshold` | 50 items, threshold 5: scrolling to the end through the ref reports ≥ 1; the same scroll with `-1` reports 0, with the end reached asserted (positive witness) |
| `CollectionView_Grouping` | headers, items and footers appear in order by platform frame, for two groups; moving an item between groups moves its cell |
| `CollectionView_Selection_Characterization` | the matrix of §3.4, with the native selection read on every case |
| `CollectionView_BatchCompletionOrder [iOS, Catalyst]` | the premise of the barrier: MAUI's deferred selection (a write while the list isn't loaded) and Reactor's barrier, issued after it, complete in that order, recorded over repeated runs and with an items change in the same apply |
| `CollectionView_OverlappingSelection_Programmatic` | writes of A then B before the list is loaded, and within a batch with an items change: after settling, the native selected index paths (iOS) or positions (Android) are exactly B's, as is `SelectedItem`, and **zero** reports; positive witness: without Reactor's verification (a fixture switch) iOS is recorded showing A |
| `CollectionView_OverlappingSelection_User` | a user's B accepted while a deferred A is pending: native B and `SelectedItem` B after settling, and the reports are **exactly `[B]`**, with no report from the repair |
| `CollectionView_EnterNone` | Single→None and Multiple→None with a selection, and with a deferred selection still pending: after settling the native selected paths are empty and nothing is reported; positive witness: without the verification (a fixture switch) the native path stays selected |
| `CollectionView_SelectionInItems` | a tap on a cell (Android: a dispatched touch; iOS and Catalyst: `UICollectionView.SelectItem` and the delegate's `ItemSelected`, which is what a tap runs): the item renders selected once accepted, and the native selection is that item; refused, it never renders selected and the native selection returns; the platform's highlight is off (iOS: `SelectedBackgroundView` clear; Android: the item view's background `null`) while the item-only list shows MAUI's highlight (positive control) |
| `CollectionView_SelectionInItems_Recycling` | scroll until a cell instance has shown two different items (identity witnessed): every live cell renders the selection of the item it shows, and the native selection is the declared one |
| `SwipeView_OpenCloseThroughARef` | `Open(OpenSwipeItem.RightItems)` moves the content's platform frame by the items' width, `Close()` returns it; each side opens; a re-render with new content while open keeps it open (frame unchanged) |
| `SwipeView_Threshold` | a swipe below `Threshold` invokes nothing and leaves it closed; above it a `Reveal` side stays open with no invocation, and an `Execute` side invokes exactly once, for a text item and a content item, and the callback is the current render's after a re-render (Android: a dispatched drag; iOS and Catalyst: the swipe recognizer's state changes, as a drag drives them) |
| `SwipeView_ContentItems` | a content item's view is realized in the open swipe, with its tree; invoking it runs the current callback |
| `RadioButton_Content` | the content-only radio button's frame equals its content's; the default template adds the indicator (wider); a tap (Android: dispatched; iOS and Catalyst: MAUI's recognizer, `SendTapped`) checks it, its peers uncheck natively, the sequence is exact; refused, it never renders checked |
| `RadioButton_Groups` | named and unnamed groups across nested direct and Controls layouts, and two unnamed groups under separate direct stacks: after a tap, exactly the tapped button is checked natively within its group, and the other group is untouched |
| `RadioButton_Accessibility` | the accessible name is the declared description (Android: the node's text or content description; iOS and Catalyst: `AccessibilityLabel`), the role is a radio button and the checked state is exposed and follows the declaration (Android: `checkable` and `checked`; iOS and Catalyst: the traits or value MAUI's templated view exposes, characterized first) |
| `Picker_ItemDisplay` | the platform shows the selected item's display text |
| `Label_Spans_Platform` | the platform's attributed text has one range per run with its color, size and decorations (Android: the `SpannableString`'s spans; iOS and Catalyst: the `NSAttributedString` attributes); a tap at the hyperlink's region (Android: a dispatched touch; iOS and Catalyst: the label's `GetChildElements` at that point, then the recognizer) runs the callback once; the same coordinate on a label whose run there is a plain run invokes nothing (negative control, same geometry), and a tap on another run doesn't either |

### 3.11 Decisions, answered questions, out of scope

**Decisions** (review round 1 settled the ones marked).

1. **Item state is an overload's argument, from the declared selection** (§3.3), with explicit rules
   for each mode. `ItemState` is the name (no WinUI equivalent; settled); it carries `IsSelected`
   only until 4c-4. Rejected: a hook (`UseItemState()`), which re-renders no less and needs a context
   per cell; closure only, which can't turn off the platform's highlight.
2. **The platform highlight goes off through MAUI's own rule**, set by the overload, overridable
   with `ShowsSelectionHighlight`; a change remounts.
3. **Grouping keys are unique across the list**; a cross-group move keeps state only on a direct
   rebind of the same cell (§3.4).
4. **Selection reports are captured from the event and delivered before any reconcile; multiple
   selection compares key sets; iOS's native selection is verified after batch completion**, with a
   generation that user input invalidates, scheduled by every commit, behind a barrier whose ordering
   is checked on device first (settled, §3.4).
5. **`SwipeAction` is renamed `SwipeItemData`** (settled); open and close are MAUI's methods through
   the ref, not a controlled value; retired items and recognizers check their identity (§3.5, §3.9).
6. **RadioButton**: WinUI's `ContentElement`, MAUI's default template, and `IsIndicatorVisible`
   (settled) for content-only buttons; **groups are Reactor's on both registries**, named groups per
   page and unnamed ones per host with child roots as boundaries, a user's check is one transaction
   that settles the native group before any callback, and conflicting declarations are applied
   verbatim with a warning (settled, §3.6).
7. **Picker display is computed by the factory**; the element doesn't change (§3.8).
8. **Label spans are WinUI's inline types with MAUI's property names**, updated in place (§3.9).
9. **`ScrollToKey`** is the one new imperative helper, because MAUI's item overload can't take the
   app's item (§3.4).
10. **CarouselView and IndicatorView move to 4c-4** (settled, §5).

**Identity across deferred notifications** (the self-check review round 2 asked for). Nothing in §3
infers which write or input caused a notification that arrives later:

- selection reports come from the synchronous event's arguments; the native verification compares
  states behind a barrier and is invalidated by generation, not by matching operations;
- radio transactions, swipe items and span recognizers are synchronous, or check identities the view
  holds (the sender, the installed recognizer);
- two places assume a premise instead, and the design stops for revision if the characterization
  breaks it: every `SelectionChanged` and `CheckedChanged` a Reactor write causes is raised inside the
  write (§3.4, §3.6), UIKit completes queued batches in order (§3.4), and MAUI applies a nested write
  to a radio button whose setter is active only after its notification returns (§3.6). These four
  are **hard gates**: they are characterized first, and if one fails on a device, implementation
  stops and the failure is reported before anything is built on it. Classifying a late
  notification as echo or input is what the carousel design needed and what review round 1 rejected.
- Inherited from 4c-1 and unchanged: cell retirement follows MAUI's binding-context changes (§2.4),
  and a cell MAUI drops without unbinding stays mounted until the list unmounts.

**Answered in review round 1**: `ItemState`'s name (kept), `IsIndicatorVisible` (kept), the
`SwipeItemData` rename (yes), the radio description (`.Semantics(description: …)`, a warning, and
device assertions, §3.6), the carousel (deferred). No question is open.

**Out of scope, with where each goes.**

- **Explicitly partial in spec 068** (🟡, gaps no port needs), found while reading MAUI: CollectionView
  reordering (`CanReorderItems`, `ReorderCompleted`), `Scrolled` and `ItemsUpdatingScrollMode`;
  SwipeView's `SwipeBehaviorOnInvoked` (per side in MAUI, per item in WinUI). The gate writes them
  into the rows' notes so the rows say what is missing.
- MAUI's `RadioButtonGroup` attached properties (`GroupName`, `SelectedValue` on a layout), which
  Reactor's groups replace (§3.6): proposed exclusion (⛔), the Reactor way being `GroupName` on each
  radio button and the selection in state.
- The `Uri` hyperlink and `RichTextLineBreak` (§3.9); sticky group headers (not in MAUI).
- CarouselView and IndicatorView (4c-4, §5); Windows (4g).

### 3.12 Gate for 4c-2

- **WinUI** isn't changed: `skills/reactor.api.txt` is byte-identical.
- **MAUI**: headless, engine and device suites green on Mac Catalyst, iOS and Android; each §3.10
  mutation turns its oracle red; the selection and radio characterizations, and the batch-order
  premise, are recorded in §3.13, with no late echo found (§3.11's self-check);
  the MAUI benchmark within noise (the list path changes: the key dictionary, the item state and the
  batch reconcile).
- **Docs**: `src/Reactor.Maui/README.md` documents item state and its rules, the highlight, grouping
  and what a cross-group move keeps, `ScrollToKey`, swipe items and opening through a ref, radio
  content, groups (their scopes, the transaction order, conflicting declarations) and accessibility,
  the Picker overload and label spans; spec 068's rows
  (CollectionView, SwipeView, RadioButton, Picker, Label) are updated with their evidence, the
  partial gaps and the exclusion of §3.11; the CarouselView and IndicatorView rows point to 4c-4.

### 3.13 As built (4c-2)

**What was built** (commits `bb34b688` to the docs commit; each step its own commit):

- **Hard gates first** (`ItemsCharacterization.cs`, plain MAUI, no Reactor code): all four premises
  hold on Mac Catalyst, iOS and Android (the batch gate is UIKit only); the table is below. Nothing
  was built before they passed.
- **Label spans** (`Handlers/LabelInlines.cs`): `RichTextInline`, `RichTextRun`, `RichTextHyperlink`,
  `Label(params RichTextInline[])`, `Run`, `Hyperlink`, and the run modifiers
  (`RichTextInlineExtensions`). A per-label state keeps the formatted string, its spans and each span's
  installed recognizer; the recognizer checks itself against that state (MAUI raises `Tapped` with
  the label as the sender, so the sender can't be the identity).
- **Picker**: the `Picker<T>(items, display, …)` factory.
- **CollectionView** (`Handlers/ItemsHandlers.cs`, `NativeSelectionVerifier.cs`,
  `CollectionViewExtensions.cs`): `ItemState`, the overloads, `ShowsSelectionHighlight`, `ItemGroup`
  and the grouped overloads, `GroupBox`, cell roles, `SelectionRules` (one rule set for the template
  and the write), the key dictionary, `ScrollToKey`, and the verifier with an `ISelectionPort`
  (UIKit's on iOS and Mac Catalyst, none elsewhere, a stub in tests). `ViewHandler` gained a virtual
  `CanUpdate`.
- **SwipeView** (`ContainerHandlers.cs`): `SwipeItemData`, four sides, modes, `Threshold`, content
  items as child roots, the events, structure-only rebuilds and identity-checked invocation.
- **RadioButton** (`Handlers/RadioButtons.cs`): a hand-written handler replaces the descriptor (the
  group table needs the engine node); `RadioGroups` per top-level root with page and host scopes,
  transactions, the queue, per-group generations and the post-pass conflict check.

**Deviations from §3, and why:**

1. **Entering `None` needs no repair on the devices measured.** UIKit deselects every item when MAUI
   sets `AllowsSelection = false`, and Android's adapter clears too: with the verification switched
   off, the native selection was empty after `Single → None` and `Multiple → None`, also with a
   deferred selection pending. The verification still runs on entering `None` (headless tests pin its
   logic); the device fixture records the switched-off result instead of asserting a witness.
2. **No late `SelectionChanged` was found** (gate 1), so the pending-report path of §3.4 wasn't
   needed; the report stays synchronous, from the event's arguments.
3. **Android never needed the verification**: with it switched off, the overlapping writes still
   ended at the declared item there. The port exists only on iOS and Mac Catalyst.
4. **On Android, plain MAUI loops forever** when a text radio button's `IsChecked` is written from its
   own `CheckedChanged` with a different value: the native button's listener writes the old value
   back while the setter is active, MAUI delays both writes, and they alternate (gate 4 ran out of
   memory before it was switched to templated buttons). Reactor never writes a button whose
   notification is active (the queue); an app writing one through a ref from its own callback hits
   MAUI's loop (documented in the README as a check from a callback running on the next turn).
5. **Accessibility**: Android exposes the templated radio button as `android.widget.RadioButton`,
   checkable, with its checked state; iOS and Mac Catalyst expose the `Button` trait with value `1` or
   `0` (UIKit has no radio trait). Both carry the description. No handler supplement was needed.
6. **Test input**: Android reports span regions in pixels (MAUI's tap handler passes the touch's pixel
   position to `GetChildElements`), so the span fixture taps in pixels; Android's jump scroll doesn't
   run MAUI's scroll listener, so the threshold fixture scrolls animated; UIKit's swipe is driven
   through `MauiSwipeView.HandleTouchInteractions` (reflected), Android's through dispatched touches.
7. **Two equivalent mutants**: the per-callback membership recheck and the restores in `finally` can't
   be told apart by any test, because a render (the only way membership changes) can't run inside a
   dispatch and callback exceptions are caught one by one. Both are kept as defense in depth.
8. **MAUI caps `Threshold` at an Execute side's open distance**, which for content items is their
   width (UIKit's `ValidateSwipeThreshold` takes `Math.Min(Threshold, GetSwipeOpenDistance())`): a
   narrow content item executes at a swipe far below `Threshold`. Measured when the first run of the
   below-threshold Execute check invoked a one-character content item (narrower than 40 points) at 40 points on Mac Catalyst.
   That is MAUI's behavior, not Reactor's; the fixture's content item is now wider than the
   threshold. Worth a sentence in user docs if content-item Execute sides become common.

**Characterization (device; gates on plain MAUI):**

| | Mac Catalyst | iOS | Android |
|---|---|---|---|
| Gate 1: `SelectionChanged` from a programmatic write (single, multiple, two in a turn, with an items change, removal, mode change) | always inside the write | same | same |
| Gate 2: `CheckedChanged` from a programmatic write (text and templated, unique group names) | always inside the write | same | same |
| Gate 3: MAUI's deferred select (list off-window) and a later barrier | deferred 12 of 12; completed before the barrier 12 of 12, with and without an items change between | same | n/a (no deferral) |
| Gate 4: a write to a radio button inside its own notification | applied after the notification returns, before the outer write returns | same | same (templated); a text button loops forever (deviation 4) |
| Overlapping selection, verification off | native A, virtual B (MAUI's bug reproduced) | same | native B |
| Overlapping selection, verification on | native B, no report | same | native B |
| A user's B while A is deferred | reports exactly `[b]`, native B | same | same |
| Highlight with `ItemState`; item-only list | none; shown | none; shown | none; shown |
| Radio tap: native state inside the tap; reports | peers unchecked; peer `false`, then `true` | same | same |
| Radio accessibility (checked; unchecked) | label, value 1; value 0 | label, `Button`, value 1; value 0 | `RadioButton`, checkable, checked true; false |
| Swipe below the threshold (Reveal and Execute sides); Reveal above; Execute above | closed, nothing; open at the items' width, nothing; the current callback once | same | same |
| Swipe content item opened through a ref; re-rendered while open; invoked | realized with a size and its text; updated in place, still open; the current callback once | same | same |

**Tests.** Headless `Reactor.Maui.Tests`: 429 after the review rounds (406 before them; 328 before 4c-2), in `LabelAndPickerTests.cs`,
`CollectionViewItemTests.cs`, `SwipeViewTests.cs`, `RadioButtonTests.cs` and
`ReadmeItemsSnippetTests.cs` (the README's snippets). Engine `Reactor.Maui.Engine.Tests`: 80
(unchanged). Device: 42 new fixtures (4 gates, 18 behaviors on both registries, 2 characterizations)
in `ItemsCharacterization.cs`, `ItemsFixtures.cs`, `ItemsPlatformInput.cs`. Full device suites at the
review rounds' head: Mac Catalyst 156 passed, 7 skipped; iOS simulator 156 passed, 7 skipped; Android
emulator 158 passed, 5 skipped (before them: 154/7, 154/7, 156/5); after the last fix (a radio
transaction's try/catch/finally), the radio fixtures (`RadioButton*`, 9) passed again on all three. Two earlier full Android runs each failed one 4c-1 fixture of code 4c-2 doesn't touch,
`WebView_Navigations_AreAttemptedAndCompleted` (once a refused back wasn't asked within 10 s, once
the fixture ran past 30 s after its back case); it passed alone twice and in the grouped ref and echo
runs, and the base commit's one full run passed it. The cause was two races in that fixture, timing
dependent and unrelated to 4c-2 (§2.15, open issues); fixed in the fixture, after which three
consecutive full Android runs passed.

**Mutation checks** (each broke the code, ran the class's tests, restored and touched the file): all
red but the two equivalent mutants of deviation 7. Label: rebuilding on every change, invoking
through `Current`, checking the span but not the recognizer, reading only the index, no hyperlink
underline, no direct fallback. CollectionView: state from the platform selection, keying the tree by
`(key, IsSelected)`, toggling the highlight in place, no highlight group, the mode written outside the
scope, sequence comparison, a posted report. Verification: scheduling only on a virtual write,
skipping `None`, a notification that keeps the pending correction, a stale completion that compares,
a stale completion that issues nothing, no owner guard, no second verification. Swipe: rebuilding on
every render, comparing the content element (first survived: the test's content was equal across
renders; the test now changes it), reading only the index, invoking through `Current`. Radio: the
element's `GroupName` on the view, the unnamed scope walking to the top root, peers unchecked outside
their scope, reporting only the checked one, dispatching before unchecking, the nested transaction
inline, one generation per table, conflicts checked during the pass, warning per render, the
description warning per render, the template switch updated in place.

**Benchmark** (Mac Catalyst, `tests/Reactor.Maui.Benchmark`, one pair against `29c66cd6`, base first):
total time within noise in every scenario on both renderers (head −5% to +0.3%), allocations
identical to the kilobyte. The benchmark renders stacks of labels and buttons, so it covers the
label's changed text path, not the list path; the list path's added work per commit is a key
dictionary rebuild in `Sync` and, on iOS and Mac Catalyst, one empty batch update after a commit
that wrote the selection or its mode, or followed user input (since the review round below; before
it, after every commit of a selecting list).

**Review round (pr-review of `a7db2b0c`).** Changes, each with its headless test:

- **Radio group lifetime.** A group (members and generation) is one object in one dictionary and goes
  with its last member, so a popped page or removed layout (a group key's scope) isn't kept alive; a
  transaction or queued check on a removed group stops. Test: a weak reference to a removed layout
  whose buttons were checked collects.
- **A queued check survives a render.** A callback that changes state and then checks another button
  through a ref: the state change's render ran before the queue and re-applied the button's
  declaration (suppressed), so the queue dropped the check. A render now skips the controlled write
  for a button with a queued check (the queue restores its declaration if it drops the check).
- **Radio accessible name.** The effective description (the declared one, else non-empty text) is
  written after the modifiers (a new `ViewHandler.AfterModifiers` hook), so removing
  `.Semantics(description:)` keeps the text as the name and empty text clears it.
- **Native verification only when needed** (§3.4 changed): a commit schedules it only when it wrote
  the selection or its mode, or input arrived since the native selection was last found as declared;
  an unrelated re-render issues no batch update. The stale-completion reissue is unchanged.
- **Input awaits its commit** (second verification round, reproduced headlessly by the reviewer): user
  B, MAUI's deferred A, then the stale barrier and its replacement completing before the accepting
  render; the replacement compared native A with the still-declared A, cleared the pending work, and
  B's render (no virtual write) issued nothing, leaving native A under virtual B. Input now sets a
  marker that only the next commit of the list consumes (accepting or refusing, which then always
  verifies); a completion while it is set compares nothing and clears nothing, since the declaration
  it would compare is stale. A commit that wrote no selection no longer bumps the generation of an
  outstanding barrier (it compares against the element live when it completes), so an accepted tap
  costs one barrier, not two.
- **The radio queue survives a throw**: a throwing platform write or app `CheckedChanged` handler in a
  drained check leaves the rest queued, re-posted, instead of stuck (a queued button skips its
  controlled writes).
- **A radio transaction is one try/catch/finally** (third verification round): a throw while
  settling the native group (an app's `CheckedChanged` handler on a peer, a platform write) skipped
  the restores and escaped into the platform dispatcher. Now the native phase and the dispatch share
  one `try`: the error goes to the checked button's boundary (`ReportError`), and every participant
  gets a restore, on the tap path and the queued path. The peer whose handler threw stays where MAUI
  left it: MAUI's `BindableObject` doesn't clear a property's "being set" flag when a change handler
  throws (4e6582f `BindableObject.cs` 675-702 has no `finally`), so every later write to it is queued
  forever. That's MAUI's, and goes upstream with the others. The drain's re-post after a throw is now
  unreachable (a transaction no longer throws) and kept as defense in depth.
- **Run decorations** toggle one flag of the effective value (a hyperlink starts underlined), so
  `.Strikethrough(false)` keeps a hyperlink's underline and `.Underline().Strikethrough()` keeps both.
- `Label` gained `.LineHeight(...)` and `.TextTransform(...)`; a swipe content item no longer rebuilds
  its side for a color or icon it doesn't show; `RadioButton`'s callback parameter is
  `onIsCheckedChanged`, WinUI's name.
- Not done: replacing `LabelInlines`' two weak tables with a `Label` subclass (optional in the review;
  the tables work and the subclass would change the native type users see).

More tests: an unnamed group across a component boundary, MAUI's own grouping as a witness on both
registries (Controls unchecks the peer, direct doesn't), a ref uncheck outside a transaction, queued
checks superseded or unmounted before they run, two queued checks where the first unchecks the
second, swipe mode-only and icon-only rebuilds, content items unmounted with the swipe view, header
replacement with new text, a group header cell's state across rebinds; every barrier completing
before the accepting render (single and multiple, with and without MAUI's deferred A), one barrier
per accepted tap, a throw in the radio drain. Device fixture code, run in the full suites above:
`SwipeView_ContentItems` (new), the Execute side below its threshold, the overlap precondition and a
correction count on iOS and Mac Catalyst, MAUI's real iOS label tap dispatch, exact events in gate 1
and exact selections in the selection characterization, radio accessibility before and after a
declared change, and the recycled list's exact native selection.

Mutation checks of this round (headless; each red unless noted): the item state cached per cell,
slots that skip unmount, the threshold through `Current`, grouped items left out of the key index,
keys unique per group only, the cell tree keyed by box, a tree kept across retirement, `ScrollToKey`
passing the key, the radio name scope widened to the root, the unnamed scope walking `ContextParent`,
peers by parent node (red since the component-boundary test), a kept empty group, a render writing a
queued button, the description written before the modifiers, scheduling regardless of writes, only
virtual writes scheduling, `Schedule` ignoring the input marker, a completion comparing while input
awaits its commit, a completion consuming the marker, every commit bumping the generation, the drain
not re-posted after a throw (red when added; equivalent since the transaction catches), a throwing
transaction escaping, restoring nothing, or reporting nothing, entering `None`
unverified (it survived at first: the stub port arrived after mount and left verification pending,
so the test now settles the mount first and counts issued barriers), the decoration replacing the
whole value, content items comparing color and icon, the mode not compared, a queued check
superseding every group (the per-group generation mutant moved there: transactions never nest, so
bumping every group in `Process` was equivalent). One more equivalent mutant: not restoring a dropped
queue entry, because the transaction that unchecked it restores it on a posted turn, after the drain.
Every earlier mutant still dies (`Invalidate` no longer sets pending itself, the marker does, so the
earlier "a notification doesn't make verification pending" mutant no longer applies).

Device mutation checks (each built and run on the device with the fixture filter, restored and
touched; all red): the radio content template set after the handler (`RadioButton_Content`, Android
and Mac Catalyst: the content-only width); swipe sides rebuilt on every render
(`SwipeView_ContentItems`, Android and Mac Catalyst); `Threshold` written as 10
(`SwipeView_Threshold`, Android: the Reveal side opened below 100, so the Execute side's +40 check is
guarded by its +160 positive control rather than killed alone); no native verification port
(`CollectionView_OverlappingSelection_*`, Mac Catalyst and iOS: native `0:0`); the hyperlink recognizer
also installed on the label (`Label_Spans_Platform`, Mac Catalyst and iOS: the other-run tap invoked
it, which the earlier span-only tap helper could not see); a declared radio check not written after
mount (`RadioButton_Accessibility`, Android and Mac Catalyst); the selection written as the first
item (`CollectionView_SelectionInItems_Recycling`, Android: native `0:0`).

**Open issues.**

- Android: plain MAUI's infinite loop on a text radio button written from its own notification
  (deviation 4), and MAUI's iOS `ValidateAndSelectItem` not checking that its item is still the
  selected one (§3.4): both to file upstream.
- Not in 4c-2, as designed: CarouselView and IndicatorView (§5), the spec 068 gaps of §3.11, Windows
  (4g).

## 4. 4c-3: shapes, brushes, ContentView, image equality, tests and the audit

### 4.1 Scope, and a proposed split

4c-3 closes the last controls rows of spec 068 that aren't CarouselView or IndicatorView: the new
items (shapes, gradient brushes, ContentView), image source equality, tests for every remaining 🧪
control and layout, and the §1 audit of every ✅ row. Modifiers (fonts, opacity, shadow, clip,
transforms, accessibility, swipe and pan) stay in 4d and 4e (spec 068 §4).

**Two gated PRs** (proposed here, accepted in design review round 1):

| PR | Scope | Rows it closes |
|---|---|---|
| 4c-3a | Shapes, gradient brushes (and `Border.Stroke` as a brush), ContentView, image source equality with Image and ImageButton fully tested, Border fully tested, and the property census (§4.8) with its matrix runner | Shapes, Brushes, ContentView, Image/ImageButton, GraphicsView/Border (Border half) |
| 4c-3b | The remaining 🧪 rows (BoxView, CheckBox, SearchBar, Switch, ActivityIndicator, ProgressBar, DatePicker, TimePicker, GraphicsView, FlexLayout, AbsoluteLayout, FlyoutPage, ToolbarItems) and the §1 audit of every ✅ row, through the census, plus whatever the audit finds | the rest of the 🧪 rows; each ✅ row re-confirmed or reopened |

Why two:

- **Size.** 4c-2 landed about 100 headless tests and 42 device fixtures and needed three review
  rounds. 4c-3 in one PR is larger: seven shape elements, brushes, ContentView and image keys
  (new public API, about 60 headless cases and 7 device fixtures, most on both registries), plus
  about 250 element properties to census: about 50 in ✅ rows have no test found by name (§4.8) and
  about 40 more in the 🧪 rows, which also need about 9 device fixtures.
- **Different reviews.** 4c-3a is new API and semantics. 4c-3b is almost all tests, whose review is of
  oracles and mutations.
- **Unknown outcome.** The audit may find bugs; reading MAUI for this design already found one (the
  flyout's split mode, §4.7). A fix needs its own characterization and shouldn't hold the API that
  WeatherTwentyOne needs (`Ellipse`, `FontImageSource` in images).
- **One mechanism, two steps.** The census gate lands in 4c-3a, with every existing property that
  has no test claimed as *pending 4c-3b*; 4c-3b's gate is an empty pending list. So 4c-3a's new
  elements are covered from the start and nothing can be added later without a test.


**MAUI sources.** Behavior claims cite MAUI 11.0.0 (commit `4e6582f`): `Brush`, `SolidColorBrush`,
`ImmutableBrush`, `GradientBrush`, `LinearGradientBrush`, `RadialGradientBrush`, `GradientStop`,
`Shapes/Shape` and its subclasses, `Border`, `ContentView`, `ImageSource`, `FontImageSource`,
`StreamImageSource`, `UriImageSource`, `FlyoutPage`, `FlexLayout` and `VisualElement`. Device
characterizations confirm or correct them before anything is built on them.

### 4.2 What WinUI has (the source of truth for names)

Checked in `src/Reactor` (greps for shape, gradient and brush-factory names, with positive controls:
the same probes find `EllipseElement` in `Core/Element.cs` and `AcrylicBrush` in `Elements/Dsl.cs`):

| Need | WinUI Reactor | Used for MAUI |
|---|---|---|
| Shapes | `Rectangle()`, `Ellipse()`, `Line(x1, y1, x2, y2)`, `Path2D()` (not `Path`, which collides with `System.IO.Path`), records `RectangleElement` (`Fill`, `Stroke`, `StrokeThickness`, `RadiusX`, `RadiusY`), `EllipseElement`, `LineElement` (`X1`…`Y2`), `PathElement` (`Data` as a `Geometry`, `PathDataString` converted only when it changes, falling back to `Data` when the markup is rejected, `StrokeDashArray`, `StrokeDashOffset`, `StrokeLineJoin`, `StrokeMiterLimit`, `FillRule`, `RenderTransform`, and start, end and dash caps); modifiers `.Fill(Brush)` (rectangle, ellipse, path), `.Stroke(Brush)`, `.StrokeThickness(double)` (line, path) | the four names, records and property names; MAUI-only shapes (`Polygon`, `Polyline`, `RoundRectangle`) and properties (`StrokeLineCap`, `Aspect`) keep MAUI's names (§4.3) |
| Brushes | native WinUI `Brush` objects; one factory, `AcrylicBrush(tint, …)`, named after its type; `BrushHelper.Parse(color)`; `ThemeResource.Brush(key)`. No gradient factory | MAUI's brush types; factories named after their type, as `AcrylicBrush` is (§4.4) |
| A single-child container | `Border` (and `Viewbox`); no plain content container | MAUI's `ContentView` (§4.5) |
| Images | `Image(string source)`: a string, compared by value | no WinUI shape for a non-string source; MAUI's `ImageSource`s, compared by value where they have one (§4.6) |

### 4.3 Shapes

```csharp
// WeatherTwentyOne's settings radio indicator, as Reactor renders it.
static Element Indicator(bool selected) =>
    Grid("Auto", "Auto",
        Ellipse().Stroke(Palette.DarkGray, 0.5).Fill(Colors.White).Size(16, 16).Center(),
        selected ? Ellipse().Stroke(Palette.Primary).Fill(Palette.Primary).Size(8, 8).Center() : null);

static Element Sparkline(IReadOnlyList<double> values) =>
    Polyline([.. values.Select((v, i) => new Point(i * 8, 40 - v * 40))])
        .Stroke(LinearGradientBrush(new(0, 0), new(1, 0), Colors.SkyBlue, Colors.Navy), 2)
        .StrokeLineJoin(PenLineJoin.Round);

static Element Badge() =>
    Path2D("M 0,10 L 10,0 L 20,10 L 10,20 Z").Fill(Palette.Accent).Aspect(Stretch.Uniform).Size(24, 24);
```

**Elements.** A base record carries what every MAUI shape has; each shape adds its own:

```csharp
public abstract record ShapeElement : ViewElement
{
    public Brush? Fill { get; init; }
    public Brush? Stroke { get; init; }
    public double? StrokeThickness { get; init; }
    public IReadOnlyList<double>? StrokeDashArray { get; init; }   // copied on init
    public double? StrokeDashOffset { get; init; }
    public PenLineCap? StrokeLineCap { get; init; }
    public PenLineJoin? StrokeLineJoin { get; init; }
    public double? StrokeMiterLimit { get; init; }
    public Stretch? Aspect { get; init; }
}

public sealed record RectangleElement : ShapeElement { public double? RadiusX { get; init; } public double? RadiusY { get; init; } }
public sealed record EllipseElement : ShapeElement;
public sealed record RoundRectangleElement(CornerRadius CornerRadius) : ShapeElement;
public sealed record LineElement : ShapeElement { public double X1 { get; init; } /* Y1, X2, Y2 */ }
public sealed record PolygonElement : ShapeElement { public IReadOnlyList<Point> Points { get; init; } /* copied */ public FillRule? FillRule { get; init; } }
public sealed record PolylineElement : ShapeElement { public IReadOnlyList<Point> Points { get; init; } /* copied */ public FillRule? FillRule { get; init; } }
public sealed record PathElement : ShapeElement
{
    public string? PathDataString { get; init; }
    public Geometry? Data { get; init; }
    public Transform? RenderTransform { get; init; }
}
```

**Factories**: `Rectangle()`, `Ellipse()`, `Line(x1, y1, x2, y2)` and `Path2D()` (WinUI's), plus
`Path2D(string pathData)` and `Path2D(Geometry data)` (overloads WinUI could add), `Polygon(params Point[] points)`,
`Polyline(params Point[] points)` and `RoundRectangle(CornerRadius cornerRadius)` (MAUI's names; a
`double` converts to a uniform `CornerRadius`).

**Modifiers**, generic over `T : ShapeElement` so they chain: `.Fill(Brush?)` and `.Fill(Color?)`,
`.Stroke(Brush?, double? thickness = null)` and `.Stroke(Color?, double? thickness = null)` (a
thickness leaves the current one when `null`; `.Stroke(brush)` is WinUI's call),
`.StrokeThickness(double)`, `.StrokeDashArray(params double[])`, `.StrokeDashOffset(double)`,
`.StrokeMiterLimit(double)` (WinUI's names), `.StrokeLineCap(PenLineCap)`, `.StrokeLineJoin(PenLineJoin)`,
`.Aspect(Stretch)`; `.Radius(double x, double? y = null)` on `RectangleElement`; `.FillRule(FillRule)` on
`PolygonElement` and `PolylineElement` (WinUI's name, on its path). The `Color?` overloads exist because MAUI's implicit `Color` to `Brush` conversion
turns a `null` color into `Brush.Default` (`Brush.cs` 110), a written empty brush rather than a
cleared property; the overloads map `null` to clearing, and a literal `null` binds to them (a `Color`
converts to a `Brush`, not the reverse, so `Color` is the better conversion target). The brush entries
also treat `Brush.Default` as unset, so `with { Fill = nullColor }` clears too.

**Semantics.**

- **Every property is optional.** `null` leaves MAUI's default, and a value removed on a later render
  is cleared back to it (`ClearValue`), as every other element does. `LineElement`'s coordinates
  default to 0, as on WinUI and in MAUI.
- **Collections are values** (review round 1). The element copies `StrokeDashArray` and `Points`
  into storage it owns when the property is initialized (the factories and `with` both go through
  the `init` accessor), so an app that mutates or reuses its array after rendering can't change a
  declaration Reactor has retained as the previous one. Successive declarations are compared by
  sequence; an equal one writes nothing, a different one writes a new `DoubleCollection` or
  `PointCollection`, converted only on that write; `null` clears the property explicitly.
  `ControlDescriptor.Property` can't express this (its value type must be the bindable property's,
  `ControlDescriptor.cs` 42-54), so the shapes use a projected entry built on `OneWay`: it compares
  declarations with the sequence comparer and, on a write, converts or clears.
- **Brushes are compared by value** (§4.4), so a gradient built in `Render` is written once.
- **Path data, as on WinUI.** `PathDataString` is converted with MAUI's `PathGeometryConverter` only
  when the string changes, ordinally compared, and wins over `Data`. Markup MAUI rejects (a
  `FormatException`) falls back to `Data`, or to no geometry, and logs one `DiagnosticLog` warning per
  path view and string (not per render). `Data` and `RenderTransform` are MAUI objects compared by
  reference: build them once (`UseMemo`) or use the string. No value equality for geometries (§4.11).
- **Clearing `RenderTransform` (review round 1).** MAUI's iOS and Android `PathHandler.MapRenderTransform`
  updates the drawable's matrix only when the transform is non-null, so clearing the property leaves
  the old transform drawn. Reactor clears it by first writing an identity `MatrixTransform` (which the
  mapper installs) and then clearing the value (which the mapper ignores, leaving the identity). It runs
  after the shared modifiers whenever the path showed a transform and now shows none, so a transform a
  removed style provided is reset too, and one a remaining style provides is left alone (review of
  `566796fa`). Verified
  on devices with an asymmetric path, set then cleared; if it can't be made to work, `RenderTransform`
  is left out rather than advertised as clearable.
- **Size.** MAUI sizes `Line`, `Polyline`, `Polygon` and `Path` from their geometry when the layout
  gives no size (`Shape.MeasureOverride`), and draws `Rectangle`, `RoundRectangle` and `Ellipse` into
  the size they are given. Reactor adds nothing: the README says so, and the device fixture pins it.
- **No events and no controlled values.** A tap on a shape is the `.OnTap` modifier, which every
  Controls view takes.

**Both registries.** Shapes render as Controls shapes on both; the direct registry inherits the
registration and hosts nothing inside them. A direct shape (MAUI's `ShapeViewHandler` draws any
`IShapeView`) was considered and rejected: each kind's path would be a copy of MAUI's `GetPath`
geometry code, for no port that needs it. Recorded in spec 068's Shapes row.

**Handler**: a `ControlDescriptor` per shape, sharing the base entries through one internal
`ShapeProperties()` extension (as `TextProperties()` does): `Property` for scalars, the projected
entry for the two collections, and a brush entry (comparer, `Brush.Default` as unset) for `Fill` and
`Stroke`. `Path` adds hand-written entries for the conversion and its fallback, and for clearing the
transform (spec 067 §16).

### 4.4 Gradient brushes, and brush equality

```csharp
Border(content)
    .Background(LinearGradientBrush(new(0, 0), new(0, 1), (Colors.SkyBlue, 0), (Colors.Navy, 1)))
    .Stroke(RadialGradientBrush(new(0.5, 0.5), 0.5, Colors.White, Colors.Gold), 2)
    .CornerRadius(12);
```

**Factories**, in `Factories`, named after the MAUI type they return, as WinUI's `AcrylicBrush` is:

```csharp
public static LinearGradientBrush LinearGradientBrush(Point startPoint, Point endPoint, params (Color Color, double Offset)[] stops);
public static LinearGradientBrush LinearGradientBrush(Point startPoint, Point endPoint, params Color[] colors); // evenly spaced
public static RadialGradientBrush RadialGradientBrush(Point center, double radius, params (Color Color, double Offset)[] stops);
public static RadialGradientBrush RadialGradientBrush(Point center, double radius, params Color[] colors);
```

Points and radius are relative to the element's bounds (0 to 1), as in MAUI. Offsets are `double`, so
`0.5` needs no suffix, and are stored as MAUI's `float`. The factories copy the stops into new
`GradientStop`s, so the app's array is never retained. A call with no stops is ambiguous between the
two overloads and doesn't compile; an empty gradient draws nothing in MAUI anyway. The same names as
MAUI's types work in invocations, as `Label(…)` and `Entry(…)` already do; a file that also names the
type's members (`LinearGradientBrush.StartPointProperty`) aliases it, as the tests alias `MauiLabel`.

**The gap: brushes compare by reference.** MAUI's `SolidColorBrush` overrides `Equals` (by color) and
`GradientStop` does (by color and offset, within 1e-5), but `LinearGradientBrush` and
`RadialGradientBrush` don't. Today `.Background` compares with `Equals` (`Props.SetRef`), so a
gradient built in `Render` is written on every render, and MAUI's `BindableObject` doesn't skip the
write either (it compares with the same `Equals`): every render re-maps the background.

**Brush equality** (one internal comparer, `BrushComparer`, used by every brush-valued property):

- the same instance, or both `null`: equal; one `null`: not;
- two `SolidColorBrush`es (an `ImmutableBrush`, which a `Color` converts to, is one): their colors'
  four float components exactly equal;
- two brushes of the same `LinearGradientBrush` or `RadialGradientBrush` type: equal start and end
  points, or center and radius, and equal stop sequences (same count, in order, each stop's float
  offset and float color components exactly equal);
- anything else (an app's own `Brush` subclass, two kinds): by reference. (MAUI's `ImageBrush` is
  internal in 11.0, §4.13.)

**Exact, never approximate** (review round 1). MAUI's `GradientStop.Equals` allows 1e-5 on the
offset and `Color.Equals` compares bytes; a tolerance isn't transitive, and the descriptor compares
successive declarations (`ControlDescriptor.cs` 177-181), not the installed value, so offsets 0,
0.000008, 0.000016 would each look unchanged while the installed brush stayed at 0. Exact float
comparison is an equivalence, so the installed brush is always the last declaration's value. The
comparer's hash combines exactly what it compares. (MAUI may still skip writing a solid brush whose
color is byte-equal: that is its own `BindableObject` check, and draws the same.)

Applied to `.Background` (Controls views; `ViewApplier`), `Border.Stroke`, and shapes' `Fill` and
`Stroke`. Not to `Shadow`'s brush: shadows are 4d's.

**Brushes are immutable once rendered** (review round 1), like image sources. A brush that compares
equal is never written, so the view keeps the earlier instance, and mutating a rendered brush is
unsupported: its effect depends on which instance the view kept. To change a brush, render a new one
(a factory call with the new value). Documented, and pinned by a test (§4.10).

**`Border.Stroke` becomes a `Brush?`** (was `Color?`; a preview break of the record property). The
modifier keeps `.Stroke(Color? color, double thickness = 1)`, so named `color:` arguments still
compile, with `null` clearing the stroke (not MAUI's `Brush.Default`, see §4.3), and gains
`.Stroke(Brush? brush, double thickness = 1)`. The handler stops wrapping colors in a new
`SolidColorBrush` per change.

**Both registries.** `.Background` with a brush already renders the element as a Controls view on the
direct registry (`ViewModifiers.NeedsControls`); unchanged. A brush on direct views belongs to 4d's
styling of direct views (§4.11).

### 4.5 ContentView

```csharp
ContentView(Component<Forecast>()).Padding(16).Background(Theme.Card)
```

- **`ContentViewElement(Element? Content)`** with `Padding` (`Thickness?`), factory
  `ContentView(Element? content)` and `.Padding(uniform)`, `.Padding(horizontal, vertical)` overloads
  like `Border`'s. A plain single-child container: no template (`ControlTemplate` is excluded, spec
  068), no `SafeAreaEdges` (safe areas are 4d's layout work).
- **MAUI's name.** WinUI Reactor has no plain content container (§4.2); the ports subclass
  `ContentView` for their custom controls, which components replace, and use it directly for a padded,
  colored slot.
- **The child** renders through the root's registry; on the direct registry a direct child is hosted
  by `AsView`, as in `Border` and `ScrollView`. Replacing the child updates it in place when it can
  (same type and key), so its state survives; clearing it unmounts it.
- **Both registries**: the Controls `ContentView` on both. A direct content view (MAUI's
  `IContentView`) would save one view; no port needs it in a hot path.
- A hand-written handler like `BorderHandler` (the single-child strategy), or a descriptor with
  `WithChildren`; the implementation picks the descriptor unless it can't express the slot.

### 4.6 Image source equality

```csharp
Image(new FontImageSource { FontFamily = "FontAwesome", Glyph = IconFont.Mobile, Color = Palette.MidGray, Size = 42 })
    .Size(50, 50);
Image(StreamImage($"avatar:{user.Id}", () => store.OpenAvatar(user.Id)));
```

**The gap.** `Props.SameImage` compares file sources by `File` and URI sources by `Uri`, and everything
else by reference. WeatherTwentyOne builds `FontImageSource`s in its pages; a new one per render
reloads the image: a new platform image, and a layout pass.

**Value equality, by type** (one function, `ImageSources.Same`, used by every consumer):

- the same instance, or both `null`: equal; one `null`: not;
- **a key on either** (a `StreamImage`, below): equal only if both have one and the keys are equal
  (ordinal);
- otherwise only sources of the same runtime type compare by value:
  - `FileImageSource`: `File` (as today);
  - `UriImageSource`: `Uri`, `CachingEnabled` and `CacheValidity` (the caching settings are ignored
    today, so changing them on the same URI isn't written: a fix);
  - `FontImageSource`: `Glyph`, `FontFamily`, `Size`, `Color` (MAUI's `Color.Equals`) and
    `FontAutoScalingEnabled`, every property MAUI raises `SourceChanged` for;
- anything else, including an unkeyed `StreamImageSource`: by reference.

**Stream sources have no value.** A `StreamImageSource` holds a `Func<CancellationToken, Task<Stream>>`,
and `ImageSource.FromStream(Func<Stream>)` wraps the app's delegate in a new closure on every call, so
not even the delegate can be compared; nor can the stream, which is opened only to load. What the app
knows is which image it means, so it says so with a key (the factory accepted in review round 1):

```csharp
public static ImageSource StreamImage(string key, Func<Stream> open);
public static ImageSource StreamImage(string key, Func<CancellationToken, Task<Stream>> open);
```

`StreamImage` returns MAUI's own `StreamImageSource` (through `ImageSource.FromStream`, so MAUI's
image services load it unchanged) and records the key against that instance in a
`ConditionalWeakTable`. Two `StreamImage`s with the same key are the same image: the second isn't
written and its `open` is never called. A plain `ImageSource.FromStream` source has no key and
compares by reference: create it once (`UseMemo`) or use `StreamImage`. Considered and rejected: a
`.WithKey(key)` extension on any `ImageSource` (the same word as an element's key for a different
mechanism, and only streams need it); a Reactor `StreamImageSource` subclass (MAUI's image service
lookup would have to accept it on every platform); comparing the wrapped delegate's target (a fresh
closure each call); a hint when an unkeyed stream source is replaced (a legitimate new image looks
the same; answered no in review).

**Consumers**, all through `Props.SetImage` or `SameImage` today: `Image` (Controls and the direct
`DirectImage`), `ImageButton`, `Button.ImageSource`, page and Shell item icons, and the structure
checks of `ToolbarAction.Icon` and `SwipeItemData.Icon` (an equal icon no longer rebuilds the
toolbar or the swipe side). One more compares by reference today: a page's `BackButtonBehavior`
(`ShellBackButton`, a record whose `Icon` the record's equality compares by reference), so an equal
icon rebuilds Shell's back button behavior on every render; its comparison uses `ImageSources.Same`
for the icon.

**Sources are values.** An equal source isn't written, so the view keeps the earlier instance;
mutating a rendered source is unsupported (documented), as for brushes.

**Characterize first** (device, all three platforms): how many times one mount of a `StreamImage`
calls `open` (expected 1; the oracle below uses the measured count N, and requires N > 0 and a decoded
platform image before any "no reload" count means anything), and that a re-render writing nothing
keeps the platform image instance (iOS and Mac Catalyst `UIImageView.Image`, Android
`ImageView.Drawable`).

### 4.7 The remaining 🧪 rows (4c-3b)

Each row gets the census's matrix rows for its properties and events (§4.8) on both registries, and
these, by row:

| Row | Beyond the matrix | Device |
|---|---|---|
| BoxView | — | none: `Color` and `CornerRadius` are bindable properties MAUI maps, with no Reactor code between (§4.8's criterion) |
| CheckBox, Switch | the direct views (`DirectCheckBox`, `DirectSwitch`) remap their colors on set, update and clear (a recording stub handler) | the existing echo fixtures; plus the direct views' colors set, updated and cleared on the platform (the switch's on and thumb tints, the check box's tint; which platform properties to read is characterized first), since the remap is Reactor's code |
| SearchBar | `OnSearch` reports the text, the current render's callback, nothing after unmount | none (echo exists) |
| ActivityIndicator, ProgressBar | — | new: `IsRunning` on, off, on and `Progress` changes reach the platform (iOS `IsAnimating`, `UIProgressView.Progress`; Android visibility and progress, characterized first), colors set and cleared to a fresh control's platform default, on both registries (the direct views are Reactor's) |
| DatePicker, TimePicker | headless (none today): `Date`/`Time` controlled per spec 067 §14 (a write unreported, input reported once, refused restored, nothing after unmount); `MinimumDate`, `MaximumDate`, `Format` set, updated and cleared; a declared date outside a new range shows MAUI's coerced date, reports nothing, and an equal re-render raises no `PropertyChanged` | the existing echo fixtures, plus the range and the date changed in one render (spec 067 §14's untested "several values on one control") |
| GraphicsView | a new drawable is assigned; the same one is invalidated on every update (counted through a stub handler's `Invoke("Invalidate")`) | new: a re-render redraws (a counting drawable) and shows the drawable's current color (pixels, §4.10) |
| Border | in 4c-3a (§4.4) | 4c-3a |
| FlexLayout, AbsoluteLayout | the layout properties and the children's `.Flex(…)` and `.Absolute(…)` attached values set, updated and cleared; a direct child with one of them becomes a Controls view (`NeedsControls`) | new: geometry for direction, wrap, justify, align, grow, and absolute and proportional bounds, with direct children (no modifier) placed in order |
| FlyoutPage | `IsPresented` controlled (below); `LayoutBehavior` set, updated, cleared; slot swaps (existing) | new, after the characterization below |
| ToolbarItems | the items in declared order, each with its `Order` and `Priority`; a render that changes only callbacks keeps the native items, a changed text, icon, order or priority rebuilds them; the current render's callback, nothing after unmount. **A retired item invokes nothing**: today an item the platform still holds after a rebuild invokes the action now at its index, the bug §3.5 fixed for swipe items; the same identity check is added (found reading `PageHandlers`) | new: the platform shows the items in order (iOS and Mac Catalyst `UINavigationItem.RightBarButtonItems` and the secondary menu; Android the toolbar `Menu` and overflow), and invoking a platform item runs the current callback once |

**The flyout's split mode (found reading MAUI; a hard gate).** `FlyoutPage` shows its flyout
permanently in split mode (`IFlyoutPageController.ShouldShowSplitMode`: not on phones; on tablets and
desktops in landscape with `Default` or `SplitOnLandscape`, in portrait with `SplitOnPortrait`, always
with `Split`). Mac Catalyst's idiom is `Desktop`, so a default flyout in a landscape window is split.
MAUI then sets `IsPresented = true` itself, from `OnAppearing` and on a layout-behavior change, and
refuses `false` by throwing (`OnIsPresentedPropertyChanging`: "Can't change IsPresented when setting
…"). Reactor today reports MAUI's `true` as the user's input, and if the app declares `false`, the
restore writes `false` and MAUI throws.

- **Characterize first**, on plain MAUI and on Reactor: on the iOS simulator (phone) and the Android
  emulator (phone), opening and closing through the platform (iOS: the toolbar button's action and the
  pan; Android: `DrawerLayout.OpenDrawer` and `CloseDrawer`, as the gestures end) and from code;
  whether every `IsPresentedChanged` from a write is raised inside it; on Mac Catalyst, the
  notification sequence at mount and when the window's shape changes from landscape to portrait and
  back (resizing the window), and whether writing `false` throws.
- **Proposed rule**, to revise if the characterization contradicts it: **while split, `IsPresented`
  is the platform's.** Reactor doesn't write it, and a change raised while `ShouldShowSplitMode` is
  true isn't reported (the user can't toggle a split flyout, so it is MAUI's enforcement; this reads
  the flyout's state at notification time, it doesn't guess which write caused it). When split mode
  ends (the flyout page's size changes and `ShouldShowSplitMode` turns false), Reactor requests a
  restore, so the declaration applies again, unreported. Documented: in split mode `IsPresented` and
  `OnIsPresentedChanged` do nothing.
- **The guard covers both write paths** (review round 1): the update path (`FlyoutHandler.Update`,
  `PageHandlers.cs` 617) and the slot synchronization that writes `IsPresented` at mount and after a
  slot swap (`SyncSlots`, `PageHandlers.cs` 642). A split guard on one of them only would still throw.
- If Mac Catalyst raises a change after a write returns, or the platform can toggle a split flyout,
  this stops for revision (§4.11's self-check).

**Not modeled, from each row's MAUI class** (listed against the ports that need them; the five ports
in `maui-samples` 11.0 were searched; BaristaNotes and eShop are checked before the gate):
FlexLayout's `Order` (a declarative tree orders its children) and `Position`; SearchBar's colors and
alignment; DatePicker's and TimePicker's text colors beyond the shared font settings; ToolbarItem's
`IsEnabled`. None is used by a port searched so far; each goes into its row's notes as a gap, or is
added if BaristaNotes or eShop uses it.

### 4.8 The §1 audit: a property census

**The method.** An audit by reading tests can't be repeated and goes stale the next time a property
is added. Instead:

1. **A census test** (`ElementCensusTests`, headless) enumerates, by reflection over the
   `Microsoft.UI.Reactor.Maui` assembly, every public non-abstract record that derives from `Element`,
   plus the value records that elements hold (`SwipeItemData`, `ToolbarAction`, `RichTextRun`,
   `RichTextHyperlink`, `ItemGroup`, `ShellBackButton`), and each of their public settable instance
   properties, inherited ones included (`TextViewElement`'s fonts, `LayoutElement.Padding`,
   `ShapeElement`'s strokes), except `Modifiers` (4d's census) and the core `Element`'s own members.
2. **Every (type, property) must have exactly one claim** (review round 1 tightened what counts):
   - a **matrix row** (a property case or an event case, below): the default evidence, because it
     executes every facet;
   - **covered by** a named test, only where the matrix can't express the property (a controlled
     value, a child slot, a structural value such as an item template): it names the test class and
     method (`nameof`), lists the facets it covers (set, update, clear, equal render, event), and
     carries its property-specific mutation evidence (the mutation, and the §4.13 or §2.15/§3.13 run
     that turned it red). Reflection checks that the method exists **and** is a discovered test
     (`[Fact]` or `[Theory]`);
   - **pending**: only the properties in a **baseline allowlist generated now**, from the existing
     elements that have no executable evidence, and only `"4c-3b"`. The list is checked in, may only
     shrink (the test pins its size as a ceiling), and can't hold a property added in 4c-3a;
   - **excluded** with a reason the test prints (expected to be rare; each one is listed in §4.13).
   A property without a claim, a claim naming no property (stale), or two claims fail the test, with
   the names. A name-only reference (a test that merely mentions the property) is inventory, not
   evidence: it can't make a row ✅.
3. **Positive controls in the census itself**: it asserts that it enumerated at least as many
   records as its first run found (a floor pinned in the test and logged), and that known pairs are in
   its list (`ButtonElement.BorderColor`,
   `EntryElement.ReturnType`, `EllipseElement.Fill` through `ShapeElement`), so an enumeration that
   finds nothing can't pass. Mutation: delete one claim, and the test names the unclaimed property.
4. **The matrix runner** (`PropertyMatrixTests`, a theory over every case and both registries):
   - **property case**: a base element, two transforms setting distinct values A and B, a reader for
     the native view (the Controls view, or the direct view through its MAUI interface), and the
     bindable property (Controls) or interface property name (direct). Steps: mount with A, read A;
     render B, read B, on the same native instance, with a write (one `PropertyChanged` for the
     property on Controls views; its `UpdateValue` on a recording stub handler for direct views);
     render a new element equal to B (built separately, so reference types are new instances): no
     write; render A again: read A; render the base: the read equals a freshly created view's value,
     MAUI's default, and on Controls views the property holds no local value (`IsSet` is false). The
     runner asserts A ≠ B and A ≠ the default, so the reapply and the clear can't be tautologies.
   - **values are domain-aware** (review round 1): a `bool` runs true → false → true → unset (B may be
     the default; A never is); an enum whose default is one of two values the same; a nullable
     reference value (a brush, a color, a collection) uses two distinct non-default values. On the
     direct registry an element that falls back to Controls is asserted to be the Controls type, and
     the steps run on it.
   - **event case**: an element with callback 1 and a trigger on the native view (a MAUI controller
     method or an interface call, as the existing tests use). Trigger: callback 1 once, with the
     expected arguments; render with callback 2, trigger: callback 2 once, callback 1 not again;
     unmount, trigger: nothing (the earlier calls are the positive control).
   - **controlled values** are claimed by their echo tests (spec 067 §14 and the per-control tests),
     not the matrix, which can't express a refusal.
5. **The device need**, recorded per element in the census claims' comments and spec 068's rows: a
   device fixture when Reactor code sits between the element and the platform (a direct view's remap,
   a layout of Reactor's, an input path, a quirk characterized), or when the result is rendering that
   a property read can't show (shapes, gradients, images loading, geometry). A bindable property on a
   Controls view that MAUI maps with nothing of Reactor's in between is covered headlessly; MAUI's own
   device tests cover its mapping. That criterion is itself a decision for review (§4.11).

**Preliminary census** (by property name in the three test projects, so only an estimate until the
census runs; the probe's positive control: it finds `ReturnType` in `ControlRefTests`, where 4c-1 put
it, and `RefreshColor` there too). ✅ rows with likely gaps:

| ✅ row | Properties with no test found by name |
|---|---|
| Button | `ImageSource`, `CornerRadius`, `BorderColor`, `BorderWidth`, `Padding`; the font settings' update and clear |
| Slider | `MinimumTrackColor`, `MaximumTrackColor`, `ThumbColor` |
| Stepper | `Increment` |
| Editor | updates from one value to another (set and clear are tested) |
| Entry | `HorizontalTextAlignment`, `ClearButtonVisibility`, `IsSpellCheckEnabled`, `PlaceholderColor` |
| Label | `HorizontalTextAlignment`, `VerticalTextAlignment`, `Padding`, `TextType`, and `LineBreakMode` headlessly |
| Picker | `Title` update and clear |
| VStack, HStack, Grid | `Spacing`, `Padding`, `RowSpacing`, `ColumnSpacing` and definitions updated and cleared (geometry has a device fixture) |
| ContentPage (`Page`) | `BackgroundColor`, `Padding`, `Icon`, `HasNavigationBar`, `HasBackButton`, `BackButtonTitle` |
| NavigationStack | `Title`, `Icon`, `BarBackgroundColor`, `BarTextColor`; `OnPopped` headlessly |
| Tabs | `BarBackgroundColor`, `SelectedTabColor`, `UnselectedTabColor` |
| Light and dark theme, window size hooks | device only (`Hooks_WindowSizeAndTheme_TrackTheWindow`); a headless test of the hooks' re-render and cleanup |

The rows from 4b, 4c-1 and 4c-2 (Shell, dialogs, back button, lifecycle, refs, focus, WebView, Entry's
selection, CollectionView, SwipeView, RadioButton, Label spans, ScrollView, RefreshView) were designed
against §1 and are expected to be claimed by their existing tests; the census confirms it.

**What the audit fixes.** A gap found is closed in 4c-3b with its matrix rows; a property whose test
finds a bug gets a fix with its own test and mutation, recorded in §4.13; a fix that needs design
(as the flyout's did) is designed in §4.7's style before it is built. A ✅ row whose fix can't land in
4c-3b goes back to 🟡 with the gap named.

### 4.9 Both registries

| Element | Controls | Direct registry |
|---|---|---|
| Shapes | a descriptor per shape; `Path` hand-written | the Controls shape |
| Gradient brushes | `.Background`, `Border.Stroke`, shapes' `Fill` and `Stroke` | `.Background` with a brush renders the element as a Controls view (unchanged) |
| ContentView | single-child strategy | the Controls `ContentView`; a direct child hosted by `AsView` |
| Image | `ImageHandler` | `DirectImage` (hybrid); both use `ImageSources.Same` |
| ImageButton, Button's image, icons | their handlers | Controls (`ImageButton`); a `Button` with an image falls back to Controls (unchanged) |
| Border | `BorderHandler` | the Controls `Border`; a direct child hosted |

A ref to any new element gets the Controls view on both registries.

### 4.10 Tests

Each oracle names the mutation that must turn it red; the implementation records each run in §4.13.
Counts are positive witnesses; zero oracles have a positive control in the same test.

**Headless (`tests/Reactor.Maui.Tests`), both registries unless noted. 4c-3a:**

| Test | Oracle | Mutation |
|---|---|---|
| Shape properties (matrix) | every `ShapeElement` property on each of the seven shapes, and each shape's own (`RadiusX`, `RadiusY`, `CornerRadius`, `X1`…`Y2`, `Points`, `FillRule`, `PathDataString`, `Data`, `RenderTransform`): set, updated in place with one write, an equal re-render with no write (for `StrokeDashArray` and `Points` a new equal array), reapplied, and unset: nullable properties cleared to MAUI's default with a fresh view's local-value state; the non-nullable `X1`…`Y2` and `CornerRadius` write their default (§4.13 deviation 10) | compare the arrays by reference; skip the clear; write every render |
| Collections are values | after rendering, the app mutates its points (and dash) array and renders a **new** array equal to the mutated one: the native collection is updated (the retained declaration was a copy, so the change is seen); the app reuses one array instance, mutated between renders: each render's content is written; mutating the array after the last render changes nothing natively | keep the app's array (no copy); compare by reference |
| Brush and color overloads | `.Stroke(null)` and `.Fill(null)` (literal), and a `Color?` variable holding `null`, clear the property (no local value, so a `Style` setter for it applies afterwards); `with { Fill = nullColor }` (`Brush.Default`) clears too | drop the `Color?` overload; write `Brush.Default` |
| `RenderTransform` clear | set a rotation, then clear: the identity transform was written before the clear (the native's value sequence is rotation, identity, unset), and the property ends unset | plain `ClearValue` |
| Path data | a changed string writes `Data` once, an equal string never; the string wins over `Data`; markup MAUI rejects shows `Data` (or no geometry) and logs one warning across three renders, a changed bad string one more | convert every render; let `Data` win; warn per render |
| Brush equality rules | the §4.4 rules as a table: solid by exact color (an immutable brush equal to a `SolidColorBrush` of that color); linear by points and stops in order (reordered stops differ; offsets 1e-6 apart differ; colors one float step apart differ); radial by center, radius and stops; linear vs radial with equal stops differ; `ImageBrush` and a subclass by reference; `null` cases; equal brushes have equal hashes | compare stops as a set; ignore the end point or the radius; compare gradients by reference; MAUI's tolerant `GradientStop.Equals` |
| Cumulative small changes | offsets 0 → 0.000008 → 0.000016 over three renders: each render writes, and the **installed** brush's stop offset equals the last declaration (0.000016), read from the native view | MAUI's tolerant comparison |
| Brush consumers | `.Background`, `Border.Stroke`, `Fill` and `Stroke`: a new equal gradient each render writes nothing (no `PropertyChanged`); a changed stop writes once; solid ↔ gradient writes once; clearing restores MAUI's default | keep `Equals` in `ViewApplier`; drop the comparer from one entry |
| Brushes are immutable once rendered | render A, then an equal B (not written: the native holds A), then mutate B's stop: the native still holds A with its color (the documented consequence); a factory-made changed brush is written (positive control) | — (pins the documented contract) |
| Gradient factories | stops in order with their offsets; the colors overload spaces offsets evenly (0, 0.5, 1 for three; 0 for one); mutating the app's stops array after the call changes nothing | — |
| Border (matrix and children) | `Stroke` (a color and a gradient), `StrokeThickness`, `Padding`, `CornerRadius` (uniform and per corner: a `RoundRectangle` with that radius; cleared: MAUI's default shape); content set, replaced (same type: state kept), cleared (cleanup ran); on the direct registry a direct child is hosted | skip the unmount on clear; write a new stroke shape every render |
| ContentView | content set, replaced (state kept), cleared (cleanup ran); `Padding` matrix; a direct child hosted on the direct registry; a ref to it gets the Controls view | as Border |
| Image source equality rules | the §4.6 rules as a table, including `StreamImage` against `ImageSource.FromStream`, two keys, `UriImageSource` caching settings, and `FontImageSource` differing in each of its five properties | reference equality for fonts; ignore caching; ignore keys; compare across types |
| Image source consumers | `Image` (Controls and `DirectImage`), `ImageButton`, `Button.ImageSource`, a page's icon, a back button behavior's icon, a toolbar item's icon and a swipe item's icon, each given a new equal `FontImageSource` per render: no write (no `PropertyChanged`; no `UpdateValue("Source")` on the direct image's stub handler), the native holds the first instance, the toolbar and swipe side aren't rebuilt (instances kept); positive control: a changed glyph writes once, and a `StreamImage` with a changed key writes once | keep `SameImage`'s reference fallback for fonts; skip `SameImage` in one consumer |
| Image and ImageButton (matrix and events) | `Aspect`, `IsAnimationPlaying`, `CornerRadius`, `Padding`; `OnClick` as an event case | — |
| Census | §4.8: no unclaimed, stale or doubled claim; the floor and the known pairs enumerated; covered-by claims name discovered tests and list facets and mutation evidence; pending claims only in the baseline, which is no larger than its ceiling and holds no 4c-3a property | delete one claim; add a claim for a missing property; point a claim at a non-test method; mark a new shape property pending |

**4c-3b:** the matrix rows for the 🧪 rows' and ✅ rows' properties (the pending list emptied), and:

| Test | Oracle | Mutation |
|---|---|---|
| Date and time echo | per §4.7: a declared write raises `DateSelected` inside the write and reports nothing; a `Date` set as input is reported once and, refused, restored; nothing after unmount; a declared date outside the range shows MAUI's coerced date, reports nothing, and an equal re-render raises no `PropertyChanged` | write outside the scope; compare with the old element instead of the view |
| GraphicsView | the same drawable: one `Invalidate` per update (stub handler); a new drawable: assigned, and invalidated by MAUI rather than Reactor | invalidate only when the drawable changes |
| Flex and absolute children | `.Flex(…)` and `.Absolute(…)` set, updated and cleared on the child's attached properties; a direct-registry child with them is a Controls view | skip the clear |
| Flyout split mode | with a test `ShouldShowSplitMode` (a `ReactorFlyoutPage` override seam): MAUI's own `true` while split isn't reported; a declaration of `false` while split writes nothing and throws nothing; leaving split restores the declaration, unreported; phone mode keeps the existing echo (input reported once, refusal restored) as the positive control | report while split; write while split; no restore on leaving split |
| Toolbar items | §4.7: declared order; native items kept on a callback-only render and rebuilt on a structure change; a retained retired item invokes nothing while the new item at its index does (positive control); the current callback; nothing after unmount | read only the index; rebuild on every render; capture the first callback |

**Device (`tests/Reactor.Maui.DeviceTests`), Mac Catalyst, iOS and Android, `[controls]` and
`[direct]` unless noted.** Waits accept only events after their own action; first-use costs (image
decoding, the first capture) are absorbed by a warm-up step before any timed wait.

| Fixture | PR | Oracle |
|---|---|---|
| `Pixels_Sampler` (characterization, gate) | 4c-3a | a new helper reads a view's rendered pixels (iOS and Mac Catalyst: the layer rendered into an image; Android: `View.Draw` into a bitmap). Inside a `BoxView` of a known color the sample is that color within a tolerance (logged raw, with the color space); outside it, the page's background; a second color for the same box changes the sample (positive control). Every pixel oracle below depends on it, so it runs first and a failure stops them |
| `Shapes_Render` | 4c-3a | per shape, predicted colors at fixed normalized coordinates, with strokes thick enough (8 points) that a sample sits well inside them: closed shapes (`Rectangle`, `RoundRectangle`, `Ellipse`, `Polygon`, `Path`) sample the fill at the interior and the stroke at an edge's middle; open shapes (`Line`, `Polyline`) sample the stroke only, on the line and beside it (background); a bounds corner outside the ellipse, and outside a `RoundRectangle`'s radius, is the background, while the same corner of a `Rectangle` is the fill (positive control); `Line` and `Polyline` sized from their points; an asymmetric `Path` (a right triangle) with `Aspect = Uniform` and `Fill` in a non-square box: the filled corner and the empty corner are where the triangle predicts, and with `None` the drawn extent is the geometry's own |
| `Shapes_UpdateInPlace` | 4c-3a | a new fill color: the same platform view instance, the interior sample changes; a dash array set then cleared changes an edge sample between dash and gap; an asymmetric path given a 90° `RenderTransform` and then cleared: the rotated sample pattern, then the original one (the clear really reaches the drawable) |
| `Gradients_Render` | 4c-3a | predicted RGBA at fixed normalized coordinates: a vertical linear background (top near the first stop, bottom near the last, the middle their mix); a re-render with a changed stop changes the sample (the written brush is drawn); a radial background with a non-default center (0.25, 0.25) and radius (0.5): the center sample is the first stop, the opposite corner the last; a gradient `Border` stroke differs between its top and bottom edges; the same on a direct stack (rendered as Controls) |
| `Border_Geometry` | 4c-3a | the content's frame is inset by the padding and the stroke thickness (MAUI's rule, characterized and logged); with a corner radius the corner sample is the background, without it the stroke (positive control) |
| `ContentView_Geometry` | 4c-3a | the content's frame is inset by the padding; a direct child is realized and placed |
| `Image_EqualSourceDoesNotReload` | 4c-3a | a `StreamImage` of a bundled PNG: after mount settles, its `open` call count N is logged, and the positive witnesses hold first: N > 0 and the platform view has a decoded image with the PNG's size; three re-renders with a new `StreamImage` of the same key leave N after settling (400 ms and a dispatcher turn) and keep the platform image instance; a changed key raises N (positive control, counting only calls after the change) and yields a new decoded image. A `FontImageSource` re-created per render: the platform image (decoded, non-empty) is the same instance after three re-renders and a new one for a new glyph (positive control). The same for an `ImageButton` |
| `ActivityIndicator_Platform`, `ProgressBar_Platform` | 4c-3b | §4.7 |
| `GraphicsView_Redraw` | 4c-3b | a re-render redraws (a counting drawable: the count rises after it, and only after it) and the sample shows the drawable's current color |
| `FlexLayout_Geometry`, `AbsoluteLayout_Geometry` | 4c-3b | children frames for direction, wrap, `JustifyContent = SpaceBetween` (first at the start, last at the end), `AlignItems`, a grow child filling the rest; absolute and proportional bounds; direct children placed in order |
| `FlyoutPage_Characterization` (gate) | 4c-3b | §4.7's matrix, on plain MAUI and Reactor |
| `FlyoutPage_IsPresented` | 4c-3b | phones: opened through the platform and accepted, it stays open; refused, it closes (Android `IsDrawerOpen`; iOS the flyout's presented state); a declared change opens it and reports nothing; nothing after unmount. Mac Catalyst (split): no exception at mount or when the app declares `false`, nothing reported, the flyout visible; after resizing the window to portrait, the declaration applies |
| `ToolbarItems_Platform` | 4c-3b | §4.7; a callback-only render keeps the platform item; a removed item leaves the platform |

### 4.11 Decisions, open questions, out of scope

**Decisions** (review round 1 settled the ones marked).

1. **Split into 4c-3a and 4c-3b** (§4.1), joined by the census's pending list (settled).
2. **Shapes take WinUI's names** (`Rectangle`, `Ellipse`, `Line`, `Path2D`, and their properties);
   MAUI's names for MAUI-only shapes and properties (`Polygon`, `Polyline`, `RoundRectangle`,
   `StrokeLineCap`, `Aspect`); Controls on both registries.
3. **Path data follows WinUI**: a string converted when it changes, winning over `Data`, falling back
   to it, with one warning; geometries and transforms by reference.
4. **Brushes are MAUI's types, compared by exact value** for solid and gradient brushes, immutable
   once rendered; factories named after the type, as WinUI's `AcrylicBrush` (settled: exactness,
   immutability).
5. **`Border.Stroke` is a `Brush?`**, with `Color?` and `Brush?` modifier overloads; `null` clears
   (settled).
6. **ContentView** is a plain container with `Padding`, Controls on both registries.
7. **Image sources compare by value** where they have one (file, URI with its caching settings,
   font), and by key for `StreamImage(key, open)` (settled); other streams by reference.
8. **The census** is the audit, and the gate's measure; executable matrix cases are its default
   evidence, other claims list facets and mutation evidence, and the pending list is a shrinking
   baseline (settled); a bindable property MAUI maps with nothing of Reactor's in between needs no
   device fixture (§4.8).
9. **The flyout's split mode** leaves `IsPresented` to the platform, after characterization (§4.7).
10. **Collections are values**: copied on init, compared by sequence, converted only on a write
    (settled).
11. **`RenderTransform` clears through an identity transform** (settled), or is left out.

**Identity across deferred notifications.** Nothing here infers which write caused a later
notification: brush and image equality compare values at render; the census and matrix are
synchronous; the flyout reads `ShouldShowSplitMode` when its notification arrives. The flyout's rule
assumes every `IsPresentedChanged` a Reactor write causes is raised inside the write, and that a
split flyout can't be toggled by the user. Both are hard gates of `FlyoutPage_Characterization`; if
either fails, 4c-3b stops there and reports.

**Answered in review round 1**: the split (two PRs); the stream key (`StreamImage(key, open)`, not
`.WithKey`); no hint for unkeyed stream sources; exact brush equality; collections and brushes as
values; the census's evidence rules; domain-aware matrix values; the pixel and load oracles' witnesses
(§4.10); the flyout guard on both paths (§4.7). Kept as proposed, with no objection: the gradient
factories' names and evenly spaced overloads, the `Path2D` overloads, the device criterion (§4.8), and
pixel samples at a per-channel tolerance with the raw values logged. No question is open.

**Out of scope, with where each goes.**

- Modifiers (fonts, opacity, shadow and its brush, clip, transforms, accessibility) → 4d; swipe and
  pan → 4e; a brush on direct views (direct styling) → 4d.
- Direct shapes and a direct `ContentView` (§4.3, §4.5), and attached flex and absolute values on
  direct children without the Controls fallback (MAUI's `FlexLayout` supports non-bindable children
  through its instance setters): no port needs them; recorded as possible optimizations.
- `ImageBrush` (no port), value equality for geometries and transforms (`Path.Data`, `.Clip`).
- `ContentView.SafeAreaEdges` → 4d's layout work.
- CarouselView and IndicatorView (4c-4, §5); Windows (4g).

### 4.12 Gate for 4c-3

**4c-3a:**

- **WinUI** isn't changed: `skills/reactor.api.txt` is byte-identical.
- **MAUI**: headless, engine and device suites green on Mac Catalyst, iOS and Android; the pixel
  sampler, the image-load and the `RenderTransform` characterizations recorded in §4.13 before the
  fixtures that use them; each §4.10 mutation of 4c-3a turns its oracle red; the census green with
  pending claims only from the baseline; `Reactor.Maui.slnx` builds in Release with no warnings; the
  MAUI benchmark within noise (`ViewApplier`'s background comparison and every image consumer
  change).
- **Docs**: `src/Reactor.Maui/README.md` documents shapes (sizing, path data and its fallback),
  gradient brushes and that brushes and image sources are values (equality, not mutating,
  `StreamImage`), with its snippets mirrored by tests; spec 068's rows (Shapes, Brushes, ContentView,
  Image and ImageButton, Border) are updated with their evidence; GraphicsView/Border is split so
  Border shows ✅; spec 065's direct-renderer notes and the CHANGELOG record the new elements and the
  `Border.Stroke` break.

**4c-3b:**

- The census has no pending claim; every 🧪 control and layout row of spec 068 is ✅ (or 🟡 with a
  gap no port needs), and each ✅ row's note names its census claims' evidence.
- The flyout characterization recorded in §4.13, with neither hard gate broken (or the design revised
  and reviewed); each §4.10 mutation of 4c-3b turns its oracle red.
- Suites green on the three platforms; the benchmark within noise if a handler changed.
- **Docs**: README sections for the flyout's split mode and toolbar items; spec 068's "not modeled"
  gaps (§4.7) written into their rows.

### 4.13 As built (4c-3a)

4c-3b's as-built section is written when it lands.

**What was built** (commits after `168a42f0`, each step its own commit):

- **Characterizations first** (`VisualsCharacterization.cs`, plain MAUI): the pixel sampler gate, a
  path's `RenderTransform` clear, a stream source's open count, and a `Border`'s content inset; table
  below. Nothing was built on them before they passed.
- **Elements** (`Elements/Visuals.cs`): `ShapeElement` and the seven shapes, `ContentViewElement`, and
  `ValueList`, the owned storage the `init` accessors copy point and dash lists into (C#'s `field`
  keyword, so `with` copies too). `BorderElement.Stroke` is a `Brush?`.
- **Handlers** (`Handlers/Visuals.cs`): `BrushComparer` (exact, with a matching hash; `Brush.Default`
  as unset), `SequenceComparer<T>`, the descriptor helpers `BrushProperty`, `ListProperty` (the
  projected entry, on `OneWay`) and `ShapeProperties`, a descriptor per shape and for `ContentView`,
  `PathData` (markup conversion with its fallback and warning, and the identity-then-clear
  transform reset, run after the modifiers through an internal `ControlDescriptor.WithAfterModifiers`
  hook), and `ImageSources` (value equality and `StreamImage` keys in a `ConditionalWeakTable`).
  `Props.SetBrush` serves `.Background` and the border stroke; `Props.SameImage` forwards to
  `ImageSources.Same`; a page's back button compares its icon with it.
- **Factories and modifiers**: the shapes, `ContentView`, `StreamImage`, the gradient factories;
  `.Fill`, `.Stroke` (brush and color), `.StrokeThickness`, `.StrokeDashArray`, `.StrokeDashOffset`,
  `.StrokeMiterLimit`, `.StrokeLineCap`, `.StrokeLineJoin`, `.Aspect`, `.Radius`, `.FillRule`;
  `ContentView.Padding`; the border's two `.Stroke`s (a color 1 unit thick unless given, as before; a
  brush keeps the current thickness, as on shapes).
- **The census** (`PropertyMatrix.cs`, `MatrixCases.cs`, `CensusBaseline.*.cs`): 58 element types
  and value records, every public settable property claimed: 89 property cases (most built by a typed
  `Case(make, name, set, a, b, …)` from fresh factory calls through the modifiers) and 1 event case,
  each run on both registries (180 theory rows), 2 covered-by claims (the content slots), and 348
  pending properties for 4c-3b. The pending list must equal its ceiling and be a subset of
  `CensusBaseline.Original`, the list as generated, whose SHA-256 is pinned; no property of an element
  4c-3a covered (the shapes, `ContentView`, `Border`, `Image`, `ImageButton`) may be pending.

**Deviations from §4, and why:**

1. **MAUI's rectangles and ellipse construct with `Aspect = Fill`** (`Rectangle`, `RoundRectangle` and
   `Ellipse` constructors), so clearing the property would draw with the property's default, `None`.
   Unsetting `Aspect` on them writes `Fill` back (`ShapeProperties(constructedAspect:)`). Found by the
   matrix, whose clear step now compares the property's local-value state with a fresh view's (a
   fresh rectangle has a local `Aspect`) instead of requiring none.
2. **`ImageBrush` is internal in MAUI 11**, so an app can't make one; the by-reference rule is tested
   with an app's own `Brush` subclass.
3. **The package's global usings alias `PenLineCap`, `PenLineJoin` and `FillRule`**: they live in
   `Microsoft.Maui.Controls.Shapes`, whose `Path` and `Rectangle` a namespace import would bring into
   every component file. (`Stretch` is in `Microsoft.Maui.Controls`.) The test projects, the
   benchmark and the sample import the shipped props file instead of copying its `Using` items, so a
   change to it is compiled there.
4. **Rejected path markup warns once per change** of the (markup, data) pair, the only time the
   entry converts, rather than once per view and string: rendering the same bad string again warns
   nothing, changing it (or the fallback `Data`) warns again.
5. **A literal `null` binds to the `Color?` overloads**, as designed; their `null`-to-clear mapping is
   redundant with the comparer's `Brush.Default` normalization (equivalent mutant M15). Both kept: the
   overload documents intent, the normalization covers `with { Fill = nullColor }`.
6. **Mutating a rendered brush can also hide a later change**, found writing the immutability test:
   the mutated brush is the retained declaration, so a new brush equal to the mutation compares equal
   to it and isn't written. The README says so; the test uses a different color for its positive
   control.
7. **Shell item icons** go through the same `Props.SetImage` as page icons and aren't in the
   consumer test, which covers images (both registries), image buttons, button images, page,
   toolbar, swipe and back-button icons.
8. **The census enumerates the `Microsoft.UI.Reactor.Maui*` namespaces**: the shared composition
   records (`Microsoft.UI.Reactor.Core`) and the members dialog elements inherit from
   `ComponentElement` (`ComponentType`, `Props`) are the engine's, not this census's.
9. **Instrument notes.** A bindable property with a default-value creator counts as set once read
   (`StrokeDashArray`, `Padding`), so the matrix reads `IsSet` before reading the value. On Android a
   layout that touches the status bar lays its children below it (`SafeAreaEdges`), so pixel and
   geometry fixtures stage their subjects 120 points down and capture the subject itself.
10. **`LineElement`'s coordinates and `RoundRectangleElement.CornerRadius` are non-nullable**, as on
    WinUI (and `RoundRectangle(radius)` takes one), so they are always written: unset means 0, written
    as a local value, and a `Style` setter for them can't apply. The matrix marks them as writing their
    default instead of clearing.
11. **Review of `566796fa`.** A style that provided a path's transform, removed while neither
    declaration has one, left the transform drawn (the entry skipped an unchanged `null`): the reset
    moved after the modifiers (§4.3). URI sources compare `OriginalString` ordinally, since `Uri`'s
    `==` ignores the user info and the fragment. `ViewModifiers.NeedsControls` treats a `null`-color
    background (`Brush.Default`) as no brush, so it no longer forces a Controls view. The internal
    `ReferenceEqualityComparer<T>` gave way to the BCL's.

**Characterization (device; plain MAUI):**

| | Mac Catalyst | iOS | Android |
|---|---|---|---|
| Sampler: a red/blue box with a white margin, then a changed color | exact (240 px at scale 2, the view hierarchy drawn into sRGB) | exact (360 px at scale 3) | exact (315 px, `View.Draw` into ARGB_8888) |
| Path `RenderTransform`: rotated, then `ClearValue` | the rotation stays drawn | same | same |
| Path `RenderTransform`: rotated, then an identity transform and `ClearValue` | the original drawing | same | same |
| One mount of a stream source; a new instance | opened once, decoded 24×12; opened again, a new platform image | same | same |
| `Border` content inset (padding 10, stroke 6) | 16 on every side; the padding area transparent | same | same |

**Tests.** Headless `Reactor.Maui.Tests`: 679 (429 before), in `PropertyMatrix.cs` (the runner and the
census, 4 census facts), `MatrixCases.cs`, `VisualsTests.cs`, `PathDataWarningTests` and
`ReadmeVisualsSnippetTests.cs`. Engine `Reactor.Maui.Engine.Tests`: 80 (unchanged). Device: 18 new
fixtures (4 characterizations, 7 behaviors on both registries) in `VisualsCharacterization.cs` and
`VisualsFixtures.cs`, with `Pixels.cs` (the sampler) and `TestPng.cs`. Full device suites: Mac Catalyst
174 passed, 7 skipped; iOS simulator 174 passed, 7 skipped; Android emulator 176 passed, 5 skipped
(before: 156/7, 156/7, 158/5). `Reactor.Maui.slnx` builds in Release with no warnings.

**Mutation checks** (each broke the code, ran the class's tests, restored and touched the file; all
red but M15): stops compared as a set (M1), the end point ignored (M2), the radius ignored (M3),
gradients by reference (M4, 33 red across rules, matrix and background), MAUI's tolerant stop
equality (M5: the 1e-6 and float-step rules and the cumulative test), `.Background` by `Equals` (M6),
the fill entry without the comparer (M7), no list copy (M8), lists by reference (M9), lists not
cleared (M10), a plain transform clear (M11), markup converted every render (M12), `Data` winning
(M13), `Brush.Default` written (M14), the `Color?` overload writing `Brush.Default` (M15: survived,
equivalent, deviation 5), fonts by reference (M16), URI caching ignored (M17), keys ignored (M18), no
type check (M19, red once the subclass rule was added), a button image by reference (M20), the back
button by record equality (M21), no constructed aspect (M22), content not cleared on `null` in a
`ContentView` (M23) and a `Border` (M25), a remount on every update of each (M24, M26); the census:
a claim deleted, a covered claim at a non-test method, a 4c-3a property pending, the baseline grown
(C1 to C4). On devices (each built and run with the fixture filter, restored and touched; all red): a
plain transform clear (`Shapes_UpdateInPlace`, all three platforms: the rotated pattern stayed), keys
ignored (`Image_EqualSourceDoesNotReload`, Mac Catalyst: 2 opens became 8), fonts by reference (the
same, Android direct: a new font image), the dash list not cleared (`Shapes_UpdateInPlace`, iOS
direct: the dashes stayed).

Review of `566796fa`, mutation checks (all red): no reset after the modifiers (the style and the
local transform tests; on Android, `Shapes_UpdateInPlace`'s styled path stayed rotated), a reset
whenever a transform was shown (a transform a remaining style provides was overwritten), the pending
list with a same-size swap, the original list edited, the ceiling raised, `.StrokeMiterLimit` and
`.FillRule` as no-ops, URIs by `Uri`'s `==` (the user-info and fragment rules), a brush stroke
resetting the border's thickness to 1, the back button ignoring its text, and `NeedsControls` on any
background (the `null`-color label stayed Controls). Full device suites afterwards: Mac Catalyst 174
passed, 7 skipped; iOS 174 passed, 7 skipped; Android 176 passed, 5 skipped.

**Benchmark** (Mac Catalyst, one pair against `7281bf4d`, base first): total time within noise or
lower in every scenario on both renderers (−4% to +0.03%, and −13% and −35% on the 40-ms `UpdateOne`),
allocations identical to the kilobyte. The benchmark sets no brushes or images, so it measures the
changed paths' cost when unused: `ViewApplier`'s background comparison and the image comparison
behind every icon.

**Open issues.**

- MAUI's iOS and Android `PathHandler.MapRenderTransform` ignore a `null` transform, so clearing one
  keeps the old one drawn (worked around here): to file upstream with the 4c-2 issues.
- 4c-3b: the 348 pending properties, the remaining 🧪 rows and the flyout's split mode (§4.7).

## 5. 4c-4: CarouselView and IndicatorView

To be designed when 4c-4 starts. Moved out of 4c-2 by its design review (round 1), which found the
round-0 design (a position reported once at rest, compared with the declaration, plus a conditional
in-flight flag) unsound. The design must meet these requirements, each from a finding:

- **Never write or report during a drag.** The round-0 design still wrote under the user's finger:
  `CarouselViewHandler.Apply` reasserts the declared position on any render during a drag, MAUI's iOS
  `ScrollToPosition` (`CarouselViewController2`) has no drag guard, and the settle bound flushed
  regardless of the drag. Writes and reports wait for the end of a drag, whatever renders meanwhile.
- **No provenance guessing.** The in-flight flag guessed whether a change came from Reactor; it would
  have swallowed accessibility scrolls and `ScrollTo` through a ref. MAUI's
  `MauiCarouselRecyclerView` (`UpdateFromPosition`, `UpdatePosition`) and `CarouselViewController2`
  expose no operation identity, so the design reads platform state, never infers the cause.
- **Characterize accessibility actions and ref commands** (VoiceOver and TalkBack scrolling,
  `ScrollTo` through a ref) along with swipes, flings, programmatic writes, items changes, `Loop` and
  iOS 26's debounced position writes, before the echo design is fixed.
- **A long-hold test**: the user holds a drag while an unrelated render runs; nothing is written to
  the carousel and nothing is reported until the drag ends, and then exactly once.
- **`ItemState.IsCurrent`** joins `IsSelected` (§3.3), replacing the `CurrentItem`, `PreviousItem` and
  `NextItem` visual states.
- **IndicatorView** stays a separate controlled element linked to the carousel through state, with
  MAUI's names; its `IndicatorTemplate` is a proposed exclusion (render a row of elements from the
  count and position instead).
