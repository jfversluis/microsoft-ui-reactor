using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Handlers;
using Microsoft.UI.Reactor.Maui.Hosting;
using Microsoft.UI.Reactor.Navigation;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiShell = Microsoft.Maui.Controls.Shell;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>The state a Shell test app renders from, and what it reports.</summary>
public sealed class ShellLab
{
    public NavLog Log { get; } = new();
    public List<string> Selected { get; } = [];
    public bool Accept { get; set; } = true;
    public string InitialTab { get; set; } = "a";
    public Func<ShellLab, Element> Build { get; set; } = DefaultBuild;

    public NavigationHandle<string> A = null!;
    public NavigationHandle<string> B = null!;
    public string Tab = "";
    public Action<string> SetTab = null!;

    /// <summary>The Shell's <c>onSelected</c>: logs, and accepts unless told not to.</summary>
    public void OnSelected(string key)
    {
        Selected.Add(key);
        if (Accept) SetTab(key);
    }

    public Element Host(NavigationHandle<string> nav) => NavigationHost(nav, route => Component<LogPage, LogPageProps>(new(route, Log)));

    public static Element DefaultBuild(ShellLab lab) =>
        Shell(lab.Tab, lab.OnSelected,
            TabBar(
                ShellTab("a", "A", null, lab.Host(lab.A)),
                ShellTab("b", "B", null, lab.Host(lab.B)),
                ShellTab("c", "C", null, Page("C", Label("c")))));
}

public sealed record ShellLabProps(ShellLab Lab, int Generation);

public sealed class ShellLabApp : Component<ShellLabProps>
{
    public override Element Render()
    {
        var lab = Props.Lab;
        var (tab, setTab) = UseState(lab.InitialTab);
        lab.A = UseNavigation("a0");
        lab.B = UseNavigation("b0");
        lab.Tab = tab;
        lab.SetTab = setTab;
        return lab.Build(lab);
    }
}

// The README's Shell snippet.
public abstract record HomeRoute
{
    public sealed record List : HomeRoute;
    public sealed record Detail(int Id) : HomeRoute;
}

public sealed class ReadmeShellApp : Component
{
    public override Element Render()
    {
        var (tab, setTab) = UseState("home");
        var homeNav = UseNavigation<HomeRoute>(new HomeRoute.List());
        return Shell(tab, setTab,
            TabBar(
                ShellTab("home", "Home", null, NavigationHost(homeNav, route => route switch
                {
                    HomeRoute.Detail d => Page($"Item {d.Id}", Label($"Item {d.Id}")),
                    _ => Page("Home", Button("Open", () => homeNav.Navigate(new HomeRoute.Detail(1)))),
                })),
                ShellTab("settings", "Settings", null, Page("Settings", Label("Settings")))))
            .FlyoutBehavior(FlyoutBehavior.Disabled);
    }
}

/// <summary>Runs the dispatcher turns a user's back is watched on, when the test says so.</summary>
internal sealed class QueueTurns : IShellTurns
{
    private readonly Queue<Action> _queue = new();

    public void Next(Action work) => _queue.Enqueue(work);

    public int Pending => _queue.Count;

    /// <summary>Runs the turns queued so far (not the ones they queue).</summary>
    public void RunPending()
    {
        for (var n = _queue.Count; n > 0 && _queue.TryDequeue(out var work); n--) work();
    }
}

/// <summary>The iOS transition coordinator as the navigator sees it.</summary>
internal sealed class FakeTransitions : IShellTransitions
{
    private Action<bool>? _completed;
    private Action? _interactionCancelled;

    /// <summary>Whether a transition is running (UIKit's coordinator is non-null).</summary>
    public bool Running { get; set; }

    public int Observed { get; private set; }

    public bool TryObserve(Page departing, Action interactionCancelled, Action<bool> completed)
    {
        if (!Running) return false;
        Observed++;
        _interactionCancelled = interactionCancelled;
        _completed = completed;
        return true;
    }

    public bool InProgress(Page departing) => Running;

    /// <summary>UIKit's transition-completed callback, after the completion or rollback animation.</summary>
    public void End(bool cancelled)
    {
        // UIKit notes the cancelled interaction when the finger lifts, before the rollback finishes.
        if (cancelled) _interactionCancelled?.Invoke();
        Running = false;
        var completed = _completed;
        _completed = null;
        completed?.Invoke(cancelled);
    }
}

public class ShellTests
{
    // A page appears (and Shell raises Navigated) only in a window that belongs to an app.
    internal static readonly Application App = new();

    private sealed class Harness
    {
        public TestHost Host { get; } = new(MauiHandlers.CreateRegistry());
        public ShellLab Lab { get; }
        public List<Exception> Errors { get; } = [];
        internal QueueTurns Turns { get; } = new();
        internal FakeTransitions Transitions { get; } = new();
        public long Now;
        public Window? Window;
        private int _generation;

