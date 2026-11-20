using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Maui;

/// <summary>Properties every MAUI view element shares.</summary>
public abstract record ViewElement : Element
{
    public Thickness? Margin { get; init; }
    public double? Width { get; init; }
    public LayoutOptions? HorizontalOptions { get; init; }

    /// <summary>Set once at mount: MAUI does not allow changing a view's AutomationId.</summary>
    public string? AutomationId { get; init; }
}

public sealed record LabelElement(string Text) : ViewElement
{
    public double? FontSize { get; init; }
    public FontAttributes? FontAttributes { get; init; }
    public TextDecorations? TextDecorations { get; init; }
}

public sealed record ButtonElement(string Text, Action? OnClick = null) : ViewElement;

/// <summary>A controlled text entry: it always shows <see cref="Text"/>.</summary>
public sealed record EntryElement(string Text, Action<string>? OnTextChanged = null) : ViewElement
{
    public string? Placeholder { get; init; }
}

/// <summary>A controlled check box: it always shows <see cref="IsChecked"/>.</summary>
public sealed record CheckBoxElement(bool IsChecked, Action<bool>? OnCheckedChanged = null) : ViewElement;

public sealed record VStackElement(Element?[] Children) : ViewElement
{
    public double Spacing { get; init; }
}

public sealed record HStackElement(Element?[] Children) : ViewElement
{
    public double Spacing { get; init; }
}

public sealed record ScrollViewElement(Element? Content) : ViewElement;
