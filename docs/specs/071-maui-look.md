# Spec 071: MAUI look (phase 4d)

Phase 4d gives Reactor's MAUI renderer the look features spec 068 lists under "Look and theme" and the
remaining 🧪 modifiers: a theme value applied without rendering, styling for direct views, registered
fonts, animations, flow direction, `AutomationProperties`, and tests for every untested modifier. It
stacks on 4c (spec 070), whose head is `1a8d88c6`.

§1 is the outline of the whole phase. §2 is 4d-1's design: characterized on devices, revised after five
design reviews (`bc46b5ea`: D1–D12; `f72f44ae`: E1–E10; `f63bbb5f`: F1–F6; `938621cb`: G1–G4; `da45a0d0`: H1,
H2, M1, cited where they apply), not yet built.
4d-2 and 4d-3 get their own sections when their characterization is done.

## 1. The phase

### 1.1 Scope

From spec 068's inventory (the "Look and theme" rows, the 🧪 modifier rows, and `AutomationProperties`):

| Item | Spec 068 status | Sub-phase |
|---|---|---|
| An `AppThemeBinding` equivalent: a value per theme, applied without rendering | ⬜ | 4d-1 |
| Styling direct views (they ignore implicit styles) | ⬜ | 4d-1 |
| Registered fonts and their rendering on devices | 🧪 (the element settings are covered; registration and rendering aren't) | 4d-1 |
| Animations: implicit, enter and exit, explicit | ⬜ | 4d-2 |
| Flow direction (right-to-left) | ⬜ (no modifier; the direct view hard-codes `MatchParent`) | 4d-3 |
| `AutomationProperties` (`IsInAccessibleTree`, `ExcludedWithChildren`) | ⬜ | 4d-3 |
| Tests for Opacity, Shadow, Clip, Rotation, Scale, Translation, ZIndex | 🧪 | 4d-3 |
| Tests for swipe and pan | 🧪 (spec 068 §4 placed them in 4e; they move here, with the other gesture modifiers' census) | 4d-3 |
| Tests for semantic description, hint and heading level | 🧪 | 4d-3 |

### 1.2 Three sub-phases

Ordered by how many ports need each item (spec 068 §4).

1. **4d-1: the theme value, styling direct views and fonts** (§2). Every port uses `AppThemeBinding`,
   and every port styles its controls with the template's `Styles.xaml` (26 to 39 implicit styles each,
   §2.1). Styles and theme values are designed together because a style's setters are mostly theme
   values.
2. **4d-2: animations.** 5 of 7 ports animate, mostly with MAUI's explicit `FadeTo`, `TranslateTo`,
   `RotateTo` and `ScaleTo`. WinUI's API is the source of truth (spec 014):
   - implicit `.Animate(curve, properties)`;
   - enter and exit `.Transition(transition, curve)`;
   - explicit `WithAnimation(curve, action)`.

   The characterization comes first:
   - MAUI's `ViewExtensions` animations and its `Animation` class on each platform: the ticker,
     cancellation, what a property write during an animation does, and completion on unmount;
   - how a spring curve maps to MAUI's `Easing`, which is a function of time;
   - how exit transitions interact with the engine's unmount and the removed-view release.

   `Curve`, `Easing`, `Transition` and `Edge` are records and enums with no WinUI dependency
   (`src/Reactor/Animation/Curve.cs` and `Transition.cs`), so 4d-2 proposes moving them to the shared
   core. WinUI stays source compatible (same namespace, type forwards in `Reactor.dll`), and the API
   index only gains entries. `AnimateProperty` names compositor properties (`Offset`, `CenterPoint`),
   so whether it moves is a 4d-2 decision. `AnimationScope` uses `Microsoft.UI.Composition` and stays
   in the WinUI renderer.
3. **4d-3: flow direction, `AutomationProperties` and every 🧪 modifier.**
   - `.FlowDirection(FlowDirection)` on both registries. The direct view's hard-coded `MatchParent`
     becomes a field. MAUI's name: WinUI has the property but no element modifier, only
     `RichTextRun.FlowDirection` and `.Set(...)`.
   - `IsInAccessibleTree` and `ExcludedWithChildren`, with WinUI's counterpart:
     `.AccessibilityView(AccessibilityView.Raw)` and its shorthand `.AccessibilityHidden()`. 4d-3
     decides whether MAUI takes WinUI's names, since MAUI's two properties don't map one to one onto
     WinUI's three views.
   - The shared-modifier census: the property census excludes `ViewElement.Modifiers` today, and
     `PropertyMatrix.cs` already reserves it for 4d. Every modifier gets a set, update, equal-render and
     clear case on both registries. Device geometry and pixel checks cover Opacity, Shadow, Clip,
     Rotation, Scale, Translation and ZIndex, real input covers swipe and pan, and the platform's
     accessibility properties cover the semantic modifiers.

### 1.3 WinUI names

| Feature | WinUI Reactor | MAUI renderer | Why it differs |
|---|---|---|---|
| A themed value on a modifier | `ThemeRef` passed to modifier overloads (`.Background(ThemeRef)`, `.Foreground(ThemeRef)`, `.BorderBrush(ThemeRef)`), stored in `Element.ThemeBindings` | `AppThemeValue<T>` passed to overloads of the same modifiers under MAUI's property names (`.BackgroundColor`, `.TextColor`, …) and to `.Themed(property, value)`, stored in `Element.ThemeBindings` | a different abstraction, so a different name (§2.2): WinUI's `ThemeRef` is a resource key that WinUI resolves with the element's inherited theme; Reactor's MAUI renderer has no theme dictionaries (spec 068: ⛔), so its value is a light and dark pair, named in MAUI's vocabulary (`AppThemeBinding`). The shape (overloads on the modifiers, literal wins) is WinUI's |
| Theme as state | `UseColorScheme()`, `UseIsDarkTheme()` | `UseAppTheme()` (exists) | unchanged |
| Implicit (type-wide) styles | XAML implicit styles, which WinUI's controls honor | `ElementStyles` provided to a subtree (§2.3) | WinUI Reactor has no Reactor-level implicit style. Its spec 015 recommends plain style functions instead (proposal 1). The MAUI renderer needs one because direct views can't read XAML styles |
| Named styles | named-style fluents (`.AccentButton()`), `.ApplyStyle(key)` | style functions, a documented pattern with no API | |
| A subtree's theme | `.RequestedTheme(ElementTheme)` | none (open question, §2.12) | MAUI has no per-view theme |
| Implicit animation | `.Animate(curve, properties)` | same (4d-2) | |
| Enter and exit | `.Transition(transition, curve)` | same (4d-2) | |
| Explicit animation | `WithAnimation(curve, action)` | same (4d-2) | |
| Flow direction | the property only (`.Set`), `RichTextRun.FlowDirection` | `.FlowDirection(direction)` (4d-3) | |
| Hidden from accessibility | `.AccessibilityView(…)`, `.AccessibilityHidden()` | decided in 4d-3 | |

### 1.4 Principles

These carry over from 4c:
- Characterize on devices first.
- Never guess provenance across deferred notifications.
- Prefer reporting over swallowing.
- Every zero oracle needs a positive control.
- Device waits accept only events after the action.
- Mutation-check the key tests.
- WinUI stays unchanged unless a type moves to the shared core. Then it stays source compatible and the
  API index only gains entries.
- Every feature works on both registries.

## 2. 4d-1: the theme value, styling direct views and registered fonts

The design review of `bc46b5ea` (two model families) asked for twelve changes (D1–D12); its second round,
on `f72f44ae`, for ten more (E1–E10). Those that needed devices first were characterized before the design
changed: re-entrant theme sets, the Disabled state's precedence, Android's stale defaults and the
instrumentation in round one (§2.1.1–§2.1.4); where a Controls view's value comes from, Android's resolver
inside the change, and divergence, threads and settling in round two (§2.1.5–§2.1.7). A third round, on
`f63bbb5f`, raised F1–F6; the repair hook's ownership and generation and MAUI's own enabling were
characterized on Android (and the latter on Mac Catalyst) first (§2.1.8). A fourth, on `938621cb`, raised
G1–G4; non-detaching insertion and the enabling layers were characterized on all four devices (§2.1.9). A
fifth, on `da45a0d0`, raised H1, H2 and M1; one bridge topology was characterized on all four devices with
the real `IVisualTreeElement` overrides (§2.1.10).

### 2.1 Characterization (device, `LookCharacterization.cs`, `LookReviewCharacterization.cs`, `LookRound2Characterization.cs`, `LookRound3Characterization.cs`, `LookRound4Characterization.cs`, `LookRound5Characterization.cs`)

The baseline is plain MAUI 11.0.0 (`4e6582f`), then the same trees through Reactor on both registries.
Devices: Mac Catalyst 26.7, the iPhone simulator (iOS 27.0), the iPad simulator (iOS 27.2) and the
Android 17 emulator.

The fixtures are on demand (`REACTOR_DEVICE_TEST_FILTER=Characterize_Look`). They log what they measure
and assert only that each instrument works:
- **Mapper hook:** appended to every `LabelHandler` and `ButtonHandler` mapping. Its positive control is
  that a `Text` change counts once.
- **Frame sampler:** `CADisplayLink` or `Choreographer`, one watch that both counts and samples, so a
  sample's index is the frame callback it ran in: 0 is the synchronous read right after the action, *n*
  the *n*-th frame callback after it. Its positive control is an observation of a new color made by a
  frame callback after the action (index 1 or more), never by the synchronous read (review D12).
- **Log capture:** an `ILoggerProvider` added in `MauiProgram`. Its positive control is a line logged
  through the app's logger factory.
- **Width oracle:** a registered family measures differently from the default.

The device app now registers its two fonts: `OpenSans-Regular.ttf` as `OpenSansRegular` and
`OpenSans-Semibold.ttf` as `OpenSansSemibold`.

**Platform-started theme changes.** An app can't change the system appearance, so the fixture logs
`REACTOR-LOOK-TOGGLE-SYSTEM` and the host flips it: `xcrun simctl ui <udid> appearance` on the
simulators and `adb shell cmd uimode night` on the emulator. Mac Catalyst wasn't run, because the
flip would change the host Mac's own appearance.

**Themes: what MAUI does at a change.**

| | Mac Catalyst | iPhone, iPad | Android |
|---|---|---|---|
| `AppThemeBinding` values (on the view, in an implicit style's setter, in an explicit style's setter) | re-applied **synchronously inside the `UserAppTheme` setter, before `RequestedThemeChanged` is raised** (MAUI's `TriggerThemeChangedActual` pushes the theme resource down the tree, then raises the event: `Application.cs` 277–300) | the same | the same |
| What the handlers re-map | exactly one `TextColor` mapping per themed view, and nothing else, then or later (one dispatch, 600 ms). The positive control (a `Text` change) re-maps `Text` and, on UIKit's attributed text, `LineHeight`, `TextDecorations`, `CharacterSpacing` and `HorizontalTextAlignment` | the same | the same (`Text` alone in the control) |
| The `UserAppTheme` set with six views | 2.5–4.8 ms | 1.4–5.2 ms | 4–12 ms |
| A `DynamicResource` color (not themed) | unchanged | unchanged | unchanged |
| A view without a color | a dynamic system color (label 85% black or white, button title 50% or 55%) that follows the platform's appearance, whose trait collection flips **by the first frame after the set**, not inside it | the same (label black or white) | **never follows**: it keeps its inflation-time color (`#000000` at 54%) after `UserAppTheme` changes, and after a system change with `UserAppTheme` unspecified, on both registries; the Controls button's text stays white |
| A platform-started change | not run | `RequestedThemeChanged` once per flip, the bindings already applied; at the event the label's trait collection is still the old one; everything shows the new theme by the next frame | once per flip, bindings applied; views without a color don't follow |

**Themes through Reactor.**

| | Mac Catalyst | iPhone, iPad | Android |
|---|---|---|---|
| A `UseAppTheme` component's re-render (posted from the event) | lands before the next frame (the sampled frame 1, the same frame the system colors flip) | the same, for app- and platform-started changes | **lands at frame 2**: one frame is drawn with MAUI's bindings already new and Reactor's colors still old, for app- and platform-started changes |
| A Controls view in a direct layout (the "anchored" view: its `Parent` is the `DirectViewHost`, but it isn't a logical child) | takes implicit styles at mount and updates its `AppThemeBinding` values at a theme change, exactly like a logical child | the same | the same |

The anchored view behaves like a logical child because MAUI's `Element.SetParent` subscribes a child
to its parent's resources-changed listener (`Element.cs` 390–420). Propagation follows `Parent`, not
`LogicalChildren`, so the anchored views have no gap here.

**Implicit styles on Reactor views.** The fixture added an implicit `Label` style and an implicit
`Button` style to `Application.Resources`:
- Label: text color as an `AppThemeBinding`, font size 23, `OpenSansSemibold`.
- Button: teal background, white text, corner radius 3, and a `Disabled` visual state with a gray
  background.

| | Mac Catalyst | iPhone, iPad | Android |
|---|---|---|---|
| Controls registry | every setter applied, including the `Disabled` state and the theme change | the same | the same |
| Direct registry | **none applied**. Label: system font 14, the system text color. Button: no background, a 50% black title, and a 22% title when disabled | none applied. Button: tint-blue title (`#0088FF`), a 45% gray title when disabled | none applied. Button: Material's primary color (`#512BD4`) as background, and a 12% background with 38% text when disabled |
| Reactor's `TextColor` write, then a render without it | the write overrides the style; the clear (`ClearValue`) **brings the style's value back** on the Controls registry and the platform default on the direct registry | the same | the same |
| `Application.Resources.Remove(key)` of both styles | nothing changed on any view within 300 ms | the same | the same |

**Theme-switch cost.** 300 labels, the median of rounds 2–4. Each cell gives the time until the last
label shows the new color, then the display frame it shows at: 0 means before the next frame, so the
same frame as the set. A Reactor re-render's synchronous part is 1–7 ms; the rest is the posted render.

| | Mac Catalyst | iPhone | iPad | Android |
|---|---|---|---|---|
| MAUI `AppThemeBinding` on each label | 39.6 ms, frame 0 | 28.8, 0 | 36.1, 0 | 1.6, 0 |
| MAUI implicit style with an `AppThemeBinding` | 38.6, 0 | 27.8, 0 | 43.4, 0 | 1.7, 0 |
| Plain MAUI, the color written by hand after the set | 33.0, 0 | 35.8, 0 | 22.7, 0 | 1.3, 0 |
| Reactor `UseAppTheme` re-render, Controls registry | 82.1, frame 1 | 85.0, 1 | 63.0, 1 | 29.8, 2 |
| Reactor `UseAppTheme` re-render, direct registry | 62.1, 1 | 92.8, 1 | 74.8, 1 | 26.6, 2 |
| Reactor, the themed color written without a render, Controls registry | 32.1, 0 | 35.6, 0 | 20.7, 0 | 1.3, 0 |
| Reactor, the themed color written without a render, direct registry | 20.5, 0 | 22.8, 0 | 23.5, 0 | 1.3, 0 |
| The same, second run: written after the setter returns, Controls registry | 31.0, 0 | 25.7, 0 | 26.3, 0 | 1.4, 0 |
| The same, written **inside a `RequestedThemeChanged` subscriber** (Reactor's path), Controls registry | 32.0, 0 | 22.7, 0 | 16.5, 0 | 1.3, 0 |
| Written after the setter returns, direct registry | 32.7, 0 | 33.3, 0 | 34.4, 0 | 1.3, 0 |
| Written inside a `RequestedThemeChanged` subscriber, direct registry | 20.3, 0 | 34.6, 0 | 32.2, 0 | 1.1, 0 |

The cost is the platform's text-color writes: about 0.07–0.13 ms a label on UIKit and 5 µs on Android.
Writing only the themed property costs what MAUI's own bindings cost. A re-render costs 2–4 times that
on Apple's platforms and about 20 times on Android, and lands a frame late on Android.

**Fonts.**

| `FontFamily` | Mac Catalyst | iPhone, iPad | Android |
|---|---|---|---|
| The registered alias (`OpenSansRegular`) | `OpenSans-Regular` | the same | the asset's typeface |
| The file name (`OpenSans-Regular.ttf`) | resolves | resolves | resolves |
| The file name without extension (`OpenSans-Regular`) | resolves (it is the PostScript name) | resolves | resolves, after MAUI logs a warning ("Unable to load font 'OpenSans-Regular' from assets") |
| The family name (`Open Sans`) | resolves (registered with Core Text) | resolves | **doesn't**: system sans-serif, a warning logged |
| A missing family (`NoSuchFont`, `NoSuchFont.ttf`) | the system font, **nothing logged** | the same | a sans-serif fallback that **isn't** `Typeface.Default`, and a warning logged |
| Bold or Italic on a family registered with one face | **ignored**: still `OpenSans-Regular` | the same | synthesized: a fake bold (style Bold, weight 700, same advance width) and a skewed italic |
| Direct vs Controls registry | the same font, size and measured width for every spelling, on labels and buttons | the same | the same |
| First resolution, then cached | alias 3 ms, missing 4–28 ms; under 0.1 ms after | alias 5–17 ms, missing about 4 ms | alias 3 ms, without extension 9 ms |

The width oracle at 17 pt for "The quick brown fox 0123456789":

| | Mac Catalyst | iPhone, iPad | Android |
|---|---|---|---|
| Default font | 265.8 | 256.6 | 249.9 |
| Open Sans Regular | 262.6 | 262.6 | 257.9 |
| Open Sans Semibold | 268.1 | 268.1 | 262.9 |

**What the measurements establish:**
- When MAUI's bindings change: synchronously, before `RequestedThemeChanged`.
- When a Reactor re-render lands: a frame later on Android.
- What each mechanism costs.
- That anchored Controls views are styled and themed.
- That direct views ignore XAML styles entirely, including their visual states.
- That font spellings and missing fonts differ by platform.

**What they leave open:**
- Mac Catalyst's platform-started change. Flipping it would change the host Mac's own appearance, and
  no non-disruptive way to give one app a different system appearance was found, so it stays
  uncharacterized (review D12).
- Real Dynamic Type and accessibility font scaling, which these fixtures don't touch.

#### 2.1.1 Re-entrant theme sets (review D5)

`Characterize_Look_Reentrancy`, plain MAUI, all four devices. Three subscribers to
`RequestedThemeChanged` (one before a stand-in for Reactor's, the stand-in, one after), a label with an
`AppThemeBinding`, and a label without a color. During a change from Light to Dark, the app sets
`UserAppTheme = Light`: from the subscriber before, from the subscriber after, or from a `TextColor`
property-changed handler that runs while MAUI applies its bindings (before the event). A control round
sets nothing.

| | Mac Catalyst | iPhone, iPad | Android |
|---|---|---|---|
| The nested set | **suppressed**: MAUI's `TriggerThemeChangedActual` returns while `_themeChangedFiring`, so there is no second event, no binding pass, and no `OnPropertyChanged(UserAppTheme)` (so the application handler doesn't map the new theme). `UserAppTheme` and `RequestedTheme` read Light from then on | the same | the same |
| What the subscribers see | the event's argument stays Dark for every subscriber; `RequestedTheme` reads Light for every subscriber after the nested set, so a subscriber after it sees a disagreement and one before it doesn't | the same | the same |
| From the binding pass | the bound label shows Light briefly (the nested set's value), then MAUI's outer pass overwrites it with Dark | the same | the same |
| Settled: MAUI's bindings | **Dark** (the event's theme) | Dark | Dark |
| Settled: the platform's appearance | **Light** (the label without a color is black) | **Dark** (trait Dark) | **Dark** (AppCompat night mode 2) |
| Recovery | `UserAppTheme = Light` again is a no-op (the setter compares with its field); only a different value (Unspecified) raises an event and re-applies | the same | the same |

A set from inside Reactor's own pass would run inside the same dispatch (Reactor's pass is a subscriber), so
it behaves as the subscriber cases.

After a suppressed nested set, MAUI's three answers disagree: `RequestedTheme` says Light, its bindings say
Dark, and the platform says Light on Mac Catalyst and Dark elsewhere. Nothing reports it.

#### 2.1.2 A forced user theme while the system changes (review D12)

`Characterize_Look_ForcedTheme` and `…_SystemChangedWhileForced`, host-driven, on iPhone, iPad and
Android (Mac Catalyst not run, §2.1). The system goes dark with `UserAppTheme` Unspecified, the app forces
Light, the system changes while forced, and the app returns to Unspecified.

| | iPhone, iPad | Android |
|---|---|---|
| Unspecified under a dark system | one event, Dark; bindings and platform dark | the same |
| The app forces Light | one event, Light; bindings and platform light | the same |
| The system flips while forced | **MAUI doesn't observe it**: `PlatformAppTheme` stays Dark (the forced override hides the system's trait change from MAUI); no event | `PlatformAppTheme` follows the system; no event (`RequestedTheme` stays Light); platform light |
| Back to Unspecified after the system went dark while forced | one event, Dark; correct | the same |
| Back to Unspecified after the system went **light** while forced | **one event, Dark** (the stale `PlatformAppTheme`): bindings Dark while the platform shows Light, never corrected (2 s later, and after another system flip to light: still Dark) | no event (`RequestedTheme` was already Light); correct |

MAUI 11 on iOS: returning to Unspecified after the system changed while a user theme was forced applies a
stale theme for good.

#### 2.1.3 The Disabled state and inherited `IsEnabled` (review D7)

`Characterize_Look_Disabled_Plain` and `…_Disabled_Reactor`, all four devices, the same on each. The style
has a teal background and a `Disabled` state with a gray background and yellow text.

| | Plain MAUI | Reactor, Controls registry | Reactor, direct registry |
|---|---|---|---|
| A local value under a `Disabled` state (implicit style, explicit style, or groups set on the view) | **the state wins while disabled**: a local red background shows gray (`WithFullVsmPriority` promotes a system state's setters above local values, `VisualStateManager.cs` 49–60, 145–165); red again when enabled | the same: Reactor's declared red shows gray while disabled | no visual states: the declared red stays, with the platform's disabled text |
| A parent layout disabled | the child's `IsEnabled` reads false (`IsEnabledCore` ANDs the parent's, `VisualElement.cs` 660–677), it goes to `Disabled`, its platform control is disabled | the same (this registry has no anchored views) | **nothing propagates**: direct children stay enabled on the platform and in look; the **anchored Controls child stays enabled** (its `Parent` is the `DirectViewHost`, so `IsEnabledCore` never sees the direct layout); the layout's own platform view is disabled on Android only (not an input barrier for its children) |
| The child explicitly `IsEnabled = true` under a disabled parent | stays disabled (the parent wins) | the same | stays enabled |
| The parent re-enabled | the child is enabled and `Normal` again | the same | — |

The direct registry has no inherited `IsEnabled` at all today: a disabled direct layout neither disables
nor greys its children, on any platform. That is a defect of the registry, independent of styling.

#### 2.1.4 Android's defaults after a theme change (review D11)

`Characterize_Look_Defaults [controls|direct]` (an app-started change) and `…_Defaults_System [direct]`
(host-started), all four devices. A label, a button, a disabled button and an entry, without colors and
with colors that a render clears after the change.

| | Mac Catalyst, iPhone, iPad | Android |
|---|---|---|
| Views without a color | follow (dynamic system colors) | **stale**: they keep the inflation-time colors (§2.1), both registries, app- and system-started |
| A color cleared after the change (label text, button text and background) | the current theme's default | **the stale inflation-time default**: MAUI restores the color list it cached at the view's first color write (`TextViewExtensions.cs` 66–82, `ButtonHandler.Android.cs` 27–55, 109–119) |
| An entry's placeholder color cleared after the change | the current default | the current default (`#FFFFFF` at 70% in dark) |
| An entry's text color without a value | follows | follows (`#FFFFFF` in dark) |
| The existing view's context after the change | — | **reconfigured**: night mode on, and its theme's attributes re-resolve (`textColorPrimary` white, `textColorSecondary` white at 70%, `textColorHint` white at 70%; disabled variants with them) |
| A fresh view of the same class created on that existing context | — | **the current theme's defaults**, enabled and disabled: `TextView` white at 70% (disabled 21%), `MaterialButton` text black, tint `#512BD4` (disabled white at 38% and 12%), `EditText` hint white at 70%; identical to a fresh view on a context re-themed for the new configuration (`CreateConfigurationContext` + `Maui.MainTheme.NoActionBar`) |

So the current defaults are recoverable natively, without hard-coded colors and without remounting: a
fresh platform view of the same class on the view's own (already reconfigured) context carries them, in
every state. Only views that already exist, and colors MAUI restores from its cache, are stale.

#### 2.1.5 Where a Controls view's value comes from (review round 2, E1)

`Characterize_Look_ValueSources`, all four devices, the same on each. `BindableObject.IsSet` on a label's
`TextColor` (and a button's `TextColor` and `BackgroundColor`):

| Source | `IsSet` | Value |
|---|---|---|
| nothing | **false** | null |
| a local value | true | the value |
| an implicit style | true | the style's |
| a local value cleared over an implicit style (Reactor's clear) | true | the style's |
| an explicit style | true | the style's |
| a themed style setter (`AppThemeBinding` in a setter) | true | the theme's |
| an `AppThemeBinding` on the view | true | the theme's |
| a `DynamicResource` | true | the resource's |
| a `Disabled` state's setter, active | true | the state's |
| the same state unapplied, the style having a normal setter | true | the style's |

`IsSet` is false exactly when no MAUI source supplies the value (`BindableObject.cs` 246–254: any
specificity other than the default counts). It is the test for "the platform's default is what shows".

#### 2.1.6 Android's resolver measured inside the change (review round 2, E1, E2)

`Characterize_Look_AndroidResolver [app|system]`, Android. Reference views are built with MAUI's own
recipes (`MauiTextView`, `MauiMaterialButton`, `MauiAppCompatEditText`) **inside a `RequestedThemeChanged`
subscriber**, disposed after reading, and compared with fresh handler-created views mounted after the change.

| | App-started (`UserAppTheme`) | System-started (`cmd uimode night`) |
|---|---|---|
| The event's thread | main | main |
| The views' contexts at the event | already reconfigured (night): the label's `MainActivity > ContextThemeWrapper > …`; the button's `ContextThemeWrapper > MauiMaterialContextThemeWrapper > MainActivity > …` (MAUI wraps it, a second theme overlay) | the same |
| Reference `MauiTextView` on the label's context | `#FFFFFF` at 70% (disabled 21%), cold 0.4 ms, warm 0.1 ms | the same, cold 0.9 ms |
| Reference `MauiMaterialButton` (MAUI's recipe on the handler's base context) | text black (disabled white 38%), tint `#512BD4` (**disabled black 12%**), 0.5 ms | text black, tint `#512BD4` (**disabled white 12%**), 2.4 ms |
| `MaterialButton` on the button's own wrapped context | the same as the recipe | the same |
| `MauiAppCompatEditText` hint | white 70% | the same; 0.9–4 ms with the button |
| **Fresh handler-created views after the change** | **identical to the in-event references, in every state** (including the black disabled tint) | identical (white disabled tint) |
| Existing views | label and button stale; the entry's hint current | the same |

The in-event references equal what MAUI itself creates after the change, for both overlays and both
starts. App- and system-started changes differ in one Material resource (the disabled tint); the
references follow MAUI either way, so the resolver's target is "what MAUI would create now", not a
guessed color.

**The repair, gated by ownership.** Writing the reference list only where `IsSet` is false: the plain
label is repaired; labels with an implicit style, a local value cleared back to a style, and a themed
style setter are left to MAUI and keep their values. A repaired button's list carries its states: disabled
shows the dark theme's disabled text (white 38%), re-enabled the dark default (black).

**MAUI's own clears.** A button whose only `TextColor` setter is in its `Disabled` state: re-enabling makes
MAUI map `TextColor` with null and restore the list it cached at creation (stale white). A `TextColor`
mapping appended to MAUI's mapper that, for a repaired view whose value is null, re-writes the reference
list after MAUI's mapping, restores the current default (counted: one re-apply); without the appended
mapping the stale cache shows.

#### 2.1.7 Divergence, threads and settling (review round 2, E9, E10)

`Characterize_Look_Divergence`, all four devices, the same pattern on each.

| | Mac Catalyst | iPhone, iPad | Android |
|---|---|---|---|
| The event's thread and dispatchers | main; the window's and the application's dispatchers need no dispatch | the same | the same |
| A normal app-started change: the platform's appearance | the trait collection still old at the event and right after the set; **new at frame 1** | the same | night mode **already new at the event** |
| Four `AppThemeBinding` labels; a nested set from the second one's handler during MAUI's binding pass | **split**: the first two show the announced theme (Dark), **the last two the nested one (Light)**: `AppThemeBinding.GetValue` reads `RequestedTheme` when it applies | the same | the same |
| An `AppThemeBinding` mounted after the divergence | the nested theme (Light) | the same | the same |
| A Reactor root mounted after it | its `AppThemeBinding` label Light; `UseAppTheme` returns Light | the same | the same |
| The platform after it | Light | Dark | Dark |

So after a suppressed nested set there is no consistent answer: MAUI's announcement says Dark, `RequestedTheme`
says Light, bindings applied before the nested set and the platform (except on Mac Catalyst) say Dark, and
bindings applied after it, and everything mounted later, say Light.

#### 2.1.8 The repair hook, and MAUI's own enabling (review round 3, F1, F2)

**The repair hook (F1), `Characterize_Look_RepairHook`, Android.** A stand-in for §2.9's hook: live views
registered as owned at mount and unregistered at unmount; on each `TextColor` mapping it rechecks
registration, the handler's identity and platform view, and `IsSet`, then resolves a reference for the
current generation (built with MAUI's recipe on the view's handler context, cached per generation) and writes
it natively. Each case has a control.

| Case | With the hook | Control |
|---|---|---|
| (a) Mounted disabled under a `Disabled`-only `TextColor` setter, Light→Dark while disabled, enabled | while disabled: 6 mappings, 0 writes (`IsSet`, the state owns it); enabled: one write of the **generation-2 (Dark)** reference: black, the current default | no hook: MAUI's cached Light-era list, white (stale) |
| (b) Repaired in Dark, a `Disabled` setter takes over, Dark→Light while it holds, re-enabled | while held: skipped ("a MAUI source owns the value"); re-enabled: one write of the **Light** reference, white (equal to a fresh reference under Light) | replaying the list retained from Dark: black (wrong) |
| (c) Unregistered (Reactor's unmount) before MAUI maps | 0 writes | the registered positive control: 1 write |

Totals: 15 mappings, 4 writes, 11 skips, 2 snapshots built (one per generation used). Gating on live
ownership plus `IsSet` at each invocation, and resolving for the current generation, gives the right color in
all three cases; replaying a retained list doesn't.

**MAUI's own enabling (F2), `Characterize_Look_MauiEnabling [controls|direct]`, Android and Mac Catalyst,
the same on both.** A Controls button disabled by its XAML style (`.Style`), one disabled by a local write
(`.Set`), and a Controls layout disabled by its XAML style holding a Controls button and a button that is
direct on the direct registry.

| | Controls registry | Direct registry |
|---|---|---|
| A XAML-style or local `IsEnabled = false` on a Controls button | disabled (MAUI) | the same |
| The XAML-disabled layout's Controls children | disabled (MAUI's inheritance) | the same |
| The XAML-disabled layout's **direct** child | — | **enabled**: MAUI propagates to logical children only |
| `IsSet(IsEnabled)` | **true on every view, including children with no own value** (the coerced inherited value counts), so `IsSet` can't separate own from inherited enabling | the same |
| Reading back the source | a local `true` over the style reads true; cleared, the style's false reads back | the same |
| Reactor's parent disabled and re-enabled, an unrelated child render | no change, no `IsEnabled` events (every view stays false) | the same |
| The layout enabled by a local write outside Reactor, then cleared | `PropertyChanged(IsEnabled)` on the layout and on each Controls descendant, both ways | the same on the layout and the Controls child; the direct child stays enabled throughout |

**A MAUI defect found on the way.** A Controls layout on the direct registry (a layout with a Controls-only
setting) whose children are a direct view followed by a Controls view doesn't mount: MAUI's
`Layout.Insert(index, child)` passes the *view* index to `InsertLogicalChild` (`Layout.cs` 245–260), which
counts only `Element`s. When the view index exceeds the number of `Element` children it throws
`ArgumentOutOfRangeException`; otherwise it inserts the logical child at the wrong logical position (§2.1.9).
Every platform. Filed upstream as [dotnet/maui#40758](https://github.com/dotnet/maui/issues/40758). §2.11
works around it without detaching siblings.

#### 2.1.9 Inserting without detaching, and the enabling layers (review round 4, G1, G3, G4)

**Insertion candidates (G1), `Characterize_Look_MixedInsert [S0–S3]`, all four devices.** A Controls
`VerticalStackLayout` holding a direct label, a **focused `Entry` with a selection (2+3) and the soft keyboard
up**, another direct label and a Controls label; a Controls label inserted at index 1. Native and logical
order, the entry's focus, selection and IME, its `Loaded`/`Unloaded`/`Focused`/`Unfocused`/`ParentChanged`/
`HandlerChanging` events, native and handler identity, Android's attach and layout child counters, frames.

| Candidate | Result (all four devices unless noted) |
|---|---|
| S0: MAUI's `Insert` as is | here no throw (the index, 1, doesn't exceed the one `Element` child), but **the logical order is wrong** (`entry, inserted, c3` while the views are `d0, inserted, entry, …`); with no `Element` before it, it throws (§2.1.8) |
| S1: the views after the index removed and re-added | correct order, but **the entry loses focus, the soft keyboard and first-responder status** (`Unfocused`, `Unloaded`, two `Loaded` and two `ParentChanged`; Android: detached and re-attached, 3 children removed and 4 added); cost grows with the suffix |
| **S2: logical stand-ins** for direct children (a logical-only `VisualElement` per direct child, added and removed with the public `AddLogicalChild`/`InsertLogicalChild`/`RemoveLogicalChild` at the same position) | correct native **and** logical order; **the entry keeps focus, selection, the keyboard and first-responder status**; no events but the entry's existing `Loaded`; same native view and handler; Android: 0 children removed, 1 added, the entry neither detached nor re-attached; frames identical to S0's |
| S3: each direct child wrapped in a Controls `Grid` | the same as S2 for the entry and order; frames identical in a stack; but one extra Controls view per direct child (cost below), and grid and flex placement would have to move to the wrapper |

Accessibility focus wasn't measured directly (no screen reader is automated here); S2 performs no native
removal or detach on any sibling (Android's counters: 0 removed, 0 detached), which is the path that clears
it. Reactor component state and effects aren't touched by any candidate: all four only rearrange native
children of one layout, with no node change. On iOS a platform-owned trailing `UIView` sits in the layout's
subviews while the entry is first responder; it vanishes in S1 because the entry loses focus.

**Cost (G1, G4), `Characterize_Look_MixedInsertCost`.** One Controls label inserted at index 1 of a Controls
layout holding one direct label and 300 Controls labels (median of rounds 2–4), and mounting 300 direct
labels into a Controls layout until first layout:

| | Mac Catalyst | iPhone | iPad | Android |
|---|---|---|---|---|
| One insertion, S1 | 166 ms | 141 ms | 152 ms | 6.4 ms (300 children removed, 301 added) |
| One insertion, S2 | 4.5 ms | 5.6 ms | 5.3 ms | 2.7 ms (0 removed, 1 added) |
| Mounting 300 direct labels, bare | 259 ms | 290 ms | 298 ms | 85 ms |
| Mounting with stand-ins (S2) | 291 ms | 298 ms | 318 ms | 64 ms |
| Mounting in wrappers (S3) | 467 ms | 518 ms | 553 ms | 107 ms |

S2 costs the same as bare within noise; S3 costs 60–90% more on Apple's platforms.

**The enabling layers (G3), `Characterize_Look_EnablingLayers`, all four devices, the same on each.**

| Sequence | Result |
|---|---|
| (A) Plain MAUI: a parent disabled, the app writes its child's local `IsEnabled = false`, the parent re-enabled | the child stays disabled (the app's value) |
| (B) The same with Reactor's inherited `false` written on the child's manual layer and cleared on re-enable (F2's design) | **the child is enabled: the app's value is gone**, because `SetValue` and `ClearValue` share the manual layer (`ManualValueSetter`) |
| (C) An owned adapter: a logical-only `VisualElement` whose `IsEnabled` Reactor writes, with the Controls button as **its logical child** (`AddLogicalChild`), the button placed visually in a direct layout | the adapter disabled: the button disabled (MAUI's `IsEnabledCore`), one `IsEnabled` event; the app's local `false`, then the adapter re-enabled: **the button stays disabled (the app's value kept)**; the app clears it: enabled; an implicit style and an `AppThemeBinding` reach the button through the adapter (text yellow, background red → blue on a theme change) |
| (C') The same adapter as the button's `Parent` only, not its logical child (Reactor's anchoring today) | the adapter disabled: **the button stays enabled**: MAUI propagates `IsEnabled` to logical (visual-tree) children only (`VisualElement.cs` 1966–1976), so `Parent` alone carries resources but not enabling |

So MAUI already inherits enabling between Controls views through `IsEnabledCore` and logical children; Reactor
needs to cover only direct views (which it owns) and Controls views whose MAUI chain doesn't reach the
source, and an adapter in that chain does it without writing the child's manual layer.

#### 2.1.10 One bridge topology (review round 5, H1)

`Characterize_Look_Bridges [T1|T3a|T3b]`, all four devices, the same on each. **H**, a Controls stack, holds
`[A, D, C]`; **D**, a direct layout, holds `[B, E, F]`; **F**, a nested direct layout, holds `[G, I]` (A, C, B
and G Controls; D, E, F and I direct). Built off-window, then shown. The traversal is the device runner's
(`Runner.cs` 111–122: down from the window through `IVisualTreeElement.GetVisualChildren`, no dedup); each
node's `GetVisualParent` is compared with the node that enumerated it. Then: a `BindingContext` set on H after
mount and changed; an implicit `Button` style in H's resources; a theme change (an `AppThemeBinding` on B);
H disabled and re-enabled; D's bridge disabled (Reactor's write for D's state); `Window` and `IsLoaded` on B
and G; H's native child count; the bridges' handlers.

| | T1: round 4's separate stand-in (in H, slot-aligned) and adapters (D's under H, F's under D's), MAUI's projection | T3a: **one bridge per direct view** (`bridge(D)` slot-aligned in H; `bridge(F)` under `bridge(D)`; B and G their bridges' logical children); H a Reactor subclass whose projection lists its **views** | T3b: T3a, and each bridge **pulls** MAUI's propagated state when its logical parent's changes |
|---|---|---|---|
| Traversal under H | `H > A > adapter > C > adapter > B > adapter > G`: **bookkeeping enumerated (3), the direct views D, E, F, I never** (a stand-in that presented D would enumerate B twice: through D and through D's adapter) | `H > A > D > B > E > F > G > I > C`: **every real view once, in slot order, no bridge** | the same |
| `GetVisualParent` against the enumerator | consistent, but over bookkeeping nodes | D → H, E → D, F → D, I → F, A and C → H; **B → `bridge(D)` → D and G → `bridge(F)` → F** | the same |
| `BindingContext` set after mount, then changed | B and G follow both | the same (BindingContext follows logical children, which the projection doesn't change) | the same |
| H's implicit style; a theme change | B and G yellow; B's background follows the theme | the same | the same |
| `Window` on B and G | set | **null**: H's projection no longer pushes to the bridge, and the bridge took its `Window` when H had none | set |
| H disabled | A, B and G disabled | **only A**: MAUI's propagation walks `GetVisualChildren`, which now skips the bridge | A, B and G disabled (2 pulls) |
| D's bridge disabled (Reactor's write) | B and G disabled | the same | the same |
| H's native child count; bridges' handlers | 3; null (no native peers) | 3; null | 3; null |

So one bridge per direct view, a projected Controls layout and a pulling bridge give a unique, slot-ordered
enumeration of real views with no bookkeeping in it, live `BindingContext`, resources, styles, themes,
enabling and `Window`, and no native peers. **One difference remains, fixed by MAUI:** a Controls view's
visual parent is its logical parent (`Element.cs` 616–620: `GetVisualParent() => Parent`), and its logical
parent must be an `Element`, which a direct layout isn't. So a Controls view inside a direct layout reports its
bridge as visual parent, and the bridge reports the direct layout: an upward walk from B reaches D through one
bookkeeping hop, while the downward walk never shows it. Wrapping B in a Controls container doesn't help (the
wrapper's visual parent is again its logical parent).

### 2.2 What WinUI has, and why the MAUI value isn't `ThemeRef` (review D1)

WinUI Reactor's theme value is `ThemeRef(string ResourceKey)`, a reference to a WinUI theme resource:
- Typed tokens: `Theme.Accent`, `Theme.CardBackground`, …. Any key: `Theme.Ref(key)`.
- It is passed to modifier overloads (`.Background(ThemeRef)`, `.Foreground(ThemeRef)`,
  `.BorderBrush(ThemeRef)`, `.WithBorder(ThemeRef, …)`), which record it in `Element.ThemeBindings`
  (`IReadOnlyDictionary<string, ThemeRef>`, keyed by property name; `ElementExtensions.cs` 3428–3433,
  `Element.cs` 202). Literals are stored separately (`VisualModifiers`), and **a literal always wins**:
  the bindings are applied as a synthesized `Style` of `{ThemeResource}` setters, which a local value
  overrides whatever the call order (`ElementExtensions.cs` 1358–1388).
- WinUI resolves the key with the **element's own effective theme**, which a subtree's
  `RequestedTheme` can override (`Theme.cs`, `Reconciler.cs` ≈8277), and re-resolves it itself when the
  theme changes.

That is a different abstraction from MAUI's `AppThemeBinding`: an indirection through a theme dictionary,
with an inherited per-element theme. Reactor's MAUI renderer has neither (no theme dictionaries, spec 068
⛔; MAUI has no per-view theme). Its value is a literal light and dark pair for the app's theme, so it
takes MAUI's vocabulary: **`AppThemeValue<T>`**. It keeps WinUI's *shape*: overloads on the same modifiers,
a binding table on the element keyed by property, literals stored apart and always winning.

**A neutral pair in the shared core?** No. WinUI wouldn't use it: its themed values are resource keys
resolved by WinUI, and a literal pair would bypass the element-inherited theme and high-contrast
resources. A type both renderers don't use doesn't belong in the core, so `AppThemeValue<T>` is MAUI-side
and WinUI is unchanged.

For styles WinUI Reactor relies on XAML: implicit styles honored by WinUI's controls, named-style
fluents, lightweight styling (`.Resources(...)`). Spec 015 declined a Reactor-level style type.

### 2.3 The model: look properties; declared, applied and native

**Look properties (review D3, D8).** Every property a theme value or a style can set is a
**`LookProperty`**: a static descriptor with a stable identity that knows

- its owner element type and value type, and its **presence**: whether a declaration has a value (§2.12:
  `null` for nullable properties, a presence bit for the four layout spacings);
- how to read, set and clear it on the element record (typed, no reflection);
- how to write it to each registry's native view: the Controls view's `BindableProperty` (`SetValue`, or
  `ClearValue` for unset), and the direct view's field and the handler key it re-maps;
- whether its value affects size (a font, a spacing, a padding), so its write may invalidate measure;
- whether a value needs a Controls view on the direct registry (§2.11);
- on Android, its platform default's recipe, if the default is theme-derived (§2.9).

The handlers' own writes for look properties go through the same descriptors, so the ordinary update path
and the look patch can't diverge (the census runs both, §2.12). Theme bindings, style merging and the look
patch are keyed by descriptor: one binding is one property.

**Three elements per host node (review D4).**

- **Declared**: `node.Element`, what the app rendered. The reconciler, `HandlerContext.Live`, callbacks and
  every controlled-value rule use it (the carousel reads `Live.Position`; the echo contract restores from
  it). It is never projected.
- **Applied**: the declared element merged with its style, with every theme binding resolved for the
  current theme and with the effective `IsEnabled` (§2.8), as plain scalar values. The handler receives it
  in ordinary mounts, updates and restores and diffs applied-to-applied. Handlers read look values only from
  the element they're given, never from `Live`.
- **Native**: what the platform views hold. After a look patch the node's applied element is updated through
  the descriptors to the values the patch wrote, so the next ordinary update diffs from what is on the view.
  Android's resolved default lists (§2.9) are native state outside the applied record.

**The look patch.** A theme pass, a style pass, a Disabled pass (§2.8) and Android's default resolution
(§2.9) write *only the affected look properties*, through their descriptors' native writers, in the root's
echo scope. A look patch never runs the handler's `Update`: not `Configure` (`.Set(...)`), not a controlled
value's restore against native drift, not child reconciliation, and no invalidation beyond what the written
properties themselves cause. A size-affecting write (a font, a spacing, a padding) invalidates measure, as
the same write does on the ordinary path; a full update's invalidation (a `GraphicsView` redraw, a re-map of
unrelated properties) never happens. When a pass needs a capability the mounted view lacks (§2.11), that
host is routed through the ordinary update path instead of patched.

### 2.4 API

```csharp
namespace Microsoft.UI.Reactor.Maui;

/// <summary>A value per app theme, written by the renderer and rewritten at a theme change, without rendering.</summary>
public readonly record struct AppThemeValue<T>(T Light, T Dark)
{
    /// <summary>Dark for <see cref="AppTheme.Dark"/>, Light otherwise (MAUI's AppThemeBinding rule).</summary>
    public T For(AppTheme theme) => theme == AppTheme.Dark ? Dark : Light;
}

public static class AppThemeValue
{
    public static AppThemeValue<T> Of<T>(T light, T dark) => new(light, dark);
}

/// <summary>A property a theme value or a style can set (§2.12's inventory). Instances are static and compared by reference.</summary>
public abstract class LookProperty
{
    public string Name { get; }
    public Type OwnerType { get; }
    public Type ValueType { get; }
}
public sealed class LookProperty<TOwner, TValue> : LookProperty where TOwner : Element { /* internal get/set/clear, native writers */ }

/// <summary>The descriptors, grouped by owner.</summary>
public static class LookProperties
{
    public static class View { public static readonly LookProperty<ViewElement, Color> BackgroundColor; /* Background, Opacity, IsEnabled, … */ }
    public static class Text { public static readonly LookProperty<TextViewElement, Color> TextColor; /* FontFamily, FontSize, … */ }
    public static class Entry { public static readonly LookProperty<EntryElement, Color> PlaceholderColor; /* … */ }
    public static class Page { public static readonly LookProperty<PageElement, Color> BackgroundColor; }
    public static class Shell { public static readonly LookProperty<ShellElement, Color> TitleColor; /* … */ }
    // … one class per owner, every property of §2.12's inventory
}

// Typed overloads on the existing modifiers (WinUI's shape), each binding its descriptor:
public static T BackgroundColor<T>(this T element, AppThemeValue<Color> color) where T : ViewElement;
public static T Background<T>(this T element, AppThemeValue<Brush> brush) where T : ViewElement;
public static T TextColor<T>(this T element, AppThemeValue<Color> color) where T : TextViewElement;
public static BorderElement Stroke(this BorderElement element, AppThemeValue<Color> color, double thickness = 1);
public static T Fill<T>(this T shape, AppThemeValue<Color> color) where T : ShapeElement;
public static T Stroke<T>(this T shape, AppThemeValue<Color> color, double? thickness = null) where T : ShapeElement;

// Every other property, pages and Shell included:
public static TElement Themed<TElement, TOwner, TValue>(this TElement element,
    LookProperty<TOwner, TValue> property, AppThemeValue<TValue> value) where TElement : TOwner where TOwner : Element;
// e.g. Entry(text, setText).Themed(LookProperties.Entry.PlaceholderColor, AppThemeValue.Of(Gray200, Gray500))

// On the renderer's Element base (namespace Microsoft.UI.Reactor.Core), WinUI's name:
public IReadOnlyDictionary<LookProperty, object>? ThemeBindings { get; }   // value: the boxed AppThemeValue<T>; set only through the methods above

/// <summary>Implicit styles for Reactor elements, by exact element type.</summary>
public sealed class ElementStyles
{
    public static ElementStyles Empty { get; }

    /// <summary>
    /// <paramref name="style"/>'s set look properties and theme bindings become the type's defaults;
    /// <paramref name="disabled"/>'s apply while the element is effectively disabled (§2.8; a disabled
    /// variant can't set IsEnabled). Replaces an earlier entry for the type.
    /// </summary>
    public ElementStyles Add<TElement>(TElement style, TElement? disabled = null) where TElement : ViewElement;

    /// <summary>Returns styles without an entry for <typeparamref name="TElement"/> (the type then falls through to an outer scope).</summary>
    public ElementStyles Remove<TElement>() where TElement : ViewElement;
}

// Provides styles to a subtree; nested scopes fall through per type (§2.7). A composition primitive, beside ErrorBoundary:
public static Element WithStyles(ElementStyles styles, Element? child);

// Android's theme-derived defaults (§2.9). Read once, when the first root mounts; setting it later throws.
public static class ReactorTheme { public static bool ResolveAndroidDefaults { get; set; } = true; }
```

**The layout spacings keep their public `double` contract (review E3).** `VStackElement.Spacing`,
`HStackElement.Spacing`, `GridElement.RowSpacing` and `ColumnSpacing` stay `double` with the same
signatures (getter and `init` accessor: binary and source compatible). Their `init` accessors now also set a
private presence bit next to the backing field:

```csharp
private double _spacing;
private bool _hasSpacing;                       // copied by `with` (record clones copy every field), compared by record equality
public double Spacing { get => _spacing; init { _spacing = value; _hasSpacing = true; } }
internal bool HasSpacing => _hasSpacing;        // the descriptor's presence
```

`VStack(8, …)` and `with { Spacing = 0 }` set it (an explicit 0 is present); `VStack(…)` doesn't (unset: a
style may fill it, else MAUI's default 0 is written). Two elements that differ only in presence are unequal,
so a change between an explicit 0 and unset reaches the handler and re-merges the style: under no style it
writes nothing (0 both ways); **under a style supplying 8, explicit 0 → unset writes 8, and unset → explicit 0
writes 0** (review F5). There is no public way to unset a present spacing (`with { Spacing = 0 }` sets it);
the descriptor's clear operation is an internal method on the record that returns a copy with the bit and
value reset (`ClearSpacing()`: a `with { }` clone whose private fields the record itself resets), used only by
style merging. A **positional** property can't carry the pattern: its constructor
always assigns it, so it is always present. It is styleable only if nullable, with `null` as unset
(`BoxView`'s `Color`); a non-nullable positional property (`RoundRectangle`'s `CornerRadius`) is excluded
from styles (§2.12).

An app's template styles, ported:

```csharp
static readonly ElementStyles AppStyles = ElementStyles.Empty
    .Add(Label("").FontFamily("OpenSansRegular").FontSize(14)
        .TextColor(AppThemeValue.Of(Colors.Black, Colors.White)))
    .Add(Button("").FontFamily("OpenSansSemibold").FontSize(14)
            .TextColor(AppThemeValue.Of(Colors.White, AppColors.Primary))
            .BackgroundColor(AppThemeValue.Of(AppColors.Primary, Colors.White))
            with { CornerRadius = 8, Padding = new Thickness(14, 10) },
        disabled: Button("")
            .TextColor(AppThemeValue.Of(AppColors.Gray950, AppColors.Gray200))
            .BackgroundColor(AppThemeValue.Of(AppColors.Gray200, AppColors.Gray600)));

new ReactorWindow(WithStyles(AppStyles, Component<AppShell>()), DirectHandlers.CreateRegistry());

// A named (keyed) XAML style is a function:
static LabelElement Headline(this LabelElement l) => l.FontSize(32).FontFamily("OpenSansSemibold");
```

### 2.5 Theme values

1. **Literal always wins (review D2).** A declaration's literals and its theme bindings are stored
   independently: literals in the element's fields and modifiers (a `with { TextColor = … }` assignment is a
   literal), bindings in `ThemeBindings`, keyed by descriptor. For each property the applied value is the
   declared literal if present, else the declared binding resolved for the theme, else the style's literal,
   else the style's binding resolved (§2.7), else unset. Call order doesn't matter. Removing the literal in a
   later render reveals the binding. A theme switch writes nothing to a property a literal overrides.
2. **One binding per property.** Binding a descriptor again (a second `.Themed` call, or a typed overload and
   `.Themed` on its descriptor) replaces the earlier binding. Bindings compare by descriptor and value.
3. **The authority is the last announced theme (review D5, E9, F3).** One app-wide `ThemeSource` subscribes to
   `Application.RequestedThemeChanged` and holds the theme MAUI last *announced* (the event's argument) as one
   immutable snapshot, `(Theme, Generation)`, published by a single reference write after the source assigns
   the next generation under its lock; readers read the reference once and use both fields of that snapshot,
   so a reader never pairs one announcement's theme with another's generation. Generations only increase.
   Before its first announcement it holds
   `Application.RequestedTheme` read when it subscribed. It subscribes when the first Reactor host
   (`ReactorWindow`, `ReactorPage`, or a root) is constructed, before any root mounts. Dark resolves the dark
   value, anything else the light one; headless tests supply it through a seam.
4. **The boundary of that authority (§2.1.1, §2.1.7).** Normally announced theme, `RequestedTheme`, MAUI's
   bindings and the platform agree. After a `UserAppTheme` set that MAUI suppressed during a change they
   don't, and nothing Reactor can choose agrees with everything: MAUI's bindings are split (applied before
   the nested set: announced; after: nested), anything mounted later that reads `RequestedTheme` (an
   `AppThemeBinding`, `UseAppTheme`, a root whose `ThemeSource` subscribed after the event) shows the nested
   theme, and the platform shows the announced theme on iOS and Android but the nested one on Mac Catalyst.
   Reactor's themed views follow the announced theme, including views mounted later; this matches the
   platform on iOS and Android and MAUI's bindings applied by the announcement. The divergence is reported
   (rule 5), not repaired.
5. **The divergence diagnostic (review E9).** After each announcement, each top root schedules one check on
   its window, taken from a settled observation: the window's platform appearance two frame callbacks after
   the announcement (iOS and Mac Catalyst settle at frame 1, Android at the event, §2.1.7), and only if the
   `ThemeSource`'s generation hasn't moved since. It compares the announced theme, `RequestedTheme` and that
   window's appearance; a disagreement is reported once per kind and window: a suppressed nested set (the
   remedy: set `UserAppTheme` from a posted callback), or iOS's stale platform theme after a forced theme
   (§2.1.2). A trait still propagating, or a newer announcement, is never reported.
6. **Re-projection without rendering.** On an announcement, each top root runs a theme pass (§2.6) over its
   enrolled hosts: look patches for the descriptors their bindings (own, from their style, from their
   Disabled variant) name. No component renders, no handler `Update` runs. The pass runs inside MAUI's
   event, in the frame MAUI's own bindings change in (frame 0 on all four devices; §2.1's in-event cost
   equals the after-set cost).
7. **Theme enrolment.** A host is in its root's theme set while it has theme bindings (own, from its style
   or Disabled variant) or, on Android, a theme-derived default it owns (§2.9). This set is separate from a
   style scope's membership (§2.7). Nodes leave both at unmount.
8. **Only look properties.** Descriptors exist only for look properties (§2.12), so no controlled value,
   event or content can be themed.

### 2.6 Transactions, re-entrancy and dispatchers (review D5, E10)

1. **One transaction per tree.** The top root holds a transaction depth entered by every operation that
   writes to native views in its tree: a commit (mount, update, unmount), a child root's commit (list cells,
   `CreateChildRoot` content, including a cell the platform realizes outside a Reactor commit), a controlled
   value's restore, teardown, and the theme, style, Disabled and Android-default passes.
2. **A change during a transaction is deferred.** The root records the pending generation; the pass runs when
   the outermost transaction ends, synchronously in the same turn. Nodes a commit mounts after the change are
   projected with the new theme at mount.
3. **A pass settles.** It takes the target theme and generation, iterates a snapshot of the enrolled nodes,
   checks each node's liveness (not unmounted, its root alive) before writing, skips nodes already applied
   for the target, and patches the rest. Nodes mounted during the pass are projected at mount with the
   current theme and enrolled. When the pass ends, if the generation moved, it runs again; only then is the
   root marked applied. A fourth run in one turn is posted with a warning: then, and only then, the
   same-frame guarantee doesn't hold.
4. **Nested sets** from inside a pass are inside MAUI's dispatch, so MAUI suppresses them (§2.1.1); rule 5 of
   §2.5 reports them.
5. **Errors.** A native write that throws goes to the nearest error boundary; the pass continues with the
   other nodes; the failed node is re-projected on the next pass.
6. **Dispatchers (review E10).** MAUI raises the change on the thread that set `UserAppTheme`, or on the main
   thread for a platform change (§2.1.6, §2.1.7). The `ThemeSource` publishes the snapshot there (§2.5 rule 3)
   and notifies each subscribed top root
   through that root's own scheduler: synchronously when the root's dispatcher needs no dispatch (on iOS,
   Mac Catalyst and Android every window shares the main thread, so every root runs in the event, in MAUI's
   frame), and posted to the root's dispatcher otherwise (a root on another window's thread, or a set from a
   background thread). A posted pass lands after MAUI's bindings, so the same-frame guarantee is stated only
   for roots on the announcing thread. Notification iterates a snapshot of subscribers; a root that unmounted
   (a window closed during the notification) is skipped by rule 3's liveness check, and a posted pass checks
   it again on arrival.
7. **Ordering (review F3).** A notification carries no theme: it only wakes the root. The root's pass reads
   the source's current snapshot when it starts, and its pending generation is raised to the larger of the
   pending one and the snapshot's, never lowered. A late or older notification (a posted wake-up arriving
   after a newer synchronous pass) finds the root already applied for a generation at least as new and does
   nothing; a pass that started before a publication sees the newer generation when it settles (rule 3) and
   runs again.

