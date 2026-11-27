using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using iOSPage = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page;
using MauiPage = Microsoft.Maui.Controls.Page;
using MauiCollectionView = Microsoft.Maui.Controls.CollectionView;
using MauiShell = Microsoft.Maui.Controls.Shell;
using MauiSwipeView = Microsoft.Maui.Controls.SwipeView;
using UIModalPresentationStyle = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.UIModalPresentationStyle;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>4c-3c's census claims (spec 070 §4.16): pages, navigation, Shell, dialogs, collections and swipe.</summary>
public static partial class MatrixCases
{
    // ── Pages and navigation (spec 070 §4.16.2, group M) ────────────────────

    private static PageElement APage() => Page("P", null);

    /// <summary>The page's back button behavior, signed by every field, read from the object current now.</summary>
    private static string BackButtonOf(object view) => MauiShell.GetBackButtonBehavior((MauiPage)view) is { } b
        ? $"{b.TextOverride}|{(b.IconOverride as FontImageSource)?.Glyph}|{b.IsVisible}|{b.IsEnabled}"
        : "none";

    private static IEnumerable<PropertyCase> PageCases()
    {
        yield return Case(APage, nameof(PageElement.Title), static (p, v) => p with { Title = v }, static () => "a", static () => "b",
            v => ((MauiPage)v).Title, MauiPage.TitleProperty) with { Base = static () => Page(null, null) };
        yield return Case(APage, nameof(PageElement.BackgroundColor), static (p, v) => p with { BackgroundColor = v }, static () => Colors.Red, static () => Colors.Blue,
            v => ((MauiPage)v).BackgroundColor, VisualElement.BackgroundColorProperty);
        yield return Case(APage, nameof(PageElement.Padding), static (p, v) => p with { Padding = v }, static () => new Thickness(4), static () => new Thickness(1, 2, 3, 4),
            v => ((MauiPage)v).Padding, MauiPage.PaddingProperty, expect: static v => v);
        yield return Case(APage, nameof(PageElement.Icon), static (p, v) => p with { Icon = v }, static () => Glyph("A"), static () => Glyph("B"),
            v => ((MauiPage)v).IconImageSource, MauiPage.IconImageSourceProperty, SameImage);
        yield return Case(APage, nameof(PageElement.HasNavigationBar), static (p, v) => p with { HasNavigationBar = v }, static () => false, static () => true,
            v => NavigationPage.GetHasNavigationBar((MauiPage)v), NavigationPage.HasNavigationBarProperty, expect: static v => v);
        yield return Case(APage, nameof(PageElement.HasBackButton), static (p, v) => p with { HasBackButton = v }, static () => false, static () => true,
            v => NavigationPage.GetHasBackButton((MauiPage)v), NavigationPage.HasBackButtonProperty, expect: static v => v);
        yield return Case(APage, nameof(PageElement.BackButtonTitle), static (p, v) => p with { BackButtonTitle = v }, static () => "back", static () => "return",
            v => NavigationPage.GetBackButtonTitle((MauiPage)v), NavigationPage.BackButtonTitleProperty);
        yield return Case(APage, nameof(PageElement.NavBarIsVisible), static (p, v) => p with { NavBarIsVisible = v }, static () => false, static () => true,
            v => MauiShell.GetNavBarIsVisible((MauiPage)v), MauiShell.NavBarIsVisibleProperty, expect: static v => v);
        yield return Case(APage, nameof(PageElement.TabBarIsVisible), static (p, v) => p with { TabBarIsVisible = v }, static () => false, static () => true,
            v => MauiShell.GetTabBarIsVisible((MauiPage)v), MauiShell.TabBarIsVisibleProperty, expect: static v => v);
        yield return Case(APage, nameof(PageElement.PresentationMode), static (p, v) => p with { PresentationMode = v }, static () => PresentationMode.Modal, static () => PresentationMode.NotAnimated,
            v => MauiShell.GetPresentationMode((MauiPage)v), MauiShell.PresentationModeProperty, expect: static v => v);
        yield return Case(APage, nameof(PageElement.ModalPresentationStyle), static (p, v) => p with { ModalPresentationStyle = v },
            static () => UIModalPresentationStyle.FormSheet, static () => UIModalPresentationStyle.PageSheet,
            v => ((MauiPage)v).GetValue(iOSPage.ModalPresentationStyleProperty), iOSPage.ModalPresentationStyleProperty, expect: static v => v);

        // A search handler is a reference: the equal render passes the same instance.
        var searchA = new SearchHandler { Placeholder = "a" };
        var searchB = new SearchHandler { Placeholder = "b" };
        yield return Case(APage(), nameof(PageElement.SearchHandler), p => p with { SearchHandler = searchA }, p => p with { SearchHandler = searchB }, null,
            v => MauiShell.GetSearchHandler((MauiPage)v), searchA, searchB, MauiShell.SearchHandlerProperty, ReferenceEquals);

        // The handler replaces the whole behavior object (spec 070 §4.16.2): the writes counted are the
        // replacements of the page's attached property, and each read is of the object current then.
        yield return Case(APage, nameof(PageElement.BackButtonBehavior), static (p, v) => p with { BackButtonBehavior = v },
            static () => new ShellBackButton(Text: "a"), static () => new ShellBackButton(IsEnabled: false),
            BackButtonOf, MauiShell.BackButtonBehaviorProperty, expect: static v => $"{v.Text}|{(v.Icon as FontImageSource)?.Glyph}|{v.IsVisible}|{v.IsEnabled}");

        // Its fields: each changes one field of the behavior the page has; removing one is a replacement
        // with MAUI's default for that field (the behavior stays, so clears: false).
        static PageElement WithBack(ShellBackButton back) => Page("P", null) with { BackButtonBehavior = back };
        yield return Case(() => WithBack(new()), nameof(ShellBackButton.Text), static (p, v) => p with { BackButtonBehavior = new(Text: v) }, static () => "a", static () => "b",
            v => MauiShell.GetBackButtonBehavior((MauiPage)v)?.TextOverride, MauiShell.BackButtonBehaviorProperty, clears: false)
            with { ElementType = typeof(ShellBackButton), Cleared = static () => null };
        yield return Case(() => WithBack(new()), nameof(ShellBackButton.Icon), static (p, v) => p with { BackButtonBehavior = new(Icon: v) }, static () => Glyph("A"), static () => Glyph("B"),
            v => MauiShell.GetBackButtonBehavior((MauiPage)v)?.IconOverride, MauiShell.BackButtonBehaviorProperty, SameImage, clears: false)
            with { ElementType = typeof(ShellBackButton), Cleared = static () => null };
        yield return Case(() => WithBack(new()), nameof(ShellBackButton.IsVisible), static (p, v) => p with { BackButtonBehavior = new(IsVisible: v) }, static () => false, static () => true,
            v => MauiShell.GetBackButtonBehavior((MauiPage)v)?.IsVisible, MauiShell.BackButtonBehaviorProperty, expect: static v => v, clears: false)
            with { ElementType = typeof(ShellBackButton), Cleared = static () => true };
        yield return Case(() => WithBack(new()), nameof(ShellBackButton.IsEnabled), static (p, v) => p with { BackButtonBehavior = new(IsEnabled: v) }, static () => false, static () => true,
            v => MauiShell.GetBackButtonBehavior((MauiPage)v)?.IsEnabled, MauiShell.BackButtonBehaviorProperty, expect: static v => v, clears: false)
            with { ElementType = typeof(ShellBackButton), Cleared = static () => true };

        static NavigationStackElement Stack() => NavigationStack([Page("root", null)]);
        yield return Case(Stack, nameof(NavigationStackElement.Title), static (s, v) => s with { Title = v }, static () => "a", static () => "b",
            v => ((MauiPage)v).Title, MauiPage.TitleProperty);
        yield return Case(Stack, nameof(NavigationStackElement.Icon), static (s, v) => s with { Icon = v }, static () => Glyph("A"), static () => Glyph("B"),
            v => ((MauiPage)v).IconImageSource, MauiPage.IconImageSourceProperty, SameImage);
        yield return Case(Stack, nameof(NavigationStackElement.BarBackgroundColor), static (s, v) => s with { BarBackgroundColor = v }, static () => Colors.Red, static () => Colors.Blue,
            v => ((NavigationPage)v).BarBackgroundColor, NavigationPage.BarBackgroundColorProperty);
        yield return Case(Stack, nameof(NavigationStackElement.BarTextColor), static (s, v) => s with { BarTextColor = v }, static () => Colors.Red, static () => Colors.Blue,
            v => ((NavigationPage)v).BarTextColor, NavigationPage.BarTextColorProperty);

        static TabsElement TwoTabs() => Tabs(0, null, Page("a", null), Page("b", null));
        yield return Case(TwoTabs, nameof(TabsElement.BarBackgroundColor), static (t, v) => t with { BarBackgroundColor = v }, static () => Colors.Red, static () => Colors.Blue,
            v => ((TabbedPage)v).BarBackgroundColor, TabbedPage.BarBackgroundColorProperty);
        yield return Case(TwoTabs, nameof(TabsElement.SelectedTabColor), static (t, v) => t with { SelectedTabColor = v }, static () => Colors.Red, static () => Colors.Blue,
            v => ((TabbedPage)v).SelectedTabColor, TabbedPage.SelectedTabColorProperty);
        yield return Case(TwoTabs, nameof(TabsElement.UnselectedTabColor), static (t, v) => t with { UnselectedTabColor = v }, static () => Colors.Red, static () => Colors.Blue,
            v => ((TabbedPage)v).UnselectedTabColor, TabbedPage.UnselectedTabColorProperty);
    }

