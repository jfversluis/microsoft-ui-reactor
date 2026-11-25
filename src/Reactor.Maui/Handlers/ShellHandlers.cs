using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>What Reactor attaches to the Shell objects it builds (spec 069 §4).</summary>
internal static class ShellNatives
{
    private static readonly BindableProperty KeyProperty =
        BindableProperty.CreateAttached("ReactorKey", typeof(string), typeof(ShellNatives), null);

    private static readonly BindableProperty NavigatorProperty =
        BindableProperty.CreateAttached("ReactorNavigator", typeof(ShellSectionNavigator), typeof(ShellNatives), null);

    private static readonly BindableProperty OwnedContentProperty =
        BindableProperty.CreateAttached("ReactorOwnedContent", typeof(ShellContent), typeof(ShellNatives), null);

    private static readonly BindableProperty MenuItemProperty =
        BindableProperty.CreateAttached("ReactorMenuItem", typeof(MenuItem), typeof(ShellNatives), null);

    /// <summary>The selection key of a tab or top tab.</summary>
    public static string? GetKey(BindableObject item) => (string?)item.GetValue(KeyProperty);

    public static void SetKey(BindableObject item, string? key) => item.SetValue(KeyProperty, key);

    /// <summary>The navigator of a section whose content is a <c>NavigationHost</c>.</summary>
    public static ShellSectionNavigator? GetNavigator(ShellSection section) => (ShellSectionNavigator?)section.GetValue(NavigatorProperty);

    public static void SetNavigator(ShellSection section, ShellSectionNavigator? navigator) => section.SetValue(NavigatorProperty, navigator);

    /// <summary>The content Reactor made for a tab with a single page or host.</summary>
    public static ShellContent? GetOwnedContent(ShellSection section) => (ShellContent?)section.GetValue(OwnedContentProperty);

    public static void SetOwnedContent(ShellSection section, ShellContent content) => section.SetValue(OwnedContentProperty, content);

    public static MenuItem? GetMenuItem(ShellItem item) => (MenuItem?)item.GetValue(MenuItemProperty);

    public static void SetMenuItem(ShellItem item, MenuItem menuItem) => item.SetValue(MenuItemProperty, menuItem);

    /// <summary>Whether <paramref name="section"/> is the one its Shell shows.</summary>
    public static bool IsVisible(ShellSection section) =>
        section.Parent is ShellItem item && ReferenceEquals(item.CurrentItem, section)
        && item.Parent is Shell shell && ReferenceEquals(shell.CurrentItem, item);

    public static ReactorShell? ShellOf(Microsoft.Maui.Controls.Element? element)
    {
        for (var e = element; e is not null; e = e.Parent)
            if (e is ReactorShell shell) return shell;
        return null;
    }
}

/// <summary>
/// The Shell Reactor renders. It holds the declared selection, routes Shell's navigation events to
/// the sections' navigators, and reports the user's switches (spec 069 §4.3).
/// </summary>
internal sealed class ReactorShell : Shell
{
    public ReactorShell()
    {
        var platform = new PlatformShellTransitions(Dispatcher);
        Turns = platform;
        Transitions = platform;
    }

    /// <summary>Schedules the dispatcher turns a user's back is watched on; tests replace it.</summary>
    internal IShellTurns Turns { get; set; }

    /// <summary>The platform's transitions; tests replace it.</summary>
    internal IShellTransitions Transitions { get; set; }

    /// <summary>Milliseconds, for the settle warning; tests replace it.</summary>
    internal Func<long> Clock { get; set; } = static () => Environment.TickCount64;

    internal Action<string>? ReportSelected;
    internal Action<Action>? Post;
    internal Action? RequestRestore;
    internal bool Mounted = true;

    /// <summary>The selection the app declares.</summary>
    internal string? Declared;

    /// <summary>The section and top tab Reactor last selected; a Shell showing anything else was switched by the user.</summary>
    internal ShellSection? ExpectedSection;
    internal ShellContent? ExpectedContent;

    /// <summary>The fallback selection last reported for a key that doesn't exist, so it is reported once.</summary>
    internal string? FallbackReported;

    private bool _writing;

    internal ContentView? FlyoutHeaderHost;
    internal ContentView? FlyoutFooterHost;
    internal ContentView? FlyoutContentHost;

