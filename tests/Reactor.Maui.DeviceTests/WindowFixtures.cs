using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui;
using static Microsoft.UI.Reactor.Maui.Factories;
using static Reactor.Maui.DeviceTests.FixtureContext;

namespace Reactor.Maui.DeviceTests;

public static partial class Fixtures
{
    private static IEnumerable<Fixture> WindowFixtures() =>
    [
        new("Window_ReplacingThePageWhileAStackNavigates_ShowsTheNewPage", ReplaceDuringPop),
    ];

    // Replacing the window's page while a navigation stack is pushing or popping. Phase 3e saw MAUI crash
    // on Android here ("CurrentPage cannot be null" in StackNavigationManager) on one emulator setup;
    // it doesn't reproduce on the current one, with or without a deferral, so this fixture is coverage,
    // not a regression test for a fix (spec 065, open issues).
    private static async Task ReplaceDuringPop(FixtureContext c)
    {
        for (var round = 0; round < 4; round++)
        {
            var log = new List<string>();
            Action<int>? setDepth = null;
            c.Show(Component<LifecycleStack, LifecycleProps>(new(log, s => setDepth = s)));
            var navigation = await LoadedPage<NavigationPage>(c);
            await c.Settle();
            setDepth!(2);
            await c.WaitFor(() => navigation.CurrentPage?.Title == "detail" && navigation.CurrentPage.IsLoaded, "detail to be shown");
            await c.Settle();

            // The app pops (or, every other round, pushes) a page, and while the navigator is running
            // that platform operation, replaces the whole window's content.
            var navigator = ((Microsoft.UI.Reactor.Maui.Handlers.ReactorNavigationPage)navigation).Navigator;
            setDepth(round % 2 == 0 ? 1 : 3);
            await c.WaitFor(() => !navigator.IsIdle, "the navigator to start");
            c.Show(Label($"replaced {round}").AutomationId("replaced"));
            await c.WaitFor(() => c.TryFind<ILabel>(l => l.Text == $"replaced {round}") is { } label && ((VisualElement)label).IsLoaded,
                "the new content to be shown", 10000);
            Check(c.UnhandledErrors.Count == 0, $"no unhandled errors, got {string.Join("; ", c.UnhandledErrors.Select(e => e.Message))}");
        }
    }
}
