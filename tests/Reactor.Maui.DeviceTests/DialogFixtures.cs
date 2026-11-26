using System.Diagnostics;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui;
using Microsoft.UI.Reactor.Maui.Handlers;
using Microsoft.UI.Reactor.Navigation;
using static Microsoft.UI.Reactor.Maui.Factories;
using static Reactor.Maui.DeviceTests.FixtureContext;
using UIModalPresentationStyle = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.UIModalPresentationStyle;

namespace Reactor.Maui.DeviceTests;

/// <summary>
/// Spec 069 §5 on the platform: MAUI's page dialogs answered through their native buttons, the
/// one-at-a-time queue, the Android back key reaching <c>UseSystemBackButton</c>, the window's
/// lifecycle, and a page-sheet modal.
/// </summary>
public static partial class Fixtures
{
    private static IEnumerable<Fixture> DialogFixtures() =>
    [
        new("Dialogs_OneButtonAlert_IsAnsweredThroughItsNativeButton", ClosingDialogs(DialogOneButton)),
        new("Dialogs_TwoButtonAlert_IsAnsweredThroughItsNativeButtons", ClosingDialogs(DialogTwoButtons)),
        new("Dialogs_ActionSheet_ReportsTheNativeButtonChosen", ClosingDialogs(DialogActionSheet)),
        new("Dialogs_Prompt_ReportsTheTextTyped", ClosingDialogs(DialogPrompt)),
        new("Dialogs_Declarative_ReportsTheAnswerToOnClosed", ClosingDialogs(DialogDeclarative)),
        new("Dialogs_TwoQueued_AreNeverShownAtOnce", ClosingDialogs(DialogTwoQueued)),
        new("Dialogs_RequestedDuringANavigation_WaitForIt", ClosingDialogs(DialogDuringNavigation)),
        new("SystemBack_HandlerRunsBeforeTheNavigationHost", BackBeforeHost),
        new("SystemBack_AtTheRoot_AHandlerKeepsTheApp_AndWithoutOneTheDefaultRuns", BackAtTheRoot),
        new("SystemBack_HandleOverload_ConsumesABackTheGuardRefuses", BackHandleOverload),
        new("WindowLifecycle_StoppedAndResumed_WhenTheAppIsBackgrounded", LifecycleBackgrounded),
        new("ModalPresentationStyle_PageSheet_OnIOS", ModalPageSheet),
    ];

    // ── Dialogs ─────────────────────────────────────────────────────────────

    /// <summary>Runs a dialog fixture, then closes any native dialog it left (a failure must not block the next fixture).</summary>
    private static Func<FixtureContext, Task> ClosingDialogs(Func<FixtureContext, Task> fixture) => async c =>
    {
        try
        {
            await fixture(c);
        }
        finally
        {
            for (var i = 0; i < 5 && NativeDialogs.Count(c.Window) > 0; i++)
            {
                NativeDialogs.DismissAll(c.Window);
                await c.Settle();
            }
        }
    };

    private sealed class DialogsPage : Component<Action<Dialogs>>
    {
        public override Element Render()
        {
            Props(UseDialogs());
            return Page("dialogs", Label("dialogs"));
        }
    }

    private static async Task<Dialogs> ShowDialogs(FixtureContext c)
    {
        Dialogs? dialogs = null;
        c.Show(Component<DialogsPage, Action<Dialogs>>(d => dialogs = d));
        await LoadedPage<ContentPage>(c);
        await c.Settle();
        return dialogs!;
    }

