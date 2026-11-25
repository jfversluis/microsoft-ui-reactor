using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Maui;

/// <summary>
/// A MAUI <see cref="Microsoft.Maui.Controls.Shell"/> (spec 069 §4): items (<c>TabBar</c>,
/// <c>FlyoutItem</c>, <c>MenuItem</c>), each holding tabs. <see cref="Selected"/> is the key of the
/// tab (or top tab) shown, held by the app; <see cref="OnSelected"/> receives the key the user
/// switched to once Shell has shown it. A switch the app doesn't accept is undone.
/// </summary>
public sealed record ShellElement(string? Selected, Action<string>? OnSelected, Element?[] Items) : Element
{
    public string? Title { get; init; }
    public FlyoutBehavior? FlyoutBehavior { get; init; }

    /// <summary>Whether the flyout is open; <see langword="null"/> leaves it to the user.</summary>
    public bool? FlyoutIsPresented { get; init; }

    /// <summary>Called when the user opens or closes the flyout.</summary>
    public Action<bool>? OnFlyoutIsPresentedChanged { get; init; }

    /// <summary>Shown above the flyout's items; reconciled in place, so it updates live.</summary>
    public Element? FlyoutHeader { get; init; }

    /// <summary>Shown below the flyout's items; reconciled in place, so it updates live.</summary>
    public Element? FlyoutFooter { get; init; }

    /// <summary>Replaces the flyout's list of items.</summary>
    public Element? FlyoutContent { get; init; }

    public Color? BackgroundColor { get; init; }
    public Color? ForegroundColor { get; init; }
    public Color? TitleColor { get; init; }
    public Color? DisabledColor { get; init; }
    public Color? UnselectedColor { get; init; }
    public Color? FlyoutBackgroundColor { get; init; }
    public Color? TabBarBackgroundColor { get; init; }
    public Color? TabBarForegroundColor { get; init; }
    public Color? TabBarTitleColor { get; init; }
    public Color? TabBarUnselectedColor { get; init; }
    public Color? TabBarDisabledColor { get; init; }
}

/// <summary>A Shell item shown as a bottom tab bar. Keyed by its position unless given a key.</summary>
public sealed record TabBarElement(ShellTabElement[] Tabs) : Element;

/// <summary>A Shell item listed in the flyout, whose tabs show at the bottom. Keyed by its position unless given a key.</summary>
public sealed record FlyoutItemElement(string? Title, ImageSource? Icon, ShellTabElement[] Tabs) : Element
{
    public FlyoutDisplayOptions? DisplayOptions { get; init; }
}

/// <summary>
/// A Shell tab (a <see cref="Tab"/>): one page or <c>NavigationHost</c> in <see cref="Content"/>, or top
/// tabs in <see cref="Contents"/>. Its <see cref="ReactorElement.Key"/> is its selection key, unique in the Shell.
/// </summary>
public sealed record ShellTabElement(string? Title, ImageSource? Icon, Element? Content) : Element
{
    /// <summary>Top tabs, each holding a page. When set, <see cref="ShellTabElement.Content"/> isn't used.</summary>
    public ShellContentElement[] Contents { get; init; } = [];
}

/// <summary>A top tab of a <see cref="ShellTabElement"/>. Its <see cref="ReactorElement.Key"/> is its selection key.</summary>
public sealed record ShellContentElement(string? Title, ImageSource? Icon, Element? Page) : Element;

/// <summary>An item in the Shell's flyout that runs an action instead of showing a page.</summary>
public sealed record ShellMenuItemElement(string Text, Action? OnClick) : Element
{
    public ImageSource? Icon { get; init; }
}

/// <summary>The back button of a page in a Shell (MAUI's <c>BackButtonBehavior</c>).</summary>
/// <param name="Text">Replaces the back button's title.</param>
/// <param name="Icon">Replaces the back button's icon.</param>
/// <param name="IsVisible">Whether the back button shows.</param>
/// <param name="IsEnabled">Whether the back button, and the iOS back swipe, work.</param>
public sealed record ShellBackButton(string? Text = null, ImageSource? Icon = null, bool IsVisible = true, bool IsEnabled = true);

/// <summary>Fluent setters for <see cref="ShellElement"/> and a page's Shell properties.</summary>
public static class ShellElementExtensions
{
    public static ShellElement FlyoutBehavior(this ShellElement shell, FlyoutBehavior behavior) => shell with { FlyoutBehavior = behavior };

    /// <summary>Controls whether the flyout is open; <paramref name="onChanged"/> receives the user's changes.</summary>
    public static ShellElement FlyoutIsPresented(this ShellElement shell, bool isPresented, Action<bool>? onChanged) =>
        shell with { FlyoutIsPresented = isPresented, OnFlyoutIsPresentedChanged = onChanged };

    public static ShellElement FlyoutHeader(this ShellElement shell, Element? header) => shell with { FlyoutHeader = header };

    public static ShellElement FlyoutFooter(this ShellElement shell, Element? footer) => shell with { FlyoutFooter = footer };

    public static ShellElement FlyoutContent(this ShellElement shell, Element? content) => shell with { FlyoutContent = content };

    /// <summary>Whether the navigation bar shows above this page in a Shell (live).</summary>
    public static PageElement NavBarIsVisible(this PageElement page, bool visible) => page with { NavBarIsVisible = visible };

    /// <summary>Whether the tab bar shows below this page in a Shell (live).</summary>
    public static PageElement TabBarIsVisible(this PageElement page, bool visible) => page with { TabBarIsVisible = visible };

    /// <summary>A view shown in the navigation bar in place of the title (live).</summary>
    public static PageElement TitleView(this PageElement page, Element? titleView) => page with { TitleView = titleView };
}