    // ── Shell (spec 070 §4.16.2, group M) ──────────────────────────────────

    private static ShellElement AShell(Element? first = null, Element? second = null) =>
        Shell(null, null, first ?? FlyoutItem(null, ShellTab("a", null, Page("A", null))), second);

    private static IEnumerable<PropertyCase> ShellCases()
    {
        static ShellElement Root() => AShell();
        // Without the app's title, MAUI's Shell mirrors the shown page's ("A"), as a local value.
        yield return Case(Root, nameof(ShellElement.Title), static (s, v) => s with { Title = v }, static () => "a", static () => "b",
            v => ((MauiPage)v).Title, MauiPage.TitleProperty, clears: false) with { Cleared = static () => "A" };
        // Through the presentation policy: no flyout presentation is declared, so nothing else is written.
        yield return Case(Root, nameof(ShellElement.FlyoutBehavior), static (s, v) => s with { FlyoutBehavior = v }, static () => FlyoutBehavior.Locked, static () => FlyoutBehavior.Disabled,
            v => ((MauiShell)v).FlyoutBehavior, MauiShell.FlyoutBehaviorProperty, expect: static v => v);

        (string Name, Func<ShellElement, Color, ShellElement> Set, BindableProperty Native)[] colors =
        [
            (nameof(ShellElement.BackgroundColor), static (s, c) => s with { BackgroundColor = c }, MauiShell.BackgroundColorProperty),
            (nameof(ShellElement.ForegroundColor), static (s, c) => s with { ForegroundColor = c }, MauiShell.ForegroundColorProperty),
            (nameof(ShellElement.TitleColor), static (s, c) => s with { TitleColor = c }, MauiShell.TitleColorProperty),
            (nameof(ShellElement.DisabledColor), static (s, c) => s with { DisabledColor = c }, MauiShell.DisabledColorProperty),
            (nameof(ShellElement.UnselectedColor), static (s, c) => s with { UnselectedColor = c }, MauiShell.UnselectedColorProperty),
            (nameof(ShellElement.FlyoutBackgroundColor), static (s, c) => s with { FlyoutBackgroundColor = c }, MauiShell.FlyoutBackgroundColorProperty),
            (nameof(ShellElement.TabBarBackgroundColor), static (s, c) => s with { TabBarBackgroundColor = c }, MauiShell.TabBarBackgroundColorProperty),
            (nameof(ShellElement.TabBarForegroundColor), static (s, c) => s with { TabBarForegroundColor = c }, MauiShell.TabBarForegroundColorProperty),
            (nameof(ShellElement.TabBarTitleColor), static (s, c) => s with { TabBarTitleColor = c }, MauiShell.TabBarTitleColorProperty),
            (nameof(ShellElement.TabBarUnselectedColor), static (s, c) => s with { TabBarUnselectedColor = c }, MauiShell.TabBarUnselectedColorProperty),
            (nameof(ShellElement.TabBarDisabledColor), static (s, c) => s with { TabBarDisabledColor = c }, MauiShell.TabBarDisabledColorProperty),
        ];
        foreach (var (name, set, native) in colors)
            yield return Case(Root, name, set, static () => Colors.Red, static () => Colors.Blue, v => ((BindableObject)v).GetValue(native), native);

        // Shell items, read on the native item the case's Shell holds (a fresh item for the default).
        static BindableObject FirstItem(object view) => view as MauiShell is { } s ? s.Items[0] : (BindableObject)view;
        static BindableObject FirstTab(object view) => view as MauiShell is { } s ? s.Items[0].Items[0] : (BindableObject)view;
        static BindableObject FirstTopTab(object view) => view as MauiShell is { } s ? s.Items[0].Items[0].Items[0] : (BindableObject)view;
        static BindableObject MenuItemOf(object view) => view as MauiShell is { } s ? ShellNatives.GetMenuItem(s.Items[1])! : (BindableObject)view;

        static ShellElement WithItem(FlyoutItemElement item) => AShell(item);
        static FlyoutItemElement Item() => FlyoutItem(null, ShellTab("a", null, Page("A", null)));
        yield return Case(() => WithItem(Item()), nameof(FlyoutItemElement.Title), static (s, v) => WithItem(Item() with { Title = v }), static () => "a", static () => "b",
            v => ((BaseShellItem)FirstItem(v)).Title, BaseShellItem.TitleProperty) with { ElementType = typeof(FlyoutItemElement), Target = FirstItem, Fresh = static _ => new FlyoutItem() };
        yield return Case(() => WithItem(Item()), nameof(FlyoutItemElement.Icon), static (s, v) => WithItem(Item() with { Icon = v }), static () => Glyph("A"), static () => Glyph("B"),
            v => ((BaseShellItem)FirstItem(v)).Icon, BaseShellItem.IconProperty, SameImage) with { ElementType = typeof(FlyoutItemElement), Target = FirstItem, Fresh = static _ => new FlyoutItem() };
        yield return Case(() => WithItem(Item()), nameof(FlyoutItemElement.DisplayOptions), static (s, v) => WithItem(Item() with { DisplayOptions = v }),
            static () => FlyoutDisplayOptions.AsMultipleItems, static () => FlyoutDisplayOptions.AsSingleItem,
            v => ((ShellGroupItem)FirstItem(v)).FlyoutDisplayOptions, ShellGroupItem.FlyoutDisplayOptionsProperty, expect: static v => v)
            with { ElementType = typeof(FlyoutItemElement), Target = FirstItem, Fresh = static _ => new FlyoutItem() };

        static ShellElement WithTab(ShellTabElement tab) => AShell(FlyoutItem(null, tab));
        yield return Case(() => WithTab(ShellTab("a", null, Page("A", null))), nameof(ShellTabElement.Title),
            static (s, v) => WithTab(ShellTab("a", v, Page("A", null))), static () => "a", static () => "b",
            v => ((BaseShellItem)FirstTab(v)).Title, BaseShellItem.TitleProperty) with { ElementType = typeof(ShellTabElement), Target = FirstTab, Fresh = static _ => new Tab() };
        yield return Case(() => WithTab(ShellTab("a", null, Page("A", null))), nameof(ShellTabElement.Icon),
            static (s, v) => WithTab(ShellTab("a", null, Page("A", null), v)), static () => Glyph("A"), static () => Glyph("B"),
            v => ((BaseShellItem)FirstTab(v)).Icon, BaseShellItem.IconProperty, SameImage) with { ElementType = typeof(ShellTabElement), Target = FirstTab, Fresh = static _ => new Tab() };

        static ShellElement WithTopTab(ShellContentElement content) =>
            AShell(FlyoutItem(null, ShellTab("t", "T", content, ShellContent("y", "Y", Page("Y", null)))));
        yield return Case(() => WithTopTab(ShellContent("x", null, Page("X", null))), nameof(ShellContentElement.Title),
            static (s, v) => WithTopTab(ShellContent("x", v, Page("X", null))), static () => "a", static () => "b",
            v => ((BaseShellItem)FirstTopTab(v)).Title, BaseShellItem.TitleProperty) with { ElementType = typeof(ShellContentElement), Target = FirstTopTab, Fresh = static _ => new ShellContent() };
        yield return Case(() => WithTopTab(ShellContent("x", null, Page("X", null))), nameof(ShellContentElement.Icon),
            static (s, v) => WithTopTab(ShellContent("x", null, Page("X", null), v)), static () => Glyph("A"), static () => Glyph("B"),
            v => ((BaseShellItem)FirstTopTab(v)).Icon, BaseShellItem.IconProperty, SameImage) with { ElementType = typeof(ShellContentElement), Target = FirstTopTab, Fresh = static _ => new ShellContent() };

        static ShellElement WithMenu(ShellMenuItemElement menu) => AShell(null, menu);
        yield return Case(() => WithMenu(ShellMenuItem("m", null)), nameof(ShellMenuItemElement.Icon),
            static (s, v) => WithMenu(ShellMenuItem("m", null, v)), static () => Glyph("A"), static () => Glyph("B"),
            v => ((MenuItem)MenuItemOf(v)).IconImageSource, MenuItem.IconImageSourceProperty, SameImage) with { ElementType = typeof(ShellMenuItemElement), Target = MenuItemOf, Fresh = static _ => new MenuItem() };
        // Text is required: its "clear" is the base's own text.
        yield return Case(() => WithMenu(ShellMenuItem("m", null)), nameof(ShellMenuItemElement.Text),
            static (s, v) => WithMenu(ShellMenuItem(v, null)), static () => "a", static () => "b",
            v => ((MenuItem)MenuItemOf(v)).Text, MenuItem.TextProperty, clears: false)
            with { ElementType = typeof(ShellMenuItemElement), Target = MenuItemOf, Fresh = static _ => new MenuItem(), Cleared = static () => "m" };
    }