    /// <summary>The navigators of every section, the shown one first.</summary>
    internal IEnumerable<ShellSectionNavigator> Navigators
    {
        get
        {
            var visible = CurrentItem?.CurrentItem is { } shown ? ShellNatives.GetNavigator(shown) : null;
            if (visible is not null) yield return visible;
            foreach (var item in Items)
            {
                foreach (var section in item.Items)
                {
                    if (ShellNatives.GetNavigator(section) is { } navigator && !ReferenceEquals(navigator, visible))
                        yield return navigator;
                }
            }
        }
    }

    protected override void OnNavigating(ShellNavigatingEventArgs args)
    {
        base.OnNavigating(args);
        // Last of the Navigating handlers, so a cancel by an earlier one is seen.
        if (args.Cancelled) return;
        foreach (var navigator in Navigators)
            if (navigator.TryClaim(args)) return;
        if (args.Source is ShellNavigationSource.Pop or ShellNavigationSource.PopToRoot
            && CurrentItem?.CurrentItem is { } section && ShellNatives.GetNavigator(section) is { } shown)
            shown.OnUserBack(this, args);
    }

    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);
        foreach (var navigator in Navigators.ToList()) navigator.OnNavigated(args);
        if (_writing || !Mounted) return;

        // Reported once committed, never from Navigating, which is only a proposal (spec 069 §4.3).
        var section = CurrentItem?.CurrentItem;
        if (section is null || (ReferenceEquals(section, ExpectedSection) && ReferenceEquals(section.CurrentItem, ExpectedContent))) return;
        if (ShownKey() is not { } shown) return;
        ReportSelected?.Invoke(shown);
        // A switch the app doesn't accept is undone once state has settled, to the latest declaration.
        RequestRestore?.Invoke();
    }

    /// <summary>The key of what is shown: the top tab of a section that has several, else the tab.</summary>
    internal string? ShownKey()
    {
        var section = CurrentItem?.CurrentItem;
        if (section is null) return null;
        if (section.Items.Count > 1 && section.CurrentItem is { } content && ShellNatives.GetKey(content) is { } contentKey) return contentKey;
        return ShellNatives.GetKey(section);
    }

    /// <summary>
    /// Shows the declared selection, written at the level it names (a top tab, a tab, an item), skipping
    /// <paramref name="removing"/>. A key that doesn't exist selects the first selectable tab, reported
    /// once; a Shell with nothing selectable has no selection.
    /// </summary>
    internal void ApplySelection(IReadOnlyCollection<object>? removing = null)
    {
        var target = Find(Declared, removing);
        var key = Declared;
        if (target is null)
        {
            target = FirstSelectable(removing);
            key = target is { } first ? ShellNatives.GetKey(first.Section) : null;
        }
        if (target is not { } t)
        {
            FallbackReported = null;
            ExpectedSection = null;
            ExpectedContent = null;
            return;
        }

        _writing = true;
        try
        {
            if (t.Content is { } content && !ReferenceEquals(t.Section.CurrentItem, content)) t.Section.CurrentItem = content;
            if (!ReferenceEquals(t.Item.CurrentItem, t.Section)) t.Item.CurrentItem = t.Section;
            if (!ReferenceEquals(CurrentItem, t.Item)) CurrentItem = t.Item;
        }
        finally
        {
            _writing = false;
        }
        ExpectedSection = t.Section;
        ExpectedContent = t.Section.CurrentItem;

        if (key is not null && key != Declared)
        {
            if (FallbackReported != key)
            {
                FallbackReported = key;
                Post?.Invoke(() => ReportSelected?.Invoke(key));
            }
        }
        else
        {
            FallbackReported = null;
        }
    }

    private readonly record struct Selection(ShellItem Item, ShellSection Section, ShellContent? Content);

    private Selection? Find(string? key, IReadOnlyCollection<object>? removing)
    {
        if (key is null) return null;
        foreach (var item in Selectable(Items, removing))
        {
            foreach (var section in Selectable(item.Items, removing))
            {
                if (ShellNatives.GetKey(section) == key) return new(item, section, null);
                foreach (var content in Selectable(section.Items, removing))
                    if (!ReferenceEquals(content, ShellNatives.GetOwnedContent(section)) && ShellNatives.GetKey(content) == key)
                        return new(item, section, content);
            }
        }
        return null;
    }

    private Selection? FirstSelectable(IReadOnlyCollection<object>? removing)
    {
        foreach (var item in Selectable(Items, removing))
            foreach (var section in Selectable(item.Items, removing))
                if (Selectable(section.Items, removing).Any()) return new(item, section, null);
        return null;
    }

    private static IEnumerable<T> Selectable<T>(IEnumerable<T> items, IReadOnlyCollection<object>? removing) where T : BaseShellItem =>
        items.Where(i => (removing is null || !removing.Contains(i)) && (i is not ShellItem item || ShellNatives.GetMenuItem(item) is null));

    /// <summary>
    /// Syncs a Shell collection with the declared natives in stages, so the selection never rests on
    /// MAUI's asynchronous fallback for a removed current item (spec 069 §4.3): new items are added,
    /// the selection is applied, the old ones are removed, then the order is fixed without removing
    /// the current one.
    /// </summary>
    internal static void Stage<T>(IList<T> actual, IReadOnlyList<T> desired, ReactorShell? shell, Func<T?> current) where T : BaseShellItem
    {
        for (var i = 0; i < desired.Count; i++)
            if (!actual.Contains(desired[i])) actual.Insert(Math.Min(i, actual.Count), desired[i]);

        var wanted = new HashSet<T>(desired, ReferenceEqualityComparer.Instance);
        var removing = actual.Where(a => !wanted.Contains(a)).ToList();
        if (removing.Count > 0 || shell is not null) shell?.ApplySelection(removing.Count == 0 ? null : [.. removing.Cast<object>()]);
        foreach (var item in removing) actual.Remove(item);

        if (actual.SequenceEqual(desired, ReferenceEqualityComparer.Instance)) return;
        var keep = current();
        for (var i = actual.Count - 1; i >= 0; i--)
            if (!ReferenceEquals(actual[i], keep)) actual.RemoveAt(i);
        for (var i = 0; i < desired.Count; i++)
            if (!ReferenceEquals(desired[i], keep)) actual.Insert(i, desired[i]);
    }

    /// <summary>Selectable keys are unique across the Shell (spec 069 §4.1).</summary>
    internal static void ValidateKeys(IEnumerable<ShellItem> items)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            foreach (var section in item.Items)
            {
                Check(ShellNatives.GetKey(section));
                foreach (var content in section.Items)
                    if (!ReferenceEquals(content, ShellNatives.GetOwnedContent(section))) Check(ShellNatives.GetKey(content));
            }
        }

        void Check(string? key)
        {
            if (key is not null && !seen.Add(key))
                throw new InvalidOperationException($"Two Shell tabs or top tabs have the key '{key}'. Selectable keys must be unique across the Shell.");
        }
    }
}

