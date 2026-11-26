using Microsoft.Maui.Graphics;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiActivityIndicator = Microsoft.Maui.Controls.ActivityIndicator;
using MauiBoxView = Microsoft.Maui.Controls.BoxView;
using MauiButton = Microsoft.Maui.Controls.Button;
using MauiCheckBox = Microsoft.Maui.Controls.CheckBox;
using MauiDatePicker = Microsoft.Maui.Controls.DatePicker;
using MauiEditor = Microsoft.Maui.Controls.Editor;
using MauiEntry = Microsoft.Maui.Controls.Entry;
using MauiFlexLayout = Microsoft.Maui.Controls.FlexLayout;
using MauiGrid = Microsoft.Maui.Controls.Grid;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiPicker = Microsoft.Maui.Controls.Picker;
using MauiProgressBar = Microsoft.Maui.Controls.ProgressBar;
using MauiRadioButton = Microsoft.Maui.Controls.RadioButton;
using MauiRefreshView = Microsoft.Maui.Controls.RefreshView;
using MauiScrollView = Microsoft.Maui.Controls.ScrollView;
using MauiSearchBar = Microsoft.Maui.Controls.SearchBar;
using MauiSlider = Microsoft.Maui.Controls.Slider;
using MauiStepper = Microsoft.Maui.Controls.Stepper;
using MauiSwitch = Microsoft.Maui.Controls.Switch;
using MauiTimePicker = Microsoft.Maui.Controls.TimePicker;
using MauiWebView = Microsoft.Maui.Controls.WebView;

namespace Microsoft.UI.Reactor.Maui.Tests;

// 4c-3b's claims (spec 070 §4.14.2): the controls and layouts of the 🧪 rows and the ✅ control rows.
public static partial class MatrixCases
{
    private static object? Get(object view, BindableProperty property) => ((BindableObject)view).GetValue(property);

    private static string TextOf(object? value) => value as string ?? "";

    // Thickness's own equality is false for NaN sides (a button's default padding).
    private static bool SameThickness(object? x, object? y) =>
        x is Thickness a && y is Thickness b
            ? a.Left.Equals(b.Left) && a.Top.Equals(b.Top) && a.Right.Equals(b.Right) && a.Bottom.Equals(b.Bottom)
            : Equals(x, y);

    // ── Group T: the shared text and font settings ──────────────────────────

    private static FontAttributes AttributesOf(Microsoft.Maui.Font font) =>
        (font.Weight >= FontWeight.Bold ? FontAttributes.Bold : FontAttributes.None)
        | (font.Slant == FontSlant.Italic ? FontAttributes.Italic : FontAttributes.None);

    /// <summary>
    /// The five font and color settings every text control shares (TextViewElement). On the direct
    /// registry a label, button or entry is a direct view whose font is Reactor's own remap: the case reads
    /// it through the MAUI interface and counts its remap.
    /// </summary>
    private static IEnumerable<PropertyCase> TextSettings<T>(Func<T> make) where T : TextViewElement
    {
        yield return Case(make, nameof(TextViewElement.FontSize), static (e, size) => (T)((TextViewElement)e with { FontSize = size }), static () => 13d, static () => 21d,
            v => v is DirectView ? ((ITextStyle)v).Font.Size : Get(v, MauiLabel.FontSizeProperty), MauiLabel.FontSizeProperty, direct: nameof(ITextStyle.Font));
        yield return Case(make, nameof(TextViewElement.FontAttributes), static (e, a) => (T)((TextViewElement)e with { FontAttributes = a }), static () => FontAttributes.Bold, static () => FontAttributes.Italic,
            v => v is DirectView ? AttributesOf(((ITextStyle)v).Font) : Get(v, MauiLabel.FontAttributesProperty), MauiLabel.FontAttributesProperty, direct: nameof(ITextStyle.Font));
        yield return Case(make, nameof(TextViewElement.FontFamily), static (e, f) => (T)((TextViewElement)e with { FontFamily = f }), static () => "Serif", static () => "Mono",
            v => v is DirectView ? ((ITextStyle)v).Font.Family : Get(v, MauiLabel.FontFamilyProperty), MauiLabel.FontFamilyProperty, direct: nameof(ITextStyle.Font));
        yield return Case(make, nameof(TextViewElement.TextColor), static (e, c) => (T)((TextViewElement)e with { TextColor = c }), static () => Colors.Red, static () => Colors.Blue,
            v => v is DirectView ? ((ITextStyle)v).TextColor : Get(v, MauiLabel.TextColorProperty), MauiLabel.TextColorProperty, direct: nameof(ITextStyle.TextColor));
        yield return Case(make, nameof(TextViewElement.CharacterSpacing), static (e, s) => (T)((TextViewElement)e with { CharacterSpacing = s }), static () => 2d, static () => 3d,
            v => v is DirectView ? ((ITextStyle)v).CharacterSpacing : Get(v, MauiLabel.CharacterSpacingProperty), MauiLabel.CharacterSpacingProperty, direct: nameof(ITextStyle.CharacterSpacing));
    }

    private static IEnumerable<PropertyCase> TextCases() =>
    [
        .. TextSettings(() => Button("b")),
        .. TextSettings(() => DatePicker(new DateTime(2024, 6, 1))),
        .. TextSettings(() => Editor("e")),
        .. TextSettings(() => Entry("e")),
        .. TextSettings(() => Label("l")),
        .. TextSettings(() => Picker(["a", "b"], 0)),
        .. TextSettings(() => RadioButton("r", false)),
        .. TextSettings(() => SearchBar("s")),
        .. TextSettings(() => TimePicker(new TimeSpan(9, 30, 0))),
    ];

    // ── Group P and L: plain values and layout ──────────────────────────────

