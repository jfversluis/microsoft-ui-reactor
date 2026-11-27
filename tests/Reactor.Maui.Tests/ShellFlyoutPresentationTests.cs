using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiShell = Microsoft.Maui.Controls.Shell;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>
/// What MAUI's flyout renderers do when told the effective behavior (spec 070 §4.16.1, C3): force the
/// presentation open under <c>Locked</c> and closed under <c>Disabled</c>, every time they are told,
/// and on Android close the drawer leaving <c>Locked</c>. Registered after Reactor's own observer, as
/// the platform's is.
/// </summary>
internal sealed class ForcingFlyoutObserver : IFlyoutBehaviorObserver
{
    private readonly MauiShell _shell;
    private readonly bool _android;
    private FlyoutBehavior? _last;

    /// <summary>
    /// Closes the flyout whenever told, whatever the behavior: not what MAUI does, but the one write
    /// the outside-change filter can't attribute to the platform (a steady <c>Flyout</c>), so it proves
    /// Reactor's echo scope on its own.
    /// </summary>
    public bool ClosesWheneverTold { get; set; }

    public ForcingFlyoutObserver(MauiShell shell, bool android)
    {
        _shell = shell;
        _android = android;
    }

    /// <summary>Like the platform's, told the current behavior when added (the shell's appear).</summary>
    public void Attach() => ((IShellController)_shell).AddFlyoutBehaviorObserver(this);

    public List<FlyoutBehavior> Told { get; } = [];

    /// <summary>True while this observer writes, so a test can tell MAUI's writes from Reactor's.</summary>
    public bool Writing { get; private set; }

    public void OnFlyoutBehaviorChanged(FlyoutBehavior behavior)
    {
        Told.Add(behavior);
        var leftLocked = _last == FlyoutBehavior.Locked && behavior == FlyoutBehavior.Flyout;
        _last = behavior;
        if (ClosesWheneverTold) Force(false);
        else if (behavior == FlyoutBehavior.Locked) Force(true);
        else if (behavior == FlyoutBehavior.Disabled) Force(false);
        else if (_android && leftLocked) Force(false);
    }

    /// <summary>A change the renderer raises itself (a user's open or close, or a forcing write).</summary>
    public void Force(bool presented)
    {
        Writing = true;
        try
        {
            ((IElementController)_shell).SetValueFromRenderer(MauiShell.FlyoutIsPresentedProperty, presented);
        }
        finally
        {
            Writing = false;
        }
    }
}

/// <summary>Spec 070 §4.16.4 item 1: the Shell's flyout presentation under each behavior.</summary>
public class ShellFlyoutPresentationTests
{
    private sealed record AppProps(FlyoutBehavior? Behavior, string Selected, bool? Presented, List<bool> Reports, Action<App> Expose);

    private sealed class App : Component<AppProps>
    {
        public Action<FlyoutBehavior?> SetBehavior = null!;
        public Action<string> SetSelected = null!;
        public Action<bool> SetExtra = null!;

        public override Element Render()
        {
            var (behavior, setBehavior) = UseState(Props.Behavior);
            var (selected, setSelected) = UseState(Props.Selected);
            var (presented, setPresented) = UseState(Props.Presented);
            var (extra, setExtra) = UseState(false);
            SetBehavior = setBehavior;
            SetSelected = setSelected;
            SetExtra = setExtra;
            Props.Expose(this);
            var shell = Shell(selected, setSelected,
                FlyoutItem("Flyout item", ShellTab("a", "A", Page("A", Label("a")))),
                TabBar(ShellTab("t", "T", Page("T", Label("t")))),
                extra ? FlyoutItem("Extra", ShellTab("x", "X", Page("X", Label("x")))) : null);
            // The callback accepts whatever it is given: a report would change the declaration.
            if (presented is { } p) shell = shell.FlyoutIsPresented(p, v => { Props.Reports.Add(v); setPresented(v); });
            return behavior is { } b ? shell.FlyoutBehavior(b) : shell;
        }
    }

