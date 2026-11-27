using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>
/// Spec 070 §4.16.2 group N: a navigation stack's pages and modals, declared and popped by the user,
/// its animation, and its two callbacks (the current one; nothing after unmount). Both registries.
/// </summary>
public class NavigationStackAuditTests
{
    public static TheoryData<string> Registries() => ["controls", "direct"];

    private static TestHost Host(string registry) =>
        new(PropertyMatrixTests.Registries.Single(r => r.Name == registry).Create());

    private static void Settle(TestHost host, Func<bool> done, Func<string>? state = null)
    {
        for (var i = 0; i < 400 && !done(); i++)
        {
            host.Scheduler.Drain();
            Thread.Sleep(5);
        }
        host.Scheduler.Drain();
        Assert.True(done(), "the stack settled: " + state?.Invoke());
    }

    private static string Titles(IEnumerable<Page> pages) => string.Join(",", pages.Select(p => p.Title));

    [Theory]
    [MemberData(nameof(Registries))]
    public void Pages_DeclaredChangesAreUnreported_AUsersPopIsReportedWithItsDepth_RefusedOrAccepted_ToTheCurrentCallback(string registry)
    {
        var log = new List<string>();
        Element Stack(string generation, bool animated, params string[] routes) =>
            NavigationStack(routes.Select(r => Page(r, null).WithKey(r)), depth => log.Add($"{generation} {depth}")) with { Animated = animated };
        var host = Host(registry).Render(Stack("first", true, "home", "detail"));
        var navigation = host.View<ReactorNavigationPage>();
        var pages = navigation.Navigation.NavigationStack;
        Settle(host, () => pages.Count == 2);

        // Declared pushes and pops: written, unreported, animated as declared.
        host.Render(Stack("first", false, "home", "detail", "more"));
        Settle(host, () => pages.Count == 3);
        Assert.False(navigation.Navigator.LastOperationAnimated);
        host.Render(Stack("first", true, "home", "detail"));
        Settle(host, () => pages.Count == 2);
        Assert.True(navigation.Navigator.LastOperationAnimated);
        Assert.Empty(log);

        // The user pops: reported once with the depth left; refused, pushed back.
        var detail = navigation.CurrentPage;
        _ = navigation.PopAsync();
        navigation.ReportPopped!(detail); // headless MAUI raises no Popped: what its handler raises
        Settle(host, () => pages.Count == 2 && log.Count == 1);
        Assert.Equal(["first 1"], log);
        Assert.Equal("home,detail", Titles(pages));

        // The current callback, then accepted: kept, not reported again.
        host.Render(Stack("second", true, "home", "detail"));
        detail = navigation.CurrentPage;
        var report = navigation.ReportPopped!;
        _ = navigation.PopAsync();
        report(detail);
        host.Render(Stack("second", true, "home"));
        Settle(host, () => pages.Count == 1);
        Assert.Equal(["first 1", "second 1"], log);
        Assert.Equal("home", Titles(pages));

        // A late platform pop after unmount reports nothing.
        host.Render(Stack("third", true, "home", "detail"));
        Settle(host, () => pages.Count == 2);
        detail = navigation.CurrentPage;
        report = navigation.ReportPopped!;
        host.Root.Unmount();
        report(detail);
        host.Scheduler.Drain();
        Assert.Equal(2, log.Count);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void Modals_DeclaredChangesAreUnreported_AUsersDismissalIsReported_RefusedOrAccepted_ToTheCurrentCallback(string registry)
    {
        var log = new List<string>();
        Element Stack(string generation, params string[] modals) =>
            NavigationStack([Page("root", null)]) with
            {
                Modals = [.. modals.Select(m => Page(m, null).WithKey(m))],
                OnModalDismissed = remaining => log.Add($"{generation} {remaining}"),
            };
        // The stack listens to the application's ModalPopped from mount: the app exists first.
        var app = DialogTests.App;
        var host = Host(registry).Render(Stack("first", "sheet"));
        var stack = host.View<ReactorNavigationPage>();
        var window = new Window { Parent = app };
        var witnessed = 0;
        app.ModalPopped += (_, _) => witnessed++;
        window.Page = stack;
        var modals = window.Navigation.ModalStack;
        Settle(host, () => modals.Count == 1);

        // Declared: a second modal pushed, then removed; unreported.
        host.Render(Stack("first", "sheet", "over"));
        Settle(host, () => modals.Count == 2);
        host.Render(Stack("first", "sheet"));
        Settle(host, () => modals.Count == 1);
        Assert.Empty(log);

        // The user dismisses: reported with the modals left; refused, presented again.
        var popped = witnessed;
        _ = window.Navigation.PopModalAsync();
        Settle(host, () => log.Count == 1 && modals.Count == 1, () => $"log [{string.Join(";", log)}] modals [{Titles(modals)}]");
        Assert.True(witnessed > popped, "the platform's dismissal raised ModalPopped");
        Assert.Equal(["first 0"], log);
        Assert.Equal("sheet", Titles(modals));

        // The current callback, then accepted.
        host.Render(Stack("second", "sheet"));
        _ = window.Navigation.PopModalAsync();
        Settle(host, () => log.Count == 2);
        host.Render(Stack("second"));
        Settle(host, () => modals.Count == 0);
        Assert.Equal(["first 0", "second 0"], log);

        // Unmounted, the stack dismisses the modal it presented: the platform raises ModalPopped (the
        // witness), and nothing is reported.
        host.Render(Stack("third", "sheet"));
        Settle(host, () => modals.Count == 1);
        popped = witnessed;
        host.Root.Unmount();
        for (var i = 0; i < 200 && witnessed == popped; i++)
        {
            host.Scheduler.Drain();
            Thread.Sleep(5);
        }
        host.Scheduler.Drain();
        Assert.True(witnessed > popped, $"the dismissal raised ModalPopped: modals [{Titles(modals)}]");
        Assert.Empty(modals);
        Assert.Equal(2, log.Count);
    }
}
