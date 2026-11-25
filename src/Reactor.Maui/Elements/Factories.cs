using System.Diagnostics.CodeAnalysis;
using Microsoft.Maui.Graphics;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Maui;

/// <summary>
/// The DSL entry point: <c>using static Microsoft.UI.Reactor.Maui.Factories;</c>.
/// Re-exposes the core factories next to the MAUI control factories.
/// </summary>
public static class Factories
{
    // ── Composition (from Reactor.Core) ─────────────────────────────────────

    public static ComponentElement Component<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>()
        where T : Component, new() => CoreFactories.Component<T>();

    public static ComponentElement<TProps> Component<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T, TProps>(TProps props)
        where T : Component<TProps>, new() => CoreFactories.Component<T, TProps>(props);

    public static GroupElement Group(params Element?[] children) => CoreFactories.Group(children);

    public static EmptyElement Empty() => CoreFactories.Empty();

    public static FuncElement RenderEachTime(Func<RenderContext, Element?> render) => CoreFactories.RenderEachTime(render);

    public static MemoElement Memo(Func<RenderContext, Element?> render, params object?[] dependencies) =>
        CoreFactories.Memo(render, dependencies);

    public static ErrorBoundaryElement ErrorBoundary(Element? child, Func<Exception, Element?> fallback) =>
        CoreFactories.ErrorBoundary(child, fallback);

    public static GroupElement ForEach<T>(IEnumerable<T> items, Func<T, string> key, Func<T, Element?> render) =>
        CoreFactories.ForEach(items, key, render);

    // ── Text and display ────────────────────────────────────────────────────

    public static LabelElement Label(string text) => new(text);

    /// <summary>An image from a file, an embedded resource name or an <c>http(s)</c> URL.</summary>
    public static ImageElement Image(string source) => new(ToImageSource(source));

    public static ImageElement Image(ImageSource? source) => new(source);

    public static BoxViewElement BoxView(Color? color = null) => new(color);

    public static ActivityIndicatorElement ActivityIndicator(bool isRunning = true) => new(isRunning);

    public static ProgressBarElement ProgressBar(double progress) => new(progress);

    public static BorderElement Border(Element? content) => new(content);

    public static GraphicsViewElement GraphicsView(IDrawable drawable) => new(drawable);

    public static WebViewElement WebView(string url) => new(url);

    public static WebViewElement HtmlView(string html) => new(null, html);

    // ── Input ───────────────────────────────────────────────────────────────

    public static ButtonElement Button(string text, Action? onClick = null) => new(text, onClick);

    public static ImageButtonElement ImageButton(string source, Action? onClick = null) => new(ToImageSource(source), onClick);

    public static EntryElement Entry(string text, Action<string>? onTextChanged = null, string? placeholder = null) =>
        new(text, onTextChanged) { Placeholder = placeholder };

    public static EditorElement Editor(string text, Action<string>? onTextChanged = null, string? placeholder = null) =>
        new(text, onTextChanged) { Placeholder = placeholder };

    public static SearchBarElement SearchBar(string text, Action<string>? onTextChanged = null, string? placeholder = null) =>
        new(text, onTextChanged) { Placeholder = placeholder };

    public static CheckBoxElement CheckBox(bool isChecked, Action<bool>? onCheckedChanged = null) => new(isChecked, onCheckedChanged);

    public static SwitchElement Switch(bool isToggled, Action<bool>? onToggled = null) => new(isToggled, onToggled);

    public static SliderElement Slider(double value, Action<double>? onValueChanged = null, double minimum = 0, double maximum = 1) =>
        new(value, onValueChanged) { Minimum = minimum, Maximum = maximum };

    public static StepperElement Stepper(double value, Action<double>? onValueChanged = null, double minimum = 0, double maximum = 100, double increment = 1) =>
        new(value, onValueChanged) { Minimum = minimum, Maximum = maximum, Increment = increment };