### 2.7 Element styles (review D9, E6)

1. **Fill unset, fieldwise.** A style is an element of the same type; its set look properties and theme
   bindings are its setters. For each descriptor of the element's type (common view properties inside
   `Modifiers` included, field by field), the declaration's literal or binding wins; otherwise the style's
   literal, then its binding (§2.5 rule 1). Content, controlled values and events have no descriptors and
   are never read from a style.
2. **Per-type fall-through.** `WithStyles` scopes nest. A host takes, for its exact type, the entry of the
   nearest scope that has one: an inner scope with only a `Label` entry doesn't hide an outer `Button` entry,
   and `Remove<T>()` in an inner scope falls through to the outer entry.
3. **Resolved once per scope.** A scope node keeps a resolved table, its entries over its outer scope's
   resolved table, computed when the scope mounts or its styles change, never per render. A host finds its
   scope through a pointer fixed at creation (like `NearestProvider`) and reads its type's entry in O(1). A
   render with the same `ElementStyles` instance does nothing.
4. **Membership (review E6).** Every host below a scope is a member of that scope, by exact element type,
   whether or not it currently has an entry, bindings, a variant or a default: membership is about who a
   restyle may reach, the theme set (§2.5 rule 7) about who a theme switch must reach. A host joins its
   nearest scope at mount and leaves at unmount.
