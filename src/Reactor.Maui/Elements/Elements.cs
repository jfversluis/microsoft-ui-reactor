using Microsoft.Maui.Graphics;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Maui;

// ── Text and display ────────────────────────────────────────────────────────

public sealed record LabelElement(string Text) : TextViewElement
{
    public TextDecorations? TextDecorations { get; init; }
    public TextAlignment? HorizontalTextAlignment { get; init; }
    public TextAlignment? VerticalTextAlignment { get; init; }
    public LineBreakMode? LineBreakMode { get; init; }
    public int? MaxLines { get; init; }
    public double? LineHeight { get; init; }
    public Thickness? Padding { get; init; }
    public TextTransform? TextTransform { get; init; }
    public TextType? TextType { get; init; }
}

public sealed record ImageElement(ImageSource? Source) : ViewElement
{
    public Aspect? Aspect { get; init; }
    public bool? IsAnimationPlaying { get; init; }
}

public sealed record BoxViewElement(Color? Color) : ViewElement
{
    public CornerRadius? CornerRadius { get; init; }
}

public sealed record ActivityIndicatorElement(bool IsRunning) : ViewElement
{
    public Color? Color { get; init; }
}

public sealed record ProgressBarElement(double Progress) : ViewElement
{
    public Color? ProgressColor { get; init; }
}

/// <summary>A container that draws a border, background and rounded corners around one child.</summary>
public sealed record BorderElement(Element? Content) : ViewElement
{
    public Color? Stroke { get; init; }
    public double? StrokeThickness { get; init; }
    public CornerRadius? CornerRadius { get; init; }
    public Thickness? Padding { get; init; }
}

public sealed record GraphicsViewElement(IDrawable Drawable) : ViewElement;

/// <summary>
/// A web view. <see cref="Url"/> is the source the app requests, not the page the view shows: it is
/// loaded when it changes. Keep the current page in separate state; rendering
/// <see cref="OnNavigated"/>'s URL back into <see cref="Url"/> loads it again. <see cref="Html"/>, when
/// set, wins over <see cref="Url"/>.
/// </summary>
public sealed record WebViewElement(string? Url, string? Html = null) : ViewElement
{
    /// <summary>
    /// Asked for each top-level navigation; set <c>Cancel</c> to refuse it. Synchronous: the platform
    /// waits for the answer, so <c>Cancel</c> set after an <c>await</c> does nothing. iOS, Mac Catalyst,
    /// and Android for a link or a changed <see cref="Url"/>, ask before loading; Android asks
    /// <see cref="Html"/>, a reload, back and forward only once loading has started, so refusing those
    /// leaves whatever the platform shows. Not exactly once (Android can ask twice), so give the same
    /// answer for the same URL. Subresources and frames aren't asked: this is a navigation policy, not a
    /// security boundary. A throwing callback cancels the navigation.
    /// </summary>
    public Action<WebNavigatingEventArgs>? OnNavigating { get; init; }

    /// <summary>
    /// A navigation completed, with its <c>Result</c>: <c>Success</c>, <c>Cancel</c>, <c>Failure</c> or
    /// <c>Timeout</c>. Called after the platform callback, so <c>CanGoBack</c> and <c>CanGoForward</c> are
    /// up to date. A navigation refused before it loaded reports no completion; one Android refused late
    /// completes with <c>Cancel</c>.
    /// </summary>
    public Action<WebNavigatedEventArgs>? OnNavigated { get; init; }
}

// ── Input ───────────────────────────────────────────────────────────────────

public sealed record ButtonElement(string Text, Action? OnClick = null) : TextViewElement
{
    public ImageSource? ImageSource { get; init; }
    public int? CornerRadius { get; init; }
    public Color? BorderColor { get; init; }
    public double? BorderWidth { get; init; }
    public Thickness? Padding { get; init; }
}

public sealed record ImageButtonElement(ImageSource? Source, Action? OnClick = null) : ViewElement
{
    public Aspect? Aspect { get; init; }
    public int? CornerRadius { get; init; }
    public Thickness? Padding { get; init; }
}

/// <summary>A controlled single-line text input: it always shows <see cref="Text"/>.</summary>
public sealed record EntryElement(string Text, Action<string>? OnTextChanged = null) : TextViewElement
{
    public string? Placeholder { get; init; }
    public Color? PlaceholderColor { get; init; }
    public bool? IsPassword { get; init; }
    public bool? IsReadOnly { get; init; }
    public int? MaxLength { get; init; }
    public Keyboard? Keyboard { get; init; }
    public ReturnType? ReturnType { get; init; }
    public ClearButtonVisibility? ClearButtonVisibility { get; init; }
    public TextAlignment? HorizontalTextAlignment { get; init; }
    public bool? IsSpellCheckEnabled { get; init; }
    public Action? OnCompleted { get; init; }

