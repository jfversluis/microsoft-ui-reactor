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

## 2. Inventory

Legend: ✅ implemented · 🟡 partial (gap noted) · ⬜ planned (sub-phase) · ⛔ proposed exclusion.

### Controls

| Item | Status | Notes |
|---|---|---|
| ActivityIndicator, BoxView, CheckBox, Editor, ProgressBar, RadioButton, SearchBar, Switch | ✅ | descriptors (3e) |
| Label, Image, Button, ImageButton, Entry, Slider, Stepper, Picker, DatePicker, TimePicker, WebView, GraphicsView, Border | ✅ | hand-written. Label has no `FormattedText` spans (4b). Picker has no item display binding (4b). |
| CollectionView | 🟡 | keyed items, selection, header, footer, empty view, remaining-items threshold. Missing: grouping, item spacing and layouts (grid, horizontal), `ItemSizingStrategy` (4b). |
| CarouselView | 🟡 | position. Missing: `Loop`, peek insets; its position echo isn't characterized (spec 067 §14) (4b). |
| RefreshView, SwipeView, ScrollView | ✅ | ScrollView has no `ScrollToAsync` handle (4e, with refs) |
| IndicatorView | ⬜ 4b | paired with CarouselView |
| ContentView | ⬜ 4b | as a plain single-child container |
| Shapes (Ellipse, Line, Path, Polygon, Polyline, Rectangle, RoundRectangle) | ⬜ 4b | |
| Brushes (linear and radial gradients) as backgrounds and strokes | ⬜ 4b | `.Background` takes a `Brush` already; gradient factories are missing |
| HybridWebView | ⬜ 4e | interop |
| TitleBar (Windows) | ⬜ 4f | needs the Windows runner |
| Map (`Microsoft.Maui.Controls.Maps`) | ⬜ 4e | through the generic `Native<TView>` and the wrapper generator, not in the core package |
| Frame | ⛔ | obsolete in MAUI; use Border |
| ListView, TableView, ViewCell, TextCell and the other cells | ⛔ | legacy; CollectionView replaces them |
| BlazorWebView | ⛔ | a different UI model; host it through `Native<TView>` if needed |
| StackLayout (the legacy, non-specialized one) | ⛔ | VStack and HStack replace it |

### Layouts and pages

| Item | Status | Notes |
|---|---|---|
| VerticalStackLayout, HorizontalStackLayout, Grid, FlexLayout, AbsoluteLayout | ✅ | the direct renderer has its own stack and grid |
| ContentPage, NavigationPage (declarative `NavigationStack`), TabbedPage (`Tabs`), FlyoutPage (`Flyout`) | ✅ | modals in `NavigationStack` |
| Shell: items, tabs, flyout, routes, query parameters, search handler, back interception | ⬜ 4c | declarative, state-driven routes like `NavigationStack` |
| Multi-window (opening a second `ReactorWindow`) | ⬜ 4f | |
| ToolbarItems | ✅ | primary and secondary order |

### Interaction

| Item | Status | Notes |
|---|---|---|
| Tap, swipe, pan gestures | ✅ | modifiers |
| Pinch, pointer (hover, press, move) | ⬜ 4d | |
| Drag and drop | ⬜ 4d | |
| ContextFlyout (context menus), MenuBar (desktop) | ⬜ 4d | |
| ToolTip | ⬜ 4d | |
| Focus and keyboard (focus a view, `Completed`, return type, keyboard type) | 🟡 | keyboard type and return type exist; no focus handle (4e, with refs) |
| Keyboard accelerators | ⬜ 4d | desktop |

### Look and theme

