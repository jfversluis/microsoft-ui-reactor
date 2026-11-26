using Microsoft.Maui.Controls.Shapes;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Maui;

/// <summary>Base of every MAUI view element. Shared properties live in <see cref="Modifiers"/>.</summary>
public abstract record ViewElement : Element, Microsoft.UI.Reactor.Hosting.IRefTarget
{
    /// <summary>Layout, appearance, accessibility and gesture settings set through the fluent modifiers.</summary>
    public ViewModifiers? Modifiers { get; init; }

    Microsoft.UI.Reactor.Hosting.ElementRefCell? Microsoft.UI.Reactor.Hosting.IRefTarget.RefCell => Modifiers?.Ref;
}

/// <summary>Base of elements that show text with a font.</summary>
public abstract record TextViewElement : ViewElement
{
    public double? FontSize { get; init; }
    public FontAttributes? FontAttributes { get; init; }
    public string? FontFamily { get; init; }
    public Color? TextColor { get; init; }
    public double? CharacterSpacing { get; init; }
}

/// <summary>Base of layout elements that host a list of children.</summary>
public abstract record LayoutElement(Element?[] Children) : ViewElement
{
    public Thickness? Padding { get; init; }
}

/// <summary>
/// The view settings every element accepts. Unset (<c>null</c>) values leave MAUI's defaults in
/// place; a value that is removed on a later render is reset to the default.
/// </summary>
public sealed record ViewModifiers
{
    internal static readonly ViewModifiers Empty = new();

    // Layout
    public Thickness? Margin { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public double? MinWidth { get; init; }
    public double? MinHeight { get; init; }
    public double? MaxWidth { get; init; }
    public double? MaxHeight { get; init; }
    public LayoutOptions? HorizontalOptions { get; init; }
    public LayoutOptions? VerticalOptions { get; init; }

    // Placement in a parent Grid, FlexLayout or AbsoluteLayout
    public int? GridRow { get; init; }
    public int? GridColumn { get; init; }
    public int? GridRowSpan { get; init; }
    public int? GridColumnSpan { get; init; }
    public float? FlexGrow { get; init; }
    public float? FlexShrink { get; init; }
    public Microsoft.Maui.Layouts.FlexBasis? FlexBasis { get; init; }
    public Microsoft.Maui.Layouts.FlexAlignSelf? FlexAlignSelf { get; init; }
    public Rect? AbsoluteBounds { get; init; }
    public Microsoft.Maui.Layouts.AbsoluteLayoutFlags? AbsoluteFlags { get; init; }

    // Appearance
    public Color? BackgroundColor { get; init; }
    public Brush? Background { get; init; }
    public double? Opacity { get; init; }
    public bool? IsVisible { get; init; }
    public bool? IsEnabled { get; init; }
    public double? Rotation { get; init; }
    public double? Scale { get; init; }
    public double? TranslationX { get; init; }
    public double? TranslationY { get; init; }
    public int? ZIndex { get; init; }
    public Shadow? Shadow { get; init; }
    public Geometry? Clip { get; init; }
    public bool? InputTransparent { get; init; }
    public Style? Style { get; init; }
    public string[]? StyleClass { get; init; }

    // Accessibility. AutomationId is applied once, at mount: MAUI does not allow changing it.
    public string? AutomationId { get; init; }
    public string? SemanticDescription { get; init; }
    public string? SemanticHint { get; init; }
    public SemanticHeadingLevel? SemanticHeadingLevel { get; init; }

    // Gestures
    public Action? OnTap { get; init; }
    public int? TapCount { get; init; }
    public Action<SwipeDirection>? OnSwipe { get; init; }
    public Action<PanUpdatedEventArgs>? OnPan { get; init; }

    /// <summary>Runs against the native MAUI view after every mount and update; the escape hatch for anything not modelled.</summary>
    public Action<object>? Configure { get; init; }

    /// <summary>The ref <c>.Ref(...)</c> attached; the engine binds it to the mounted view.</summary>
    internal Microsoft.UI.Reactor.Hosting.ElementRefCell? Ref { get; init; }

    /// <summary>Whether a setting that only a <c>Microsoft.Maui.Controls</c> view supports is used.</summary>
    internal bool NeedsControls =>
        Style is not null || StyleClass is not null || Shadow is not null || Clip is not null || Handlers.BrushComparer.Normalize(Background) is not null
        || OnTap is not null || OnSwipe is not null || OnPan is not null || Configure is not null
        || FlexGrow is not null || FlexShrink is not null || FlexBasis is not null || FlexAlignSelf is not null
        || AbsoluteBounds is not null || AbsoluteFlags is not null;
}

/// <summary>A row or column size for <c>Grid</c>: <c>Auto</c>, <c>*</c>, <c>2*</c> or an absolute number.</summary>
public static class GridLengths
{
    /// <summary>Parses a comma-separated list such as <c>"Auto,*,2*,48"</c>.</summary>
    public static GridLength[] Parse(string definitions)
    {
        if (string.IsNullOrWhiteSpace(definitions)) return [];
        var parts = definitions.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var result = new GridLength[parts.Length];
        for (var i = 0; i < parts.Length; i++) result[i] = ParseOne(parts[i]);
        return result;
    }

    private static GridLength ParseOne(string text)
    {
        if (text.Equals("auto", StringComparison.OrdinalIgnoreCase)) return GridLength.Auto;
        if (text.EndsWith('*'))
        {
            var weight = text.Length == 1 ? 1 : ParseNumber(text[..^1], text);
            return new GridLength(weight, GridUnitType.Star);
        }
        return new GridLength(ParseNumber(text, text), GridUnitType.Absolute);
    }

    private static double ParseNumber(string number, string original) =>
        double.TryParse(number, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : throw new FormatException($"'{original}' is not a grid length. Use Auto, *, 2* or a number.");
}