    private static IEnumerable<PropertyCase> ControlCases()
    {
        // BoxView: Controls on both registries.
        yield return Case(() => BoxView(), nameof(BoxViewElement.Color), static (e, c) => e with { Color = c }, static () => Colors.Red, static () => Colors.Blue,
            v => ((MauiBoxView)v).Color, MauiBoxView.ColorProperty);
        yield return Case(() => BoxView(Colors.Gray), nameof(BoxViewElement.CornerRadius), static (e, r) => e with { CornerRadius = r }, static () => new CornerRadius(3), static () => new CornerRadius(1, 2, 3, 4),
            v => ((MauiBoxView)v).CornerRadius, MauiBoxView.CornerRadiusProperty);

        // ActivityIndicator and ProgressBar: direct views on the direct registry. The running state and the
        // progress aren't nullable: unset is false and 0, written as values.
        yield return Case(() => ActivityIndicator(false), nameof(ActivityIndicatorElement.IsRunning), static (e, r) => e with { IsRunning = r }, static () => true, static () => false,
            v => ((IActivityIndicator)v).IsRunning, MauiActivityIndicator.IsRunningProperty, direct: nameof(IActivityIndicator.IsRunning), clears: false);
        yield return Case(() => ActivityIndicator(true), nameof(ActivityIndicatorElement.Color), static (e, c) => e with { Color = c }, static () => Colors.Red, static () => Colors.Blue,
            v => ((IActivityIndicator)v).Color, MauiActivityIndicator.ColorProperty, direct: nameof(IActivityIndicator.Color));
        yield return Case(() => ProgressBar(0), nameof(ProgressBarElement.Progress), static (e, p) => e with { Progress = p }, static () => 0.4, static () => 0.7,
            v => ((IProgress)v).Progress, MauiProgressBar.ProgressProperty, direct: nameof(IProgress.Progress), clears: false);
        yield return Case(() => ProgressBar(0.5), nameof(ProgressBarElement.ProgressColor), static (e, c) => e with { ProgressColor = c }, static () => Colors.Red, static () => Colors.Blue,
            v => ((IProgress)v).ProgressColor, MauiProgressBar.ProgressColorProperty, direct: nameof(IProgress.ProgressColor));

        // CheckBox and Switch colors: the direct views remap them (the check box's foreground paint; the
        // switch's track only while on, so the base is on).
        yield return Case(() => CheckBox(true), nameof(CheckBoxElement.Color), static (e, c) => e with { Color = c }, static () => Colors.Red, static () => Colors.Blue,
            v => v is MauiCheckBox box ? box.Color : (((ICheckBox)v).Foreground as SolidPaint)?.Color, MauiCheckBox.ColorProperty, direct: nameof(ICheckBox.Foreground));
        yield return Case(() => Switch(true), nameof(SwitchElement.OnColor), static (e, c) => e with { OnColor = c }, static () => Colors.Red, static () => Colors.Blue,
            v => v is MauiSwitch toggle ? toggle.OnColor : ((ISwitch)v).TrackColor, MauiSwitch.OnColorProperty, direct: nameof(ISwitch.TrackColor));
        yield return Case(() => Switch(true), nameof(SwitchElement.ThumbColor), static (e, c) => e with { ThumbColor = c }, static () => Colors.Red, static () => Colors.Blue,
            v => ((ISwitch)v).ThumbColor, MauiSwitch.ThumbColorProperty, direct: nameof(ISwitch.ThumbColor));

        // Slider: the range is written with the value (non-nullable; unset is MAUI's 0 and 1, written).
        yield return Case(() => Slider(0.5), nameof(SliderElement.Minimum), static (e, m) => e with { Minimum = m }, static () => 0.2, static () => 0.1,
            v => ((IRange)v).Minimum, MauiSlider.MinimumProperty, direct: nameof(IRange.Minimum), clears: false);
        yield return Case(() => Slider(0.5), nameof(SliderElement.Maximum), static (e, m) => e with { Maximum = m }, static () => 2d, static () => 3d,
            v => ((IRange)v).Maximum, MauiSlider.MaximumProperty, direct: nameof(IRange.Maximum), clears: false);
        yield return Case(() => Slider(0.5), nameof(SliderElement.MinimumTrackColor), static (e, c) => e with { MinimumTrackColor = c }, static () => Colors.Red, static () => Colors.Blue,
            v => ((ISlider)v).MinimumTrackColor, MauiSlider.MinimumTrackColorProperty, direct: nameof(ISlider.MinimumTrackColor));
        yield return Case(() => Slider(0.5), nameof(SliderElement.MaximumTrackColor), static (e, c) => e with { MaximumTrackColor = c }, static () => Colors.Red, static () => Colors.Blue,
            v => ((ISlider)v).MaximumTrackColor, MauiSlider.MaximumTrackColorProperty, direct: nameof(ISlider.MaximumTrackColor));
        yield return Case(() => Slider(0.5), nameof(SliderElement.ThumbColor), static (e, c) => e with { ThumbColor = c }, static () => Colors.Red, static () => Colors.Blue,
            v => ((ISlider)v).ThumbColor, MauiSlider.ThumbColorProperty, direct: nameof(ISlider.ThumbColor));

        // Stepper: Controls on both registries; the range and increment are non-nullable.
        yield return Case(() => Stepper(5), nameof(StepperElement.Minimum), static (e, m) => e with { Minimum = m }, static () => 2d, static () => 1d,
            v => ((MauiStepper)v).Minimum, MauiStepper.MinimumProperty, clears: false);
        yield return Case(() => Stepper(5), nameof(StepperElement.Maximum), static (e, m) => e with { Maximum = m }, static () => 50d, static () => 60d,
            v => ((MauiStepper)v).Maximum, MauiStepper.MaximumProperty, clears: false);
        yield return Case(() => Stepper(5), nameof(StepperElement.Increment), static (e, i) => e with { Increment = i }, static () => 2d, static () => 5d,
            v => ((MauiStepper)v).Increment, MauiStepper.IncrementProperty, clears: false);

        // Picker: the items are written as a new items source when their texts change.
        yield return Case(() => Picker([], -1), nameof(PickerElement.Items), static (e, items) => e with { Items = items }, static () => new[] { "a", "b" }, static () => new[] { "c", "d" },
            v => string.Join(";", ((MauiPicker)v).ItemsSource?.Cast<object>() ?? []), MauiPicker.ItemsSourceProperty, expect: static items => string.Join(";", items), clears: false);
        yield return Case(() => Picker(["a"], 0), nameof(PickerElement.Title), static (e, t) => e with { Title = t }, static () => "Pick", static () => "Choose",
            v => ((MauiPicker)v).Title, MauiPicker.TitleProperty);

        // Date and time pickers: the range is written with the date.
        yield return Case(() => DatePicker(new DateTime(2024, 6, 1)), nameof(DatePickerElement.MinimumDate), static (e, d) => e with { MinimumDate = d },
            static () => new DateTime(2020, 1, 1), static () => new DateTime(2021, 1, 1),
            v => ((MauiDatePicker)v).MinimumDate, MauiDatePicker.MinimumDateProperty);
        yield return Case(() => DatePicker(new DateTime(2024, 6, 1)), nameof(DatePickerElement.MaximumDate), static (e, d) => e with { MaximumDate = d },
            static () => new DateTime(2030, 1, 1), static () => new DateTime(2031, 1, 1),
            v => ((MauiDatePicker)v).MaximumDate, MauiDatePicker.MaximumDateProperty);
        yield return Case(() => DatePicker(new DateTime(2024, 6, 1)), nameof(DatePickerElement.Format), static (e, f) => e with { Format = f }, static () => "yyyy", static () => "MM/dd",
            v => ((MauiDatePicker)v).Format, MauiDatePicker.FormatProperty);
        yield return Case(() => TimePicker(new TimeSpan(9, 30, 0)), nameof(TimePickerElement.Format), static (e, f) => e with { Format = f }, static () => "HH:mm", static () => "hh tt",
            v => ((MauiTimePicker)v).Format, MauiTimePicker.FormatProperty);

        // Editor: Controls on both registries.
        yield return Case(() => Editor("e"), nameof(EditorElement.Placeholder), static (e, p) => e with { Placeholder = p }, static () => "first", static () => "second",
            v => ((MauiEditor)v).Placeholder, MauiEditor.PlaceholderProperty);
        yield return Case(() => Editor("e"), nameof(EditorElement.PlaceholderColor), static (e, c) => e with { PlaceholderColor = c }, static () => Colors.Red, static () => Colors.Blue,
            v => ((MauiEditor)v).PlaceholderColor, MauiEditor.PlaceholderColorProperty);
        yield return Case(() => Editor("e"), nameof(EditorElement.IsReadOnly), static (e, r) => e with { IsReadOnly = r }, static () => true, static () => false,
            v => ((MauiEditor)v).IsReadOnly, MauiEditor.IsReadOnlyProperty);
        yield return Case(() => Editor("e"), nameof(EditorElement.MaxLength), static (e, m) => e with { MaxLength = m }, static () => 4, static () => 8,
            v => ((MauiEditor)v).MaxLength, MauiEditor.MaxLengthProperty);
        yield return Case(() => Editor("e"), nameof(EditorElement.Keyboard), static (e, k) => e with { Keyboard = k }, static () => Keyboard.Email, static () => Keyboard.Numeric,
            v => ((MauiEditor)v).Keyboard, MauiEditor.KeyboardProperty);
        yield return Case(() => Editor("e"), nameof(EditorElement.AutoSize), static (e, a) => e with { AutoSize = a }, static () => EditorAutoSizeOption.TextChanges, static () => EditorAutoSizeOption.Disabled,
            v => ((MauiEditor)v).AutoSize, MauiEditor.AutoSizeProperty);

        // Entry: a direct view on the direct registry, read through IEntry on both.
        yield return Case(() => Entry("e"), nameof(EntryElement.Placeholder), static (e, p) => e with { Placeholder = p }, static () => "first", static () => "second",
            v => TextOf(((IEntry)v).Placeholder), MauiEntry.PlaceholderProperty, direct: nameof(IPlaceholder.Placeholder));
        yield return Case(() => Entry("e"), nameof(EntryElement.PlaceholderColor), static (e, c) => e with { PlaceholderColor = c }, static () => Colors.Red, static () => Colors.Blue,
            v => ((IEntry)v).PlaceholderColor, MauiEntry.PlaceholderColorProperty, direct: nameof(IPlaceholder.PlaceholderColor));
        yield return Case(() => Entry("e"), nameof(EntryElement.IsPassword), static (e, p) => e with { IsPassword = p }, static () => true, static () => false,
            v => ((IEntry)v).IsPassword, MauiEntry.IsPasswordProperty, direct: nameof(IEntry.IsPassword));
        yield return Case(() => Entry("e"), nameof(EntryElement.IsReadOnly), static (e, r) => e with { IsReadOnly = r }, static () => true, static () => false,
            v => ((IEntry)v).IsReadOnly, MauiEntry.IsReadOnlyProperty, direct: nameof(ITextInput.IsReadOnly));
        yield return Case(() => Entry("e"), nameof(EntryElement.MaxLength), static (e, m) => e with { MaxLength = m }, static () => 4, static () => 8,
            v => ((IEntry)v).MaxLength, MauiEntry.MaxLengthProperty, direct: nameof(ITextInput.MaxLength));
        yield return Case(() => Entry("e"), nameof(EntryElement.Keyboard), static (e, k) => e with { Keyboard = k }, static () => Keyboard.Email, static () => Keyboard.Numeric,
            v => ((IEntry)v).Keyboard, MauiEntry.KeyboardProperty, direct: nameof(ITextInput.Keyboard));
        yield return Case(() => Entry("e"), nameof(EntryElement.ReturnType), static (e, r) => e with { ReturnType = r }, static () => ReturnType.Go, static () => ReturnType.Send,
            v => ((IEntry)v).ReturnType, MauiEntry.ReturnTypeProperty, direct: nameof(IEntry.ReturnType));
        yield return Case(() => Entry("e"), nameof(EntryElement.ClearButtonVisibility), static (e, c) => e with { ClearButtonVisibility = c },
            static () => ClearButtonVisibility.WhileEditing, static () => ClearButtonVisibility.Never,
            v => ((IEntry)v).ClearButtonVisibility, MauiEntry.ClearButtonVisibilityProperty, direct: nameof(IEntry.ClearButtonVisibility));
        yield return Case(() => Entry("e"), nameof(EntryElement.HorizontalTextAlignment), static (e, a) => e with { HorizontalTextAlignment = a }, static () => TextAlignment.Center, static () => TextAlignment.End,
            v => ((IEntry)v).HorizontalTextAlignment, MauiEntry.HorizontalTextAlignmentProperty, direct: nameof(ITextAlignment.HorizontalTextAlignment));
        yield return Case(() => Entry("e"), nameof(EntryElement.IsSpellCheckEnabled), static (e, s) => e with { IsSpellCheckEnabled = s }, static () => false, static () => true,
            v => ((IEntry)v).IsSpellCheckEnabled, MauiEntry.IsSpellCheckEnabledProperty, direct: nameof(ITextInput.IsSpellCheckEnabled));

        // SearchBar: Controls on both registries.
        yield return Case(() => SearchBar("s"), nameof(SearchBarElement.Placeholder), static (e, p) => e with { Placeholder = p }, static () => "first", static () => "second",
            v => ((MauiSearchBar)v).Placeholder, MauiSearchBar.PlaceholderProperty);

        // Label: a direct view unless one of its Controls-only settings is set; the cases for those keep one
        // set in the base too, so the label stays the same Controls view on the direct registry.
        yield return Case(() => Label(""), nameof(LabelElement.Text), static (e, t) => e with { Text = t }, static () => "first", static () => "second",
            v => TextOf(v is DirectView ? ((IText)v).Text : Get(v, MauiLabel.TextProperty)), MauiLabel.TextProperty, direct: nameof(IText.Text), clears: false);
        yield return Case(() => Label("l"), nameof(LabelElement.TextDecorations), static (e, d) => e with { TextDecorations = d }, static () => TextDecorations.Underline, static () => TextDecorations.Strikethrough,
            v => ((ILabel)v).TextDecorations, MauiLabel.TextDecorationsProperty, direct: nameof(ILabel.TextDecorations));
        yield return Case(() => Label("l"), nameof(LabelElement.LineHeight), static (e, h) => e with { LineHeight = h }, static () => 1.5, static () => 2d,
            v => ((ILabel)v).LineHeight, MauiLabel.LineHeightProperty, direct: nameof(ILabel.LineHeight));
        yield return Case(() => Label("l"), nameof(LabelElement.HorizontalTextAlignment), static (e, a) => e with { HorizontalTextAlignment = a }, static () => TextAlignment.Center, static () => TextAlignment.End,
            v => ((ILabel)v).HorizontalTextAlignment, MauiLabel.HorizontalTextAlignmentProperty, direct: nameof(ITextAlignment.HorizontalTextAlignment));
        yield return Case(() => Label("l"), nameof(LabelElement.VerticalTextAlignment), static (e, a) => e with { VerticalTextAlignment = a }, static () => TextAlignment.Center, static () => TextAlignment.End,
            v => ((ILabel)v).VerticalTextAlignment, MauiLabel.VerticalTextAlignmentProperty, direct: nameof(ITextAlignment.VerticalTextAlignment));
        yield return Case(() => Label("l"), nameof(LabelElement.Padding), static (e, p) => e with { Padding = p }, static () => new Thickness(4), static () => new Thickness(1, 2, 3, 4),
            v => ((ILabel)v).Padding, MauiLabel.PaddingProperty, direct: nameof(IPadding.Padding));
        yield return Case(() => Label("l") with { MaxLines = 2 }, nameof(LabelElement.TextTransform), static (e, t) => e with { TextTransform = t }, static () => TextTransform.Uppercase, static () => TextTransform.Lowercase,
            v => ((MauiLabel)v).TextTransform, MauiLabel.TextTransformProperty);
        yield return Case(() => Label("l") with { MaxLines = 2 }, nameof(LabelElement.LineBreakMode), static (e, m) => e with { LineBreakMode = m }, static () => LineBreakMode.TailTruncation, static () => LineBreakMode.CharacterWrap,
            v => ((MauiLabel)v).LineBreakMode, MauiLabel.LineBreakModeProperty);
        yield return Case(() => Label("l") with { LineBreakMode = LineBreakMode.WordWrap }, nameof(LabelElement.MaxLines), static (e, m) => e with { MaxLines = m }, static () => 2, static () => 3,
            v => ((MauiLabel)v).MaxLines, MauiLabel.MaxLinesProperty);
        yield return Case(() => Label("l") with { MaxLines = 2 }, nameof(LabelElement.TextType), static (e, t) => e with { TextType = t }, static () => TextType.Html, static () => TextType.Text,
            v => ((MauiLabel)v).TextType, MauiLabel.TextTypeProperty);

        // Button: a direct view unless it has an image.
        yield return Case(() => Button(""), nameof(ButtonElement.Text), static (e, t) => e with { Text = t }, static () => "first", static () => "second",
            v => TextOf(v is DirectView ? ((IText)v).Text : Get(v, MauiButton.TextProperty)), MauiButton.TextProperty, direct: nameof(IText.Text), clears: false);
        yield return Case(() => Button("b"), nameof(ButtonElement.BorderColor), static (e, c) => e with { BorderColor = c }, static () => Colors.Red, static () => Colors.Blue,
            v => v is MauiButton b ? b.BorderColor : ((IButtonStroke)v).StrokeColor, MauiButton.BorderColorProperty, direct: nameof(IButtonStroke.StrokeColor));
        yield return Case(() => Button("b"), nameof(ButtonElement.BorderWidth), static (e, w) => e with { BorderWidth = w }, static () => 2d, static () => 3d,
            v => v is MauiButton b ? b.BorderWidth : ((IButtonStroke)v).StrokeThickness, MauiButton.BorderWidthProperty, direct: nameof(IButtonStroke.StrokeThickness));
        yield return Case(() => Button("b"), nameof(ButtonElement.CornerRadius), static (e, r) => e with { CornerRadius = r }, static () => 4, static () => 8,
            v => v is MauiButton b ? b.CornerRadius : ((IButtonStroke)v).CornerRadius, MauiButton.CornerRadiusProperty, direct: nameof(IButtonStroke.CornerRadius));
        yield return Case(() => Button("b"), nameof(ButtonElement.Padding), static (e, p) => e with { Padding = p }, static () => new Thickness(4), static () => new Thickness(1, 2, 3, 4),
            v => ((IPadding)v).Padding, MauiButton.PaddingProperty, same: SameThickness, direct: nameof(IPadding.Padding));
        // On the direct registry an image makes the button a Controls view and removing it a direct view
        // again (ControlTests.AControlsOnlySetting_SwitchesTheElementToAControlsView_AndBack), so the
        // in-place steps run on the Controls registry.
        yield return Case(() => Button("b"), nameof(ButtonElement.ImageSource), static (e, i) => e with { ImageSource = i }, static () => Glyph("A"), static () => Glyph("B"),
            v => ((MauiButton)v).ImageSource, MauiButton.ImageSourceProperty, same: static (x, y) => ImageSources.Same(x as ImageSource, y as ImageSource)) with { OnlyRegistry = "controls" };

        // RadioButton: Controls on both registries; its text content.
        yield return Case(() => RadioButton("", false), nameof(RadioButtonElement.Content), static (e, c) => e with { Content = c }, static () => "first", static () => "second",
            v => TextOf(((MauiRadioButton)v).Content), MauiRadioButton.ContentProperty, clears: false);

        // ScrollView and RefreshView.
        yield return Case(() => ScrollView(Label("x")), nameof(ScrollViewElement.Orientation), static (e, o) => e with { Orientation = o }, static () => ScrollOrientation.Horizontal, static () => ScrollOrientation.Both,
            v => ((MauiScrollView)v).Orientation, MauiScrollView.OrientationProperty);
        yield return Case(() => ScrollView(Label("x")), nameof(ScrollViewElement.VerticalScrollBarVisibility), static (e, b) => e with { VerticalScrollBarVisibility = b },
            static () => ScrollBarVisibility.Never, static () => ScrollBarVisibility.Always,
            v => ((MauiScrollView)v).VerticalScrollBarVisibility, MauiScrollView.VerticalScrollBarVisibilityProperty);
        yield return Case(() => ScrollView(Label("x")), nameof(ScrollViewElement.HorizontalScrollBarVisibility), static (e, b) => e with { HorizontalScrollBarVisibility = b },
            static () => ScrollBarVisibility.Never, static () => ScrollBarVisibility.Always,
            v => ((MauiScrollView)v).HorizontalScrollBarVisibility, MauiScrollView.HorizontalScrollBarVisibilityProperty);
        yield return Case(() => RefreshView(Label("x"), false, null), nameof(RefreshViewElement.RefreshColor), static (e, c) => e with { RefreshColor = c }, static () => Colors.Red, static () => Colors.Blue,
            v => ((MauiRefreshView)v).RefreshColor, MauiRefreshView.RefreshColorProperty);

        // WebView: the source is replaced when the URL or the HTML changes; unset writes no source (null).
        static string? SourceOf(object v) => ((MauiWebView)v).Source switch
        {
            UrlWebViewSource url => "url " + url.Url,
            HtmlWebViewSource html => "html " + html.Html,
            null => null,
            var other => other.GetType().Name,
        };
        yield return Case(() => new WebViewElement(null), nameof(WebViewElement.Url), static (e, u) => e with { Url = u }, static () => "https://a.example/", static () => "https://b.example/",
            SourceOf, MauiWebView.SourceProperty, expect: static u => "url " + u, clears: false);
        yield return Case(() => new WebViewElement(null), nameof(WebViewElement.Html), static (e, h) => e with { Html = h }, static () => "<p>a</p>", static () => "<p>b</p>",
            SourceOf, MauiWebView.SourceProperty, expect: static h => "html " + h, clears: false);

        // Flyout: the layout behavior (a page; Controls on both registries).
        yield return Case(() => Flyout(Page("Menu", Label("m")), Page("Detail", Label("d")), false), nameof(FlyoutElement.LayoutBehavior), static (e, b) => e with { LayoutBehavior = b },
            static () => FlyoutLayoutBehavior.Popover, static () => FlyoutLayoutBehavior.SplitOnLandscape,
            v => ((FlyoutPage)v).FlyoutLayoutBehavior, FlyoutPage.FlyoutLayoutBehaviorProperty);
    }

