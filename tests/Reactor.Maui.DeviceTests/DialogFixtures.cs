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
        new("Dialogs_OnAModalOpenedAfterAnEarlierDialog_IsShownOnItPromptly", ClosingDialogs(DialogOnALaterModal)),
        new("Dialogs_RequestedFromABackgroundThread_IsShownOnce", ClosingDialogs(DialogFromABackgroundThread)),
        new("Dialogs_AnAlertOutsideTheQueue_DoesNotHoldReactorsDialogsBack", ClosingDialogs(DialogOverAForeignAlert)),
        new("Dialogs_AnAnswerArrivingBeforeTheAlertHasGone_HoldsTheNextDialog", ClosingDialogs(DialogAnsweredBeforeItHasGone)),
        new("Dialogs_TheQueuesOwnAlert_HoldsTheNextDialog_AfterACollection", ClosingDialogs(DialogOwnAlertAfterACollection)),
        new("Dialogs_AnAlertOnClosedOpens_IsNotTakenForTheQueuesOwn", ClosingDialogs(DialogOpenedByOnClosed)),
        new("Dialogs_AForeignAlertTitledTheSame_IsNotTakenForTheQueuesOwn", ClosingDialogs(DialogSameTitleForeignAlert)),
        new("SystemBack_HandlerRunsBeforeTheNavigationHost", BackBeforeHost),
        new("SystemBack_AtTheRoot_AHandlerKeepsTheApp_AndWithoutOneTheDefaultRuns", BackAtTheRoot),
        new("SystemBack_HandleOverload_ConsumesABackTheGuardRefuses", BackHandleOverload),
        new("SystemBack_PageAndTabsRoots_TheCallbackIsEnabledOnlyWithAHandler", BackOnPageAndTabsRoots),
        new("SystemBack_RealKeyOnAModal_AsksTheModalThenTheRoot_AndPopsWhenUnhandled", BackOnAModal),
        new("SystemBack_RealKeyInAShell_AsksTheHandlerOnce_BeforeShellsGuardedBack", BackInAShell),
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
            if (!await NativeDialogs.CloseAll(c.Window))
                Console.WriteLine($"#   a dialog could not be closed after the fixture: {NativeDialogs.Describe(c.Window)}");
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

    /// <summary>
    /// Waits for the native dialog, answers it through <paramref name="button"/>, and waits for it to go. With
    /// <paramref name="nextQueued"/> another request is waiting, so the next dialog may be on screen as soon as this one has
    /// gone: <see cref="NativeDialogs.Answer"/> already waited for the answered one, and "none left" would race the next.
    /// </summary>
    private static async Task<T> AnswerNatively<T>(FixtureContext c, Task<T> answer, string button, string? text = null, string? expect = null, bool nextQueued = false)
    {
        await c.WaitFor(() => NativeDialogs.IsReady(c.Window), "the native dialog to be shown", NativeDialogs.Timeout);
        if (expect is not null) Check(NativeDialogs.Describe(c.Window).Contains(expect, StringComparison.Ordinal), $"the dialog shows '{expect}': {NativeDialogs.Describe(c.Window)}");
        Check(!answer.IsCompleted, "no answer before the native button");
        await NativeDialogs.Answer(c.Window, button, text);
        var result = await answer.WaitAsync(TimeSpan.FromSeconds(5));
        if (!nextQueued)
            await c.WaitFor(() => NativeDialogs.Count(c.Window) == 0, "no native dialog left on screen", NativeDialogs.Timeout);
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

    /// <summary>MAUI's platform, recording what each presentation found (device tests check the barrier).</summary>
    private sealed class RecordingPlatform(Action<Page> presenting) : IDialogPlatform
    {
        private readonly IDialogPlatform _inner = MauiDialogPlatform.Instance;

        public Task<object?> Present(Page presenter, DialogContent content)
        {
            presenting(presenter);
            return _inner.Present(presenter, content);
        }

        public bool IsDialogShown(Window window) => _inner.IsDialogShown(window);
        public bool WaitForNavigation(Window window, Action resume) => _inner.WaitForNavigation(window, resume);
        public bool ScheduleFrame(Window window, Action check) => _inner.ScheduleFrame(window, check);
        public void AfterAnswer(Window window, Action next) => _inner.AfterAnswer(window, next);
    }

    /// <summary>Runs <paramref name="body"/> with the window's dialogs presented through a <see cref="RecordingPlatform"/>.</summary>
    private static async Task Recording(FixtureContext c, Action<Page> presenting, Func<Task> body)
    {
        var queue = DialogQueue.For(c.Window);
        queue.Platform = new RecordingPlatform(presenting);
        try
        {
            await body();
        }
        finally
        {
            queue.Platform = MauiDialogPlatform.Instance;
        }
    }

    private static async Task DialogTwoQueued(FixtureContext c)
    {
        var dialogs = await ShowDialogs(c);
        var sampler = new DialogSampler(c);
        // When each dialog is presented, how many native dialogs are still on screen: none, every time.
        var onScreenAtPresentation = new List<int>();
        await Recording(c, _ => onScreenAtPresentation.Add(NativeDialogs.Count(c.Window)), async () =>
        {
            var first = dialogs.Alert("First", null, "Yes", "No");
            var second = dialogs.ActionSheet("Second", "Cancel", null, "One", "Two");
            Equal(true, await AnswerNatively(c, first, "Yes", expect: "First", nextQueued: true), "the first answer");
            await c.WaitFor(() => NativeDialogs.IsReady(c.Window), "the second dialog to be shown", NativeDialogs.Timeout);
            Check(NativeDialogs.Describe(c.Window).Contains("Second", StringComparison.Ordinal) || NativeDialogs.Describe(c.Window).Contains("Two", StringComparison.Ordinal),
                $"the second dialog is shown: {NativeDialogs.Describe(c.Window)}");
            Equal("Two", await AnswerNatively(c, second, "Two"), "the second answer");
        });
        sampler.Stop();
        // Android runs a button's click and the dialog's dismissal in one looper turn, so no frame could
        // show both even without the barrier: the oracle is that the first dialog's window is gone when
        // the second is presented.
        Equal("0,0", string.Join(",", onScreenAtPresentation), "native dialogs on screen at each presentation");
        Equal(1, sampler.MostAtOnce, "most native dialogs on screen at once");
    }

    private static async Task DialogOnALaterModal(FixtureContext c)
    {
        var dialogs = await ShowDialogs(c);
        // An earlier dialog, answered: nothing of it may hold back the next one.
        Equal(true, await AnswerNatively(c, dialogs.Alert("Earlier", null, "Yes", "No"), "Yes"), "the earlier answer");
        var modal = new ContentPage { Title = "modal", Content = new Label { Text = "modal" } };
        await c.Window.Navigation.PushModalAsync(modal, false);
        await c.WaitFor(() => modal.IsLoaded, "the modal to be shown");
        await c.Settle();
        Page? presenter = null;
        await Recording(c, p => presenter = p, async () =>
        {
            var clock = Stopwatch.StartNew();
            var answer = dialogs.Alert("On the modal", null, "Yes", "No");
            // Promptly: the old focus barrier never showed it at all, so a CI-sized bound still tells.
            await c.WaitFor(() => NativeDialogs.IsReady(c.Window), "the dialog to be shown on the modal", NativeDialogs.Timeout);
            Console.WriteLine($"#   shown after {clock.ElapsedMilliseconds} ms");
            Check(ReferenceEquals(presenter, modal), "presented on the modal");
            Equal(false, await AnswerNatively(c, answer, "No"), "the answer");
        });
        await c.Window.Navigation.PopModalAsync(false);
    }

    // UIKit runs a tapped action's handler once the alert has gone, so the queue's barrier matters when
    // an answer arrives earlier: the next dialog must wait until the answered alert has left the screen.
    private static async Task DialogAnsweredBeforeItHasGone(FixtureContext c)
    {
#if IOS || MACCATALYST
        var dialogs = await ShowDialogs(c);
        var onScreenAtPresentation = new List<int>();
        await Recording(c, _ => onScreenAtPresentation.Add(NativeDialogs.Count(c.Window)), async () =>
        {
            var first = dialogs.Alert("First", null, "Yes", "No");
            var second = dialogs.Alert("Second", null, "Yes", "No");
            await c.WaitFor(() => NativeDialogs.IsReady(c.Window, "First"), "the first dialog", NativeDialogs.Timeout);
            await NativeDialogs.Answer(c.Window, "Yes", answerBeforeDismissal: true);
            Equal(true, await first.WaitAsync(TimeSpan.FromSeconds(5)), "the first answer");
            await c.WaitFor(() => NativeDialogs.IsReady(c.Window, "Second"), "the second dialog", NativeDialogs.Timeout);
            await NativeDialogs.Answer(c.Window, "No");
            Equal(false, await second.WaitAsync(TimeSpan.FromSeconds(5)), "the second answer");
        });
        Equal("0,0", string.Join(",", onScreenAtPresentation), "native dialogs on screen at each presentation");
#else
        await Task.CompletedTask;
        throw new SkipException("Android's barrier is the posted turn, checked by Dialogs_TwoQueued_AreNeverShownAtOnce");
#endif
    }

    // The queue's own alert holds the next dialog for as long as UIKit presents it, even once the
    // managed wrappers of that alert have been collected.
    private static async Task DialogOwnAlertAfterACollection(FixtureContext c)
    {
#if IOS || MACCATALYST
        var dialogs = await ShowDialogs(c);
        var first = dialogs.Alert("Own", null, "Yes", "No");
        var second = dialogs.Alert("Next", null, "Yes", "No");
        await c.WaitFor(() => NativeDialogs.IsReady(c.Window, "Own"), "the queue's dialog", NativeDialogs.Timeout);
        NativeDialogs.AnswerWithoutDismissing(c.Window, "Yes");
        Equal(true, await first.WaitAsync(TimeSpan.FromSeconds(5)), "the first answer");
        await Collected(c);
        Equal(1, NativeDialogs.Count(c.Window), $"the answered alert is still presented: {NativeDialogs.Describe(c.Window)}");
        Check(MauiDialogPlatform.Instance.IsDialogShown(c.Window), "the barrier still sees the queue's own alert after a collection");
        await Task.Delay(300);
        Equal(1, NativeDialogs.Count(c.Window), $"the next dialog is held: {NativeDialogs.Describe(c.Window)}");
        await NativeDialogs.Dismiss(c.Window, "Own");
        await c.WaitFor(() => NativeDialogs.IsReady(c.Window, "Next"), "the next dialog once the first has gone", NativeDialogs.Timeout);
        await NativeDialogs.Answer(c.Window, "No");
        Equal(false, await second.WaitAsync(TimeSpan.FromSeconds(5)), "the next answer");
#else
        await Task.CompletedTask;
        throw new SkipException("Android's barrier is the posted turn, checked by Dialogs_TwoQueued_AreNeverShownAtOnce");
#endif
    }

    private static async Task Collected(FixtureContext c)
    {
        for (var i = 0; i < 4; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await c.Settle();
        }
    }

    private sealed record ForeignOnClosedProps(Action<Dialogs> Report, Action OnClosed);

    private sealed class ForeignOnClosedPage : Component<ForeignOnClosedProps>
    {
        public override Element Render()
        {
            var (open, setOpen) = UseState(true);
            Props.Report(UseDialogs());
            return Page("foreign-on-closed", VStack(
                Label("foreign-on-closed"),
                Alert("Declared", null, "OK", "Cancel") with
                {
                    IsOpen = open,
                    OnClosed = _ =>
                    {
                        Props.OnClosed();
                        setOpen(false);
                    },
                }));
        }
    }

    // An alert the answer's own callback opens isn't the queue's: it doesn't hold Reactor's next dialog,
    // while Reactor's own alert, shown above it, still does.
    private static async Task DialogOpenedByOnClosed(FixtureContext c)
    {
#if IOS || MACCATALYST
        Dialogs? dialogs = null;
        c.Show(Component<ForeignOnClosedPage, ForeignOnClosedProps>(new(d => dialogs = d, () => NativeDialogs.PresentForeign(c.Window, "Foreign"))));
        await LoadedPage<ContentPage>(c);
        await c.WaitFor(() => NativeDialogs.IsReady(c.Window, "Declared"), "the declared alert", NativeDialogs.Timeout);
        var next = dialogs!.Alert("Next", null, "Yes", "No");
        var third = dialogs.Alert("Third", null, "Yes", "No");
        await NativeDialogs.Answer(c.Window, "OK");
        await c.WaitFor(() => NativeDialogs.IsReady(c.Window, "Foreign"), "the alert OnClosed opened", NativeDialogs.Timeout);
        await c.WaitFor(() => NativeDialogs.IsReady(c.Window, "Next"), "Reactor's next dialog, above the alert OnClosed opened", NativeDialogs.Timeout);
        NativeDialogs.AnswerWithoutDismissing(c.Window, "Yes");
        Equal(true, await next.WaitAsync(TimeSpan.FromSeconds(5)), "the next answer");
        await Collected(c);
        await Task.Delay(300);
        Check(!NativeDialogs.IsReady(c.Window, "Third"), $"Reactor's own alert holds the third dialog: {NativeDialogs.Describe(c.Window)}");
        Equal(2, NativeDialogs.Count(c.Window), $"the foreign alert and Reactor's answered one: {NativeDialogs.Describe(c.Window)}");
        await NativeDialogs.Dismiss(c.Window, "Next");
        await c.WaitFor(() => NativeDialogs.IsReady(c.Window, "Third"), "the third dialog once Reactor's has gone", NativeDialogs.Timeout);
        await NativeDialogs.Answer(c.Window, "No");
        Equal(false, await third.WaitAsync(TimeSpan.FromSeconds(5)), "the third answer");
        Check(await NativeDialogs.CloseAll(c.Window), $"the foreign alert closed: {NativeDialogs.Describe(c.Window)}");
#else
        await Task.CompletedTask;
        throw new SkipException("Android's barrier is the posted turn: no alert is tracked as the queue's own");
#endif
    }

    // MAUI's dialogs, with an alert outside the queue opened in the middle of the first presentation:
    // after the queue has looked at the chain, before MAUI's own presentation turn.
    private sealed class ForeignDuringPresentation(Window window, string title) : IDialogPlatform
    {
        private readonly IDialogPlatform _inner = MauiDialogPlatform.Instance;
        private bool _opened;

        public Task<object?> Present(Page presenter, DialogContent content)
        {
            var answer = _inner.Present(presenter, content);
            if (!_opened)
            {
                _opened = true;
                NativeDialogs.PresentForeign(window, title);
            }
            return answer;
        }

        public bool IsDialogShown(Window w) => _inner.IsDialogShown(w);
        public bool WaitForNavigation(Window w, Action resume) => _inner.WaitForNavigation(w, resume);
        public bool ScheduleFrame(Window w, Action check) => _inner.ScheduleFrame(w, check);
        public void AfterAnswer(Window w, Action next) => _inner.AfterAnswer(w, next);
    }

    // An alert outside the queue with the very title of the queue's own, opened while the queue presents
    // it, isn't taken for the queue's: once the queue's own has gone, Reactor's next dialog is shown.
    private static async Task DialogSameTitleForeignAlert(FixtureContext c)
    {
#if IOS || MACCATALYST
        var dialogs = await ShowDialogs(c);
        var queue = DialogQueue.For(c.Window);
        queue.Platform = new ForeignDuringPresentation(c.Window, "Same");
        try
        {
            var first = dialogs.Alert("Same", null, "Yes", "No");
            var next = dialogs.Alert("Next", null, "Yes", "No");
            await c.WaitFor(() => NativeDialogs.Count(c.Window) == 2 && NativeDialogs.IsReady(c.Window, "Same"), "the foreign alert and the queue's, both titled 'Same'", NativeDialogs.Timeout);
            Check(NativeDialogs.Describe(c.Window).Contains("Dismiss", StringComparison.Ordinal), $"the foreign alert is under the queue's: {NativeDialogs.Describe(c.Window)}");
            await NativeDialogs.Answer(c.Window, "Yes");
            Equal(true, await first.WaitAsync(TimeSpan.FromSeconds(5)), "the queue's answer");
            await c.WaitFor(() => NativeDialogs.IsReady(c.Window, "Next"), "Reactor's next dialog, above the foreign alert titled the same", NativeDialogs.Timeout);
            await NativeDialogs.Answer(c.Window, "No");
            Equal(false, await next.WaitAsync(TimeSpan.FromSeconds(5)), "the next answer");
            Equal(1, NativeDialogs.Count(c.Window), $"the foreign alert still shown: {NativeDialogs.Describe(c.Window)}");
        }
        finally
        {
            queue.Platform = MauiDialogPlatform.Instance;
        }
#else
        await Task.CompletedTask;
        throw new SkipException("Android's barrier is the posted turn: no alert is tracked as the queue's own");
#endif
    }

    // An alert shown outside Reactor's queue (by the app through UIKit or Android directly, or by the
    // system) never holds Reactor's dialogs back: they show above it.
    private static async Task DialogOverAForeignAlert(FixtureContext c)
    {
        var dialogs = await ShowDialogs(c);
        NativeDialogs.PresentForeign(c.Window, "Foreign");
        await c.WaitFor(() => NativeDialogs.IsReady(c.Window, "Foreign"), "the foreign alert", NativeDialogs.Timeout);
        var answer = dialogs.Alert("Reactor's", null, "Yes", "No");
        await c.WaitFor(() => NativeDialogs.IsReady(c.Window, "Reactor's"), "Reactor's dialog above the foreign one", NativeDialogs.Timeout);
        Equal(2, NativeDialogs.Count(c.Window), "native dialogs on screen");
        await NativeDialogs.Answer(c.Window, "Yes");
        Equal(true, await answer.WaitAsync(TimeSpan.FromSeconds(5)), "Reactor's answer");
        Equal(1, NativeDialogs.Count(c.Window), "the foreign alert still shown");
        Check(await NativeDialogs.CloseAll(c.Window), $"the foreign alert closed: {NativeDialogs.Describe(c.Window)}");
    }

    private static async Task DialogFromABackgroundThread(FixtureContext c)
    {
        var dialogs = await ShowDialogs(c);
        var sampler = new DialogSampler(c);
        var onWorker = false;
        // The request itself, made on a pool thread (not awaited there).
        var answer = await Task.Run(() =>
        {
            onWorker = !MainThread.IsMainThread;
            return Task.FromResult(dialogs.Alert("From a worker", null, "Yes", "No"));
        });
        Check(onWorker, "the request was made off the UI thread");
        Equal(true, await AnswerNatively(c, answer, "Yes", expect: "From a worker"), "the answer");
        sampler.Stop();
        Equal(1, sampler.MostAtOnce, "native dialogs on screen at once");
    }

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
        // Positive control: the request did arrive during the push, and nothing is shown yet.
        Check(!navigator.IsIdle, "the push is still running after the request");
        Equal(0, NativeDialogs.Count(c.Window), "native dialogs right after the request");
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

    private const string NoBackKey = "iOS and Mac Catalyst have no back key";

#if ANDROID
    private static AndroidX.Activity.ComponentActivity Activity =>
        (AndroidX.Activity.ComponentActivity)Microsoft.Maui.ApplicationModel.Platform.CurrentActivity!;

    private static AndroidX.Activity.OnBackPressedDispatcher BackDispatcher => Activity.OnBackPressedDispatcher;

    private static AndroidX.Activity.OnBackPressedCallback MauiCallback =>
        (AndroidX.Activity.OnBackPressedCallback?)typeof(MauiAppCompatActivity)
            .GetField("_mauiOnBackPressedCallback", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(Activity)
        ?? throw new CheckFailedException("MAUI's back callback was not found (MauiAppCompatActivity changed)");

    /// <summary>
    /// Presses back with a stand-in for the platform's default (moving the task to the back) below
    /// MAUI's callback and Reactor's, so the app stays in front. Returns whether the default was reached.
    /// </summary>
    private static bool BackReachingTheDefault(Window window)
    {
        var reached = 0;
        var standIn = new StandInCallback(() => reached++);
        var maui = MauiCallback;
        var reactor = Microsoft.UI.Reactor.Maui.Hosting.SystemBack.CallbackOf(window);
        // Callbacks are asked last-added first: Reactor's, then MAUI's, then the stand-in.
        reactor?.Remove();
        maui.Remove();
        BackDispatcher.AddCallback(Activity, standIn);
        BackDispatcher.AddCallback(Activity, maui);
        if (reactor is not null) BackDispatcher.AddCallback(Activity, reactor);
        try
        {
            BackDispatcher.OnBackPressed();
        }
        finally
        {
            standIn.Remove();
        }
        return reached > 0;
    }

    private sealed class StandInCallback(Action onBack) : AndroidX.Activity.OnBackPressedCallback(true)
    {
        public override void HandleOnBackPressed() => onBack();
    }
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
        await Task.CompletedTask;
        throw new SkipException(NoBackKey);
#endif
    }

    private static async Task BackAtTheRoot(FixtureContext c)
    {
#if ANDROID
        var (_, page, log) = await ShowBackApp(c, handleOverload: false);
        Check(Microsoft.UI.Reactor.Maui.Hosting.SystemBack.HasEnabledCallback(c.Window), "Reactor's back callback is enabled while handlers are registered");

        log.Handles.Add("home");
        BackDispatcher.OnBackPressed();
        await c.Settle();
        Equal("back:home", string.Join(",", log.Entries), "the root page's handler");
        Check(!Activity.IsFinishing && ReferenceEquals(c.Window.Page, page) && Activity.HasWindowFocus, "the app stays in front");

        // Without a handler handling it, the key goes on to MAUI (the host passes it on at the root),
        // then to the platform's default.
        log.Entries.Clear();
        log.Handles.Clear();
        Check(BackReachingTheDefault(c.Window), "the platform's default reached");
        await c.Settle();
        Equal("back:home", string.Join(",", log.Entries), "the root page's handler, unhandled");
        Check(Microsoft.UI.Reactor.Maui.Hosting.SystemBack.HasEnabledCallback(c.Window), "Reactor's callback re-enabled after passing the key on");
#else
        await Task.CompletedTask;
        throw new SkipException(NoBackKey);
#endif
    }

    private sealed record RootBackProps(string Kind, bool WithHandler, List<string> Log, bool Handles);

    /// <summary>A Page root or a Tabs root, with or without a back handler on the page shown.</summary>
    private sealed class RootBackApp : Component<RootBackProps>
    {
        public override Element Render()
        {
            var props = Props;
            Element Content(string name) => props.WithHandler && name == "first"
                ? Component<RootBackProbe, RootBackProps>(props)
                : Label(name);
            return props.Kind == "page"
                ? Page("first", Content("first"))
                : Tabs(0, null, Page("first", Content("first")), Page("second", Content("second")));
        }
    }

    private sealed class RootBackProbe : Component<RootBackProps>
    {
        public override Element Render()
        {
            var props = Props;
            UseSystemBackButton(() =>
            {
                props.Log.Add("back");
                return props.Handles;
            });
            return Label("with a handler");
        }
    }

    private static async Task BackOnPageAndTabsRoots(FixtureContext c)
    {
#if ANDROID
        foreach (var kind in new[] { "page", "tabs" })
        {
            foreach (var withHandler in new[] { false, true })
            {
                foreach (var handles in withHandler ? new[] { true, false } : new[] { false })
                {
                    var what = $"{kind}, {(withHandler ? $"a handler that {(handles ? "handles" : "passes")}" : "no handler")}";
                    var log = new List<string>();
                    c.Unmount();
                    c.Show(Component<RootBackApp, RootBackProps>(new(kind, withHandler, log, handles)));
                    await c.WaitFor(() => c.Window.Page is { IsLoaded: true } p && (p is ContentPage || p is TabbedPage), $"{what}: the root to load");
                    await c.Settle();
                    Equal(withHandler, Microsoft.UI.Reactor.Maui.Hosting.SystemBack.HasEnabledCallback(c.Window), $"{what}: Reactor's callback enabled");
                    // A Page or Tabs root overrides nothing, so MAUI leaves its own callback off at the root
                    // and Android can show its back-to-home preview.
                    Equal(false, MauiCallback.Enabled, $"{what}: MAUI's callback enabled");
                    var reachedDefault = BackReachingTheDefault(c.Window);
                    await c.Settle(200);
                    Equal(withHandler ? "back" : "", string.Join(",", log), $"{what}: handlers asked");
                    Equal(!(withHandler && handles), reachedDefault, $"{what}: the platform's default reached");
                    Console.WriteLine($"#   {what}: ok");
                }
            }
        }
#else
        await Task.CompletedTask;
        throw new SkipException(NoBackKey);
#endif
    }

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
        await Task.CompletedTask;
        throw new SkipException(NoBackKey);
#endif
    }

    private static int s_keyRequests;
    // Keys requested whose arrival wasn't seen: a key that comes after its fixture gave up must not
    // land on the next fixture (at a root, it finishes the activity or moves the app to the background).
    private static int s_keysOutstanding;
