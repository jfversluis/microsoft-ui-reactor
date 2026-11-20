using Microsoft.Maui.Graphics;
using Font = Microsoft.Maui.Font;

namespace Microsoft.UI.Reactor.Maui.Direct;

/// <summary>Fonts resolved the way Controls resolves them, so both renderers lay out the same text.</summary>
internal static class DirectFonts
{
    private static Font? _default;

    // Controls' default: the font manager's default size, with auto-scaling on.
    public static Font Default => _default ??= OfSize(
        IPlatformApplication.Current?.Services.GetService<IFontManager>()?.DefaultFontSize ?? 0);

    public static Font OfSize(double size) => Font.OfSize(null, size, enableScaling: true);
}

public sealed class DirectLabel : DirectView, ILabel
{
    public string Text { get; private set; } = "";
    public Font Font { get; private set; } = DirectFonts.Default;
    public TextDecorations TextDecorations { get; private set; }

    public Color TextColor => null!;
    public double CharacterSpacing => 0;
    public TextAlignment HorizontalTextAlignment => TextAlignment.Start;
    public TextAlignment VerticalTextAlignment => TextAlignment.Start;
    public Thickness Padding => default;
    public double LineHeight => -1;

    internal void SetText(string text)
    {
        if (Text == text) return;
        Text = text;
        Remap(nameof(IText.Text));
        InvalidateMeasure();
    }

    internal void SetFont(double? size, FontAttributes? attributes)
    {
        var font = size is { } s ? DirectFonts.OfSize(s) : DirectFonts.Default;
        if (attributes is { } a && a.HasFlag(FontAttributes.Bold)) font = font.WithWeight(FontWeight.Bold);
        if (Font == font) return;
        Font = font;
        Remap(nameof(ITextStyle.Font));
        InvalidateMeasure();
    }

    internal void SetTextDecorations(TextDecorations? decorations)
    {
        var value = decorations ?? TextDecorations.None;
        if (TextDecorations == value) return;
        TextDecorations = value;
        Remap(nameof(ILabel.TextDecorations));
    }
}

public sealed class DirectButton : DirectView, ITextButton
{
    public string Text { get; private set; } = "";
    public Action? OnClicked { get; internal set; }

    public Color TextColor => null!;
    public Font Font => DirectFonts.Default;
    public double CharacterSpacing => 0;
    public Thickness Padding => new(double.NaN);
    public Color StrokeColor => null!;
    public double StrokeThickness => -1;
    public int CornerRadius => -1;

    public void Clicked() => OnClicked?.Invoke();
    public void Pressed() { }
    public void Released() { }

    internal void SetText(string text)
    {
        if (Text == text) return;
        Text = text;
        Remap(nameof(IText.Text));
        InvalidateMeasure();
    }
}

public sealed class DirectEntry : DirectView, IEntry
{
    private string _text = "";

    /// <summary>Raised when the platform view reports new text, i.e. when the user types.</summary>
    public Action<string>? OnTextChanged { get; internal set; }

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

    public string Placeholder { get; private set; } = "";

    public Color TextColor => null!;
    public Font Font => DirectFonts.Default;
    public double CharacterSpacing => 0;
    public Color PlaceholderColor { get; set; } = null!;
    public TextAlignment HorizontalTextAlignment => TextAlignment.Start;
    public TextAlignment VerticalTextAlignment => TextAlignment.Center;
    public bool IsReadOnly => false;
    public Keyboard Keyboard => Keyboard.Default;
    public bool IsSpellCheckEnabled => true;
    public bool IsTextPredictionEnabled => true;
    public int MaxLength => int.MaxValue;
    public int CursorPosition { get; set; }
    public int SelectionLength { get; set; }
    public bool IsPassword => false;
    public ReturnType ReturnType => ReturnType.Default;
    public ClearButtonVisibility ClearButtonVisibility => ClearButtonVisibility.Never;

    public void Completed() { }

    internal void SetText(string text)
    {
        if (_text == text) return;
        _text = text;
        Remap(nameof(ITextInput.Text));
    }

    internal void SetPlaceholder(string? placeholder)
    {
        var value = placeholder ?? "";
        if (Placeholder == value) return;
        Placeholder = value;
        Remap(nameof(IPlaceholder.Placeholder));
    }
}