internal sealed class ShellHandler : Reactor.Hosting.IElementHandler<ShellElement, ReactorShell>
{
    public ReactorShell Mount(HandlerContext<ShellElement> context, ShellElement element)
    {
        var shell = new ReactorShell { Declared = element.Selected };
        var node = context.Node;
        shell.ReportSelected = key =>
        {
            if (context.Live is { } live) live.OnSelected?.Invoke(key);
        };
        shell.Post = node.Root.Post;
        shell.RequestRestore = context.RestoreAfterEvent;
        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != Shell.FlyoutIsPresentedProperty.PropertyName || context.IsEchoing(shell)) return;
            if (context.Live is not { } live) return;
            live.OnFlyoutIsPresentedChanged?.Invoke(shell.FlyoutIsPresented);
            context.RestoreAfterEvent();
        };
        Apply(context, shell, null, element);
        return shell;
    }

    public void Update(HandlerContext<ShellElement> context, ShellElement oldElement, ShellElement newElement, ReactorShell control)
    {
        Apply(context, control, oldElement, newElement);
        control.Declared = newElement.Selected;
        // A restore (the same element) re-asserts the declared selection; a new element's is applied
        // once its items are synced.
        if (ReferenceEquals(oldElement, newElement)) control.ApplySelection();
    }

    public void Unmount(ReactorShell control)
    {
        control.Mounted = false;
        control.ReportSelected = null;
    }

    private static void Apply(HandlerContext<ShellElement> context, ReactorShell shell, ShellElement? previous, ShellElement next)
    {
        Props.SetRef(shell, Page.TitleProperty, previous?.Title, next.Title);
        Props.Set(shell, Shell.FlyoutBehaviorProperty, previous?.FlyoutBehavior, next.FlyoutBehavior);
        Props.SetRef(shell, Shell.BackgroundColorProperty, previous?.BackgroundColor, next.BackgroundColor);
        Props.SetRef(shell, Shell.ForegroundColorProperty, previous?.ForegroundColor, next.ForegroundColor);
        Props.SetRef(shell, Shell.TitleColorProperty, previous?.TitleColor, next.TitleColor);
        Props.SetRef(shell, Shell.DisabledColorProperty, previous?.DisabledColor, next.DisabledColor);
        Props.SetRef(shell, Shell.UnselectedColorProperty, previous?.UnselectedColor, next.UnselectedColor);
        Props.SetRef(shell, Shell.FlyoutBackgroundColorProperty, previous?.FlyoutBackgroundColor, next.FlyoutBackgroundColor);
        Props.SetRef(shell, Shell.TabBarBackgroundColorProperty, previous?.TabBarBackgroundColor, next.TabBarBackgroundColor);
        Props.SetRef(shell, Shell.TabBarForegroundColorProperty, previous?.TabBarForegroundColor, next.TabBarForegroundColor);
        Props.SetRef(shell, Shell.TabBarTitleColorProperty, previous?.TabBarTitleColor, next.TabBarTitleColor);
        Props.SetRef(shell, Shell.TabBarUnselectedColorProperty, previous?.TabBarUnselectedColor, next.TabBarUnselectedColor);
        Props.SetRef(shell, Shell.TabBarDisabledColorProperty, previous?.TabBarDisabledColor, next.TabBarDisabledColor);
        // Compared with the Shell, not the previous element: a restore re-asserts the declared value.
        if (next.FlyoutIsPresented is { } presented && shell.FlyoutIsPresented != presented)
            context.WriteSuppressed(shell, () => shell.FlyoutIsPresented = presented);
    }

    public IChildrenStrategy<ShellElement, ReactorShell> Children { get; } = new ShellChildren();

    // The items, then the flyout header, footer and content.
    private sealed class ShellChildren : ISlottedChildrenStrategy<ShellElement, ReactorShell>
    {
        public IReadOnlyList<Element?> GetChildren(ShellElement element) =>
            [.. element.Items, element.FlyoutHeader, element.FlyoutFooter, element.FlyoutContent];

        public void SyncSlots(ShellElement element, ReactorShell shell, IReadOnlyList<IReadOnlyList<object>> slots)
        {
            var count = element.Items.Length;
            var items = slots.Take(count).SelectMany(static s => s).Select(AsItem).ToList();
            ReactorShell.ValidateKeys(items);
            shell.Declared = element.Selected;
            ReactorShell.Stage(shell.Items, items, shell, () => shell.CurrentItem);

            var header = Single(slots[count], "flyout header");
            if (header is not null || shell.FlyoutHeaderHost is not null)
            {
                var host = shell.FlyoutHeaderHost ??= new ContentView();
                // MAUI binds the header once: the same host stays, and its content changes.
                if (!ReferenceEquals(shell.FlyoutHeader, host)) shell.FlyoutHeader = host;
                host.Content = header is null ? null : Props.AsView(header);
            }
            var footer = Single(slots[count + 1], "flyout footer");
            if (footer is not null || shell.FlyoutFooterHost is not null)
            {
                var host = shell.FlyoutFooterHost ??= new ContentView();
                if (!ReferenceEquals(shell.FlyoutFooter, host)) shell.FlyoutFooter = host;
                host.Content = footer is null ? null : Props.AsView(footer);
            }
            var content = Single(slots[count + 2], "flyout content");
            if (content is null)
            {
                // Without content the flyout lists its items again.
                if (shell.IsSet(Shell.FlyoutContentProperty)) shell.ClearValue(Shell.FlyoutContentProperty);
            }
            else
            {
                var host = shell.FlyoutContentHost ??= new ContentView();
                if (!ReferenceEquals(shell.FlyoutContent, host)) shell.FlyoutContent = host;
                host.Content = Props.AsView(content);
            }
        }

        private static ShellItem AsItem(object native) => native as ShellItem ?? throw new InvalidOperationException(
            $"A Shell holds TabBar, FlyoutItem and MenuItem elements, but an item rendered a {native.GetType().Name}.");

        private static object? Single(IReadOnlyList<object> natives, string what) => natives.Count switch
        {
            0 => null,
            1 => natives[0],
            _ => throw new InvalidOperationException($"A Shell's {what} rendered {natives.Count} controls. Wrap them in a stack."),
        };
    }
}