#if ANDROID
    private static StandInCallback? s_lateKeySink;
    private static int s_lateKeysAbsorbed;
#endif

    /// <summary>
    /// The back key as the user presses it: a real key event to the focused window. An app can't inject
    /// one into itself (INJECT_EVENTS), so the fixture asks run.sh, which follows the log for
    /// <c>REACTOR-KEY-BACK</c> and runs <c>adb shell input keyevent 4</c>. Waits until
    /// <paramref name="arrived"/> shows the key reached the app; one that doesn't arrive in time is
    /// absorbed when it comes (<see cref="AbsorbLateBackKeys"/>), not left to the next fixture.
    /// </summary>
    private static async Task PressBack(FixtureContext c, Func<bool> arrived)
    {
        s_keysOutstanding++;
        Console.WriteLine($"REACTOR-KEY-BACK {++s_keyRequests}");
        try
        {
            await c.WaitFor(arrived, "the back key from run.sh (adb shell input keyevent 4)", 8000);
            s_keysOutstanding--;
        }
        catch (CheckFailedException)
        {
            // Synchronously on the UI thread, so the key can't be dispatched between the timeout and this.
            AbsorbLateBackKeys();
            throw;
        }
    }

    /// <summary>A callback above every other swallows the back keys still on their way, until the runner's teardown.</summary>
    private static void AbsorbLateBackKeys()
    {
#if ANDROID
        if (s_lateKeySink is not null || Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is not AndroidX.Activity.ComponentActivity activity) return;
        s_lateKeysAbsorbed = 0;
        s_lateKeySink = new StandInCallback(() => s_lateKeysAbsorbed++);
        activity.OnBackPressedDispatcher.AddCallback(activity, s_lateKeySink);
#endif
    }

    /// <summary>
    /// The runner's teardown on Android, so one fixture's leftovers can't reach the next: an app a
    /// fixture left in the background is brought back to the front through run.sh (an app in the
    /// background can't start its own activity, and Android freezes it soon after), then the back keys
    /// still on their way are waited for and absorbed. Returns a failure for the fixture when the app
    /// had left the foreground.
    /// </summary>
    internal static async Task<string?> IsolateFromTheNextFixture(FixtureContext c, Action<string> note)
    {
#if ANDROID
        if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is not AndroidX.Activity.ComponentActivity activity) return null;
        string? failure = null;
        bool InFront() => activity.Lifecycle.CurrentState.IsAtLeast(AndroidX.Lifecycle.Lifecycle.State.Resumed!);
        if (!InFront())
        {
            // Paused for a moment (a dialog's window closing) is not the background: wait a little first.
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (!InFront() && wait.ElapsedMilliseconds < 1000) await Task.Delay(50);
            if (!InFront())
            {
                var state = activity.Lifecycle.CurrentState;
                Console.WriteLine("REACTOR-BRING-TO-FRONT");
                wait.Restart();
                while (!InFront() && wait.ElapsedMilliseconds < 15000) await Task.Delay(50);
                failure = $"teardown: the app was in the background ({state}){(InFront() ? "; run.sh brought it back" : "; it could not be brought back")}";
            }
        }
        if (s_lateKeySink is { } sink)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (s_lateKeysAbsorbed < s_keysOutstanding && clock.ElapsedMilliseconds < 20000) await Task.Delay(50);
            note($"absorbed {s_lateKeysAbsorbed} of {s_keysOutstanding} late back key(s)");
            sink.Remove();
            s_lateKeySink = null;
        }
        s_keysOutstanding = 0;
        return failure;
