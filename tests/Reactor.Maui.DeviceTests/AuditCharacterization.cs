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

            log.Stage = "true";
            var writeTrue = log.Write(page, true);
            await c.Settle();
            var afterTrue = $"{page.IsPresented}, shown {FlyoutShown(page)}";

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

    /// <summary>Whether the flyout's content is on screen: realized, with a width, inside the window.</summary>
    private static string FlyoutShown(FlyoutPage page)
    {
        if (page.Flyout?.Handler?.PlatformView is null) return "unrealized";
        var content = page.Flyout is ContentPage { Content: { } view } ? view : null;
        if (content?.Handler?.PlatformView is null) return "content unrealized";
        try
        {
            var frame = Platform.WindowFrame(content);
            var windowWidth = page.Window?.Width ?? double.NaN;
            var visible = frame.Width > 0 && frame.Right > 1 && frame.X < windowWidth;
#if ANDROID
            visible &= ((Android.Views.View)content.Handler.PlatformView).IsShown;
#elif IOS || MACCATALYST
            visible &= ((UIKit.UIView)content.Handler.PlatformView).Window is not null;
#endif
            return visible ? $"yes at x {frame.X:0}" : $"no (x {frame.X:0}, w {frame.Width:0})";
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

    /// <summary>A toolbar item kept after Reactor rebuilt the toolbar, invoked: whose action runs.</summary>
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
        var (oldA, oldB) = (page.ToolbarItems[0], page.ToolbarItems[1]);

        c.Render(NavigationStack([Screen("C", "D")]));
        await c.Settle();
        var rebuilt = !ReferenceEquals(oldA, page.ToolbarItems[0]);
        ((IMenuItemController)oldA).Activate();
        ((IMenuItemController)oldB).Activate();
        var staleBoth = string.Join(",", log);
        log.Clear();

        c.Render(NavigationStack([Screen("E")]));
        await c.Settle();
        ((IMenuItemController)oldA).Activate();
        ((IMenuItemController)oldB).Activate();
        var staleShorter = string.Join(",", log);
        log.Clear();
        ((IMenuItemController)page.ToolbarItems[0]).Activate();
        Console.WriteLine($"# toolbar: rebuilt {rebuilt}; stale A, B after A,B -> C,D ran [{staleBoth}]; after -> E ran [{staleShorter}]; the current item ran [{string.Join(",", log)}]; " +
            $"platform items {PlatformToolbarCount(page)}");
        Check(rebuilt, "the toolbar was rebuilt (precondition)");
    }

    private static string PlatformToolbarCount(ContentPage page)
    {
#if IOS || MACCATALYST
        if (page.Handler is IPlatformViewHandler { ViewController: { } controller })
            return $"{controller.NavigationItem.RightBarButtonItems?.Length ?? 0} bar button(s)";
#endif
        return "n/a";
    }
}
