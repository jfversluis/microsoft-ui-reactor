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
    ];

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
