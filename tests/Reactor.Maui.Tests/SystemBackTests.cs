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

// Builds a Shell: shares ShellTests' collection (MAUI's Routing is process-wide).
[Collection("MauiShell")]
public class SystemBackTests
{
    private sealed class Shown
    {
        public TestHost Host { get; } = new(MauiHandlers.CreateRegistry());
        public Window Window { get; } = new() { Parent = DialogTests.App };

        public Shown(Element element)
        {
            Render(element);
            Window.Page = (Page)Host.RootView!;
        }

        public void Render(Element element)
        {
            Host.Render(MauiContexts.Window.Provide(Window, element));
            Host.Scheduler.Drain();
        }

        public T View<T>() => Host.View<T>();

        /// <summary>The Android back key: Reactor's callback (the handlers), then MAUI's window back routing.</summary>
        public bool BackKey() => SystemBack.HandleBackKey(Window, () => ((IWindow)Window).BackButtonClicked());

        public void Settle(Func<bool> done)
        {
            for (var i = 0; i < 200 && !done(); i++)
            {
                Host.Scheduler.Drain();
                Thread.Sleep(5);
            }
            Host.Scheduler.Drain();
        }
    }

    private static Element Probe(string name, BackLog log, Element? child = null) =>
        Component<BackProbe, BackProbeProps>(new(name, log, child));

    private static Element ProbePage(string name, BackLog log) => Page(name, Probe(name, log));

    [Fact]
    public void OnlyTheVisiblePagesHandlersAreAsked_DeepestFirst_ThenTheMostRecentlyMounted_EachOnce()
    {
        var log = new BackLog();
        var shown = new Shown(Probe("root", log, Tabs(1, null,
            ProbePage("tab-a", log),
            NavigationStack([
                ProbePage("below", log),
                Page("top", VStack(Probe("top-first", log), Probe("top-second", log, Probe("top-deep", log)))).WithKey("top"),
            ]))));
        var tabs = shown.View<TabbedPage>();
        var stack = Assert.IsType<ReactorNavigationPage>(tabs.Children[1]);
        shown.Settle(() => stack.Navigation.NavigationStack.Count == 2);
        Assert.Equal(2, stack.Navigation.NavigationStack.Count);

        log.Handles.Add("root");
        Assert.True(shown.BackKey());
        // The deselected tab and the page below the top never intercept; the root-level handler is asked last.
        Assert.Equal("top-deep,top-second,top-first,root", log.Take());
        Assert.Equal(2, stack.Navigation.NavigationStack.Count);
    }

    [Fact]
    public void AHandlerThatHandlesTheBack_StopsIt_AndOtherwiseMAUIGetsIt()
    {
        var log = new BackLog();
        var shown = new Shown(NavigationStack([ProbePage("below", log), ProbePage("top", log).WithKey("top")]));
        var stack = shown.View<ReactorNavigationPage>();
        shown.Settle(() => stack.Navigation.NavigationStack.Count == 2);
        log.Handles.Add("top");
        Assert.True(shown.BackKey());
        Assert.Equal("top", log.Take());
        Assert.Equal(2, stack.Navigation.NavigationStack.Count);

        // Unhandled, the back goes on to MAUI, whose navigation page pops (each handler asked once).
        log.Handles.Clear();
        Assert.True(shown.BackKey());
        Assert.Equal("top", log.Take());
        Assert.Single(stack.Navigation.NavigationStack);
    }

    [Fact]
    public void AFlyout_AsksItsDetail_AndItsFlyoutWhilePresented()
    {
        var log = new BackLog();
        Element Tree(bool presented) => Flyout(ProbePage("menu", log), ProbePage("detail", log), presented);
        var shown = new Shown(Tree(false));
        Assert.False(shown.BackKey());
        Assert.Equal("detail", log.Take());

        shown.Render(Tree(true));
        shown.BackKey();
        Assert.Equal("detail,menu", log.Take());
    }