    private sealed class Lab
    {
        public required TestHost Host;
        public required App App;
        public required List<bool> Reports;
        public ReactorShell Shell => Host.View<ReactorShell>();
        public ForcingFlyoutObserver? Platform;

        /// <summary>MAUI's renderer attaches: it is told the behavior, and forces what it forces.</summary>
        public ForcingFlyoutObserver Appear(bool android)
        {
            Platform = new ForcingFlyoutObserver(Shell, android);
            Platform.Attach();
            Host.Scheduler.Drain();
            return Platform;
        }

        public void Behavior(FlyoutBehavior? behavior)
        {
            App.SetBehavior(behavior);
            Host.Scheduler.Drain();
        }

        public void Select(string key)
        {
            App.SetSelected(key);
            Host.Scheduler.Drain();
        }

        /// <summary>Counts the presentation writes Reactor makes (not MAUI's) from now on.</summary>
        public Func<int> CountReactorWrites()
        {
            var writes = 0;
            Shell.PropertyChanging += (_, e) =>
            {
                if (e.PropertyName == MauiShell.FlyoutIsPresentedProperty.PropertyName && Platform?.Writing != true) writes++;
            };
            return () => writes;
        }
    }

    private static Lab Mount(string registry, FlyoutBehavior? behavior, bool? presented, string selected = "a")
    {
        var reports = new List<bool>();
        App? app = null;
        var host = new TestHost(PropertyMatrixTests.Registries.Single(r => r.Name == registry).Create());
        host.Render(Component<App, AppProps>(new(behavior, selected, presented, reports, a => app = a)));
        host.Scheduler.Drain();
        return new Lab { Host = host, App = app!, Reports = reports };
    }

    /// <summary>What the Shell holds under a behavior with a declaration: the declaration only under Flyout.</summary>
    private static bool Expected(FlyoutBehavior behavior, bool declared) => behavior switch
    {
        FlyoutBehavior.Locked => true,
        FlyoutBehavior.Disabled => false,
        _ => declared,
    };

