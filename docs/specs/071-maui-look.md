# Spec 071: MAUI look (phase 4d)

Phase 4d gives Reactor's MAUI renderer the look features spec 068 lists under "Look and theme" and the
remaining 🧪 modifiers: a theme value applied without rendering, styling for direct views, registered
fonts, animations, flow direction, `AutomationProperties`, and tests for every untested modifier. It
stacks on 4c (spec 070), whose head is `1a8d88c6`.

§1 is the outline of the whole phase. §2 is 4d-1's design: characterized on devices, revised after the
design review of `bc46b5ea` (its decisions D1–D12 are cited where they apply), not yet built.
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

The design review of `bc46b5ea` (two model families) asked for twelve changes. Four needed devices first
(re-entrant theme sets, the Disabled state's precedence, Android's stale defaults, and the
instrumentation), so §2.1 gained §2.1.1–§2.1.4 and the rest of §2 was rewritten on them.

### 2.1 Characterization (device, `LookCharacterization.cs`, `LookReviewCharacterization.cs`)

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

- its owner element type and value type, and its **presence**: whether a declaration has a value (§2.12's
  inventory: `null` means unset for nullable properties);
- how to read, set and clear it on the element record (typed, no reflection);
- how to write it to each registry's native view: the Controls view's `BindableProperty`
  (`SetValue`, or `ClearValue` for unset), and the direct view's field and the handler key it re-maps;
- whether a value needs a Controls view on the direct registry (§2.11);
- on Android, whether its platform default is theme-derived and how to re-resolve it (§2.9).

The handlers' own property writes for look properties go through the same descriptors, so the ordinary
update path and the look patch below can't diverge (the census runs both, §2.12).

Theme bindings, style merging and the look patch are all keyed by descriptor: one binding is one property.

**Three elements per host node (review D4).**

- **Declared**: `node.Element`, what the app rendered. The reconciler, `HandlerContext.Live`, callbacks and
  every controlled-value rule use it (the carousel reads `Live.Position`; the echo contract restores from
  it). It is never projected.
- **Applied**: the declared element merged with its style and with every theme binding resolved for the
  current theme, as plain values. The handler receives it in ordinary mounts and updates and diffs
  applied-to-applied. Handlers read look values only from the element they're given, never from `Live`.
- **Native**: what the platform views hold. After a look patch the node's applied element is updated
  through the descriptors to the values the patch wrote, so the next ordinary update diffs from what is
  really on the view.

**The look patch.** A theme pass, a style pass, an `IsEnabled`-driven Disabled-variant change (§2.8) and
Android's default re-resolution (§2.9) write *only the affected look properties*, through their
descriptors' native writers, in the root's echo scope. A look patch never runs the handler's `Update`:
not `Configure` (`.Set(...)`), not a controlled value's restore against native drift, not a
`GraphicsView` invalidate, not child reconciliation.

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
    /// <paramref name="disabled"/>'s apply while the element is effectively disabled (§2.8). Replaces an earlier entry for the type.
    /// </summary>
    public ElementStyles Add<TElement>(TElement style, TElement? disabled = null) where TElement : ViewElement;

    /// <summary>Returns styles without an entry for <typeparamref name="TElement"/> (the type then falls through to an outer scope).</summary>
    public ElementStyles Remove<TElement>() where TElement : ViewElement;
}

// Provides styles to a subtree; nested scopes fall through per type (§2.7). A composition primitive, beside ErrorBoundary:
public static Element WithStyles(ElementStyles styles, Element? child);