    [Fact]
    public void AShell_AsksTheSelectedTabsPage_NotADeselectedTabsComponents()
    {
        var log = new BackLog();
        var shown = new Shown(Shell("a", null,
            TabBar(
                ShellTab("a", "A", ProbePage("a", log)),
                ShellTab("b", "B", Probe("b-holder", log, ProbePage("b", log))))));
        Assert.False(shown.BackKey());
        Assert.Equal("a", log.Take());
    }

    [Fact]
    public void ARootPage_PassesAnUnhandledBackOn()
    {
        var log = new BackLog();
        var shown = new Shown(Probe("root", log, ProbePage("page", log)));
        Assert.False(shown.BackKey());
        Assert.Equal("page,root", log.Take());
        log.Handles.Add("root");
        Assert.True(shown.BackKey());
        Assert.Equal("page,root", log.Take());
    }

    [Fact]
    public void AModal_IsTheVisiblePage()
    {
        var log = new BackLog();
        var shown = new Shown(Probe("root", log, NavigationStack([ProbePage("under", log)]) with { Modals = [ProbePage("modal", log)] }));
        shown.Settle(() => shown.Window.Navigation.ModalStack.Count == 1);
        Assert.Single(shown.Window.Navigation.ModalStack);
        log.Handles.Add("modal");
        Assert.True(shown.BackKey());
        Assert.Equal("modal", log.Take());
    }

    [Fact]
    public void AnUnmountedHandler_IsNotAsked()
    {
        var log = new BackLog();
        var shown = new Shown(Page("page", VStack(Probe("kept", log), Probe("removed", log))));
        shown.Render(Page("page", VStack(Probe("kept", log))));
        shown.BackKey();
        Assert.Equal("kept", log.Take());
    }

    [Fact]
    public void AThrowingHandler_ConsumesTheBack_AndReportsToItsBoundary()
    {
        var log = new BackLog();
        log.Throws.Add("throws");
        var shown = new Shown(Page("page", ErrorBoundary(Probe("throws", log), e => Label($"caught: {e.Message}"))));
        var page = shown.View<ContentPage>();
        Assert.True(shown.BackKey());
        shown.Host.Scheduler.Drain();
        Assert.Equal("caught: back boom throws", Assert.IsType<Label>(page.Content).Text);
    }

    [Fact]
    public void APagesOwnBackHandling_NeverAsksTheHandlers()
    {
        // MAUI calls SendBackButtonPressed for Shell's iOS navigation-bar back too; Reactor's pages don't
        // override it for the handlers (only the Android back callback asks them), so it passes on.
        var log = new BackLog();
        log.Handles.Add("page");
        var shown = new Shown(ProbePage("page", log));
        Assert.False(shown.View<ContentPage>().SendBackButtonPressed());
        Assert.Empty(log.Asked);
        Assert.False(SystemBack.HasEnabledCallback(shown.Window));
    }

    [Fact]
    public void AHandlerInAListCell_IsAskedOnItsPage_NotInADeselectedTab()
    {
        var log = new BackLog();
        Element Cells(string tab) => CollectionView(["x"], static x => x, x => Probe($"{tab}-cell", log));
        var shown = new Shown(Tabs(0, null, Page("a", Cells("a")), Page("b", Cells("b"))));
        var tabs = shown.View<TabbedPage>();
        foreach (var page in tabs.Children)
        {
            var view = (CollectionView)((ContentPage)page).Content;
            // What the platform does for a visible item: create a cell and bind it.
            var cell = (ContentView)view.ItemTemplate.CreateContent();
            cell.BindingContext = ((System.Collections.IList)view.ItemsSource)[0];
        }
        shown.Host.Scheduler.Drain();
        shown.BackKey();
        Assert.Equal("a-cell", log.Take());
    }

    [Fact]
    public void AReactorPage_InAMauiNavigationPage_IsAskedWhileOnTop()
    {
        var log = new BackLog();
        var hosted = new ReactorPage(Probe("hosted", log));
        var window = new Window { Parent = DialogTests.App };
        var navigation = new NavigationPage(hosted);
        window.Page = navigation;
        log.Handles.Add("hosted");
        Assert.True(SystemBack.HandleBackKey(window, () => false));
        Assert.Equal("hosted", log.Take());

        // Covered by another page, its tree's handlers are below the visible page.
        _ = navigation.PushAsync(new ContentPage(), false);
        Assert.False(SystemBack.HandleBackKey(window, () => false));
        Assert.Empty(log.Asked);
    }

