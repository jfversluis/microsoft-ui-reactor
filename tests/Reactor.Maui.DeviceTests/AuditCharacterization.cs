using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui;
using static Microsoft.UI.Reactor.Maui.Factories;
using static Reactor.Maui.DeviceTests.FixtureContext;

namespace Reactor.Maui.DeviceTests;

// Spec 070 §4.14 (4c-3b): characterizations of plain MAUI and of today's Reactor before the 4c-3b
// design is fixed. They log what they measure and assert only that the measurement ran.
public static partial class Fixtures
{
    private static IEnumerable<Fixture> AuditCharacterizationFixtures() =>
    [
        new("Characterize_FlyoutPage_LayoutBehaviors", FlyoutLayoutBehaviors),
        new("Characterize_FlyoutPage_PlatformToggle", FlyoutPlatformToggle),
        new("Characterize_FlyoutPage_WindowShape", FlyoutWindowShape),
        .. new[] { FlyoutLayoutBehavior.SplitOnLandscape, FlyoutLayoutBehavior.Default, FlyoutLayoutBehavior.SplitOnPortrait, FlyoutLayoutBehavior.Split, FlyoutLayoutBehavior.Popover }
            .Select(behavior => new Fixture($"Characterize_FlyoutPage_Rotation_{behavior}", c => FlyoutRotation(c, behavior))),
        .. new[] { FlyoutLayoutBehavior.SplitOnLandscape, FlyoutLayoutBehavior.Default }
            .Select(behavior => new Fixture($"Characterize_FlyoutElement_Rotation_{behavior}", c => FlyoutElementRotation(c, behavior))),
        new("Characterize_Orientation_DeviceKvc", OrientationDeviceKvc),
        new("Characterize_FlyoutPage_BehaviorChange", FlyoutBehaviorChange),
        new("Characterize_FlyoutElement_Today", FlyoutElementToday),
        new("Characterize_ToolbarItem_StaleInvocation", ToolbarStaleInvocation),
    ];

    /// <summary>Records IsPresentedChanged with the value, the stage, and whether a write of ours was running.</summary>
    private sealed class FlyoutLog
    {
        public readonly List<string> Events = [];
        public string Stage = "create";
        public bool Writing;

        public void Attach(FlyoutPage page) =>
            page.IsPresentedChanged += (_, _) => Events.Add($"{Stage}:{(Writing ? "in-write" : "outside")}:{page.IsPresented}");

        public string Write(FlyoutPage page, bool value)
        {
            Writing = true;
            try
            {
                page.IsPresented = value;
                return "ok";
            }
            catch (Exception ex)
            {
                return $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                Writing = false;
            }
        }
    }

    private static FlyoutPage PlainFlyout(FlyoutLayoutBehavior behavior) => new()
    {
        FlyoutLayoutBehavior = behavior,
        Flyout = new ContentPage { Title = "Menu", Content = new Label { Text = "menu", AutomationId = "flyout-content" } },
        Detail = new NavigationPage(new ContentPage { Title = "Detail", Content = new Label { Text = "detail", AutomationId = "detail-content" } }),
    };

    private static string SplitState(FlyoutPage page)
    {
        var controller = (IFlyoutPageController)page;
        var window = page.Window;
        return $"split {controller.ShouldShowSplitMode}, canChange {controller.CanChangeIsPresented}, behavior {((IFlyoutView)page).FlyoutBehavior}, " +
            $"presented {page.IsPresented}, window {window?.Width:0}x{window?.Height:0}";
    }