        public Harness(Action<ShellLab>? configure = null)
        {
            Lab = new ShellLab();
            configure?.Invoke(Lab);
            Host.Root.UnhandledException += Errors.Add;
            RenderAgain();
        }

        public void RenderAgain()
        {
            Host.Render(Component<ShellLabApp, ShellLabProps>(new(Lab, ++_generation)));
            Attach();
            Settle();
        }

        private void Attach()
        {
            if (Host.RootView is not ReactorShell shell || Window is not null) return;
            shell.Turns = Turns;
            shell.Transitions = Transitions;
            shell.Clock = () => Now;
            // Shell raises Navigated once it is in a window, and a page appears only in an app's window.
            Window = new Window(shell) { Parent = App };
        }

        internal ReactorShell Shell => Host.View<ReactorShell>();

        public ShellSection Section(string key) =>
            Shell.Items.SelectMany(i => i.Items).Single(s => ShellNatives.GetKey(s) == key);

        public ShellItem ItemOf(string key) => (ShellItem)Section(key).Parent;

        internal ShellSectionNavigator Navigator(string key) => ShellNatives.GetNavigator(Section(key))!;

        /// <summary>The section's pages, root first, by title.</summary>
        public string Pages(string key)
        {
            var section = Section(key);
            var root = section.CurrentItem?.Content as Page;
            return string.Join(",", new[] { root }.Concat(section.Stack.Skip(1)).Select(p => p?.Title));
        }

        public string? ShownKey => Shell.ShownKey();

        /// <summary>
        /// Drains posted work and the watch's turns. Each pass the page shown appears, as the platform
        /// makes it: Shell raises Navigated once the destination has appeared.
        /// </summary>
        public void Settle(int turns = 30)
        {
            for (var i = 0; i < turns; i++)
            {
                Host.Scheduler.Drain();
                Turns.RunPending();
                (Host.RootView as ReactorShell)?.CurrentPage?.SendAppearing();
            }
            Host.Scheduler.Drain();
        }

        /// <summary>A tab tap, the way the platform's tab bar proposes it.</summary>
        public bool TapTab(string key)
        {
            var section = Section(key);
            var accepted = ((IShellItemController)section.Parent).ProposeSection(section);
            Settle();
            return accepted;
        }

        /// <summary>The Android back key, through Shell's back handling.</summary>
        public bool BackKey()
        {
            var consumed = Shell.SendBackButtonPressed();
            Settle();
            return consumed;
        }

        /// <summary>The start of the iOS back swipe: the gesture's ShouldBegin offers the pop.</summary>
        public bool SwipeBegins(string key)
        {
            var section = Section(key);
            var stack = section.Stack.ToList();
            stack.RemoveAt(stack.Count - 1);
            var begins = ((IShellController)Shell).ProposeNavigation(
                ShellNavigationSource.Pop, (ShellItem)section.Parent, section, section.CurrentItem, stack, true);
            if (begins) Transitions.Running = true;
            Host.Scheduler.Drain();
            Turns.RunPending();
            return begins;
        }

        /// <summary>The user lifts their finger past the threshold: UIKit completes, and MAUI follows.</summary>
        public void SwipeCompletes(string key)
        {
            ((IShellSectionController)Section(key)).SendPopping(Task.CompletedTask);
            Transitions.End(cancelled: false);
            Settle();
        }

        /// <summary>The user lets go early: UIKit rolls back and reports a cancelled transition.</summary>
        public void SwipeCancels()
        {
            Transitions.End(cancelled: true);
            Settle();
        }
    }

    private static int Count(NavLog log, string entry) => log.Entries.Count(e => e == entry);

    // ── Building ────────────────────────────────────────────────────────────

    [Fact]
    public void TheReadmeSnippet_ShowsItsTabs_AndPushesInTheTab()
    {
        var host = new TestHost(MauiHandlers.CreateRegistry());
        host.Render(Component<ReadmeShellApp>());
        var shell = host.View<ReactorShell>();
        _ = new Window(shell) { Parent = App };
        host.Scheduler.Drain();
        shell.CurrentPage.SendAppearing();

        var section = shell.CurrentItem.CurrentItem;
        Assert.Equal("home", ShellNatives.GetKey(section));
        Assert.Equal(FlyoutBehavior.Disabled, shell.FlyoutBehavior);
        var home = Assert.IsType<ContentPage>(section.CurrentItem.Content);
        ((IButtonController)Assert.IsType<Button>(home.Content)).SendClicked();
        host.Scheduler.Drain();

        Assert.Equal("Item 1", section.Stack[^1].Title);
        Assert.Same(home, section.CurrentItem.Content);
    }