    public static TheoryData<string, FlyoutBehavior, FlyoutBehavior, bool, bool> Transitions()
    {
        var data = new TheoryData<string, FlyoutBehavior, FlyoutBehavior, bool, bool>();
        var behaviors = new[] { FlyoutBehavior.Flyout, FlyoutBehavior.Locked, FlyoutBehavior.Disabled };
        foreach (var registry in new[] { "controls", "direct" })
            foreach (var from in behaviors)
                foreach (var to in behaviors)
                    if (from != to)
                        foreach (var declared in new[] { false, true })
                            foreach (var android in new[] { false, true })
                                data.Add(registry, from, to, declared, android);
        return data;
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public void ABehaviorChangedByARender_ReportsNothing_AndThePresentationFollowsThePolicy(
        string registry, FlyoutBehavior from, FlyoutBehavior to, bool declared, bool android)
    {
        var lab = Mount(registry, from, declared);
        var platform = lab.Appear(android);
        Assert.Equal(Expected(from, declared), lab.Shell.FlyoutIsPresented);
        Assert.Empty(lab.Reports);

        platform.Told.Clear();
        lab.Behavior(to);

        // Positive control: MAUI's renderer was told of the change inside the render, and forced it.
        Assert.Contains(to, platform.Told);
        Assert.Empty(lab.Reports);
        Assert.Equal(Expected(to, declared), lab.Shell.FlyoutIsPresented);
        Assert.Equal(declared, lab.Shell.DeclaredPresented);
    }

    [Theory]
    [InlineData("controls", false)]
    [InlineData("controls", true)]
    [InlineData("direct", false)]
    [InlineData("direct", true)]
    public void SwitchingBetweenAFlyoutItemAndATabBar_WithTheRootUnset_ReportsNothing_AndReappliesTheDeclaration(string registry, bool android)
    {
        var lab = Mount(registry, null, presented: true);
        var platform = lab.Appear(android);
        Assert.Equal(FlyoutBehavior.Flyout, lab.Shell.EffectiveBehavior);
        Assert.True(lab.Shell.FlyoutIsPresented);

        // A tab bar has no flyout: MAUI closes it, inside the selection Reactor staged.
        lab.Select("t");
        Assert.Equal(FlyoutBehavior.Disabled, lab.Shell.EffectiveBehavior);
        Assert.Contains(FlyoutBehavior.Disabled, platform.Told);
        Assert.False(lab.Shell.FlyoutIsPresented);
        Assert.Empty(lab.Reports);

        // Back on the flyout item, the declaration is applied after the staging made it a flyout again.
        var writes = lab.CountReactorWrites();
        lab.Select("a");
        Assert.Equal(FlyoutBehavior.Flyout, lab.Shell.EffectiveBehavior);
        Assert.True(lab.Shell.FlyoutIsPresented);
        Assert.Equal(1, writes());
        Assert.Empty(lab.Reports);
    }

    [Theory]
    [InlineData("controls")]
    [InlineData("direct")]
    public void APresentationChangeMauiRaisesInsideReactorsProjection_IsAnEcho_AndTheDeclarationIsReapplied(string registry)
    {
        var lab = Mount(registry, null, presented: true);
        var platform = lab.Appear(android: true);
        Assert.True(lab.Shell.FlyoutIsPresented);
        platform.ClosesWheneverTold = true;

        // A new flyout item staged, then selected: MAUI is told inside Reactor's SyncSlots, the effective
        // behavior staying Flyout throughout.
        lab.App.SetExtra(true);
        lab.Host.Scheduler.Drain();
        platform.Told.Clear();
        lab.Select("x");
        Assert.Equal("x", lab.Shell.ShownKey());
        Assert.NotEmpty(platform.Told);
        Assert.All(platform.Told, b => Assert.Equal(FlyoutBehavior.Flyout, b));
        Assert.Empty(lab.Reports);
        Assert.True(lab.Shell.FlyoutIsPresented);
    }

    [Theory]
    [InlineData("controls", FlyoutBehavior.Locked, false)]
    [InlineData("controls", FlyoutBehavior.Disabled, true)]
    [InlineData("direct", FlyoutBehavior.Locked, false)]
    [InlineData("direct", FlyoutBehavior.Disabled, true)]
    public void MauisForcingWrites_OutsideARender_UnderLockedOrDisabled_AreNotReported_NorRestored(string registry, FlyoutBehavior behavior, bool declared)
    {
        var lab = Mount(registry, behavior, declared);
        var writes = lab.CountReactorWrites();
        var platform = lab.Appear(android: true);
        Assert.Equal(!declared, lab.Shell.FlyoutIsPresented); // the appear-time forcing write

        // A page change tells the observers again; MAUI forces the presentation again.
        ((IShellController)lab.Shell).AppearanceChanged(lab.Shell.CurrentPage, false);
        platform.Force(!declared);
        lab.Host.Scheduler.Drain();

        Assert.Equal(2, platform.Told.Count); // positive control: told at appear and at the page change
        Assert.Empty(lab.Reports);
        Assert.Equal(!declared, lab.Shell.FlyoutIsPresented);
        Assert.Equal(0, writes());
    }

    [Theory]
    [InlineData("controls")]
    [InlineData("direct")]
    public void UnderFlyout_TheUsersChange_IsReportedOnce(string registry)
    {
        var lab = Mount(registry, FlyoutBehavior.Flyout, false);
        var platform = lab.Appear(android: true);
        platform.Force(true);
        lab.Host.Scheduler.Drain();
        Assert.Equal([true], lab.Reports);
        Assert.True(lab.Shell.FlyoutIsPresented);
    }

    [Theory]
    [InlineData("controls")]
    [InlineData("direct")]
    public void AnEffectiveBehaviorChangedOutsideARender_IsNotReported_AndTheDeclarationReturnsUnderFlyout(string registry)
    {
        var lab = Mount(registry, null, presented: true);
        var platform = lab.Appear(android: true);
        var page = lab.Shell.CurrentPage;
        Assert.True(lab.Shell.FlyoutIsPresented);

        // A page's own behavior (MAUI's attached value) overrides the root's: set outside Reactor.
        MauiShell.SetFlyoutBehavior(page, FlyoutBehavior.Disabled);
        lab.Host.Scheduler.Drain();
        Assert.Contains(FlyoutBehavior.Disabled, platform.Told);
        Assert.False(lab.Shell.FlyoutIsPresented);
        Assert.Empty(lab.Reports);

        MauiShell.SetFlyoutBehavior(page, FlyoutBehavior.Locked);
        lab.Host.Scheduler.Drain();
        Assert.True(lab.Shell.FlyoutIsPresented);

        // Leaving Locked, Android's drawer closes (MAUI's write); the declaration then comes back.
        page.ClearValue(MauiShell.FlyoutBehaviorProperty);
        Assert.False(lab.Shell.FlyoutIsPresented);
        lab.Host.Scheduler.Drain();
        Assert.Equal(FlyoutBehavior.Flyout, lab.Shell.EffectiveBehavior);
        Assert.True(lab.Shell.FlyoutIsPresented);
        Assert.Empty(lab.Reports);
    }

    [Theory]
    [InlineData("controls", FlyoutBehavior.Flyout, false, 2)]
    [InlineData("controls", FlyoutBehavior.Flyout, true, 0)]
    [InlineData("controls", FlyoutBehavior.Locked, false, 2)]
    [InlineData("controls", FlyoutBehavior.Locked, true, 0)]
    [InlineData("direct", FlyoutBehavior.Flyout, false, 2)]
    [InlineData("direct", FlyoutBehavior.Flyout, true, 0)]
    [InlineData("direct", FlyoutBehavior.Locked, false, 2)]
    [InlineData("direct", FlyoutBehavior.Locked, true, 0)]
    public void LeavingDisabled_OnAndroid_ReprojectsAPaneThatShouldShow_OnTheNextTurn_UnlessTheDrawerShows(string registry, FlyoutBehavior to, bool drawerShows, int expectedWrites)
    {
        var lab = Mount(registry, FlyoutBehavior.Disabled, true);
        lab.Appear(android: true);
        var deferred = new List<Action>();
        lab.Shell.ReprojectsLeavingDisabled = true;
        lab.Shell.Defer = deferred.Add;
        lab.Shell.DrawerShows = () => drawerShows;

        lab.Behavior(to);
        Assert.True(lab.Shell.FlyoutIsPresented);
        var work = Assert.Single(deferred);

        var writes = lab.CountReactorWrites();
        work();
        Assert.Equal(expectedWrites, writes());
        Assert.True(lab.Shell.FlyoutIsPresented);
        Assert.Empty(lab.Reports);

        // No other change re-projects: Locked and back, or a closed declaration leaving Disabled.
        deferred.Clear();
        lab.Behavior(FlyoutBehavior.Locked);
        lab.Behavior(FlyoutBehavior.Flyout);
        Assert.Empty(deferred);
    }

    [Theory]
    [InlineData("controls")]
    [InlineData("direct")]
    public void LeavingDisabled_WithTheFlyoutDeclaredClosed_ReprojectsNothing_ButALockedPaneIs(string registry)
    {
        var lab = Mount(registry, FlyoutBehavior.Disabled, false);
        lab.Appear(android: true);
        var deferred = new List<Action>();
        lab.Shell.ReprojectsLeavingDisabled = true;
        lab.Shell.Defer = deferred.Add;
        lab.Behavior(FlyoutBehavior.Flyout);
        Assert.Empty(deferred);
        Assert.False(lab.Shell.FlyoutIsPresented);

        // A locked pane shows whatever is declared.
        lab.Behavior(FlyoutBehavior.Disabled);
        lab.Behavior(FlyoutBehavior.Locked);
        Assert.Single(deferred);
    }
}
