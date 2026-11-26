using System.Reflection;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>
/// A handler for a flyout page that routes property updates through MAUI's own mapper, so Reactor's
/// wrapper around the FlyoutBehavior mapping runs as MAUI calls it, and records what was updated.
/// </summary>
internal sealed class StubFlyoutHandler : IFlyoutViewHandler
{
    public List<string> Updates { get; } = [];
    public IFlyoutView? VirtualView { get; private set; }
    IFlyoutView IFlyoutViewHandler.VirtualView => VirtualView!;
    IView? IViewHandler.VirtualView => VirtualView;
    IElement? IElementHandler.VirtualView => VirtualView;
    public object PlatformView { get; } = new();
    object IFlyoutViewHandler.PlatformView => PlatformView;
    object? IElementHandler.PlatformView => PlatformView;
    public IMauiContext? MauiContext { get; private set; }
    public bool HasContainer { get; set; }
    public object? ContainerView => null;

    public void SetMauiContext(IMauiContext mauiContext) => MauiContext = mauiContext;
    public void SetVirtualView(IElement view) => VirtualView = (IFlyoutView)view;

    public void UpdateValue(string property)
    {
        Updates.Add(property);
        // What Controls' remapping does when it is registered (FlyoutPage.Mapper: the layout behavior
        // updates the native mode).
        if (property == nameof(FlyoutPage.FlyoutLayoutBehavior) && FlyoutViewHandler.Mapper.GetProperty(property) is null)
            UpdateValue(nameof(IFlyoutView.FlyoutBehavior));
        else if (VirtualView is not null)
            FlyoutViewHandler.Mapper.UpdateProperty(this, VirtualView, property);
    }

    public void Invoke(string command, object? args = null) { }
    public void DisconnectHandler() => VirtualView = null;
    public Size GetDesiredSize(double widthConstraint, double heightConstraint) => Size.Zero;
    public void PlatformArrange(Rect frame) { }
}

/// <summary>A display whose orientation a test sets: what MAUI's split mode reads on a tablet.</summary>
internal sealed class FakeDisplay : IDeviceDisplay
{
    public DisplayOrientation Orientation { get; set; }
    public bool KeepScreenOn { get; set; }
    public DisplayInfo MainDisplayInfo => new(1000, 800, 1, Orientation, DisplayRotation.Rotation0);
    public event EventHandler<DisplayInfoChangedEventArgs>? MainDisplayInfoChanged { add { } remove { } }

    /// <summary>Installs the display through Essentials' internal seam until disposed.</summary>
    public static IDisposable Install(FakeDisplay display)
    {
        var setCurrent = typeof(DeviceDisplay).GetMethod("SetCurrent", BindingFlags.NonPublic | BindingFlags.Static)!;
        setCurrent.Invoke(null, [display]);
        return new Restore(() => setCurrent.Invoke(null, [null]));
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}

[CollectionDefinition(nameof(FlyoutSplitTests), DisableParallelization = true)]
public sealed class FlyoutSplitCollection;

/// <summary>
/// The flyout's split rule (spec 070 §4.14.3). Headless MAUI's idiom is Unknown, not a phone, so
/// <c>Split</c> really splits here: assigning it forces IsPresented true and a false write throws.
/// </summary>
[Collection(nameof(FlyoutSplitTests))]
public class FlyoutSplitTests
{
    public static TheoryData<string> Registries() => ["controls", "direct"];

    private static TestHost Host(string registry) =>
        new(PropertyMatrixTests.Registries.Single(r => r.Name == registry).Create());

    private static FlyoutElement Tree(bool presented, Action<bool> report, FlyoutLayoutBehavior? behavior) =>
        Flyout(Page("Menu", Label("menu")), Page("Detail", Label("detail")), presented, report) with { LayoutBehavior = behavior };