    /// <summary>
    /// Each layout behavior on this device: what MAUI sets itself while the page appears, what a write of
    /// false and of true does (inside the write, late, or an exception), and the flyout's on-screen state.
    /// </summary>
    private static async Task FlyoutLayoutBehaviors(FixtureContext c)
    {
        Console.WriteLine($"# flyout: idiom {DeviceInfo.Idiom}, display {DeviceDisplay.MainDisplayInfo.Orientation}");
        foreach (var behavior in Enum.GetValues<FlyoutLayoutBehavior>())
        {
            var page = PlainFlyout(behavior);
            var log = new FlyoutLog();
            log.Attach(page);
            log.Stage = "appear";
            c.Pages.Set(page);
            await c.WaitFor(() => page.IsLoaded, $"the {behavior} flyout page to load");
            await c.Settle();
            var appeared = SplitState(page);
            var flyoutShown = FlyoutShown(page);

            log.Stage = "false";
            var writeFalse = log.Write(page, false);
            await c.Settle();
            var afterFalse = $"{page.IsPresented}, shown {FlyoutShown(page)}";
            var probeClosed = FlyoutShown(page);

            log.Stage = "true";
            var writeTrue = log.Write(page, true);
            await c.Settle();
            var afterTrue = $"{page.IsPresented}, shown {FlyoutShown(page)}";
            var probeOpen = FlyoutShown(page);

            // The probe's positive control: where the flyout can be toggled, it must tell open from closed;
            // where it is split, it must see the flyout throughout.
            if (((IFlyoutPageController)page).ShouldShowSplitMode)
                Check(probeClosed.StartsWith("yes") && probeOpen.StartsWith("yes"), $"{behavior}: a split flyout is seen as shown ({probeClosed}; {probeOpen})");
            else
                Check(probeOpen.StartsWith("yes") && probeClosed.StartsWith("no"), $"{behavior}: the probe tells open ({probeOpen}) from closed ({probeClosed})");

            log.Stage = "false-again";
            var writeFalseAgain = log.Write(page, false);
            await c.Settle();

            Console.WriteLine($"# flyout {behavior}: appeared [{appeared}, shown {flyoutShown}]; write false -> {writeFalse} [{afterFalse}]; " +
                $"write true -> {writeTrue} [{afterTrue}]; write false again -> {writeFalseAgain} [{page.IsPresented}, shown {FlyoutShown(page)}]; " +
                $"events [{string.Join(" ", log.Events)}]");
            c.Pages.Set(new ContentPage());
            await c.WaitFor(() => !page.IsLoaded, "the flyout page to unload");
        }
    }

    /// <summary>
    /// Whether the flyout's content is on screen. Android: realized, shown, with its frame inside the window.
    /// iOS and Mac Catalyst: the window's hit test at the content's center lands in the flyout page, since on
    /// an iPhone the flyout's view stays at x 0 and the detail slides over it.
    /// </summary>
    private static string FlyoutShown(FlyoutPage page)
    {
        if (page.Flyout?.Handler?.PlatformView is null) return "unrealized";
        var content = page.Flyout is ContentPage { Content: { } view } ? view : null;
        if (content?.Handler?.PlatformView is null) return "content unrealized";
        try
        {
            var frame = Platform.WindowFrame(content);
#if ANDROID
            var windowWidth = page.Window?.Width ?? double.NaN;
            var visible = frame.Width > 0 && frame.Right > 1 && frame.X < windowWidth
                && ((Android.Views.View)content.Handler.PlatformView).IsShown;
            return visible ? $"yes at x {frame.X:0}" : $"no (x {frame.X:0}, w {frame.Width:0})";
#elif IOS || MACCATALYST
            var contentView = (UIKit.UIView)content.Handler.PlatformView;
            var flyoutRoot = (UIKit.UIView)page.Flyout.Handler.PlatformView;
            if (contentView.Window is not { } window) return $"no (detached, x {frame.X:0})";
            var center = contentView.ConvertPointToView(new CoreGraphics.CGPoint(contentView.Bounds.Width / 2, contentView.Bounds.Height / 2), null);
            var hit = window.HitTest(center, null);
            var inFlyout = hit is not null && hit.IsDescendantOfView(flyoutRoot);
            return inFlyout ? $"yes at x {frame.X:0}" : $"no (x {frame.X:0}, hit {hit?.GetType().Name ?? "nothing"})";
#else
            return $"unknown platform (x {frame.X:0})";
#endif
        }
        catch (Exception ex)
        {
            return "unknown: " + ex.GetType().Name;
        }
    }

    /// <summary>Opening and closing through the platform, as the user would: when IsPresentedChanged arrives.</summary>
    private static async Task FlyoutPlatformToggle(FixtureContext c)
    {
        var page = PlainFlyout(FlyoutLayoutBehavior.Popover);
        var log = new FlyoutLog();
        log.Attach(page);
        log.Stage = "appear";
        c.Pages.Set(page);
        await c.WaitFor(() => page.IsLoaded, "the flyout page to load");
        await c.Settle();
        var state = SplitState(page);
        var opened = await TogglePlatform(c, page, log, open: true);
        await c.Settle();
        var closed = await TogglePlatform(c, page, log, open: false);
        await c.Settle();
        Console.WriteLine($"# flyout toggle (Popover): [{state}]; open -> {opened}; close -> {closed}; events [{string.Join(" ", log.Events)}]");
    }

