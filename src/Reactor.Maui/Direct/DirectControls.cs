using Microsoft.Maui.Graphics;
using Font = Microsoft.Maui.Font;

namespace Microsoft.UI.Reactor.Maui.Direct;

/// <summary>Fonts resolved the way Controls resolves them, so both renderers lay out the same text.</summary>
internal static class DirectFonts
{
    private static double? _defaultSize;

    // Controls' default size comes from the font manager.
    private static double DefaultSize => _defaultSize ??=
        IPlatformApplication.Current?.Services.GetService<IFontManager>()?.DefaultFontSize ?? 0;

    public static Font Default => From(null);

    /// <summary>Mirrors Controls' <c>FontExtensions.ToFont</c>: family and size, auto-scaling on, then the attributes.</summary>
    public static Font From(TextViewElement? element)
    {
        var font = Font.OfSize(element?.FontFamily, element?.FontSize ?? DefaultSize, enableScaling: true);
        if (element?.FontAttributes is { } attributes)
        {
            if (attributes.HasFlag(FontAttributes.Bold)) font = font.WithWeight(FontWeight.Bold);
            if (attributes.HasFlag(FontAttributes.Italic)) font = font.WithSlant(FontSlant.Italic);
        }
        return font;
    }
}

/// <summary>A direct view whose properties are plain fields, written through <see cref="Update{T}"/>.</summary>
public abstract class DirectControl : DirectView
{
    /// <summary>Stores <paramref name="value"/>, re-maps <paramref name="property"/>, and re-measures if it affects size.</summary>
    protected bool Update<T>(ref T field, T value, string property, bool affectsSize = false)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Remap(property);
        if (affectsSize) InvalidateMeasure();
        return true;
    }
}

public sealed class DirectLabel : DirectControl, ILabel
{
    private string _text = "";
    private Font _font = DirectFonts.Default;
    private Color _textColor = null!;
    private double _characterSpacing;
    private TextAlignment _horizontalTextAlignment = TextAlignment.Start;
    private TextAlignment _verticalTextAlignment = TextAlignment.Start;
    private Thickness _padding;
    private double _lineHeight = -1;
    private TextDecorations _textDecorations;

    public string Text => _text;
    public Font Font => _font;
    public Color TextColor => _textColor;
    public double CharacterSpacing => _characterSpacing;
    public TextAlignment HorizontalTextAlignment => _horizontalTextAlignment;
    public TextAlignment VerticalTextAlignment => _verticalTextAlignment;
    public Thickness Padding => _padding;
    public double LineHeight => _lineHeight;
    public TextDecorations TextDecorations => _textDecorations;

    internal void Apply(LabelElement element)
    {
        Update(ref _text, element.Text, nameof(IText.Text), affectsSize: true);
        Update(ref _font, DirectFonts.From(element), nameof(ITextStyle.Font), affectsSize: true);
        Update(ref _textColor, element.TextColor!, nameof(ITextStyle.TextColor));
        Update(ref _characterSpacing, element.CharacterSpacing ?? 0, nameof(ITextStyle.CharacterSpacing), affectsSize: true);
        Update(ref _horizontalTextAlignment, element.HorizontalTextAlignment ?? TextAlignment.Start, nameof(ITextAlignment.HorizontalTextAlignment));
        Update(ref _verticalTextAlignment, element.VerticalTextAlignment ?? TextAlignment.Start, nameof(ITextAlignment.VerticalTextAlignment));
        Update(ref _padding, element.Padding ?? default, nameof(IPadding.Padding), affectsSize: true);
        Update(ref _lineHeight, element.LineHeight ?? -1, nameof(ILabel.LineHeight), affectsSize: true);
        Update(ref _textDecorations, element.TextDecorations ?? TextDecorations.None, nameof(ILabel.TextDecorations));
    }
}

public sealed class DirectButton : DirectControl, ITextButton
{
    private string _text = "";
    private Font _font = DirectFonts.Default;
    private Color _textColor = null!;
    private double _characterSpacing;
    private Thickness _padding = new(double.NaN);
    private Color _strokeColor = null!;
    private double _strokeThickness = -1;
    private int _cornerRadius = -1;