    /// <summary>Counts IsPresented writes (PropertyChanging) from now on.</summary>
    private static Func<int> CountWrites(BindableObject view)
    {
        var writes = 0;
        view.PropertyChanging += (_, e) => { if (e.PropertyName == nameof(FlyoutPage.IsPresented)) writes++; };
        return () => writes;
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void SplitAtMount_KeepsMauisPresentedFlyout_ReportsNothing_AndNeverWritesFalse(string registry)
    {
        var reports = new List<bool>();
        var host = Host(registry).Render(Tree(false, reports.Add, FlyoutLayoutBehavior.Split));
        var flyout = host.View<ReactorFlyoutPage>();
        host.Scheduler.Drain();

        // The seed: MAUI forced the flyout presented when the behavior was assigned.
        Assert.True(flyout.IsSplit);
        Assert.True(flyout.IsPresented);
        Assert.Empty(reports);

        // The page stays usable: a later declaration still writes (not stuck by a throw).
        var writes = CountWrites(flyout);
        host.Render(Tree(false, reports.Add, FlyoutLayoutBehavior.Split));
        Assert.Equal(0, writes());
        Assert.Empty(reports);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void WhileSplit_DeclaredChanges_WriteNothing(string registry)
    {
        var reports = new List<bool>();
        var host = Host(registry).Render(Tree(true, reports.Add, FlyoutLayoutBehavior.Split));
        var flyout = host.View<ReactorFlyoutPage>();
        Assert.True(flyout.IsPresented);
        var writes = CountWrites(flyout);

        host.Render(Tree(false, reports.Add, FlyoutLayoutBehavior.Split));
        host.Render(Tree(true, reports.Add, FlyoutLayoutBehavior.Split));
        host.Render(Tree(false, reports.Add, FlyoutLayoutBehavior.Split));
        host.Scheduler.Drain();

        Assert.Equal(0, writes());
        Assert.True(flyout.IsPresented);
        Assert.Empty(reports);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void LeavingAndEnteringSplit_ByABehaviorChange_AppliesTheDeclaration_ReportingNothing(string registry)
    {
        var reports = new List<bool>();
        var host = Host(registry).Render(Tree(false, reports.Add, FlyoutLayoutBehavior.Split));
        var flyout = host.View<ReactorFlyoutPage>();
        Assert.True(flyout.IsPresented);

        host.Render(Tree(false, reports.Add, FlyoutLayoutBehavior.Popover));
        Assert.False(flyout.IsSplit);
        Assert.False(flyout.IsPresented);

        host.Render(Tree(true, reports.Add, FlyoutLayoutBehavior.Split));
        Assert.True(flyout.IsPresented);

        host.Render(Tree(true, reports.Add, FlyoutLayoutBehavior.Popover));
        Assert.True(flyout.IsPresented);
        host.Scheduler.Drain();
        Assert.Empty(reports);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void MausEnforcementWhileSplit_OutsideAnyWrite_IsNotReported(string registry)
    {
        // A flyout that enters split mode while closed (a tablet turned to landscape): MAUI then forces it
        // presented when the page next appears (OnAppearing calls UpdateFlyoutLayoutBehavior), outside any
        // write of Reactor's.
        var display = new FakeDisplay { Orientation = DisplayOrientation.Portrait };
        using var installed = FakeDisplay.Install(display);
        var reports = new List<bool>();
        var host = Host(registry).Render(Tree(false, reports.Add, FlyoutLayoutBehavior.SplitOnLandscape));
        var flyout = host.View<ReactorFlyoutPage>();
        Assert.False(flyout.IsSplit);
        Assert.False(flyout.IsPresented);

        display.Orientation = DisplayOrientation.Landscape;
        Assert.True(flyout.IsSplit, "split after turning");
        flyout.UpdateFlyoutLayoutBehavior();
        host.Scheduler.Drain();

        Assert.True(flyout.IsPresented, "presented after appearing"); // positive control: MAUI's enforcement ran
        Assert.Empty(reports);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void NotSplit_InputIsReportedOnce_RefusedRestored_DeclaredWritesUnreported_NothingAfterUnmount(string registry)
    {
        var reports = new List<bool>();
        var host = Host(registry).Render(Tree(false, reports.Add, FlyoutLayoutBehavior.Popover));
        var flyout = host.View<ReactorFlyoutPage>();
        Assert.False(flyout.IsSplit);

        flyout.IsPresented = true; // the user opens it; the app doesn't accept
        Assert.Equal([true], reports);
        host.Scheduler.Drain();
        Assert.False(flyout.IsPresented);

        reports.Clear();
        host.Render(Tree(true, reports.Add, FlyoutLayoutBehavior.Popover));
        Assert.True(flyout.IsPresented);
        host.Scheduler.Drain();
        Assert.Empty(reports);

        host.Root.Unmount();
        flyout.IsPresented = false;
        host.Scheduler.Drain();
        Assert.Empty(reports);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void LeavingSplitByADisplayChange_TheForcedCloseInsideTheMapping_IsNotInput(string registry) =>
        LeaveSplitByDisplayChange(registry, closeInsideTheMapping: true);

    [Theory]
    [MemberData(nameof(Registries))]
    public void LeavingSplitByADisplayChange_ACloseThePlatformDefers_IsNotInputEither(string registry) =>
        LeaveSplitByDisplayChange(registry, closeInsideTheMapping: false);

    private static void LeaveSplitByDisplayChange(string registry, bool closeInsideTheMapping)
    {
        var display = new FakeDisplay { Orientation = DisplayOrientation.Landscape };
        using var installed = FakeDisplay.Install(display);
        var reports = new List<bool>();
        var host = Host(registry).Render(Tree(true, reports.Add, FlyoutLayoutBehavior.SplitOnLandscape));
        var flyout = host.View<ReactorFlyoutPage>();
        var handler = new StubFlyoutHandler();
        flyout.Handler = handler;
        host.Scheduler.Drain();
        Assert.True(flyout.IsSplit);
        Assert.True(flyout.IsPresented);

        // What FlyoutPage.OnMainDisplayInfoChanged does; Android's drawer then closes as it leaves Locked.
        display.Orientation = DisplayOrientation.Portrait;
        if (closeInsideTheMapping)
        {
            flyout.RunModeTransition(() =>
            {
                FlyoutViewHandler.Mapper.UpdateProperty(handler, flyout, nameof(IFlyoutView.FlyoutBehavior));
                flyout.IsPresented = false;
            });
        }
        else
        {
            handler.UpdateValue(nameof(IFlyoutView.FlyoutBehavior));
            flyout.IsPresented = false; // the platform's close, after the mapping, before the scheduler runs
        }
        Assert.False(flyout.IsSplit);
        Assert.False(flyout.IsPresented); // positive control: the close happened
        Assert.Empty(reports);

        host.Scheduler.Drain();
        Assert.Empty(reports);
        Assert.True(flyout.IsPresented); // the declaration, applied again
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void AfterAModeChange_AnEqualValue_IsProjectedToThePlatformAgain(string registry)
    {
        var reports = new List<bool>();
        var host = Host(registry).Render(Tree(true, reports.Add, FlyoutLayoutBehavior.Popover));
        var flyout = host.View<ReactorFlyoutPage>();
        var handler = new StubFlyoutHandler();
        flyout.Handler = handler;
        host.Scheduler.Drain();

        // Into split and out again with the same declaration: the virtual value never changes, while the
        // platform's own state may have (an iOS pan that finished after split began).
        host.Render(Tree(true, reports.Add, FlyoutLayoutBehavior.Split));
        host.Scheduler.Drain();
        handler.Updates.Clear();
        host.Render(Tree(true, reports.Add, FlyoutLayoutBehavior.Popover));
        Assert.True(handler.Updates.Contains(nameof(IFlyoutView.FlyoutBehavior)), "updates: " + string.Join(",", handler.Updates)); // positive control: a mode change
        handler.Updates.Clear();
        host.Scheduler.Drain();

        Assert.True(flyout.IsPresented);
        Assert.Equal([nameof(IFlyoutView.IsPresented)], handler.Updates);
        Assert.Empty(reports);
    }

    [Fact]
    public void TheModeWrapper_IsInMausMapper_ForReactorsFlyoutsOnly()
    {
        var page = new ReactorFlyoutPage { Flyout = new ContentPage { Title = "m" }, Detail = new ContentPage() };
        var transitions = 0;
        page.ModeTransitioned = () => transitions++;
        var handler = new StubFlyoutHandler();
        page.Handler = handler;

        FlyoutViewHandler.Mapper.UpdateProperty(handler, page, nameof(IFlyoutView.FlyoutBehavior));
        Assert.True(page.TransitionPending);
        Assert.Equal(1, transitions);

        var plain = new FlyoutPage { Flyout = new ContentPage { Title = "m" }, Detail = new ContentPage() };
        var plainHandler = new StubFlyoutHandler();
        plain.Handler = plainHandler;
        FlyoutViewHandler.Mapper.UpdateProperty(plainHandler, plain, nameof(IFlyoutView.FlyoutBehavior));
        Assert.Equal(1, transitions);
    }
}