5. **Restyling without rendering.** A scope rendered with a different `ElementStyles` instance recomputes its
   resolved table and its inner scopes', then runs a style pass over the members of each type whose resolved
   entry changed (by reference), beneath skipped components too: look patches for the properties the old or
   new entry names (an entry removed writes the declaration's values or unset). Empty → a literal `Label`
   style → Empty reaches labels that never had a binding. No component renders. A member whose new applied
   element needs a capability its view lacks is routed through the ordinary path (§2.11).
6. **Root boundaries.** Child roots made with `CreateChildRoot` (list cells, item content) continue their
   owner's scope. A standalone root (a `ReactorWindow`, a `ReactorPage` the app creates) starts with no
   styles; the app wraps its content in `WithStyles`.
7. **XAML styles on the Controls registry.** Reactor's applied values are local values: they win over the
   app's XAML implicit styles, and unset properties are cleared and show the XAML style (§2.1, §2.1.5). The
   exception is a XAML `Disabled` state, which MAUI ranks above local values (§2.1.3).
8. **Reporting the direct registry's gap.** When a direct view mounts and the app's resources define an
   implicit XAML style for the Controls type it stands for, Reactor warns once per type per app.
9. **Keyed XAML styles are functions** (§2.4).

### 2.8 The Disabled state, own and effective `IsEnabled` (review D7, E4, F2, G3)

MAUI (§2.1.3): a `Disabled` state's setters beat local values; `IsEnabled` is inherited, and a descendant's
own `IsEnabled = true` can't override a disabled ancestor. Reactor matches both, on both registries.

Reactor never writes an inherited `IsEnabled` onto a Controls view's manual layer (review G3): that layer is
shared with the app's own writes, and clearing it on re-enable erases the app's value (§2.1.9 (B)). Instead,
the host tree is treated as segments, and each boundary is bridged by MAUI's own inheritance:

1. **Own enabled.** A host's own `IsEnabled`: the declaration's literal, else its binding, else the style's
   literal, else the style's binding, else unset. A Disabled variant can't set `IsEnabled`: one there is
   ignored and reported once per style entry. On a Controls view the own value, when present, is written as
   a local value like any declared property; when unset, Reactor writes nothing (no `ClearValue` of a value
   it didn't write).
2. **Controls → Controls.** A Controls host below a Controls host is its logical child, so MAUI's
   `IsEnabledCore` inherits; Reactor writes nothing inherited.
3. **Direct → Controls: the bridge** (§2.11.1). The Controls children of a direct layout D are logical
   children of `bridge(D)`, so their `Parent` is the bridge: implicit styles, resources, `AppThemeBinding`s,
   `BindingContext` and enabling reach them through it (§2.1.9 (C), §2.1.10). Reactor writes the bridge's
   `IsEnabled`: D's effective state. This replaces today's `Parent`-only anchoring, which carries resources but
   not enabling (§2.1.9 (C')).
4. **Controls → direct: the same bridge.** When D is a child of a Controls layout H, `bridge(D)` is H's logical
   child at D's slot. It inherits H's enabling from MAUI (XAML styles, app writes, MAUI parents outside Reactor)
   by pulling it (§2.11.1 rule 6), and Reactor observes it: the MAUI part of the bridge's `IsEnabled` (its
   `IsEnabledCore` from H) is the direct subtree's MAUI input. Reactor's own write to the bridge is the direct
   side's state; the bridge's effective value is MAUI's AND of both, so the input Reactor reads is H's own
   `IsEnabled`, never the bridge's (no feedback).
5. **Direct → direct.** Reactor computes and writes: effective = own AND the parent's effective AND, at a
   segment's top, the Controls parent's (or anchor's) MAUI input. Reactor owns direct views, so it writes the effective
   value on every mount, ordinary update, controlled restore and patch (`DirectView.ApplyModifiers` reads the
   applied modifiers' value, no longer `n.IsEnabled ?? true`).
6. **Effective state for the Disabled variant.** A direct host's effective state is rule 5's. A Controls
   host's is MAUI's `IsEnabled` (which includes its own value, the app's writes, XAML styles and the inherited
   state through rules 2 and 3), read after Reactor's own writes and watched through `PropertyChanged`. The
   ancestry crosses components and child roots made with `CreateChildRoot` (cells inherit the list's
   state), not page roots.
7. **The variant.** An effectively disabled host applies its style's `disabled` element: for the properties
   it sets, above the declaration's literals (MAUI's precedence), then the normal rules.
8. **Propagation.** A change of a direct host's effective state, of an adapter's input, or of a watched MAUI
   value raised outside Reactor's own writes runs a Disabled pass over the affected hosts (beneath skipped
   components too): direct views' written `IsEnabled`, adapters' `IsEnabled`, and look patches for variants.
   A host whose variant needs a capability its view lacks is replaced (§2.11).
9. **Themes while disabled.** A theme pass resolves the variant's bindings for disabled hosts.
10. **What this fixes.** The direct registry's missing inheritance (§2.1.3), the XAML-disabled Controls layout
    over direct children (§2.1.8), and the coincident app write (§2.1.9 (B)): Reactor's enabling now behaves
    like plain MAUI's in that sequence, without an ordinary update re-running `Configure` to hide a loss.

### 2.9 Android's theme-derived defaults (review D11, E1, E2)

On Android a view without a value keeps its inflation-time default, and MAUI restores that stale default when
a value is cleared (§2.1.4, §2.1.6). Reactor re-resolves defaults natively; it never remounts and never uses
a hard-coded palette.

1. **Ownership: only when the platform default shows (E1).** A property is repaired only when no source
   supplies a value: on a Controls view, the `BindableProperty` is not `IsSet` (no local value, style, state,
   binding or resource, §2.1.5); on a direct view, Reactor owns every value, so unset in the applied element.
   A XAML style, a themed setter, an `AppThemeBinding` or a `Disabled` state is never overwritten, and a
   Reactor clear back to a XAML style leaves the style's value.
2. **What is written.** The whole `ColorStateList` (every state, enabled and disabled), written natively and
   held out-of-band per view, never in the scalar applied record. The properties: those the gate's per-control
   characterization finds stale (premise 3; so far `TextView` text, `MaterialButton` text and background
   tint; the entry's hint and text follow already).
3. **The reference (E2).** One reference view per recipe and context, built with **MAUI's own creation
   recipe** for the target's handler (`MauiTextView` for a label, `MauiMaterialButton`, which wraps its
   context in `MauiMaterialContextThemeWrapper`, for a button, …) on **the target view's handler base
   context**, its lists read, then disposed at once. The snapshot is keyed by recipe, the context's identity
   (weakly) and the `ThemeSource` generation; a new generation drops the previous snapshots, so the cache
   holds at most one generation and releases contexts that go away. Characterized: in the event, both app-
   and system-started, the references equal fresh handler-created views in every state, for both overlays
   (§2.1.6).
4. **When.** In the theme pass (inside the event), for owned properties of enrolled hosts; when a property
   becomes unset after a theme change (Reactor writes the reference list after MAUI's clear); and after
   MAUI's own mappings, through an appended mapping per repairable key (review F1):
   - **Registration is live ownership.** A host's view is registered at mount, with its handler, for each
     repairable property, and removed at unmount or handler detach; nothing depends on "previously repaired".
   - **Every invocation rechecks**: the view is registered, its handler is the registered one and connected,
     the property is still Reactor-owned (`IsSet` false on a Controls view; unset in the applied element on a
     direct view). Then it resolves the reference for **the current generation** (built if absent, §2.9 rule 3)
     and writes it. This covers a first repair (a view mounted under a `Disabled` setter that releases the
     property after a theme change) and never replays a list from an older generation (§2.1.8 (a), (b)).
   - **A native write**, never `UpdateValue`: the hook runs inside MAUI's mapping and must not re-enter it.
   - It acts only on registered views; an unmounted view's late mapping writes nothing (§2.1.8 (c)).
5. **Cost.** Cold, per recipe, 0.4–2.4 ms in the event; warm 0.1 ms; one snapshot per recipe and context per
   change (§2.1.6). The gate bounds it (§2.14).
6. **Opt-out, startup-only.** `ReactorTheme.ResolveAndroidDefaults` is read when the first root mounts;
   setting it afterwards throws, so a process never mixes repaired and unrepaired views.
7. **Rejected: a built-in default `ElementStyles` palette.** It would hard-code Material's colors (and get the
   app- vs system-started disabled tint wrong, §2.1.6), diverge from the app's own Android theme, and theme
   only the properties it knows.

### 2.10 Registered fonts (review D10, E8)

1. **No new API.** `FontFamily` takes the alias registered with `ConfigureFonts` (portable), the file name with
   or without extension (portable), on Android MAUI's system aliases, or on iOS and Mac Catalyst the font's
   PostScript or family name (not portable; §2.1).
2. **Normalization (E8).** Before evidence is compared, the family is normalized as MAUI's Android font
   manager does (`FontManager.Android.cs` 143–160, 186–193): case-insensitive; `sansserif`, `sans-serif`,
   `serif` and `monospace` and the `sansserif-*`/`sans-serif-*` families (e.g. `sans-serif-medium`) are
   system aliases; a trailing `-light`, `-medium`, `-black` or `-thin` is a weight suffix on Android 9+, so
   evidence is sought for the name and for the name without the suffix.
3. **Evidence first, no lookups that log.** When a family first reaches a handler (both registries), Reactor
   classifies it: **registered** (`IFontRegistrar.GetFont` returns a file); **available** (iOS, Mac
   Catalyst: `UIFont.FromName` or a non-empty `UIFont.FontNamesForFamilyName`, neither of which logs); **an
   asset** (Android: the name, or with MAUI's extensions, in the asset listing read once); **a system alias**
   (Android, rule 2); **a system family** (Android 14+: the view's resolved `Typeface.SystemFontFamilyName`
   equals the normalized family; Reactor reads the view's typeface, it doesn't resolve again).
4. **Reports.** iOS and Mac Catalyst: no evidence means "isn't registered or installed" (definite). Android:
   no evidence and the view renders the system sans-serif means a **suspected fallback**; below Android 14,
   nothing for an unregistered non-asset family (MAUI's own warning remains). Once per normalized family per
   app and font manager; sizes and attributes don't repeat it.
5. **Traits.** iOS and Mac Catalyst ignore `Bold` and `Italic` on a single-face family: reported once per
   family and trait when the resolved font lacks it. Android fakes them; nothing reported.
6. **Styles carry fonts** like any property.

### 2.11 Both registries and eligibility (review D6, E5)

Projection, styles and the theme, Disabled and default passes run in the engine, before or instead of the
handler, so both registries see the same applied values. Two differences remain, both reported: XAML
implicit styles reach only Controls views (§2.7 rule 8), and a XAML `Disabled` state ranks above Reactor's
values on Controls views (§2.7 rule 7).

**Eligibility (E5).** On the direct registry, the direct view or the Controls fallback is chosen at mount
from the union of: the declaration, its current style entry's normal **and** Disabled variants, and **both**
branches of every theme binding in all of them. A view that needs Controls in any of them is a Controls view
from the start, so a theme switch or a Disabled activation never remounts it.

A later change can still need a capability the mounted view lacks: a style replacement (a new entry with a
gradient `Background`), or a Disabled variant from a style that arrived after mount. Reactor doesn't patch
such a host silently. **Routing (review F4):** each host records the backend it was mounted with (direct or
Controls). A style or Disabled pass computes the host's new eligibility envelope (rule above, for the new
style entry and state) and compares it with the recorded backend, applying `HybridHandler`'s rule in both
directions (an envelope that no longer needs Controls returns to the direct view, as removing a declared
Controls-only property would). On a mismatch the engine **replaces the host with a fresh mount lifetime**
(review G2): it unmounts the old host node (marked unmounted for good, its handler contexts retired, its
subtree's components unmounted) and mounts **a new host node** at the same position in the parent's node
children and the same slot of the parent's native children, from **the same declared element instance**
(the new node's `Live` is the declaration; the projected element goes only to the handler). The old node is
never re-marked as mounted: `HandlerContext.Live`, `IsMounted` and posted reports guard on the node's
`Unmounted`, so reviving it would revive retired contexts. Ordinary `Update` isn't used: it returns early on
a reference-equal declaration, and passing it the projected element would make `Live` the projection. The consequences are the
same as declaring it: a new native view, the host's subtree remounted (its components' state reset, effects
cleaned up and run again, refs rebound to the new views); components above it keep their state. This happens
beneath skipped components too (the pass reaches the host without rendering its owners), and the same rule
applies when a parent's disabling activates a Controls-only variant.

#### 2.11.1 The bridge topology (review G1, G3, G4, H1, H2, M1)

One design replaces round 4's separate stand-ins and adapters: **one bridge per direct view that needs one**, a
Reactor-owned logical-only `VisualElement` with no handler and no native view (§2.1.10). It fixes MAUI's
`Layout.Insert` for mixed layouts ([dotnet/maui#40758](https://github.com/dotnet/maui/issues/40758)), carries
enabling between the registries' segments (§2.8), and is the logical parent of Controls views inside direct
layouts.

1. **Who has a bridge.** A direct view D has `bridge(D)` when D is a child of a Controls layout (including the
   `DirectViewHost` anchor that hosts a direct tree in a Controls slot), or when D is a direct layout with a
   Controls descendant reachable through direct layouts. One bridge per direct view, never two.
2. **Logical placement.** A bridge has exactly one logical parent:
   - **slot-aligned** in the Controls layout H when D is H's child: at D's position among H's logical children,
     through MAUI's public `InsertLogicalChild` and `RemoveLogicalChild` (rule 8 chooses the position);
   - **reserved under its direct parent's bridge** otherwise: `bridge(F)` is a logical child of `bridge(D)` when
     F is a child of the direct layout D. A bridge is never a logical child of a Controls layout it doesn't
     occupy a slot in.

   A bridge's logical children are D's Controls children and the bridges of D's direct-layout children, in D's
   slot order. A Controls layout's logical children are its Controls children and its direct children's bridges.
3. **The visual projection: real views only, once, in slot order.**
   - A Controls layout that can hold direct children (every Controls layout the direct registry creates as a
     fallback, and `DirectViewHost`) is a Reactor-owned subclass of the MAUI layout that re-implements
     `IVisualTreeElement`: its children are its **views** in slot order (direct ones included), its parent its
     `Parent`.
   - A direct view's parent is the real view whose view list holds it (`DirectView.GetVisualParent`: its
     Controls layout or its direct parent; `DirectView.cs` 23 changes to the slot owner, not the anchor), and a
     direct layout's children are its views in slot order.
   - A bridge is never enumerated downward. Its own `GetVisualParent` returns the direct view it serves.
   - **The boundary (§2.1.10):** a Controls view inside a direct layout reports its bridge as visual parent
     (MAUI's `GetVisualParent() => Parent`), and the bridge reports the direct layout. Downward walks see each
     real view once; an upward walk from such a view passes one bookkeeping hop before the direct layout.
     Reactor's own upward walks (the scroll bridge, spec 070 §2.6.1) use the slot owner table, not the visual
     parent.
4. **No native peers.** Bridges have no handler; a layout's native children are exactly its views (§2.1.10:
   3 for `[A, D, C]`).
5. **Live inheritance through the logical graph.** `BindingContext` propagates through logical children,
   resources and implicit styles through each element's `Parent` listener, themes through both: none depends on
   the projection, and all reach Controls views inside direct layouts, including a change after mount
   (§2.1.10).
6. **MAUI's propagation, pulled.** MAUI pushes `IsEnabled`, `InputTransparent`, `Window`, `FlowDirection` and
   `Visual` through `GetVisualChildren` (`VisualElement.cs` 1966–1976), which a projected layout no longer
   routes to a bridge. Each bridge therefore watches its logical parent's `PropertyChanged` for those
   properties and re-runs MAUI's own pull-and-push (`OnParentSet`'s base: from the parent, then to the bridge's
   logical children); its own `GetVisualChildren` stays its logical children, so MAUI's push from the bridge
   down is unchanged. Without the pull, `Window` and enabling stop at the bridge (§2.1.10 T3a); with it they
   arrive (T3b). MAUI's coercion (`IsEnabledCore` reads `Parent`) needs nothing: the bridge is the `Parent`.
7. **Lifecycle (review H2).** Every bridge operation runs inside the tree's transaction (§2.6):
   - **Attach.** A direct view entering a slot gets its bridge if rule 1 requires one (created on demand up the
     chain of direct parents), placed by rule 2; a Controls view entering a direct layout D becomes a logical
     child of `bridge(D)` (`AddLogicalChild`, which sets its `Parent`); the slot-owner table records its real
     visual parent.
   - **Detach.** The reverse, for every membership: `RemoveLogicalChild` from the bridge or layout (MAUI clears
     the child's `Parent`), the slot-owner entry removed, the bridge's watch on its parent and Reactor's watch on
     the bridge unsubscribed, its presented-view reference cleared. A bridge left with no role under rule 1 is
     retired (removed from its own logical parent, the same cleanup), recursively up the chain.
   - **Move.** A keyed move within one parent is a detach and an attach of the moved child only; its bridge moves
     with it (slot-aligned bridges re-placed by rule 8). No other membership changes.
   - **Backend replacement** (§2.11, review G2). The old host's memberships are detached as above, with its
     subtree's; the new host is attached at the same slot. A direct → Controls replacement retires
     `bridge(old)`; Controls → direct creates the new view's bridge.
   - **Cell retirement.** A retired cell's tree is unmounted; its direct root's top bridge leaves the cell
     host's logical children and every bridge below is retired.
   - **Anchor retarget.** The anchor (the `DirectViewHost` or Controls layout that holds a direct tree's top) is
     distinct from a Controls child's logical owner (its bridge). Moving a direct tree to another anchor moves
     only the top bridge (removed from the old anchor's logical children, inserted at the new slot); Controls
     views keep their bridge as `Parent`, and MAUI re-subscribes the bridge's resource listener when its own
     parent changes. `SetControlsAnchor`/`AnchorChild` (`DirectLayouts.cs` 98–129) no longer set Controls
     children's `Parent`, and `DirectLayout.Detach`'s clear of a child parented to the anchor (131–138) becomes
     the bridge detach above.
   - **After any of these**, no live element keeps a retired bridge or retired control as a logical child or
     parent, nothing propagates to them (their watches are gone and they're out of every logical list), and
     they are collectable.
8. **Insertion by probe (review M1).** The slot placement doesn't assume MAUI's `Layout.Insert` algorithm. At
   the first mixed layout, a one-time probe on a scratch layout (a non-`Element` view, then an `Element` A, then
   an `Element` E inserted at view index 1) reads the logical order:
   - `[A, E]`: **view-index semantics** (MAUI 11 today). Bridges are slot-aligned, so the view index is also a
     valid, correct logical index and `Insert` neither throws nor misorders.
   - `[E, A]`: **Element-count semantics** (#40758 fixed: the logical index is the number of `Element` views
     before the index). Bridges then live in a **reserved tail** after every `Element` child, so MAUI's count
     ignores them and places `Element`s exactly; padding would make MAUI insert before a bridge (with `[bridge(D),
     A]`, at logical 0 instead of 1).
   - Anything else (a throw, a missing logical child): reported once, and Reactor falls back to the tail
     placement, verifying each insertion's logical result.

   Logical order inside a projected layout has no visible effect (enumeration uses views; `BindingContext`,
   resources and propagation don't depend on order), so either placement is correct; the probe only keeps
   `Insert` from throwing and `Element`s ordered.
9. **Operations.** An insertion is one `Layout.Insert` (one native add) and, for a direct child, one bridge
   placement; a removal one `Layout.RemoveAt` plus the bridge detach; a keyed move the moved child's removal
   and insertion only. Unaffected siblings are never removed, re-added or detached (§2.1.9: re-adding the
   suffix detaches them, dropping focus, selection and the keyboard). Each affected position is touched at
   most once per sync; a batch of N insertions is N native adds; a reverse of N is at most N−1 moves, as for an
   all-Controls layout.
10. **Layouts with only Controls children** keep MAUI's types and behave as today; on the Controls registry no
    bridge exists.

### 2.12 Census and the styleable inventory (review D8, E3)

**The common-look census moves into 4d-1.** The property census excludes `ViewElement.Modifiers`
(`PropertyMatrix.cs` 348–365). 4d-1 adds the modifiers that are look properties: `BackgroundColor`,
`Background`, `Opacity`, `IsVisible`, `IsEnabled`, `Rotation`, `Scale`, `TranslationX`, `TranslationY`,
`ZIndex`, `Shadow`, `Clip`, `InputTransparent`, `Margin`, `Width`, `Height`, the minimums and maximums,
`HorizontalOptions` and `VerticalOptions`. Each gets the matrix's set, update, equal-render and clear steps on
both registries, a themed step where a typed overload exists, and a style step.

**The inventory.** Every element property is classified, and the census fails on an unclassified one:

| Class | Properties | Presence |
|---|---|---|
| Look: common (`LookProperties.View`) | the modifiers above | `null` = unset |
| Look: text (`LookProperties.Text`) | `FontSize`, `FontAttributes`, `FontFamily`, `TextColor`, `CharacterSpacing` (every text element) | `null` |
| Look: per control | Label: `TextDecorations`, alignments, `LineBreakMode`, `MaxLines`, `LineHeight`, `Padding`, `TextTransform`, `TextType`; Image: `Aspect`; BoxView: `Color` (positional, nullable), `CornerRadius`; ActivityIndicator: `Color`; ProgressBar: `ProgressColor`; Border: `Stroke`, `StrokeThickness`, `CornerRadius`, `Padding`; Button: `CornerRadius`, `BorderColor`, `BorderWidth`, `Padding`; ImageButton: `Aspect`, `CornerRadius`, `Padding`; Entry: `PlaceholderColor`, `HorizontalTextAlignment`, `ClearButtonVisibility`; Editor: `PlaceholderColor`; CheckBox: `Color`; Switch: `OnColor`, `ThumbColor`; Slider: the track and thumb colors; layouts: `Padding`, the flex enums; ScrollView: scroll bar visibilities; RefreshView: `RefreshColor`; IndicatorView: the colors, `IndicatorSize`, `IndicatorsShape`; shapes: `Fill`, `Stroke`, the stroke settings, `Aspect`, `RadiusX`, `RadiusY`, `FillRule`; ContentView: `Padding` | `null` |
| Look: layout spacings | `VStack.Spacing`, `HStack.Spacing`, `Grid.RowSpacing`, `Grid.ColumnSpacing` | a presence bit set by `init` (§2.4); public `double` unchanged |
| Look, theme values only (pages and Shell aren't `ViewElement`s, so not styleable) | Page: `BackgroundColor`; NavigationStack: `BarBackgroundColor`, `BarTextColor`; Tabs: the three colors; Shell: its eleven colors | `null` |
| Excluded: presence can't be told | RadioButton `IsIndicatorVisible`, IndicatorView `HideSingle`, CollectionView and CarouselView `ItemSpacing`, swipe modes (non-nullable, not look-critical: the presence pattern can extend to them later), `RoundRectangle.CornerRadius` (positional, non-nullable) | listed with the reason |
| Not look | content (text, sources, items, children, slots, inlines, data), controlled values, events, behavior (`Keyboard`, `IsReadOnly`, `MaxLength`, `Orientation`, …), identity and placement modifiers (`AutomationId`, grid and flex placement), `Style`, `StyleClass`, `Configure`, `Ref`, semantics | — |

**The spacing cases (E3).** For each spacing: unset with a style of 8 applies 8; explicit 0 with that style
keeps 0; `with { Spacing = 0 }` on an unset element makes it present (and unequal); removing the style entry
returns an unset spacing to MAUI's default (0 written); a declaration going from explicit 8 to unset under no
style returns to 0; `with` copies presence; equality distinguishes present 0 from unset; under an unchanged
style of 8, explicit 0 → unset → explicit 0 writes 0, 8, 0 (F5); the descriptor's clear resets the bit (a
`with { Spacing = 0 }` doesn't).

**Mutants the census must catch.** Omitting a common field from the fieldwise modifier merge (`Opacity`);
omitting a per-control descriptor (`Entry.PlaceholderColor`); a descriptor whose native writer differs from
the handler's ordinary write (the census runs both and compares); a spacing presence bit not set by `init`
(explicit 0 under a style shows 8).

### 2.13 Tests

Each oracle names the mutation that must turn it red; every zero-write and no-warning oracle has a positive
control in the same test. Headless tests use the theme seam, a stub `ThemeSource`, the stub handlers' write
counters, and counters of `Update`, `Configure`, restore, invalidate-measure and redraw calls. **Write counts
are kept apart (review F6):** *mapper invocations* (MAUI mapping a key, whoever caused it), *primary patch
writes* (a descriptor write by a theme, style, Disabled or default pass) and *corrective writes* (the Android
hook's native writes after MAUI's mapping). An oracle names which count it constrains; "zero writes" never
sums them. Device tests
run on Mac Catalyst, iPhone, iPad and Android, `[controls]` and `[direct]`; their waits accept only
observations made after the action (a frame index or sequence number taken at the action).

**Headless: theme values and the look patch.**

| Test | Oracle | Mutation |
|---|---|---|
| Projection at mount | Light: the light value; Dark: the dark value | always light |
| Literal wins, both orders | `.TextColor(red).TextColor(themed)` and the reverse apply red; a switch writes nothing to `TextColor` (positive: a themed `BackgroundColor` on the same element is written) | last call wins |
| `with` is a literal | `with { TextColor = red }` beside a binding applies red | binding over field |
| Literal removal | a render without the literal applies the binding | keep the stale literal |
| Switching while overridden | two switches: zero writes to it; after removing the literal, the next switch writes it | patch overridden properties |
| One binding per property | two `.Themed` calls on one descriptor: the second's values; a typed overload then `.Themed` on its descriptor: one binding, the later | key by delegate or call site |
| A literal over a themed style setter | red, and a switch writes nothing to it (positive: an unoverridden themed setter is written) | style binding over declared literal |
| Look patch only | a switch on a themed view with a controlled value's native drift pending, a `.Set(...)` counter and a themed `GraphicsView`: drift not restored, `Configure` 0, redraw 0, `Update` 0; positive: an ordinary render restores, configures and redraws once | route the patch through `Update` |
| Invalidation narrowed | a themed font size: one measure invalidation; a themed color: none (positive: the font size case) | invalidate on every patch; never invalidate |
| Declared vs applied | during and after a switch `Live` returns the declared instance; the next ordinary update diffs from the patched applied element (an unchanged themed value writes nothing) | `Live` returns applied; applied not updated by the patch |
| No renders | a switch: render counts unchanged (positive: a state change renders once) | re-render on the event |

**Headless: the theme authority, transactions and dispatchers.**

| Test | Oracle | Mutation |
|---|---|---|
| Last announced theme | stub: announced Dark while `RequestedTheme` reads Light: patches and later mounts use Dark | read `RequestedTheme` |
| Before the first announcement | a source subscribed after a stub divergence starts from `RequestedTheme` | — (boundary documented) |
| Divergence diagnostic | a divergence settled after two frames with the generation unchanged: one report; a trait still old at the event but new at frame 1: no report; a newer announcement before the check: no report (positive: the first case) | report at the event; ignore the generation |
| Flip inside the pass | the stub source announces during the pass: the pass re-runs, every node ends on the newest theme, each patched at most twice | mark applied before settling |
| During first mount | a change while the first commit mounts: later nodes mounted with the new theme, earlier ones patched at the end, none twice | nested pass |
| A recycled cell | a cell rebinding A→null→B (with and without `ReuseItemTrees`) during a flip: no write to the retired tree, B on the new theme | live list, no liveness check |
| A boundary removes later nodes | a write throws; the boundary unmounts later enrolled nodes: no write to them; one error | no liveness check |
| A later switch after a throw | the failed node is patched next time | drop it |
| Two windows, two dispatchers | roots on the announcing dispatcher patched in the event; a root on another stub dispatcher patched by a posted pass, after the event; one closed during notification: no write | patch every root synchronously |
| Background announcement | a stub announcement from a non-UI thread: no write off the root's thread; the posted pass patches | write on the announcing thread |
| Unmount | after unmount the source's subscriber count for the root is 0 (direct proof) and the root is collectable (a `WeakReference` after GC); positive: a live root is counted | rely on MAUI's weak event manager |
| Atomic snapshot (F3) | a stub source that publishes between a reader's two field reads (a controlled interleaving hook): the reader still pairs theme and generation of one announcement (it read one reference); a pass started before a publication settles on the newer generation | separate theme and generation fields |
| Late, older notification (F3) | a posted wake-up for generation 1 arriving after a synchronous pass applied generation 2: no primary writes, the pending generation stays 2 (positive: a wake-up for a newer generation runs a pass) | take the notification's generation; lower the pending one |

**Headless: styles, Disabled, eligibility, spacing, inventory.**

| Test | Oracle | Mutation |
|---|---|---|
| Fill-unset census | every inventory descriptor of every type: style fills it when unset, the declaration keeps it when set; common modifiers fieldwise | omit `Opacity`; omit `Entry.PlaceholderColor` |
| Spacing presence | §2.12's spacing cases | presence bit not set by `init`; `with` not copying |
| Per-type fall-through | outer `{Label, Button}`, inner `{Label}`: buttons use the outer; inner `Remove<Label>()`: labels use the outer | inner hides outer |
| Membership reaches every host | Empty → a literal `Label` style → Empty, beneath a skipped component: the labels (no bindings, variants or defaults) take the style and return to their declaration; zero renders (positive: the same instance writes nothing) | restyle only theme-enrolled hosts |
| Root boundaries | a cell uses the list's scope; a standalone `ReactorPage` without `WithStyles` uses none | inherit across standalone roots |
| MAUI's own enabling (F2) | a Controls `Button` disabled by its XAML style (stub `IsEnabled` false), `ElementStyles` normal teal / disabled gray: the gray variant; the stub turning true outside Reactor's writes: one Disabled pass, teal (positive); a `PropertyChanged(IsEnabled)` raised by Reactor's own write: no pass | effective from Reactor's declaration and style only; ignore outside changes; react to own writes |
| The coincident app write (G3) | a Controls child under a Reactor-disabled parent (Controls → Controls, and Direct → Controls through the adapter): the app writes the child's local `false` (`.Set` run once, then no ordinary update), the parent re-enabled: the child stays disabled, as in plain MAUI (§2.1.9 (A)); Reactor's manual-layer write count on the child is 0 (positive: a declared `.IsEnabled(false)` writes once) | write the inherited state on the child's manual layer and clear it |
| The adapter (G3) | a direct layout disabled → its Controls children disabled through the adapter (MAUI's `IsEnabledCore`), with zero writes to the children; re-enabled → enabled; their implicit styles and `AppThemeBinding`s still resolve through the adapter; nested direct layouts chain adapters | anchor by `Parent` only (no inheritance); write the children |
| The bridge as input (F2, G3) | a Controls layout disabled by its XAML style (stub) over direct descendants: they are written disabled, including after a Reactor parent disable → re-enable and an unrelated child render; the layout's `IsEnabled` changing outside Reactor re-propagates both ways through the bridge's pull (positive: an enabled layout leaves them enabled); Reactor's write to the bridge never reads back as the input (the input is the layout's own `IsEnabled`) | direct descendants from Reactor state only; read the bridge's own value as input |
| Own vs effective enabled (direct) | a direct child's state update while its parent stays disabled: written disabled; a controlled restore under a disabled parent: written disabled; an own-disabled value cleared under a disabled parent, then the parent re-enabled: enabled; an explicit child `IsEnabled(true)` stays disabled under the parent | write own; `n.IsEnabled ?? true` on direct views |
| A XAML-disabled anchored child | a Controls child whose XAML style sets `IsEnabled = false`, under a direct layout disabled then re-enabled: Reactor writes nothing to the child, which stays disabled (positive: a child without the style is enabled) | write or clear the child's value |
| `IsEnabled` in a variant | ignored, one report | apply it |
| Disabled precedence | an effectively disabled button: the variant's background above the declared literal; enabled: the literal | literal over variant |
| Propagation | disabling a parent patches descendants' variants below a skipped component and in cells; re-enabling restores | own state only |
| Style-supplied `IsEnabled(false)` | the style disables: variant applied | ignore styled `IsEnabled` |
| Theme while inherited-disabled | a switch resolves the variant's bindings | resolve the normal style |
| Eligibility union | a binding needing Controls only in Dark, and one only in Light; a Disabled variant needing Controls: a Controls view from mount; switches and disabling keep native identity, a child's hook state, its effect (no cleanup) and its ref | decide on the current theme or state |
| Late capability, routed | a style replacement adding a gradient to a mounted direct button, beneath a skipped component; a parent disabling activating a Controls-only variant that arrived after mount: the host is replaced through `CanUpdate` (new native identity, the host's subtree remounted: child state reset, cleanup ran, ref rebound), owners above keep state, zero renders of the skipped owner (positive: a patchable change keeps identity) | patch silently; re-render the owner |
| Routing in place (F4) | the same declared instance across renders: a later style entry needing Controls replaces the direct host (new native identity, the subtree remounted, the slot unchanged among its siblings, `Live` the same declared instance); removing the entry replaces it back with a direct view; then ordinary renders keep identity, theme toggles and Disabled toggles keep identity and write patches only | route through ordinary `Update` (returns early on the equal declaration); pass the projected element as `Live`; lose the slot |
| Fresh mount lifetime (G2) | keep the old node's handler context and native control; replace the backend direct → Controls and back: an event raised on the old control reaches no callback, a report queued by the old context before the replacement is dropped when drained, and the old node stays `Unmounted`; positive: the new node's event reaches the current callback once | re-mark the old node mounted; reuse its context |
| Mixed insert (G1, M1) | a Controls layout's children in every order of direct and Controls views, inserted, removed and moved by key, **under both insertion algorithms** (an injectable insertion strategy simulating MAUI's view-index semantics and the Element-count semantics of a fixed #40758, each driving a view list and a logical list): nothing throws, the views' order matches, `Element`s keep their relative logical order, bridges are slot-aligned under view-index and in the reserved tail under Element-count, and **no unaffected sibling is removed or re-added** (native child-operation counter) | `Layout.Insert` at the view index; re-add the suffix; pad under Element-count (an `Element` lands before a bridge) |
| The probe (M1) | the one-time probe classifies the stub's view-index algorithm, its Element-count algorithm, and a throwing one (reported once, tail fallback with per-insert verification); it runs once per process (counted) | assume today's algorithm |
| Batched operations (G4) | a direct prefix and a long Controls suffix: N insertions are N native adds; a keyed move is one remove and one add of the moved child; a reverse of N is at most N−1 moves; each affected position is touched at most once per sync (counter per position) | touch a position twice; re-add siblings |
| Projection (H1) | §2.1.10's tree headlessly: every real view enumerated once in slot order, no bridge enumerated, D's and E's visual parents their slot owners, B's its bridge whose parent is D (the documented boundary) | enumerate logical children; a direct view's parent the anchor |
| Bridge lifecycle (H2) | with the enclosing root alive, each of: a Controls child removed from a direct layout; a direct child removed from a Controls layout; keyed moves; reparenting by anchor retarget; backend replacement both ways; a cell retired (recycled, with and without `ReuseItemTrees`): the old memberships are gone from every logical list, the retired bridge has no parent, no logical children, no presented view and no watches; toggling the old parent's `IsEnabled` and `BindingContext` reaches nothing retired (pull counter, binding-context value); the retired bridges and controls are collected (`WeakReference` after GC; positive: a live bridge's reference survives) | keep a membership; keep a watch; anchor = owner |
| Inventory | every element property classified | — |

**Headless: Android defaults (stub platform) and fonts.**

| Test | Oracle | Mutation |
|---|---|---|
| Ownership | a Controls view with an implicit style, an explicit style, a themed setter, an `AppThemeBinding` (stub `IsSet` true): not written; none (`IsSet` false): written; a direct view unset: written | repair every unset-in-Reactor property |
| Clear back to a style | a Reactor override cleared over a XAML style: not repaired (positive: cleared with no style: repaired) | repair after every clear |
| Whole lists | the written default carries the disabled state: enabled → disabled → enabled shows each state's color; the applied record is unchanged | write the scalar default |
| Hook: a first repair (F1 a) | a view mounted under a `Disabled`-only setter (stub `IsSet` true while disabled), a theme change while disabled (0 corrective writes), enabled: one corrective write of the current generation's reference (positive: the same view with the opt-out: 0) | gate on "previously repaired" |
| Hook: no retained list (F1 b) | repaired in Dark, the setter takes over, a change to Light while it holds (0 corrective writes), released: the Light reference written | replay the retained list |
| Hook: liveness (F1 c) | unregistered (unmounted) before MAUI's mapping: 0 corrective writes; a replaced handler: 0 to the old view; a registered view: 1 (positive) | keep unmounted views; no handler check |
| Hook: no re-entry | the hook makes no `UpdateValue` calls (counted) and no mapper invocation follows its write | write through `UpdateValue` |
| Snapshot key | two contexts (activity, Material wrapper) give two snapshots; a new generation drops them; reference views disposed (counted) | key by class and configuration |
| Startup-only opt-out | setting it after the first mount throws; off: nothing written (positive: on writes) | read dynamically |
| Font normalization | `sansserif`, `sans-serif`, `sans-serif-medium`, `serif`, `monospace`, `OpenSansRegular-medium` (the alias plus a weight suffix): no report; `NoSuchFont`, `NoSuchFont.ttf`, `no such font`: one report each, once | compare raw names |
| Font evidence | registered alias: no native lookup (counted); iOS PostScript name: available; missing: definite on iOS, suspected on Android 14+, none below | sentinel or fallback equality alone |
| Sizes and traits | one classification per family across sizes and attributes; iOS Bold on a single face: one trait report; Android: none | report per size; never report traits |
| XAML gap | a direct label under an app-level implicit `Label` style: one warning; a second: none; Controls registry: none; no style: none | per view; never |

**Device.**

| Fixture | Oracle |
|---|---|
| `Theme_SameFrame [controls|direct]` | a Reactor-themed label, a MAUI `AppThemeBinding` label and a `UseAppTheme` probe: the themed label's new color at the same frame index as MAUI's (0); the probe (later on Android), observed by a frame callback, is the sampler's positive control |
| `Theme_LookPatchOnly [controls|direct]` | mapper hook: a switch maps each themed property once per view and nothing else; a themed Entry with drift isn't restored; positive: a `Text` change maps once |
| `Theme_LiteralWins [controls|direct]` | both orders, `with`, removal; a switch while overridden changes nothing on the platform (pixels), the themed sibling changes |
| `Theme_Divergence` | a nested set during the change: Reactor's views show the announced theme, the report arrives once after the settled observation; a normal change reports nothing (positive control) |
| `Theme_SystemAppearance`, `Theme_ForcedTheme` (host-driven, iPhone, iPad, Android) | platform-started changes land in MAUI's frame; iOS's stale theme after a forced theme is reported |
| `Theme_Eligibility [direct]` | a binding or variant needing Controls in one state: identity unchanged across switches and disabling; a late style adding one: replaced like a declared change |
| `Styles_Parity` | `ElementStyles` for Label and Button: native font, size, color and width equal on both registries and equal to a plain MAUI view under the equivalent XAML style; override wins, removal shows the style, a switch restyles |
| `Styles_Scopes` | nested scopes fall through per type; restyling patches without rendering (render counter), reaching unthemed labels |
| `Spacing_Presence [controls|direct]` | a style's spacing on an unset stack (geometry), an explicit 0 kept, style removal back to 0 |
| `Disabled_Inherited [controls|direct]` | a disabled parent: children disabled natively and showing the variant, including an anchored Controls child and an explicit-enabled child; a child state update under it stays disabled; re-enabled: back; a XAML-disabled anchored child stays disabled |
| `Android_RepairHook [controls|direct]` (Android) | §2.1.8's cases (a), (b), (c) with the real hook: the current generation's default after release, never the retained list, nothing after unmount; corrective writes counted apart from mapper invocations |
| `Disabled_MauiSource [controls|direct]` | a XAML-disabled Controls button shows the `ElementStyles` Disabled variant; a XAML-disabled Controls layout disables its direct descendants (native, through the bridges), also after a Reactor parent disable → re-enable and an unrelated child render; the source changed outside Reactor re-propagates both ways |
| `Bridges_Topology [direct]` | §2.1.10's H → D (interleaved Controls and direct children) → F (nested), through Reactor with the real overrides: the runner's traversal lists every real view once in slot order and no bridge; D's parent is H; B's upward walk reaches D through its bridge; a `BindingContext` changed after mount reaches B and G; H's implicit style and a theme change reach them; H disabled disables them (Window set, `IsLoaded` true); H's native children are exactly its views |
| `Bridges_Lifecycle [direct]` | the H2 cases on device: after each, the runner's traversal shows no retired view, the old parent's toggles reach nothing retired, and the retired bridges and controls are collected |
| `Layout_MixedInsert [direct]` | a Controls layout with a direct child before a Controls child mounts, and survives insertions and moves (it threw before); a focused `Entry` after the insertion point keeps focus, selection, the keyboard and first-responder status, gets no `Unloaded`/`ParentChanged`, and (Android) is never detached; native and handler identity unchanged |
| `Enabling_CoincidentWrite [controls|direct]` | §2.1.9's (A) sequence through Reactor on both registries, Controls → Controls and through the adapter: the app's local `false` survives the parent's re-enable, as in plain MAUI |
| `Replacement_Lifetime [direct]` | a late style forcing Controls and its removal: the old native's events reach nothing after each replacement; the new one's work |
| `Android_Defaults [controls|direct]` (Android; Apple as reference) | after app- and system-started changes: owned properties show the in-event references (enabled and disabled), cleared colors too; implicit styles, explicit styles and themed setters keep their values; a Reactor override cleared to a XAML style shows the style; a `Disabled`-only setter unapplied shows the reference; with the opt-out, MAUI's stale behavior (the positive control) |
| `Fonts_Registered`, `Fonts_Evidence` | spellings render the face (width equals plain MAUI's, differs from default); missing families: definite on iOS, suspected on Android 14+; the system aliases not reported; Reactor adds no MAUI warnings (log capture count) |

### 2.14 Gate

Hard premises, characterized first in the implementation; if one fails on a device, stop and report:

1. **The same frame.** Reactor's in-event look patch shows at the same frame index as MAUI's bindings on all
   four devices for app-started changes, and on iPhone, iPad and Android for platform-started ones.
2. **Look patch only.** A switch produces one primary patch write per themed property per view, one mapper
   invocation each and no other mapper invocation, and runs no `Update`, `Configure`, restore or redraw;
   measure is invalidated only by size-affecting properties. Corrective writes are counted separately and
   occur only for Android-owned defaults (F6).
3. **Android defaults.** For every Android platform class Reactor renders, a per-control characterization
   (app- and system-started, inside the event) lists which defaults go stale; the resolver covers exactly
   those, each reference equals a fresh handler-created view in every state, and no property with a MAUI
   source is written. The hook reproduces §2.1.8's (a), (b) and (c).
4. **Disabled.** Reactor's effective state and variant match plain MAUI's §2.1.3 cases on both registries, and
   MAUI's own enabling (§2.1.8) reaches Reactor's effective state, direct descendants included.
5. **Font evidence.** Every spelling of §2.1 and every alias of §2.10 rule 2 is classified as stated, with no
   extra MAUI warnings.
6. **Performance (E7).** `Reactor.Maui.Benchmark` (1,000 rows, both registries, all four devices):
   - **Method.** Baseline (`1a8d88c6`) and candidate builds run paired and interleaved (B, C, B, C, …), at
     least 9 pairs after 2 warm-ups. Noise is calibrated first by running baseline against baseline the same
     way; the tolerance for a metric is the larger of 3% and twice the calibrated baseline-vs-baseline
     spread of the paired median difference.
   - **Metrics, gated separately:** render (Reactor's reconcile and handler writes), layout (forced native
     measure and arrange), and managed allocations.
   - **Positive and negative controls (F6).** A candidate with injected overhead in the projection path (a test
     switch) sized at three times the tolerance plus the calibrated noise must fail the gate, so a failure is
     never marginal; and baseline against baseline must pass it (no false failure). Without both, the gate's
     power is unproven and it doesn't pass.
   - **Unstyled, unthemed tree:** each metric within tolerance of the baseline.
   - **Styled and themed tree** (an app-wide `ElementStyles` for Label and Button with themed colors) against
     the same visuals declared as literals: render and allocations within 10%, layout within tolerance.
   - **A theme switch** of the styled tree: lands in MAUI's frame; its time within 1.2× MAUI's own
     `AppThemeBinding` switch over the same views (§2.1's cost probe).
   - **Android's resolver cold:** at most 5 ms added to the first switch's pass, 1 ms warm, per window.
7. **Mixed layouts (G4).** A mixed-layout benchmark on all four devices, both registries, run and gated like
   premise 6 (paired, noise-calibrated, injected-overhead and baseline-vs-baseline controls): a direct prefix
   and a 500-view Controls suffix; batch insertions (50 at scattered positions); keyed moves (50); a reverse of
   500. Against the same edits on an all-Controls layout: native child operations equal (counted), each
   affected position touched at most once per sync, no unaffected sibling removed or detached, and render,
   layout and allocations within tolerance; mounting with bridges within tolerance of mounting without them
   (§2.1.9 measured S2 ≈ bare).
8. **The bridge topology (H1, H2).** Through Reactor's own bridges and projected layouts, on all four devices:
   §2.1.10's T3b results (each real view once in slot order, no bridge enumerated, live `BindingContext`,
   styles, themes, enabling and `Window`, no native peers), and every H2 lifecycle case leaving nothing retired
   in a logical list, reachable by propagation, or uncollected.

Then: full device suites on all four devices with the look fixtures; the Release build of `Reactor.Maui.slnx`
with no warnings; the mutants above; spec 068's rows (the theme value ✅, styling direct views ✅, fonts ✅ for
registration and rendering); README caveats (the reported MAUI divergences, XAML styles on direct views,
Android defaults' limits).

### 2.15 Decisions to review

1. **`AppThemeValue<T>`** (D1), MAUI-side; WinUI's `ThemeRef` is a different abstraction.
2. **Literal always wins**, independent storage (D2), as in WinUI.
3. **Descriptors** (`LookProperty`) key bindings, styles and the look patch (D3); `.Themed` takes a
   descriptor; handlers' look writes go through them.
4. **The look patch** never runs `Update`; declared, applied and native are distinct (D4); invalidation is
   narrowed to size-affecting properties (E7).
5. **The authority is the last announced theme** (D5, E9), with a documented boundary after a suppressed
   nested set (no choice agrees with everything, §2.1.7) and a generation-checked, settled divergence report.
6. **One transaction per tree**, deferred and settling passes (D5); **each root patched on its own
   dispatcher**, synchronously when that is the announcing thread (E10); **an atomic `(theme, generation)`
   snapshot**, notifications that only wake, and generations that never regress (F3).
7. **Eligibility from the union** of normal and Disabled variants and all theme branches (D6, E5); a later
   capability change replaces the host with **a fresh node** at the same slot from the same declared
   instance, with the consequences of declaring it, in both directions (F4, G2).
8. **MAUI's Disabled precedence and inheritance** (D7); **own and effective enabled** computed apart (E4);
   **enabling bridged by MAUI's own inheritance** (F2, G3): Reactor never writes an inherited value on a
   Controls view's manual layer; one bridge per direct view carries a direct layout's state to its Controls
   children and a Controls layout's state (XAML, app writes) to its direct children (G3, H1).
9. **The direct registry gets inherited `IsEnabled`**, fixing §2.1.3's defect in 4d-1.
10. **The common-look census and the inventory in 4d-1** (D8); **spacing presence** by a private bit behind the
    existing `double` (E3): no binary or source break; an internal clear for style merging (F5).
11. **Style scopes**: per-type fall-through, resolved per scope, **membership separate from the theme set**
    (D9, E6).
12. **Font evidence** with MAUI's alias normalization, never a sentinel; "suspected" on Android (D10, E8).
13. **Android defaults**: a native resolver, on by default, startup-only opt-out, gated on `IsSet` ownership,
    whole state lists out-of-band, references by MAUI's recipes on the target's context, re-applied after
    MAUI's clears by a hook gated on live ownership and resolving the current generation at each
    invocation (D11, E1, E2, F1).
14. **Reports**, once each: XAML styles ignored by direct views, theme divergence, font evidence, traits, an
    `IsEnabled` in a Disabled variant.
15. **One bridge topology** (G1, G4, H1, H2, M1): one logical-only bridge per direct view that needs one,
    slot-aligned in a Controls parent or reserved under its direct parent's bridge; projected Controls layouts
    that enumerate views; bridges that pull MAUI's propagation; a defined lifecycle; slot placement chosen by a
    one-time probe of `Layout.Insert`'s semantics. MAUI's `Layout.Insert` works without detaching any sibling.
    Rejected: re-adding the suffix (it detaches siblings: focus, selection and keyboard lost; O(n)), Controls
    wrappers (60–90% slower mounts on Apple's platforms, placement moves to the wrapper, and the wrapper's visual
    parent is again its logical parent), and round 4's separate stand-ins and adapters (bookkeeping in the
    visual tree, direct views missing from it, Controls children reachable twice). Upstream:
    [dotnet/maui#40758](https://github.com/dotnet/maui/issues/40758).
16. **The visual-parent boundary** (§2.1.10): a Controls view inside a direct layout reports its bridge as
    visual parent, because MAUI fixes a Controls view's visual parent to its logical parent; documented, and
    Reactor's own upward walks use the slot-owner table.

### 2.16 Open questions

- A subtree theme override, like WinUI's `.RequestedTheme(...)`.
- An opt-in fallback to Controls for direct views under XAML implicit styles.
- Theme values and styles in rich-text runs (a label's `Inlines`).
- Styles for pages and Shell (theme values are supported, styles aren't).
- Whether the Disabled variant should also cover other system states once 4e brings pointer and focus.
- Font family evidence below Android 14.
- The presence bit for the excluded non-nullable properties (item spacings, `HideSingle`, …).
- Mac Catalyst's platform-started theme change and forced-theme behavior (uncharacterized, §2.1).
- A window on a thread other than the main thread (Windows, 4g) is designed (§2.6 rule 6) but untested here.
- Whether MAUI would accept an extension point for an `Element`'s visual parent (removing §2.1.10's one
  bookkeeping hop); not proposed upstream without approval.
- Accessibility focus across an insertion isn't measured directly (no automated screen reader); S2 performs no
  native removal or detach on siblings, which is the path that clears it (§2.1.9).
- MAUI issues (one filed; the rest are listed here only, and filing them waits for approval):
  - a `UserAppTheme` set during a theme change is suppressed and leaves MAUI inconsistent, with split
    bindings (§2.1.1, §2.1.7);
  - iOS applies a stale platform theme when returning to Unspecified after the system changed while forced
    (§2.1.2);
  - Android keeps inflation-time defaults and restores them from a cache after a theme change (§2.1.4,
    §2.1.6);
  - an implicit style removed from `Application.Resources` stays applied;
  - filed: [dotnet/maui#40758](https://github.com/dotnet/maui/issues/40758), `Layout.Insert` passes the view
    index to `InsertLogicalChild`, throwing or misordering when non-`Element` children precede it (§2.1.8);
  - iOS ignores `Bold` and `Italic` on a single-face registered family silently.

### 2.17 Out of scope

- `ResourceDictionary`, `StaticResource`, `DynamicResource` (spec 068: ⛔); `StyleClass` and CSS.
- Visual states other than `Disabled`; triggers.
- Dynamic Type and font auto-scaling.
- Windows (4g).
