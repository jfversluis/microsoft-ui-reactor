# Spec 071: MAUI look (phase 4d)

Phase 4d gives Reactor's MAUI renderer the look features spec 068 lists under "Look and theme" and the
remaining 🧪 modifiers: a theme value applied without rendering, styling for direct views, registered
fonts, animations, flow direction, `AutomationProperties`, and tests for every untested modifier. It
stacks on 4c (spec 070), whose head is `1a8d88c6`.

§1 is the outline of the whole phase. §2 is 4d-1's design: characterized on devices, not yet built.
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
| A themed value on a modifier | `ThemeRef` passed to modifier overloads (`.Background(ThemeRef)`, `.Foreground(ThemeRef)`, `.BorderBrush(ThemeRef)`), stored in `Element.ThemeBindings` | `ThemeRef<T>` passed to modifier overloads (`.BackgroundColor(ThemeRef<Color>)`, `.TextColor(ThemeRef<Color>)`, …), stored in `Element.ThemeBindings` | MAUI has no theme resource dictionaries in Reactor (spec 068: ⛔), so the value is a light and dark pair, not a resource key. The modifier names are MAUI's property names (spec 070 §3.2) |
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

### 2.1 Characterization (device, `LookCharacterization.cs`)

The baseline is plain MAUI 11.0.0 (`4e6582f`), then the same trees through Reactor on both registries.
Devices: Mac Catalyst 26.7, the iPhone simulator (iOS 27.0), the iPad simulator (iOS 27.2) and the
Android 17 emulator.

The fixtures are on demand (`REACTOR_DEVICE_TEST_FILTER=Characterize_Look`). They log what they measure
and assert only that each instrument works:
- **Mapper hook:** appended to every `LabelHandler` and `ButtonHandler` mapping. Its positive control is
  that a `Text` change counts once.
- **Frame sampler:** `CADisplayLink` or `Choreographer`. Its positive control is that some view
  takes the new color.
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
- Mac Catalyst's platform-started change (the host's appearance).
- Real Dynamic Type and accessibility font scaling, which these fixtures don't touch.

### 2.2 What WinUI has (the source of truth for names)

WinUI Reactor's theme value is `ThemeRef(string ResourceKey)`, a reference to a WinUI theme
resource:
- Typed tokens: `Theme.Accent`, `Theme.CardBackground`, …. Any key: `Theme.Ref(key)`.
- It is passed to modifier overloads (`.Background(ThemeRef)`, `.Foreground(ThemeRef)`,
  `.BorderBrush(ThemeRef)`, `.WithBorder(ThemeRef, …)`), which record it in
  `Element.ThemeBindings` (`IReadOnlyDictionary<string, ThemeRef>`, keyed by property name).
- The reconciler applies the bindings as a synthesized `Style` whose setters are `{ThemeResource}`
  references, so WinUI itself re-resolves them when the theme changes.
- The host also re-renders on `ActualThemeChanged`, for values resolved in render.

For styles, WinUI Reactor relies on XAML:
- implicit styles in app resources, honored natively by WinUI's controls;
- named-style fluents (`.AccentButton()`, typography factories over `.ApplyStyle(key)`);
- lightweight styling (`.Resources(r => r.Set(key, value))`).