    private static IEnumerable<PropertyCase> LayoutCases()
    {
        // A direct stack has no parameterless constructor.
        static object FreshStack(object native) => native is DirectStackLayout stack ? new DirectStackLayout(stack.Orientation) : Activator.CreateInstance(native.GetType())!;

        // Stacks: direct views on the direct registry, whose spacing only invalidates the measure. The
        // spacing isn't nullable: unset is 0, written as a value.
        yield return Case(() => VStack(), nameof(VStackElement.Spacing), static (e, s) => e with { Spacing = s }, static () => 4d, static () => 8d,
            v => ((IStackLayout)v).Spacing, StackBase.SpacingProperty, direct: nameof(IView.InvalidateMeasure), clears: false)
            with { Fresh = FreshStack };
        yield return Case(() => HStack(), nameof(HStackElement.Spacing), static (e, s) => e with { Spacing = s }, static () => 4d, static () => 8d,
            v => ((IStackLayout)v).Spacing, StackBase.SpacingProperty, direct: nameof(IView.InvalidateMeasure), clears: false)
            with { Fresh = FreshStack };
        yield return Case(() => VStack(), nameof(VStackElement.Padding), static (e, p) => e with { Padding = p }, static () => new Thickness(4), static () => new Thickness(1, 2, 3, 4),
            v => ((IPadding)v).Padding, Layout.PaddingProperty, direct: nameof(IPadding.Padding)) with { Fresh = FreshStack };
        yield return Case(() => HStack(), nameof(HStackElement.Padding), static (e, p) => e with { Padding = p }, static () => new Thickness(4), static () => new Thickness(1, 2, 3, 4),
            v => ((IPadding)v).Padding, Layout.PaddingProperty, direct: nameof(IPadding.Padding)) with { Fresh = FreshStack };

        // Grid: the spacings aren't nullable either.
        yield return Case(() => Grid("*", "*"), nameof(GridElement.RowSpacing), static (e, s) => e with { RowSpacing = s }, static () => 4d, static () => 8d,
            v => ((IGridLayout)v).RowSpacing, MauiGrid.RowSpacingProperty, direct: nameof(IView.InvalidateMeasure), clears: false);
        yield return Case(() => Grid("*", "*"), nameof(GridElement.ColumnSpacing), static (e, s) => e with { ColumnSpacing = s }, static () => 4d, static () => 8d,
            v => ((IGridLayout)v).ColumnSpacing, MauiGrid.ColumnSpacingProperty, direct: nameof(IView.InvalidateMeasure), clears: false);
        yield return Case(() => Grid("*", "*"), nameof(GridElement.Padding), static (e, p) => e with { Padding = p }, static () => new Thickness(4), static () => new Thickness(1, 2, 3, 4),
            v => ((IPadding)v).Padding, Layout.PaddingProperty, direct: nameof(IPadding.Padding));

        // FlexLayout and AbsoluteLayout: Controls on both registries.
        yield return Case(() => FlexLayout(), nameof(FlexLayoutElement.Padding), static (e, p) => e with { Padding = p }, static () => new Thickness(4), static () => new Thickness(1, 2, 3, 4),
            v => ((Layout)v).Padding, Layout.PaddingProperty);
        yield return Case(() => FlexLayout(), nameof(FlexLayoutElement.Direction), static (e, d) => e with { Direction = d },
            static () => Microsoft.Maui.Layouts.FlexDirection.Column, static () => Microsoft.Maui.Layouts.FlexDirection.RowReverse,
            v => ((MauiFlexLayout)v).Direction, MauiFlexLayout.DirectionProperty);
        yield return Case(() => FlexLayout(), nameof(FlexLayoutElement.Wrap), static (e, w) => e with { Wrap = w },
            static () => Microsoft.Maui.Layouts.FlexWrap.Wrap, static () => Microsoft.Maui.Layouts.FlexWrap.Reverse,
            v => ((MauiFlexLayout)v).Wrap, MauiFlexLayout.WrapProperty);
        yield return Case(() => FlexLayout(), nameof(FlexLayoutElement.JustifyContent), static (e, j) => e with { JustifyContent = j },
            static () => Microsoft.Maui.Layouts.FlexJustify.Center, static () => Microsoft.Maui.Layouts.FlexJustify.SpaceBetween,
            v => ((MauiFlexLayout)v).JustifyContent, MauiFlexLayout.JustifyContentProperty);
        yield return Case(() => FlexLayout(), nameof(FlexLayoutElement.AlignItems), static (e, a) => e with { AlignItems = a },
            static () => Microsoft.Maui.Layouts.FlexAlignItems.Center, static () => Microsoft.Maui.Layouts.FlexAlignItems.End,
            v => ((MauiFlexLayout)v).AlignItems, MauiFlexLayout.AlignItemsProperty);
        yield return Case(() => FlexLayout(), nameof(FlexLayoutElement.AlignContent), static (e, a) => e with { AlignContent = a },
            static () => Microsoft.Maui.Layouts.FlexAlignContent.Center, static () => Microsoft.Maui.Layouts.FlexAlignContent.SpaceAround,
            v => ((MauiFlexLayout)v).AlignContent, MauiFlexLayout.AlignContentProperty);
        yield return Case(() => AbsoluteLayout(), nameof(AbsoluteLayoutElement.Padding), static (e, p) => e with { Padding = p }, static () => new Thickness(4), static () => new Thickness(1, 2, 3, 4),
            v => ((Layout)v).Padding, Layout.PaddingProperty);
    }

