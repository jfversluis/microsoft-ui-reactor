# Spec 068: MAUI feature inventory (phase 4)

Phase 4's gate needs every .NET MAUI 11 control and feature to be either implemented in Reactor's
MAUI renderer, with headless and device fixtures, or listed as an exclusion with a reason. This spec
is that inventory, the order the work lands in, and the bar each item must meet. Status is as of
phase 4a (stacked on phase 3e).

## 1. The bar for "implemented"

An item is implemented when:

- **The element.** It has an element record, a factory and modifiers where they apply. Its handler is
  a `ControlDescriptor` unless it needs a hand-written handler (spec 067 §16).
- **Controlled values.** Any controlled value follows the echo contract (spec 067 §14): Reactor's
  writes aren't reported, refused input is restored, and nothing is reported after unmount.
- **Headless tests** cover its properties being set and cleared, its events, and in-place updates.
- **Device fixtures** cover what headless tests can't: rendering, geometry, real input and platform
  quirks. They run on Mac Catalyst, iOS and Android, and on Windows once the Dev Box runner exists.
- **Both renderers.** It works on both registries, or the direct registry falls back to the Controls
  view.
- **WinUI's name.** Where WinUI Reactor already has the feature, the MAUI API uses its name and
  shape. A MAUI-only name needs a reason in this spec. The Notes column names the WinUI counterpart
  for every planned item that has one.

## 2. Inventory

Legend: ✅ implemented (meets §1) · 🧪 the code exists, tests are missing · 🟡 partial (gap noted) ·
⬜ planned (sub-phase) · ⛔ proposed exclusion, with the Reactor way to do it where there is one.

Test status comes from a census of the three MAUI test projects by element, factory and modifier
name, plus reading the descriptor tests. A ✅ therefore means tests exist, not that every property is
set, cleared and updated; 4c audits each ✅ row against §1 before the phase 4 gate.

"Ports" counts how many of the seven apps phases 6–8 port need a feature (Calculator, WordPuzzle,
DeveloperBalance, WeatherTwentyOne, PointOfSale, BaristaNotes, eShop ClientApp), from a census of
their sources. It sets the order in §4.

### Controls