| Item | Status | Notes |
|---|---|---|
| Light and dark theme (`UseAppTheme`) | ✅ | |
| An `AppThemeBinding` equivalent (a value per theme without re-rendering) | ⬜ 4d | |
| Styling direct views (they ignore implicit styles) | ⬜ 4d | spec 065 lists it as the main gap of the direct renderer |
| Fonts (registered fonts, `FontFamily`, font size and attributes) | ✅ | |
| Images (file, URI, stream, font images) | 🟡 | every `ImageSource` renders; font and stream sources compare by reference, so a new instance per render reloads the image (4b: value equality) |
| Shadow, Clip, Opacity, Rotation, Scale, Translation, ZIndex | ✅ | modifiers |
| Animations (implicit transitions, explicit `Animate`) | ⬜ 4d | |
| VisualStateManager | ⛔ | state drives the element; Controls views keep their own visual states |

### Platform and app

| Item | Status | Notes |
|---|---|---|
| Accessibility (semantic description, hint, heading level; AutomationId) | ✅ | modifiers. Checked through UIA/Appium in phase 5. |
| Window size and theme hooks | ✅ | `UseWindowSize`, `UseAppTheme` |
| Dependency injection (`UseService<T>`) | ⬜ 4e | |
| Reactor inside XAML (`ReactorView`) and XAML inside Reactor (`Native<TView>`) | ⬜ 4e | `ReactorPage` exists for page-level hosting |
| Third-party controls: a MAUI wrapper generator (spec 058 equivalent) emitting `ControlDescriptor`s | ⬜ 4e | validated on Syncfusion.Maui.Toolkit, CommunityToolkit.Maui and Maps |
| Hot reload (`dotnet watch`, VS) | ✅ | re-renders every live root; verified with real edits in 4f |
| Analyzers (hook rules) on MAUI code | ⬜ 4f | |
| Windows (WinUI-backed MAUI) | ⬜ 4f | Dev Box runner |

## 3. Open issues carried into phase 4

- **Android: replacing `Window.Page` while a `NavigationPage` navigates crashes MAUI.** Spec 065 §7
  records this; it reproduced with plain MAUI, without Reactor ("NavigationManager cannot be null",
  and in phase 3e "CurrentPage cannot be null", both from `NavigationViewFragment.OnCreateView`).
  - Phase 3e hit it on a GPU-backed emulator, when a test replaced the window's page right after an
    app-initiated pop (3 of 3 runs).
  - On the current SwiftShader emulator it doesn't reproduce, even when the page is swapped while the
    navigator is mid-push or mid-pop. So a deferred page swap couldn't be shown to fix it, and it
    isn't shipped.
  - `Window_ReplacingThePageWhileAStackNavigates_ShowsTheNewPage` covers the scenario.
  - Next steps: file the plain-MAUI repro in dotnet/maui, then add a `ReactorWindow` workaround once
    there's a device setup that reproduces it.
- **Back gestures and platform pops** are reconciled with a 3-second cutoff (`StackNavigator`);
  replace it with MAUI's navigation-completed signal (4c, with Shell).
- **Android JNI references.** Phase 3e's SIGSEGV after a theme change was one instance: removed
  views were never disconnected. Removed views are now disconnected (spec 067 §16). A full-suite
  memory and handle check on Android lands with phase 5's harness.

## 4. Order

1. **4a**: this inventory, and the window-swap fixture.
2. **4b**: the missing controls and gaps:
   - IndicatorView, ContentView, shapes, brushes;
   - CollectionView grouping and layouts; CarouselView loop and peek;
   - Label spans, image sources.
3. **4c**: Shell, declaratively, with routes as state.
4. **4d**: interaction and look:
   - pinch, pointer, drag and drop, context menus, MenuBar, tooltips, accelerators;
   - animations;
   - the theme-value hook;
   - styling direct views.
5. **4e**: interop and extensibility:
   - `Native<TView>`, `ReactorView`, `UseService<T>`, element refs (focus, scroll);
   - HybridWebView;
   - the wrapper generator with its three validation libraries.
6. **4f**: Windows on the Dev Box runner (TitleBar, multi-window, MenuBar on Windows), analyzers,
   and the hot-reload verification.

Each sub-phase is gated like phase 3, and the gate for phase 4 as a whole is this table with no ⬜ left.