    [Fact]
    public void TwoTrees_EachHandlerAskedOnce_TheCoveredTreesPagesExcluded()
    {
        var log = new BackLog();
        var shown = new Shown(Probe("outer-root", log, ProbePage("outer-page", log)));
        var hosted = new ReactorPage(Probe("inner", log));
        _ = shown.Window.Navigation.PushModalAsync(hosted, false);
        Assert.False(SystemBack.HandleBackKey(shown.Window, () => false));
        // The modal tree's handler and the outer tree's root-level one, once each; the covered page's not.
        Assert.Equal(["inner", "outer-root"], log.Asked.Order().ToList());
    }

    private sealed class HostHarness
    {
        public Shown Shown { get; }
        public BackLog Log { get; } = new();
        public NavigationHandle<string> Nav = null!;

        public HostHarness(bool handleOverload)
        {
            Shown = new Shown(Component<BackHostApp, BackHostProps>(new(Log, n => Nav = n, handleOverload)));
            Settle();
        }

        public ReactorNavigationPage Page => Shown.View<ReactorNavigationPage>();
        public string Titles => string.Join(",", Page.Navigation.NavigationStack.Select(p => p.Title));

        public void Settle() => Shown.Settle(() => Page.Navigator.IsIdle);
    }

    [Fact]
    public void AnUnhandledBack_FallsThroughToTheNavigationHost_WhoseGuardRunsOnce()
    {
        var h = new HostHarness(handleOverload: false);
        h.Nav.Navigate("detail");
        h.Settle();
        Assert.Equal("home,detail", h.Titles);
        h.Log.Take();

        Assert.True(h.Shown.BackKey());
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

        Assert.True(h.Shown.BackKey());
        h.Settle();
        Assert.Equal("detail,page:detail,guard:detail", h.Log.Take());
        Assert.Equal(0, pops);
        Assert.Equal("home,detail", h.Titles);

        h.Log.Handles.Clear();
        Assert.True(h.Shown.BackKey());
        h.Settle();
        Assert.Equal("detail,page:detail,guard:detail", h.Log.Take());
        Assert.Equal("home", h.Titles);

        // At the root the handle can't go back: the key is passed on.
        Assert.False(h.Shown.BackKey());
        Assert.Equal("home,page:home", h.Log.Take());
    }

    [Fact]
    public void ANavigationHostInADeselectedTab_IsNotAsked()
    {
        var log = new BackLog();
        NavigationHandle<string>? nav = null;
        var shown = new Shown(Tabs(0, null,
            ProbePage("a", log),
            Component<BackHostApp, BackHostProps>(new(log, n => nav = n, HandleOverload: true))));
        var tabs = shown.View<TabbedPage>();
        var stack = Assert.IsType<ReactorNavigationPage>(tabs.Children[1]);
        nav!.Navigate("detail");
        shown.Settle(() => stack.Navigation.NavigationStack.Count == 2 && stack.Navigator.IsIdle);
        log.Take();

        Assert.False(shown.BackKey());
        Assert.Equal("a", log.Take());
        Assert.Equal("detail", nav.CurrentRoute);
    }

    [Fact]
    public void TheReadmeSnippet_HandlesTheBackWhileDirty()
    {
        var window = new Window { Parent = DialogTests.App };
        var platform = new FakeDialogPlatform();
        DialogQueue.For(window).Platform = platform;
        var host = new TestHost(MauiHandlers.CreateRegistry());
        var discarded = 0;
        host.Render(MauiContexts.Window.Provide(window, Component<ReadmeEditor, Action>(() => discarded++)));
        host.Scheduler.Drain();
        window.Page = host.View<ContentPage>();
        Assert.True(SystemBack.HandleBackKey(window, () => false));
        Assert.Equal("Discard changes?", Assert.IsType<AlertContent>(Assert.Single(platform.Presented).Content).Title);
        platform.Answer(true);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref discarded) == 1, 2000));
    }
}
