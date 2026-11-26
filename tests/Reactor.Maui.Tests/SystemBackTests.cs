using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Handlers;
using Microsoft.UI.Reactor.Maui.Hosting;
using Microsoft.UI.Reactor.Navigation;
using static Microsoft.UI.Reactor.Maui.Factories;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>What the back handlers report to, and which of them handle the back.</summary>
public sealed class BackLog
{
    public List<string> Asked { get; } = [];
    public HashSet<string> Handles { get; } = [];
    public HashSet<string> Throws { get; } = [];

    public bool Ask(string name)
    {
        Asked.Add(name);
        if (Throws.Contains(name)) throw new InvalidOperationException($"back boom {name}");
        return Handles.Contains(name);
    }

    public string Take()
    {
        var text = string.Join(",", Asked);
        Asked.Clear();
        return text;
    }
}

public sealed record BackProbeProps(string Name, BackLog Log, Element? Child = null);

/// <summary>Registers a back handler under its name, and renders its child (or a label).</summary>
public sealed class BackProbe : Component<BackProbeProps>
{
    public override Element? Render()
    {
        var (name, log) = (Props.Name, Props.Log);
        UseSystemBackButton(() => log.Ask(name));
        return Props.Child ?? Label(name);
    }
}

public sealed record BackHostProps(BackLog Log, Action<NavigationHandle<string>> Report, bool HandleOverload);

/// <summary>A NavigationHost whose pages register back handlers; optionally the handle overload at the root.</summary>
public sealed class BackHostApp : Component<BackHostProps>
{
    public override Element Render()
    {
        var nav = UseNavigation("home");
        Props.Report(nav);
        if (Props.HandleOverload) UseSystemBackButton(nav);
        var log = Props.Log;
        return NavigationHost(nav, route => Component<GuardedBackPage, BackProbeProps>(new(route, log)));
    }
}

/// <summary>A page whose guard logs and refuses when its name is in <see cref="BackLog.Handles"/> as "refuse:name".</summary>
public sealed class GuardedBackPage : Component<BackProbeProps>
{
    public override Element Render()
    {
        var (name, log) = (Props.Name, Props.Log);
        // The route's component renders the page, so its handler belongs to that page.
        UseSystemBackButton(() => log.Ask($"page:{name}"));
        UseNavigationLifecycle(onNavigatingFrom: c =>
        {
            log.Asked.Add($"guard:{name}");
            if (log.Handles.Contains($"refuse:{name}")) c.Cancel();
        });
        return Page(name, Component<BackProbe, BackProbeProps>(new(name, log)));
    }
}

// The README's system back snippet.
public sealed class ReadmeEditor : Component<Action>
{
    public override Element Render()
    {
        var (dirty, setDirty) = UseState(true);
        var dialogs = UseDialogs();
        UseSystemBackButton(() =>
        {
            if (!dirty) return false;
            _ = Discard();
            return true;
        });
        return Page("Editor", Entry("draft", _ => setDirty(true)));

        async Task Discard()
        {
            if (await dialogs.Alert("Discard changes?", null, "Discard", "Keep")) Props();
        }
    }
}

public class SystemBackTests
{
    // An AsyncLocal set in a synchronous helper stays set for the calling test: Android's behavior,
    // on whatever OS the suite runs.
    private static void AsOnAndroid(bool android = true) => SystemBack.EnabledOverride.Value = android;

    private static TestHost Show(Element element)
    {
        AsOnAndroid();
        var host = new TestHost(MauiHandlers.CreateRegistry());
        host.Render(element);
        host.Scheduler.Drain();
        return host;
    }

    private static Element Probe(string name, BackLog log, Element? child = null) =>
        Component<BackProbe, BackProbeProps>(new(name, log, child));

    private static Element ProbePage(string name, BackLog log) => Page(name, Probe(name, log));