    public static PickerElement Picker(IReadOnlyList<string> items, int selectedIndex, Action<int>? onSelectedIndexChanged = null, string? title = null) =>
        new(items, selectedIndex, onSelectedIndexChanged) { Title = title };

    public static DatePickerElement DatePicker(DateTime date, Action<DateTime>? onDateSelected = null) => new(date, onDateSelected);

    public static TimePickerElement TimePicker(TimeSpan time, Action<TimeSpan>? onTimeSelected = null) => new(time, onTimeSelected);

    public static RadioButtonElement RadioButton(string content, bool isChecked, Action<bool>? onCheckedChanged = null, string? groupName = null) =>
        new(content, isChecked, onCheckedChanged) { GroupName = groupName };

    // ── Layout ──────────────────────────────────────────────────────────────

    public static VStackElement VStack(params Element?[] children) => new(children);

    public static VStackElement VStack(double spacing, params Element?[] children) => new(children) { Spacing = spacing };

    public static HStackElement HStack(params Element?[] children) => new(children);

    public static HStackElement HStack(double spacing, params Element?[] children) => new(children) { Spacing = spacing };

    /// <summary>A grid with row and column definitions such as <c>"Auto,*,2*"</c>. Place children with <c>.Grid(row, column)</c>.</summary>
    public static GridElement Grid(string rows, string columns, params Element?[] children) =>
        new(children) { Rows = rows, Columns = columns };

    public static FlexLayoutElement FlexLayout(params Element?[] children) => new(children);

    public static AbsoluteLayoutElement AbsoluteLayout(params Element?[] children) => new(children);

    public static ScrollViewElement ScrollView(Element? content) => new(content);

    public static RefreshViewElement RefreshView(Element? content, bool isRefreshing, Action? onRefresh) => new(content, isRefreshing, onRefresh);

    public static SwipeViewElement SwipeView(Element? content) => new(content);

    // ── Pages and navigation ────────────────────────────────────────────────

    public static PageElement Page(string? title, Element? content) => new(title, content);

    /// <summary>A navigation stack of <paramref name="pages"/>, root first. <paramref name="onPopped"/> receives the new depth after a back gesture.</summary>
    public static NavigationStackElement NavigationStack(IEnumerable<Element?> pages, Action<int>? onPopped = null) =>
        new([.. pages]) { OnPopped = onPopped };

    /// <summary>
    /// Shows <paramref name="nav"/>'s back stack and current route as a navigation stack, each route
    /// mapped to its page by <paramref name="routeMap"/>, and provides <paramref name="nav"/> to the
    /// pages, so a descendant can call <c>UseNavigation&lt;TRoute&gt;()</c>. Navigating through the
    /// handle pushes or pops pages; the user's back button, gesture or Android back key goes back
    /// through the handle, so guards and lifecycle callbacks run (spec 069 §2.4).
    /// </summary>
    /// <param name="nav">The handle from <c>UseNavigation</c>.</param>
    /// <param name="routeMap">The page for a route.</param>
    /// <param name="stack">
    /// Configures the stack the host renders: its title and icon as a tab or flyout detail, bar colors,
    /// modals, and <see cref="NavigationStackElement.Animated"/>, the host's default animation. The host
    /// sets <see cref="NavigationStackElement.Pages"/> and <see cref="NavigationStackElement.OnPopped"/>.
    /// <c>NavigateOptions.Animated</c> overrides the default for the <c>Navigate</c> call it is passed
    /// to; every other change (<c>GoBack</c>, <c>GoForward</c>, <c>Replace</c>, <c>Reset</c>,
    /// <c>PopTo</c>, <c>SetState</c>, the user's back) uses the default.
    /// </param>
    public static ComponentElement NavigationHost<TRoute>(
        Navigation.NavigationHandle<TRoute> nav,
        Func<TRoute, Element?> routeMap,
        Func<NavigationStackElement, NavigationStackElement>? stack = null)
        where TRoute : notnull
    {
        ArgumentNullException.ThrowIfNull(nav);
        ArgumentNullException.ThrowIfNull(routeMap);
        return CoreFactories.Component<Navigation.NavigationHostComponent<TRoute>, Navigation.NavigationHostProps<TRoute>>(new(nav, routeMap, stack));
    }

