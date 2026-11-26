using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Handlers;
using Microsoft.UI.Reactor.Maui.Hosting;
using static Microsoft.UI.Reactor.Maui.Factories;
using iOSPage = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page;
using UIModalPresentationStyle = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.UIModalPresentationStyle;

namespace Microsoft.UI.Reactor.Maui.Tests;

public sealed record LifecycleProbeProps(List<string> Log, string Tag = "", string? Throw = null);

/// <summary>Logs the window's lifecycle, and its own unmount.</summary>
public sealed class WindowLifecycleProbe : Component<LifecycleProbeProps>
{
    public override Element Render()
    {
        var (log, tag) = (Props.Log, Props.Tag);
        void Note(string what)
        {
            log.Add($"{tag}{what}");
            if (Props.Throw == what) throw new InvalidOperationException($"{tag}{what} boom");
        }
        UseWindowLifecycle(
            onActivated: () => Note("activated"),
            onDeactivated: () => Note("deactivated"),
            onStopped: () => Note("stopped"),
            onResumed: () => Note("resumed"),
            onDestroying: () => Note("destroying"));
        UseEffect(() => () => log.Add($"{tag}cleanup"), Array.Empty<object>());
        return Label(tag);
    }
}

// The README's lifecycle snippet.
public sealed class ReadmeAutosave : Component<Action>
{
    public override Element Render()
    {
        var save = Props;
        UseWindowLifecycle(onStopped: save, onDestroying: save);
        return Label("Editing");
    }
}

public class WindowLifecycleTests
{
    private static Element Probe(List<string> log, string tag = "", string? throws = null) =>
        Component<WindowLifecycleProbe, LifecycleProbeProps>(new(log, tag, throws));

    private static void Raise(IWindow window, params string[] events)
    {
        foreach (var e in events)
        {
            switch (e)
            {
                case "activated": window.Activated(); break;
                case "deactivated": window.Deactivated(); break;
                case "stopped": window.Stopped(); break;
                case "resumed": window.Resumed(); break;
                case "destroying": window.Destroying(); break;
            }
        }
    }

    [Fact]
    public void TheCallbacks_FollowTheWindow_AreRefreshed_AndUnsubscribeAtUnmount()
    {
        var log = new List<string>();
        var window = new Window(new ContentPage()) { Parent = DialogTests.App };
        var host = new TestHost(MauiHandlers.CreateRegistry());
        host.Render(MauiContexts.Window.Provide(window, Probe(log, "one:")));
        Raise(window, "activated", "deactivated", "stopped", "resumed");
        Assert.Equal(["one:activated", "one:deactivated", "one:stopped", "one:resumed"], log);

        log.Clear();
        host.Render(MauiContexts.Window.Provide(window, Probe(log, "two:")));
        Raise(window, "activated");
        Assert.Equal(["two:activated"], log);

        log.Clear();
        host.Root.Unmount();
        Raise(window, "deactivated", "stopped", "resumed", "activated");
        // The mount-only effect's cleanup kept its first render's tag.
        Assert.Equal(["one:cleanup"], log);
    }

    [Fact]
    public void ALateWindow_IsFoundWhenTheHostedPageLoads()
    {
        var log = new List<string>();
        var page = new ReactorPage(Probe(log));
        var window = new Window { Parent = DialogTests.App };
        Raise(window, "activated");
        Assert.Empty(log);
        window.Page = page;
        Raise(window, "deactivated");
        Assert.Equal(["deactivated"], log);
    }

    [Fact]
    public void AnErrorInACallback_GoesToTheComponentsBoundary()
    {
        var log = new List<string>();
        var window = new Window(new ContentPage()) { Parent = DialogTests.App };
        var host = new TestHost(MauiHandlers.CreateRegistry());
        host.Render(MauiContexts.Window.Provide(window, ErrorBoundary(Probe(log, throws: "stopped"), e => Label($"caught: {e.Message}"))));
        Raise(window, "stopped");
        host.Scheduler.Drain();
        Assert.Equal("caught: stopped boom", host.View<Label>().Text);
    }

    [Fact]
    public void AReactorWindow_RunsOnDestroying_BeforeItUnmountsItsTree()
    {
        var log = new List<string>();
        var window = new ReactorWindow(Probe(log)) { Parent = DialogTests.App };
        window.UnhandledException += e => log.Add($"error: {e.Message}");
        Raise(window, "destroying");
        Assert.Equal(["destroying", "cleanup"], log);
    }

    [Fact]
    public void OnDestroyingErrors_AreHeld_UntilTheTreeIsUnmounted_ThenReported()
    {
        var log = new List<string>();
        var window = new ReactorWindow(VStack(Probe(log, "a:", throws: "destroying"), Probe(log, "b:"))) { Parent = DialogTests.App };
        window.UnhandledException += e => log.Add($"error: {e.Message}");
        Raise(window, "destroying");
        // Every callback ran, the tree was unmounted, and only then was the error reported.
        Assert.Equal(["a:destroying", "b:destroying", "a:cleanup", "b:cleanup", "error: a:destroying boom"], log);
    }

    [Fact]
    public void OnDestroyingErrors_WithoutASubscriber_AreRethrownAfterTheTeardown()
    {
        var log = new List<string>();
        var window = new ReactorWindow(Probe(log, throws: "destroying")) { Parent = DialogTests.App };
        var error = Assert.Throws<InvalidOperationException>(() => Raise(window, "destroying"));
        Assert.Equal("destroying boom", error.Message);
        Assert.Equal(["destroying", "cleanup"], log);
    }

    [Fact]
    public void TheReadmeSnippet_SavesWhenTheAppStops()
    {
        var saves = 0;
        var window = new ReactorWindow(Component<ReadmeAutosave, Action>(() => saves++)) { Parent = DialogTests.App };
        Raise(window, "stopped");
        Assert.Equal(1, saves);
        Raise(window, "destroying");
        Assert.Equal(2, saves);
    }

    // ── Modal presentation style ────────────────────────────────────────────

    [Fact]
    public void ModalPresentationStyle_IsWrittenBeforeTheModalIsPresented()
    {
        var host = new TestHost(MauiHandlers.CreateRegistry());
        host.Render(NavigationStack([Page("root", null)]) with
        {
            Modals = [Page("sheet", null) with { ModalPresentationStyle = UIModalPresentationStyle.PageSheet }],
        });
        var stack = host.View<ReactorNavigationPage>();
        var window = new Window { Parent = DialogTests.App };
        UIModalPresentationStyle? atPresentation = null;
        window.ModalPushing += (_, e) => atPresentation = iOSPage.GetModalPresentationStyle(e.Modal);
        window.Page = stack;
        for (var i = 0; i < 200 && window.Navigation.ModalStack.Count == 0; i++)
        {
            host.Scheduler.Drain();
            Thread.Sleep(5);
        }
        Assert.Equal("sheet", Assert.Single(window.Navigation.ModalStack).Title);
        Assert.Equal(UIModalPresentationStyle.PageSheet, atPresentation);

        // Unset, MAUI's default applies again.
        var page = new TestHost(MauiHandlers.CreateRegistry());
        page.Render(Page("p", null) with { ModalPresentationStyle = UIModalPresentationStyle.FormSheet });
        var content = page.View<ContentPage>();
        Assert.Equal(UIModalPresentationStyle.FormSheet, iOSPage.GetModalPresentationStyle(content));
        page.Render(Page("p", null));
        Assert.Equal(UIModalPresentationStyle.FullScreen, iOSPage.GetModalPresentationStyle(content));
    }
}
