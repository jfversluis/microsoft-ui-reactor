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

    // ── Covered-by claims (groups A, E, K, N, S, D) ─────────────────────────

    private static IEnumerable<CoveredClaim> PageCovered()
    {
        const string Spec = "(spec 070 §4.17)";
        static CoveredClaim C(Type type, string property, Type test, string method, string facets, string mutants) => new(type, property, test, method, facets, mutants);

        // Pages (A, E, S)
        yield return C(typeof(PageElement), nameof(PageElement.AutomationId), typeof(PageAuditTests), nameof(PageAuditTests.APagesAutomationId_IsSetAtMount_AndAChange_WritesNothing_ThrowsNothing_AndWarnsOncePerPage),
            "set at mount; a change writes nothing and throws nothing; warns once per page (an equal render and later changes don't); another page warns; both registries",
            $"the per-page dedup dropped (A1) and the warning dropped (A2): red {Spec}");
        yield return C(typeof(PageElement), nameof(PageElement.OnAppearing), typeof(PageAuditTests), nameof(PageAuditTests.APagesLifecycle_ReachesTheCurrentCallbacks_AndNothingAfterUnmount),
            "a window-hosted page; each trigger re-armed; the page's own event witnesses every trigger, after unmount too; the current callback; nothing after unmount; both registries",
            $"the callback read from the mount-time element (L1): red {Spec}");
        yield return C(typeof(PageElement), nameof(PageElement.OnDisappearing), typeof(PageAuditTests), nameof(PageAuditTests.APagesLifecycle_ReachesTheCurrentCallbacks_AndNothingAfterUnmount),
            "as OnAppearing, for Disappearing (needs an appeared page: each cycle appears first)",
            $"the callback read from the mount-time element (L2): red {Spec}");
        yield return C(typeof(PageElement), nameof(PageElement.Content), typeof(StructureAuditTests), nameof(StructureAuditTests.APagesContentAndTitleView_AreSetUpdatedInPlaceReplacedAndCleared),
            "set; updated in place (the native and the state kept); replaced (the old tree cleaned up); cleared (null); both registries",
            $"content not cleared on null (Sa): red {Spec}");
        yield return C(typeof(PageElement), nameof(PageElement.TitleView), typeof(StructureAuditTests), nameof(StructureAuditTests.APagesContentAndTitleView_AreSetUpdatedInPlaceReplacedAndCleared),
            "set (Shell's attached title view); updated in place (state kept); cleared (no local value, cleaned up); both registries",
            $"title view not cleared (Sb): red {Spec}");
        yield return C(typeof(TabsElement), nameof(TabsElement.Pages), typeof(StructureAuditTests), nameof(StructureAuditTests.TabsPages_AreAddedRemovedAndReordered_KeepingTheirPages),
            "keyed pages added, removed and reordered, the kept pages' natives kept; both registries",
            $"the list reorder skipped (Sc): red {Spec}");

        // Shell structure (S)
        yield return C(typeof(ShellElement), nameof(ShellElement.Items), typeof(StructureAuditTests), nameof(StructureAuditTests.TheShellsItems_AndTheirTabs_AreAddedRemovedAndReordered_KeepingTheirObjects),
            "root items (flyout items and a tab bar) reordered, removed and added, keeping the kept items' natives and the current item; both registries. Also ShellTests.Build_CreatesItemsTabsAndTopTabs_WithTheirKeys",
            $"the Shell's staging skipping the reorder (Sh): red {Spec}");
        yield return C(typeof(TabBarElement), nameof(TabBarElement.Tabs), typeof(StructureAuditTests), nameof(StructureAuditTests.TheShellsItems_AndTheirTabs_AreAddedRemovedAndReordered_KeepingTheirObjects),
            "a tab bar's tabs reordered keeping their sections; both registries. Also ShellTests.Update_AddsRemovesAndReordersTabsAndTopTabs_KeepingTheirObjects (add, remove, reorder) and ConditionalTabs_AndTopTabs_SkipNulls",
            $"the staging skipping the reorder (Sh): red {Spec}");
        yield return C(typeof(FlyoutItemElement), nameof(FlyoutItemElement.Tabs), typeof(StructureAuditTests), nameof(StructureAuditTests.TheShellsItems_AndTheirTabs_AreAddedRemovedAndReordered_KeepingTheirObjects),
            "a flyout item's tabs reordered keeping their sections, the item kept when moved; both registries",
            $"the staging skipping the reorder (Sh): red {Spec}");
        yield return C(typeof(ShellElement), nameof(ShellElement.FlyoutHeader), typeof(StructureAuditTests), nameof(StructureAuditTests.TheShellsFlyoutHeaderFooterAndContent_AreSetUpdatedInPlaceAndCleared_InRetainedHosts),
            "set; updated in place (state kept); cleared: the retained host emptied and the tree cleaned up; set again into the same host (a fresh tree); both registries. Also ShellTests.Flyout_IsControlled_* (updated in place)",
            $"the host not emptied (Sd) and the host replaced (Se): red {Spec}");
        yield return C(typeof(ShellElement), nameof(ShellElement.FlyoutFooter), typeof(StructureAuditTests), nameof(StructureAuditTests.TheShellsFlyoutHeaderFooterAndContent_AreSetUpdatedInPlaceAndCleared_InRetainedHosts),
            "set; updated in place (state kept); cleared: the retained host emptied, the tree cleaned up; both registries",
            $"the host not emptied (Sf): red {Spec}");
        yield return C(typeof(ShellElement), nameof(ShellElement.FlyoutContent), typeof(StructureAuditTests), nameof(StructureAuditTests.TheShellsFlyoutHeaderFooterAndContent_AreSetUpdatedInPlaceAndCleared_InRetainedHosts),
            "set; updated in place (state kept); cleared (no local value, so the flyout lists its items again; cleaned up); both registries",
            $"the content not cleared (Sg): red {Spec}");
        yield return C(typeof(ShellTabElement), nameof(ShellTabElement.Content), typeof(StructureAuditTests), nameof(StructureAuditTests.AShellTabsContent_AndATopTabsPage_AreReplaced_AndTopTabsReordered_KeepingTheirObjects),
            "a single page replaced by another (keyed), in the tab's own content, which stays; both registries. Also ShellTests.ATabsContentChangingFromADeepHost_PopsTheSectionToItsRoot (host, page and top tabs)",
            $"the page not replaced (Si): red {Spec}");
        yield return C(typeof(ShellTabElement), nameof(ShellTabElement.Contents), typeof(StructureAuditTests), nameof(StructureAuditTests.AShellTabsContent_AndATopTabsPage_AreReplaced_AndTopTabsReordered_KeepingTheirObjects),
            "from one page to top tabs; top tabs reordered keeping their contents; both registries. Also ShellTests.Update_AddsRemovesAndReordersTabsAndTopTabs_KeepingTheirObjects (a top tab removed)",
            $"the staging skipping the reorder (Sh): red {Spec}");
        yield return C(typeof(ShellContentElement), nameof(ShellContentElement.Page), typeof(StructureAuditTests), nameof(StructureAuditTests.AShellTabsContent_AndATopTabsPage_AreReplaced_AndTopTabsReordered_KeepingTheirObjects),
            "a top tab's page replaced, in the same content; both registries",
            $"the page not replaced (Sj): red {Spec}");

        // Selections (K)
        yield return C(typeof(ShellElement), nameof(ShellElement.Selected), typeof(ShellTests), nameof(ShellTests.TheSelectionCallback_IsTheCurrentOne_AndNothingIsReportedAfterUnmount),
            "refused, restored to the declaration; both registries. Also ShellTests.SelectionFromCode_ShowsTheTab_AtEachLevel_WithoutReportingIt (written at each level, unreported), RefusedSwitch_* (restored to the latest), UnknownKey_*, RemovingTheSelectedTab_*",
            $"the report from the mount-time element (K1): red {Spec}");
        yield return C(typeof(ShellElement), nameof(ShellElement.OnSelectedChanged), typeof(ShellTests), nameof(ShellTests.TheSelectionCallback_IsTheCurrentOne_AndNothingIsReportedAfterUnmount),
            "the current callback; nothing after unmount (the switch witnessed natively); both registries. Also UserSwitch_IsReportedOnce_FromNavigated_AndKeptWhenAccepted",
            $"the report from the mount-time element (K1): red {Spec}");
        yield return C(typeof(CollectionViewElement), nameof(CollectionViewElement.SelectedKey), typeof(CollectionSelectionTests), nameof(CollectionSelectionTests.TheSelection_FollowsTheMode_ReachesTheCurrentCallback_AndNothingAfterUnmount),
            "with both keys declared, Single writes SelectedKey; a mode change back to Single writes it, unreported; both registries. Also ControlTests.Selection_IsControlledByKey and the verification tests (spec 070 §3.4)",
            $"the echo check dropped (K3): red {Spec}");
        yield return C(typeof(CollectionViewElement), nameof(CollectionViewElement.SelectedKeys), typeof(CollectionSelectionTests), nameof(CollectionSelectionTests.TheSelection_FollowsTheMode_ReachesTheCurrentCallback_AndNothingAfterUnmount),
            "Multiple writes SelectedKeys (a mode change with a selection, unreported); the user's change reported and, refused, restored; both registries. Also MultipleSelection_DeclaredInAnotherOrder_WritesNothing",
            $"the echo check dropped (K3): red {Spec}");
        yield return C(typeof(CollectionViewElement), nameof(CollectionViewElement.OnSelectionChanged), typeof(CollectionSelectionTests), nameof(CollectionSelectionTests.TheSelection_FollowsTheMode_ReachesTheCurrentCallback_AndNothingAfterUnmount),
            "the current callback; nothing after unmount (the native selection changed: the positive control); both registries. Also AReport_ReachesTheAppBeforeAQueuedRenderApplies",
            $"the report from the mount-time element (K2): red {Spec}");

        // Navigation stacks (N)
        yield return C(typeof(NavigationStackElement), nameof(NavigationStackElement.Pages), typeof(NavigationStackAuditTests), nameof(NavigationStackAuditTests.Pages_DeclaredChangesAreUnreported_AUsersPopIsReportedWithItsDepth_RefusedOrAccepted_ToTheCurrentCallback),
            "declared pushes and pops written, unreported; a user pop refused (pushed back) and accepted (kept); both registries. Also PageTests.NavigationStack_FollowsTheDeclaredStack and APopOfAPageTheAppNoLongerDeclares_IsNotReported",
            $"no restore after a refused pop (N4): red; the user-pop check dropped (N5): red in APopOfAPageTheAppNoLongerDeclares_IsNotReported {Spec}");
        yield return C(typeof(NavigationStackElement), nameof(NavigationStackElement.Modals), typeof(NavigationStackAuditTests), nameof(NavigationStackAuditTests.Modals_DeclaredChangesAreUnreported_AUsersDismissalIsReported_RefusedOrAccepted_ToTheCurrentCallback),
            "a window-hosted stack: declared modals pushed and removed, unreported; a dismissal refused (presented again) and accepted; dismissed at unmount; both registries",
            $"the dismissal report from the mount-time element (N2): red {Spec}");
        yield return C(typeof(NavigationStackElement), nameof(NavigationStackElement.Animated), typeof(NavigationStackAuditTests), nameof(NavigationStackAuditTests.Pages_DeclaredChangesAreUnreported_AUsersPopIsReportedWithItsDepth_RefusedOrAccepted_ToTheCurrentCallback),
            "false and true each reach the navigator for that change; both registries. Also NavigationHostTests.AHostConfiguredNotAnimated_UsesThatForEveryChange_UnlessNavigateSaysOtherwise",
            $"the declaration ignored (N3): red {Spec}");
        yield return C(typeof(NavigationStackElement), nameof(NavigationStackElement.OnPopped), typeof(NavigationStackAuditTests), nameof(NavigationStackAuditTests.Pages_DeclaredChangesAreUnreported_AUsersPopIsReportedWithItsDepth_RefusedOrAccepted_ToTheCurrentCallback),
            "reported once with the depth; the current callback; a late platform pop after unmount reports nothing; both registries",
            $"the report from the mount-time element (N1): red {Spec}");
        yield return C(typeof(NavigationStackElement), nameof(NavigationStackElement.OnModalDismissed), typeof(NavigationStackAuditTests), nameof(NavigationStackAuditTests.Modals_DeclaredChangesAreUnreported_AUsersDismissalIsReported_RefusedOrAccepted_ToTheCurrentCallback),
            "reported once with the modals left; the current callback; nothing for the dismissal at unmount (witnessed by ModalPopped); both registries",
            $"the report from the mount-time element (N2): red {Spec}");

        // Collections (S)
        var items = nameof(StructureAuditTests.ACollectionsItems_AreKeyed_TheirTemplateRerendersBoundCells_AndARecycledCellRemountsUnlessTreesAreReused);
        yield return C(typeof(CollectionViewElement), nameof(CollectionViewElement.Items), typeof(StructureAuditTests), items,
            "added, removed and reordered by key, the kept and the moved items keeping their boxes and a bound cell its tree; both registries. Also ControlTests.Items_AreDiffedByKey_KeepingUnchangedEntries (a changed value replaces its box)",
            $"a moved item's box replaced (Sn): red {Spec}");
        yield return C(typeof(CollectionViewElement), nameof(CollectionViewElement.ItemKey), typeof(StructureAuditTests), items,
            "the identity of the boxes and cell trees (as Items); both registries. Also ControlTests.Items_AreDiffedByKey_KeepingUnchangedEntries",
            $"a moved item's box replaced (Sn): red {Spec}");
        yield return C(typeof(CollectionViewElement), nameof(CollectionViewElement.ItemTemplate), typeof(StructureAuditTests), items,
            "a changed template re-renders the bound cells; both registries. Also ControlTests.Cells_RenderTheTemplate_InheritContext_* and Cells_Rerender_WhenTheTemplateCapturesChangedParentState",
            $"the template kept from mount (X1): red {Spec}");
        yield return C(typeof(CollectionViewElement), nameof(CollectionViewElement.ReuseItemTrees), typeof(StructureAuditTests), items,
            "off: a recycled cell remounts; on: it keeps its tree and state; both registries. Also ControlTests.ARecycledCell_ResetsItemState_UnlessTreesAreReused",
            $"trees never reused (So): red {Spec}");
        var groups = nameof(StructureAuditTests.ACollectionsGroups_AreKeyed_AndTheirTemplatesAreReplacedInPlace_AndRemoved);
        yield return C(typeof(CollectionViewElement), nameof(CollectionViewElement.Groups), typeof(StructureAuditTests), groups,
            "groups reordered by key keeping their boxes; both registries. Also CollectionViewFeatureTests.Groups_AreKeyed_AndTheirCellsRenderTheTemplates, GroupedAndFlat_Remount",
            $"groups not reordered (X4): red {Spec}");
        yield return C(typeof(CollectionViewElement), nameof(CollectionViewElement.GroupHeaderTemplate), typeof(StructureAuditTests), groups,
            "set; replaced by another, re-rendering the bound header cells through the same native template; removed (null); both registries",
            $"not replaced (Sl) and not removed (Sm): red {Spec}");
        yield return C(typeof(CollectionViewElement), nameof(CollectionViewElement.GroupFooterTemplate), typeof(StructureAuditTests), groups,
            "set; replaced by another through the same native template; removed (null); both registries",
            $"not replaced (X2) and not removed (X3): red {Spec}");
        yield return C(typeof(CollectionViewElement), nameof(CollectionViewElement.Header), typeof(CollectionViewFeatureTests), nameof(CollectionViewFeatureTests.HeaderFooterAndEmptyView_AreSetReplacedAndCleared),
            "set; updated in place (state kept); cleared (null, cleaned up); both registries (a direct view hosted)",
            $"the slot not unmounted (X8): red {Spec}");
        yield return C(typeof(CollectionViewElement), nameof(CollectionViewElement.Footer), typeof(StructureAuditTests), nameof(StructureAuditTests.ACollectionsFooterAndEmptyView_KeepTheirStateOnUpdate_AndCleanUpWhenRemoved),
            "set; updated in place (the native and the state kept); removed (null, cleaned up); both registries",
            $"the slot not unmounted (Sk): red {Spec}");
        yield return C(typeof(CollectionViewElement), nameof(CollectionViewElement.EmptyView), typeof(StructureAuditTests), nameof(StructureAuditTests.ACollectionsFooterAndEmptyView_KeepTheirStateOnUpdate_AndCleanUpWhenRemoved),
            "set; updated in place (the native and the state kept); removed (null, cleaned up); both registries",
            $"the slot not unmounted (Sk): red {Spec}");
        yield return C(typeof(CollectionViewElement), nameof(CollectionViewElement.ShowsSelectionHighlight), typeof(ItemStateTests), nameof(ItemStateTests.TheStateOverload_TurnsOffThePlatformHighlight_OnEveryCell),
            "false: every cell has the transparent Selected state, early and late; true: none; a toggle remounts (iOS clears its highlight once)",
            $"a toggle not remounting (X9): red {Spec}");
        yield return C(typeof(ItemGroup), nameof(ItemGroup.Key), typeof(StructureAuditTests), groups,
            "a group's box kept by key across a reorder; both registries. Also Groups_AreKeyed_* (duplicate keys throw)",
            $"boxes never kept (X5): red {Spec}");
        yield return C(typeof(ItemGroup), nameof(ItemGroup.Group), typeof(StructureAuditTests), groups,
            "a changed group value re-renders its header; both registries",
            $"the value not updated (X6): red {Spec}");
        yield return C(typeof(ItemGroup), nameof(ItemGroup.Items), typeof(CollectionViewFeatureTests), nameof(CollectionViewFeatureTests.Groups_AreKeyed_AndTheirCellsRenderTheTemplates),
            "a group's items diffed by key (an item kept across a group move of its group); also ACrossGroupMove_KeepsStateOnlyOnADirectRebindOfTheSameCell",
            $"group items not synced (X7): red {Spec}");

        // Swipe (S)
        var swipe = nameof(StructureAuditTests.ASwipeViewsContentAndSides_AreSetUpdatedAndCleared_AndItsItemsReachTheCurrentCallback);
        yield return C(typeof(SwipeViewElement), nameof(SwipeViewElement.Content), typeof(StructureAuditTests), swipe,
            "set; updated in place (state kept); cleared (null, cleaned up); both registries",
            $"the content not cleared (Sp): red {Spec}");
        var sides = nameof(SwipeViewTests.FourSides_TheirModes_AndTheThreshold);
        yield return C(typeof(SwipeViewElement), nameof(SwipeViewElement.LeftItems), typeof(SwipeViewTests), sides,
            "each side's items written to its own side (with the others'); cleared in StructureAuditTests (a side emptied, both registries)",
            $"the left side reading another side's items (X10): red {Spec}");
        yield return C(typeof(SwipeViewElement), nameof(SwipeViewElement.RightItems), typeof(SwipeViewTests), sides,
            "two items in order on the right; a side emptied in StructureAuditTests (both registries)",
            $"the right side reading another side's items (X11): red {Spec}");
        yield return C(typeof(SwipeViewElement), nameof(SwipeViewElement.TopItems), typeof(SwipeViewTests), sides,
            "the top side's items", $"the top side reading another side's items (X12): red {Spec}");
        yield return C(typeof(SwipeViewElement), nameof(SwipeViewElement.BottomItems), typeof(SwipeViewTests), sides,
            "the bottom side's items", $"the bottom side reading another side's items (X13): red {Spec}");
        yield return C(typeof(SwipeItemData), nameof(SwipeItemData.Text), typeof(SwipeViewTests), nameof(SwipeViewTests.AChangedTextColorOrIcon_RebuildsOnlyThatSide),
            "an equal render keeps the side; a changed text rebuilds only that side",
            $"a text change not rebuilding (X14): red {Spec}");
        yield return C(typeof(SwipeItemData), nameof(SwipeItemData.Icon), typeof(StructureAuditTests), swipe,
            "set; removed: a rebuilt item without an icon; both registries. Also AModeOrIconChange_RebuildsTheSide_ButAContentItemsColorDoesNot",
            $"a removal not rebuilding (Sq): red {Spec}");
        yield return C(typeof(SwipeItemData), nameof(SwipeItemData.BackgroundColor), typeof(StructureAuditTests), swipe,
            "set; removed: a rebuilt item at MAUI's default (no local value); both registries. Also AChangedTextColorOrIcon_RebuildsOnlyThatSide",
            $"a removal not rebuilding (Sq): red {Spec}");
        yield return C(typeof(SwipeItemData), nameof(SwipeItemData.Content), typeof(SwipeViewTests), nameof(SwipeViewTests.AContentItem_RerendersInPlace_AndUnmountsWhenRemoved),
            "re-rendered in place (state kept, the item kept); unmounted when removed; both registries. Also UnmountingTheSwipeView_UnmountsItsContentItems",
            $"content roots not unmounted on a rebuild (X15): red {Spec}");
        yield return C(typeof(SwipeItemData), nameof(SwipeItemData.OnInvoked), typeof(StructureAuditTests), swipe,
            "the current callback on a kept item; nothing after unmount; both registries. Also SwipeViewTests.ARetiredItem_InvokesNothing",
            $"an item invoking its mount-time data (X16): red {Spec}");

        // Dialogs (D): each content field's own mutant, its init made a no-op, turns the theory red.
        var request = nameof(DialogTests.ADeclarativeDialog_RequestsItsContent_OncePerOpen_AndReportsTheAnswerToTheContentShown);
        var queue = nameof(DialogTests.ADeclarativeDialog_ChangedWhileQueued_ShowsItsLatestContent_ClosedWhileQueuedIsRemoved_AndUnmountedWhileShownDropsTheAnswer);
        (Type Type, string[] Fields)[] kinds =
        [
            (typeof(AlertElement), [nameof(AlertElement.Title), nameof(AlertElement.Message), nameof(AlertElement.Accept), nameof(AlertElement.Cancel)]),
            (typeof(ActionSheetElement), [nameof(ActionSheetElement.Title), nameof(ActionSheetElement.Cancel), nameof(ActionSheetElement.Destruction), nameof(ActionSheetElement.Buttons)]),
            (typeof(PromptElement), [nameof(PromptElement.Title), nameof(PromptElement.Message), nameof(PromptElement.Accept), nameof(PromptElement.Cancel),
                nameof(PromptElement.Placeholder), nameof(PromptElement.InitialValue), nameof(PromptElement.Keyboard), nameof(PromptElement.MaxLength)]),
        ];
        foreach (var (type, fields) in kinds)
        {
            foreach (var field in fields)
                yield return C(type, field, typeof(DialogTests), request,
                    "in the one request an open makes, set through the factory and through the property; the new value shown after a re-open; the latest while queued (" + queue + ")",
                    $"its init made a no-op: red {Spec}");
            yield return C(type, nameof(DialogElement.IsOpen), typeof(DialogTests), request,
                "closed at mount, nothing; open, one request; an equal render, none; false then true, shown again; closed while queued, removed (" + queue + ")",
                $"its init made a no-op: red {Spec}");
            yield return C(type, "OnClosed", typeof(DialogTests), request,
                "once with the answer, to the content shown (a later render's callback doesn't take it); dropped when unmounted while shown (" + queue + ")",
                $"its init made a no-op: red {Spec}");
        }
    }
}