#else
        await Task.CompletedTask;
        return null;
#endif
    }

    private sealed record ModalBackProps(List<string> Log, HashSet<string> Handles);

    /// <summary>A root that handles the back (above every page) and declares a modal with its own handler.</summary>
    private sealed class ModalBackApp : Component<ModalBackProps>
    {
        public override Element Render()
        {
            var (log, handles) = (Props.Log, Props.Handles);
            var (showModal, setShowModal) = UseState(true);
            UseSystemBackButton(() =>
            {
                log.Add("root");
                return handles.Contains("root");
            });
            return NavigationStack([Page("base", Label("base"))]) with
            {
                Modals = showModal ? [Component<ModalBackPage, ModalBackProps>(Props)] : [],
                OnModalDismissed = _ => setShowModal(false),
            };
        }
    }

    private sealed class ModalBackPage : Component<ModalBackProps>
    {
        public override Element Render()
        {
            var (log, handles) = (Props.Log, Props.Handles);
            UseSystemBackButton(() =>
            {
                log.Add("modal");
                return handles.Contains("modal");
            });
            return Page("modal", Label("modal"));
        }
    }

    // The real back key while a MAUI modal is shown: the modal's dialog has its own back dispatcher,
    // which never reaches the activity's.
    private static async Task BackOnAModal(FixtureContext c)
    {
#if ANDROID
        var log = new List<string>();
        var handles = new HashSet<string>();
        c.Show(Component<ModalBackApp, ModalBackProps>(new(log, handles)));
        await LoadedPage<NavigationPage>(c);
        await c.WaitFor(() => c.Window.Navigation.ModalStack.Count == 1 && c.Window.Navigation.ModalStack[0].IsLoaded, "the modal to be shown");
        await c.WaitFor(() => Microsoft.UI.Reactor.Maui.Hosting.SystemBack.ModalCallbacksOf(c.Window).Any(), "Reactor's callback on the modal's dialog");
        await c.Settle();

        handles.Add("modal");
        await PressBack(c, () => log.Count > 0);
        await c.Settle();
        Equal("modal", string.Join(",", log), "the modal's handler, which handles it");
        Equal(1, c.Window.Navigation.ModalStack.Count, "modals after a handled back");

        log.Clear();
        handles.Clear();
        handles.Add("root");
        await PressBack(c, () => log.Count > 1);
        await c.Settle();
        Equal("modal,root", string.Join(",", log), "the modal's handler, then the root's, once each");
        Equal(1, c.Window.Navigation.ModalStack.Count, "modals after a back the root handled");

        log.Clear();
        handles.Clear();
        await PressBack(c, () => log.Count > 1);
        await c.WaitFor(() => c.Window.Navigation.ModalStack.Count == 0, "MAUI to pop the modal");
        await c.Settle();
        Equal("modal,root", string.Join(",", log), "the handlers of an unhandled back, once each");
#else
        await Task.CompletedTask;
        throw new SkipException(NoBackKey);
#endif
    }

    private sealed record ShellBackProps(BackLog Log, Action<NavigationHandle<string>> Report, Action<Action<string>> ReportSelect);

    private sealed class ShellBackApp : Component<ShellBackProps>
    {
        public override Element Render()
        {
            var (tab, setTab) = UseState("b");
            var nav = UseNavigation("home");
            Props.Report(nav);
            Props.ReportSelect(setTab);
            var log = Props.Log;
            return Shell(tab, setTab,
                FlyoutItem("A", ShellTab("a", "A", NavigationHost(nav, route => Component<BackPage, BackPageProps>(new(route, log))))),
                FlyoutItem("B", ShellTab("b", "B", Page("B", Label("b")))));
        }
    }

    // The real back key in a Shell, which has a back callback of its own on Android.
    private static async Task BackInAShell(FixtureContext c)
    {
#if ANDROID
        var log = new BackLog();
        NavigationHandle<string>? nav = null;
        Action<string>? select = null;
        c.Show(Component<ShellBackApp, ShellBackProps>(new(log, n => nav = n, s => select = s)));
        var shell = await LoadedPage<Microsoft.Maui.Controls.Shell>(c);
        await c.Settle();
        // Reactor's callback exists before item A's view is created (MAUI's Shell adds its own back
        // callback then). Whichever path the key takes first, the handlers run once per back.
        Check(Microsoft.UI.Reactor.Maui.Hosting.SystemBack.CallbackOf(c.Window) is not null, "Reactor's callback exists before item A is shown");
        select!("a");
        await c.WaitFor(() => shell.CurrentPage is { Title: "home" } home && Platform.IsDisplayed(home), "item A");
        await c.Settle();
        nav!.Navigate("detail");
        await c.WaitFor(() => shell.CurrentPage is { Title: "detail" } page && Platform.IsDisplayed(page), "the push");
        await c.Settle();
        log.Entries.Clear();

        log.Handles.Add("detail");
        await PressBack(c, () => log.Entries.Count > 0);
        await c.Settle(600);
        Equal("back:detail", string.Join(",", log.Entries), "the page's handler, once, handling the back");
        Equal("detail", shell.CurrentPage?.Title, "the page shown after a handled back");

        log.Entries.Clear();
        log.Handles.Clear();
        await PressBack(c, () => log.Entries.Count > 0);
        await c.WaitFor(() => shell.CurrentPage is { Title: "home" } page && Platform.IsDisplayed(page), "Shell's guarded back");
        await c.Settle();
        Equal("back:detail,guard:detail", string.Join(",", log.Entries), "the handler, then the guard, once each");
        Equal("home", nav.CurrentRoute, "the handle's route");
#else
        await Task.CompletedTask;
        throw new SkipException(NoBackKey);
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
        await Task.CompletedTask;
        throw new SkipException("iOS and Mac Catalyst apps can't background themselves in process");
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
