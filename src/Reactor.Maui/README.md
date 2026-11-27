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
`Microsoft.UI.Reactor.Maui.Hosting`, `Microsoft.UI.Reactor.Maui.Direct` and `Microsoft.UI.Reactor.Input`
(element refs), plus `Element` and
`NavigationMode` aliases, because MAUI's own `Microsoft.Maui.Controls.Element` and
`Microsoft.Maui.ApplicationModel.NavigationMode` would otherwise make the bare names ambiguous. It also
aliases `PenLineCap`, `PenLineJoin` and `FillRule` for shapes: they live in
`Microsoft.Maui.Controls.Shapes`, and importing that namespace would bring in its `Path` and `Rectangle`
too. The factories are a `using static` you add per file, since names such as `Grid` and
`Label` would otherwise shadow MAUI's types everywhere. Set
`<ReactorImplicitUsings>false</ReactorImplicitUsings>` in the project to opt out of the global usings;
then add the namespaces you use per file, and the aliases as file-level ones, for example
`using PenLineJoin = Microsoft.Maui.Controls.Shapes.PenLineJoin;`.

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
| Shapes and brushes | `Rectangle`, `Ellipse`, `RoundRectangle`, `Line`, `Polygon`, `Polyline`, `Path2D`; `LinearGradientBrush`, `RadialGradientBrush` |
| Input | `Button`, `ImageButton`, `Entry`, `Editor`, `SearchBar`, `CheckBox`, `Switch`, `Slider`, `Stepper`, `Picker`, `DatePicker`, `TimePicker`, `RadioButton` |
| Formatted text | `Label(Run(…), Hyperlink(…))` |
| Layout | `VStack`, `HStack`, `Grid`, `FlexLayout`, `AbsoluteLayout`, `ScrollView`, `Border`, `ContentView`, `RefreshView`, `SwipeView` |
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
| `UseDialogs()` | Alerts, action sheets and prompts for the component's window (below). |
| `UseSystemBackButton(onBack)` | Handle Android's back key (below). |
| `UseWindowLifecycle(…)` | The window's `Activated`, `Deactivated`, `Stopped`, `Resumed` and `Destroying` (below). |
| `UseElementRef<T>()`, `UseElementFocus()` | A ref to the view an element renders, for focusing, scrolling and other calls from code; a ref plus a `RequestFocus` that waits until the view can take focus (below). |

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

## Refs: calling the view from code