    // ── Group R: rich-text runs, read on the label's span at their index ────

    private static Span SpanOf(object label) => ((MauiLabel)label).FormattedText.Spans[0];

    private static MauiLabel FreshSpanLabel(object native) => new() { FormattedText = new FormattedString { Spans = { new Span() } } };

    private static IEnumerable<PropertyCase> InlineCases<T>(Func<T> make) where T : RichTextInline
    {
        PropertyCase SpanCase<TValue>(string property, Func<T, TValue, T> set, Func<TValue> a, Func<TValue> b, BindableProperty native, bool clears = true, Func<object?>? cleared = null) =>
            Case(() => Label(make()), property, (label, value) => label with { Inlines = [set((T)label.Inlines![0], value)] }, a, b,
                v => SpanOf(v).GetValue(native), native, clears: clears) with
            {
                ElementType = typeof(T),
                Target = SpanOf,
                Fresh = FreshSpanLabel,
                Cleared = cleared,
            };

        yield return SpanCase(nameof(RichTextInline.FontSize), static (i, v) => i with { FontSize = v }, static () => 13d, static () => 21d, Microsoft.Maui.Controls.Span.FontSizeProperty);
        yield return SpanCase(nameof(RichTextInline.FontAttributes), static (i, v) => i with { FontAttributes = v }, static () => FontAttributes.Bold, static () => FontAttributes.Italic, Microsoft.Maui.Controls.Span.FontAttributesProperty);
        yield return SpanCase(nameof(RichTextInline.FontFamily), static (i, v) => i with { FontFamily = v }, static () => "Serif", static () => "Mono", Microsoft.Maui.Controls.Span.FontFamilyProperty);
        yield return SpanCase(nameof(RichTextInline.TextColor), static (i, v) => i with { TextColor = v }, static () => Colors.Red, static () => Colors.Blue, Microsoft.Maui.Controls.Span.TextColorProperty);
        yield return SpanCase(nameof(RichTextInline.BackgroundColor), static (i, v) => i with { BackgroundColor = v }, static () => Colors.Red, static () => Colors.Blue, Microsoft.Maui.Controls.Span.BackgroundColorProperty);
        yield return SpanCase(nameof(RichTextInline.CharacterSpacing), static (i, v) => i with { CharacterSpacing = v }, static () => 2d, static () => 3d, Microsoft.Maui.Controls.Span.CharacterSpacingProperty);
        yield return SpanCase(nameof(RichTextInline.LineHeight), static (i, v) => i with { LineHeight = v }, static () => 1.5, static () => 2d, Microsoft.Maui.Controls.Span.LineHeightProperty);
        yield return SpanCase(nameof(RichTextInline.TextTransform), static (i, v) => i with { TextTransform = v }, static () => TextTransform.Uppercase, static () => TextTransform.Lowercase, Microsoft.Maui.Controls.Span.TextTransformProperty);
        // A hyperlink is underlined unless it says otherwise, so unsetting writes the underline.
        yield return typeof(T) == typeof(RichTextHyperlink)
            ? SpanCase(nameof(RichTextInline.TextDecorations), static (i, v) => i with { TextDecorations = v }, static () => TextDecorations.Strikethrough, static () => TextDecorations.Underline | TextDecorations.Strikethrough,
                Microsoft.Maui.Controls.Span.TextDecorationsProperty, clears: false, cleared: static () => TextDecorations.Underline)
            : SpanCase(nameof(RichTextInline.TextDecorations), static (i, v) => i with { TextDecorations = v }, static () => TextDecorations.Underline, static () => TextDecorations.Strikethrough,
                Microsoft.Maui.Controls.Span.TextDecorationsProperty);
    }