    [Fact]
    public void Build_CreatesItemsTabsAndTopTabs_WithTheirKeys()
    {
        var h = new Harness(lab => lab.Build = l => Shell(l.Tab, l.OnSelected,
            TabBar(
                ShellTab("a", "A", null, Page("A", null)),
                ShellTab("media", "Media", null,
                    ShellContent("photos", "Photos", null, Page("Photos", null)),
                    ShellContent("videos", "Videos", null, Page("Videos", null)))),
            FlyoutItem("More", null, ShellTab("more", "More", null, Page("More", null))),
            MenuItem("Help", () => l.Selected.Add("help"))) with { TabBarBackgroundColor = Colors.Red, Title = "App" });

        var shell = h.Shell;
        Assert.Equal(Colors.Red, MauiShell.GetTabBarBackgroundColor(shell));
        Assert.Equal("App", shell.Title);
        Assert.Equal(3, shell.Items.Count);
        Assert.IsType<TabBar>(shell.Items[0]);
        Assert.IsType<FlyoutItem>(shell.Items[1]);
        Assert.Equal("More", shell.Items[1].Title);
        Assert.Equal(["a", "media"], shell.Items[0].Items.Select(ShellNatives.GetKey));
        Assert.Equal(["photos", "videos"], h.Section("media").Items.Select(ShellNatives.GetKey));
        Assert.Equal("Videos", h.Section("media").Items[1].Content is Page p ? p.Title : null);
        Assert.Equal("a", h.ShownKey);

        // The menu item runs its action.
        ((IMenuItemController)ShellNatives.GetMenuItem(shell.Items[2])!).Activate();
        Assert.Contains("help", h.Lab.Selected);
        Assert.Empty(h.Errors);
    }

    [Fact]
    public void Update_AddsRemovesAndReordersTabsAndTopTabs_KeepingTheirObjects()
    {
        var tabs = new[] { "a", "b", "c" };
        var tops = new[] { "x", "y" };
        var h = new Harness(lab => lab.Build = l => Shell(l.Tab, l.OnSelected,
            TabBar([
                .. tabs.Select(k => ShellTab(k, k.ToUpperInvariant(), null, Page(k, null))),
                ShellTab("top", "Top", null, [.. tops.Select(k => ShellContent(k, k, null, Page(k, null)))]),
            ])));
        var a = h.Section("a");
        var c = h.Section("c");
        var y = h.Section("top").Items[1];

        tabs = ["c", "a", "d"];
        tops = ["y"];
        h.RenderAgain();

        var item = h.Shell.Items[0];
        Assert.Equal(["c", "a", "d", "top"], item.Items.Select(ShellNatives.GetKey));
        Assert.Same(a, h.Section("a"));
        Assert.Same(c, h.Section("c"));
        Assert.Same(y, Assert.Single(h.Section("top").Items));
        Assert.Same(a, item.CurrentItem);
        Assert.Empty(h.Errors);
    }

    [Fact]
    public void DuplicateKeys_Throw()
    {
        var h = new Harness(lab => lab.Build = l => Shell(l.Tab, l.OnSelected,
            TabBar(ShellTab("a", "A", null, Page("A", null))),
            TabBar(ShellTab("x", "X", null, ShellContent("a", "dup", null, Page("dup", null))))));

        var error = Assert.IsType<InvalidOperationException>(Assert.Single(h.Errors));
        Assert.Contains("'a'", error.Message);
    }

    // ── Selection ───────────────────────────────────────────────────────────

    [Fact]
    public void SelectionFromCode_ShowsTheTab_AtEachLevel_WithoutReportingIt()
    {
        var h = new Harness(lab => lab.Build = l => Shell(l.Tab, l.OnSelected,
            TabBar(ShellTab("a", "A", null, Page("A", null)),
                ShellTab("media", "Media", null,
                    ShellContent("photos", "Photos", null, Page("Photos", null)),
                    ShellContent("videos", "Videos", null, Page("Videos", null)))),
            FlyoutItem("More", null, ShellTab("more", "More", null, Page("More", null)))));

        h.Lab.SetTab("videos");
        h.Settle();
        Assert.Equal("videos", h.ShownKey);
        h.Lab.SetTab("more");
        h.Settle();
        Assert.Same(h.Shell.Items[1], h.Shell.CurrentItem);
        h.Lab.SetTab("a");
        h.Settle();
        Assert.Equal("a", h.ShownKey);

        Assert.Empty(h.Lab.Selected);
    }