    /// <summary>
    /// Where the caret goes, written when this value changes (and at mount), as WinUI's
    /// <c>TextBox.SelectionStart</c>. Not controlled: the user moves the caret freely, and a move isn't
    /// undone. <see langword="null"/> never writes. Clamped to the text by the platform.
    /// </summary>
    public int? CursorPosition { get; init; }

    /// <summary>How many characters after <see cref="CursorPosition"/> are selected; written like it.</summary>
    public int? SelectionLength { get; init; }

    /// <summary>
    /// The selection changed: the selected text, the cursor position and the selection length, as the
    /// platform shows them. Reported once per burst of changes, after the platform callback. A selection
    /// Reactor wrote isn't reported; a caret a text change moved is.
    /// </summary>
    public Action<string, int, int>? OnSelectionChanged { get; init; }
}

/// <summary>A controlled multi-line text input: it always shows <see cref="Text"/>.</summary>
public sealed record EditorElement(string Text, Action<string>? OnTextChanged = null) : TextViewElement
{
    public string? Placeholder { get; init; }
    public Color? PlaceholderColor { get; init; }
    public bool? IsReadOnly { get; init; }
    public int? MaxLength { get; init; }
    public Keyboard? Keyboard { get; init; }
    public EditorAutoSizeOption? AutoSize { get; init; }
    public Action? OnCompleted { get; init; }
}

/// <summary>A controlled search box: it always shows <see cref="Text"/>.</summary>
public sealed record SearchBarElement(string Text, Action<string>? OnTextChanged = null) : TextViewElement
{
    public string? Placeholder { get; init; }
    public Action<string>? OnSearch { get; init; }
}

/// <summary>A controlled check box: it always shows <see cref="IsChecked"/>.</summary>
public sealed record CheckBoxElement(bool IsChecked, Action<bool>? OnCheckedChanged = null) : ViewElement
{
    public Color? Color { get; init; }
}

/// <summary>A controlled switch: it always shows <see cref="IsToggled"/>.</summary>
public sealed record SwitchElement(bool IsToggled, Action<bool>? OnToggled = null) : ViewElement
{
    public Color? OnColor { get; init; }
    public Color? ThumbColor { get; init; }
}

/// <summary>A controlled slider: it always shows <see cref="Value"/>.</summary>
public sealed record SliderElement(double Value, Action<double>? OnValueChanged = null) : ViewElement
{
    public double Minimum { get; init; }
    public double Maximum { get; init; } = 1;
    public Color? MinimumTrackColor { get; init; }
    public Color? MaximumTrackColor { get; init; }
    public Color? ThumbColor { get; init; }
    public Action? OnDragCompleted { get; init; }
}

/// <summary>A controlled stepper: it always shows <see cref="Value"/>.</summary>
public sealed record StepperElement(double Value, Action<double>? OnValueChanged = null) : ViewElement
{
    public double Minimum { get; init; }
    public double Maximum { get; init; } = 100;
    public double Increment { get; init; } = 1;
}

/// <summary>A controlled picker over <see cref="Items"/>: it always shows <see cref="SelectedIndex"/>.</summary>
public sealed record PickerElement(IReadOnlyList<string> Items, int SelectedIndex, Action<int>? OnSelectedIndexChanged = null) : TextViewElement
{
    public string? Title { get; init; }
}

/// <summary>A controlled date picker: it always shows <see cref="Date"/>.</summary>
public sealed record DatePickerElement(DateTime Date, Action<DateTime>? OnDateSelected = null) : TextViewElement
{
    public DateTime? MinimumDate { get; init; }
    public DateTime? MaximumDate { get; init; }
    public string? Format { get; init; }
}

/// <summary>A controlled time picker: it always shows <see cref="Time"/>.</summary>
public sealed record TimePickerElement(TimeSpan Time, Action<TimeSpan>? OnTimeSelected = null) : TextViewElement
{
    public string? Format { get; init; }
}

/// <summary>A controlled radio button. Buttons with the same <see cref="GroupName"/> are mutually exclusive.</summary>
public sealed record RadioButtonElement(string Content, bool IsChecked, Action<bool>? OnCheckedChanged = null) : TextViewElement
{
    public string? GroupName { get; init; }
}

// ── Layout ──────────────────────────────────────────────────────────────────

public sealed record VStackElement(Element?[] Children) : LayoutElement(Children)
{
    public double Spacing { get; init; }
}

public sealed record HStackElement(Element?[] Children) : LayoutElement(Children)
{
    public double Spacing { get; init; }
}

/// <summary>
/// A grid. Place children with the <c>.Grid(row, column)</c> modifier; definitions use the
/// <see cref="GridLengths"/> syntax, for example <c>"Auto,*,2*"</c>.
/// </summary>
public sealed record GridElement(Element?[] Children) : LayoutElement(Children)
{
    public string Rows { get; init; } = "";
    public string Columns { get; init; } = "";
    public double RowSpacing { get; init; }
    public double ColumnSpacing { get; init; }
}