    private static IEnumerable<PropertyCase> RichTextCases()
    {
        foreach (var c in InlineCases(() => Run("run"))) yield return c;
        foreach (var c in InlineCases(() => Hyperlink("link", static () => { }))) yield return c;

        // The run's text: not nullable, so the base is an empty run and the span's text is written.
        yield return Case(() => Label(Run("")), nameof(RichTextRun.Text), static (label, t) => label with { Inlines = [Run(t)] }, static () => "first", static () => "second",
            v => TextOf(SpanOf(v).Text), Microsoft.Maui.Controls.Span.TextProperty, clears: false) with
        { ElementType = typeof(RichTextRun), Target = SpanOf, Fresh = FreshSpanLabel };
        yield return Case(() => Label(Hyperlink("", static () => { })), nameof(RichTextHyperlink.Text), static (label, t) => label with { Inlines = [Hyperlink(t, static () => { })] },
            static () => "first", static () => "second",
            v => TextOf(SpanOf(v).Text), Microsoft.Maui.Controls.Span.TextProperty, clears: false) with
        { ElementType = typeof(RichTextHyperlink), Target = SpanOf, Fresh = FreshSpanLabel };
    }

    // ── Group C: controlled values with their change callbacks ──────────────

    private static ControlledCase ControlledValue<TValue>(Type element, string property, string callback, Func<TValue, Action<string>, Element> create,
        Func<object, object?> read, Action<object, TValue> input, TValue a, TValue b, TValue c, string writeName, string? payload = null, string? directWriteName = null) where TValue : notnull => new()
        {
            ElementType = element,
            Property = property,
            Callback = callback,
            Create = (value, report) => create((TValue)value, report),
            Read = read,
            Input = (view, value) => input(view, (TValue)value),
            A = a,
            B = b,
            C = c,
            Payload = payload ?? c.ToString()!,
            WriteName = writeName,
            DirectWriteName = directWriteName,
        };