    public Action? OnClicked { get; internal set; }

    public string Text => _text;
    public Font Font => _font;
    public Color TextColor => _textColor;
    public double CharacterSpacing => _characterSpacing;
    public Thickness Padding => _padding;
    public Color StrokeColor => _strokeColor;
    public double StrokeThickness => _strokeThickness;
    public int CornerRadius => _cornerRadius;

    public void Clicked() => OnClicked?.Invoke();
    public void Pressed() { }
    public void Released() { }

    internal void Apply(ButtonElement element)
    {
        Update(ref _text, element.Text, nameof(IText.Text), affectsSize: true);
        Update(ref _font, DirectFonts.From(element), nameof(ITextStyle.Font), affectsSize: true);
        Update(ref _textColor, element.TextColor!, nameof(ITextStyle.TextColor));
        Update(ref _characterSpacing, element.CharacterSpacing ?? 0, nameof(ITextStyle.CharacterSpacing), affectsSize: true);
        // Controls' default button padding is NaN on every side: "use the platform's".
        Update(ref _padding, element.Padding ?? new Thickness(double.NaN), nameof(IPadding.Padding), affectsSize: true);
        Update(ref _strokeColor, element.BorderColor!, nameof(IButtonStroke.StrokeColor));
        Update(ref _strokeThickness, element.BorderWidth ?? -1, nameof(IButtonStroke.StrokeThickness));
        Update(ref _cornerRadius, element.CornerRadius ?? -1, nameof(IButtonStroke.CornerRadius));
    }
}

public sealed class DirectEntry : DirectControl, IEntry
{
    private string _text = "";
    private string _placeholder = "";
    private Color _placeholderColor = null!;
    private Font _font = DirectFonts.Default;
    private Color _textColor = null!;
    private double _characterSpacing;
    private TextAlignment _horizontalTextAlignment = TextAlignment.Start;
    private bool _isReadOnly;
    private Keyboard _keyboard = Keyboard.Default;
    private bool _isSpellCheckEnabled = true;
    private int _maxLength = int.MaxValue;
    private bool _isPassword;
    private ReturnType _returnType = ReturnType.Default;
    private ClearButtonVisibility _clearButtonVisibility = ClearButtonVisibility.Never;

    /// <summary>Raised when the platform view reports new text, i.e. when the user types.</summary>
    public Action<string>? OnTextChanged { get; internal set; }

    public Action? OnCompleted { get; internal set; }