/// <summary>A <c>TabBar</c> or <c>FlyoutItem</c>'s tabs, synced in stages through the Shell.</summary>
internal sealed class ShellItemChildren<TElement, TItem>(Func<TElement, ShellTabElement[]> tabs) : IChildrenStrategy<TElement, TItem>
    where TElement : Element
    where TItem : ShellItem
{
    public IReadOnlyList<Element?> GetChildren(TElement element) => tabs(element);

    public void Sync(TItem item, IReadOnlyList<object> natives)
    {
        var sections = natives.Select(n => n as ShellSection ?? throw new InvalidOperationException(
            $"A TabBar or FlyoutItem holds ShellTab elements, but a tab rendered a {n.GetType().Name}.")).ToList();
        var shell = item.Parent as ReactorShell;
        if (shell is not null) ReactorShell.ValidateKeys(shell.Items);
        ReactorShell.Stage(item.Items, sections, shell, () => item.CurrentItem);
    }
}

internal sealed class TabBarHandler : Reactor.Hosting.IElementHandler<TabBarElement, TabBar>
{
    public TabBar Mount(HandlerContext<TabBarElement> context, TabBarElement element) => new();

    public void Update(HandlerContext<TabBarElement> context, TabBarElement oldElement, TabBarElement newElement, TabBar control) { }