| Item | Status | Notes |
|---|---|---|
| Editor | ✅ | descriptor (3e) |
| BoxView, CheckBox, SearchBar, Switch | 🧪 | descriptors (3e). Mount and echo are tested; updating and clearing their other properties (colors, placeholder, corner radius) is not |
| Button, Slider, Stepper | ✅ | |
| Entry | ✅ | spec 070 §2.8 (4c-1): `CursorPosition` and `SelectionLength` with WinUI's semantics (written when the declaration changes, never restored) and WinUI's `OnSelectionChanged(selectedText, cursor, length)`, reported once per change from the platform's selection, on both registries; return type set, updated and cleared. Selection isn't controlled: a gate decision for WinUI parity (no port needs a controlled caret). Evidence: headless `EntrySelectionTests`, device `Entry_Selection_*` (with the per-platform characterization) and `Entry_ReturnType_*` |
| RadioButton | ✅ | spec 070 §3.6 (4c-2): WinUI's `ContentElement` with MAUI's default template or a content-only one (`IsIndicatorVisible = false`), the checked look rendered from state; the text as accessible name, else a one-time warning asking for `.Semantics(description: …)`. Groups are Reactor's on both registries (MAUI's search misses peers in direct layouts): named per page, unnamed per layout; a tap settles the native group before any callback and reports the peers' unchecks first; checks from inside a callback run on the next turn (MAUI delays nested writes); conflicting declarations shown as declared, with a warning. Evidence: headless `RadioContentTests`, `RadioGroupTests`, `RadioTransactionTests`, `RadioConflictTests`; device `Gate_RadioButton_*`, `RadioButton_*` |
| ActivityIndicator, ProgressBar | 🧪 | descriptors; headless tests only, no device fixture |
| DatePicker, TimePicker | 🧪 | device fixtures only, no headless tests |
| Label | ✅ | spec 070 §3.9 (4c-2): `Label(Run(…), Hyperlink(…))`, WinUI's inline types with MAUI's property names (font, colors, decorations, line height, transform), spans updated in place, hyperlinks underlined and invoking only while current; the direct registry falls back to the Controls label. Evidence: headless `LabelInlineTests`, device `Label_Spans_Platform` (platform attributes and a hit-tested tap, with negative controls) |
| Picker | ✅ | spec 070 §3.8 (4c-2): `Picker(items, display, index, …)` computes a text per item; the selection stays an index. Evidence: headless `PickerDisplayTests`, device `Picker_ItemDisplay` |
| Image, ImageButton | ✅ | spec 070 §4.6 (4c-3a): font, file and URI sources (with the URI's caching settings) compared by value, `StreamImage(key, open)` by key, other streams by reference, in every consumer (images, image buttons, button images, page, toolbar, swipe and back-button icons, the direct image); every property on both registries in the census matrix. Evidence: headless `PropertyMatrixTests` (`ImageElement.*`, `ImageButtonElement.*`), `VisualsTests` (`ImageSources_FollowTheRules`, `AnEqualImageSource_IsNeverWritten_ByAnyConsumer`, `AnEqualIcon_*`); device `Image_EqualSourceDoesNotReload` (decoded images, open counts) and `Characterize_StreamImage_LoadCount` |
| WebView | ✅ | spec 070 §2.7 (4c-1): `OnNavigating` (cancel; not exactly once on Android), `OnNavigated` with MAUI's `WebNavigatedEventArgs` and its result, posted so `CanGoBack` is current; `Reload`, `GoBack`, `GoForward`, `EvaluateJavaScriptAsync` through a ref; `Url` is the requested source, loaded when it changes. Evidence: headless `WebViewTests`, device `WebView_*` with bundled pages on all three platforms |
| Border | ✅ | spec 070 §4.4 (4c-3a): `Stroke` is a brush (compared by value; `.Stroke(color)` and `.Stroke(brush)`, `null` clears), thickness, padding, corner radius, content set, updated in place, replaced and cleared, a direct child hosted. Evidence: headless `PropertyMatrixTests` (`BorderElement.*`), `VisualsTests.Border_Content_IsSetReplacedAndCleared`; device `Border_Geometry` (content inset by padding plus stroke, rounded corners), `Characterize_Border_ContentInset` |
| GraphicsView | 🧪 | 4c-3b (spec 070 §4.7) |
| CollectionView | 🟡 | spec 070 §3.3–3.4 (4c-2): layouts, spacing, sizing, header, footer, empty view and threshold tested on devices; `ItemState` in templates (the declared selection, with the platform highlight off) replaces the `Selected` visual state; grouping with list-wide keys; `ScrollToKey`; selection reports from the event, key-set comparison, and a native selection verification on iOS and Mac Catalyst behind MAUI's deferred batch updates. Evidence: headless `ItemStateTests`, `CollectionSelectionTests`, `NativeSelectionVerificationTests`, `CollectionViewFeatureTests`; device `Gate_CollectionView_*`, `CollectionView_*`. Gaps no port needs: reordering (`CanReorderItems`, `ReorderCompleted`), `Scrolled`, `ItemsUpdatingScrollMode` |
| CarouselView | 🧪 | position, `Loop`, `PeekAreaInsets` and `ItemSpacing` exist, all untested; its position echo isn't characterized (spec 067 §14). Moved to 4c-4 (spec 070 §5): its design review found the position echo needs more than 4c-2's scope (never write or report during a drag, no provenance guessing) |
| SwipeView | 🟡 | spec 070 §3.5 (4c-2): WinUI's `SwipeItemData` (was `SwipeAction`) on four sides with their modes, content items, `Threshold`, the swipe events, items rebuilt only when their structure changes and invoking only while current; opening and closing through a ref with MAUI's `Open`/`Close`. Evidence: headless `SwipeViewTests`; device `SwipeView_*` (open, close, Reveal and Execute around the threshold). Gap no port needs: `SwipeBehaviorOnInvoked` (per side in MAUI, per item in WinUI) |
| ScrollView, RefreshView | ✅ | spec 070 §2.6 (4c-1): scrolling from code through a ref, and `ScrollToAsync(ElementRef<T>, …)` to an element in Controls or direct layouts (MAUI's own position math, a confirmed-completion task); orientation and scroll bars; RefreshView's user refresh reported once and a refusal restored. Evidence: headless `ScrollBridgeTests`, `ScrollAndRefreshTests`, device `ScrollView_*` (equivalent trees with UIKit insets) and `RefreshView_*` (a real pull on Android, the refresh control on iOS and Mac Catalyst) |
| IndicatorView | ⬜ 4c-4 | paired with CarouselView; no port. `IndicatorTemplate`: proposed exclusion (render a row of elements from the count and position) |
| ContentView | ✅ | spec 070 §4.5 (4c-3a): `ContentView(child)` with padding, the child updated in place, replaced and cleared; the Controls view on both registries, a direct child hosted. Evidence: headless `PropertyMatrixTests` (`ContentViewElement.Padding`), `VisualsTests.ContentView_Content_IsSetReplacedAndCleared`; device `ContentView_Geometry` |
| Shapes (Ellipse, Line, Path, Polygon, Polyline, Rectangle, RoundRectangle) | ✅ | spec 070 §4.3 (4c-3a): WinUI's `Rectangle`, `Ellipse`, `Line`, `Path2D` and MAUI's `RoundRectangle`, `Polygon`, `Polyline`, with fill, stroke, thickness, dashes, caps, joins, miter limit and `Aspect`; point and dash lists copied and compared by sequence; path markup converted when it changes, falling back to `Data`; `RenderTransform` cleared through an identity transform (MAUI's path handlers keep the old one otherwise). Controls shapes on both registries (direct shapes rejected: no port needs them). Evidence: headless `PropertyMatrixTests` (every shape property), `VisualsTests` (collections, path data, transform clear, null colors), `PathDataWarningTests`; device `Shapes_Render`, `Shapes_Aspect`, `Shapes_UpdateInPlace`, `Characterize_Path_RenderTransformClear` |
| Brushes (linear and radial gradients) as backgrounds and strokes | ✅ | spec 070 §4.4 (4c-3a): `LinearGradientBrush(…)` and `RadialGradientBrush(…)` factories (named after their type, as WinUI's `AcrylicBrush`; WinUI has no gradient factory); solid and gradient brushes compared exactly by value in backgrounds, border strokes and shape fills and strokes, immutable once rendered; `Brush.Default` (a `null` color) clears. A brush background renders a direct view as a Controls view (direct styling is 4d). Evidence: headless `VisualsTests` (`BrushComparer_FollowsTheRules`, `CumulativeSmallChanges_*`, `Background_ComparesBrushesByValue`, `ABrush_IsImmutableOnceRendered_*`, `GradientFactories_*`); device `Gradients_Render` |
| HybridWebView | ⬜ 4f | interop |
| TitleBar (Windows) | ⬜ 4g | needs the Windows runner |
| Map (`Microsoft.Maui.Controls.Maps`) | ⬜ 4f | through `Native<TView>` and the wrapper generator, not in the core package (eShop) |
| Frame | ⛔ | obsolete in MAUI; use Border. No port uses it |
| ListView, TableView, ViewCell, TextCell and the other cells | ⛔ | legacy; CollectionView replaces them. No port uses them |
| BlazorWebView | ⛔ | a different UI model; host it through `Native<TView>`. PointOfSale's signature page (`RootComponents` plus DI services) is a 4f acceptance case for `Native<TView>` |
| StackLayout (the legacy, non-specialized one) | ⛔ | VStack and HStack replace it. The ports that use it use no `*AndExpand` options, so the replacement behaves the same |

### Layouts, pages and navigation

| Item | Status | Notes |
|---|---|---|
| VerticalStackLayout, HorizontalStackLayout, Grid | ✅ | the direct renderer has its own stack and grid |
| FlexLayout, AbsoluteLayout | 🧪 | |
| ContentPage, NavigationPage (declarative `NavigationStack`, with modals), TabbedPage (`Tabs`) | ✅ | |
| Typed navigation (`UseNavigation<TRoute>`, `NavigationHandle<TRoute>`, `UseNavigationLifecycle`, `NavigationHost`) | ✅ | spec 069 §2; WinUI's API over the shared model. Headless `NavigationHostTests`, core `NavigationModelTests`, device `NavigationHost_*` fixtures on all three platforms, and the `Reactor.Navigation.AotSmoke` AOT proof |
| FlyoutPage (`Flyout`) | 🧪 | headless tests only, no device fixture; **bug (4c-3b characterization)**: a split flyout (`Split`, `SplitOnPortrait` on Mac Catalyst and iPad) reports MAUI's forced `true` and the restore's `false` throws, so the page never loads (spec 070 §4.14) |
| ToolbarItems | 🧪 | primary and secondary order; headless tests only; **bug (4c-3b characterization)**: an item the platform retains after a rebuild runs the action now at its index (spec 070 §4.14) |
| Shell | ✅ | spec 069 §4 (4b-2): `Shell`, `TabBar`, `FlyoutItem`, `ShellMenuItem`, `ShellTab` (a page or a `NavigationHost`, whose routes are the tab's stack) and top-tab `ShellContent`, keyed by the app; the selection held in state (a user's switch reported from `Navigated`, a refused one undone, an unknown key falling back to the first tab, reported once); the back button, swipe and Android back key preflighted in `Shell.Navigating`, so a refused back moves no page; flyout state, header, footer and content; appearance colors; `NavBarIsVisible`, `TabBarIsVisible`, `TitleView`, `SearchHandler` (an object), `PresentationMode` and `BackButtonBehavior` per page. Routes are typed: an absolute route (`//route`) is a selection plus the tab handle's `Reset`; a relative back with a result (`..?refresh=true`) is the result kept in app state or context that the destination reads, then `GoBack` (putting it in the route takes `PopTo` and `Replace`, and remounts the page); deep links resolve with `DeepLinkMap`. A route page can't be presented modally in a tab (the host's `Modals` are). Evidence: headless `ShellTests`, device `Shell_*` fixtures on all three platforms. Not in 4b-2: a declarative search element, top tabs with their own stacks, Windows (4g) |
| Page dialogs: alert, action sheet, prompt | ✅ | spec 069 §5.1 (4b-3): `UseDialogs()` (a stable `Dialogs` with `Alert`, `ActionSheet`, `Prompt`, awaitable from handlers and effects, never faulting: a dialog that can't be answered completes with the cancel answer) and declarative `Alert`, `ActionSheet` and `Prompt` elements (`IsOpen`, `OnClosed`, which gets the answer for the content shown). One native dialog at a time per window, on its top modal or else its page, after the previous one's native UI has gone and while no navigation runs; a destroyed window settles its dialogs. Evidence: headless `DialogTests`, device `Dialogs_*` fixtures answered through the native buttons on all three platforms |
| Toast and Snackbar (CommunityToolkit) | ⬜ 4f | 2 ports; part of the CommunityToolkit validation |
| Android back button outside Shell (`OnBackButtonPressed`) | ✅ | spec 069 §5.2 (4b-3): `UseSystemBackButton(Func<bool>)` and `UseSystemBackButton(nav)`, asked on Android through an AndroidX back callback enabled only while a handler is registered (only the visible page's handlers, deepest first), before a `NavigationHost`'s or Shell's guarded back; iOS and Mac Catalyst have no back key, Windows in 4g. At the root an unhandled back gets Android's default; the predictive back-to-home preview is lost only while a handler is registered (spec 065 §7). Evidence: headless `SystemBackTests`, device `SystemBack_*` fixtures on Android |
| Modal presentation style | ✅ | spec 069 §5.3 (4b-3): `PageElement.ModalPresentationStyle` (`PageSheet`, `FormSheet`, …), set before presentation for `NavigationStack`, `NavigationHost` and Shell modals. iOS only: not applied on Mac Catalyst, where MAUI can't dismiss a sheet (spec 069 §5.6). Evidence: headless `WindowLifecycleTests`, device `ModalPresentationStyle_PageSheet_OnIOS` |
| App and window lifecycle (`Activated`, `Deactivated`, `Stopped`, `Resumed`, `Destroying`) | ✅ | spec 069 §5.4 (4b-3): `UseWindowLifecycle(onActivated, onDeactivated, onStopped, onResumed, onDestroying)`, refreshed every render and unsubscribed at unmount; a hosted `ReactorPage` finds its window when loaded; a `ReactorWindow` runs `onDestroying` before it unmounts its tree. Evidence: headless `WindowLifecycleTests`, device `WindowLifecycle_*` on Android (iOS and Mac Catalyst can't background themselves in process) |
| Window properties (title, size, minimum and maximum size, position) | ⬜ 4g | desktop |
| Multi-window (opening a second `ReactorWindow`) | ⬜ 4g | |

### Look and theme

| Item | Status | Notes |
|---|---|---|
| Light and dark theme (`UseAppTheme`) | ✅ | |
| An `AppThemeBinding` equivalent (a value per theme without re-rendering) | ⬜ 4d | every port uses `AppThemeBinding`. A value, not a hook (a hook re-renders, and `UseAppTheme` already covers that case). WinUI passes a `ThemeRef` to modifier overloads such as `.Background(ThemeRef)`, applied by the reconciler without re-rendering; MAUI follows that shape |
| Styling direct views (they ignore implicit styles) | ⬜ 4d | spec 065 lists it as the main gap of the direct renderer; every port styles heavily |
| `ResourceDictionary`, `StaticResource`, `DynamicResource` | ⛔ | C# values and the theme value (4d) replace them |
| Fonts (registered fonts, `FontFamily`, font size and attributes) | 🧪 | `FontFamily` is untested |
| Opacity, Shadow, Clip, Rotation, Scale, Translation, ZIndex | 🧪 | modifiers. Opacity has a headless test, the rest none; they need device geometry checks |
| Flow direction (right-to-left) | ⬜ 4d | no modifier; the direct view hard-codes `MatchParent` |
| Animations | ⬜ 4d | 5 of 7 ports animate, mostly with explicit `FadeTo`, `TranslateTo`, `RotateTo` and `ScaleTo`. WinUI's names and meanings (spec 014): implicit `.Animate(curve)`, enter and exit `.Transition(...)`, explicit `WithAnimation(curve, ...)`. The `Curve` and `Transition` types move to the shared core if they're framework-neutral |
| VisualStateManager | ⛔ | state drives the element; Controls views keep their own visual states. The ports' states map to: `Checked` and `Selected` → props from state (a radio button's content and `ItemState` in item templates, spec 070 §3.3 and §3.6); `PointerOver` → pointer events (4e); `AcceptDrop` → drag and drop (4e); `AdaptiveTrigger` → `UseWindowSize` |
| Triggers (`Trigger`, `DataTrigger`, `EventTrigger`) and trigger actions | ⛔ | a `DataTrigger` is a conditional prop; an `EventTrigger` on `Appearing` is `OnAppearing` plus an animation (4d); a style trigger on a Shell item's `IsChecked` is that item's selected state (4b) |
| `ControlTemplate`, `ContentPresenter` | ⛔ | components are the templates |
| Data binding (`BindingContext`, compiled bindings) | ⛔ | state and props replace it |
| `RadioButtonGroup` attached properties (`GroupName`, `SelectedValue` on a layout) | ⛔ | Reactor groups radio buttons itself (spec 070 §3.6): `GroupName` on each button, the selection in state |
| `DataTemplateSelector`, `BindableLayout` | ⛔ | an item-render function chooses per item; `ForEach` renders a list into any layout |
| Effects | ⛔ | legacy in MAUI (handlers replace them); `.Set` and descriptors reach the native view |

### Interaction

| Item | Status | Notes |
|---|---|---|
| Tap | ✅ | `OnTap` |
| Swipe, pan | 🧪 | modifiers, untested |
| Focus and keyboard (`Completed`, return type, keyboard type, focusing from code) | ✅ | spec 070 §2.6 (4c-1): `ref.Current?.Focus()` on both registries (direct views focus through their handler), and WinUI's `UseElementFocus()`, because Android can't focus from a new tree's first effect. Evidence: headless `ElementRefTests`, `ElementFocusTests`, device `Ref_Focus*` |
| Keyboard navigation (`TabIndex`, `IsTabStop`) | ⬜ 4e | |
| Pinch, pointer (hover, press, move) | ⬜ 4e | WinUI: `OnPinch`, `OnPointerPressed/Moved/Entered/Exited` |
| Drag and drop | ⬜ 4e | WinUI: `OnDragStart`, `OnDragEnter`, `OnDragOver`, `OnDrop` |
| ContextFlyout (context menus), MenuBar (desktop) | ⬜ 4e | WinUI: `WithContextFlyout`, `MenuBar` |
| ToolTip | ⬜ 4e | WinUI: `.ToolTip(...)` |
| Keyboard accelerators | ⬜ 4e | desktop. WinUI: `Accelerator(...)` |

### Platform and app

| Item | Status | Notes |
|---|---|---|
| Accessibility (semantic description, hint, heading level; AutomationId) | 🧪 | modifiers; AutomationId is exercised by every device fixture, the semantic properties by none. Checked through UIA and Appium in phase 5 |
| `AutomationProperties` (`IsInAccessibleTree`, `ExcludedWithChildren`) | ⬜ 4d | |
| Window size and theme hooks | ✅ | `UseWindowSize`, `UseAppTheme` |
| Element refs | ✅ | spec 070 §2.2–2.5 (4c-1): WinUI's `UseElementRef<T>()`, `.Ref(...)` and `TypedElementRef.Create<T>()` in `Microsoft.UI.Reactor.Input`, with `T : IView` (a Controls-typed ref makes that element a Controls view); nothing moved to the core. Bound by the engine and released on every unmount and failure path; list cells release theirs when retired. Evidence: headless `ElementRefTests`, engine `RefBindingTests`, device `CollectionView_RefsInItemComponents_*` and the focus, scroll and WebView fixtures |
| Behaviors (attached once per native view) | ⬜ 4f | the ports use CommunityToolkit's `IconTintColorBehavior`, `StatusBarBehavior`, `TextValidationBehavior` and `EventToCommandBehavior`. `.Set` runs after every update, so it would add a behavior each render; this needs a mount-only hook or a `.Behavior(...)` modifier |
| Dependency injection (`UseService<T>`) | ⬜ 4f | reads the app's `IServiceProvider` (MAUI's `MauiContext.Services`). It complements `Context<T>` / `UseContext`, Reactor's tree-scoped injection, rather than replacing it. 4f decides whether WinUI gets the same hook, so shared components can call one API |
| Reactor inside XAML (`ReactorView`) and XAML inside Reactor (`Native<TView>`) | ⬜ 4f | `ReactorPage` exists for page-level hosting. WinUI's host is `ReactorHostControl`; `ReactorView` follows MAUI's `*View` naming for the same role. WinUI has no general native-view element (only `InlineUI` inside RichTextBlock), so 4f decides whether `Native<T>` is a shared-core shape that WinUI also gets |
| Third-party controls: a MAUI wrapper generator (spec 058 equivalent) emitting `ControlDescriptor`s | ⬜ 4f | validated on what the ports use: Syncfusion.Maui.Toolkit (DeveloperBalance's chart, segmented control, text input layout and shimmer), SkiaSharp.Views.Maui (2 ports), CommunityToolkit.Maui (its behaviors, Toast and Snackbar) and Maps (eShop) |
| Essentials (device, sensors, preferences, …) | ⛔ | not UI; apps call them directly |
| Hot reload (`dotnet watch`, VS) | 🟡 | re-renders every live root. Verified headlessly only (spec 065 §7); real edits from an IDE on devices in 4g |
| Analyzers (hook rules) on MAUI code | ⬜ 4g | |
| Windows (WinUI-backed MAUI) | ⬜ 4g | Dev Box runner. Includes the path transform reset's `WINDOWS` branch (`W2DGraphicsView { Drawable: ShapeDrawable }` → `UpdateRenderTransform(null)`, `Invalidate()`), since MAUI's Windows `PathHandler.MapRenderTransform` also ignores a `null` transform, verified with `Shapes_UpdateInPlace` (spec 070 §4.13) |

## 3. Open issues carried into phase 4

- **Android: replacing `Window.Page` while a `NavigationPage` navigates crashes MAUI.** Spec 065 §7
  records this. The push variant ("NavigationManager cannot be null", from
  `NavigationViewFragment.OnCreateView`) reproduced with plain MAUI, without Reactor. Phase 3e hit a
  second message from the same frame ("CurrentPage cannot be null") during a pop. That variant hasn't
  been reproduced without Reactor, so its cause isn't established.
  - Phase 3e hit it in 1 of 4 full Android runs, when a fixture tore down straight after an
    app-initiated pop, while the pop was still animating (the phase 3e report).
  - **Worked around in 4a.** Three device fixtures drive the `ReactorWindow` path: the same root
    switches from a `NavigationStack` (alone, or in a tab) to other content, so the reconciler
    unmounts the stack (stopping its navigator) and then replaces the window's page, during pops and
    pushes, as the navigator starts and once MAUI's stack has changed. Swapping in alternating pops
    and pushes after the stack changed crashed in round 7 (the fourth push) in 5 of 10 runs; a single
    push, or swaps swept across one push animation, never did. On Android, `WindowPageSetter` now
    waits until every stack in the outgoing page has finished before replacing the page: 0 crashes
    in 18 runs. The stack-in-a-tab case didn't crash without the wait (0 of 6), so its wait is
    defensive.
  - Not covered: a platform back gesture in flight at the moment of the swap (the navigator isn't
    running then). Next step: file the plain-MAUI repro in dotnet/maui.
- **Back gestures and platform pops** are reconciled with a 3-second cutoff (`StackNavigator`).
  Inside Shell this is replaced by MAUI's navigation-completed signal (spec 069 §4.2); a `NavigationPage` keeps the cutoff.
- **Android JNI references.** Phase 3e's SIGSEGV after a theme change was one instance: removed
  views were never disconnected. Removed views are now disconnected (spec 067 §16). A full-suite
  memory and handle check on Android lands with phase 5's harness.

## 4. Order

The order follows the ports: what blocks the most of them lands first.

1. **4a**: this inventory, and the Android window-swap workaround with its fixtures.
2. **4b**: Shell and the rest of app navigation: Shell, page dialogs, the Android back button outside
   Shell, modal presentation style, and app and window lifecycle hooks. Shell unblocks every port
   except Calculator.
3. **4c**: controls, ordered by how many ports need them:
   - element refs (focus, scrolling, WebView commands);
   - Label spans; CollectionView layouts, spacing and sizing tests, and selection state in items;
     shapes; RadioButton with view content; Entry cursor and selection; SwipeView items; WebView;
     Picker display;
   - tests for every 🧪 control and layout, and the §1 audit of every ✅ row;
   - CollectionView grouping, IndicatorView, CarouselView tests, gradient brushes, ContentView, image
     source equality. (CarouselView and IndicatorView moved to a sub-phase of their own, 4c-4: spec
     070 §5.)
4. **4d**: look: animations, the theme value (`ThemeRef`-style), styling direct views, flow
   direction, `AutomationProperties`, and tests for every 🧪 modifier.
5. **4e**: desktop interaction: pinch, pointer, drag and drop, context menus, MenuBar, tooltips,
   accelerators, keyboard navigation, and tests for swipe and pan.
6. **4f**: interop and extensibility: `Native<TView>`, `ReactorView`, `UseService<T>`, behaviors,
   HybridWebView, Toast and Snackbar, and the wrapper generator with its validation libraries.
7. **4g**: Windows on the Dev Box runner (TitleBar, window properties, multi-window, MenuBar on
   Windows), analyzers, and the hot-reload verification.

Each sub-phase is gated like phase 3. The gate for phase 4 as a whole is this table with no ⬜ and no
🧪 left, and no 🟡 whose gap a port needs.
