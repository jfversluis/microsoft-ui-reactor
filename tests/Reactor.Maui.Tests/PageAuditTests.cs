using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Maui.Factories;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>Spec 070 §4.16: a page's mount-only automation id and its lifecycle callbacks.</summary>
public class PageAuditTests
{
    public static TheoryData<string> Registries() => ["controls", "direct"];

    private static TestHost Host(string registry) =>
        new(PropertyMatrixTests.Registries.Single(r => r.Name == registry).Create());

    [Theory]
    [MemberData(nameof(Registries))]
    public void APagesAutomationId_IsSetAtMount_AndAChange_WritesNothing_ThrowsNothing_AndWarnsOncePerPage(string registry)
    {
        using var listener = new WarningListener("Page.AutomationId");
        var host = Host(registry).Render(Page("P", null) with { AutomationId = "first" });
        var page = host.View<ContentPage>();
        Assert.Equal("first", page.AutomationId);

        host.Render(Page("P", null) with { AutomationId = "second" });
        Assert.Same(page, host.View<ContentPage>());
        Assert.Equal("first", page.AutomationId);
        Assert.Single(listener.Warnings);

        host.Render(Page("P", null) with { AutomationId = "third" });
        host.Render(Page("P", null) with { AutomationId = "first" });
        Assert.Equal("first", page.AutomationId);
        Assert.Single(listener.Warnings);

        // An equal render doesn't warn; another page's change does, once.
        var other = Host(registry).Render(Page("Q", null) with { AutomationId = "a" });
        other.Render(Page("Q", null) with { AutomationId = "a" });
        Assert.Single(listener.Warnings);
        other.Render(Page("Q", null) with { AutomationId = "b" });
        Assert.Equal(2, listener.Warnings.Count);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void AViewsAutomationId_IsSetAtMount_AndAChange_WritesNothing_AndWarnsOncePerView(string registry)
    {
        using var listener = new WarningListener("View.AutomationId");
        static string? IdOf(object view) => view switch
        {
            IView v => v.AutomationId,
            _ => null,
        };
        var host = Host(registry).Render(Label("x").AutomationId("first"));
        var view = host.RootView!;
        Assert.Equal("first", IdOf(view));

        host.Render(Label("x").AutomationId("first"));
        Assert.Empty(listener.Warnings); // an equal render doesn't warn
        host.Render(Label("y").AutomationId("second"));
        Assert.Same(view, host.RootView);
        Assert.Equal("first", IdOf(view));
        Assert.Single(listener.Warnings);
        host.Render(Label("y").AutomationId("third"));
        Assert.Single(listener.Warnings);

        var other = Host(registry).Render(Label("x").AutomationId("a"));
        other.Render(Label("x").AutomationId("b"));
        Assert.Equal(2, listener.Warnings.Count);
    }

    /// <summary>
    /// An element without modifiers between two with ids: the update passes null modifiers, which is
    /// not a mount (MAUI throws when an id is set twice; a direct view would overwrite it).
    /// </summary>
    [Theory]
    [MemberData(nameof(Registries))]
    public void AViewsAutomationId_ThroughAnElementWithoutModifiers_IsNotSetAgain(string registry)
    {
        using var listener = new WarningListener("View.AutomationId");
        var errors = new List<Exception>();
        var host = Host(registry);
        host.Root.UnhandledException += errors.Add;
        host.Render(Label("x").AutomationId("a"));
        var view = (IView)host.RootView!;

        host.Render(Label("x"));
        host.Render(Label("x").AutomationId("b"));

        Assert.Empty(errors);
        Assert.Same(view, host.RootView);
        Assert.Equal("a", view.AutomationId);
        Assert.Single(listener.Warnings);
    }

    /// <summary>
    /// MAUI raises Appearing only for a page in an app's window and not twice in a row, and Disappearing
    /// only for an appeared page, so each trigger is re-armed, and the page's own events witness that
    /// every trigger fired, after Reactor's unmount too.
    /// </summary>
    [Theory]
    [MemberData(nameof(Registries))]
    public void APagesLifecycle_ReachesTheCurrentCallbacks_AndNothingAfterUnmount(string registry)
    {
        var log = new List<string>();
        PageElement Tree(string generation) => Page("P", null) with
        {
            OnAppearing = () => log.Add("appearing " + generation),
            OnDisappearing = () => log.Add("disappearing " + generation),
        };
        var host = Host(registry).Render(Tree("1"));
        var page = host.View<ContentPage>();
        _ = new Window(page) { Parent = ShellTests.App };
        var native = new List<string>();
        page.Appearing += (_, _) => native.Add("appearing");
        page.Disappearing += (_, _) => native.Add("disappearing");

        void Cycle()
        {
            page.SendAppearing();
            page.SendDisappearing();
            host.Scheduler.Drain();
        }

        Cycle();
        Assert.Equal(["appearing", "disappearing"], native);
        Assert.Equal(["appearing 1", "disappearing 1"], log);

        host.Render(Tree("2"));
        Cycle();
        Assert.Equal(4, native.Count);
        Assert.Equal(["appearing 1", "disappearing 1", "appearing 2", "disappearing 2"], log);

        host.Root.Unmount();
        Cycle();
        Assert.Equal(["appearing", "disappearing", "appearing", "disappearing", "appearing", "disappearing"], native);
        Assert.Equal(4, log.Count);
    }
}