    public IChildrenStrategy<TabBarElement, TabBar> Children { get; } = new ShellItemChildren<TabBarElement, TabBar>(e => e.Tabs);
}

internal sealed class FlyoutItemHandler : Reactor.Hosting.IElementHandler<FlyoutItemElement, FlyoutItem>
{
    public FlyoutItem Mount(HandlerContext<FlyoutItemElement> context, FlyoutItemElement element)
    {
        var item = new FlyoutItem();
        Apply(item, null, element);
        return item;
    }

    public void Update(HandlerContext<FlyoutItemElement> context, FlyoutItemElement oldElement, FlyoutItemElement newElement, FlyoutItem control) =>
        Apply(control, oldElement, newElement);

    private static void Apply(FlyoutItem item, FlyoutItemElement? previous, FlyoutItemElement next)
    {
        Props.SetRef(item, BaseShellItem.TitleProperty, previous?.Title, next.Title);
        Props.SetImage(item, BaseShellItem.IconProperty, previous?.Icon, next.Icon, previous is null);
        Props.Set(item, ShellGroupItem.FlyoutDisplayOptionsProperty, previous?.DisplayOptions, next.DisplayOptions);
    }

    public IChildrenStrategy<FlyoutItemElement, FlyoutItem> Children { get; } = new ShellItemChildren<FlyoutItemElement, FlyoutItem>(e => e.Tabs);
}

internal sealed class ShellMenuItemHandler : Reactor.Hosting.IElementHandler<ShellMenuItemElement, ShellItem>
{
    public ShellItem Mount(HandlerContext<ShellMenuItemElement> context, ShellMenuItemElement element)
    {
        var menuItem = new MenuItem();
        menuItem.Clicked += (_, _) => context.Live?.OnClick?.Invoke();
        // Shell keeps menu items in its items as MAUI's wrapper, which Reactor makes itself so it owns it.
        ShellItem item = menuItem;
        ShellNatives.SetMenuItem(item, menuItem);
        Apply(menuItem, null, element);
        return item;
    }

    public void Update(HandlerContext<ShellMenuItemElement> context, ShellMenuItemElement oldElement, ShellMenuItemElement newElement, ShellItem control) =>
        Apply(ShellNatives.GetMenuItem(control)!, oldElement, newElement);

    private static void Apply(MenuItem item, ShellMenuItemElement? previous, ShellMenuItemElement next)
    {
        Props.SetRef(item, MenuItem.TextProperty, previous?.Text, next.Text);
        Props.SetImage(item, MenuItem.IconImageSourceProperty, previous?.Icon, next.Icon, previous is null);
    }
}