    private static void Settle(TestHost host, Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++)
        {
            host.Scheduler.Drain();
            Thread.Sleep(5);
        }
        host.Scheduler.Drain();
    }

    [Fact]
    public void OnlyTheVisiblePagesHandlersAreAsked_DeepestFirst_ThenTheMostRecentlyMounted_EachOnce()
    {
        var log = new BackLog();
        var host = Show(Probe("root", log, Tabs(1, null,
            ProbePage("tab-a", log),
            NavigationStack([
                ProbePage("below", log),
                Page("top", VStack(Probe("top-first", log), Probe("top-second", log, Probe("top-deep", log)))).WithKey("top"),
            ]))));
        var tabs = host.View<TabbedPage>();
        var stack = Assert.IsType<ReactorNavigationPage>(tabs.Children[1]);
        Settle(host, () => stack.Navigation.NavigationStack.Count == 2);
        Assert.Equal(2, stack.Navigation.NavigationStack.Count);

        log.Handles.Add("root");
        Assert.True(tabs.SendBackButtonPressed());
        // The deselected tab and the page below the top never intercept; the root-level handler is asked last.
        Assert.Equal("top-deep,top-second,top-first,root", log.Take());
        Assert.Equal(2, stack.Navigation.NavigationStack.Count);
    }

    [Fact]
    public void AHandlerThatHandlesTheBack_StopsIt()
    {
        var log = new BackLog();
        var host = Show(NavigationStack([ProbePage("below", log), ProbePage("top", log).WithKey("top")]));
        var stack = host.View<ReactorNavigationPage>();
        Settle(host, () => stack.Navigation.NavigationStack.Count == 2);
        log.Handles.Add("top");
        Assert.True(stack.SendBackButtonPressed());
        Assert.Equal("top", log.Take());
        Assert.Equal(2, stack.Navigation.NavigationStack.Count);

        // Unhandled, the back falls through to the stack, which pops (asking each handler once).
        log.Handles.Clear();
        Assert.True(stack.SendBackButtonPressed());
        Assert.Equal("top", log.Take());
        Assert.Single(stack.Navigation.NavigationStack);
    }

    [Fact]
    public void AFlyout_AsksItsDetail_AndItsFlyoutWhilePresented()
    {
        var log = new BackLog();
        Element Tree(bool presented) => Flyout(ProbePage("menu", log), ProbePage("detail", log), presented);
        var host = Show(Tree(false));
        var flyout = host.View<FlyoutPage>();
        Assert.False(flyout.SendBackButtonPressed());
        Assert.Equal("detail", log.Take());

        host.Render(Tree(true));
        host.Scheduler.Drain();
        flyout.SendBackButtonPressed();
        Assert.Equal("detail,menu", log.Take());
    }

    [Fact]
    public void AShell_AsksTheSelectedTabsPage_NotADeselectedTabsComponents()
    {
        var log = new BackLog();
        var host = Show(Shell("a", null,
            TabBar(
                ShellTab("a", "A", ProbePage("a", log)),
                ShellTab("b", "B", Probe("b-holder", log, ProbePage("b", log))))));
        var shell = host.View<ReactorShell>();
        _ = new Window(shell) { Parent = DialogTests.App };
        host.Scheduler.Drain();
        Assert.False(shell.SendBackButtonPressed());
        Assert.Equal("a", log.Take());
    }

    [Fact]
    public void ARootPage_PassesAnUnhandledBackOn()
    {
        var log = new BackLog();
        var host = Show(Probe("root", log, ProbePage("page", log)));
        var page = host.View<ContentPage>();
        Assert.False(page.SendBackButtonPressed());
        Assert.Equal("page,root", log.Take());
        log.Handles.Add("root");
        Assert.True(page.SendBackButtonPressed());
        Assert.Equal("page,root", log.Take());
    }

    [Fact]
    public void AnUnmountedHandler_IsNotAsked()
    {
        var log = new BackLog();
        var host = Show(Page("page", VStack(Probe("kept", log), Probe("removed", log))));
        host.Render(Page("page", VStack(Probe("kept", log))));
        host.Scheduler.Drain();
        host.View<ContentPage>().SendBackButtonPressed();
        Assert.Equal("kept", log.Take());
    }

    [Fact]
    public void AThrowingHandler_ConsumesTheBack_AndReportsToItsBoundary()
    {
        var log = new BackLog();
        log.Throws.Add("throws");
        var host = Show(Page("page", ErrorBoundary(Probe("throws", log), e => Label($"caught: {e.Message}"))));
        var page = host.View<ContentPage>();
        Assert.True(page.SendBackButtonPressed());
        host.Scheduler.Drain();
        Assert.Equal("caught: back boom throws", Assert.IsType<Label>(page.Content).Text);
    }

    [Fact]
    public void OnIOS_TheHandlersAreNotAsked()
    {
        var log = new BackLog();
        log.Handles.Add("page");
        var host = Show(ProbePage("page", log));
        AsOnAndroid(false);
        Assert.False(host.View<ContentPage>().SendBackButtonPressed());
        Assert.Empty(log.Asked);
    }

    private sealed class HostHarness
    {
        public TestHost Host { get; } = new(MauiHandlers.CreateRegistry());
        public BackLog Log { get; } = new();
        public NavigationHandle<string> Nav = null!;

        public HostHarness(bool handleOverload)
        {
            AsOnAndroid();
            Host.Render(Component<BackHostApp, BackHostProps>(new(Log, n => Nav = n, handleOverload)));
            Settle();
        }

        public ReactorNavigationPage Page => Host.View<ReactorNavigationPage>();
        public string Titles => string.Join(",", Page.Navigation.NavigationStack.Select(p => p.Title));

        public void Settle()
        {
            for (var i = 0; i < 200; i++)
            {
                Host.Scheduler.Drain();
                if (Page.Navigator.IsIdle) break;
                Thread.Sleep(5);
            }
            Host.Scheduler.Drain();
        }
    }

    [Fact]
    public void AnUnhandledBack_FallsThroughToTheNavigationHost_WhoseGuardRunsOnce()
    {
        var h = new HostHarness(handleOverload: false);
        h.Nav.Navigate("detail");
        h.Settle();
        Assert.Equal("home,detail", h.Titles);
        h.Log.Take();

        Assert.True(h.Page.SendBackButtonPressed());
        h.Settle();
        // The page's handlers (deepest first), never the page below's, then the host's guard.
        Assert.Equal("detail,page:detail,guard:detail", h.Log.Take());
        Assert.Equal("home", h.Titles);
        Assert.Equal("home", h.Nav.CurrentRoute);
    }

    [Fact]
    public void TheHandleOverload_ConsumesARefusedBack_WithTheGuardRunningOnce()
    {
        var h = new HostHarness(handleOverload: true);
        h.Nav.Navigate("detail");
        h.Settle();
        h.Log.Take();
        h.Log.Handles.Add("refuse:detail");
        var pops = 0;
        h.Page.Popped += (_, _) => pops++;

        Assert.True(h.Page.SendBackButtonPressed());
        h.Settle();
        Assert.Equal("detail,page:detail,guard:detail", h.Log.Take());
        Assert.Equal(0, pops);
        Assert.Equal("home,detail", h.Titles);

        h.Log.Handles.Clear();
        Assert.True(h.Page.SendBackButtonPressed());
        h.Settle();
        Assert.Equal("detail,page:detail,guard:detail", h.Log.Take());
        Assert.Equal("home", h.Titles);

        // At the root the handle can't go back: the key is passed on.
        Assert.False(h.Page.SendBackButtonPressed());
        Assert.Equal("home,page:home", h.Log.Take());
    }

    [Fact]
    public void ANavigationHostInADeselectedTab_IsNotAsked()
    {
        var log = new BackLog();
        NavigationHandle<string>? nav = null;
        var host = Show(Tabs(0, null,
            ProbePage("a", log),
            Component<BackHostApp, BackHostProps>(new(log, n => nav = n, HandleOverload: true))));
        var tabs = host.View<TabbedPage>();
        var stack = Assert.IsType<ReactorNavigationPage>(tabs.Children[1]);
        nav!.Navigate("detail");
        Settle(host, () => stack.Navigation.NavigationStack.Count == 2 && stack.Navigator.IsIdle);
        log.Take();

        Assert.False(tabs.SendBackButtonPressed());
        Assert.Equal("a", log.Take());
        Assert.Equal("detail", nav.CurrentRoute);
    }

    [Fact]
    public void TheReadmeSnippet_HandlesTheBackWhileDirty()
    {
        AsOnAndroid();
        var window = new Window { Parent = DialogTests.App };
        var platform = new FakeDialogPlatform();
        DialogQueue.For(window).Platform = platform;
        var host = new TestHost(MauiHandlers.CreateRegistry());
        var discarded = 0;
        host.Render(MauiContexts.Window.Provide(window, Component<ReadmeEditor, Action>(() => discarded++)));
        host.Scheduler.Drain();
        window.Page = host.View<ContentPage>();
        Assert.True(window.Page.SendBackButtonPressed());
        Assert.Equal("Discard changes?", Assert.IsType<AlertContent>(Assert.Single(platform.Presented).Content).Title);
        platform.Answer(true);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref discarded) == 1, 2000));
    }
}