    [Fact]
    public void UserSwitch_IsReportedOnce_FromNavigated_AndKeptWhenAccepted()
    {
        var h = new Harness();
        var reportedDuringNavigating = -1;
        h.Shell.Navigating += (_, _) => reportedDuringNavigating = h.Lab.Selected.Count;

        Assert.True(h.TapTab("b"));

        Assert.Equal(0, reportedDuringNavigating);
        Assert.Equal(["b"], h.Lab.Selected);
        Assert.Equal("b", h.Lab.Tab);
        Assert.Equal("b", h.ShownKey);
    }

    [Fact]
    public void RefusedSwitch_IsRestored_AndRapidSwitchesRestoreToTheLatestDeclaration()
    {
        var h = new Harness(lab => lab.Accept = false);

        h.TapTab("b");
        Assert.Equal(["b"], h.Lab.Selected);
        Assert.Equal("a", h.ShownKey);

        // Two taps before state settles, then the app declares a third tab meanwhile.
        var b = h.Section("b");
        var c = h.Section("c");
        ((IShellItemController)b.Parent).ProposeSection(b);
        ((IShellItemController)c.Parent).ProposeSection(c);
        h.Lab.SetTab("c");
        h.Settle();

        Assert.Equal(["b", "b", "c"], h.Lab.Selected);
        Assert.Equal("c", h.ShownKey);
    }

    [Fact]
    public void NothingSelectable_HasNoSelection_AndReportsNothing()
    {
        var h = new Harness(lab =>
        {
            lab.InitialTab = "missing";
            lab.Build = l => Shell(l.Tab, l.OnSelected, MenuItem("Help", null));
        });

        Assert.Null(h.ShownKey);
        Assert.Empty(h.Lab.Selected);
        Assert.Empty(h.Errors);

        var empty = new Harness(lab => lab.Build = l => Shell(l.Tab, l.OnSelected));
        Assert.Empty(empty.Shell.Items);
        Assert.Empty(empty.Lab.Selected);
        Assert.Empty(empty.Errors);
    }

    [Fact]
    public void UnknownKey_SelectsTheFirstTab_ReportedOncePerChangeOfWhatIsShown()
    {
        var tabs = new[] { "a", "b" };
        var h = new Harness(lab =>
        {
            lab.InitialTab = "missing";
            lab.Accept = false;
            lab.Build = l => Shell(l.Tab, l.OnSelected, TabBar([.. tabs.Select(k => ShellTab(k, k, null, Page(k, null)))]));
        });

        Assert.Equal("a", h.ShownKey);
        Assert.Equal(["a"], h.Lab.Selected);

        h.RenderAgain();
        h.RenderAgain();
        Assert.Equal(["a"], h.Lab.Selected);

        // The first tab goes: what is shown changes, so it is reported again.
        tabs = ["b"];
        h.RenderAgain();
        Assert.Equal("b", h.ShownKey);
        Assert.Equal(["a", "b"], h.Lab.Selected);
    }

    [Fact]
    public void RemovingTheSelectedTab_SelectsTheDeclaredOne_BeforeItGoes()
    {
        // The same tab elements each render, so only the tab bar re-syncs, as with memoized tabs.
        var elements = new[] { "a", "b", "c" }.ToDictionary(k => k, k => ShellTab(k, k, null, Page(k, null)));
        var tabs = new[] { "a", "b", "c" };
        var h = new Harness(lab => lab.Build = l => Shell(l.Tab, l.OnSelected, TabBar([.. tabs.Select(k => elements[k])])));
        var changes = new List<string?>();
        h.Shell.ItemsCollectionChangedProbe(changes);

        tabs = ["b", "c"];
        h.Lab.SetTab("c");
        h.Settle();

        Assert.Equal("c", h.ShownKey);
        Assert.Equal(["b", "c"], h.Shell.Items[0].Items.Select(ShellNatives.GetKey));
        // "c" was current before "a" was removed.
        Assert.Equal(["current:c", "removed:a"], changes);
        Assert.Empty(h.Lab.Selected);
    }

    // ── Stacks in tabs ──────────────────────────────────────────────────────

    [Fact]
    public void HandleNavigations_ReachTheSectionStack_WithTheRootAsTheContentsPage()
    {
        var h = new Harness();
        var root = h.Section("a").CurrentItem.Content;

        h.Lab.A.Navigate("a1");
        h.Lab.A.Navigate("a2");
        h.Settle();
        Assert.Equal("a0,a1,a2", h.Pages("a"));
        Assert.Null(h.Section("a").Stack[0]);
        Assert.Same(root, h.Section("a").CurrentItem.Content);

        h.Lab.A.GoBack();
        h.Settle();
        Assert.Equal("a0,a1", h.Pages("a"));

        h.Lab.A.Replace("r");
        h.Settle();
        Assert.Equal("a0,r", h.Pages("a"));

        h.Lab.A.Navigate("a3");
        h.Lab.A.Navigate("a4");
        h.Settle();
        h.Lab.A.PopTo(r => r == "r");
        h.Settle();
        Assert.Equal("a0,r", h.Pages("a"));

        h.Lab.A.Reset("z");
        h.Settle();
        Assert.Equal("z", h.Pages("a"));
        Assert.NotSame(root, h.Section("a").CurrentItem.Content);
        Assert.Empty(h.Errors);
    }