// Android's theme-derived defaults (§2.9), on by default:
public static class ReactorTheme { public static bool ResolveAndroidDefaults { get; set; } = true; }
```

The layout spacings become nullable so a style can tell "unset" from 0 (§2.12): `VStackElement.Spacing`,
`HStackElement.Spacing`, `GridElement.RowSpacing` and `ColumnSpacing` are `double?` (unset: MAUI's
default, 0). Writers (`with { Spacing = 8 }`, `VStack(8, …)`) compile unchanged; a reader of `.Spacing`
sees `double?`.

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
   independently: literals in the element's fields and modifiers (a `with { TextColor = … }` assignment is
   a literal), bindings in `ThemeBindings`, keyed by descriptor. For each property, the applied value is
   the declared literal if present, else the declared binding resolved for the theme, else the style's
   literal, else the style's binding resolved (§2.7), else unset. Call order doesn't matter:
   `.TextColor(red).TextColor(themed)` and `.TextColor(themed).TextColor(red)` both apply red. Removing
   the literal in a later render reveals the binding. A theme switch writes nothing to a property a
   literal overrides.
2. **One binding per property.** Binding a descriptor again (a second `.Themed` call, or a typed overload
   and `.Themed` with the same descriptor) replaces the earlier binding: last binding wins among bindings.
   Bindings compare by descriptor and value, so two renders with equal values are equal elements.
3. **The theme (review D5).** The renderer's theme is **the theme MAUI last applied**: the argument of the
   last `RequestedThemeChanged`, held by one app-wide `ThemeSource` subscribed before the first root
   mounts and initialized from `Application.RequestedTheme`. It is what MAUI's bindings and the app's XAML
   styles show on every platform (§2.1.1, §2.1.2). Without an application (headless), a test seam supplies
   it. Dark resolves the dark value, anything else the light one.
4. **Divergence is reported, not guessed (review D5).** After each change, a posted check compares the
   applied theme, `Application.RequestedTheme` and the platform's appearance (iOS and Mac Catalyst: the key
   window's trait collection; Android: the activity's night mode). A disagreement is reported once per
   kind with what it means: a `UserAppTheme` set during a theme change that MAUI suppressed (§2.1.1; the
   remedy is to set it from a posted callback), or iOS's stale platform theme after a forced theme
   (§2.1.2). Reactor doesn't repair MAUI's state and doesn't follow `RequestedTheme` there: doing either
   would split Reactor's views from the app's XAML bindings.
5. **Re-projection without rendering.** Each top root subscribes to the `ThemeSource`. On a change it
   runs a theme pass (§2.6): every enrolled host node gets a look patch for the descriptors its bindings
   (own or from its style) name. No component renders, no handler `Update` runs. The pass runs inside
   MAUI's event, so it lands in the frame MAUI's own bindings change in (frame 0 on all four devices; the
   in-event cost equals the after-set cost, §2.1).
6. **Enrolment.** A host node is enrolled while it has theme bindings, its style has any, it has a
   Disabled variant, or (Android, §2.9) it has a theme-derived default that is unset. Nodes leave the set at
   unmount. Unmounted roots unsubscribe; the `ThemeSource` iterates a snapshot of subscribers and skips a
   root that unmounted during the notification.
7. **Only look properties.** Descriptors exist only for look properties (§2.12), so no controlled value,
   event or content can be themed.

### 2.6 Transactions and re-entrancy (review D5)

1. **One transaction per tree.** The top root holds a transaction depth entered by every operation that
   writes to native views in its tree: a commit (mount, update, unmount), a child root's commit (list
   cells, `CreateChildRoot` content, including a cell realized by the platform outside a Reactor commit),
   a controlled value's restore, teardown, and the theme, style, Disabled and Android-default passes.
2. **A change during a transaction is deferred.** The `ThemeSource` notification records the pending
   theme and bumps a generation; the pass runs when the outermost transaction ends, synchronously in the
   same turn. Nodes a commit mounts after the change are projected with the new theme at mount.
3. **A pass settles.** It takes the target theme and generation, iterates a snapshot of the enrolled
   nodes, checks each node's liveness (not unmounted, its root alive) before writing, skips nodes already
   applied for the target, and patches the rest. Nodes mounted during the pass (an error boundary's
   fallback, a realized cell) are projected at mount with the current theme and enrolled. When the pass
   ends, if the generation moved, it runs again for the newest theme; only then is the root marked applied.
   A pass that would run a fourth time in one turn is posted, with a warning: then, and only then, the
   same-frame guarantee doesn't hold.
4. **Nested theme sets** from inside a pass can't produce a second MAUI event (MAUI suppresses it,
   §2.1.1); they reach rule 4 of §2.5 instead.
5. **Errors.** A native write that throws is routed to the nearest error boundary, as a restore's error is;
   the pass continues with the other nodes; the failed node is re-projected on the next pass.
6. **Two windows.** Each window's top root subscribes; the source notifies them in subscription order;
   a window closed during the notification is skipped by rule 3's liveness check.

### 2.7 Element styles (review D9)

1. **Fill unset, fieldwise.** A style is an element of the same type; its set look properties and its
   theme bindings are its setters. For each descriptor of the element's type (common view properties
   inside `Modifiers` included, field by field), the declaration's literal or binding wins; otherwise the
   style's literal, then the style's binding (§2.5 rule 1). Content, controlled values and events are
   never read from a style (they have no descriptors).
2. **Per-type fall-through.** `WithStyles` scopes nest. A host node takes, for its exact type, the entry of
   the nearest scope that has one: an inner scope with only a `Label` entry doesn't hide an outer `Button`
   entry, and `Remove<T>()` in an inner scope falls through to the outer entry.
3. **Resolved once per scope.** A scope node keeps a resolved table: its entries over its outer scope's
   resolved table, computed when the scope mounts or its styles change, never per render. A host node finds
   its scope through a pointer fixed at creation (like `NearestProvider`) and reads its type's entry in
   O(1). A render with the same `ElementStyles` instance does nothing.
4. **Restyling without rendering.** A scope rendered with a different `ElementStyles` instance recomputes
   its resolved table and its inner scopes', then runs a style pass: look patches for the host nodes
   enrolled under it whose type's resolved entry changed (compared by reference), beneath skipped
   components too. No component renders.
5. **Root boundaries.** Child roots made with `CreateChildRoot` (list cells, item content) continue their
   owner's scope. A standalone root (a `ReactorWindow`, a `ReactorPage` created by the app) starts with no
   styles: the app wraps its content in `WithStyles` (the guide shows one shared `ElementStyles` for
   every root).
6. **XAML styles on the Controls registry.** Reactor's applied values are local values: they win over
   the app's XAML implicit styles, and unset properties are cleared and show the XAML style (§2.1). The
   exception is a XAML `Disabled` state, which MAUI ranks above local values (§2.1.3).
7. **Reporting the direct registry's gap.** When a direct view mounts and the app's resources define an
   implicit XAML style for the Controls type it stands for, Reactor warns once per type per app, naming the
   type and the remedies (`ElementStyles`, or the Controls registry for that subtree).
8. **Keyed XAML styles are functions** (§2.4).
9. **Measured.** The projection path is part of the gate (§2.14): with no styles and no bindings in the
   tree it must cost nothing measurable.

### 2.8 The Disabled state and effective `IsEnabled` (review D7)

MAUI (§2.1.3): a `Disabled` state's setters beat local values; `IsEnabled` is inherited (a disabled
ancestor disables every descendant, and a descendant's own `IsEnabled = true` can't override it). Reactor
matches both, on both registries.

1. **Effective enabled.** A host node is effectively enabled when its applied `IsEnabled` (declared, or
   from its style) isn't false and its nearest host ancestor is effectively enabled. The ancestry is
   Reactor's host-node tree, through components and through child roots made with `CreateChildRoot`
   (cells inherit the list's state), not through page roots.
2. **The variant.** An effectively disabled node applies its style's `disabled` element: for the
   properties it sets, **above the declaration's literals** (MAUI's precedence), then the normal rules.
   A declaration that wants a different disabled look declares it conditionally (`enabled ? red : gray`)
   or uses its own `ElementStyles` scope.
3. **Propagation.** A change of a node's effective state is propagated to its host descendants (beneath
   skipped components too) by a Disabled pass: a look patch for the descendants with a Disabled variant,
   and the effective `IsEnabled` written to every descendant's native view on the direct registry and to
   anchored Controls views (whose MAUI parent doesn't carry the direct layout's state, §2.1.3). On the
   Controls registry MAUI's own inheritance does the rest.
4. **Themes while disabled.** A theme pass resolves the Disabled variant's bindings for disabled nodes.
5. **The direct registry's `IsEnabled` defect** (§2.1.3) is fixed by rule 3: a disabled direct layout
   disables its children's platform views.

### 2.9 Android's theme-derived defaults (review D11)

On Android a view without a color keeps its inflation-time default, and MAUI restores that stale default
when a color is cleared (§2.1.4). Reactor re-resolves defaults natively; it never remounts and never uses
a hard-coded palette.

1. **The resolver.** For each Android platform class Reactor renders whose look properties have
   theme-derived defaults (the gate's premise 5 lists them, from a per-control characterization:
   `TextView` text, `MaterialButton` text and background tint, …), Reactor creates one reference view per
   class and configuration, on the existing views' context (characterized as reconfigured, §2.1.4), and
   reads its defaults: color state lists with every state (enabled, disabled).
2. **When.** In every theme pass, for enrolled nodes whose descriptor is unset (no literal, binding or
   style), and whenever a property becomes unset after the first theme change (MAUI restores its stale
   cache; Reactor writes the reference default after MAUI's clear).
3. **Opt-out.** `ReactorTheme.ResolveAndroidDefaults = false` leaves MAUI's behavior.
4. **Limits.** MAUI's own clears (a XAML `Disabled` state unapplied on a Controls view) still restore
   MAUI's cache; that is reported to MAUI (§2.16). Plain MAUI views outside Reactor aren't touched.
5. **Rejected: a built-in default `ElementStyles` palette.** It would hard-code Material's colors,
   diverge from an app's own Android theme, and theme only properties it knows.

### 2.10 Registered fonts (review D10)

1. **No new API.** `FontFamily` takes the alias registered with `ConfigureFonts` (portable), the file name
   with or without extension (portable), or on iOS and Mac Catalyst the font's PostScript or family name
   (not portable; §2.1).
2. **Evidence first, and no lookups that log.** When a family first reaches a handler (both registries),
   Reactor classifies it from evidence:
   - **registered**: `IFontRegistrar.GetFont(family)` returns a file;
   - **available** (iOS, Mac Catalyst): `UIFont.FromName(family, size)` or a non-empty
     `UIFont.FontNamesForFamilyName(family)`; neither logs;
   - **an asset** (Android): the family, or the family with MAUI's extensions, is in the app's assets
     (the listing read once);
   - **a system family** (Android 14+): the view's resolved `Typeface.SystemFontFamilyName` equals the
     family (MAUI already resolved it for the view; Reactor reads the view's typeface, it doesn't resolve
     again).

   No sentinel lookup: on Android MAUI logs a warning for each name it can't load, so a synthetic lookup
   would add noise to the normal path.
3. **Reports.** iOS and Mac Catalyst: a family with no evidence "isn't registered or installed" (definite).
   Android: a family with no evidence whose view renders the system sans-serif is a **suspected
   fallback** (the wording says so); below Android 14 there is no system-family evidence, so nothing is
   reported for an unregistered non-asset family (MAUI's own warning remains). Once per family per app and
   font manager.
4. **Traits.** iOS and Mac Catalyst ignore `Bold` and `Italic` on a single-face family: reported once per
   family and trait when the resolved font lacks it (`UIFontDescriptor.SymbolicTraits`). Android fakes
   them; nothing reported. The guide recommends an alias per face.
5. **Styles carry fonts** like any property; sizes and auto-scaling are unchanged.

### 2.11 Both registries and eligibility (review D6)

Projection, styles, theme passes and Disabled passes run in the engine, before or instead of the handler,
so both registries see the same applied values. Two differences remain, both reported: XAML implicit
styles reach only Controls views (§2.7 rule 7), and a XAML `Disabled` state ranks above Reactor's values
on Controls views (§2.7 rule 6).

**Eligibility is stable across themes.** On the direct registry, the choice between the direct view and
the Controls fallback is computed from the declaration with its style and **both** branches of every theme
binding: a view that needs Controls in either theme (a themed brush whose dark branch is a gradient) is a
Controls view in both. A theme switch never remounts. Ordinary renders can still change the choice, as
any declared change can.

### 2.12 Census and the styleable inventory (review D8)

**The common-look census moves into 4d-1.** The property census excludes `ViewElement.Modifiers`
(`PropertyMatrix.cs` 348–365). 4d-1 adds the modifiers that are look properties: `BackgroundColor`,
`Background`, `Opacity`, `IsVisible`, `IsEnabled`, `Rotation`, `Scale`, `TranslationX`, `TranslationY`,
`ZIndex`, `Shadow`, `Clip`, `InputTransparent`, `Margin`, `Width`, `Height`, the minimums and maximums,
`HorizontalOptions` and `VerticalOptions`. Each gets the matrix's set, update, equal-render and clear
steps on both registries, a themed step where a typed overload exists, and a style step. 4d-3 still adds
device geometry and pixels, gestures and semantics.

**The inventory.** Every element property is classified, and the census fails on an unclassified one:

| Class | Properties | Presence |
|---|---|---|
| Look: common (`LookProperties.View`) | the modifiers above | `null` = unset |
| Look: text (`LookProperties.Text`) | `FontSize`, `FontAttributes`, `FontFamily`, `TextColor`, `CharacterSpacing` (every text element) | `null` |
| Look: per control | Label: `TextDecorations`, alignments, `LineBreakMode`, `MaxLines`, `LineHeight`, `Padding`, `TextTransform`, `TextType`; Image: `Aspect`; BoxView: `Color` (positional), `CornerRadius`; ActivityIndicator: `Color`; ProgressBar: `ProgressColor`; Border: `Stroke`, `StrokeThickness`, `CornerRadius`, `Padding`; Button: `CornerRadius`, `BorderColor`, `BorderWidth`, `Padding`; ImageButton: `Aspect`, `CornerRadius`, `Padding`; Entry: `PlaceholderColor`, `HorizontalTextAlignment`, `ClearButtonVisibility`; Editor: `PlaceholderColor`; CheckBox: `Color`; Switch: `OnColor`, `ThumbColor`; Slider: the track and thumb colors; layouts: `Padding`, `Spacing`, `RowSpacing`, `ColumnSpacing`, the flex enums; ScrollView: scroll bar visibilities; RefreshView: `RefreshColor`; IndicatorView: the colors, `IndicatorSize`, `IndicatorsShape`; shapes: `Fill`, `Stroke`, the stroke settings, `Aspect`, `RadiusX`, `RadiusY`, `FillRule`; ContentView: `Padding` | `null`; the four spacings become nullable (§2.4) |
| Look, theme values only (not styleable: pages and Shell aren't `ViewElement`s) | Page: `BackgroundColor`; NavigationStack: `BarBackgroundColor`, `BarTextColor`; Tabs: the three colors; Shell: its eleven colors | `null` |
| Excluded: presence can't be told (non-nullable, not look-critical) | RadioButton `IsIndicatorVisible`, IndicatorView `HideSingle`, CollectionView and CarouselView `ItemSpacing`, swipe modes, `RoundRectangle.CornerRadius` (positional) | listed with the reason |
| Not look | content (text, sources, items, children, slots, inlines, data), controlled values, events, behavior (`Keyboard`, `IsReadOnly`, `MaxLength`, `Orientation`, …), identity and placement modifiers (`AutomationId`, grid and flex placement), `Style`, `StyleClass`, `Configure`, `Ref`, semantics | — |

**Mutants the census must catch.** Omitting a common field from the fieldwise modifier merge (e.g.
`Opacity`); omitting a per-control descriptor (e.g. `Entry.PlaceholderColor`); a descriptor whose native
writer differs from the handler's ordinary write (the census runs both paths and compares the native
value).

### 2.13 Tests

Each oracle names the mutation that must turn it red; every zero-write and no-warning oracle has a
positive control in the same test. Headless tests use the theme seam, a stub `ThemeSource`, the stub
handlers' write counters, and a counter of `Update`, `Configure`, restore and invalidate calls. Device
tests run on Mac Catalyst, iPhone, iPad and Android, `[controls]` and `[direct]`; their waits accept only
observations made after the action (a frame index or sequence number taken at the action).

**Headless: theme values.**

| Test | Oracle | Mutation |
|---|---|---|
| Projection at mount | Light: the light value written; Dark: the dark value | always light |
| Literal wins, both orders | `.TextColor(red).TextColor(themed)` and the reverse both apply red; a switch writes nothing to `TextColor` (positive control: a themed `BackgroundColor` on the same element is written) | last call wins |
| `with` is a literal | `with { TextColor = red }` beside a binding applies red | binding over field |
| Literal removal | a render without the literal applies the binding for the current theme | keep the stale literal |
| Switching while overridden | two switches while a literal overrides: zero writes to that property; after removing the literal, the next switch writes it | patch overridden properties |
| One binding per property | two `.Themed` calls on one descriptor: the second's values; a typed overload then `.Themed` on its descriptor: one binding, the later one | key by delegate or call site |
| A literal over a themed style setter | style `TextColor` themed, declaration red: red, and a switch writes nothing to it (positive: an unoverridden themed setter is written) | style binding over declared literal |
| Look patch only | a switch on a themed view that has a controlled value with native drift pending, a `.Set(...)` counter and a themed `GraphicsView`: the drift isn't restored, `Configure` count 0, invalidate count 0, `Update` count 0; positive controls: an ordinary render restores the drift, runs `Configure` once and invalidates once | route the patch through `Update` |
| Declared vs applied | during and after a switch, `Live` returns the declared element (unprojected bindings, same instance); the next ordinary update diffs from the patched applied element (a property the patch changed and the render sets to the same value writes nothing) | `Live` returns applied; applied not updated by the patch |
| No renders | a switch: component render count unchanged; positive: a state change renders once | re-render on the event |
| Equality | two renders with equal values: equal elements, zero writes | reference-equal bindings |

**Headless: transactions and re-entrancy.**

| Test | Oracle | Mutation |
|---|---|---|
| Flip inside the pass | a stub write handler flips the source during the pass: the pass ends, re-runs for the newest theme, every node ends on it, each patched at most twice | mark applied before settling |
| During first mount | a change while the first commit mounts: nodes after the change mounted with the new theme, earlier ones patched at the end of the commit, none twice | nested pass |
| Subscription before mount | a change between root creation and first mount: the mount uses the new theme | subscribe at first mount |
| A recycled cell | a cell rebinding A→null→B (with and without `ReuseItemTrees`) during a flip: no write to the retired tree, B's nodes on the new theme | iterate live list, not snapshot + liveness |
| A boundary removes later nodes | a write in the pass throws; the boundary unmounts later enrolled nodes: no write to them; the error reaches the boundary once | no liveness check |
| A later switch after a throw | the next switch patches the failed node | drop the failed node |
| Two windows | two top roots: both patched; one closed during the notification: no write to it, the other patched | iterate live subscribers |
| Unmount | after unmount, the source's subscriber count for the root is 0 (direct proof) and the root is collectable (a `WeakReference` after GC); positive: a live root is counted | rely on MAUI's weak event manager |
| Divergence report | a stub where `RequestedTheme` differs from the applied theme after dispatch: one report naming the suppressed set; positive: a consistent change reports nothing | follow `RequestedTheme`; never report |

**Headless: styles, Disabled, eligibility, inventory.**

| Test | Oracle | Mutation |
|---|---|---|
| Fill-unset census | every inventory descriptor of every type: style fills it when unset, the declaration keeps it when set; common modifiers fieldwise | omit `Opacity` from the merge; omit `Entry.PlaceholderColor` |
| Per-type fall-through | outer `{Label, Button}`, inner `{Label}`: buttons use the outer entry; inner `Remove<Label>()`: labels use the outer | inner scope hides outer |
| Restyle without renders | a scope rendered with new styles: descendants patched (also below a skipped component), zero component renders; the same instance: zero writes (positive: a changed entry writes) | context re-render; per-render walk |
| Root boundaries | a cell's tree uses the list's scope; a standalone `ReactorPage` root without `WithStyles` uses none | inherit across standalone roots |
| Disabled precedence | an effectively disabled button: the disabled variant's background above the declared literal; enabled: the literal | literal over variant |
| Parent disable and re-enable | disabling a parent patches descendants' variants (also below a skipped component and in cells), re-enabling restores; an explicit child `IsEnabled(true)` stays disabled | own state only |
| Style-supplied `IsEnabled(false)` | the style disables: variant applied | ignore styled `IsEnabled` |
| Theme while inherited-disabled | a switch resolves the variant's bindings | resolve the normal style |
| Mixed ancestry | a disabled direct layout over a direct button and an anchored Controls button: both disabled natively (written `IsEnabled`) | write only to direct views |
| Eligibility union, both directions | a binding needing Controls only in Dark, and one only in Light: a Controls view from mount; switches keep native identity, a child component's hook state, its effect (no cleanup or re-run) and its ref | decide on the current theme |
| Inventory | every element property classified; a new unclassified property fails | — |

**Headless: fonts and reports.**

| Test | Oracle | Mutation |
|---|---|---|
| Valid family same as the fallback | Android stub: `sans-serif` resolves to the fallback and has system-family evidence: no report; positive: `NoSuchFont` with no evidence reports once as suspected | sentinel or fallback equality alone |
| Evidence order | registered alias: no native lookup made (counted); iOS PostScript name: available; missing: definite on iOS | look up before the registrar |
| Size and traits | the same family at two sizes and with Bold/Italic: classified once per family; iOS Bold on a single face: one trait report; Android: none | report per size; never report traits |
| Several missing spellings | `NoSuchFont`, `NoSuchFont.ttf`, `no such font`: one report each, once | report once in total |
| XAML gap | a direct label under an app-level implicit `Label` style: one warning; a second: none; Controls registry: none; no style: none (positive: the first) | per view; never |

**Device.**

| Fixture | Oracle |
|---|---|
| `Theme_SameFrame [controls|direct]` | a Reactor-themed label, a MAUI `AppThemeBinding` label and a `UseAppTheme` probe: the themed label's new color first observed at the same frame index as MAUI's label (0); the probe (later on Android) is the sampler's positive control, observed by a frame callback |
| `Theme_LookPatchOnly [controls|direct]` | mapper hook: a switch maps each themed property once per view and nothing else; a themed Entry with drift isn't restored; positive control: a `Text` change maps once |
| `Theme_LiteralWins [controls|direct]` | both orders, `with`, removal; a switch while overridden changes nothing on the platform (pixels), the themed sibling changes |
| `Theme_Reentrancy` | a subscriber sets `UserAppTheme` during the change: Reactor's views show MAUI's applied theme, the divergence is reported once |
| `Theme_SystemAppearance`, `Theme_ForcedTheme` (host-driven, iPhone, iPad, Android) | platform-started changes land in the same frame as MAUI's bindings; iOS's stale theme after a forced theme is reported |
| `Theme_Eligibility [direct]` | a binding needing Controls in one theme: native identity unchanged across switches, a stateful child keeps its state |
| `Styles_Parity` | `ElementStyles` for Label and Button: native font, size, color and width equal on both registries and equal to a plain MAUI view under the equivalent XAML style; override wins, removal shows the style, a switch restyles |
| `Styles_Scopes` | nested scopes fall through per type; restyling patches without rendering (render counter) |
| `Disabled_Inherited [controls|direct]` | a disabled parent: children disabled natively and showing the variant, including an anchored Controls child and an explicit-enabled child; re-enabled: back; the direct registry no longer differs from Controls |
| `Android_Defaults [controls|direct]` (Android; Apple as reference) | after app- and system-started changes: views without colors show the fresh-view defaults (enabled and disabled), cleared colors too; with the opt-out: MAUI's stale behavior (the positive control that the resolver is what fixes it) |
| `Fonts_Registered`, `Fonts_Evidence` | spellings render the face (width equals plain MAUI's, differs from default); missing families: definite on iOS, suspected on Android 14+; `sans-serif` not reported; MAUI's own warnings not increased by Reactor (log capture count) |

### 2.14 Gate

Hard premises, characterized first in the implementation; if one fails on a device, stop and report:

1. **The same frame.** Reactor's in-event look patch shows in the same frame index as MAUI's bindings on all
   four devices for app-started changes, and on iPhone, iPad and Android for platform-started ones.
2. **Look patch only.** A switch maps each themed property once per view and nothing else, and runs no
   `Update`, `Configure`, restore or invalidate.
3. **Android defaults.** For every Android platform class Reactor renders, a per-control characterization
   lists which defaults go stale after a change (app and system); the resolver covers exactly those, and a
   fresh reference view reproduces the defaults a newly mounted view shows, enabled and disabled.
4. **Disabled.** Reactor's effective enabled state and variant match plain MAUI's for the §2.1.3 cases on
   both registries.
5. **Font evidence.** Every spelling of §2.1 is classified as its row says, with no extra MAUI warnings.
6. **Projection cost.** `Reactor.Maui.Benchmark` (1,000 rows, both registries): with no styles and no
   bindings, mount and update within noise of `1a8d88c6` (the medians' ranges overlap); with an app-wide
   `ElementStyles` and themed colors, the cost is reported.

Then: full device suites on all four devices with the look fixtures; the Release build of
`Reactor.Maui.slnx` with no warnings; the mutants above; spec 068's rows (the theme value ✅, styling
direct views ✅, fonts ✅ for registration and rendering); README caveats (the reported MAUI divergences,
XAML styles on direct views, Android defaults' limits).

### 2.15 Decisions to review

1. **`AppThemeValue<T>`** (review D1), MAUI-side; WinUI's `ThemeRef` is a different abstraction.
2. **Literal always wins**, independent storage (review D2), as in WinUI.
3. **Descriptors** (`LookProperty`) key bindings, styles and the look patch (review D3); `.Themed` takes a
   descriptor. Handlers' look writes go through them.
4. **The look patch** never runs `Update` (review D4); declared, applied and native are distinct.
5. **The applied theme is MAUI's** (the last event's argument), not `RequestedTheme`, against review D5's
   suggestion: §2.1.1 and §2.1.2 show that after a suppressed nested set, and after iOS's forced-theme
   case, `RequestedTheme` disagrees with what MAUI's bindings and XAML styles show; following it would
   split Reactor's views from the app's XAML. Divergence is reported. The alternative (follow
   `RequestedTheme`, with a posted settle) is recorded.
6. **One transaction per tree**, deferred and settling passes (review D5).
7. **Eligibility from both theme branches** (review D6): no remount on a switch.
8. **MAUI's Disabled precedence**: the variant above declared literals, inherited state (review D7). The
   alternative (declared literals above the variant, a deliberate deviation) is recorded.
9. **The direct registry gets inherited `IsEnabled`**, fixing §2.1.3's defect in 4d-1.
10. **The common-look modifier census and the inventory move into 4d-1** (review D8); the four layout
    spacings become nullable (a reader-side source break).
11. **Style scopes** fall through per type, resolved per scope, restyled by look patches (review D9).
12. **Font evidence**, never a sentinel; "suspected" wording on Android (review D10).
13. **Android defaults** by a native resolver, on by default (review D11); a palette rejected.
14. **Reports**, once each: XAML styles ignored by direct views, theme divergence, font evidence, traits.

### 2.16 Open questions

- A subtree theme override, like WinUI's `.RequestedTheme(...)`.
- An opt-in fallback to Controls for direct views under XAML implicit styles.
- Theme values and styles in rich-text runs (a label's `Inlines`).
- Styles for pages and Shell (theme values are supported, styles aren't).
- Whether the Disabled variant should also cover other system states once 4e brings pointer and focus.
- Font family evidence below Android 14 (no reliable system-family name).
- Mac Catalyst's platform-started theme change and forced-theme behavior (uncharacterized, §2.1).
- MAUI issues to file:
  - a `UserAppTheme` set during a theme change is suppressed and leaves MAUI inconsistent (§2.1.1);
  - iOS applies a stale platform theme when returning to Unspecified after the system changed while forced
    (§2.1.2);
  - Android keeps inflation-time defaults and restores them from a cache after a theme change (§2.1.4);
  - an implicit style removed from `Application.Resources` stays applied;
  - iOS ignores `Bold` and `Italic` on a single-face registered family silently.

### 2.17 Out of scope

- `ResourceDictionary`, `StaticResource`, `DynamicResource` (spec 068: ⛔); `StyleClass` and CSS.
- Visual states other than `Disabled`; triggers.
- Dynamic Type and font auto-scaling.
- Windows (4g).