Spec 015 considered and declined a Reactor-level style type ("Style bundles: document the pattern,
don't add ceremony") and deferred typed per-control style protocols.

The MAUI renderer keeps WinUI's shape for the theme value: a value type passed to modifier overloads,
recorded in `Element.ThemeBindings` and applied by the renderer, not by render. It differs where MAUI
does:
- There are no theme dictionaries in Reactor (spec 068 excludes `ResourceDictionary`), so the value is
  a light and dark pair, `ThemeRef<T>`.
- The overloads are named after MAUI's properties (`.BackgroundColor`, `.TextColor`).

WinUI is unchanged: nothing in 4d-1 moves to the shared core. WinUI's `ThemeRef` is tied to WinUI's
resource system, and the MAUI type differs in meaning and arity.

### 2.3 API

```csharp
namespace Microsoft.UI.Reactor.Maui;

/// <summary>
/// A value per app theme. The renderer writes the current theme's value and rewrites it when the theme
/// changes, without rendering.
/// </summary>
public readonly record struct ThemeRef<T>(T Light, T Dark)
{
    /// <summary>The value for <paramref name="theme"/>: <see cref="Dark"/> for Dark, <see cref="Light"/> otherwise (MAUI's rule).</summary>
    public T For(AppTheme theme) => theme == AppTheme.Dark ? Dark : Light;
}

public static class ThemeRef
{
    public static ThemeRef<T> Of<T>(T light, T dark) => new(light, dark);
}

// Modifier overloads, beside the literal ones (WinUI's shape):
public static T BackgroundColor<T>(this T element, ThemeRef<Color> color) where T : ViewElement;
public static T Background<T>(this T element, ThemeRef<Brush> brush) where T : ViewElement;
public static T TextColor<T>(this T element, ThemeRef<Color> color) where T : TextViewElement;
public static BorderElement Stroke(this BorderElement element, ThemeRef<Color> color, double thickness = 1);
public static T Fill<T>(this T shape, ThemeRef<Color> color) where T : ShapeElement;
public static T Stroke<T>(this T shape, ThemeRef<Color> color, double? thickness = null) where T : ShapeElement;

// Any other property, including init-only ones and those of pages and Shell items:
public static TElement Themed<TElement, TValue>(this TElement element, ThemeRef<TValue> value,
    Func<TElement, TValue, TElement> apply) where TElement : Element;
// e.g. Entry(text, setText).Themed(ThemeRef.Of(Gray200, Gray500), static (e, c) => e with { PlaceholderColor = c })

// The bindings, as WinUI names them (on the renderer's Element base, namespace Microsoft.UI.Reactor.Core):
public IReadOnlyList<ThemeBinding>? ThemeBindings { get; }   // set only through the modifiers above
public sealed class ThemeBinding { public string Key { get; } public object? Light { get; } public object? Dark { get; } }

/// <summary>
/// Implicit styles for Reactor elements: per element type, an element whose set properties are the
/// style's setters, and optionally the look while disabled (MAUI's CommonStates Disabled).
/// </summary>
public sealed class ElementStyles
{
    public static ElementStyles Empty { get; }

    /// <summary>Returns styles with <paramref name="style"/> for <typeparamref name="TElement"/>, replacing an earlier one.</summary>
    public ElementStyles Add<TElement>(TElement style, TElement? disabled = null)
        where TElement : ViewElement, IStyleable<TElement>;

    /// <summary>These styles with <paramref name="inner"/>'s entries replacing theirs per type (MAUI's BasedOn, by composition).</summary>
    public ElementStyles With(ElementStyles inner);
}

/// <summary>An element type whose look properties a style can fill. Every built-in element implements it.</summary>
public interface IStyleable<TSelf> where TSelf : ViewElement, IStyleable<TSelf>
{
    /// <summary>
    /// <paramref name="declared"/> with every look property it leaves unset, and has no theme binding
    /// for, taken from <paramref name="style"/>.
    /// </summary>
    static abstract TSelf MergeUnder(TSelf declared, TSelf style);
}

// Provides styles to a subtree (a composition primitive, beside ErrorBoundary):
public static Element WithStyles(ElementStyles styles, Element? child);
```

An app's template styles, ported:

```csharp
static readonly ElementStyles AppStyles = ElementStyles.Empty
    .Add(Label("").FontFamily("OpenSansRegular").FontSize(14)
        .TextColor(ThemeRef.Of(Colors.Black, Colors.White)))
    .Add(Button("").FontFamily("OpenSansSemibold").FontSize(14)
            .TextColor(ThemeRef.Of(Colors.White, AppColors.Primary))
            .BackgroundColor(ThemeRef.Of(AppColors.Primary, Colors.White))
            with { CornerRadius = 8, Padding = new Thickness(14, 10) },
        disabled: Button("")
            .TextColor(ThemeRef.Of(AppColors.Gray950, AppColors.Gray200))
            .BackgroundColor(ThemeRef.Of(AppColors.Gray200, AppColors.Gray600)));

// The root: every view below takes the styles, on either registry.
new ReactorWindow(WithStyles(AppStyles, Component<AppShell>()), DirectHandlers.CreateRegistry());

// A named (keyed) XAML style is a function:
static LabelElement Headline(this LabelElement l) => l.FontSize(32).FontFamily("OpenSansSemibold");
```

### 2.4 The theme value

1. **The theme.** `Application.Current.RequestedTheme`: Dark gives the dark value, anything else
   (Light, Unspecified) the light one. This is MAUI's own rule (`AppThemeBinding.GetValue`). Without an
   application (headless tests), a test seam supplies the theme.
2. **Projection before the handler.** Before a host element reaches its handler (mount, update, a
   controlled value's restore), Reactor projects it: each theme binding's value for the current theme
   is written into the element through its `apply`, in declaration order. The handler sees only plain
   values, so handlers don't change. The node keeps the projected element it last gave its handler,
   and every later update diffs from it.
3. **Last call wins** between a literal and a themed modifier for the same key:
   - `.TextColor(Colors.Red)` removes a `TextColor` binding.
   - `.TextColor(ThemeRef.Of(a, b))` adds the binding and clears the field.
   - A field set with `with { TextColor = … }` beside a binding for the same key: the binding wins, and
     Reactor warns once per element type and key. Reporting, not swallowing: the field is silently
     overwritten otherwise.

   `.Themed(value, apply)` is keyed by its `apply` delegate. A static lambda is one cached instance
   per call site, so two renders' bindings are equal. A capturing lambda is allowed but makes the
   element unequal on every render; that costs a handler update, never a wrong write.
4. **A theme change re-projects without rendering.**
   - **Subscription:** the top root of a tree subscribes to `Application.RequestedThemeChanged` while
     mounted. MAUI raises it synchronously, after applying its own bindings (§2.1).
   - **Inside the event**, Reactor updates every mounted host node with theme bindings (its own, or
     inherited from a style, §2.5) in its root's echo scope: from the projected element it last applied
     to the element projected for the new theme.
   - **Only the themed properties are written**: the census proves an equal value writes nothing
     (spec 070 §4.14). No component renders.
   - **Timing:** this is the frame MAUI's own bindings change in (frame 0 on all four devices, §2.1),
     where a `UseAppTheme` re-render lands a frame late on Android.
   - **Child roots** (list cells, pushed pages, dialogs) are re-projected by their top root, in tree
     order.
5. **Not during a commit.** If the event arrives while a root is committing (an app setting
   `UserAppTheme` from inside a render), the re-projection runs once at the end of the commit. Nodes
   the commit projected after the change already used the new theme; the pass fixes the earlier ones.
6. **Once per change.** Each root records the theme it last projected for and ignores an event that
   names it again. MAUI deduplicates its own events, but a re-entrant set and a commit-end pass must not
   re-project twice.
7. **Unmount.**
   - A node leaves the themed set when it unmounts, and the top root unsubscribes when it unmounts.
   - Nothing is written after either.
   - The handler swap the direct registry's fallback performs (a projected element that now needs
     Controls) is a remount, as for any property flip.
8. **Errors.** A handler's exception during re-projection goes to the nearest error boundary, as a
   restore's does (`RestoreControlledValues`).
9. **Controlled values aren't look.** The typed overloads exist only for look properties (colors,
   brushes). `.Themed` on a controlled property is a declared change at every switch: written in the
   echo scope, never reported.
