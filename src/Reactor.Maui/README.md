# Microsoft.UI.Reactor.Maui

Reactor for .NET MAUI: declarative, component-based UI written in C#, with React-style hooks
and keyed reconciliation, rendered to native views on Android, iOS, Mac Catalyst and Windows.

> **Status: preview.** The API can still change. Design notes, decisions and what has been verified
> on which platform: [spec 065](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/specs/065-maui-renderer.md).

## Get started

Add the package to a .NET MAUI app (`dotnet new maui`):

```bash
dotnet add package Microsoft.UI.Reactor.Maui --prerelease
```

Write a component. `Render()` returns a description of the UI; Reactor creates and updates the
real controls to match it.

```csharp
using static Microsoft.UI.Reactor.Maui.Factories;

public sealed class Counter : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        return VStack(12,
            Label($"Clicked {count} times").FontSize(18),
            Button("Click me", () => setCount(count + 1)))
            .Padding(24);
    }
}
```

Show it in a window:

```csharp
// MauiProgram.cs
builder
    .UseMauiApp<App>()
    .UseReactorDirectViews();

// App.xaml.cs
using static Microsoft.UI.Reactor.Maui.Factories;

public partial class App : Application
{
    public App() => InitializeComponent();

    protected override Window CreateWindow(IActivationState? activationState) =>
        new ReactorWindow(Component<Counter>(), DirectHandlers.CreateRegistry());
}
```

The package adds global usings for `Microsoft.UI.Reactor.Core`, `Microsoft.UI.Reactor.Maui`,
`Microsoft.UI.Reactor.Maui.Hosting` and `Microsoft.UI.Reactor.Maui.Direct`, plus `Element` and
`NavigationMode` aliases, because MAUI's own `Microsoft.Maui.Controls.Element` and
`Microsoft.Maui.ApplicationModel.NavigationMode` would otherwise make the bare names ambiguous. The factories are a `using static` you add per file, since names such as `Grid` and
`Label` would otherwise shadow MAUI's types everywhere. Set
`<ReactorImplicitUsings>false</ReactorImplicitUsings>` in the project to opt out of the global usings.

## Two renderers

Both renderers take the same components. They differ only in the registry you pass:

| Registry | What it creates | Use it when |
|---|---|---|
| `DirectHandlers.CreateRegistry()` (needs `UseReactorDirectViews()`) | Labels, buttons, entries, check boxes, switches, sliders, progress, images and the stack and grid layouts as lightweight views that MAUI's platform handlers render directly, skipping the Controls layer (no bindable properties, bindings or logical tree). Everything else is a regular MAUI control. | You want the fastest rendering. Mounting and full updates are 27–58% faster, with 59–87% fewer allocations (spec 065 §4.1). |
| `MauiHandlers.CreateRegistry()`, or no registry | Regular MAUI controls for everything. | You rely on implicit styles, `VisualStateManager`, effects or behaviors for every view. |

To render an element with your own handler, or replace a built-in one, register it on the
registry you pass. The last registration for a type wins, so yours overrides the built-in one:

```csharp
var registry = MauiHandlers.CreateRegistry()
    .Register(new BadgeHandler())              // IElementHandler<BadgeElement, BadgeView>
    .RegisterForDerivedTypes(new ChipHandler()); // every record derived from ChipBase
```

The simplest way to write a handler for your own element is a `ControlDescriptor`. It writes the
element's values, applies the shared modifiers (margin, size, gestures, semantics…) and handles
controlled values, so the view always shows what the element declares:

```csharp
using Microsoft.UI.Reactor.Maui.Handlers;

public sealed record RatingElement(int Value, Action<int>? OnChanged = null) : ViewElement;

var rating = ControlDescriptor.Create<RatingElement, Stepper>()
    .Initial(static _ => 5.0, static (v, max) => v.Maximum = max)
    .Controlled(
        static e => (double)e.Value,                       // what the element declares
        static (v, value) => v.Value = value,              // show a value
        static (v, report) => v.ValueChanged += (_, e) => report(e.NewValue),
        static e => e.OnChanged is { } changed ? value => changed((int)value) : null,
        static v => v.Value);                              // what the view shows now

var registry = MauiHandlers.CreateRegistry().Register(rating);
```