internal sealed class ShellTabHandler : Reactor.Hosting.IElementHandler<ShellTabElement, Tab>
{
    public Tab Mount(HandlerContext<ShellTabElement> context, ShellTabElement element)
    {
        var tab = new Tab();
        ShellNatives.SetKey(tab, KeyOf(element));
        Apply(tab, null, element);
        return tab;
    }

    public void Update(HandlerContext<ShellTabElement> context, ShellTabElement oldElement, ShellTabElement newElement, Tab control)
    {
        ShellNatives.SetKey(control, KeyOf(newElement));
        Apply(control, oldElement, newElement);
    }

    public void Unmount(Tab control) => ShellNatives.GetNavigator(control)?.DetachSection(control);

    internal static string KeyOf(Element element) => element.Key ?? throw new InvalidOperationException(
        $"A {element.GetType().Name} needs a key: it is how the Shell's selection names it.");

    private static void Apply(Tab tab, ShellTabElement? previous, ShellTabElement next)
    {
        Props.SetRef(tab, BaseShellItem.TitleProperty, previous?.Title, next.Title);
        Props.SetImage(tab, BaseShellItem.IconProperty, previous?.Icon, next.Icon, previous is null);
        if (ShellNatives.GetOwnedContent(tab) is { } owned) Props.SetRef(owned, BaseShellItem.TitleProperty, previous?.Title, next.Title);
    }

    public IChildrenStrategy<ShellTabElement, Tab> Children { get; } = new TabChildren();

    private sealed class TabChildren : ISlottedChildrenStrategy<ShellTabElement, Tab>
    {
        // A single content is told it is a Shell tab's, so a NavigationHost renders the section's stack.
        public IReadOnlyList<Element?> GetChildren(ShellTabElement element) =>
            element.Contents.Length > 0 ? element.Contents : [Navigation.ShellTabScope.Context.Provide(true, element.Content)];

        public void SyncSlots(ShellTabElement element, Tab tab, IReadOnlyList<IReadOnlyList<object>> slots)
        {
            var shell = ShellNatives.ShellOf(tab);
            var navigator = ShellNatives.GetNavigator(tab);
            if (element.Contents.Length > 0)
            {
                navigator?.DetachSection(tab);
                var contents = slots.SelectMany(static s => s).Select(n => n as ShellContent ?? throw new InvalidOperationException(
                    $"A ShellTab's top tabs are ShellContent elements, but one rendered a {n.GetType().Name}.")).ToList();
                if (shell is not null) ReactorShell.ValidateKeys(shell.Items);
                ReactorShell.Stage(tab.Items, contents, shell, () => tab.CurrentItem);
                return;
            }

            var owned = ShellNatives.GetOwnedContent(tab);
            if (owned is null)
            {
                owned = new ShellContent { Title = element.Title };
                ShellNatives.SetKey(owned, ShellNatives.GetKey(tab));
                ShellNatives.SetOwnedContent(tab, owned);
            }
            var native = slots[0] switch
            {
                [] => null,
                [var one] => one,
                var many => throw new InvalidOperationException($"A ShellTab's content rendered {many.Count} controls; it holds one page."),
            };
            switch (native)
            {
                case ShellSectionNavigator stack:
                    if (!ReferenceEquals(navigator, stack)) navigator?.DetachSection(tab);
                    // The navigator sets the root page as it reconciles.
                    ReactorShell.Stage(tab.Items, [owned], shell, () => tab.CurrentItem);
                    stack.Attach(tab, owned);
                    break;
                case null or Page:
                    navigator?.DetachSection(tab);
                    if (native is Page page and (NavigationPage or TabbedPage or FlyoutPage))
                        throw new InvalidOperationException(
                            $"A ShellTab holds a page or a NavigationHost, but its content rendered a {page.GetType().Name}, which Shell can't host.");
                    if (!ReferenceEquals(owned.Content, native)) owned.Content = native;
                    ReactorShell.Stage(tab.Items, [owned], shell, () => tab.CurrentItem);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"A ShellTab holds a page or a NavigationHost, but its content rendered a {native.GetType().Name}. Wrap it in Page(…).");
            }
        }
    }
}

