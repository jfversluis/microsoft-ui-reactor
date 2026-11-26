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

Found while reading (not changed by 4c-1): WinUI's update path clears the old cell without the
owner check its unmount path has. A ref moved in one render from a later sibling to an earlier one
is set by the earlier sibling, then cleared by the later one, so it ends `null`. A WinUI unit test
would confirm it; it is a follow-up, not part of this phase.

### 2.2 The split: nothing moves to the core

WinUI's `ElementRef` can't move: its public `Current` is a `FrameworkElement`, and the public API is
fixed. What MAUI needs is small: a cell, an owner check, and a type check. WinUI wouldn't call a
shared cell (its cell also feeds reference edges and the dirty set), so there is no second consumer,
and spec 069's rule applies: share the model only when both renderers use it.

| Where | What |
|---|---|
| Core | Nothing |
| MAUI public (`Microsoft.UI.Reactor.Input`, as on WinUI) | `ElementRef<T> where T : class, IView` with `T? Current`; `UseElementRef<T>()` on `RenderContext` (in `MauiCoreHooks`) and on `Component`; `.Ref(ElementRef<T>?)` on every `ViewElement` |
| MAUI internal (engine) | An untyped cell: the current native as `object?`, the node that set it, and the expected type. `ViewModifiers` holds it in an internal `Ref` property |

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
                Button("Back", () => help.Current?.GoBack())),
            ScrollView(ResultList(query)).Ref(results).Height(240),   // ResultList: the app's own content
            (WebView("https://learn.microsoft.com/dotnet/maui") with
            {
                // Runs synchronously while the platform waits for the answer.
                OnNavigating = e => e.Cancel = !e.Url.StartsWith("https://learn.microsoft.com/"),
            }).Ref(help));
    }
}
```

### 2.4 Ref semantics

**Binding lives in the engine**, not in the handlers. After a host node mounts or updates,
`ReactorRoot` binds the ref its element declares; when the node unmounts, it unbinds. That covers
every handler kind (Controls, descriptor, direct, hybrid, third-party) the way removed-view release
does (spec 067 §16), and the engine has the element at unmount, which `IElementHandler.Unmount`
doesn't. The engine reads the cell through an internal interface that `ViewElement` implements, so
the engine tests' fake elements can implement it too.

| Case | Behaviour |
|---|---|
| Mount | `Current` is the native view, set after the handler mounts and its children are synchronized, before any effect of the pass runs |
| Update, same ref | Nothing to do: the native is the same |
| Update, ref removed or swapped | The old cell is cleared if it still names this node; the new one is set |
| Unmount | The cell is cleared if it still names this node. Children unmount first, so in a parent component's cleanup `Current` is already `null` (as in React) |
| Ref moved to another element in one render | The owner check makes the order irrelevant: whichever sibling the engine visits first, `Current` ends as the new element's view. (WinUI's update path doesn't have this; §2.1) |
| Remount in place (`CanUpdate` false: another element type at the position, a hybrid switching renderer) | A child is mounted anew before the old one unmounts; a root's old tree unmounts first. Either way the owner check leaves the new binding |
| Keyed move | The node and native are kept, so `Current` doesn't change |
| One ref on several mounted elements | Unsupported: the last one bound wins, and unmounting it clears the ref even though others still declare it. Documented, not detected (a check at bind time can't tell this from a move in progress) |
| A ref inside an item template (CollectionView, CarouselView) | Each cell is a child root. A ref created in the item's component belongs to that cell's tree: cells key their tree by item (spec 068), so a recycled cell remounts and its component gets a fresh ref. With `ReuseItemTrees`, the component and its ref survive recycling and keep naming the same cell view, which now shows another item. A ref created outside the list and attached inside the template is the "several elements" case above |
| Error boundary fallback, window or root teardown | Unmount, so cleared |
| Hot reload | Hook state survives, so the ref keeps its identity; the re-render rebinds the same native, or a new one if the edit changed the element type |
| Ref on a component | Doesn't compile: `.Ref` is on `ViewElement`. Pass the ref as a prop to the component, which attaches it to the view it means (React's forwarded-ref pattern). WinUI accepts `.Ref` on any `Element` |
| Ref on a page, Shell item, toolbar item or dialog | Not in 4c-1: these aren't `ViewElement`s, and their imperative APIs (dialogs, navigation, lifecycle) are already declarative in Reactor. The engine binding is element-agnostic, so adding `.Ref` to `PageElement` later is a modifier, not a design change |

- **No shallow-skip problem.** MAUI's engine skips only when the element instance is the same, and a
  different ref makes a different element record. A component that skips keeps its old elements and
  so its old, still correct, binding.
- **Threading.** The root renders on the UI thread, so `Current` is written there. Read it and call
  the view on the UI thread; from a background continuation, use `MainThread.BeginInvokeOnMainThread`.
  The ref does no marshaling (WinUI's doesn't either).
- **When the view can act.** `Current` is set at mount, which can be before MAUI has created the
  view's handler or the platform view is in a window (a page about to be pushed). A command then
  returns `false` or does nothing, as it would in plain MAUI. Call commands from events, or from an
  effect once the view is shown; open question 1 covers focusing on mount.
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
  renders flips the choice and remounts, as any hybrid switch does; refs from the hook never change.
- **Direct views can be focused.** `DirectView.Focus()` returns `false` today. It becomes MAUI's own
  path: create a `FocusRequest`, invoke the handler's `Focus` command, return the request's result;
  `Unfocus()` invokes the `Unfocus` command. So `UseElementRef<IView>()` plus `Current?.Focus()`
  works on both registries without a fallback.
- **A type mismatch is an error in every build.** Binding `ElementRef<Entry>` to an element whose
  native isn't an `Entry` (a `Label`, or a third-party handler's view) throws an
  `InvalidOperationException` that names both types, which goes to the error boundaries like a render
  error. WinUI only asserts in DEBUG builds of Reactor, which an app consuming the Release package
  never sees; a silently `null` ref is the bug the check exists for. The check is one
  `IsInstanceOfType` per bind.

### 2.6 Imperative commands: MAUI's own API through the ref

Reactor adds no wrappers; the ref gives the Controls (or Core) view, and the app calls MAUI:

| Need | Call |
|---|---|
| Focus, unfocus | `Current?.Focus()` (returns whether it worked), `Current?.Unfocus()` |
| Scroll | `ScrollView.ScrollToAsync(x, y, animated)`, `ScrollToAsync(element, position, animated)`; `ScrollX`, `ScrollY` |
| WebView | `Reload()`, `GoBack()`, `GoForward()`, `CanGoBack`, `CanGoForward`, `EvaluateJavaScriptAsync(script)` |

- **Scrolling to an element** takes a Controls `Element`, so the target's ref must be a Controls type
  (`ElementRef<Label>`), which §2.5 makes it on the direct registry. MAUI computes its position by
  walking the target's parents; a Controls view inside a direct stack has the stack's Controls host
  as its parent, so the device fixture checks the offset is right in that layout.
- **WebView `Url` and `Html` aren't controlled values.** Reactor loads the source when the declared
  value changes; a link the user follows, or `GoBack`, isn't undone. `CanGoBack` is read after
  `OnNavigated`, typically into state for a Back button's `IsEnabled`.
- **Not mirrored: `FocusManager.Focus(ref, state)` and `UseElementFocus()`.** WinUI's take a
  `FocusState`; on MAUI `Current?.Focus()` is the whole call. `UseElementFocus`'s value is deferring
  the focus; whether MAUI needs that is open question 1.
- **`.Set<TView>(...)` stays the way to configure a view on every render.** A ref is for calls made
  outside render (handlers, effects, async work). Capturing the view from `.Set` into a field instead
  of a ref is stale after a remount and forces the Controls view.

### 2.7 WebView `OnNavigating`

```csharp
public Action<WebNavigatingEventArgs>? OnNavigating { get; init; }
```

- **MAUI's own arguments**: the URL, the navigation kind (`NewPage`, `Back`, `Forward`, `Refresh`)
  and the source; setting `e.Cancel = true` cancels. The name follows the element's existing MAUI
  naming (`OnNavigated`); WinUI's `OnNavigationStarting` has no cancel and is a different element.
- **Synchronous.** MAUI raises `Navigating` inside the platform's decision callback and reads `Cancel`
  when the handler returns, so the callback must decide before it returns; setting `Cancel` after an
  `await` does nothing (documented).
- **Every navigation is reported**, including the one a changed `Url` starts. Cancelling that one
  leaves the previous page; the declared `Url` isn't loaded again until it changes.
- **After unmount** the callback isn't called (`context.Live`), and the navigation is allowed.
- **A throwing callback cancels the navigation**, and the exception goes to the error boundaries
  (`HandlerContext.ReportError`), as a throwing navigation guard does (spec 069 §2.6). It must not
  escape into the platform's callback, which on iOS would crash the app.

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
- **A value is controlled**, per the echo contract (spec 067 §14):
  - Reactor writes text first, then cursor, then length, each compared with the view, not the old
    element, and inside the echo scope. A selection change Reactor's own text write causes (iOS puts
    the caret at the end) is inside that scope and isn't reported.
  - A position past the text is clamped by the platform; it isn't reported, and re-rendering the
    same value doesn't loop.
  - A user's selection change is reported once and then restored if the app doesn't take it, so an
    app that controls the cursor must update it from `OnSelectionChanged` (typing moves the caret).
  - Nothing is reported after unmount.
- **One report per user change, after the text.** MAUI's handlers write `CursorPosition` and then
  `SelectionLength` as two property changes, and iOS writes the cursor before the text. So a change
  outside the echo scope is noted, and one report is posted to the root's scheduler; it reads both
  values then, drops itself if the element has unmounted or the values equal the declared ones, and
  is followed by the restore. `OnTextChanged` therefore comes first, and there is no report with the
  new cursor and the old length.
- **Both renderers.** `DirectEntry` implements `IEntry.CursorPosition` and `SelectionLength` as plain
  properties that the platform handler writes and Reactor ignores. Their setters raise the same
  notification as `Text`'s, and Reactor writes through a `SetSelection` that remaps both properties,
  as `SetText` does.
- **Rejected: WinUI's write-when-the-declaration-changes.** Writing only when the declared value
  differs from the previous element means setting the same position twice does nothing, and a
  refused move isn't restored, so it isn't controlled. It is the WinUI divergence decision 4 lists.

### 2.9 Tests

Each oracle names the mutation that must turn it red; the implementation PR runs each mutation once
and records it in §2.13.

**Headless (`tests/Reactor.Maui.Tests`), both registries where the element is hybrid:**

| Test | Oracle | Mutation |
|---|---|---|
| Ref set at mount, cleared at unmount | `Current` is the host's view (same instance), then `null` after a conditional removal | skip the bind; skip the unbind |
| Set before effects | a mount effect reads a non-null `Current` | bind after the pass's effects |
| Ref moved between siblings, both orders | `Current` is the new element's view | drop the owner check |
| Remount in place (element type change; hybrid switch through `.OnTap`) | `Current` is the new view | drop the owner check |
| Ref removed, and swapped for another ref, on a mounted element | old ref `null`, new ref the view | skip the clear |
| Keyed reorder | `Current` is the same instance | none needed (the reorder is the existing keyed tests') |
| Typed ref on the direct registry | `ElementRef<Entry>` → a Controls `Entry`; `ElementRef<IView>` → a `DirectEntry` | drop the hybrid condition |
| Type mismatch | the boundary receives an error naming both types | skip the check (`Current` silently `null`) |
| Error boundary fallback, root unmount, hot reload | cleared, cleared, same ref and view | — |
| Hook stability | same instance across renders | — |
| `DirectView.Focus` | invokes the handler's `Focus` command and returns its result (stub handler) | return `false` |
| WebView `OnNavigating` | `IWebView.Navigating(...)` (what the platform handler calls) returns cancel as decided; not called and allowed after unmount; a throw cancels and reaches the boundary | ignore `Cancel`; invoke through `Current` instead of `Live`; let the exception escape |
| WebView `Url`, `Html`, `OnNavigated` | source set, replaced, cleared; reported | — |
| `Entry` selection, both registries | written at mount and update, never when `null`; Reactor's text and selection writes not reported; a handler-style move (`ITextInput.CursorPosition` then `SelectionLength`) reported once with both final values after the scheduler runs; refused → restored, the restore not reported; nothing after unmount | write without the echo scope; report per property; compare with the old element |
| `Entry` return type, both registries | set, updated and cleared to `Default`; `OnCompleted` reported | — |
| `ScrollView` | orientation and scroll bar visibility set, updated, cleared; `OnScrolled` reported from `SetScrolledPosition` | — |
| `RefreshView` | mount with `IsRefreshing` true; toggling not reported; `RefreshColor` set and cleared; a handler-style refresh (`IRefreshView.IsRefreshing = true`) reported once, kept when the app accepts, restored and not re-reported when it refuses; nothing after unmount; content set, replaced, cleared (a direct view hosted in the wrapper on the direct registry) | write outside the echo scope; skip the restore |

**Engine (`tests/Reactor.Maui.Engine.Tests`):** the ordering rows above (move, remount in place,
keyed move, boundary) on the fake renderer, with a fake element that implements the ref interface,
so they hold for any handler.

**Device (`tests/Reactor.Maui.DeviceTests`), Mac Catalyst, iOS and Android, `[controls]` and
`[direct]` unless noted:**

| Fixture | Oracle |
|---|---|
| `Ref_FocusFromAnEvent_FocusesThePlatformView` | `ElementRef<Entry>` and, on direct, `ElementRef<IView>`: after a tap, the platform view is first responder (iOS, Catalyst) or `IsFocused` (Android), and `Focus()` returned `true` |
| `Ref_FocusFromAMountEffect` | records whether focusing in the first effect of a newly shown page works on each platform; it passes either way and its result decides open question 1 |
| `ScrollView_ScrollToFromARef_ReachesThePlatform` | after `ScrollToAsync(0, 300, false)` the platform offset (`ContentOffset.Y`, or `ScrollY` in device-independent units) is 300 ± 1 and `OnScrolled` reported it; horizontal orientation likewise on x |
| `ScrollView_ScrollToAnElement_InADirectStack [direct]` | the offset equals the target's platform frame top ± 1 |
| `ScrollView_ScrollBars_ReachThePlatform` | the platform's indicator flags follow the declared visibility |
| `RefreshView_UserRefresh_IsReported_AndRefusalStopsTheSpinner` | a refresh triggered through the platform control (`UIRefreshControl` value-changed action; the `SwipeRefreshLayout` listener) is reported once; the platform spinner state follows the declaration; refused, it stops and nothing more is reported. If Catalyst has no pull-to-refresh, the fixture records that and the inventory says so |
| `WebView_NavigatingCancel_KeepsThePage` | inline HTML, no network: a navigation to a blocked URL (started by `EvaluateJavaScriptAsync("location.href=…")`) is reported with that URL, cancelled, never reported by `OnNavigated`, and `document.title` is still the first page's |
| `WebView_CommandsThroughARef` | after two in-page navigations, `CanGoBack`, `GoBack` and `GoForward` move between them; `Reload` re-runs the page's script (a `sessionStorage` counter reads 2); `EvaluateJavaScriptAsync` returns a value |
| `Entry_Selection_ReachesThePlatform_AndUserMovesAreReported` | the platform selection (`SelectedTextRange`, `SelectionStart`/`End`) equals the declaration after mount and update; a selection set on the platform view (what a user's tap does) is reported once with final values; refused, the platform selection returns; typing reports text, then selection |
| `Entry_ReturnType_ReachesThePlatform` | `ReturnKeyType` (iOS, Catalyst) and `ImeOptions` (Android) follow the declaration |
| `CollectionView_RefsInItemComponents_FollowTheirCells [controls]` | after scrolling far enough to recycle, every live item component's ref names a cell showing its item, and the refs of unmounted ones are `null` |

### 2.10 Gate for 4c-1

- **WinUI** isn't changed: `skills/reactor.api.txt` is byte-identical, and its tiers stay green.
- **MAUI**: headless, engine and device suites green on Mac Catalyst, iOS and Android; each §2.9
  mutation turns its oracle red; the MAUI benchmark within noise.
- **Docs**: `src/Reactor.Maui/README.md` documents refs, the commands, `OnNavigating` and the
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
   declaration changes and never restores (§2.8). Same shape (`int?` properties and the event), a
   stricter meaning, chosen because the phase's bar is the echo contract.
5. **Native commands, no wrappers** (§2.6), and no `FocusManager` or `UseElementFocus` mirror
   unless open question 1 needs one.
6. **`OnNavigating` takes MAUI's `WebNavigatingEventArgs`** (§2.7). Rejected: `Func<string, bool>`
   (which way `true` means is unclear, and the navigation kind is lost) and a Reactor context type
   (a second type for the same thing).
7. **Binding in the engine, with an owner check** (§2.4), rather than in each handler.

### 2.12 Open questions

1. **Focus on mount.** If focusing from the first effect of a newly shown page fails on a platform
   (the view isn't in a window yet), should MAUI get WinUI's `UseElementFocus()` with the same name
   and shape, whose `RequestFocus` waits for the view's `Loaded` and then focuses? The device
   fixture's measurement decides; the default is no new API.
2. **A WebView URL mirrored into state.** An app that stores `OnNavigated`'s URL and renders it back
   as `Url` makes Reactor load the page it is already showing. Should the handler skip a write equal
   to the last URL the view reported (with what URL normalization), or is this documented as "Url is
   the source to load, not the current page"? The default is the documentation.
3. **One ref on several elements** (§2.4): document it (the default), or detect it at the end of a
   pass in DEBUG?
4. **Should `OnNavigating` see the navigation Reactor's own `Url` change starts?** The default
   reports it, so one callback sees every navigation.
5. **Label spans** are placed in 4c-2 as text items; confirm, or move them to 4c-3.

### 2.13 As built (4c-1)

To be written when 4c-1 lands.

## 3. 4c-2: collections and items

To be written when 4c-2 starts.

## 4. 4c-3: shapes, brushes, ContentView, image equality, tests and the audit

To be written when 4c-3 starts.