`RegisterForDerivedTypes` covers a family of element records with a non-generic base, such as the
closed types of a generic `Chip<T> : ChipBase`. A registration for exactly one type wins over it.
Unlike WinUI's process-wide `ControlRegistry`, where the first registration wins, a MAUI registry
belongs to the roots you pass it to.

The direct renderer switches an element to its Controls version when it uses a setting that only
Controls supports (`.Style()`, `.StyleClass()`, gestures, shadows, clipping, `MaxLines`…), so you
never lose a feature by choosing it. Implicit styles in `App.xaml` (`Styles.xaml` in the template)
apply to Controls views only. Style direct views with modifiers.

## Elements

Create elements with the factories in `Factories` (`using static Microsoft.UI.Reactor.Maui.Factories;`)
and refine them with modifiers. Elements are immutable records, so `with` works too.

| Group | Factories |
|---|---|
| Text and media | `Label`, `Image`, `BoxView`, `GraphicsView`, `WebView`, `HtmlView`, `ActivityIndicator`, `ProgressBar` |
| Input | `Button`, `ImageButton`, `Entry`, `Editor`, `SearchBar`, `CheckBox`, `Switch`, `Slider`, `Stepper`, `Picker`, `DatePicker`, `TimePicker`, `RadioButton` |
| Layout | `VStack`, `HStack`, `Grid`, `FlexLayout`, `AbsoluteLayout`, `ScrollView`, `Border`, `RefreshView`, `SwipeView` |
| Lists | `CollectionView`, `CarouselView` (virtualized; each item is its own tree) |
| Pages and navigation | `Page`, `NavigationHost`, `NavigationStack`, `Tabs`, `Flyout`, `Shell` (`TabBar`, `FlyoutItem`, `ShellTab`, `ShellContent`, `ShellMenuItem`) |
| Composition | `Component<T>()`, `Component<T, TProps>(props)`, `Group`, `Empty`, `Memo`, `ErrorBoundary`, `Context.Provide` |

Common modifiers: `.Margin()`, `.Padding()`, `.Width()`, `.Height()`, `.Size()`,
`.HorizontalOptions()`, `.VerticalOptions()`, `.Center()`, `.BackgroundColor()`, `.Opacity()`,
`.IsVisible()`, `.IsEnabled()`, `.FontSize()`, `.Bold()`, `.TextColor()`, `.Grid(row, column)`,
`.OnTap()`, `.AutomationId()`, `.Semantics()`, `.WithKey()`.

```csharp
Grid("auto,*", "*,auto",
    Label("Name").Bold(),
    Entry(name, setName, placeholder: "Your name").Grid(1, 0),
    Button("Save", Save).Grid(1, 1))
```

## State, effects and other hooks

Hooks are called in `Render()`, unconditionally and in the same order every render.

| Hook | Purpose |
|---|---|
| `UseState(initial)` | A value and its setter. Setting an equal value does nothing. |
| `UseReducer(initial)` | A value and an `update(old => new)` function, for updates that depend on the previous value. |
| `UseEffect(effect, deps…)` | Runs after the UI is updated, again when a dependency changes; return a cleanup from the `Func<Action>` overload. |
| `UseMemo`, `UseCallback`, `UseRef` | Cache a computed value, keep a callback identity stable, keep a mutable box across renders. |
| `UseContext(context)` | Read a value provided higher up with `context.Provide(value, child)`. |
| `UseResource(load, deps…)` | Load data asynchronously; returns loading, value or error, and cancels stale loads. |
| `UseNavigation(initial)`, `UseNavigationLifecycle(…)` | Typed navigation, as on WinUI (below). |
| `UseRouteStack(root)` | A plain list of routes in state, for the lower-level `NavigationStack` (below). |
| `UseAppTheme()`, `UseWindowSize()` | The app theme and the window size, re-rendering when they change. In a component class call them on `this`. |

State setters can be called from any thread; the update is applied on the UI thread, and several
updates in a row render once.

**Inputs are controlled.** An `Entry`, `Slider`, `Switch` and the like always show the value you
pass. Their callback reports what the user did; you decide whether it becomes the new value. If you
don't accept it, the control returns to your value:

```csharp
Entry(code, value => setCode(value.ToUpperInvariant()), placeholder: "Code")
```

**Keys keep identity.** Give list items a stable `.WithKey(id)`. Then a reorder moves the existing
controls, and each item's component state (text typed into its own entry, for example) moves with it.
Once any sibling has a key, an unkeyed one is matched by its position: if it can move, key it too, or
it remounts and loses its state.

## Navigation

Typed navigation works as on WinUI: `UseNavigation` returns a handle that keeps the history, and
`NavigationHost` shows it as a navigation stack, mapping each route to its page:

```csharp
using Microsoft.UI.Reactor.Navigation;

public abstract record Route
{
    public sealed record Home : Route;
    public sealed record Detail(int Id) : Route;
}

public sealed class App : Component
{
    public override Element Render()
    {
        var nav = UseNavigation<Route>(new Route.Home());
        return NavigationHost(nav, route => route switch
        {
            Route.Detail d => Page($"Item {d.Id}", Label($"Item {d.Id}")),
            _ => Page("Home", Button("Open", () => nav.Navigate(new Route.Detail(1)))),
        });
    }
}
```

`Navigate`, `GoBack`, `Replace`, `Reset`, `PopTo` and `SetState` push and pop pages. A page reads
the handle with `UseNavigation<Route>()`, and `UseNavigationLifecycle(onNavigatingFrom: c => c.Cancel())`
can refuse to be left: the back button, the swipe and the Android back key all go through the
handle, so a refused back keeps the page. A third argument configures the stack, for example its
title as a tab, its modals, or `Animated = false` for every change;
`Navigate(route, new NavigateOptions { Animated = false })` skips the animation of that one push.
This is the recommended navigation API, and the one shared with WinUI.

`UseRouteStack` is the lower level: the navigation stack is a plain list in state. Render it with
`NavigationStack`, and pass `stack.PopTo` so that the back button and back gesture update your
state:

```csharp
public sealed class App : Component
{
    public override Element Render()
    {
        var stack = UseRouteStack("home");
        return NavigationStack(
            stack.Items.Select(route => route == "home"
                ? Page("Home", Button("Details", () => stack.Push("details"))).WithKey(route)
                : Page("Details", Label("Details")).WithKey(route)),
            stack.PopTo);
    }
}
```

Pushing and popping animate as usual. If a pop callback doesn't accept a back navigation, the page
is pushed back. Modals are a second list on the same element:
`NavigationStack(…) with { Modals = […], OnModalDismissed = … }`. `Tabs(selectedIndex, onSelected, pages…)`
and `Flyout(flyout, detail, isPresented, onIsPresentedChanged)` follow the same controlled pattern.

### Shell

`Shell` builds MAUI's Shell from elements. The app holds the selected tab as state, and each tab with
a `NavigationHost` keeps its own stack in its handle:

```csharp
public abstract record HomeRoute
{
    public sealed record List : HomeRoute;
    public sealed record Detail(int Id) : HomeRoute;
}

public sealed class App : Component
{
    public override Element Render()
    {
        var (tab, setTab) = UseState("home");
        var homeNav = UseNavigation<HomeRoute>(new HomeRoute.List());
        return Shell(tab, setTab,
            TabBar(
                ShellTab("home", "Home", NavigationHost(homeNav, route => route switch
                {
                    HomeRoute.Detail d => Page($"Item {d.Id}", Label($"Item {d.Id}")),
                    _ => Page("Home", Button("Open", () => homeNav.Navigate(new HomeRoute.Detail(1)))),
                })),
                ShellTab("settings", "Settings", Page("Settings", Label("Settings")))))
            .FlyoutBehavior(FlyoutBehavior.Disabled);
    }
}
```

- **Items.** `TabBar(tabs…)`, `FlyoutItem(title, tabs…)` (with `.Icon(…)`) and
  `ShellMenuItem(text, onClick)`. A `ShellTab(key, title, content, icon)` holds a page or a
  `NavigationHost`; `ShellTab(key, title, ShellContent(key, title, page, icon)…)` has top tabs, each
  holding a page. Icons are optional, and a null tab or top tab is skipped, so one can be
  conditional (`isAdmin ? ShellTab(…) : null`). Tab and top-tab keys name the selection and are
  unique across the Shell (a duplicate throws).