    // The handler writes here when the user types; Reactor writes through SetText.
    public string Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (_text == value) return;
            _text = value;
            OnTextChanged?.Invoke(value);
        }
    }

    public string Placeholder => _placeholder;
    public Color PlaceholderColor { get => _placeholderColor; set => _placeholderColor = value; }
    public Font Font => _font;
    public Color TextColor => _textColor;
    public double CharacterSpacing => _characterSpacing;
    public TextAlignment HorizontalTextAlignment => _horizontalTextAlignment;
    public TextAlignment VerticalTextAlignment => TextAlignment.Center;
    public bool IsReadOnly => _isReadOnly;
    public Keyboard Keyboard => _keyboard;
    public bool IsSpellCheckEnabled => _isSpellCheckEnabled;
    public bool IsTextPredictionEnabled => true;
    public int MaxLength => _maxLength;
    private int _cursorPosition;
    private int _selectionLength;

    /// <summary>Raised when the platform handler reports a new cursor position or selection length.</summary>
    internal Action? OnSelectionChanged { get; set; }

    // The handler writes these when the user moves the selection; Reactor writes through SetSelection.
    public int CursorPosition
    {
        get => _cursorPosition;
        set
        {
            if (_cursorPosition == value) return;
            _cursorPosition = value;
            OnSelectionChanged?.Invoke();
        }
    }

    public int SelectionLength
    {
        get => _selectionLength;
        set
        {
            if (_selectionLength == value) return;
            _selectionLength = value;
            OnSelectionChanged?.Invoke();
        }
    }

    internal void SetSelection(int? cursor, int? length)
    {
        if (cursor is { } c && _cursorPosition != c)
        {
            _cursorPosition = c;
            Remap(nameof(ITextInput.CursorPosition));
        }
        if (length is { } l && _selectionLength != l)
        {
            _selectionLength = l;
            Remap(nameof(ITextInput.SelectionLength));
        }
    }
    public bool IsPassword => _isPassword;
    public ReturnType ReturnType => _returnType;
    public ClearButtonVisibility ClearButtonVisibility => _clearButtonVisibility;

    public void Completed() => OnCompleted?.Invoke();

    internal void SetText(string text)
    {
        if (_text == text) return;
        _text = text;
        Remap(nameof(ITextInput.Text));
    }

    internal void Apply(EntryElement element)
    {
        Update(ref _placeholder, element.Placeholder ?? "", nameof(IPlaceholder.Placeholder));
        Update(ref _placeholderColor, element.PlaceholderColor!, nameof(IPlaceholder.PlaceholderColor));
        Update(ref _font, DirectFonts.From(element), nameof(ITextStyle.Font), affectsSize: true);
        Update(ref _textColor, element.TextColor!, nameof(ITextStyle.TextColor));
        Update(ref _characterSpacing, element.CharacterSpacing ?? 0, nameof(ITextStyle.CharacterSpacing));
        Update(ref _horizontalTextAlignment, element.HorizontalTextAlignment ?? TextAlignment.Start, nameof(ITextAlignment.HorizontalTextAlignment));
        Update(ref _isReadOnly, element.IsReadOnly ?? false, nameof(ITextInput.IsReadOnly));
        Update(ref _keyboard, element.Keyboard ?? Keyboard.Default, nameof(ITextInput.Keyboard));
        Update(ref _isSpellCheckEnabled, element.IsSpellCheckEnabled ?? true, nameof(ITextInput.IsSpellCheckEnabled));
        Update(ref _maxLength, element.MaxLength ?? int.MaxValue, nameof(ITextInput.MaxLength));
        Update(ref _isPassword, element.IsPassword ?? false, nameof(IEntry.IsPassword));
        Update(ref _returnType, element.ReturnType ?? ReturnType.Default, nameof(IEntry.ReturnType));
        Update(ref _clearButtonVisibility, element.ClearButtonVisibility ?? ClearButtonVisibility.Never, nameof(IEntry.ClearButtonVisibility));
    }
}

public sealed class DirectCheckBox : DirectControl, ICheckBox
{
    private bool _isChecked;
    private Color? _color;

    public Action<bool>? OnCheckedChanged { get; internal set; }

    // The handler writes here when the user taps.
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            OnCheckedChanged?.Invoke(value);
        }
    }

    public Paint Foreground => _color?.AsPaint()!;

    internal void SetChecked(bool value)
    {
        if (_isChecked == value) return;
        _isChecked = value;
        Remap(nameof(ICheckBox.IsChecked));
    }

    // Kept as a color, compared by value: a new paint per render would remap the foreground every time.
    internal void Apply(CheckBoxElement element) =>
        Update(ref _color, element.Color, nameof(ICheckBox.Foreground));
}

public sealed class DirectSwitch : DirectControl, ISwitch
{
    private bool _isOn;
    private Color? _onColor;
    private Color _thumbColor = null!;

    public Action<bool>? OnToggled { get; internal set; }

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value) return;
            _isOn = value;
            // Controls' track color depends on the state.
            Remap(nameof(ISwitch.TrackColor));
            OnToggled?.Invoke(value);
        }
    }

    // As in Controls: the on-color while on, the (unset) off-color otherwise.
    public Color TrackColor => (_isOn ? _onColor : null)!;
    public Color ThumbColor => _thumbColor;

    internal void SetOn(bool value)
    {
        if (_isOn == value) return;
        _isOn = value;
        Remap(nameof(ISwitch.IsOn));
        Remap(nameof(ISwitch.TrackColor));
    }

    internal void Apply(SwitchElement element)
    {
        if (!Equals(_onColor, element.OnColor))
        {
            _onColor = element.OnColor;
            Remap(nameof(ISwitch.TrackColor));
        }
        Update(ref _thumbColor, element.ThumbColor!, nameof(ISwitch.ThumbColor));
    }
}

