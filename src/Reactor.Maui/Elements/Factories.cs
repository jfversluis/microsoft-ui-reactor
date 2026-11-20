using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Maui;

/// <summary>
/// The DSL entry point: <c>using static Microsoft.UI.Reactor.Maui.Factories;</c>.
/// Re-exposes the core factories next to the MAUI control factories.
/// </summary>
public static class Factories
{
    public static ComponentElement Component<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>()
        where T : Component, new() => CoreFactories.Component<T>();

    public static ComponentElement<TProps> Component<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T, TProps>(TProps props)
        where T : Component<TProps>, new() => CoreFactories.Component<T, TProps>(props);

    public static GroupElement Group(params Element?[] children) => CoreFactories.Group(children);

    public static EmptyElement Empty() => CoreFactories.Empty();

    public static FuncElement RenderEachTime(Func<RenderContext, Element?> render) => CoreFactories.RenderEachTime(render);

    public static LabelElement Label(string text) => new(text);

    public static ButtonElement Button(string text, Action? onClick = null) => new(text, onClick);

    public static EntryElement Entry(string text, Action<string>? onTextChanged = null, string? placeholder = null) =>
        new(text, onTextChanged) { Placeholder = placeholder };

    public static CheckBoxElement CheckBox(bool isChecked, Action<bool>? onCheckedChanged = null) => new(isChecked, onCheckedChanged);

    public static VStackElement VStack(params Element?[] children) => new(children);

    public static VStackElement VStack(double spacing, params Element?[] children) => new(children) { Spacing = spacing };

    public static HStackElement HStack(params Element?[] children) => new(children);

    public static HStackElement HStack(double spacing, params Element?[] children) => new(children) { Spacing = spacing };

    public static ScrollViewElement ScrollView(Element? content) => new(content);
}

/// <summary>Fluent modifiers. The generic ones keep the concrete element type through a chain.</summary>
public static class ViewElementExtensions
{
    public static T Margin<T>(this T element, double uniform) where T : ViewElement =>
        (T)(((ViewElement)element) with { Margin = new Thickness(uniform) });

    public static T Margin<T>(this T element, double horizontal, double vertical) where T : ViewElement =>
        (T)(((ViewElement)element) with { Margin = new Thickness(horizontal, vertical) });

    public static T Width<T>(this T element, double width) where T : ViewElement =>
        (T)(((ViewElement)element) with { Width = width });

    public static T HorizontalOptions<T>(this T element, LayoutOptions options) where T : ViewElement =>
        (T)(((ViewElement)element) with { HorizontalOptions = options });

    public static T AutomationId<T>(this T element, string automationId) where T : ViewElement =>
        (T)(((ViewElement)element) with { AutomationId = automationId });

    public static LabelElement FontSize(this LabelElement element, double size) => element with { FontSize = size };

    public static LabelElement Bold(this LabelElement element) => element with { FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold };

    public static LabelElement Strikethrough(this LabelElement element, bool enabled = true) =>
        element with { TextDecorations = enabled ? Microsoft.Maui.TextDecorations.Strikethrough : null };
}