10. **`UseAppTheme` is unchanged.** A component that branches on the theme still re-renders. Its
    projection then writes nothing new for the bindings, which already changed.

### 2.5 Element styles

1. **Fill unset.** A style is an element of the same type, and the properties it sets are its setters.
   A host element of exactly that type takes the style's value or theme binding for each look property
   it leaves unset (null) and has no binding for. Content is never styled: the positional parameters
   (text, items, children, callbacks) aren't look properties, and `MergeUnder` doesn't read them. The
   declared element always wins, as a MAUI local value beats a style setter.
2. **Disabled.** When the element's own `IsEnabled` is false, the `disabled` style is merged first, then
   the normal one. This is MAUI's `Disabled` visual state layered over the style's setters, the only
   state the template styles use besides `Normal`.
   - A change of `IsEnabled` re-projects.
   - A parent's `IsEnabled` isn't inherited into this decision (open question, §2.12).
   - Other states: `PointerOver`, `Focused` and `Pressed` need pointer events (4e). `Checked`,
     `Selected`, `On` and `Off` are props from state (spec 068).
3. **Order.** Projection merges the styles (disabled, then normal), then resolves the theme bindings:
   the element's own and those it took from the style.
4. **Scope.** `WithStyles(styles, child)` provides styles to its subtree through Reactor's context
   mechanism (a provider node), so a host node finds them through its nearest provider, as context
   lookups do.
   - Nested providers: the nearest provider's entry for a type replaces outer ones. That is MAUI's
     nearest-implicit-style rule; `BasedOn` is `outer.With(inner)`.
   - Child roots (list cells, pushed pages, dialogs) inherit, as context does.