    private static IReadOnlyList<ControlledCase> ControlledCases() =>
    [
        ControlledValue<string>(typeof(EditorElement), nameof(EditorElement.Text), nameof(EditorElement.OnTextChanged), static (v, r) => Editor(v, t => r(t)),
            static v => ((MauiEditor)v).Text, static (v, t) => ((MauiEditor)v).Text = t, "a", "b", "c", nameof(MauiEditor.Text)),
        ControlledValue<string>(typeof(EntryElement), nameof(EntryElement.Text), nameof(EntryElement.OnTextChanged), static (v, r) => Entry(v, t => r(t)),
            static v => ((IEntry)v).Text, static (v, t) => ((IEntry)v).Text = t, "a", "b", "c", nameof(IEntry.Text)),
        ControlledValue<string>(typeof(SearchBarElement), nameof(SearchBarElement.Text), nameof(SearchBarElement.OnTextChanged), static (v, r) => SearchBar(v, t => r(t)),
            static v => ((MauiSearchBar)v).Text, static (v, t) => ((MauiSearchBar)v).Text = t, "a", "b", "c", nameof(MauiSearchBar.Text)),
        ControlledValue(typeof(CheckBoxElement), nameof(CheckBoxElement.IsChecked), nameof(CheckBoxElement.OnCheckedChanged), static (bool v, Action<string> r) => CheckBox(v, x => r($"{x}")),
            static v => ((ICheckBox)v).IsChecked, static (v, x) => ((ICheckBox)v).IsChecked = x, true, false, true, nameof(ICheckBox.IsChecked)),
        ControlledValue(typeof(SwitchElement), nameof(SwitchElement.IsToggled), nameof(SwitchElement.OnToggled), static (bool v, Action<string> r) => Switch(v, x => r($"{x}")),
            static v => ((ISwitch)v).IsOn, static (v, x) => ((ISwitch)v).IsOn = x, true, false, true, nameof(MauiSwitch.IsToggled), directWriteName: nameof(ISwitch.IsOn)),
        ControlledValue(typeof(RadioButtonElement), nameof(RadioButtonElement.IsChecked), nameof(RadioButtonElement.OnCheckedChanged), static (bool v, Action<string> r) => RadioButton("r", v, x => r($"{x}")),
            static v => ((MauiRadioButton)v).IsChecked, static (v, x) => ((MauiRadioButton)v).IsChecked = x, true, false, true, nameof(MauiRadioButton.IsChecked)),
        ControlledValue(typeof(SliderElement), nameof(SliderElement.Value), nameof(SliderElement.OnValueChanged), static (double v, Action<string> r) => Slider(v, x => r($"{x}")),
            static v => ((IRange)v).Value, static (v, x) => ((IRange)v).Value = x, 0.2, 0.4, 0.7, nameof(IRange.Value)),
        ControlledValue(typeof(StepperElement), nameof(StepperElement.Value), nameof(StepperElement.OnValueChanged), static (double v, Action<string> r) => Stepper(v, x => r($"{x}")),
            static v => ((MauiStepper)v).Value, static (v, x) => ((MauiStepper)v).Value = x, 2d, 4d, 7d, nameof(MauiStepper.Value)),
        ControlledValue(typeof(PickerElement), nameof(PickerElement.SelectedIndex), nameof(PickerElement.OnSelectedIndexChanged), static (int v, Action<string> r) => Picker(["a", "b", "c"], v, x => r($"{x}")),
            static v => ((MauiPicker)v).SelectedIndex, static (v, x) => ((MauiPicker)v).SelectedIndex = x, 0, 1, 2, nameof(MauiPicker.SelectedIndex)),
        ControlledValue(typeof(DatePickerElement), nameof(DatePickerElement.Date), nameof(DatePickerElement.OnDateSelected),
            static (DateTime v, Action<string> r) => DatePicker(v, d => r(d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))),
            static v => ((MauiDatePicker)v).Date, static (v, d) => ((MauiDatePicker)v).Date = d,
            new DateTime(2024, 1, 1), new DateTime(2024, 2, 2), new DateTime(2024, 3, 3), nameof(MauiDatePicker.Date), "2024-03-03"),
        ControlledValue(typeof(TimePickerElement), nameof(TimePickerElement.Time), nameof(TimePickerElement.OnTimeSelected),
            static (TimeSpan v, Action<string> r) => TimePicker(v, t => r(t.ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture))),
            static v => ((MauiTimePicker)v).Time, static (v, t) => ((MauiTimePicker)v).Time = t,
            new TimeSpan(9, 0, 0), new TimeSpan(10, 0, 0), new TimeSpan(11, 30, 0), nameof(MauiTimePicker.Time), "11:30"),
        // A pull to refresh starts refreshing: the input is true, so the declarations go true, false, true.
        ControlledValue(typeof(RefreshViewElement), nameof(RefreshViewElement.IsRefreshing), nameof(RefreshViewElement.OnRefresh),
            static (bool v, Action<string> r) => RefreshView(Label("x"), v, () => r("refresh")),
            static v => ((MauiRefreshView)v).IsRefreshing, static (v, x) => ((IRefreshView)v).IsRefreshing = x, true, false, true, nameof(MauiRefreshView.IsRefreshing), "refresh"),
        ControlledValue(typeof(FlyoutElement), nameof(FlyoutElement.IsPresented), nameof(FlyoutElement.OnIsPresentedChanged),
            static (bool v, Action<string> r) => Flyout(Page("Menu", Label("m")), Page("Detail", Label("d")), v, x => r($"{x}")) with { LayoutBehavior = FlyoutLayoutBehavior.Popover },
            static v => ((FlyoutPage)v).IsPresented, static (v, x) => ((FlyoutPage)v).IsPresented = x, true, false, true, nameof(FlyoutPage.IsPresented)),
    ];

    // ── Group E: events ─────────────────────────────────────────────────────

    private static IEnumerable<EventCase> ControlEvents() =>
    [
        new()
        {
            ElementType = typeof(ButtonElement), Property = nameof(ButtonElement.OnClick),
            Create = report => Button("b", () => report("clicked")),
            Trigger = native => ((IButton)native).Clicked(), Payload = "clicked",
        },
        new()
        {
            ElementType = typeof(EditorElement), Property = nameof(EditorElement.OnCompleted),
            Create = report => Editor("e") with { OnCompleted = () => report("completed") },
            Trigger = native => ((IEditor)native).Completed(), Payload = "completed",
        },
        new()
        {
            ElementType = typeof(EntryElement), Property = nameof(EntryElement.OnCompleted),
            Create = report => Entry("e") with { OnCompleted = () => report("completed") },
            Trigger = native => ((IEntry)native).Completed(), Payload = "completed",
        },
        new()
        {
            ElementType = typeof(SearchBarElement), Property = nameof(SearchBarElement.OnSearch),
            Create = report => SearchBar("query") with { OnSearch = q => report(q) },
            Trigger = native => ((ISearchBar)native).SearchButtonPressed(), Payload = "query",
        },
        new()
        {
            ElementType = typeof(SliderElement), Property = nameof(SliderElement.OnDragCompleted),
            Create = report => Slider(0.5) with { OnDragCompleted = () => report("done") },
            Trigger = native => ((ISlider)native).DragCompleted(), Payload = "done",
        },
        new()
        {
            ElementType = typeof(ScrollViewElement), Property = nameof(ScrollViewElement.OnScrolled),
            // MAUI raises Scrolled only when the position changes, so each trigger scrolls further.
            Create = report => ScrollView(Label("x")) with { OnScrolled = (_, y) => report(y > 0 ? "scrolled" : "none") },
            Trigger = native => ((MauiScrollView)native).SetScrolledPosition(0, ((MauiScrollView)native).ScrollY + 40), Payload = "scrolled",
        },
        new()
        {
            ElementType = typeof(WebViewElement), Property = nameof(WebViewElement.OnNavigating),
            Create = report => new WebViewElement("https://a.example/") { OnNavigating = e => report(e.Url) },
            Trigger = native => ((IWebViewController)native).SendNavigating(new WebNavigatingEventArgs(WebNavigationEvent.NewPage, null, "https://next.example/")),
            Payload = "https://next.example/",
        },
        new()
        {
            ElementType = typeof(WebViewElement), Property = nameof(WebViewElement.OnNavigated),
            Create = report => new WebViewElement("https://a.example/") { OnNavigated = e => report($"{e.Url} {e.Result}") },
            Trigger = native => ((IWebViewController)native).SendNavigated(new WebNavigatedEventArgs(WebNavigationEvent.NewPage, null, "https://next.example/", WebNavigationResult.Success)),
            Payload = "https://next.example/ Success",
        },
        new()
        {
            ElementType = typeof(RichTextHyperlink), Property = nameof(RichTextHyperlink.OnClick),
            Create = report => Label(Hyperlink("link", () => report("clicked"))),
            Trigger = native => SpanOf(native).GestureRecognizers.OfType<TapGestureRecognizer>().Single().SendTapped((View)native),
            Payload = "clicked",
        },
    ];
}