    /// <summary>Waits for the native dialog, answers it through <paramref name="button"/>, and waits for it to go.</summary>
    private static async Task<T> AnswerNatively<T>(FixtureContext c, Task<T> answer, string button, string? text = null, string? expect = null)
    {
        await c.WaitFor(() => NativeDialogs.IsReady(c.Window), "the native dialog to be shown");
        if (expect is not null) Check(NativeDialogs.Describe(c.Window).Contains(expect, StringComparison.Ordinal), $"the dialog shows '{expect}': {NativeDialogs.Describe(c.Window)}");
        Check(!answer.IsCompleted, "no answer before the native button");
        NativeDialogs.Answer(c.Window, button, text);
        var result = await answer.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await c.WaitFor(() => NativeDialogs.Count(c.Window) == 0, "the native dialog to disappear");
        }
        catch (CheckFailedException e)
        {
            throw new CheckFailedException($"{e.Message}; on screen: {NativeDialogs.Describe(c.Window)}");
        }
        return result;
    }

    private static async Task DialogOneButton(FixtureContext c)
    {
        var dialogs = await ShowDialogs(c);
        var closed = dialogs.Alert("Saved", "Your note was saved.", "OK").ContinueWith(static t => { t.GetAwaiter().GetResult(); return true; });
        Check(await AnswerNatively(c, closed, "OK", expect: "Saved"), "the alert's task completed");
    }

    private static async Task DialogTwoButtons(FixtureContext c)
    {
        var dialogs = await ShowDialogs(c);
        Equal(true, await AnswerNatively(c, dialogs.Alert("Delete?", "It can't be undone.", "Delete", "Keep"), "Delete", expect: "Delete?"), "accepted");
        Equal(false, await AnswerNatively(c, dialogs.Alert("Delete?", null, "Delete", "Keep"), "Keep"), "cancelled");
    }

    private static async Task DialogActionSheet(FixtureContext c)
    {
        var dialogs = await ShowDialogs(c);
        Equal("Messages", await AnswerNatively(c, dialogs.ActionSheet("Share", "Cancel", null, "Mail", "Messages"), "Messages", expect: "Mail"), "the button chosen");
        Equal("Cancel", await AnswerNatively(c, dialogs.ActionSheet("Share", "Cancel", "Delete", "Mail"), "Cancel"), "the cancel button");
    }

    private static async Task DialogPrompt(FixtureContext c)
    {
        var dialogs = await ShowDialogs(c);
        Equal("Ada", await AnswerNatively(c, dialogs.Prompt("Name", "Who are you?", initialValue: "x"), "OK", text: "Ada", expect: "Name"), "the text typed");
        Equal(null, await AnswerNatively(c, dialogs.Prompt("Name", null), "Cancel", text: "ignored"), "a cancelled prompt");
    }

    private sealed record DeclarativeDialogProps(Action<Action<bool>> Report, List<bool> Closed, TaskCompletionSource<bool> Answered);

    private sealed class DeclarativeDialogPage : Component<DeclarativeDialogProps>
    {
        public override Element Render()
        {
            var (open, setOpen) = UseState(false);
            Props.Report(setOpen);
            return Page("declarative", VStack(
                Label("declarative"),
                Alert("Discard?", "Your changes will be lost.", "Discard", "Keep") with
                {
                    IsOpen = open,
                    OnClosed = discard =>
                    {
                        Props.Closed.Add(discard);
                        Props.Answered.TrySetResult(discard);
                        setOpen(false);
                    },
                }));
        }
    }

    private static async Task DialogDeclarative(FixtureContext c)
    {
        Action<bool>? setOpen = null;
        var closed = new List<bool>();
        var answered = new TaskCompletionSource<bool>();
        c.Show(Component<DeclarativeDialogPage, DeclarativeDialogProps>(new(s => setOpen = s, closed, answered)));
        await LoadedPage<ContentPage>(c);
        await c.Settle();
        Check(NativeDialogs.Count(c.Window) == 0, "nothing shown while closed");
        setOpen!(true);
        Equal(true, await AnswerNatively(c, answered.Task, "Discard", expect: "Discard?"), "OnClosed's answer");
        await c.Settle();
        Equal(1, closed.Count, "OnClosed calls");
        Check(NativeDialogs.Count(c.Window) == 0, "closed for good");
    }

    /// <summary>Samples the native dialogs every frame; records the most ever shown at once.</summary>
    private sealed class DialogSampler
    {
        private bool _running = true;
        public int MostAtOnce { get; private set; }
        public List<string> Violations { get; } = [];

        public DialogSampler(FixtureContext c, Func<string?>? violation = null)
        {
            _ = Run();
            async Task Run()
            {
                while (_running)
                {
                    MostAtOnce = Math.Max(MostAtOnce, NativeDialogs.Count(c.Window));
                    if (violation?.Invoke() is { } v) Violations.Add(v);
                    await Task.Delay(8);
                }
            }
        }

        public void Stop() => _running = false;
    }

    private static async Task DialogTwoQueued(FixtureContext c)
    {
        var dialogs = await ShowDialogs(c);
        var sampler = new DialogSampler(c);
        var first = dialogs.Alert("First", null, "Yes", "No");
        var second = dialogs.ActionSheet("Second", "Cancel", null, "One", "Two");
#if ANDROID
        // Android runs a dialog button's click and the dialog's dismissal in one looper turn, so no
        // frame could show both dialogs even without the barrier. What the barrier guarantees there is
        // that the first dialog's window is gone (the activity has the focus back) before the second
        // is added; without it, the focus goes straight from one dialog to the next.
        await c.WaitFor(() => NativeDialogs.IsReady(c.Window), "the first dialog to be shown");
        var decor = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity!.Window!.DecorView;
        var activityFocused = 0;
        var listener = new FocusListener(focused => { if (focused) activityFocused++; });
        decor.ViewTreeObserver!.AddOnWindowFocusChangeListener(listener);
        try
        {
            Equal(true, await AnswerNatively(c, first, "Yes", expect: "First"), "the first answer");
            await c.WaitFor(() => NativeDialogs.IsReady(c.Window), "the second dialog to be shown");
            Equal(1, activityFocused, "times the activity had the focus between the two dialogs");
        }
        finally
        {
            decor.ViewTreeObserver!.RemoveOnWindowFocusChangeListener(listener);
        }
#else
        Equal(true, await AnswerNatively(c, first, "Yes", expect: "First"), "the first answer");
#endif
        await c.WaitFor(() => NativeDialogs.IsReady(c.Window), "the second dialog to be shown");
        Check(NativeDialogs.Describe(c.Window).Contains("Second", StringComparison.Ordinal) || NativeDialogs.Describe(c.Window).Contains("Two", StringComparison.Ordinal),
            $"the second dialog is shown: {NativeDialogs.Describe(c.Window)}");
        Equal("Two", await AnswerNatively(c, second, "Two"), "the second answer");
        sampler.Stop();
        Equal(1, sampler.MostAtOnce, "most native dialogs on screen at once");
    }