    [Fact]
    public void TabStacks_PersistAcrossSelection()
    {
        var h = new Harness();
        h.Lab.A.Navigate("a1");
        h.Settle();
        var a1 = h.Section("a").Stack[1];

        h.Lab.SetTab("b");
        h.Settle();
        h.Lab.B.Navigate("b1");
        h.Settle();
        // A hidden tab's handle still navigates its stack.
        h.Lab.A.Navigate("a2");
        h.Settle();
        Assert.Equal("a0,a1,a2", h.Pages("a"));

        // Coming back moves no page: the stack was kept, not rebuilt.
        var stackChanges = new List<ShellNavigationSource>();
        h.Shell.Navigating += (_, e) => stackChanges.Add(e.Source);
        h.Lab.SetTab("a");
        h.Settle();
        Assert.DoesNotContain(stackChanges, s => s is ShellNavigationSource.Push or ShellNavigationSource.Pop or ShellNavigationSource.PopToRoot);
        Assert.Equal("a0,a1,a2", h.Pages("a"));
        Assert.Same(a1, h.Section("a").Stack[1]);
        Assert.Equal("b0,b1", h.Pages("b"));
        Assert.Equal(0, h.Lab.Log.Entries.Count(e => e.Contains("navigatingFrom:Pop")));
    }

    [Fact]
    public void TwoSectionsNavigating_DoNotTakeEachOthersOperationsForTheUsers()
    {
        var h = new Harness();
        h.Lab.A.Navigate("a1");
        h.Lab.B.Navigate("b1");
        h.Settle();
        h.Lab.A.Navigate("a2");
        h.Settle();
        // The shown section and a hidden one pop at once, then push at once.
        h.Lab.A.GoBack();
        h.Lab.B.GoBack();
        h.Settle();
        h.Lab.B.Navigate("b2");
        h.Lab.A.Navigate("a3");
        h.Settle();

        Assert.Equal("a0,a1,a3", h.Pages("a"));
        Assert.Equal("b0,b2", h.Pages("b"));
        Assert.Equal("a3", h.Lab.A.CurrentRoute);
        // Only the handles' own guards ran: none for a user's back.
        Assert.Equal(["a2:navigatingFrom:Pop", "b1:navigatingFrom:Pop"],
            h.Lab.Log.Entries.Where(e => e.Contains("navigatingFrom:Pop")).Order());
        Assert.False(h.Navigator("a").IsUserBackPending);
        Assert.False(h.Navigator("b").IsUserBackPending);
        Assert.Empty(h.Errors);
    }

    // ── The user's back ─────────────────────────────────────────────────────

    [Fact]
    public void BackKey_AcceptedByTheGuard_PopsOnce_AndTheModelFollows()
    {
        var h = new Harness();
        h.Lab.A.Navigate("a1");
        h.Lab.A.Navigate("a2");
        h.Settle();
        h.Lab.Log.Entries.Clear();

        Assert.True(h.BackKey());

        Assert.Equal("a0,a1", h.Pages("a"));
        Assert.Equal("a1", h.Lab.A.CurrentRoute);
        Assert.Equal(
            "a2:navigatingFrom:Pop a1:navigatingTo:Pop a1:navigatedTo:Pop a2:navigatedFrom:Pop",
            string.Join(" ", h.Lab.Log.Entries));
        Assert.True(h.Navigator("a").IsIdle);
    }

    [Fact]
    public void BackKey_RefusedByTheGuard_MovesNothing()
    {
        var h = new Harness();
        h.Lab.A.Navigate("a1");
        h.Settle();
        var a1 = h.Section("a").Stack[1];
        h.Lab.Log.Entries.Clear();
        h.Lab.Log.RefuseFrom.Add("a1");
        var navigations = 0;
        h.Shell.Navigated += (_, _) => navigations++;

        Assert.True(h.BackKey());

        Assert.Equal(0, navigations);
        Assert.Equal("a0,a1", h.Pages("a"));
        Assert.Same(a1, h.Section("a").Stack[1]);
        Assert.Equal("a1", h.Lab.A.CurrentRoute);
        Assert.Equal(1, Count(h.Lab.Log, "a1:navigatingFrom:Pop"));
        Assert.Equal(0, Count(h.Lab.Log, "a0:navigatingTo:Pop"));
        Assert.True(h.Navigator("a").IsIdle);
    }