    // ── Swipe and collections (spec 070 §4.16.2, group M) ───────────────────

    private static IEnumerable<PropertyCase> SwipeAndCollectionCases()
    {
        static SwipeViewElement Swipe() => SwipeView(Label("row")) with
        {
            LeftItems = [new("l")], RightItems = [new("r")], TopItems = [new("t")], BottomItems = [new("b")],
        };
        yield return Case(Swipe, nameof(SwipeViewElement.Threshold), static (s, v) => s with { Threshold = v }, static () => 40d, static () => 80d,
            v => ((MauiSwipeView)v).Threshold, MauiSwipeView.ThresholdProperty, expect: static v => v);
        // A mode rebuilds its side: the write is the side's items, never cleared (the base is Reveal).
        (string Name, Func<SwipeViewElement, SwipeMode, SwipeViewElement> Set, BindableProperty Native, Func<MauiSwipeView, SwipeItems> Items)[] modes =
        [
            (nameof(SwipeViewElement.LeftItemsMode), static (s, m) => s with { LeftItemsMode = m }, MauiSwipeView.LeftItemsProperty, static v => v.LeftItems),
            (nameof(SwipeViewElement.RightItemsMode), static (s, m) => s with { RightItemsMode = m }, MauiSwipeView.RightItemsProperty, static v => v.RightItems),
            (nameof(SwipeViewElement.TopItemsMode), static (s, m) => s with { TopItemsMode = m }, MauiSwipeView.TopItemsProperty, static v => v.TopItems),
            (nameof(SwipeViewElement.BottomItemsMode), static (s, m) => s with { BottomItemsMode = m }, MauiSwipeView.BottomItemsProperty, static v => v.BottomItems),
        ];
        foreach (var (name, set, native, items) in modes)
            yield return Case(Swipe, name, set, static () => SwipeMode.Execute, static () => SwipeMode.Reveal,
                v => items((MauiSwipeView)v).Mode, native, clears: false) with { Cleared = static () => SwipeMode.Reveal };

        static CollectionViewElement List() => CollectionView(new[] { "a", "b" }, k => k, k => Label(k));
        static string LayoutOf(object view) => ((MauiCollectionView)view).ItemsLayout switch
        {
            GridItemsLayout g => $"grid {g.Orientation} {g.Span} {g.VerticalItemSpacing}",
            LinearItemsLayout l => $"list {l.Orientation} {l.ItemSpacing}",
            var other => other?.GetType().Name ?? "none",
        };
        // The layout values replace ItemsLayout (one write each) and are non-nullable: their clear is
        // the element's default (spec 070 §4.15 deviation 2).
        yield return Case(List, nameof(CollectionViewElement.Arrangement), static (c, v) => c with { Arrangement = v },
            static () => ItemsArrangement.HorizontalList, static () => ItemsArrangement.VerticalGrid, LayoutOf, StructuredItemsView.ItemsLayoutProperty,
            expect: static v => v == ItemsArrangement.HorizontalList ? "list Horizontal 0" : "grid Vertical 1 0", clears: false)
            with { Cleared = static () => "list Vertical 0" };
        yield return Case(() => List() with { Arrangement = ItemsArrangement.VerticalGrid }, nameof(CollectionViewElement.Span), static (c, v) => c with { Span = v },
            static () => 2, static () => 3, LayoutOf, StructuredItemsView.ItemsLayoutProperty, expect: static v => $"grid Vertical {v} 0", clears: false)
            with { Cleared = static () => "grid Vertical 1 0" };
        yield return Case(List, nameof(CollectionViewElement.ItemSpacing), static (c, v) => c with { ItemSpacing = v },
            static () => 4d, static () => 8d, LayoutOf, StructuredItemsView.ItemsLayoutProperty, expect: static v => $"list Vertical {v}", clears: false)
            with { Cleared = static () => "list Vertical 0" };
        yield return Case(List, nameof(CollectionViewElement.ItemSizingStrategy), static (c, v) => c with { ItemSizingStrategy = v },
            static () => ItemSizingStrategy.MeasureFirstItem, static () => ItemSizingStrategy.MeasureAllItems,
            v => ((MauiCollectionView)v).ItemSizingStrategy, MauiCollectionView.ItemSizingStrategyProperty, expect: static v => v);
        yield return Case(List, nameof(CollectionViewElement.RemainingItemsThreshold), static (c, v) => c with { RemainingItemsThreshold = v },
            static () => 5, static () => 7, v => ((MauiCollectionView)v).RemainingItemsThreshold, ItemsView.RemainingItemsThresholdProperty, clears: false)
            with { Cleared = static () => -1 };
        yield return Case(List, nameof(CollectionViewElement.SelectionMode), static (c, v) => c with { SelectionMode = v },
            static () => SelectionMode.Single, static () => SelectionMode.Multiple,
            v => ((MauiCollectionView)v).SelectionMode, SelectableItemsView.SelectionModeProperty, clears: false)
            with { Cleared = static () => SelectionMode.None };
    }