- **Selection.** `Shell(selected, onSelectedChanged, …)` shows the tab or top tab whose key is
  `selected`. A user's switch is reported to `onSelectedChanged` once Shell shows it; if the app doesn't
  take it, the declared tab is shown again. A key that matches nothing shows the first tab and reports
  it once.
- **Stacks.** A `NavigationHost` in a tab renders the tab's own stack (the root is the tab's page).
  The back button, the iOS swipe and the Android back key run the guards before any page moves, so
  a refused back moves nothing; an app navigation made during the user's back waits until it
  finishes, and so does a page change while one of the tab's modals is shown. A tab keeps its stack
  while another is shown. The host's third argument sets its modals and `Animated` here; the tab's
  title and icon come from `ShellTab`, the bar colors from the Shell. A route page can't have a modal
  `PresentationMode`: show it through the host's `Modals`.
- **Routes with results.** A deep link is the selected tab plus the routes for that tab's handle
  (`SetState`); Reactor never navigates Shell by URI. For a back with a result (`..?refresh=true`),
  put the result in app state or a context the destination reads, then `GoBack()`: the destination
  stays mounted. (A result in the destination's route, through `PopTo` and `Replace`, remounts it.)
- **Shell properties.** `.FlyoutBehavior(…)`, `.FlyoutIsPresented(isPresented, onChanged)`,
  `.FlyoutHeader(…)`, `.FlyoutFooter(…)`, `.FlyoutContent(…)` (elements, updated in place) and the
  appearance colors (`with { TabBarBackgroundColor = … }`).
- **Page properties in a Shell.** `.NavBarIsVisible(…)`, `.TabBarIsVisible(…)`, `.TitleView(element)`,
  and `with { SearchHandler = …, PresentationMode = …, BackButtonBehavior = new ShellBackButton(…) }`.
- With `using static Microsoft.UI.Reactor.Maui.Factories;`, `Shell` names the factory; write
  `Microsoft.Maui.Controls.Shell` for MAUI's type.

## Hosting

- `ReactorWindow(root, registry)`: the whole window is Reactor. The root can render a page
  (`NavigationStack`, `Tabs`, `Flyout`, `Page`) or a plain view.
- `ReactorPage(root, registry)`: a `ContentPage` with Reactor content, for an existing app. It
  works as a Shell route through `ReactorPage.Template(() => Component<Settings>())`, or pushed onto a
  XAML navigation stack. Call `Unmount()` when the page is discarded for good.

Both raise `UnhandledException` for errors that no `ErrorBoundary` caught. By then the tree has been
unmounted.

## Lists

```csharp
CollectionView(contacts, c => c.Id.ToString(), c =>
    HStack(12, Label(c.Name).Bold(), Label(c.Role)))
    with
{
    SelectionMode = SelectionMode.Single,
    SelectedKey = selectedId,
    OnSelectionChanged = keys => setSelectedId(keys.FirstOrDefault()),
    RemainingItemsThreshold = 5,
    OnRemainingItemsThresholdReached = LoadMore,
}
```

Each item renders its own component tree; the list only creates trees for the items on screen.
Items are matched by key across updates, and selection is controlled.

## Hot reload

Reactor registers a .NET Hot Reload handler: after an edit is applied, every live Reactor tree
re-renders, keeping component state.

## Samples and tests

- [`samples/maui/Reactor.Maui.Sample`](https://github.com/microsoft/microsoft-ui-reactor/tree/main/samples/maui/Reactor.Maui.Sample):
  a gallery app with tabs, a navigation stack, controls, a `CollectionView` and the app hooks. Set
  `REACTOR_RENDERER=direct` to use the direct renderer, or `REACTOR_SAMPLE_ROUTE=<demo>` to open a
  demo at startup.
- [`tests/Reactor.Maui.DeviceTests`](https://github.com/microsoft/microsoft-ui-reactor/tree/main/tests/Reactor.Maui.DeviceTests):
  fixtures that run against the real platform controls (`run.sh maccatalyst|ios|android`).
