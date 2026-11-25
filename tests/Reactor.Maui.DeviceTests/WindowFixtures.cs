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
        new("Window_ReplacingThePageDuringRepeatedPushesAndPops_DoesNotCrash", c => ReplaceRepeatedly(c, inTabs: false)),
        new("Window_ReplacingTabsWhileTheirStackPushesAndPops_DoesNotCrash", c => ReplaceRepeatedly(c, inTabs: true)),
    ];

    // Replacing the window's page while a navigation stack is pushing or popping. Without
    // WindowPageSetter's wait, MAUI crashes on Android ("NavigationManager cannot be null here", from
    // NavigationViewFragment.OnCreateView); this fixture hit it in 2 of about 13 runs, and the next one
    // is the reliable regression test. The swap goes through the same root, as ReactorWindow.Render
    // does: the reconciler unmounts the stack (stopping its navigator) and then sets the window's
    // page, while the platform push or pop is still running.
    private static async Task ReplaceDuringPop(FixtureContext c)
    {
        for (var round = 0; round < 8; round++)
        {
            var log = new List<string>();
            Action<int>? setDepth = null;
            c.Unmount();
            c.Show(Component<LifecycleStack, LifecycleProps>(new(log, s => setDepth = s)));
            var navigation = await LoadedPage<NavigationPage>(c);
            await c.Settle();
            setDepth!(2);
            await c.WaitFor(() => navigation.CurrentPage?.Title == "detail" && navigation.CurrentPage.IsLoaded, "detail to be shown");
            var navigator = ((Microsoft.UI.Reactor.Maui.Handlers.ReactorNavigationPage)navigation).Navigator;
            await c.WaitFor(() => navigator.IsIdle, "the push of detail to finish");
            await c.Settle();
            Check(navigator.IsIdle, "the navigator is idle before the change, so a busy navigator below comes from it");

            // The app pops (or, every other round, pushes) a page, and while the navigator is running
            // that platform operation, replaces the whole window's content: in the first four rounds as
            // soon as the navigator starts, in the last four once MAUI's stack has changed and the
            // animation is still running (the timing phase 3e crashed at).
            var pop = round % 2 == 0;
            setDepth(pop ? 1 : 3);
            await c.WaitFor(() => !navigator.IsIdle, "the navigator to start");
            if (round >= 4)
            {
                await c.WaitFor(() => navigation.Navigation.NavigationStack.Count == (pop ? 1 : 3), "MAUI's stack to change");
                Check(!navigator.IsIdle, "the swap happens while the operation is still running");
            }
            Console.WriteLine($"#   round {round}: {(pop ? "pop" : "push")}, swap {(round >= 4 ? "after the stack changed" : "as the navigator starts")}");
            c.Render(Label($"replaced {round}").AutomationId("replaced"));
            if (c.Pages.WaitForNavigation)
                Check(ReferenceEquals(c.Window.Page, navigation), "on Android the swap waits for the running navigation");
            await c.WaitFor(() => c.TryFind<ILabel>(l => l.Text == $"replaced {round}") is { } label && ((VisualElement)label).IsLoaded,
                "the new content to be shown", 10000);

            // The interrupted operation still finishes on the detached page; nothing it does afterwards
            // may crash or take the window back.
            await c.WaitFor(() => navigator.IsIdle, "the interrupted navigation to finish", 10000);
            await c.Settle();
            Check(c.TryFind<ILabel>(l => l.Text == $"replaced {round}") is not null, "the window still shows the new content");
            Check(!ReferenceEquals(c.Window.Page, navigation), "the navigation page is no longer the window's page");
            Check(c.UnhandledErrors.Count == 0, $"no unhandled errors, got {string.Join("; ", c.UnhandledErrors.Select(e => e.Message))}");
        }
    }

    // The Android crash's regression test. Each round swaps the window's content once MAUI's stack has
    // changed, alternating pops and pushes. Without WindowPageSetter's wait, MAUI crashed in round 7
    // (the fourth push) in 5 of 10 runs; with it, 0 of 18. A single push, or swaps swept across the
    // push animation, didn't reproduce it: it needs the earlier interrupted navigations. The Tabs
    // variant didn't crash without the wait either (0 of 6); it covers the setter's nested-stack check.
    // inTabs puts the stack in the first tab of a Tabs root, so the window's outgoing page is the
    // TabbedPage, not the navigation page.
    private static async Task ReplaceRepeatedly(FixtureContext c, bool inTabs)
    {
        var pushesWhileRunning = 0;
        for (var round = 0; round < 12; round++)
        {
            Action<int>? setDepth = null;
            c.Unmount();
            Element stack = Component<LifecycleStack, LifecycleProps>(new([], s => setDepth = s));
            c.Show(inTabs ? Tabs(0, null, stack, Page("other", Label("other"))) : stack);
            NavigationPage navigation;
            Page outgoing;
            if (inTabs)
            {
                var tabs = await LoadedPage<TabbedPage>(c);
                await c.WaitFor(() => tabs.Children.FirstOrDefault() is NavigationPage { IsLoaded: true }, "the tab's stack to load");
                (navigation, outgoing) = ((NavigationPage)tabs.Children[0], tabs);
            }
            else
            {
                navigation = await LoadedPage<NavigationPage>(c);
                outgoing = navigation;
            }
            var navigator = ((Microsoft.UI.Reactor.Maui.Handlers.ReactorNavigationPage)navigation).Navigator;
            setDepth!(2);
            await c.WaitFor(() => navigation.CurrentPage?.Title == "detail" && navigator.IsIdle, "the push of detail to finish");
            await c.Settle();
            var pop = round % 2 == 0;
            setDepth(pop ? 1 : 3);
            await c.WaitFor(() => navigation.Navigation.NavigationStack.Count == (pop ? 1 : 3), "MAUI's stack to change");
            var running = !navigator.IsIdle;
            if (running && !pop) pushesWhileRunning++;
            Console.WriteLine($"#   round {round}: {(pop ? "pop" : "push")}, running {running}");
            c.Render(Label($"replaced {round}").AutomationId("replaced"));
            if (running && c.Pages.WaitForNavigation)
                Check(ReferenceEquals(c.Window.Page, outgoing), "on Android the swap waits for the running navigation");
            await c.WaitFor(() => c.TryFind<ILabel>(l => l.Text == $"replaced {round}") is { } label && ((VisualElement)label).IsLoaded,
                "the new content to be shown", 10000);
            await c.WaitFor(() => navigator.IsIdle, "the interrupted navigation to finish", 10000);
            await c.Settle();
        }
        // The swaps must land mid-push for the test to exercise the crash; a timing shift that lets
        // the push finish first fails here instead of passing without testing anything.
        Check(pushesWhileRunning >= 4, $"at least 4 of 6 pushes still running at the swap, got {pushesWhileRunning}");
        Check(c.UnhandledErrors.Count == 0, $"no unhandled errors, got {string.Join("; ", c.UnhandledErrors.Select(e => e.Message))}");
    }
}