internal sealed class ShellContentHandler : Reactor.Hosting.IElementHandler<ShellContentElement, ShellContent>
{
    public ShellContent Mount(HandlerContext<ShellContentElement> context, ShellContentElement element)
    {
        var content = new ShellContent();
        ShellNatives.SetKey(content, ShellTabHandler.KeyOf(element));
        Apply(content, null, element);
        return content;
    }

    public void Update(HandlerContext<ShellContentElement> context, ShellContentElement oldElement, ShellContentElement newElement, ShellContent control)
    {
        ShellNatives.SetKey(control, ShellTabHandler.KeyOf(newElement));
        Apply(control, oldElement, newElement);
    }

    private static void Apply(ShellContent content, ShellContentElement? previous, ShellContentElement next)
    {
        Props.SetRef(content, BaseShellItem.TitleProperty, previous?.Title, next.Title);
        Props.SetImage(content, BaseShellItem.IconProperty, previous?.Icon, next.Icon, previous is null);
    }

    public IChildrenStrategy<ShellContentElement, ShellContent> Children { get; } =
        ChildrenStrategy.Single<ShellContentElement, ShellContent, object>(
            e => e.Page,
            (content, native) =>
            {
                if (native is not (null or ContentPage))
                    throw new InvalidOperationException(
                        $"A top tab (ShellContent) holds a page, but it rendered a {native.GetType().Name}. A NavigationHost belongs in ShellTab(key, title, icon, host).");
                if (!ReferenceEquals(content.Content, native)) content.Content = native;
            });
}

internal sealed class ShellSectionStackHandler : Reactor.Hosting.IElementHandler<ShellSectionStackElement, ShellSectionNavigator>
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ShellSectionNavigator, EventHandler<ModalPoppedEventArgs>> ModalHandlers = new();

    public ShellSectionNavigator Mount(HandlerContext<ShellSectionStackElement> context, ShellSectionStackElement element)
    {
        var navigator = new ShellSectionNavigator { OnError = context.ReportError, Binding = element.HostBinding };
        EventHandler<ModalPoppedEventArgs> modalPopped = (_, e) =>
        {
            if (!navigator.IsUserModalPop(e.Modal, out var remaining))
            {
                navigator.Run();
                return;
            }
            context.Live?.OnModalDismissed?.Invoke(remaining);
            context.RestoreAfterEvent();
        };
        ModalHandlers.AddOrUpdate(navigator, modalPopped);
        if (Application.Current is { } app) app.ModalPopped += modalPopped;
        element.HostBinding?.Attach(context.Node, navigator);
        return navigator;
    }

    public void Update(HandlerContext<ShellSectionStackElement> context, ShellSectionStackElement oldElement, ShellSectionStackElement newElement, ShellSectionNavigator control)
    {
        if (!ReferenceEquals(oldElement.HostBinding, newElement.HostBinding))
        {
            oldElement.HostBinding?.Detach(control);
            newElement.HostBinding?.Attach(context.Node, control);
            control.Binding = newElement.HostBinding;
        }
        // The same element is a restore: reconcile against the current declaration.
        if (ReferenceEquals(oldElement, newElement)) control.Run();
    }

    public void Unmount(ShellSectionNavigator control)
    {
        if (Application.Current is { } app && ModalHandlers.TryGetValue(control, out var handler)) app.ModalPopped -= handler;
        control.Binding?.Detach(control);
        control.Stop();
    }

    public IChildrenStrategy<ShellSectionStackElement, ShellSectionNavigator> Children { get; } = new StackChildren();

    private sealed class StackChildren : ISlottedChildrenStrategy<ShellSectionStackElement, ShellSectionNavigator>
    {
        public IReadOnlyList<Element?> GetChildren(ShellSectionStackElement element) => [.. element.Pages, .. element.Modals];

        public void SyncSlots(ShellSectionStackElement element, ShellSectionNavigator control, IReadOnlyList<IReadOnlyList<object>> slots)
        {
            var pages = slots.Take(element.Pages.Length).SelectMany(static s => s).Select(NavigationStackHandler.AsPage).ToList();
            var modals = slots.Skip(element.Pages.Length).SelectMany(static s => s).Select(NavigationStackHandler.AsPage).ToList();
            if (element.HostBinding is { } binding && element.HostDeclaration is { } declaration)
                binding.Sync(declaration, element.Animated, pages, modals, control);
            else
                control.SetDesired(pages, modals, element.Animated);
        }
    }
}
