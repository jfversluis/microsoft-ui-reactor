using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using static Reactor.Maui.DeviceTests.FixtureContext;
using MauiShell = Microsoft.Maui.Controls.Shell;

namespace Reactor.Maui.DeviceTests;

// Spec 070 §4.16 (4c-3c): what Shell does with FlyoutIsPresented under each FlyoutBehavior, on plain
// MAUI and on today's Reactor, before the 4c-3c design relies on it. Logs; asserts only that it ran.
public static partial class Fixtures
{
    private static IEnumerable<Fixture> ShellAuditCharacterizationFixtures() =>
    [
        new("Characterize_ShellFlyout_Behaviors", ShellFlyoutBehaviors),
        new("Characterize_ShellFlyout_ReactorDeclared", ShellFlyoutReactorDeclared),
        new("Characterize_ShellFlyout_Visibility", ShellFlyoutVisibility),
        new("Characterize_ShellFlyout_TransitionsFromFlyout", c => ShellFlyoutTransitions(c, FlyoutBehavior.Flyout)),
        new("Characterize_ShellFlyout_TransitionsFromLocked", c => ShellFlyoutTransitions(c, FlyoutBehavior.Locked)),
        new("Characterize_ShellFlyout_TransitionsFromDisabled", c => ShellFlyoutTransitions(c, FlyoutBehavior.Disabled)),
        new("Characterize_ShellFlyout_Animation", ShellFlyoutAnimation),
        new("Characterize_ShellFlyout_ItemSwitch", ShellFlyoutItemSwitch),
        new("Characterize_ShellFlyout_DisabledFresh", ShellFlyoutDisabledFresh),
        new("Characterize_ShellFlyout_DisabledAfterFlyout", ShellFlyoutDisabledAfterFlyout),
        new("Characterize_ShellFlyout_LeaveDisabledTiming", ShellFlyoutLeaveDisabledTiming),
        new("Characterize_ShellFlyout_PageChange", ShellFlyoutPageChange),
    ];

    /// <summary>A shell with a flyout item and a tab bar, a header to probe, and a change log.</summary>
    private sealed class ShellProbe
    {
        public readonly MauiShell Shell;
        public readonly Label Header = new() { Text = "flyout header", BackgroundColor = Colors.Orange, HeightRequest = 60 };
        public readonly List<string> Events = [];
        public string Stage = "create";
        public bool Writing;