5. **Changing the styles** (a provider rendered with a different `ElementStyles` instance; compared by
   reference, since the type is immutable) re-projects the provider's host descendants without
   rendering components.
6. **XAML styles on the Controls registry.** Reactor's projected values are local values, so they win
   over the app's XAML implicit styles. Properties Reactor leaves unset are cleared (`ClearValue`) and
   show the XAML style (§2.1). The layers on the Controls registry are therefore:
   declared ⊕ `ElementStyles` > XAML implicit style > MAUI's default. On the direct registry XAML
   styles don't apply.
7. **Reporting the direct registry's gap.** When a direct view mounts and the app defines an implicit
   XAML style for the Controls type it stands for (`Label` for `DirectLabel`, …), Reactor warns once
   per type per app. The warning names the type and the two remedies: `ElementStyles`, or the Controls
   registry for that subtree. The check reads MAUI's implicit-style key, the target type's full name,
   in `Application.Current.Resources` and up the direct tree's Controls anchor. It runs once per type.
8. **Keyed XAML styles are functions** (§2.3; WinUI's named-style fluents and spec 015 proposal 1). There
   is no API for them, only the documented pattern.
9. **`IStyleable` on every built-in element.** `MergeUnder` is written by hand per type. The census
   (§2.8) proves it fills every look property of every element.

### 2.6 Registered fonts

1. **No new API.** `FontFamily` takes:
   - the alias registered with `ConfigureFonts`: portable, and what the guide recommends;
   - the file name, with or without its extension: portable, although Android logs a warning for the
     form without one;
   - on iOS and Mac Catalyst, the font's PostScript or family name: not portable, since Android doesn't
     resolve them (§2.1).

   Both registries resolve every spelling the same way, through MAUI's font manager.
2. **An unresolved family is reported once per family per app**, on both registries, when a text
   element's family first reaches a handler. Unresolved means MAUI's font manager returns the same
   native font for the family as for a sentinel name no font has (iOS and Mac Catalyst: `UIFont` name
   and size; Android: `Typeface` equality).
   - Comparing with the sentinel's font, not the default font, matters: on Android a missing family
     resolves to a sans-serif fallback that isn't `Typeface.Default` (§2.1).
   - The result is cached per family. MAUI caches the resolution itself (under 0.1 ms after the
     first).
   - iOS and Mac Catalyst log nothing for a missing family today, and Android logs a warning per
     resolution. The report gives every platform one actionable line naming the family and the
     registration API.
3. **Attributes on a single-face family.** iOS and Mac Catalyst ignore `Bold` and `Italic` on a family
   registered with one face; Android fakes them. On iOS and Mac Catalyst Reactor reports once per
   family and attribute when the resolved font lacks the trait (`UIFontDescriptor.SymbolicTraits`). The
   guide recommends registering each face under its own alias.
4. **Styles carry fonts** like any other property. Font sizes and auto-scaling are unchanged: the direct
   views mirror Controls (spec 065 §4).

### 2.7 Both registries

Projection, styles and theme re-projection run in the engine, before the registry's handler, so both
registries see the same element. The two differ only in:
- **XAML implicit styles**, which only Controls views honor (§2.5 rule 6, reported by rule 7);
- **the direct registry's fallback decision** (`NeedsControls`), which sees the projected element. A
  themed `Background` brush is a Controls setting in both themes. A theme switch that changes the
  decision (a value Controls needs in one theme only) remounts that view, as any property flip does.

Controls views inside direct layouts are styled and themed like logical children (§2.1), so mixed trees
need nothing more.

### 2.8 Census keys and their evidence

- **No new element keys.** The theme bindings live on the renderer's `Element` base and `WithStyles`
  is a composition primitive. Both are in `Microsoft.UI.Reactor.Core`, which the property census
  skips (`PropertyMatrix.cs`), as it skips `Key`.
- **Their evidence** is §2.9's tests.
- **Two existing groups gain facets:**
  - Group T (text and font, spec 070 §4.14.2): `TextCases` emits a themed case for `TextColor` on every
    text element, with themed set, switched, equal-render and cleared steps. Font families gain a
    registered-alias case.
  - Group P: every color property with a typed overload (`BackgroundColor`, `Background`, a Border's
    and a shape's `Stroke` and `Fill`) gains a themed case.
- **A new style census:**
  - For each element type and each look property the census lists, a style that sets it and a
    declaration that doesn't: the merged element has the style's value.
  - A declaration that sets it keeps its own.
  - A look property `MergeUnder` misses fails its case.
  - The census already enumerates the properties. "Look" is every property except the content
    parameters, controlled values and events (groups C, E and S).

### 2.9 Tests

Each oracle names the mutation that must turn it red. Headless tests read the theme from the test seam
and count handler writes through the existing stub handlers. Device tests run on Mac Catalyst, iOS
(iPhone and iPad) and Android, `[controls]` and `[direct]`.

**Headless (`tests/Reactor.Maui.Tests`, `tests/Reactor.Maui.Engine.Tests`):**

| Test | Oracle | Mutation |
|---|---|---|
| Projection at mount | Light: the light value written; Dark: the dark one | always the light value |
| Re-projection without render | a switch: the themed property written once, every other property zero writes, the component's render count unchanged. Positive controls: a state change renders once, and a literal change writes once | re-render on the event; re-apply every property |
| In-order, once | two switches and back in one turn: the last theme applied, one write per switch; an event naming the projected theme writes nothing (positive: a real change writes) | no dedupe |
| During a commit | the theme changes inside a handler's update: no nested re-projection; after the commit, the earlier nodes carry the new theme | apply re-entrantly |
| Unmount | a node unmounted, then a switch: zero writes to it; a root unmounted: unsubscribed (the event reaches no handler). Positive: a live sibling is written | keep removed nodes in the set |
| Child roots | a themed label in a list cell and in a pushed page re-projects on the switch | re-project only the top root's nodes |
| Last call wins | the literal after the themed: literal; the themed after the literal: themed; a field beside a binding: the binding, one warning per type and key, none for the positive control (no field) | field wins; warn on every render |
| Equality | two renders with equal `ThemeRef`s and static `apply`s: equal elements, zero writes | reference-equal bindings |
| Style fill-unset (census) | every look property of every element: the style fills it when unset, the declaration keeps it when set | drop one property from one `MergeUnder` |
| Styles and themes | a themed style setter re-projects on a switch; a declared literal beats a themed setter, and a declared binding beats a literal setter | resolve themes before merging |
| Disabled | `IsEnabled(false)`: the disabled style over the normal one; enabling writes the normal values back | ignore `disabled` |
| Scope | nested providers: the inner entry replaces the outer for its type, other types fall through; a provider re-rendered with new styles re-projects its descendants with zero component renders (positive: the same instance writes nothing) | merge nested entries; re-render to restyle |
| Both registries | the same tree, styled and themed, gives each registry's handler the same projected element (a recorded projection per node, compared) | project in one registry's adapter only |
| XAML gap report | a direct label under an app-level implicit `Label` style: one warning; a second label: none; a Controls label: none; no style: none (positive: the first case) | warn per view; never warn |
| Font report | a stub font manager where the sentinel and `NoSuchFont` resolve to one fallback and the alias to another: one report for `NoSuchFont`, none for the alias; the Android-like stub (fallback ≠ default) still reports | compare with the default font |

**Device (`tests/Reactor.Maui.DeviceTests`):**

| Fixture | Oracle |
|---|---|
| `Theme_ThemeRef_SameFrame` | A tree with a Reactor-themed label, a MAUI `AppThemeBinding` label and a `UseAppTheme` probe. `UserAppTheme` switches; frames sampled from before the set, accepting only observations made after it. The themed label shows the new color in the same sampled frame as MAUI's label (frame 0). The probe is the sampler's positive control: later on Android. The mapper hook counts one `TextColor` mapping per themed view and nothing else (positive control: a `Text` change counts once) |
| `Theme_SystemAppearance` (host-driven, on demand) | the same oracle for a platform-started change (`simctl ui`, `cmd uimode`); skips without a host |
| `Styles_Parity` | `ElementStyles` for Label and Button: the native font name, size, text color and measured width are equal on both registries, and equal to a plain MAUI view under the equivalent XAML style (a differential oracle); a declared override wins; its removal shows the style's value; a theme switch restyles |
| `Styles_Disabled` | a disabled button shows the disabled style's background (pixels) and text color; enabling restores the normal style |
| `Fonts_Registered` | the alias, the file name and the name without extension each render the face (iOS: the native font name; all: the width equals the plain MAUI label's with the same family and differs from the default, the positive control); a missing family reports once, a registered one never |
| `Fonts_AttributesOnSingleFace` | iOS and Mac Catalyst: Bold on `OpenSansRegular` reports once and renders regular; Android: renders fake bold, no report |
| `Styles_XamlGapReport` | the direct registry under an app-level implicit `Label` style: one warning naming `Label`; the Controls registry: none |

### 2.10 Gate

Hard premises, characterized first in the implementation. If one fails on a device, stop and report.

1. **The same frame.** Reactor's re-projection inside `RequestedThemeChanged` shows in the same sampled
   frame as MAUI's own bindings, on all four devices, for app-started changes. It must also hold for
   platform-started ones on iPhone, iPad and Android; Mac Catalyst only if the host allows its
   appearance to be flipped.
2. **Minimal writes.** A switch maps each themed property once per view and nothing else, measured with
   the mapper hook.
3. **The sentinel.** Every spelling resolves or fails to resolve as §2.1's table says, on all four
   devices.
4. **Style parity.** A styled direct view matches a styled Controls view and a plain MAUI view under the
   equivalent XAML style: font, size, color and width.

Then:
- full device suites on all four devices, with the look fixtures;
- the Release build of `Reactor.Maui.slnx` with no warnings;
- the mutants above;
- spec 068's rows: the theme value ✅, styling direct views ✅, and fonts ✅ for registration and
  rendering;
- README caveats: the Android default colors, and XAML styles on direct views.

### 2.11 Decisions to review

1. **`ThemeRef<T>`, WinUI's name for a light and dark pair.** WinUI's `ThemeRef` names a resource key.
   The alternative is MAUI's vocabulary, `AppThemeValue<T>`. WinUI is unchanged either way.
2. **Projection before the handler.** The alternatives are per-handler theme code, or MAUI's
   `AppThemeBinding` on Controls views plus Reactor code for direct views. The second was rejected for
   three reasons:
   - two mechanisms;
   - Reactor's `SetValue` and `ClearValue` would contend with the binding;
   - direct views have no bindable properties.

   Projection needs no handler changes and gives the registries one behavior.
3. **Synchronously inside `RequestedThemeChanged`**, the same frame as MAUI's bindings (§2.1), not a
   posted pass. A posted pass is a `UseAppTheme` re-render's timing: a frame late on Android.
4. **Last call wins** between literal and themed modifiers. WinUI differs incidentally: a literal local
   value beats the `ThemeRef`'s synthesized style whatever the order. A field beside a binding warns.
5. **`.Themed(value, apply)`**, keyed by delegate identity, for every property without a typed
   overload. It avoids typed overloads for nearly 40 color and brush properties of elements, pages and Shell.
6. **`ElementStyles`, Reactor's implicit styles in C#,** fill-unset, with a `Disabled` state.
   Rejected:
   - translating XAML styles for direct views: a second style engine with partial semantics (visual
     states, triggers, `BasedOn`, `StyleClass`, `DynamicResource`, `ApplyToDerivedTypes`);
   - falling back to Controls whenever an implicit XAML style exists: honest, but it silently turns a
     template app's direct registry into the Controls registry. It is kept as an open question for an
     opt-in.

   WinUI has no counterpart: its controls honor XAML styles.
7. **Styles through a provider element (subtree)**, not a root option: page-level styles exist in 3 of
   the 5 sample ports.
8. **`MergeUnder` written by hand** per built-in element and enforced by the census. Reflection is
   rejected (AOT, and it would read content); a source generator is the alternative, worth it only if
   4f's wrapper generator needs one anyway.
9. **Reporting.** Reactor reports:
   - XAML implicit styles that direct views ignore: once per type;
   - unresolved font families: once per family;
   - attributes iOS ignores: once per family and attribute;
   - a field beside a binding: once per type and key.
10. **Android's default colors aren't fixed.** A view without a color doesn't follow a runtime theme
    change on Android, and MAUI's templates mask this with styles. Reactor documents that any color
    that must follow the theme needs a theme value or a style.

### 2.12 Open questions

- A subtree theme override, like WinUI's `.RequestedTheme(...)`. MAUI has none; Reactor could project
  a subtree with a fixed theme.
- An opt-in fallback to Controls for direct views under XAML implicit styles.
- Theme values in rich-text runs (a label's `Inlines`).
- Whether a parent's `IsEnabled` should select a child's `Disabled` style (MAUI inherits `IsEnabled`
  into the visual state).
- `IStyleable` for third-party elements: 4f's wrapper generator should emit `MergeUnder`.
- Mac Catalyst's platform-started theme change is uncharacterized (§2.1).
- MAUI issues to file:
  - an implicit style removed from `Application.Resources` stays applied;
  - iOS ignores `Bold` and `Italic` on a single-face registered family silently;
  - a missing family is silent on iOS and logs a warning per resolution on Android.

### 2.13 Out of scope

- `ResourceDictionary`, `StaticResource` and `DynamicResource` (spec 068: ⛔).
- `StyleClass` and CSS style sheets.
- Visual states other than `Disabled`, and triggers.
- Theme values in rich-text runs.
- Dynamic Type and font auto-scaling.
- Windows (4g).