public sealed record FlexLayoutElement(Element?[] Children) : LayoutElement(Children)
{
    public Microsoft.Maui.Layouts.FlexDirection? Direction { get; init; }
    public Microsoft.Maui.Layouts.FlexWrap? Wrap { get; init; }
    public Microsoft.Maui.Layouts.FlexJustify? JustifyContent { get; init; }
    public Microsoft.Maui.Layouts.FlexAlignItems? AlignItems { get; init; }
    public Microsoft.Maui.Layouts.FlexAlignContent? AlignContent { get; init; }
}

/// <summary>Positions children with the <c>.Absolute(bounds, flags)</c> modifier.</summary>
public sealed record AbsoluteLayoutElement(Element?[] Children) : LayoutElement(Children);

public sealed record ScrollViewElement(Element? Content) : ViewElement
{
    public ScrollOrientation? Orientation { get; init; }
    public ScrollBarVisibility? VerticalScrollBarVisibility { get; init; }
    public ScrollBarVisibility? HorizontalScrollBarVisibility { get; init; }
    public Action<double, double>? OnScrolled { get; init; }
}

/// <summary>Pull-to-refresh around one child. Controlled: it shows the spinner while <see cref="IsRefreshing"/>.</summary>
public sealed record RefreshViewElement(Element? Content, bool IsRefreshing, Action? OnRefresh = null) : ViewElement
{
    public Color? RefreshColor { get; init; }
}

/// <summary>An action revealed by swiping a <c>SwipeView</c>.</summary>
public sealed record SwipeAction(string Text, Action OnInvoked)
{
    public Color? BackgroundColor { get; init; }
    public ImageSource? Icon { get; init; }
}

public sealed record SwipeViewElement(Element? Content) : ViewElement
{
    public SwipeAction[] LeftItems { get; init; } = [];
    public SwipeAction[] RightItems { get; init; } = [];
}

// ── Virtualized collections ─────────────────────────────────────────────────

/// <summary>How a <c>CollectionView</c> arranges its items.</summary>
public enum ItemsArrangement
{
    VerticalList,
    HorizontalList,
    VerticalGrid,
    HorizontalGrid,
}

/// <summary>
/// A virtualized list. Each visible item is rendered by <see cref="ItemTemplate"/> into its own
/// tree, which inherits this element's context. Items are matched across renders by
/// <see cref="ItemKey"/>, so changes animate and scroll position is kept. Create it with the generic
/// <c>CollectionView&lt;T&gt;</c> factory.
/// </summary>
public sealed record CollectionViewElement(
    IReadOnlyList<object?> Items,
    Func<object?, string> ItemKey,
    Func<object?, Element?> ItemTemplate) : ViewElement
{
    public ItemsArrangement Arrangement { get; init; }
    public int Span { get; init; } = 1;
    public double ItemSpacing { get; init; }
    public SelectionMode SelectionMode { get; init; }

    /// <summary>The key of the selected item (single selection). Controlled.</summary>
    public string? SelectedKey { get; init; }

    /// <summary>The keys of the selected items (multiple selection). Controlled.</summary>
    public IReadOnlyList<string>? SelectedKeys { get; init; }

    public Action<IReadOnlyList<string>>? OnSelectionChanged { get; init; }
    public Element? Header { get; init; }
    public Element? Footer { get; init; }
    public Element? EmptyView { get; init; }
    public int RemainingItemsThreshold { get; init; } = -1;
    public Action? OnRemainingItemsThresholdReached { get; init; }
    public ItemSizingStrategy? ItemSizingStrategy { get; init; }

    /// <summary>
    /// When <c>false</c> (the default) an item's tree is remounted when its cell is recycled for a
    /// different item, so component state never leaks between items. Set <c>true</c> for stateless
    /// item templates to reuse the native views across items, which scrolls faster.
    /// </summary>
    public bool ReuseItemTrees { get; init; }
}

/// <summary>A swipeable carousel of items, rendered like <see cref="CollectionViewElement"/>. Controlled by <see cref="Position"/>.</summary>
public sealed record CarouselViewElement(
    IReadOnlyList<object?> Items,
    Func<object?, string> ItemKey,
    Func<object?, Element?> ItemTemplate,
    int Position,
    Action<int>? OnPositionChanged = null) : ViewElement
{
    public bool Loop { get; init; }
    public Thickness? PeekAreaInsets { get; init; }
    public double ItemSpacing { get; init; }
}

/// <summary>Adapts a typed list to the <c>object?</c> list the collection elements store, without copying.</summary>
internal sealed class ItemsAdapter<T>(IReadOnlyList<T> items) : IReadOnlyList<object?>
{
    public IReadOnlyList<T> Source => items;
    public object? this[int index] => items[index];
    public int Count => items.Count;
    public IEnumerator<object?> GetEnumerator() { foreach (var item in items) yield return item; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