#if ANDROID
    private sealed class FocusListener(Action<bool> changed) : Java.Lang.Object, Android.Views.ViewTreeObserver.IOnWindowFocusChangeListener
    {
        public void OnWindowFocusChanged(bool hasFocus) => changed(hasFocus);
    }
#endif

    private sealed record DialogStackProps(Action<RouteStack<string>, Dialogs> Report);

    private sealed class DialogStackApp : Component<DialogStackProps>
    {
        public override Element Render()
        {
            var stack = UseRouteStack("home");
            Props.Report(stack, UseDialogs());
            return NavigationStack(stack.Items.Select(route => Page(route, Label($"{route} page")).WithKey(route)), stack.PopTo);
        }
    }

    private static async Task DialogDuringNavigation(FixtureContext c)
    {
        RouteStack<string>? stack = null;
        Dialogs? dialogs = null;
        c.Show(Component<DialogStackApp, DialogStackProps>(new((s, d) => (stack, dialogs) = (s, d))));
        var navigation = await LoadedPage<NavigationPage>(c);
        var navigator = ((ReactorNavigationPage)navigation).Navigator;
        await c.WaitFor(() => navigator.IsIdle, "the root to settle");
        await c.Settle();

        stack!.Push("detail");
        await c.WaitFor(() => !navigator.IsIdle, "the push to start");
        var sampler = new DialogSampler(c, () => NativeDialogs.Count(c.Window) > 0 && !navigator.IsIdle ? "a dialog was shown during the push" : null);
        var answer = dialogs!.Alert("During", "Requested while pushing.", "Yes", "No");
        Equal(true, await AnswerNatively(c, answer, "Yes", expect: "During"), "the answer");
        sampler.Stop();
        Equal(0, sampler.Violations.Count, "dialogs shown while the push ran");
        Equal("detail", navigation.CurrentPage?.Title, "the page the push showed");
    }

    // ── System back (Android) ──────────────────────────────────────────────

    private sealed class BackLog
    {
        public List<string> Entries { get; } = [];
        public HashSet<string> Handles { get; } = [];
        public HashSet<string> Refuses { get; } = [];
    }

    private sealed record BackAppProps(BackLog Log, Action<NavigationHandle<string>> Report, bool HandleOverload);

    private sealed class BackApp : Component<BackAppProps>
    {
        public override Element Render()
        {
            var nav = UseNavigation("home");
            Props.Report(nav);
            if (Props.HandleOverload) UseSystemBackButton(nav);
            var log = Props.Log;
            return NavigationHost(nav, route => Component<BackPage, BackPageProps>(new(route, log)));
        }
    }

    private sealed record BackPageProps(string Route, BackLog Log);

    private sealed class BackPage : Component<BackPageProps>
    {
        public override Element Render()
        {
            var (route, log) = (Props.Route, Props.Log);
            UseSystemBackButton(() =>
            {
                log.Entries.Add($"back:{route}");
                return log.Handles.Contains(route);
            });
            UseNavigationLifecycle(onNavigatingFrom: context =>
            {
                log.Entries.Add($"guard:{route}");
                if (log.Refuses.Contains(route)) context.Cancel();
            });
            return Page(route, Label(route));
        }
    }

    private static async Task<(NavigationHandle<string> Nav, ReactorNavigationPage Page, BackLog Log)> ShowBackApp(FixtureContext c, bool handleOverload)
    {
        var log = new BackLog();
        NavigationHandle<string>? nav = null;
        c.Show(Component<BackApp, BackAppProps>(new(log, n => nav = n, handleOverload)));
        var page = (ReactorNavigationPage)await LoadedPage<NavigationPage>(c);
        await c.Settle();
        return (nav!, page, log);
    }

    private static async Task SettledAt(FixtureContext c, ReactorNavigationPage page, string titles, string what)
    {
        await c.WaitFor(() => string.Join(",", page.Navigation.NavigationStack.Select(p => p.Title)) == titles
            && page.Navigator.IsIdle && page.CurrentPage?.IsLoaded == true, what);
        await c.Settle();
    }

