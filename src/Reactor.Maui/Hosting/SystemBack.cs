using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Maui.Hosting;

/// <summary>
/// The <c>UseSystemBackButton</c> handlers of each tree and their dispatch (spec 069 §5.2). MAUI sends
/// the window's back request to its top modal, or else its page, through
/// <see cref="Page.SendBackButtonPressed"/>; every page Reactor creates calls <see cref="Handle"/> from
/// its <c>OnBackButtonPressed</c>. The first Reactor page of a tree that the request reaches asks the
/// handlers of the pages the user sees (and those above every page), deepest first, then the most
/// recently mounted; pages nested in it pass the request on without asking them again.
/// </summary>
internal static class SystemBack
{
    private static readonly ConditionalWeakTable<ReactorRoot, List<Registration>> Registries = new();
    private static long s_sequence;

    // The trees whose handlers the back request being dispatched on this thread has asked.
    [ThreadStatic] private static HashSet<ReactorRoot>? t_asked;

    /// <summary>Tests turn the handlers on or off for their own flow; otherwise Android and Windows only.</summary>
    internal static readonly AsyncLocal<bool?> EnabledOverride = new();

    /// <summary>
    /// Whether the handlers run: for the system back key, which Android (and Windows, from 4g) send.
    /// iOS and Mac Catalyst have no back key; MAUI calls <c>SendBackButtonPressed</c> there only for
    /// Shell's navigation-bar back, which goes through the stack's guards instead.
    /// </summary>
    internal static bool Enabled => EnabledOverride.Value ?? (OperatingSystem.IsAndroid() || OperatingSystem.IsWindows());

    internal sealed class Registration(Node node, Func<bool> handler, long sequence)
    {
        public Node Node { get; } = node;
        public Func<bool> Handler { get; } = handler;
        public long Sequence { get; } = sequence;
        public bool Removed { get; set; }
    }

    /// <summary>Registers <paramref name="handler"/> for <paramref name="node"/>'s component; returns its removal.</summary>
    public static Action Register(Node node, Func<bool> handler)
    {
        var registry = Registries.GetValue(node.Root.TopRoot, static _ => []);
        var registration = new Registration(node, handler, Interlocked.Increment(ref s_sequence));
        registry.Add(registration);
        return () =>
        {
            registration.Removed = true;
            registry.Remove(registration);
        };
    }

    /// <summary>
    /// A Reactor page of <paramref name="root"/>'s tree received the back request: asks the eligible
    /// handlers once, then, if none handled it, runs <paramref name="passOn"/>, the page's own back
    /// handling (pages it reaches don't ask the handlers again).
    /// </summary>
    public static bool Handle(Page receiver, ReactorRoot? root, Func<bool> passOn)
    {
        if (!Enabled || root is null) return passOn();
        var top = root.TopRoot;
        var asked = t_asked ??= [];
        if (!asked.Add(top)) return passOn();
        try
        {
            return Ask(receiver, top) || passOn();
        }
        finally
        {
            asked.Remove(top);
        }
    }

    private static bool Ask(Page receiver, ReactorRoot root)
    {
        if (!Registries.TryGetValue(root, out var registry) || registry.Count == 0) return false;
        var visible = new HashSet<object>(ReferenceEqualityComparer.Instance);
        AddVisible(receiver, visible);
        // A snapshot: each handler runs at most once, even if handlers mount or unmount others.
        var eligible = registry
            .Where(r => !r.Node.Unmounted && (PageOf(r.Node) is not { } page || visible.Contains(page)))
            .OrderByDescending(r => Depth(r.Node))
            .ThenByDescending(r => r.Sequence)
            .ToList();
        foreach (var registration in eligible)
        {
            if (registration.Removed || registration.Node.Unmounted) continue;
            try
            {
                if (registration.Handler()) return true;
            }
            catch (Exception ex)
            {
                // Like a throwing navigation guard: the back is consumed, and the error goes to the
                // component's error boundary.
                registration.Node.Root.ReportCallbackError(registration.Node, ex);
                return true;
            }
        }
        return false;
    }

    /// <summary>The page the user sees through <paramref name="page"/>, and the containers it is shown in.</summary>
    internal static void AddVisible(Page? page, HashSet<object> visible)
    {
        if (page is null || !visible.Add(page)) return;
        switch (page)
        {
            case Shell shell:
                if (shell.CurrentItem is { } item)
                {
                    visible.Add(item);
                    if (item.CurrentItem is { } section)
                    {
                        visible.Add(section);
                        if (section.CurrentItem is { } content) visible.Add(content);
                    }
                }
                AddVisible(shell.CurrentPage, visible);
                break;
            case NavigationPage navigation:
                AddVisible(navigation.CurrentPage, visible);
                break;
            case FlyoutPage flyout:
                AddVisible(flyout.Detail, visible);
                if (flyout.IsPresented) AddVisible(flyout.Flyout, visible);
                break;
            case MultiPage<Page> tabs:
                AddVisible(tabs.CurrentPage, visible);
                break;
        }
    }

    /// <summary>
    /// The page a component belongs to: the page it renders (a route's component that returns
    /// <c>Page(…)</c>, or one that returns a <c>NavigationHost</c>), else the nearest page (or Shell
    /// item, tab or content) above it, across list cells. <see langword="null"/> above every page.
    /// </summary>
    internal static object? PageOf(Node node)
    {
        var rendered = node;
        while (rendered.Kind != NodeKind.Host && rendered.Children.Count == 1) rendered = rendered.Children[0];
        if (rendered.Kind == NodeKind.Host && rendered.Native is Page or BaseShellItem) return rendered.Native;
        return NearestPage(node);
    }

    private static object? NearestPage(Node node)
    {
        for (var n = Up(node); n is not null; n = Up(n))
            if (n.Kind == NodeKind.Host && n.Native is Page or BaseShellItem) return n.Native;
        return null;
    }

    private static Node? Up(Node node) => node.Parent ?? node.Root.ContextParent;

    private static int Depth(Node node) => node.Depth + (node.Root.ContextParent is { } owner ? Depth(owner) + 1 : 0);
}