    [Fact]
    public void BackRefusedByTheDestination_MovesNothing()
    {
        var h = new Harness();
        h.Lab.A.Navigate("a1");
        h.Settle();
        h.Lab.Log.Entries.Clear();
        h.Lab.Log.RefuseTo.Add("a0");

        h.BackKey();

        Assert.Equal("a0,a1", h.Pages("a"));
        Assert.Equal("a1", h.Lab.A.CurrentRoute);
        Assert.Equal("a1:navigatingFrom:Pop a0:navigatingTo:Pop", string.Join(" ", h.Lab.Log.Entries));
    }

    [Fact]
    public void BackToTheRoot_IsReportedAsPopToRoot_AndHandledAsABack()
    {
        var h = new Harness();
        h.Lab.A.Navigate("a1");
        h.Settle();
        var sources = new List<ShellNavigationSource>();
        h.Shell.Navigating += (_, e) => sources.Add(e.Source);
        h.Lab.Log.Entries.Clear();

        h.BackKey();

        Assert.Equal([ShellNavigationSource.PopToRoot], sources);
        Assert.Equal("a0", h.Pages("a"));
        Assert.Equal("a0", h.Lab.A.CurrentRoute);
        Assert.Equal(1, Count(h.Lab.Log, "a1:navigatingFrom:Pop"));
    }

    [Fact]
    public void BackAtTheRoot_ReachesTheApp_WithoutAGuard()
    {
        var h = new Harness();

        Assert.False(h.BackKey());

        Assert.DoesNotContain(h.Lab.Log.Entries, e => e.Contains("navigatingFrom"));
        Assert.True(h.Navigator("a").IsIdle);
    }

    [Fact]
    public void ModalDismissal_LeavesTheModelAlone()
    {
        Action<bool>? setModal = null;
        var dismissed = new List<int>();
        var h = new Harness(lab => lab.Build = l => Shell(l.Tab, l.OnSelected,
            TabBar(ShellTab("a", "A", null, Component<ModalHost, ModalHostProps>(new(l, set => setModal = set, dismissed))))));
        h.Lab.A.Navigate("a1");
        h.Settle();
        setModal!(true);
        h.Settle();
        var section = h.Section("a");
        Assert.Single(section.Navigation.ModalStack);
        h.Lab.Log.Entries.Clear();

        // What MAUI offers when a modal goes back: a Pop while the modal is shown.
        var args = new ShellNavigatingEventArgs(h.Shell.CurrentState, "..", ShellNavigationSource.Pop, true);
        h.Shell.SendNavigatingForTest(args);
        h.Settle();

        Assert.False(args.Cancelled);
        Assert.Empty(h.Lab.Log.Entries);
        Assert.Equal("a1", h.Lab.A.CurrentRoute);
        Assert.False(h.Navigator("a").IsUserBackPending);
    }

    private sealed record ModalHostProps(ShellLab Lab, Action<Action<bool>> Report, List<int> Dismissed);

    private sealed class ModalHost : Component<ModalHostProps>
    {
        public override Element Render()
        {
            var (modal, setModal) = UseState(false);
            Props.Report(setModal);
            return NavigationHost(Props.Lab.A, route => Component<LogPage, LogPageProps>(new(route, Props.Lab.Log)), s => s with
            {
                Modals = modal ? [Page("sheet", null)] : [],
                OnModalDismissed = remaining =>
                {
                    Props.Dismissed.Add(remaining);
                    setModal(remaining > 0);
                },
            });
        }
    }

    // ── The iOS swipe's state machine ───────────────────────────────────────

    [Fact]
    public void Swipe_Completed_CommitsTheApproval_WithoutAskingTheGuardsAgain()
    {
        var h = new Harness();
        h.Lab.A.Navigate("a1");
        h.Lab.A.Navigate("a2");
        h.Settle();
        h.Lab.Log.Entries.Clear();

        Assert.True(h.SwipeBegins("a"));
        Assert.Equal("a2:navigatingFrom:Pop a1:navigatingTo:Pop", string.Join(" ", h.Lab.Log.Entries));
        Assert.True(h.Navigator("a").IsUserBackPending);
        Assert.Equal("a2", h.Lab.A.CurrentRoute);
        Assert.Equal(1, h.Transitions.Observed);

        h.SwipeCompletes("a");

        Assert.Equal("a0,a1", h.Pages("a"));
        Assert.Equal("a1", h.Lab.A.CurrentRoute);
        Assert.Equal(
            "a2:navigatingFrom:Pop a1:navigatingTo:Pop a1:navigatedTo:Pop a2:navigatedFrom:Pop",
            string.Join(" ", h.Lab.Log.Entries));
        Assert.True(h.Navigator("a").IsIdle);
    }