#if ANDROID
    private static AndroidX.Activity.OnBackPressedDispatcher BackDispatcher =>
        ((AndroidX.Activity.ComponentActivity)Microsoft.Maui.ApplicationModel.Platform.CurrentActivity!).OnBackPressedDispatcher;
#endif

    private static async Task BackBeforeHost(FixtureContext c)
    {
#if ANDROID
        var (nav, page, log) = await ShowBackApp(c, handleOverload: false);
        nav.Navigate("detail");
        await SettledAt(c, page, "home,detail", "the push");
        log.Entries.Clear();

        log.Handles.Add("detail");
        BackDispatcher.OnBackPressed();
        await c.Settle(600);
        Equal("back:detail", string.Join(",", log.Entries), "a handling handler stops the back");
        Equal("home,detail", string.Join(",", page.Navigation.NavigationStack.Select(p => p.Title)), "the stack");

        log.Entries.Clear();
        log.Handles.Clear();
        BackDispatcher.OnBackPressed();
        await SettledAt(c, page, "home", "the host's back");
        Equal("back:detail,guard:detail", string.Join(",", log.Entries), "the handler, then the host's guard, once each");
        Equal("home", nav.CurrentRoute, "the handle's route");
#else
        Console.WriteLine("#   Android only: iOS and Mac Catalyst have no back key");
        await Task.CompletedTask;
#endif
    }

    private static async Task BackAtTheRoot(FixtureContext c)
    {
#if ANDROID
        var (_, page, log) = await ShowBackApp(c, handleOverload: false);
        var activity = (MauiAppCompatActivity)Microsoft.Maui.ApplicationModel.Platform.CurrentActivity!;
        var mauiCallback = (AndroidX.Activity.OnBackPressedCallback?)typeof(MauiAppCompatActivity)
            .GetField("_mauiOnBackPressedCallback", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(activity)
            ?? throw new CheckFailedException("MAUI's back callback was not found (MauiAppCompatActivity changed)");
        // A Reactor page overrides OnBackButtonPressed, so MAUI keeps its callback enabled at the root:
        // the key reaches the page (and Android shows no predictive back-to-home preview).
        Check(mauiCallback.Enabled, "MAUI's back callback is enabled at the root");

        log.Handles.Add("home");
        BackDispatcher.OnBackPressed();
        await c.Settle();
        Equal("back:home", string.Join(",", log.Entries), "the root page's handler");
        Check(!activity.IsFinishing && ReferenceEquals(c.Window.Page, page) && activity.HasWindowFocus, "the app stays in front");

        // Without a handler handling it, MAUI passes the key to the activity's default (which moves the
        // task to the back). A callback below MAUI's stands in for that default, so the app stays.
        log.Entries.Clear();
        log.Handles.Clear();
        var reachedDefault = 0;
        var sentinel = new StandInCallback(() => reachedDefault++);
        BackDispatcher.AddCallback(activity, sentinel);
        mauiCallback.Remove();
        BackDispatcher.AddCallback(activity, mauiCallback);
        try
        {
            BackDispatcher.OnBackPressed();
        }
        finally
        {
            sentinel.Remove();
        }
        await c.Settle();
        Equal("back:home", string.Join(",", log.Entries), "the root page's handler, unhandled");
        Equal(1, reachedDefault, "the platform default reached");
        Check(mauiCallback.Enabled, "MAUI re-enabled its callback");
#else
        Console.WriteLine("#   Android only: iOS and Mac Catalyst have no back key");
        await Task.CompletedTask;
#endif
    }

#if ANDROID
    private sealed class StandInCallback(Action onBack) : AndroidX.Activity.OnBackPressedCallback(true)
    {
        public override void HandleOnBackPressed() => onBack();
    }
#endif

    private static async Task BackHandleOverload(FixtureContext c)
    {
#if ANDROID
        var (nav, page, log) = await ShowBackApp(c, handleOverload: true);
        nav.Navigate("detail");
        await SettledAt(c, page, "home,detail", "the push");
        log.Entries.Clear();
        log.Refuses.Add("detail");
        var pops = 0;
        page.Popped += (_, _) => pops++;
        BackDispatcher.OnBackPressed();
        await c.Settle(800);
        Equal("back:detail,guard:detail", string.Join(",", log.Entries), "the page's handler, then the handle's guarded back, once");
        Equal(0, pops, "platform pops for a refused back");
        Equal("home,detail", string.Join(",", page.Navigation.NavigationStack.Select(p => p.Title)), "the stack after a refused back");

        log.Entries.Clear();
        log.Refuses.Clear();
        BackDispatcher.OnBackPressed();
        await SettledAt(c, page, "home", "the accepted back");
        Equal("back:detail,guard:detail", string.Join(",", log.Entries), "the accepted back's handlers");
#else
        Console.WriteLine("#   Android only: iOS and Mac Catalyst have no back key");
        await Task.CompletedTask;
#endif
    }

    // ── Window lifecycle ────────────────────────────────────────────────────

    private sealed class LifecyclePage : Component<List<string>>
    {
        public override Element Render()
        {
            var log = Props;
            UseWindowLifecycle(
                onActivated: () => log.Add("activated"),
                onDeactivated: () => log.Add("deactivated"),
                onStopped: () => log.Add("stopped"),
                onResumed: () => log.Add("resumed"));
            return Page("lifecycle", Label("lifecycle"));
        }
    }

    private static async Task LifecycleBackgrounded(FixtureContext c)
    {
#if ANDROID
        var log = new List<string>();
        c.Show(Component<LifecyclePage, List<string>>(log));
        await LoadedPage<ContentPage>(c);
        await c.Settle();
        log.Clear();
        // Another activity covering the app's, as the home screen does: the window stops.
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity!;
        activity.StartActivity(new Android.Content.Intent(activity, typeof(CoverActivity)));
        await c.WaitFor(() => log.Contains("stopped"), "the window to stop", 10000);
        CoverActivity.Current?.Finish();
        await c.WaitFor(() => log.Contains("activated") && log.IndexOf("activated") > log.IndexOf("stopped"), "the window to come back", 10000);
        Equal("deactivated,stopped,resumed,activated", string.Join(",", log), "the lifecycle");
#else
        Console.WriteLine("#   Android only: iOS and Mac Catalyst apps can't background themselves in process");
        await Task.CompletedTask;
#endif
    }

    // ── Modal presentation style ────────────────────────────────────────────

    private static async Task ModalPageSheet(FixtureContext c)
    {
        var sheet = Page("sheet", Label("sheet")) with { ModalPresentationStyle = UIModalPresentationStyle.PageSheet };
        c.Show(NavigationStack([Page("root", Label("root"))]) with { Modals = [sheet] });
        await LoadedPage<NavigationPage>(c);
        await c.WaitFor(() => c.Window.Navigation.ModalStack.Count == 1 && c.Window.Navigation.ModalStack[0].IsLoaded, "the modal to be shown");
        await c.Settle();
#if IOS || MACCATALYST
        var presented = (c.Window.Handler?.PlatformView as UIKit.UIWindow)?.RootViewController?.PresentedViewController
            ?? throw new CheckFailedException("no presented view controller");
#if MACCATALYST
        // Ignored on Mac Catalyst, where MAUI can't dismiss a sheet (spec 069 §5.6).
        Equal(UIKit.UIModalPresentationStyle.FullScreen, presented.ModalPresentationStyle, "the modal's presentation style");
#else
        Equal(UIKit.UIModalPresentationStyle.PageSheet, presented.ModalPresentationStyle, "the modal's presentation style");
#endif
#else
        Console.WriteLine("#   the presentation style is iOS-only; the modal is shown as usual");
#endif
        // The sheet is dismissed like any modal: by the app dropping it, and when its stack unmounts.
        c.Render(NavigationStack([Page("root", Label("root"))]));
        await c.WaitFor(() => c.Window.Navigation.ModalStack.Count == 0, "the app's dismissal of the sheet");
        c.Render(NavigationStack([Page("root", Label("root"))]) with { Modals = [sheet] });
        await c.WaitFor(() => c.Window.Navigation.ModalStack.Count == 1 && c.Window.Navigation.ModalStack[0].IsLoaded, "the sheet to be shown again");
        await c.Settle();
        c.Unmount(keepWindowPage: true);
        await c.WaitFor(() => c.Window.Navigation.ModalStack.Count == 0, "the unmounted stack's sheet to be dismissed");
    }
}