        public ShellProbe(FlyoutBehavior? behavior)
        {
            Shell = new MauiShell { FlyoutHeader = Header };
            if (behavior is { } b) Shell.FlyoutBehavior = b;
            Shell.Items.Add(new FlyoutItem { Title = "One", Items = { new ShellContent { Title = "One", Content = new ContentPage { Content = new Label { Text = "one" } } } } });
            Shell.Items.Add(new TabBar { Title = "Tabs", Items = { new ShellContent { Title = "Tab", Content = new ContentPage { Content = new Label { Text = "tab" } } } } });
            Shell.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == MauiShell.FlyoutIsPresentedProperty.PropertyName)
                    Events.Add($"{Stage}:{(Writing ? "in-write" : "outside")}:{Shell.FlyoutIsPresented}");
            };
        }

        public void Write(Action write)
        {
            Writing = true;
            try { write(); }
            finally { Writing = false; }
        }

        public string State => $"presented {Shell.FlyoutIsPresented}, shown {ShellFlyoutShown(Shell, Header)}";
    }

    private static async Task<ShellProbe> ShowProbe(FixtureContext c, FlyoutBehavior? behavior)
    {
        var probe = new ShellProbe(behavior);
        c.Pages.Set(probe.Shell);
        await c.WaitFor(() => probe.Shell.IsLoaded, "the shell to load");
        await c.Settle(800);
        return probe;
    }

    /// <summary>Every directed behavior change, with the flyout declared open and closed first.</summary>
    private static async Task ShellFlyoutTransitions(FixtureContext c, FlyoutBehavior from)
    {
        var behaviors = new[] { FlyoutBehavior.Flyout, FlyoutBehavior.Locked, FlyoutBehavior.Disabled };
        foreach (var to in behaviors)
        {
            if (from == to) continue;
            foreach (var declared in new[] { false, true })
            {
                var p = await ShowProbe(c, from);
                p.Stage = "declare";
                p.Write(() => p.Shell.FlyoutIsPresented = declared);
                await c.Settle(900);
                var before = p.State;
                p.Events.Clear();
                p.Stage = "behavior";
                p.Write(() => p.Shell.FlyoutBehavior = to);
                p.Stage = "after";
                await c.Settle(900);
                Console.WriteLine($"# shell transition {from}->{to} declared {declared}: before [{before}]; after [{p.State}]; events [{string.Join(" ", p.Events)}]");
                c.Pages.Set(new ContentPage());
                await c.Settle(300);
            }
        }
    }

    /// <summary>
    /// A shell that starts Disabled (its flyout never created on Android) and leaves it with the flyout
    /// declared open, then a re-projection (false, then true) of the presentation.
    /// </summary>
    private static async Task ShellFlyoutDisabledFresh(FixtureContext c)
    {
        foreach (var to in new[] { FlyoutBehavior.Flyout, FlyoutBehavior.Locked })
        {
            var p = await ShowProbe(c, FlyoutBehavior.Disabled);
            p.Write(() => p.Shell.FlyoutIsPresented = true);
            await c.Settle(900);
            var fresh = p.State;
            p.Events.Clear();
            p.Stage = "behavior";
            p.Write(() => p.Shell.FlyoutBehavior = to);
            p.Stage = "after";
            await c.Settle(2500);
            var left = p.State;
            p.Stage = "reproject";
            p.Write(() => { p.Shell.FlyoutIsPresented = false; p.Shell.FlyoutIsPresented = true; });
            p.Stage = "after-reproject";
            await c.Settle(1500);
            var reprojected = p.State;
            p.Stage = "split-reproject";
            p.Write(() => p.Shell.FlyoutIsPresented = false);
            await c.Settle(900);
            p.Write(() => p.Shell.FlyoutIsPresented = true);
            p.Stage = "after-split";
            await c.Settle(1500);
            Console.WriteLine($"# shell disabled fresh true then {to}: fresh [{fresh}]; left [{left}]; reprojected in one turn [{reprojected}]; reprojected across turns [{p.State}]; events [{string.Join(" ", p.Events)}]");
            c.Pages.Set(new ContentPage());
            await c.Settle(300);
        }
    }

    /// <summary>
    /// A presentation the behavior doesn't force (Locked closed, Disabled open after a realized flyout),
    /// then a page pushed and popped in the shown section: does MAUI's behavior observer force it again?
    /// </summary>
    private static async Task ShellFlyoutPageChange(FixtureContext c)
    {
        foreach (var (behavior, presented) in new[] { (FlyoutBehavior.Locked, false), (FlyoutBehavior.Disabled, true) })
        {
            var p = await ShowProbe(c, behavior == FlyoutBehavior.Disabled ? FlyoutBehavior.Flyout : behavior);
            if (behavior == FlyoutBehavior.Disabled)
            {
                p.Write(() => p.Shell.FlyoutBehavior = FlyoutBehavior.Disabled);
                await c.Settle(600);
            }
            p.Write(() => p.Shell.FlyoutIsPresented = presented);
            await c.Settle(900);
            var before = p.State;
            p.Events.Clear();
            p.Stage = "push";
            await p.Shell.Navigation.PushAsync(new ContentPage { Content = new Label { Text = "pushed" } }, false);
            await c.Settle(900);
            var pushed = p.State;
            p.Stage = "pop";
            await p.Shell.Navigation.PopAsync(false);
            await c.Settle(900);
            Console.WriteLine($"# shell page change under {behavior} with {presented}: before [{before}]; pushed [{pushed}]; popped [{p.State}]; events [{string.Join(" ", p.Events)}]");
            c.Pages.Set(new ContentPage());
            await c.Settle(300);
        }
    }

    /// <summary>When a re-projection after leaving a fresh Disabled takes effect.</summary>
    private static async Task ShellFlyoutLeaveDisabledTiming(FixtureContext c)
    {
        foreach (var delay in new[] { -1, 0, 50, 200 })
        {
            var p = await ShowProbe(c, FlyoutBehavior.Disabled);
            p.Write(() => p.Shell.FlyoutIsPresented = true);
            await c.Settle(600);
            void Reproject() => p.Write(() => { p.Shell.FlyoutIsPresented = false; p.Shell.FlyoutIsPresented = true; });
            p.Write(() => p.Shell.FlyoutBehavior = FlyoutBehavior.Flyout);
            if (delay < 0) Reproject();
            else if (delay == 0) p.Shell.Dispatcher.Dispatch(Reproject);
            else p.Shell.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(delay), Reproject);
            await c.Settle(1800);
            Console.WriteLine($"# shell leave disabled, reproject {(delay < 0 ? "in the same turn" : delay == 0 ? "on the next dispatch" : $"after {delay} ms")}: [{p.State}]");
            c.Pages.Set(new ContentPage());
            await c.Settle(300);
        }
    }

    /// <summary>A realized flyout, then Disabled, then the flyout declared open under Disabled.</summary>
    private static async Task ShellFlyoutDisabledAfterFlyout(FixtureContext c)
    {
        foreach (var from in new[] { FlyoutBehavior.Flyout, FlyoutBehavior.Locked })
        {
            var p = await ShowProbe(c, from);
            p.Write(() => p.Shell.FlyoutIsPresented = false);
            await c.Settle(900);
            p.Events.Clear();
            p.Stage = "disable";
            p.Write(() => p.Shell.FlyoutBehavior = FlyoutBehavior.Disabled);
            await c.Settle(900);
            var disabled = p.State;
            p.Stage = "declare-true";
            p.Write(() => p.Shell.FlyoutIsPresented = true);
            p.Stage = "after";
            await c.Settle(1500);
            Console.WriteLine($"# shell disabled after {from}: disabled [{disabled}]; true declared [{p.State}]; events [{string.Join(" ", p.Events)}]");
            c.Pages.Set(new ContentPage());
            await c.Settle(300);
        }
    }

    /// <summary>A behavior change while an open or close animates (written right after the presentation).</summary>
    private static async Task ShellFlyoutAnimation(FixtureContext c)
    {
        foreach (var (open, to) in new[] { (true, FlyoutBehavior.Locked), (true, FlyoutBehavior.Disabled), (false, FlyoutBehavior.Locked) })
        {
            var p = await ShowProbe(c, FlyoutBehavior.Flyout);
            if (!open)
            {
                p.Write(() => p.Shell.FlyoutIsPresented = true);
                await c.Settle(900);
            }
            p.Events.Clear();
            p.Stage = open ? "open" : "close";
            p.Write(() => p.Shell.FlyoutIsPresented = open);
            await Task.Delay(60);
            p.Stage = "behavior";
            p.Write(() => p.Shell.FlyoutBehavior = to);
            p.Stage = "after";
            await c.Settle(1200);
            Console.WriteLine($"# shell animation {(open ? "opening" : "closing")} then {to}: after [{p.State}]; events [{string.Join(" ", p.Events)}]");
            c.Pages.Set(new ContentPage());
            await c.Settle(300);
        }
    }

    /// <summary>
    /// The root behavior unset, the flyout open, and the current item switched between a flyout item
    /// (effective behavior Flyout) and a tab bar (effective Disabled), and back.
    /// </summary>
    private static async Task ShellFlyoutItemSwitch(FixtureContext c)
    {
        foreach (var behavior in new FlyoutBehavior?[] { null, FlyoutBehavior.Locked })
        {
            var p = await ShowProbe(c, behavior);
            p.Write(() => p.Shell.FlyoutIsPresented = true);
            await c.Settle(900);
            var before = p.State;
            p.Events.Clear();
            p.Stage = "to-tabbar";
            p.Write(() => p.Shell.CurrentItem = p.Shell.Items[1]);
            p.Stage = "after-tabbar";
            await c.Settle(1200);
            var atTabs = p.State;
            p.Stage = "declare-true";
            p.Write(() => p.Shell.FlyoutIsPresented = true);
            await c.Settle(900);
            var declaredAtTabs = p.State;
            p.Stage = "to-flyoutitem";
            p.Write(() => p.Shell.CurrentItem = p.Shell.Items[0]);
            p.Stage = "after-flyoutitem";
            await c.Settle(1200);
            Console.WriteLine($"# shell item switch root {behavior?.ToString() ?? "unset"}: before [{before}]; at the tab bar [{atTabs}]; true declared there [{declaredAtTabs}]; back [{p.State}]; events [{string.Join(" ", p.Events)}]");
            c.Pages.Set(new ContentPage());
            await c.Settle(300);
        }
    }

    /// <summary>
    /// Whether the Shell's flyout pane is on screen: its header label is realized and, on iOS and Mac
    /// Catalyst, the window's hit test at the label's center lands in it; on Android, it is shown and
    /// inside the window.
    /// </summary>
    private static string ShellFlyoutShown(MauiShell shell, Label header)
    {
        if (header.Handler?.PlatformView is null) return "no (unrealized)";
        try
        {
#if IOS || MACCATALYST
            var view = (UIKit.UIView)header.Handler.PlatformView;
            if (view.Window is not { } window) return "no (detached)";
            var center = view.ConvertPointToView(new CoreGraphics.CGPoint(view.Bounds.Width / 2, view.Bounds.Height / 2), null);
            var hit = window.HitTest(center, null);
            var inside = window.Bounds.Contains(center);
            var frame = Platform.WindowFrame(header);
            return hit is not null && inside && (hit == view || hit.IsDescendantOfView(view) || view.IsDescendantOfView(hit) && hit.Superview is not null && IsAncestorShell(hit, view))
                ? $"yes at x {frame.X:0}" : $"no (x {frame.X:0}, hit {hit?.GetType().Name ?? "nothing"})";
#elif ANDROID
            var view = (Android.Views.View)header.Handler.PlatformView;
            var frame = Platform.WindowFrame(header);
            var width = shell.Window?.Width ?? double.NaN;
            var visible = view.IsShown && frame.Width > 0 && frame.Right > 1 && frame.X < width;
            return visible ? $"yes at x {frame.X:0}" : $"no (x {frame.X:0}, shown {view.IsShown})";
#else
            return "unknown platform";
#endif
        }
        catch (Exception ex)
        {
            return "unknown: " + ex.GetType().Name;
        }
    }