    // ── Events (group E) and controlled values (group C) ────────────────────

    private static IEnumerable<EventCase> PageEvents()
    {
        yield return new()
        {
            ElementType = typeof(ShellMenuItemElement),
            Property = nameof(ShellMenuItemElement.OnClick),
            Create = report => AShell(null, ShellMenuItem("m", () => report("clicked"))),
            Trigger = native => ((IMenuItemController)ShellNatives.GetMenuItem(((MauiShell)native).Items[1])!).Activate(),
            Payload = "clicked",
        };
        yield return new()
        {
            ElementType = typeof(SwipeViewElement),
            Property = nameof(SwipeViewElement.OnSwipeStarted),
            Create = report => SwipeView(Label("row")) with { OnSwipeStarted = d => report($"{d}") },
            Trigger = native => ((ISwipeViewController)native).SendSwipeStarted(new SwipeStartedEventArgs(SwipeDirection.Left)),
            Payload = "Left",
        };
        yield return new()
        {
            ElementType = typeof(SwipeViewElement),
            Property = nameof(SwipeViewElement.OnSwipeChanging),
            Create = report => SwipeView(Label("row")) with { OnSwipeChanging = (d, offset) => report($"{d} {offset}") },
            Trigger = native => ((ISwipeViewController)native).SendSwipeChanging(new SwipeChangingEventArgs(SwipeDirection.Right, 12)),
            Payload = "Right 12",
        };
        yield return new()
        {
            ElementType = typeof(SwipeViewElement),
            Property = nameof(SwipeViewElement.OnSwipeEnded),
            Create = report => SwipeView(Label("row")) with { OnSwipeEnded = (d, open) => report($"{d} {open}") },
            Trigger = native => ((ISwipeViewController)native).SendSwipeEnded(new SwipeEndedEventArgs(SwipeDirection.Up, true)),
            Payload = "Up True",
        };
        yield return new()
        {
            ElementType = typeof(CollectionViewElement),
            Property = nameof(CollectionViewElement.OnRemainingItemsThresholdReached),
            Create = report => CollectionView(new[] { "a" }, k => k, k => Label(k)) with
            {
                RemainingItemsThreshold = 0,
                OnRemainingItemsThresholdReached = () => report("reached"),
            },
            Trigger = native => ((MauiCollectionView)native).SendRemainingItemsThresholdReached(),
            Payload = "reached",
        };
    }

    private static IEnumerable<ControlledCase> PageControlled()
    {
        yield return ControlledValue(typeof(TabsElement), nameof(TabsElement.SelectedIndex), nameof(TabsElement.OnSelectedIndexChanged),
            static (int v, Action<string> r) => Tabs(v, x => r($"{x}"), Page("a", null), Page("b", null), Page("c", null)),
            static v => ((TabbedPage)v).Children.IndexOf(((TabbedPage)v).CurrentPage),
            static (v, i) => ((TabbedPage)v).CurrentPage = ((TabbedPage)v).Children[i], 0, 1, 2, nameof(TabbedPage.CurrentPage));
        // Under the Flyout behavior (a flyout item, the root's unset), where the presentation is the app's.
        yield return ControlledValue(typeof(ShellElement), nameof(ShellElement.FlyoutIsPresented), nameof(ShellElement.OnFlyoutIsPresentedChanged),
            static (bool v, Action<string> r) => AShell().FlyoutIsPresented(v, x => r($"{x}")),
            static v => ((MauiShell)v).FlyoutIsPresented, static (v, x) => ((MauiShell)v).FlyoutIsPresented = x, true, false, true, nameof(MauiShell.FlyoutIsPresented));
    }
}
