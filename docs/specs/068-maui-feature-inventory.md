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
| Entry | 🟡 | no `CursorPosition` / `SelectionLength`, which the direct `Entry` already supports (4c); its return type is untested (4c) |
| RadioButton | 🟡 | text content only. Templated RadioButtons with view content (WeatherTwentyOne, PointOfSale) in 4c |
| ActivityIndicator, ProgressBar | 🧪 | descriptors; headless tests only, no device fixture |
| DatePicker, TimePicker | 🧪 | device fixtures only, no headless tests |
| Label | 🟡 | no `FormattedText` spans (4c; PointOfSale uses 24) |
| Picker | 🟡 | no item display binding (4c) |
| Image, ImageButton | 🧪 | font and stream sources compare by reference, so a new instance per render reloads the image (4c: value equality) |
| WebView | 🧪 | `Url`, `Html`, `OnNavigated` only. Missing: cancelling `Navigating`; reload, back and JavaScript evaluation, through refs (4c) |
| GraphicsView, Border | 🧪 | |
| CollectionView | 🟡 | keyed items, item templates, cell recycling, context and selection are tested. Header, footer, empty view, remaining-items threshold, grid and horizontal layouts, `ItemSpacing` and `ItemSizingStrategy` exist but are untested (4c; layouts and spacing are used by 3 ports). Grouping is missing (4c; no port). Item rendering must be able to show the selection state, which replaces the `Selected` visual state (4c) |
| CarouselView | 🧪 | position, `Loop`, `PeekAreaInsets` and `ItemSpacing` exist, all untested; its position echo isn't characterized (spec 067 §14) |
| SwipeView | 🟡 | left and right items of text and icon only. Missing: top and bottom items, custom item views, `SwipeMode`, `Threshold`, opening and closing from code (4c) |
| ScrollView, RefreshView | 🧪 | ScrollView has headless tests only, RefreshView none. Scrolling from code waits for refs (4c): `scrollRef.Current?.ScrollToAsync(...)` |
| IndicatorView | ⬜ 4c | paired with CarouselView; no port |
| ContentView | ⬜ 4c | as a plain single-child container |
| Shapes (Ellipse, Line, Path, Polygon, Polyline, Rectangle, RoundRectangle) | ⬜ 4c | WeatherTwentyOne uses `Ellipse` |
| Brushes (linear and radial gradients) as backgrounds and strokes | ⬜ 4c | `.Background` takes a `Brush` already; gradient factories are missing (1 port). WinUI has none either |
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
| FlyoutPage (`Flyout`) | 🧪 | headless tests only, no device fixture |
| ToolbarItems | 🧪 | primary and secondary order; headless tests only |
| Shell | ⬜ 4b | 6 of 7 ports use it as their root. Declarative and state-driven, like `NavigationStack`. Scope from the ports: `TabBar`; `FlyoutItem` and custom flyout content; absolute (`//route`) and relative (`..`) routes with query parameters; `NavBarIsVisible` / `TabBarIsVisible` per page; `Navigating` interception and `BackButtonBehavior`; the selected item's state (WeatherTwentyOne styles it with a trigger); search handler. Routes are typed, as in WinUI's `NavigationHandle<TRoute>` / `UseNavigation<TRoute>` (spec 011): query parameters are fields of the route record, and string URIs exist only at the deep-link boundary. Back interception follows WinUI's back-handling contract |
| Page dialogs: alert, action sheet, prompt | ⬜ 4b | 5 of 7 ports. No Reactor API today. 4b picks the shape: an imperative hook bound to the current page (such as `UseDialogs()`), or a controlled element |
| Toast and Snackbar (CommunityToolkit) | ⬜ 4f | 2 ports; part of the CommunityToolkit validation |
| Android back button outside Shell (`OnBackButtonPressed`) | ⬜ 4b | with Shell's back interception |
| Modal presentation style | ⬜ 4b | |
| App and window lifecycle (`Activated`, `Deactivated`, `Stopped`, `Resumed`, `Destroying`) | ⬜ 4b | as hooks |
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
| VisualStateManager | ⛔ | state drives the element; Controls views keep their own visual states. The ports' states map to: `Checked` and `Selected` → props from state (templated RadioButton and CollectionView selection, 4c); `PointerOver` → pointer events (4e); `AcceptDrop` → drag and drop (4e); `AdaptiveTrigger` → `UseWindowSize` |
| Triggers (`Trigger`, `DataTrigger`, `EventTrigger`) and trigger actions | ⛔ | a `DataTrigger` is a conditional prop; an `EventTrigger` on `Appearing` is `OnAppearing` plus an animation (4d); a style trigger on a Shell item's `IsChecked` is that item's selected state (4b) |
| `ControlTemplate`, `ContentPresenter` | ⛔ | components are the templates |
| Data binding (`BindingContext`, compiled bindings) | ⛔ | state and props replace it |
| `DataTemplateSelector`, `BindableLayout` | ⛔ | an item-render function chooses per item; `ForEach` renders a list into any layout |
| Effects | ⛔ | legacy in MAUI (handlers replace them); `.Set` and descriptors reach the native view |