public static partial class MatrixCases
{
    // Each claim's mutation was run against its test (spec 070 §4.15, C1 to C25).
    private static IEnumerable<CoveredClaim> ControlCovered()
    {
        const string layoutFacets = "set, reorder by key keeping the views, add, remove, clear; both registries";
        const string layoutEvidence = "the Controls layouts' list sync removed (C4) and the direct layouts' insert appending (C5): red";
        foreach (var layout in new[] { typeof(VStackElement), typeof(HStackElement), typeof(GridElement), typeof(FlexLayoutElement), typeof(AbsoluteLayoutElement) })
            yield return new(layout, nameof(LayoutElement.Children), typeof(AuditCoverageTests), nameof(AuditCoverageTests.LayoutChildren_AreAddedReorderedRemovedAndCleared_KeepingKeyedViews),
                layoutFacets, layoutEvidence);

        yield return new(typeof(GridElement), nameof(GridElement.Rows), typeof(AuditCoverageTests), nameof(AuditCoverageTests.GridDefinitions_AreSetUpdatedKeptWhenEqual_AndCleared),
            "set, update, equal render keeps the definitions, clear; both registries", "rows rebuilt on every render, Controls (C6) and direct (C8): red");
        yield return new(typeof(GridElement), nameof(GridElement.Columns), typeof(AuditCoverageTests), nameof(AuditCoverageTests.GridDefinitions_AreSetUpdatedKeptWhenEqual_AndCleared),
            "set, update, equal render keeps the definitions, clear; both registries", "columns rebuilt on every render, Controls (C7) and direct (C9): red");
        yield return new(typeof(GraphicsViewElement), nameof(GraphicsViewElement.Drawable), typeof(AuditCoverageTests), nameof(AuditCoverageTests.GraphicsView_ANewDrawableIsAssigned_TheSameOneIsInvalidatedOnEveryUpdate),
            "set, a new drawable assigned, the same one invalidated on each update; Controls on both registries", "invalidated only when the drawable changes (C10): red");
        yield return new(typeof(ScrollViewElement), nameof(ScrollViewElement.Content), typeof(AuditCoverageTests), nameof(AuditCoverageTests.ScrollView_Content_IsSetReplacedAndCleared),
            "set, update in place, replace, clear; both registries", "content not cleared on null (C11): red");
        yield return new(typeof(RefreshViewElement), nameof(RefreshViewElement.Content), typeof(ScrollAndRefreshTests), nameof(ScrollAndRefreshTests.RefreshView_Content_IsSetReplacedAndCleared),
            "set (a direct child hosted), replace, clear; both registries", "content not cleared on null (C12): red");
        yield return new(typeof(FlyoutElement), nameof(FlyoutElement.Flyout), typeof(PageTests), nameof(PageTests.Flyout_SwappingTheTwoPages_MovesThemBetweenSlots),
            "set, the two pages swapped between slots", "the flyout slot written only when empty (C14): red");
        yield return new(typeof(FlyoutElement), nameof(FlyoutElement.Detail), typeof(AuditCoverageTests), nameof(AuditCoverageTests.Flyout_Pages_AreSetUpdatedInPlaceReplacedAndSwapped),
            "set, updated in place, replaced by another kind of page; both registries", "the detail slot written only when empty (C13): red");
        yield return new(typeof(LabelElement), nameof(LabelElement.Inlines), typeof(LabelInlineTests), nameof(LabelInlineTests.AnUnchangedCount_UpdatesTheSpansInPlace_AndClearsRemovedProperties),
            "set, updated in place (the same spans), a removed property cleared (the rebuild and text swap: the class's other tests)", "spans rebuilt on every render (C15): red");
        yield return new(typeof(RadioButtonElement), nameof(RadioButtonElement.ContentElement), typeof(RadioContentTests), nameof(RadioContentTests.ViewContent_IsTheContent_WithTheTemplateItAsksFor),
            "set, updated in place (the content component's state kept), replaced by text; both registries", "view content never set (C16): red");
        yield return new(typeof(RadioButtonElement), nameof(RadioButtonElement.GroupName), typeof(AuditCoverageTests), nameof(AuditCoverageTests.RadioGroupName_JoinsNamedGroups_ChangesGroup_AndClearsToTheUnnamedScope_KeepingItsNativeName),
            "a named group joined (its peer unchecked), changed to another name (the new peers, not the old), cleared to the unnamed scope, the native GUID name kept; both registries",
            "the group name ignored (C17): red");
        yield return new(typeof(RadioButtonElement), nameof(RadioButtonElement.IsIndicatorVisible), typeof(RadioIndicatorTests), nameof(RadioIndicatorTests.RadioIndicator_IsShownAndHiddenByARender_ForViewContent_AndTextKeepsTheNativeTemplate),
            "shown, hidden and shown again by a render with view content (a remount each time), no effect on text content; both registries",
            "the indicator always shown (C18) and the change applied in place (C19): red");
        yield return new(typeof(EntryElement), nameof(EntryElement.CursorPosition), typeof(EntrySelectionTests), nameof(EntrySelectionTests.ADeclaredSelection_IsWrittenAtMountAndWhenItChanges_NeverRestored),
            "set at mount, updated when it changes, never restored; the class covers echo, clamping and unmount on both registries", "the cursor never written (C1): red");
        yield return new(typeof(EntryElement), nameof(EntryElement.SelectionLength), typeof(EntrySelectionTests), nameof(EntrySelectionTests.ADeclaredSelection_IsWrittenAtMountAndWhenItChanges_NeverRestored),
            "set at mount, updated when it changes, never restored; the class covers echo, clamping and unmount on both registries", "the length never written (C2): red");
        yield return new(typeof(EntryElement), nameof(EntryElement.OnSelectionChanged), typeof(EntrySelectionTests), nameof(EntrySelectionTests.SplitNotifications_GiveOneReport_WithBothFinalValues),
            "one report per burst with the final values; programmatic writes unreported and nothing after unmount (the class's other tests)", "the report removed (C3): red");

        yield return new(typeof(PageElement), nameof(PageElement.ToolbarItems), typeof(ToolbarTests), nameof(ToolbarTests.AStructureChange_Rebuilds_InDeclaredOrder_AndTheOldItemsInvokeNothing),
            "set in declared order, rebuilt on a structure change, kept on a callback-only render (the class's other tests); both registries", "never rebuilt (C20): red");
        yield return new(typeof(ToolbarAction), nameof(ToolbarAction.Text), typeof(ToolbarTests), nameof(ToolbarTests.AStructureChange_Rebuilds_InDeclaredOrder_AndTheOldItemsInvokeNothing),
            "a changed text rebuilds the items with it", "the text ignored by the comparison (C21): red");
        yield return new(typeof(ToolbarAction), nameof(ToolbarAction.Order), typeof(ToolbarTests), nameof(ToolbarTests.AStructureChange_Rebuilds_InDeclaredOrder_AndTheOldItemsInvokeNothing),
            "an order-only change rebuilds, each item's order on the native", "the order ignored (C22): red");
        yield return new(typeof(ToolbarAction), nameof(ToolbarAction.Priority), typeof(ToolbarTests), nameof(ToolbarTests.AStructureChange_Rebuilds_InDeclaredOrder_AndTheOldItemsInvokeNothing),
            "a priority-only change rebuilds, each item's priority on the native", "the priority ignored (C23): red");
        yield return new(typeof(ToolbarAction), nameof(ToolbarAction.Icon), typeof(ToolbarTests), nameof(ToolbarTests.AStructureChange_Rebuilds_InDeclaredOrder_AndTheOldItemsInvokeNothing),
            "an icon change rebuilds (an equal icon doesn't: VisualsTests' image consumers)", "the icon ignored (C24): red");
        yield return new(typeof(ToolbarAction), nameof(ToolbarAction.OnClick), typeof(ToolbarTests), nameof(ToolbarTests.ACallbackOnlyRender_KeepsTheItems_WhichInvokeTheCurrentCallback_AndNothingAfterUnmount),
            "the current render's callback, nothing after unmount, a retired item nothing (the class's other tests); both registries", "the first callback captured (C25): red");
    }
}
