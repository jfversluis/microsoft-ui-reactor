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

    /// <summary>A label of formatted runs: <c>Label(Run("Total ").Bold(), Run(price), Hyperlink(" details", show))</c>.</summary>
    public static LabelElement Label(params RichTextInline[] inlines) => new("") { Inlines = inlines };

    /// <summary>A run of formatted text in a label (WinUI's <c>Run</c>).</summary>
    public static RichTextRun Run(string text) => new(text);

    /// <summary>Text in a label that runs <paramref name="onClick"/> when tapped (WinUI's <c>Hyperlink</c>).</summary>
    public static RichTextHyperlink Hyperlink(string text, Action onClick)
    {
        ArgumentNullException.ThrowIfNull(onClick);
        return new(text, onClick);
    }

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

    /// <summary>
    /// A picker over <paramref name="items"/>, each shown as <paramref name="display"/> returns. The
    /// selection is an index, as in the string overload; the texts are compared, so a re-render with
    /// the same texts changes nothing.
    /// </summary>
    public static PickerElement Picker<T>(IReadOnlyList<T> items, Func<T, string> display, int selectedIndex, Action<int>? onSelectedIndexChanged = null, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(display);
        var texts = new string[items.Count];
        for (var i = 0; i < texts.Length; i++) texts[i] = display(items[i]);
        return new(texts, selectedIndex, onSelectedIndexChanged) { Title = title };
    }

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
    /// In a <c>ShellTab</c> only the modals and <c>Animated</c> apply: the title and icon come from the
    /// <c>ShellTab</c>, and the bar colors from the Shell's appearance (a warning is logged otherwise).
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

    // ── Dialogs (spec 069 §5.1) ──

    /// <summary>A declarative one-button alert. Open it with <c>with { IsOpen = …, OnClosed = … }</c>.</summary>
    public static AlertElement Alert(string title, string? message, string close) => new(title, message, null, close);

    /// <summary>A declarative two-button alert; <c>OnClosed</c> receives true for <paramref name="accept"/>.</summary>
    public static AlertElement Alert(string title, string? message, string accept, string cancel) => new(title, message, accept, cancel);

    /// <summary>A declarative action sheet; <c>OnClosed</c> receives the button chosen, or null.</summary>
    public static ActionSheetElement ActionSheet(string? title, string? cancel, string? destruction, params string[] buttons) =>
        new(title, cancel, destruction, buttons);

    /// <summary>A declarative prompt for one line of text; <c>OnClosed</c> receives the text, or null when cancelled.</summary>
    public static PromptElement Prompt(string title, string? message) => new(title, message);

    public static TabsElement Tabs(int selectedIndex, Action<int>? onSelectedIndexChanged, params Element?[] pages) =>
        new(pages, selectedIndex, onSelectedIndexChanged);

    public static FlyoutElement Flyout(Element? flyout, Element? detail, bool isPresented, Action<bool>? onIsPresentedChanged = null) =>
        new(flyout, detail, isPresented, onIsPresentedChanged);

    // ── Shell (spec 069 §4) ─────────────────────────────────────────────────

    /// <summary>
    /// A Shell of <paramref name="items"/> (<c>TabBar</c>, <c>FlyoutItem</c> and <c>ShellMenuItem</c>
    /// elements). <paramref name="selected"/> is the key of the tab or top tab shown;
    /// <paramref name="onSelectedChanged"/> receives the key the user switched to, once Shell shows it.
    /// Update the state to accept it, or the declared tab is shown again. A key that matches no tab
    /// shows the first one and reports it once.
    /// </summary>
    public static ShellElement Shell(string? selected, Action<string>? onSelectedChanged, params Element?[] items) =>
        new(selected, onSelectedChanged, items);

    /// <summary>
    /// A Shell item shown as a bottom tab bar (a null tab is skipped). It is keyed by its position unless
    /// given a key with <c>WithKey</c>.
    /// </summary>
    public static TabBarElement TabBar(params ShellTabElement?[] tabs) => new(tabs);

    /// <summary>
    /// A Shell item listed in the flyout (a null tab is skipped); set its icon with <c>.Icon(…)</c>. It is
    /// keyed by its position unless given a key with <c>WithKey</c>.
    /// </summary>
    public static FlyoutItemElement FlyoutItem(string? title, params ShellTabElement?[] tabs) => new(title, tabs);

    /// <summary>
    /// A Shell tab showing <paramref name="content"/>: a <c>Page(…)</c>, or a <c>NavigationHost(…)</c>,
    /// whose routes become the tab's navigation stack. <paramref name="key"/> names it in the Shell's
    /// selection and is unique across the Shell.
    /// </summary>
    public static ShellTabElement ShellTab(string key, string? title, Element? content, ImageSource? icon = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new ShellTabElement(title, content) { Key = key, Icon = icon };
    }

    /// <summary>
    /// A Shell tab with top tabs, each a <c>ShellContent(…)</c> holding a page (a null one is skipped);
    /// set its icon with <c>.Icon(…)</c>.
    /// </summary>
    public static ShellTabElement ShellTab(string key, string? title, params ShellContentElement?[] contents)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new ShellTabElement(title, null) { Key = key, Contents = contents };
    }

    /// <summary>A top tab holding <paramref name="page"/>. <paramref name="key"/> names it in the Shell's selection.</summary>
    public static ShellContentElement ShellContent(string key, string? title, Element? page, ImageSource? icon = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new ShellContentElement(title, page) { Key = key, Icon = icon };
    }

    /// <summary>A flyout entry that runs <paramref name="onClick"/>.</summary>
    public static ShellMenuItemElement ShellMenuItem(string text, Action? onClick, ImageSource? icon = null) => new(text, onClick) { Icon = icon };

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