### Interaction

| Item | Status | Notes |
|---|---|---|
| Tap | ✅ | `OnTap` |
| Swipe, pan | 🧪 | modifiers, untested |
| Focus and keyboard (`Completed`, return type, keyboard type, focusing from code) | 🟡 | keyboard type and return type exist. Focusing from code waits for refs (4c): WinUI's `UseElementRef<T>()` plus `.Ref(...)`, then `ref.Current?.Focus()` |
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
| Element refs | ⬜ 4c | WinUI's `UseElementRef<T>()` and `.Ref(...)`, with `VisualElement` as the MAUI constraint. 4c decides whether `ElementRef<T>` moves to the shared core. Focus, scrolling, WebView commands and explicit animations use it |
| Behaviors (attached once per native view) | ⬜ 4f | the ports use CommunityToolkit's `IconTintColorBehavior`, `StatusBarBehavior`, `TextValidationBehavior` and `EventToCommandBehavior`. `.Set` runs after every update, so it would add a behavior each render; this needs a mount-only hook or a `.Behavior(...)` modifier |
| Dependency injection (`UseService<T>`) | ⬜ 4f | reads the app's `IServiceProvider` (MAUI's `MauiContext.Services`). It complements `Context<T>` / `UseContext`, Reactor's tree-scoped injection, rather than replacing it. 4f decides whether WinUI gets the same hook, so shared components can call one API |
| Reactor inside XAML (`ReactorView`) and XAML inside Reactor (`Native<TView>`) | ⬜ 4f | `ReactorPage` exists for page-level hosting. WinUI's host is `ReactorHostControl`; `ReactorView` follows MAUI's `*View` naming for the same role. WinUI has no general native-view element (only `InlineUI` inside RichTextBlock), so 4f decides whether `Native<T>` is a shared-core shape that WinUI also gets |
| Third-party controls: a MAUI wrapper generator (spec 058 equivalent) emitting `ControlDescriptor`s | ⬜ 4f | validated on what the ports use: Syncfusion.Maui.Toolkit (DeveloperBalance's chart, segmented control, text input layout and shimmer), SkiaSharp.Views.Maui (2 ports), CommunityToolkit.Maui (its behaviors, Toast and Snackbar) and Maps (eShop) |
| Essentials (device, sensors, preferences, …) | ⛔ | not UI; apps call them directly |
| Hot reload (`dotnet watch`, VS) | 🟡 | re-renders every live root. Verified headlessly only (spec 065 §7); real edits from an IDE on devices in 4g |
| Analyzers (hook rules) on MAUI code | ⬜ 4g | |
| Windows (WinUI-backed MAUI) | ⬜ 4g | Dev Box runner |

## 3. Open issues carried into phase 4

- **Android: replacing `Window.Page` while a `NavigationPage` navigates crashes MAUI.** Spec 065 §7
  records this. The push variant ("NavigationManager cannot be null", from
  `NavigationViewFragment.OnCreateView`) reproduced with plain MAUI, without Reactor. Phase 3e hit a
  second message from the same frame ("CurrentPage cannot be null") during a pop. That variant hasn't
  been reproduced without Reactor, so its cause isn't established.
  - Phase 3e hit it in 1 of 4 full Android runs, when a fixture tore down straight after an
    app-initiated pop, while the pop was still animating (the phase 3e report).
  - **Worked around in 4a.** Two device fixtures drive the `ReactorWindow` path: the same root
    switches from a `NavigationStack` to other content, so the reconciler unmounts the stack
    (stopping its navigator) and then replaces the window's page, during pops and pushes, as the
    navigator starts and once MAUI's stack has changed. Swapping in alternating pops and pushes
    after the stack changed crashed in round 7 (the fourth push) in 4 of 6 runs; a single push, or
    swaps swept across one push animation, never did. On Android, `WindowPageSetter` now waits for
    the outgoing stack's navigator to finish before replacing the page: 0 crashes in 8 runs.
  - Not covered: a platform back gesture in flight at the moment of the swap (the navigator isn't
    running then). Next step: file the plain-MAUI repro in dotnet/maui.
- **Back gestures and platform pops** are reconciled with a 3-second cutoff (`StackNavigator`);
  replace it with MAUI's navigation-completed signal (4b, with Shell).
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
     source equality.
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