    [Fact]
    public void Swipe_Cancelled_KeepsThePage_AndTheModel()
    {
        var h = new Harness();
        h.Lab.A.Navigate("a1");
        h.Settle();
        var a1 = h.Section("a").Stack[1];
        h.Lab.Log.Entries.Clear();

        Assert.True(h.SwipeBegins("a"));
        h.Settle();
        Assert.True(h.Navigator("a").IsUserBackPending);

        h.SwipeCancels();

        Assert.False(h.Navigator("a").IsUserBackPending);
        Assert.Equal("a0,a1", h.Pages("a"));
        Assert.Same(a1, h.Section("a").Stack[1]);
        Assert.Equal("a1", h.Lab.A.CurrentRoute);
        Assert.Equal("a1:navigatingFrom:Pop a0:navigatingTo:Pop", string.Join(" ", h.Lab.Log.Entries));
    }

    [Fact]
    public void Swipe_RefusedByTheGuard_NeverBegins()
    {
        var h = new Harness();
        h.Lab.A.Navigate("a1");
        h.Settle();
        h.Lab.Log.RefuseFrom.Add("a1");

        Assert.False(h.SwipeBegins("a"));

        Assert.False(h.Navigator("a").IsUserBackPending);
        Assert.Equal("a0,a1", h.Pages("a"));
    }

    [Fact]
    public void AnAppNavigationDuringTheSwipe_IsHeld_AndAppliedAfterIt()
    {
        var h = new Harness();
        h.Lab.A.Navigate("a1");
        h.Settle();
        Assert.True(h.SwipeBegins("a"));

        h.Lab.A.Navigate("a2");
        h.Settle();
        Assert.Equal("a0,a1", h.Pages("a"));
        Assert.True(h.Navigator("a").IsUserBackPending);

        // Cancelled: the held push lands on the page the user kept.
        h.SwipeCancels();
        Assert.Equal("a0,a1,a2", h.Pages("a"));
        Assert.Equal("a2", h.Lab.A.CurrentRoute);

        // Completed: the approval is void (the model moved), and the stack follows the model.
        Assert.True(h.SwipeBegins("a"));
        h.Lab.A.Navigate("a3");
        h.Settle();
        h.SwipeCompletes("a");
        Assert.Equal("a3", h.Lab.A.CurrentRoute);
        Assert.Equal("a0,a1,a2,a3", h.Pages("a"));
        Assert.Empty(h.Errors);
    }

    [Fact]
    public void Swipe_WithoutSettlement_WaitsPastTheWarning_UntilNoTransitionRuns()
    {
        var h = new Harness();
        h.Lab.A.Navigate("a1");
        h.Settle();

        Assert.True(h.SwipeBegins("a"));
        // UIKit's coordinator never reports, and Shell never navigates.
        h.Now += 10_000;
        h.Settle();
        Assert.True(h.Navigator("a").IsUserBackPending);

        h.Transitions.Running = false;
        h.Settle();

        Assert.False(h.Navigator("a").IsUserBackPending);
        Assert.Equal("a0,a1", h.Pages("a"));
        Assert.Equal("a1", h.Lab.A.CurrentRoute);
    }

    [Fact]
    public void ACancelByAnotherHandler_SettlesAsCancelled_OnTheNextTurn()
    {
        var h = new Harness();
        h.Lab.A.Navigate("a1");
        h.Settle();
        var args = new ShellNavigatingEventArgs(h.Shell.CurrentState, "..", ShellNavigationSource.Pop, true);
        h.Shell.SendNavigatingForTest(args);
        Assert.True(h.Navigator("a").IsUserBackPending);

        // A handler cancels after Reactor's preflight (a deferral that ends cancelled does the same).
        args.Cancel();
        h.Turns.RunPending();

        Assert.False(h.Navigator("a").IsUserBackPending);
        Assert.Equal("a1", h.Lab.A.CurrentRoute);
    }

    [Fact]
    public void WindowSwap_WaitsForABusySection()
    {
        var h = new Harness();
        h.Lab.A.Navigate("a1");
        h.Settle();
        var errors = new List<Exception>();
        var setter = new WindowPageSetter(h.Window!, errors.Add) { WaitForNavigation = true };
        Assert.True(h.SwipeBegins("a"));

        var next = new ContentPage();
        setter.Set(next);
        Assert.True(setter.IsPending);
        Assert.Same(h.Shell, h.Window!.Page);

        h.SwipeCancels();
        Assert.False(setter.IsPending);
        Assert.Same(next, h.Window.Page);
        Assert.Empty(errors);
    }

