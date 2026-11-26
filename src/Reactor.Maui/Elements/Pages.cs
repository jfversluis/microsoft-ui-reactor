using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Maui;

/// <summary>An item in a page's toolbar (navigation bar).</summary>
public sealed record ToolbarAction(string Text, Action OnClick)
{
    public ImageSource? Icon { get; init; }
    public ToolbarItemOrder Order { get; init; }
    public int Priority { get; init; }
}

/// <summary>
/// A page. As the root of a <c>ReactorWindow</c>, or inside a <c>NavigationStack</c>, <c>Tabs</c> or
/// <c>Flyout</c>, it becomes a MAUI <see cref="ContentPage"/>.
/// </summary>
public sealed record PageElement(string? Title, Element? Content) : Element
{
    public Color? BackgroundColor { get; init; }
    public Thickness? Padding { get; init; }

    /// <summary>The icon shown for this page in a tab bar or flyout.</summary>
    public ImageSource? Icon { get; init; }

    public ToolbarAction[] ToolbarItems { get; init; } = [];
    public bool? HasNavigationBar { get; init; }
    public bool? HasBackButton { get; init; }
    public string? BackButtonTitle { get; init; }
    /// <summary>Called when the page becomes visible: shown, or uncovered.</summary>
    public Action? OnAppearing { get; init; }

    /// <summary>
    /// Called when the page stops being visible while it is still declared: covered by another page,
    /// or popped by the user before the app's state drops it. Not called once the app has removed the
    /// page from its state; release resources in an effect's cleanup, which runs then.
    /// </summary>
    public Action? OnDisappearing { get; init; }

    /// <summary>Automation id of the page, applied once at mount.</summary>
    public string? AutomationId { get; init; }

    // ── In a Shell (spec 069 §4.1): Shell's attached properties, which nothing else reads ──

    /// <summary>Whether the navigation bar shows above this page in a Shell (live).</summary>
    public bool? NavBarIsVisible { get; init; }

    /// <summary>Whether the tab bar shows below this page in a Shell (live).</summary>
    public bool? TabBarIsVisible { get; init; }

    /// <summary>A view shown in a Shell's navigation bar in place of the title (live).</summary>
    public Element? TitleView { get; init; }

    /// <summary>The search box shown in a Shell's navigation bar (live): an instance of the app's <see cref="Microsoft.Maui.Controls.SearchHandler"/> subclass.</summary>
    public SearchHandler? SearchHandler { get; init; }

    /// <summary>How a Shell presents this page. Read when the page is pushed, so it is set before the push.</summary>
    public PresentationMode? PresentationMode { get; init; }

    /// <summary>The back button shown on this page in a Shell (live).</summary>
    public ShellBackButton? BackButtonBehavior { get; init; }

    /// <summary>
    /// How iOS presents this page when it is shown as a modal (a <c>NavigationStack</c>'s or
    /// <c>NavigationHost</c>'s modals, or a Shell's): <c>PageSheet</c>, <c>FormSheet</c>,
    /// <c>OverFullScreen</c>, … Set before the page is presented. iOS only: it is not applied on Mac
    /// Catalyst, where MAUI can't dismiss a sheet-style modal, and Android ignores it.
    /// </summary>
    public Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.UIModalPresentationStyle? ModalPresentationStyle { get; init; }
}

/// <summary>
/// A navigation stack: <see cref="Pages"/> is the stack, root first, and MAUI's navigation is kept in
/// step with it (pushes and pops animate). When the user navigates back with the back button or
/// gesture, <see cref="OnPopped"/> receives the new depth; update the stack to accept it, or the
/// page is pushed back.
/// </summary>
public sealed record NavigationStackElement(Element?[] Pages) : Element
{
    public Action<int>? OnPopped { get; init; }

    /// <summary>
    /// Whether the pushes and pops that bring MAUI's stack to this declaration animate (default
    /// <see langword="true"/>). A newer declaration's value replaces it for the operations still to run.
    /// </summary>
    public bool Animated { get; init; } = true;

    // Set when a NavigationHost renders this stack (spec 069 §2.4).
    internal Microsoft.UI.Reactor.Navigation.NavigationHostBinding? HostBinding { get; init; }
    internal Microsoft.UI.Reactor.Navigation.HostDeclaration? HostDeclaration { get; init; }

    /// <summary>The title and icon shown for the stack when it is a tab or flyout detail.</summary>
    public string? Title { get; init; }

    public ImageSource? Icon { get; init; }
    public Color? BarBackgroundColor { get; init; }
    public Color? BarTextColor { get; init; }

    /// <summary>Pages shown modally above the stack, bottom first.</summary>
    public Element?[] Modals { get; init; } = [];

    /// <summary>Called with the remaining modal count when the user dismisses a modal page.</summary>
    public Action<int>? OnModalDismissed { get; init; }
}

/// <summary>Pages shown as tabs. Controlled by <see cref="SelectedIndex"/>.</summary>
public sealed record TabsElement(Element?[] Pages, int SelectedIndex, Action<int>? OnSelectedIndexChanged = null) : Element
{
    public Color? BarBackgroundColor { get; init; }
    public Color? SelectedTabColor { get; init; }
    public Color? UnselectedTabColor { get; init; }
}

/// <summary>A flyout (drawer) beside a detail page. Controlled by <see cref="IsPresented"/>.</summary>
public sealed record FlyoutElement(Element? Flyout, Element? Detail, bool IsPresented, Action<bool>? OnIsPresentedChanged = null) : Element
{
    public FlyoutLayoutBehavior? LayoutBehavior { get; init; }
}

/// <summary>
/// What a <c>NavigationHost</c> renders inside a <c>ShellTab</c> (spec 069 §4.2): its pages become the
/// Shell section's stack (the root is the tab's content page), never a <see cref="NavigationPage"/>.
/// </summary>
internal sealed record ShellSectionStackElement(Element?[] Pages) : Element
{
    public Element?[] Modals { get; init; } = [];
    public Action<int>? OnModalDismissed { get; init; }
    public bool Animated { get; init; } = true;
    public Microsoft.UI.Reactor.Navigation.NavigationHostBinding? HostBinding { get; init; }
    public Microsoft.UI.Reactor.Navigation.HostDeclaration? HostDeclaration { get; init; }
}