    public static TabsElement Tabs(int selectedIndex, Action<int>? onSelectedIndexChanged, params Element?[] pages) =>
        new(pages, selectedIndex, onSelectedIndexChanged);

    public static FlyoutElement Flyout(Element? flyout, Element? detail, bool isPresented, Action<bool>? onIsPresentedChanged = null) =>
        new(flyout, detail, isPresented, onIsPresentedChanged);

    // ── Shell (spec 069 §4) ─────────────────────────────────────────────────

    /// <summary>
    /// A Shell of <paramref name="items"/> (<c>TabBar</c>, <c>FlyoutItem</c> and <c>MenuItem</c>
    /// elements). <paramref name="selected"/> is the key of the tab or top tab shown;
    /// <paramref name="onSelected"/> receives the key the user switched to, once Shell shows it. Update
    /// the state to accept it, or the declared tab is shown again. A key that matches no tab shows the
    /// first one and reports it once.
    /// </summary>
    public static ShellElement Shell(string? selected, Action<string>? onSelected, params Element?[] items) => new(selected, onSelected, items);

    /// <summary>A Shell item shown as a bottom tab bar. It is keyed by its position unless given a key with <c>WithKey</c>.</summary>
    public static TabBarElement TabBar(params ShellTabElement[] tabs) => new(tabs);

    /// <summary>A Shell item listed in the flyout. It is keyed by its position unless given a key with <c>WithKey</c>.</summary>
    public static FlyoutItemElement FlyoutItem(string? title, ImageSource? icon, params ShellTabElement[] tabs) => new(title, icon, tabs);

    /// <summary>
    /// A Shell tab showing <paramref name="content"/>: a <c>Page(…)</c>, or a <c>NavigationHost(…)</c>,
    /// whose routes become the tab's navigation stack. <paramref name="key"/> names it in the Shell's
    /// selection and is unique across the Shell.
    /// </summary>
    public static ShellTabElement ShellTab(string key, string? title, ImageSource? icon, Element? content)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new ShellTabElement(title, icon, content) { Key = key };
    }

    /// <summary>A Shell tab with top tabs, each a <c>ShellContent(…)</c> holding a page.</summary>
    public static ShellTabElement ShellTab(string key, string? title, ImageSource? icon, params ShellContentElement[] contents)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new ShellTabElement(title, icon, null) { Key = key, Contents = contents };
    }

    /// <summary>A top tab holding <paramref name="page"/>. <paramref name="key"/> names it in the Shell's selection.</summary>
    public static ShellContentElement ShellContent(string key, string? title, ImageSource? icon, Element? page)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new ShellContentElement(title, icon, page) { Key = key };
    }

    /// <summary>A flyout entry that runs <paramref name="onClick"/>.</summary>
    public static ShellMenuItemElement MenuItem(string text, Action? onClick, ImageSource? icon = null) => new(text, onClick) { Icon = icon };

    // ── Virtualized collections ─────────────────────────────────────────────

    /// <summary>A virtualized list of <paramref name="items"/>, matched across renders by <paramref name="key"/>.</summary>
    public static CollectionViewElement CollectionView<T>(IReadOnlyList<T> items, Func<T, string> key, Func<T, Element?> itemTemplate) =>
        new(new ItemsAdapter<T>(items), item => key((T)item!), item => itemTemplate((T)item!));

    public static CarouselViewElement CarouselView<T>(
        IReadOnlyList<T> items, Func<T, string> key, Func<T, Element?> itemTemplate, int position, Action<int>? onPositionChanged = null) =>
        new(new ItemsAdapter<T>(items), item => key((T)item!), item => itemTemplate((T)item!), position, onPositionChanged);

    private static ImageSource ToImageSource(string source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? ImageSource.FromUri(uri)
            : ImageSource.FromFile(source);
}