public sealed class DirectSlider : DirectControl, ISlider
{
    private double _minimum;
    private double _maximum = 1;
    private double _value;
    private Color _minimumTrackColor = null!;
    private Color _maximumTrackColor = null!;
    private Color _thumbColor = null!;

    public Action<double>? OnValueChanged { get; internal set; }
    public Action? OnDragCompleted { get; internal set; }

    public double Minimum => _minimum;
    public double Maximum => _maximum;

    public double Value
    {
        get => _value;
        set
        {
            value = Math.Clamp(value, _minimum, _maximum);
            if (_value == value) return;
            _value = value;
            OnValueChanged?.Invoke(value);
        }
    }

    public Color MinimumTrackColor => _minimumTrackColor;
    public Color MaximumTrackColor => _maximumTrackColor;
    public Color ThumbColor => _thumbColor;
    public IImageSource ThumbImageSource => null!;

    public void DragStarted() { }
    public void DragCompleted() => OnDragCompleted?.Invoke();

    internal void SetRange(double minimum, double maximum, double value)
    {
        if (minimum > maximum) throw new ArgumentException($"Minimum ({minimum}) is greater than maximum ({maximum}).");
        Update(ref _minimum, minimum, nameof(IRange.Minimum));
        Update(ref _maximum, maximum, nameof(IRange.Maximum));
        Update(ref _value, Math.Clamp(value, minimum, maximum), nameof(IRange.Value));
    }

    internal void Apply(SliderElement element)
    {
        Update(ref _minimumTrackColor, element.MinimumTrackColor!, nameof(ISlider.MinimumTrackColor));
        Update(ref _maximumTrackColor, element.MaximumTrackColor!, nameof(ISlider.MaximumTrackColor));
        Update(ref _thumbColor, element.ThumbColor!, nameof(ISlider.ThumbColor));
    }
}

public sealed class DirectProgressBar : DirectControl, IProgress
{
#if ANDROID
    static DirectProgressBar() => Handlers.ProgressColorClear.Register();
#endif

    private double _progress;
    private Color _progressColor = null!;

    public double Progress => _progress;
    public Color ProgressColor => _progressColor;

    internal void Apply(ProgressBarElement element)
    {
        Update(ref _progress, element.Progress, nameof(IProgress.Progress));
        Update(ref _progressColor, element.ProgressColor!, nameof(IProgress.ProgressColor));
    }
}

public sealed class DirectActivityIndicator : DirectControl, IActivityIndicator
{
    private bool _isRunning;
    private Color _color = null!;

    public bool IsRunning => _isRunning;
    public Color Color => _color;

    internal void Apply(ActivityIndicatorElement element)
    {
        Update(ref _isRunning, element.IsRunning, nameof(IActivityIndicator.IsRunning));
        Update(ref _color, element.Color!, nameof(IActivityIndicator.Color));
    }
}

public sealed class DirectImage : DirectControl, Microsoft.Maui.IImage
{
    private IImageSource? _source;
    private Aspect _aspect = Aspect.AspectFit;
    private bool _isAnimationPlaying;

    public IImageSource Source => _source!;
    public Aspect Aspect => _aspect;
    public bool IsOpaque => false;
    public bool IsAnimationPlaying => _isAnimationPlaying;
    public bool IsLoading { get; private set; }

    public void UpdateIsLoading(bool isLoading) => IsLoading = isLoading;

    internal void Apply(ImageElement? previous, ImageElement element)
    {
        // Factories create a new source per render, so sources are compared by what they load.
        if (previous is null || !Handlers.Props.SameImage(previous.Source, element.Source))
        {
            _source = element.Source;
            Remap(nameof(IImageSourcePart.Source));
            InvalidateMeasure();
        }
        Update(ref _aspect, element.Aspect ?? Aspect.AspectFit, nameof(Microsoft.Maui.IImage.Aspect));
        Update(ref _isAnimationPlaying, element.IsAnimationPlaying ?? false, nameof(IImageSourcePart.IsAnimationPlaying));
    }
}