`UseElementRef<T>()` returns a ref, the same one every render. Attach it with `.Ref(...)`, then call
the MAUI view through `Current` in an event handler or an effect (the names are WinUI Reactor's):

```csharp
sealed class Search : Component
{
    public override Element Render()
    {
        var (query, setQuery) = UseState("");
        var input = UseElementRef<Entry>();
        var results = UseElementRef<ScrollView>();
        var first = UseElementRef<IView>();

        return VStack(8,
            Entry(query, setQuery, placeholder: "Search").Ref(input),
            HStack(8,
                Button("Clear", () => { setQuery(""); input.Current?.Focus(); }),
                Button("Top", async () =>
                {
                    if (results.Current is { } scroll) await scroll.ScrollToAsync(first, ScrollToPosition.Start, animated: true);
                })),
            ScrollView(VStack(Label("Results").Ref(first), Label($"Matches for '{query}'"))).Ref(results).Height(240));
    }
}
```

- **Lifetime.** `Current` is set when the element mounts, before its effects run, and is `null` once
  it unmounts (children unmount first, so it is already `null` in a parent's cleanup). A ref names one
  element: attached to several, the last one mounted wins. A list cell's tree, and its refs, go away
  when the list clears the cell's item.
- **The type.** `ElementRef<Entry>` is always a Controls `Entry`: with the direct renderer, an element
  whose ref asks for a type its direct view isn't renders as a Controls view. `ElementRef<IView>` (or
  `IEntry`) keeps the direct view, which can still be focused. A ref of the wrong type for its element
  is an error that names both types. A component can't take `.Ref` itself; pass the ref as a prop to
  the view it means. To make a ref outside a render (a test, a field), use `TypedElementRef.Create<T>()`.
- **A ref holds its view.** One kept outside its tree keeps the view alive until the tree unmounts;
  call `ReactorPage.Unmount()` for a page you discard.
- **The UI thread.** Read `Current` and call the view on the UI thread.
- **Focus when a view appears.** Android can't focus a view in the first effect of a newly shown
  tree. `UseElementFocus()` returns a ref and a `RequestFocus` that focuses as soon as the view is on
  screen (a Controls view's `Loaded`; for a direct view, its platform view joining a window), from any
  thread. A newer request replaces a pending one, and a request is dropped when the ref stops naming
  the view it waits for:

  ```csharp
  var (field, requestFocus) = UseElementFocus();
  UseEffect(requestFocus, Array.Empty<object>());
  return Entry(name, setName).Ref(field);
  ```
- **Hot reload** keeps a ref while the component's hooks keep their shape; an edit that changes the
  ref's type starts the component's hooks over.
- **Scrolling to an element.** `scroll.ScrollToAsync(ref, position, animated)` works for any view the
  ref names, inside Controls or direct layouts (MAUI's own `ScrollToAsync(Element, …)` is right only
  when every layout between the target and the scroll view is a Controls layout). It returns `true`
  once the scroll finished at the target, and `false` when nothing was requested (the ref is empty, a
  view between the target and the scroll view isn't laid out yet, or the orientation is `Neither`) or
  the scroll wasn't confirmed there (a later request superseded it, the scroll view went away, or the
  scroll ended elsewhere); `false` doesn't mean it didn't move. Call it after layout, from an event or
  a view's `Loaded`. MAUI's own overloads can leave a superseded `await` pending; this one always
  completes, on any `ScrollView`, including one Reactor didn't create, and returns `false` if MAUI's
  dispatch throws or the scroll view's handler is replaced. If the platform never reports the end of
  the scroll (a touch can stop an animation), the task completes once a bound above the platform's
  longest scroll animation has passed (1.5 s on iOS and Mac Catalyst; 1 s times the animator duration
  scale, plus 1 s, on Android): `true` if the scroll view is at the target then, else `false`. The `ScrollView` element renders a `ScrollView` subclass, so an implicit style for it
  needs `ApplyToDerivedTypes`, as for `CollectionView`.

### Web views

`OnNavigating` is asked for each top-level navigation; set `Cancel` to refuse it, before the callback
returns (the platform waits for the answer). iOS, Mac Catalyst, and Android for a link or a declared
`Url`, ask before loading; Android asks `Html`, `Reload`, `GoBack` and `GoForward` only once loading
has started. It isn't exactly once (Android can ask twice), so answer the same way for the same URL.
Subresources and frames aren't asked, so it is a navigation policy, not a security boundary. A
throwing callback refuses the navigation and reaches the error boundaries.

`OnNavigated` reports each completion with its `Result` (`Success`, `Cancel`, `Failure`, `Timeout`),
after the platform callback, so `CanGoBack` is current. A refused navigation never completes with
`Success`: one refused before loading (iOS, Mac Catalyst, an Android link or declared `Url`) reports
no completion and the page stays; one Android refuses late (`Html`, `Reload`, `GoBack`, `GoForward`)
completes with `Cancel`, and what stays on screen is the platform's.

`Url` is the page you request, not the page shown: Reactor loads it when it changes. Keep the
current page in separate state; rendering `OnNavigated`'s URL back into `Url` loads it again.

```csharp
static bool IsDocs(string url) =>
    Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
    && string.Equals(uri.IdnHost, "learn.microsoft.com", StringComparison.OrdinalIgnoreCase);

var (requested, setRequested) = UseState("https://learn.microsoft.com/dotnet/maui");
var (current, setCurrent) = UseState<string?>(null);   // what the view shows; never fed back into Url
var web = UseElementRef<WebView>();
return VStack(
    Button("Back", () => web.Current?.GoBack()),
    (WebView(requested) with
    {
        OnNavigating = e => e.Cancel = !IsDocs(e.Url),
        OnNavigated = e => { if (e.Result == WebNavigationResult.Success) setCurrent(e.Url); },
    }).Ref(web).Height(400));
```

### The caret and selection

`Entry`'s `CursorPosition` and `SelectionLength` move the caret and the selection when their values
change, as WinUI's `SelectionStart` does; the user moves them freely afterwards and nothing is undone.
`OnSelectionChanged(selectedText, cursor, length)` reports the platform's selection once per change,
after the platform callback; a selection you declared isn't reported, a caret a text change (or a
keyboard, password or `MaxLength` change) moved is.

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

### Flyouts and split mode

On tablets and desktops a flyout can be *split*: shown beside the detail all the time.

```csharp
public sealed class MailApp : Component
{
    public override Element Render()
    {
        var (open, setOpen) = UseState(false);
        return Flyout(
            Page("Folders", VStack(Button("Inbox", () => setOpen(false)))),
            NavigationStack([Page("Inbox", Label("No new mail"))]),
            open, setOpen) with { LayoutBehavior = FlyoutLayoutBehavior.Split };
    }
}
```

While the flyout is split, `IsPresented` is the platform's: Reactor doesn't write it (MAUI refuses
`false` by throwing, and a refused write leaves the property stuck for the page's life), and
`onIsPresentedChanged` doesn't report what MAUI sets itself. When a render or the device leaves split
mode, the declared value applies again. With MAUI 11, an iPad or a Mac splits a flyout whose
behavior is `Split` or `SplitOnPortrait`, in any orientation, and never the others (MAUI reads a
portrait orientation there); an Android tablet splits per orientation; a phone never does. Code
outside Reactor that sets `IsPresented = false` on the page while it is split (through
`Window.Page`, say) still makes MAUI throw.

Whether a flyout is split isn't exposed, deliberately: base split-dependent UI on the device instead
(`DeviceInfo.Idiom`, or the window's size from `UseWindowSize`), and keep the menu reachable either
way.

### Toolbar items

```csharp
Page("Inbox", Label("No new mail")) with
{
    ToolbarItems =
    [
        new ToolbarAction("Compose", compose),
        new ToolbarAction("Settings", openSettings) { Order = ToolbarItemOrder.Secondary },
    ],
}
```

Items appear in their declared order: primary ones in the navigation bar, secondary ones in its
overflow menu. A render that only changes the callbacks keeps the platform's items (each runs the
current callback); a changed text, icon, order or priority rebuilds them. An item a rebuild retired
invokes nothing, even if the platform or your code still holds it.

### Progress bars

`ProgressBar(progress)` renders MAUI's `ProgressBar` (implicit styles apply as usual). On Android,
MAUI 11 leaves a cleared `ProgressColor` drawn
(it sets a tint and clears a filter); Reactor completes the clear on its own progress bars, so they
return to the theme's color. A MAUI `ProgressBar` you create yourself keeps MAUI's behavior.

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
- **The flyout's presentation follows the behavior.** `FlyoutIsPresented` is yours only while the
  Shell's effective behavior is `Flyout`. MAUI takes the nearest behavior set on the current page,
  its content, its section or its item, then the root's (`.FlyoutBehavior(…)`), then the item's
  default (a `FlyoutItem` has a flyout, a `TabBar` has none). Under `Locked` the pane is shown and under `Disabled` it is hidden, on
  iOS, Mac Catalyst and Android alike: MAUI forces the presentation again at every page change, so
  Reactor writes nothing then and reports nothing. Changing the behavior or the selection never
  reports MAUI's resulting open or close as the user's; once the flyout is a flyout again, your
  declaration is applied. (MAUI itself differs per platform here: iOS and Mac Catalyst show a locked
  pane whatever the property says, Android follows the property, and Android's drawer opens only if
  it was created before; Reactor evens these out.)
- **Page properties in a Shell.** `.NavBarIsVisible(…)`, `.TabBarIsVisible(…)`, `.TitleView(element)`,
  and `with { SearchHandler = …, PresentationMode = …, BackButtonBehavior = new ShellBackButton(…) }`.
- **A page's or view's `AutomationId`** (`with { AutomationId = … }` on a page, `.AutomationId(…)` on
  a view) is set once, when it mounts: MAUI doesn't allow changing it. A later change is ignored and
  logged once per page or view as a Reactor warning; to use a new id, give it a new key so it mounts
  again.
- With `using static Microsoft.UI.Reactor.Maui.Factories;`, `Shell` names the factory; write
  `Microsoft.Maui.Controls.Shell` for MAUI's type.

## Dialogs

`UseDialogs()` returns MAUI's page dialogs for the component's window. The object is stable, so a
handler can await it:

```csharp
public sealed class DeleteButton : Component<Action>
{
    public override Element Render()
    {
        var dialogs = UseDialogs();
        return Button("Delete", async () =>
        {
            if (await dialogs.Alert("Delete?", "This can't be undone.", "Delete", "Cancel")) Props();
        });
    }
}
```

`Alert(title, message, close)` has one button; `ActionSheet(title, cancel, destruction, buttons…)`
returns the button chosen; `Prompt(title, message, …)` returns the text, or `null` when cancelled.
The same dialogs are elements, for a dialog that follows state, as WinUI's `ContentDialog` does:

```csharp
public sealed class Rename : Component<Action<string>>
{
    public override Element Render()
    {
        var (renaming, setRenaming) = UseState(false);
        return VStack(
            Button("Rename", () => setRenaming(true)),
            Prompt("Rename", "New name") with
            {
                IsOpen = renaming,
                InitialValue = "Untitled",
                OnClosed = name =>
                {
                    setRenaming(false);
                    if (name is not null) Props(name);
                },
            });
    }
}
```

A window shows one dialog at a time, in the order they were requested, on its top modal or else its
page; the next waits until the previous one has left the screen, and while a navigation runs. The
awaited dialogs never throw, so an `async` handler like the one above is safe: a dialog the user
can't answer (its window was destroyed, or it couldn't be shown) completes with the cancel answer,
`false` or `null`. One that couldn't be shown is also logged and reported to the component's
`ErrorBoundary`, if it has one. An element's answer reaches `OnClosed` once, and only if the element
is still open: closing, re-opening or unmounting it first drops that answer (a dialog already on
screen stays until the user answers it; MAUI can't close one from code). The answer goes to the
`OnClosed` of the render whose title and buttons the user saw, so a dialog showing "Delete A?" never
runs a later render's callback for B.

## The back key

`UseSystemBackButton(onBack)` handles Android's back key: return `true` when you handled it.

```csharp
public sealed class Editor : Component<Action>
{
    public override Element Render()
    {
        var (dirty, setDirty) = UseState(true);
        var dialogs = UseDialogs();
        UseSystemBackButton(() =>
        {
            if (!dirty) return false;
            _ = Discard();
            return true;
        });
        return Page("Editor", Entry("draft", _ => setDirty(true)));

        async Task Discard()
        {
            if (await dialogs.Alert("Discard changes?", null, "Discard", "Keep")) Props();
        }
    }
}
```

Only the page on screen is asked (not the pages below it, nor other tabs), along with components
above every page: the deepest first. When no handler takes the back, it goes on to the
`NavigationHost` or Shell (a guarded back), then to Android. `UseSystemBackButton(nav)` is WinUI's
shape: the key goes back in `nav` whenever `nav.CanGoBack`, even if a guard refuses. It is Android
only (Windows' back button comes later); iOS has no back key, and its back button and swipe go
through the stack's guards. While a handler is registered, Android doesn't show its "back to home"
preview animation at the root, because Reactor takes the key first; without one, a `Page` or `Tabs`
root keeps it.

## Window lifecycle

`UseWindowLifecycle(onActivated, onDeactivated, onStopped, onResumed, onDestroying)` subscribes to
MAUI's window events while the component is mounted, for example to save work:

```csharp
public sealed class Autosave : Component<Action>
{
    public override Element Render()
    {
        var save = Props;
        UseWindowLifecycle(onStopped: save, onDestroying: save);
        return Label("Editing");
    }
}
```

A `ReactorWindow` calls `onDestroying` before it unmounts its tree. An error thrown there skips the
error boundaries (the tree is about to go): it reaches the window's `UnhandledException` once the
tree has been unmounted.

`PageElement.ModalPresentationStyle` sets how iOS presents a page shown as a modal. Import the enum
with an alias; importing the whole `iOSSpecific` namespace brings its own `Page`, `Entry` and others,
which clash with MAUI's:

```csharp
using UIModalPresentationStyle = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.UIModalPresentationStyle;

NavigationStack([Page("Notes", list)]) with
{
    Modals = editing ? [Page("Edit", editor) with { ModalPresentationStyle = UIModalPresentationStyle.PageSheet }] : [],
}
```

It isn't applied on Mac Catalyst, where MAUI can't dismiss a sheet; modals stay full screen there.

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
Items are matched by key across updates, and selection is controlled: a tap is reported, and the
list shows what the app declares (a refused tap is undone). Multiple selection compares the keys
as a set. On iOS and Mac Catalyst, Reactor also checks the native selection after MAUI's deferred
batch updates and repairs it, without reporting anything.

**Drawing the selection.** Give the template the item's `ItemState` to draw a selected item
yourself, instead of MAUI's `Selected` visual state. `IsSelected` is the declared selection
(`None` selects nothing, `Single` compares `SelectedKey`, `Multiple` looks in `SelectedKeys`), so a
refused tap never renders selected, and a recycled cell renders its new item's state. This overload
turns off the platform's own highlight (`ShowsSelectionHighlight = false`):

```csharp
CollectionView(products, p => p.Id, (p, state) =>
    Border(Label(p.Name).Padding(12))
        .Stroke(state.IsSelected ? Colors.White : Colors.Gray, state.IsSelected ? 2 : 1)
        .CornerRadius(8))
    with
{
    Arrangement = ItemsArrangement.VerticalGrid,
    Span = 2,
    ItemSpacing = 12,
    SelectionMode = SelectionMode.Single,
    SelectedKey = selected,
    OnSelectionChanged = keys => setSelected(keys.FirstOrDefault()),
    Header = Label("Menu").FontSize(24),
    EmptyView = Label("Nothing on the menu today"),
}
```

**Groups.** The grouped overload takes the groups, their keys and items, and optional header and
footer templates. Item keys are unique across the whole list. An item that moves to another group
is removed from one and inserted in the other; its component state survives only if MAUI rebinds
the same cell to it directly.

```csharp
CollectionView(teams, t => t.Name, t => t.Members, m => m.Id,
    m => Label(m.Name),
    groupHeader: t => Label(t.Name).Bold(),
    groupFooter: t => Label($"{t.Members.Count} members"))
```

**Errors in templates.** The item, group header and group footer templates run when the platform
shows a cell. An exception they throw goes to the `ErrorBoundary` above the list, like an exception
in the cell's own components, rather than into the platform.

**Scrolling from code.** Through a ref, MAUI's `ScrollTo(index, …)` works as is. To scroll to an
item by key, in any group, use `ScrollToKey` (MAUI's `ScrollTo(item, …)` can't take your item,
because the list holds its own boxes): `list.Current?.ScrollToKey(specialId)`.

## Swipe actions

```csharp
var swipe = UseElementRef<SwipeView>();
(SwipeView(Label(task.Title).Padding(12)) with
{
    LeftItems = [new SwipeItemData("Done", complete) { Content = Label("✓").Padding(16) }],
    LeftItemsMode = SwipeMode.Execute,
    RightItems = [new SwipeItemData("Delete", delete) { BackgroundColor = Colors.Red }],
    Threshold = 80,
    OnSwipeEnded = (direction, isOpen) => setOpen(isOpen),
}).Ref(swipe)
```

Items go on four sides (`LeftItems`, `RightItems`, `TopItems`, `BottomItems`), each with its mode:
`Reveal` leaves the items open, `Execute` runs the first one once the swipe passes `Threshold`. An
item with `Content` shows that element, and its `Text` becomes its accessible name. A side is rebuilt
only when its count, mode, or an item's kind or text changes, or a text item's color or icon (a
content item shows its content, so those don't count), and a re-render doesn't close an open swipe:
content re-renders in place. An item the platform still holds after a rebuild invokes nothing. Open and close it through
a ref with MAUI's own methods: `swipe.Current?.Open(OpenSwipeItem.RightItems)`,
`swipe.Current?.Close()`. They do nothing before the view is shown.

## Radio buttons

```csharp
VStack(8,
    (RadioButton(ThemeCard("Light", theme == "light"), theme == "light",
        on => { if (on) setTheme("light"); }, groupName: "theme") with { IsIndicatorVisible = false })
        .Semantics(description: "Light theme"),
    (RadioButton(ThemeCard("Dark", theme == "dark"), theme == "dark",
        on => { if (on) setTheme("dark"); }, groupName: "theme") with { IsIndicatorVisible = false })
        .Semantics(description: "Dark theme"))
```

A radio button takes a text or any element as its content (`ContentElement`). With an element it
shows MAUI's indicator next to the content, or, with `IsIndicatorVisible = false`, only the content:
render the checked look from your state, instead of a `ControlTemplate` with a `Checked` visual
state. Give it an accessible name with `.Semantics(description: …)` (or the text overload's text);
without one, a warning is logged once.

Reactor groups radio buttons itself, on both renderers: buttons with the same `GroupName` on one
page, or unnamed buttons in the same layout. A tap checks the button, unchecks its peers on the
platform, and then reports each change (the peers' unchecks first), so a callback already sees the
group settled. A check made from inside such a callback (through a ref) runs on the next turn, after
the first one. Don't write a button's `IsChecked` through a ref from its own callback: MAUI delays
that write, and on Android a text radio button then flips back and forth forever; change your state
instead. Two buttons declared checked in one group are both shown checked, and a warning is
logged. MAUI's `RadioButtonGroup` attached properties (`GroupName`, `SelectedValue`) are not
supported: setting them through `.Set(...)` rewrites the native group names, and peers then uncheck
without a report. Use the `GroupName` argument.

## Pickers and formatted text

`Picker(projects, p => p.Name, index, setIndex, title: "Project")` shows a text per item; the
selection is the index.

A label of formatted runs uses WinUI's `Run` and `Hyperlink` with the label's modifiers:

```csharp
Label(
    Run("Total ").TextColor(Colors.Gray),
    Run($"{total:C}").FontSize(28).Bold(),
    Hyperlink(" details", () => setShowDetails(true)))
```

A hyperlink is underlined unless it declares `TextDecorations`. Runs are updated in place while their
number stays the same. On the direct renderer, a label with runs renders as a Controls label.

## Shapes, brushes and images

Shapes use WinUI's names (`Rectangle`, `Ellipse`, `Line`, `Path2D`, so it doesn't collide with
`System.IO.Path`) and MAUI's for the rest (`RoundRectangle`, `Polygon`, `Polyline`). They fill and
stroke with brushes; a `Color` works wherever a brush does, and `null` removes the fill or stroke:

```csharp
// A settings radio indicator: two circles.
Grid("auto", "auto",
    Ellipse().Stroke(Colors.DarkGray, 0.5).Fill(Colors.White).Size(16, 16).Center(),
    selected ? Ellipse().Stroke(Colors.SteelBlue).Fill(Colors.SteelBlue).Size(8, 8).Center() : null)

Polyline([.. values.Select((v, i) => new Point(i * 8, 40 - v * 40))])
    .Stroke(LinearGradientBrush(new(0, 0), new(1, 0), Colors.SkyBlue, Colors.Navy), 2)
    .StrokeLineJoin(PenLineJoin.Round)

Path2D("M 0,10 L 10,0 L 20,10 L 10,20 Z").Fill(Colors.Orange).Aspect(Stretch.Uniform).Size(24, 24)
```

- `Line`, `Polyline`, `Polygon` and `Path2D` take their size from their points when the layout gives
  none; `Rectangle`, `RoundRectangle` and `Ellipse` draw into the size they're given. `Aspect` stretches
  the geometry into the box.
- Point and dash arrays are copied when the element is made, so changing the array later changes
  nothing; a new array with the same values writes nothing.
- `Path2D(markup)` converts the markup only when it changes. Markup MAUI rejects falls back to the
  path's `Data` (or draws nothing) and logs a warning. `Data` and `RenderTransform` are MAUI objects
  compared by reference: create them once (`UseMemo`).
- A path whose `RenderTransform` goes away, however it's cleared (the element, a style or class style,
  a parent's resources, a ref), is redrawn untransformed. The path is MAUI's own `Path`, so implicit
  styles apply as usual.

Gradients are brushes for `.Background(…)`, a border's `.Stroke(…)` and a shape's fill and stroke.
A stroke given a brush keeps the current thickness unless you pass one; a border's stroke given a color
is 1 unit thick unless you pass one.
Points and the radius are relative to the element's bounds (0 to 1):

```csharp
ContentView(Label("Today")).Padding(16)
    .Background(LinearGradientBrush(new(0, 0), new(0, 1), (Colors.SkyBlue, 0), (Colors.Navy, 1)))

Border(content).Stroke(RadialGradientBrush(new(0.5, 0.5), 0.5, Colors.White, Colors.Gold), 2)
```

**Brushes and image sources are values.** Solid and gradient brushes are compared by value (exactly),
so a brush built in `Render` is written once, not on every render. Font, file and URI image sources are
compared by value too, so an icon built in `Render` isn't reloaded. Once rendered, don't mutate a brush
or an image source: Reactor may keep the equal one it already shows, and a mutated one can hide your
next change. To change one, render a new one.

A stream has no value to compare, so give stream images a key: a `StreamImage` with the same key is the
same image, and its `open` isn't called again.

```csharp
Image(StreamImage($"avatar:{user.Id}", () => store.OpenAvatar(user.Id)))
```

An `ImageSource.FromStream(…)` without a key is compared by reference: create it once (`UseMemo`).

`ContentView(child)` is a plain container with optional padding. Shapes, `ContentView` and a view with a
brush background render as Controls views on the direct renderer.

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