    private static Task<string> TogglePlatform(FixtureContext c, FlyoutPage page, FlyoutLog log, bool open)
    {
        log.Stage = open ? "platform-open" : "platform-close";
        var before = log.Events.Count;
#if ANDROID
        var drawer = FindDrawer(page.Handler?.PlatformView as Android.Views.View);
        if (drawer is null) return Task.FromResult("no DrawerLayout");
        if (open) drawer.OpenDrawer((int)Android.Views.GravityFlags.Start, false);
        else drawer.CloseDrawer((int)Android.Views.GravityFlags.Start, false);
        return Task.FromResult($"{(drawer.IsDrawerOpen((int)Android.Views.GravityFlags.Start) ? "drawer open" : "drawer closed")}, " +
            $"events inside the call {log.Events.Count - before}, presented {page.IsPresented}");
#elif IOS || MACCATALYST
        // The flyout toggle MAUI puts in the detail's navigation bar, invoked as a tap would.
        if (page.Detail is NavigationPage { Handler: IPlatformViewHandler { ViewController: UIKit.UINavigationController nav } }
            && nav.TopViewController?.NavigationItem.LeftBarButtonItem is { } toggle && toggle.Action is { } action)
        {
            UIKit.UIApplication.SharedApplication.SendAction(action, toggle.Target, toggle, null);
            return Task.FromResult($"toggle sent, events inside the call {log.Events.Count - before}, presented {page.IsPresented}");
        }
        return Task.FromResult("no toggle button in the detail's navigation bar");
#else
        return Task.FromResult("unsupported");
#endif
    }

#if ANDROID
    private static AndroidX.DrawerLayout.Widget.DrawerLayout? FindDrawer(Android.Views.View? root)
    {
        var pending = new Stack<Android.Views.View>();
        if (root is not null) pending.Push(root);
        var activityRoot = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window?.DecorView;
        if (activityRoot is not null) pending.Push(activityRoot);
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (next is AndroidX.DrawerLayout.Widget.DrawerLayout found) return found;
            if (next is Android.Views.ViewGroup group)
                for (var i = 0; i < group.ChildCount; i++) pending.Push(group.GetChildAt(i)!);
        }
        return null;
    }