#if IOS || MACCATALYST
    // A hit on a container of the label (a label passes touches to its parent) counts when that
    // container is the label's own header host, not a view covering it.
    private static bool IsAncestorShell(UIKit.UIView hit, UIKit.UIView label)
    {
        for (var v = label.Superview; v is not null; v = v.Superview)
            if (v == hit) return v.Subviews.Length <= 2;
        return false;
    }
#endif

    private static async Task ShellFlyoutVisibility(FixtureContext c)
    {
        Console.WriteLine($"# shell flyout visibility: idiom {DeviceInfo.Idiom}");
        foreach (var behavior in new[] { FlyoutBehavior.Flyout, FlyoutBehavior.Locked, FlyoutBehavior.Disabled })
        {
            var shell = PlainShell(behavior);
            var header = new Label { Text = "flyout header", AutomationId = "shell-flyout-header", BackgroundColor = Colors.Orange, HeightRequest = 60 };
            shell.FlyoutHeader = header;
            var events = new List<string>();
            var stage = "appear";
            var writing = false;
            shell.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == MauiShell.FlyoutIsPresentedProperty.PropertyName)
                    events.Add($"{stage}:{(writing ? "in-write" : "outside")}:{shell.FlyoutIsPresented}");
            };
            c.Pages.Set(shell);
            await c.WaitFor(() => shell.IsLoaded, $"the {behavior} shell to load");
            await c.Settle(800);
            var steps = new List<string> { $"appear [presented {shell.FlyoutIsPresented}, shown {ShellFlyoutShown(shell, header)}]" };
            foreach (var (name, value) in new[] { ("false", false), ("true", true), ("false-again", false) })
            {
                stage = name;
                writing = true;
                try { shell.FlyoutIsPresented = value; }
                finally { writing = false; }
                await c.Settle(900);
                steps.Add($"{name} [presented {shell.FlyoutIsPresented}, shown {ShellFlyoutShown(shell, header)}]");
            }
            Console.WriteLine($"# shell flyout visibility {behavior}: {string.Join("; ", steps)}; events [{string.Join(" ", events)}]");
            c.Pages.Set(new ContentPage());
            await c.Settle();
        }
    }

    private static MauiShell PlainShell(FlyoutBehavior behavior)
    {
        var shell = new MauiShell { FlyoutBehavior = behavior };
        shell.Items.Add(new FlyoutItem { Title = "One", Items = { new ShellContent { Title = "One", Content = new ContentPage { Content = new Label { Text = "one" } } } } });
        shell.Items.Add(new FlyoutItem { Title = "Two", Items = { new ShellContent { Title = "Two", Content = new ContentPage { Content = new Label { Text = "two" } } } } });
        return shell;
    }

    private static async Task ShellFlyoutBehaviors(FixtureContext c)
    {
        Console.WriteLine($"# shell flyout: idiom {DeviceInfo.Idiom}");
        foreach (var behavior in new[] { FlyoutBehavior.Flyout, FlyoutBehavior.Locked, FlyoutBehavior.Disabled })
        {
            var shell = PlainShell(behavior);
            var events = new List<string>();
            var stage = "appear";
            var writing = false;
            shell.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == MauiShell.FlyoutIsPresentedProperty.PropertyName)
                    events.Add($"{stage}:{(writing ? "in-write" : "outside")}:{shell.FlyoutIsPresented}");
            };
            c.Pages.Set(shell);
            await c.WaitFor(() => shell.IsLoaded, $"the {behavior} shell to load");
            await c.Settle(600);
            var steps = new List<string> { $"appear presented {shell.FlyoutIsPresented}" };
            foreach (var (name, value) in new[] { ("true", true), ("false", false), ("true-again", true), ("false-again", false) })
            {
                stage = name;
                writing = true;
                string result;
                try { shell.FlyoutIsPresented = value; result = "ok"; }
                catch (Exception ex) { result = $"{ex.GetType().Name}: {ex.Message}"; }
                finally { writing = false; }
                await c.Settle(600);
                steps.Add($"write {name} -> {result}, presented {shell.FlyoutIsPresented}");
            }
            // The renderer's own write, as when the user opens or closes the flyout.
            stage = "renderer-open";
            shell.SetValueFromRenderer(MauiShell.FlyoutIsPresentedProperty, true);
            await c.Settle(600);
            steps.Add($"renderer open -> presented {shell.FlyoutIsPresented}");
            stage = "renderer-close";
            shell.SetValueFromRenderer(MauiShell.FlyoutIsPresentedProperty, false);
            await c.Settle(600);
            steps.Add($"renderer close -> presented {shell.FlyoutIsPresented}");
            Console.WriteLine($"# shell flyout {behavior}: {string.Join("; ", steps)}; events [{string.Join(" ", events)}]");
            c.Pages.Set(new ContentPage());
            await c.Settle();
        }
    }

    private sealed record ShellDeclaredProps(FlyoutBehavior Behavior, bool Accept, List<bool> Reports);

    private sealed class ShellDeclaredApp : Component<ShellDeclaredProps>
    {
        public static Action<bool>? Set;

        public override Element Render()
        {
            var (open, setOpen) = UseState(false);
            Set = setOpen;
            return Shell("one", null,
                    FlyoutItem("One", ShellTab("one", "One", Page("One", Label("one")))),
                    FlyoutItem("Two", ShellTab("two", "Two", Page("Two", Label("two")))))
                .FlyoutBehavior(Props.Behavior)
                .FlyoutIsPresented(open, value =>
                {
                    Props.Reports.Add(value);
                    if (Props.Accept) setOpen(value);
                });
        }
    }

    private static async Task ShellFlyoutReactorDeclared(FixtureContext c)
    {
        foreach (var behavior in new[] { FlyoutBehavior.Flyout, FlyoutBehavior.Locked, FlyoutBehavior.Disabled })
        {
            var reports = new List<bool>();
            var context = new FixtureContext(c.Window);
            string? error = null;
            try
            {
                context.Show(Component<ShellDeclaredApp, ShellDeclaredProps>(new(behavior, false, reports)));
                await context.WaitFor(() => c.Window.Page is ReactorShell { IsLoaded: true }, "the Reactor shell to load");
                await context.Settle(600);
                var shell = (ReactorShell)c.Window.Page!;
                var mount = $"presented {shell.FlyoutIsPresented}, reports [{string.Join(",", reports)}]";
                reports.Clear();
                ShellDeclaredApp.Set!(true);
                await context.Settle(600);
                var declaredTrue = $"presented {shell.FlyoutIsPresented}, reports [{string.Join(",", reports)}]";
                reports.Clear();
                ShellDeclaredApp.Set!(false);
                await context.Settle(600);
                var declaredFalse = $"presented {shell.FlyoutIsPresented}, reports [{string.Join(",", reports)}]";
                reports.Clear();
                shell.SetValueFromRenderer(MauiShell.FlyoutIsPresentedProperty, true);
                await context.Settle(600);
                var userOpen = $"presented {shell.FlyoutIsPresented}, reports [{string.Join(",", reports)}]";
                Console.WriteLine($"# reactor shell flyout {behavior}: mount [{mount}]; declared true [{declaredTrue}]; declared false [{declaredFalse}]; " +
                    $"user open, refused [{userOpen}]; unhandled [{string.Join(" | ", context.UnhandledErrors.Select(e => e.Message))}]");
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                Console.WriteLine($"# reactor shell flyout {behavior}: threw {error}; reports [{string.Join(",", reports)}]");
            }
            try { context.Unmount(); } catch { }
            c.Pages.Set(new ContentPage());
            await c.Settle();
        }
    }
}