    // ── Pages and the flyout ────────────────────────────────────────────────

    [Fact]
    public void PageShellProperties_AreApplied_AndUpdatedLive()
    {
        var search = new SearchHandler();
        var visible = true;
        var h = new Harness(lab => lab.Build = l => Shell(l.Tab, l.OnSelected,
            TabBar(ShellTab("a", "A", null, Page("A", null) with
            {
                NavBarIsVisible = visible,
                TabBarIsVisible = !visible,
                TitleView = Label(visible ? "title" : "other"),
                SearchHandler = visible ? search : null,
                PresentationMode = PresentationMode.ModalAnimated,
                BackButtonBehavior = new ShellBackButton("Back", IsEnabled: visible),
            }))));
        var page = (Page)h.Section("a").CurrentItem.Content;

        Assert.True(MauiShell.GetNavBarIsVisible(page));
        Assert.False(MauiShell.GetTabBarIsVisible(page));
        var title = Assert.IsType<Label>(MauiShell.GetTitleView(page));
        Assert.Equal("title", title.Text);
        Assert.Same(search, MauiShell.GetSearchHandler(page));
        Assert.Equal(PresentationMode.ModalAnimated, MauiShell.GetPresentationMode(page));
        Assert.Equal("Back", MauiShell.GetBackButtonBehavior(page).TextOverride);
        Assert.True(MauiShell.GetBackButtonBehavior(page).IsEnabled);

        visible = false;
        h.RenderAgain();

        Assert.Same(page, h.Section("a").CurrentItem.Content);
        Assert.False(MauiShell.GetNavBarIsVisible(page));
        Assert.True(MauiShell.GetTabBarIsVisible(page));
        Assert.Same(title, MauiShell.GetTitleView(page));
        Assert.Equal("other", title.Text);
        Assert.Null(MauiShell.GetSearchHandler(page));
        Assert.False(MauiShell.GetBackButtonBehavior(page).IsEnabled);
    }

    [Fact]
    public void Flyout_IsControlled_TheUsersChangeReported_AndItsHeaderUpdatesInPlace()
    {
        var presented = false;
        var reported = new List<bool>();
        var header = "one";
        var h = new Harness(lab => lab.Build = l => Shell(l.Tab, l.OnSelected,
                FlyoutItem("A", null, ShellTab("a", "A", null, Page("A", null))))
            .FlyoutIsPresented(presented, reported.Add)
            .FlyoutHeader(Label(header))
            .FlyoutFooter(Label("footer"))
            .FlyoutContent(header == "two" ? Label("custom") : null));
        var shell = h.Shell;
        var host = Assert.IsType<ContentView>(shell.FlyoutHeader);
        var label = Assert.IsType<Label>(host.Content);
        Assert.Equal("footer", Assert.IsType<Label>(Assert.IsType<ContentView>(shell.FlyoutFooter).Content).Text);
        Assert.False(shell.IsSet(MauiShell.FlyoutContentProperty));

        // The user opens the flyout: reported; the app doesn't accept, so it closes again.
        shell.FlyoutIsPresented = true;
        h.Settle();
        Assert.Equal([true], reported);
        Assert.False(shell.FlyoutIsPresented);

        presented = true;
        header = "two";
        h.RenderAgain();
        Assert.True(shell.FlyoutIsPresented);
        Assert.Equal([true], reported);
        Assert.Same(host, shell.FlyoutHeader);
        Assert.Same(label, host.Content);
        Assert.Equal("two", label.Text);
        Assert.Equal("custom", Assert.IsType<Label>(Assert.IsType<ContentView>(shell.FlyoutContent).Content).Text);
    }
}

internal static class ShellTestExtensions
{
    /// <summary>Raises Shell's Navigating the way its navigation manager does.</summary>
    public static void SendNavigatingForTest(this MauiShell shell, ShellNavigatingEventArgs args) =>
        typeof(MauiShell).GetMethod("OnNavigating", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic)!
            .Invoke(shell, [args]);

    /// <summary>Records, for the Shell's first item, when its current tab changes and when a tab is removed.</summary>
    public static void ItemsCollectionChangedProbe(this MauiShell shell, List<string?> log)
    {
        var item = shell.Items[0];
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == ShellItem.CurrentItemProperty.PropertyName) log.Add($"current:{ShellNatives.GetKey(item.CurrentItem)}");
        };
        ((global::System.Collections.Specialized.INotifyCollectionChanged)item.Items).CollectionChanged += (_, e) =>
        {
            foreach (var removed in e.OldItems?.Cast<ShellSection>() ?? []) log.Add($"removed:{ShellNatives.GetKey(removed)}");
        };
    }
}