#endif

    /// <summary>Mac Catalyst: the window resized to portrait and back, with Default and SplitOnLandscape.</summary>
    private static async Task FlyoutWindowShape(FixtureContext c)
    {
        if (DeviceInfo.Platform != DevicePlatform.MacCatalyst) throw new SkipException("only Mac Catalyst windows can be resized from code");
        var window = c.Window;
        var (width, height) = (window.Width, window.Height);
        try
        {
            foreach (var behavior in new[] { FlyoutLayoutBehavior.Default, FlyoutLayoutBehavior.SplitOnLandscape, FlyoutLayoutBehavior.SplitOnPortrait })
            {
                var page = PlainFlyout(behavior);
                var log = new FlyoutLog();
                log.Attach(page);
                log.Stage = "appear";
                c.Pages.Set(page);
                await c.WaitFor(() => page.IsLoaded, "the flyout page to load");
                await c.Settle();
                var landscape = SplitState(page);
                log.Stage = "to-portrait";
                window.Width = 500;
                window.Height = 800;
                await c.Settle(1000);
                var portrait = SplitState(page) + $", shown {FlyoutShown(page)}";
                log.Stage = "false-in-portrait";
                var write = log.Write(page, false);
                await c.Settle();
                var afterWrite = SplitState(page);
                log.Stage = "to-landscape";
                window.Width = Math.Max(width, 1000);
                window.Height = Math.Min(height, 700);
                await c.Settle(1000);
                var back = SplitState(page) + $", shown {FlyoutShown(page)}";
                Console.WriteLine($"# flyout window shape {behavior}: landscape [{landscape}]; portrait [{portrait}]; write false -> {write} [{afterWrite}]; " +
                    $"landscape again [{back}]; events [{string.Join(" ", log.Events)}]");
                c.Pages.Set(new ContentPage());
                await c.WaitFor(() => !page.IsLoaded, "the flyout page to unload");
            }
        }
        finally
        {
            window.Width = width;
            window.Height = height;
            await c.Settle(500);
        }
    }

    private static string Orientation(FixtureContext c)
    {
        var sources = $"display {DeviceDisplay.MainDisplayInfo.Orientation}";
#if IOS && !MACCATALYST
        UIKit.UIWindowScene? scene = null;
        foreach (var connected in UIKit.UIApplication.SharedApplication.ConnectedScenes)
            if (connected is UIKit.UIWindowScene windowScene) scene = windowScene;
#pragma warning disable CA1422 // what MAUI's DeviceDisplay reads on iOS
        sources += $", status bar {UIKit.UIApplication.SharedApplication.StatusBarOrientation}";
#pragma warning restore CA1422
        sources += $", scene {(OperatingSystem.IsIOSVersionAtLeast(16) ? scene?.EffectiveGeometry.InterfaceOrientation.ToString() : "n/a")}, device {UIKit.UIDevice.CurrentDevice.Orientation}";
#elif ANDROID
        sources += $", configuration {Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Resources?.Configuration?.Orientation}";
#endif
        return $"{(c.Window.Width > c.Window.Height ? "landscape" : "portrait")} {c.Window.Width:0}x{c.Window.Height:0}, {sources}";
    }

    /// <summary>Rotates an iPad (simulator) in process, waiting for the window's shape to follow.</summary>
    private static async Task Rotate(FixtureContext c, bool landscape)
    {
#if IOS && !MACCATALYST
        UIKit.UIWindowScene? scene = null;
        foreach (var connected in UIKit.UIApplication.SharedApplication.ConnectedScenes)
            if (connected is UIKit.UIWindowScene windowScene) scene = windowScene;
        if (scene is null) throw new InvalidOperationException("no window scene");
        string? error = null;
        var mask = landscape ? UIKit.UIInterfaceOrientationMask.LandscapeRight : UIKit.UIInterfaceOrientationMask.Portrait;
        if (!OperatingSystem.IsIOSVersionAtLeast(16)) throw new SkipException("rotation from code needs iOS 16");
        foreach (var window in scene.Windows) window.RootViewController?.SetNeedsUpdateOfSupportedInterfaceOrientations();
        scene.RequestGeometryUpdate(new UIKit.UIWindowSceneGeometryPreferencesIOS(mask), e => error = e.LocalizedDescription);
        await c.WaitFor(() => error is not null || (c.Window.Width > c.Window.Height) == landscape, $"the window to turn {(landscape ? "landscape" : "portrait")}", 8000);
        if (error is not null) throw new InvalidOperationException("rotation refused: " + error);
        await c.Settle(1000);
#elif ANDROID
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity ?? throw new InvalidOperationException("no activity");
        activity.RequestedOrientation = landscape ? Android.Content.PM.ScreenOrientation.Landscape : Android.Content.PM.ScreenOrientation.Portrait;
        await c.WaitFor(() => (c.Window.Width > c.Window.Height) == landscape, $"the window to turn {(landscape ? "landscape" : "portrait")}", 8000);
        await c.Settle(1000);
#else
        await Task.CompletedTask;
        throw new SkipException("rotation is characterized on an iPad simulator or an Android tablet");
#endif
    }

    /// <summary>
    /// iPad: the device orientation set through UIDevice (what a physical rotation changes), to see whether
    /// MAUI's display orientation (the status bar's) follows where a scene geometry request doesn't.
    /// </summary>
    private static async Task OrientationDeviceKvc(FixtureContext c)
    {
        RequireTablet();
#if IOS && !MACCATALYST
        var page = PlainFlyout(FlyoutLayoutBehavior.SplitOnLandscape);
        c.Pages.Set(page);
        await c.WaitFor(() => page.IsLoaded, "the flyout page to load");
        await c.Settle();
        var before = $"{Orientation(c)}; {SplitState(page)}";
        UIKit.UIDevice.CurrentDevice.SetValueForKey(Foundation.NSNumber.FromInt32((int)UIKit.UIDeviceOrientation.LandscapeLeft), new Foundation.NSString("orientation"));
        await c.Settle(2000);
        var after = $"{Orientation(c)}; {SplitState(page)}, shown {FlyoutShown(page)}";
        UIKit.UIDevice.CurrentDevice.SetValueForKey(Foundation.NSNumber.FromInt32((int)UIKit.UIDeviceOrientation.Portrait), new Foundation.NSString("orientation"));
        await c.Settle(2000);
        Console.WriteLine($"# orientation through UIDevice: before [{before}]; landscape [{after}]; back [{Orientation(c)}]");
        await Rotate(c, landscape: false);
#else
        await Task.CompletedTask;
#endif
    }

    private static void RequireTablet()
    {
        if (DeviceInfo.Platform == DevicePlatform.MacCatalyst || DeviceInfo.Idiom != DeviceIdiom.Tablet)
            throw new SkipException("rotation is characterized on an iPad simulator or an Android tablet");
    }

    /// <summary>
    /// iPad: each layout behavior through portrait → landscape → portrait, with writes in each state: when
    /// split mode starts and ends, what MAUI does to IsPresented, which events arrive, what a write does.
    /// </summary>
    private static async Task FlyoutRotation(FixtureContext c, FlyoutLayoutBehavior behavior)
    {
        RequireTablet();
        try
        {
            await Rotate(c, landscape: false);
            {
                var page = PlainFlyout(behavior);
                var log = new FlyoutLog();
                log.Attach(page);
                log.Stage = "appear";
                c.Pages.Set(page);
                await c.WaitFor(() => page.IsLoaded, "the flyout page to load");
                await c.Settle();
                var steps = new List<string> { $"appear [{Orientation(c)}; {SplitState(page)}, shown {FlyoutShown(page)}]" };

                async Task Step(string stage, Func<Task<string>> act)
                {
                    log.Stage = stage;
                    var before = log.Events.Count;
                    var result = await act();
                    await c.Settle();
                    steps.Add($"{stage} -> {result} [{SplitState(page)}, shown {FlyoutShown(page)}, events {string.Join(" ", log.Events.Skip(before))}]");
                }

                if (!((IFlyoutPageController)page).ShouldShowSplitMode)
                    await Step("portrait-write-false", () => Task.FromResult(log.Write(page, false)));
                await Step("to-landscape", async () => { await Rotate(c, true); return Orientation(c); });
                await Step("landscape-write-true", () => Task.FromResult(log.Write(page, true)));
                if (!((IFlyoutPageController)page).ShouldShowSplitMode)
                    await Step("landscape-write-false", () => Task.FromResult(log.Write(page, false)));
                await Step("to-portrait", async () => { await Rotate(c, false); return Orientation(c); });
                await Step("portrait-write-false", () => Task.FromResult(log.Write(page, false)));
                await Step("portrait-write-true", () => Task.FromResult(log.Write(page, true)));
                await Step("portrait-write-false-again", () => Task.FromResult(log.Write(page, false)));
                await Step("to-landscape-closed", async () => { await Rotate(c, true); return Orientation(c); });
                await Step("to-portrait-again", async () => { await Rotate(c, false); return Orientation(c); });
                Console.WriteLine($"# flyout rotation {behavior}: {string.Join("; ", steps)}");
                c.Pages.Set(new ContentPage());
                await c.WaitFor(() => !page.IsLoaded, "the flyout page to unload");
            }
        }
        finally
        {
            if (DeviceInfo.Idiom == DeviceIdiom.Tablet) await Rotate(c, landscape: false);
#if ANDROID
            if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is { } activity) activity.RequestedOrientation = Android.Content.PM.ScreenOrientation.Unspecified;
#endif
        }
    }

    /// <summary>iPad: today's Reactor flyout, declared closed, rotated into split mode and out again.</summary>
    private static async Task FlyoutElementRotation(FixtureContext c, FlyoutLayoutBehavior behavior)
    {
        RequireTablet();
        try
        {
            {
                await Rotate(c, landscape: false);
                var reports = new List<bool>();
                Element Tree(bool presented) => Flyout(Page("Menu", Label("menu")), Page("Detail", Label("detail")), presented, reports.Add) with { LayoutBehavior = behavior };
                var context = new FixtureContext(c.Window);
                var steps = new List<string>();
                string Errors() => string.Join(" | ", context.UnhandledErrors.Select(e => e.GetType().Name + ": " + e.Message));
                void Record(string stage, FlyoutPage? page) =>
                    steps.Add($"{stage} [{Orientation(c)}; {(page is null ? "no page" : $"{SplitState(page)}, shown {FlyoutShown(page)}")}, reports [{string.Join(",", reports)}], unhandled [{Errors()}]]");

                async Task Guarded(string stage, Func<Task> act)
                {
                    try { await act(); }
                    catch (Exception ex) { steps.Add($"{stage} threw {ex.GetType().Name}: {ex.Message}"); }
                    Record(stage, c.Window.Page as FlyoutPage);
                    reports.Clear();
                    context.UnhandledErrors.Clear();
                }

                await Guarded("mount-portrait", async () =>
                {
                    context.Show(Tree(false));
                    await context.WaitFor(() => c.Window.Page is FlyoutPage { IsLoaded: true }, "the Reactor flyout to load");
                    await context.Settle();
                });
                await Guarded("to-landscape", () => Rotate(c, true));
                await Guarded("render-false-in-landscape", async () => { context.Render(Tree(false)); await context.Settle(); });
                await Guarded("render-true-then-false-in-landscape", async () =>
                {
                    context.Render(Tree(true)); await context.Settle();
                    context.Render(Tree(false)); await context.Settle();
                });
                await Guarded("to-portrait", () => Rotate(c, false));
                await Guarded("render-false-in-portrait", async () => { context.Render(Tree(false)); await context.Settle(); });
                Console.WriteLine($"# flyout element rotation {behavior}: {string.Join("; ", steps)}");
                try { context.Unmount(); } catch (Exception ex) { Console.WriteLine($"# flyout element rotation {behavior}: unmount threw {ex.GetType().Name}"); }
                c.Pages.Set(new ContentPage());
                await c.Settle();
            }
        }
        finally
        {
            await Rotate(c, landscape: false);
#if ANDROID
            if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is { } activity) activity.RequestedOrientation = Android.Content.PM.ScreenOrientation.Unspecified;
#endif
        }
    }

    /// <summary>
    /// Leaving and entering split mode by changing the layout behavior (the transition every device can
    /// reach): what MAUI does to IsPresented, which events arrive and whether they arrive inside the write,
    /// and whether false can be written afterwards.
    /// </summary>
    private static async Task FlyoutBehaviorChange(FixtureContext c)
    {
        var page = PlainFlyout(FlyoutLayoutBehavior.Split);
        var log = new FlyoutLog();
        log.Attach(page);
        log.Stage = "appear";
        c.Pages.Set(page);
        await c.WaitFor(() => page.IsLoaded, "the flyout page to load");
        await c.Settle();
        var steps = new List<string> { $"appear Split [{SplitState(page)}, shown {FlyoutShown(page)}]" };

        async Task Step(string stage, Func<string> act)
        {
            log.Stage = stage;
            var before = log.Events.Count;
            log.Writing = true;
            string result;
            try { result = act(); }
            catch (Exception ex) { result = $"{ex.GetType().Name}: {ex.Message}"; }
            finally { log.Writing = false; }
            await c.Settle();
            steps.Add($"{stage} -> {result} [{SplitState(page)}, shown {FlyoutShown(page)}, events {string.Join(" ", log.Events.Skip(before))}]");
        }

        await Step("behavior-Popover", () => { page.FlyoutLayoutBehavior = FlyoutLayoutBehavior.Popover; return "ok"; });
        await Step("write-false", () => { page.IsPresented = false; return "ok"; });
        await Step("write-true", () => { page.IsPresented = true; return "ok"; });
        await Step("write-false-again", () => { page.IsPresented = false; return "ok"; });
        await Step("behavior-Split", () => { page.FlyoutLayoutBehavior = FlyoutLayoutBehavior.Split; return "ok"; });
        await Step("write-false-in-split", () => { page.IsPresented = false; return "ok"; });
        await Step("behavior-Popover-again", () => { page.FlyoutLayoutBehavior = FlyoutLayoutBehavior.Popover; return "ok"; });
        await Step("write-false-after", () => { page.IsPresented = false; return "ok"; });
        Console.WriteLine($"# flyout behavior change: {string.Join("; ", steps)}");
        c.Pages.Set(new ContentPage());
        await c.WaitFor(() => !page.IsLoaded, "the flyout page to unload");
    }

    /// <summary>Today's Reactor flyout, declared closed, per layout behavior: what it reports and what throws.</summary>
    private static async Task FlyoutElementToday(FixtureContext c)
    {
        foreach (FlyoutLayoutBehavior? behavior in new FlyoutLayoutBehavior?[] { null, FlyoutLayoutBehavior.Split, FlyoutLayoutBehavior.SplitOnPortrait })
        {
            var reports = new List<bool>();
            Element Tree(bool presented) => Flyout(Page("Menu", Label("menu")), Page("Detail", Label("detail")), presented, reports.Add) with { LayoutBehavior = behavior };
            string? error = null;
            var context = new FixtureContext(c.Window);
            try
            {
                context.Show(Tree(false));
                await context.WaitFor(() => c.Window.Page is FlyoutPage { IsLoaded: true }, "the Reactor flyout to load");
                await context.Settle();
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
            }
            var page = c.Window.Page as FlyoutPage;
            var mount = $"reports [{string.Join(",", reports)}], unhandled [{string.Join(" | ", context.UnhandledErrors.Select(e => e.GetType().Name + ": " + e.Message))}], thrown {error ?? "none"}";
            if (page is not null) mount += $", {SplitState(page)}";
            reports.Clear();
            context.UnhandledErrors.Clear();
            error = null;
            try
            {
                context.Render(Tree(true));
                await context.Settle();
                context.Render(Tree(false));
                await context.Settle();
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
            }
            Console.WriteLine($"# flyout element {behavior?.ToString() ?? "unset"}: mount [{mount}]; declared true then false -> presented {page?.IsPresented}, reports [{string.Join(",", reports)}], " +
                $"unhandled [{string.Join(" | ", context.UnhandledErrors.Select(e => e.GetType().Name + ": " + e.Message))}], thrown {error ?? "none"}");
            try { context.Unmount(); } catch (Exception ex) { Console.WriteLine($"# flyout element {behavior}: unmount threw {ex.GetType().Name}"); }
            c.Pages.Set(new ContentPage());
            await c.Settle();
        }
    }

    /// <summary>
    /// A toolbar item kept after Reactor rebuilt the toolbar, invoked: whose action runs. Also where the
    /// platform shows the items (the probe the device fixture needs, with its positive control: it finds
    /// the declared titles), and what invoking a platform item, current or retained, runs.
    /// </summary>
    private static async Task ToolbarStaleInvocation(FixtureContext c)
    {
        var log = new List<string>();
        PageElement Screen(params string[] names) => Page("Toolbar", Label("body")) with
        {
            ToolbarItems = [.. names.Select(name => new ToolbarAction(name, () => log.Add(name)))],
        };
        c.Show(NavigationStack([Screen("A", "B")]));
        await c.WaitFor(() => c.FindAll<ContentPage>().Any(p => p.ToolbarItems.Count == 2 && p.IsLoaded), "the toolbar");
        var page = c.FindAll<ContentPage>().First(p => p.ToolbarItems.Count == 2);
        await c.WaitFor(() => PlatformToolbar(page).Count == 2, "the platform to show two toolbar items");
        var platformFirst = PlatformToolbar(page);
        var platformTitles = string.Join(",", platformFirst.Select(item => item.Title));
        Check(platformFirst.Select(item => item.Title).OrderBy(t => t).SequenceEqual(["A", "B"]), $"the probe finds the declared items ({platformTitles}) (positive control)");
        var invokedB = PlatformToolbar(page).Single(item => item.Title == "B").Invoke();
        var platformCurrent = $"{invokedB}: " + string.Join(",", log);
        log.Clear();
        var retainedPlatformA = platformFirst.Single(item => item.Title == "A");
        var (oldA, oldB) = (page.ToolbarItems[0], page.ToolbarItems[1]);

        c.Render(NavigationStack([Screen("C", "D")]));
        await c.Settle();
        var rebuilt = !ReferenceEquals(oldA, page.ToolbarItems[0]);
        var platformAfter = string.Join(",", PlatformToolbar(page).Select(item => item.Title));
        ((IMenuItemController)oldA).Activate();
        ((IMenuItemController)oldB).Activate();
        var staleBoth = string.Join(",", log);
        log.Clear();
        var invokedA = retainedPlatformA.Invoke();
        var stalePlatform = $"{invokedA}: " + string.Join(",", log);
        log.Clear();

        c.Render(NavigationStack([Screen("E")]));
        await c.Settle();
        ((IMenuItemController)oldA).Activate();
        ((IMenuItemController)oldB).Activate();
        var staleShorter = string.Join(",", log);
        log.Clear();
        ((IMenuItemController)page.ToolbarItems[0]).Activate();
        Console.WriteLine($"# toolbar: rebuilt {rebuilt}; stale A, B after A,B -> C,D ran [{staleBoth}]; after -> E ran [{staleShorter}]; the current item ran [{string.Join(",", log)}]");
        Console.WriteLine($"# toolbar platform: {PlatformToolbarWhere(page)}; titles [{platformTitles}] then [{platformAfter}]; invoking the platform B ran [{platformCurrent}]; " +
            $"the retained platform A after A,B -> C,D ran [{stalePlatform}]");
        Check(rebuilt, "the toolbar was rebuilt (precondition)");
    }

    private sealed record PlatformItem(string Title, Func<string> Invoke);

    /// <summary>
    /// The page's toolbar items as the platform holds them. iOS and Mac Catalyst: the navigation controller's
    /// top item (MAUI hosts the page's controller in a parent controller, whose item the bar shows);
    /// Android: the MaterialToolbar's menu.
    /// </summary>
    private static List<PlatformItem> PlatformToolbar(ContentPage page)
    {
        var items = new List<PlatformItem>();
#if IOS || MACCATALYST
        if (page.Parent is NavigationPage { Handler: IPlatformViewHandler { ViewController: UIKit.UINavigationController nav } }
            && nav.TopViewController?.NavigationItem is { } navigationItem)
        {
            foreach (var button in navigationItem.RightBarButtonItems ?? [])
            {
                var captured = button;
                items.Add(new(button.Title ?? "", () =>
                {
                    // MAUI disposes the bar's previous items when it replaces them.
                    if (captured.Handle == IntPtr.Zero) return "disposed";
                    if (captured.Action is not { } action) return "no action";
                    return UIKit.UIApplication.SharedApplication.SendAction(action, captured.Target, captured, null) ? "sent" : "not handled";
                }));
            }
        }
#elif ANDROID
        var toolbar = FindToolbar(Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window?.DecorView);
        if (toolbar?.Menu is { } menu)
        {
            for (var i = 0; i < menu.Size(); i++)
            {
                var menuItem = menu.GetItem(i)!;
                var id = menuItem.ItemId;
                // MAUI disposes the menu's previous items when it rebuilds the menu.
                items.Add(new(menuItem.TitleFormatted?.ToString() ?? "", () =>
                    menuItem.Handle == IntPtr.Zero ? "disposed" : menu.PerformIdentifierAction(id, 0) ? "performed" : "not performed"));
            }
        }
#endif
        return items;
    }

    private static string PlatformToolbarWhere(ContentPage page)
    {
#if IOS || MACCATALYST
        if (page.Parent is NavigationPage { Handler: IPlatformViewHandler { ViewController: UIKit.UINavigationController nav } })
        {
            var top = nav.TopViewController;
            var own = page.Handler is IPlatformViewHandler { ViewController: { } controller } ? controller.NavigationItem.RightBarButtonItems?.Length ?? 0 : -1;
            return $"top controller {top?.GetType().Name} right items {top?.NavigationItem.RightBarButtonItems?.Length ?? 0}, page controller right items {own}, bar hidden {nav.NavigationBarHidden}";
        }
        return "no navigation controller";
#elif ANDROID
        return FindToolbar(Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window?.DecorView) is { } toolbar
            ? $"{toolbar.GetType().Name} menu {toolbar.Menu?.Size() ?? 0}" : "no toolbar";
#else
        return "n/a";
#endif
    }

#if ANDROID
    private static AndroidX.AppCompat.Widget.Toolbar? FindToolbar(Android.Views.View? root)
    {
        var pending = new Stack<Android.Views.View>();
        if (root is not null) pending.Push(root);
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (next is AndroidX.AppCompat.Widget.Toolbar found && found.IsShown) return found;
            if (next is Android.Views.ViewGroup group)
                for (var i = 0; i < group.ChildCount; i++) pending.Push(group.GetChildAt(i)!);
        }
        return null;
    }
#endif
}
