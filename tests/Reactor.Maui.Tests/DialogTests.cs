using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Maui.Handlers;
using Microsoft.UI.Reactor.Maui.Hosting;
using static Microsoft.UI.Reactor.Maui.Factories;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>A platform whose dialogs, dismissals, navigations and frames the test drives.</summary>
internal sealed class FakeDialogPlatform : IDialogPlatform
{
    public sealed record Shown(Page Presenter, DialogContent Content, TaskCompletionSource<object?> Answer);

    public List<Shown> Presented { get; } = [];
    public List<Action> Frames { get; } = [];

    /// <summary>Whether a native dialog is on screen; set when one is presented, cleared by <see cref="Dismiss"/>.</summary>
    public bool NativeShown { get; set; }

    public bool Navigating { get; set; }
    /// <summary>Thrown by the next presentation.</summary>
    public Exception? Throw { get; set; }

    public Task<object?> Present(Page presenter, DialogContent content)
    {
        if (Throw is { } error)
        {
            Throw = null;
            throw error;
        }
        Assert.False(NativeShown, "a dialog was presented while another was still on screen");
        var answer = new TaskCompletionSource<object?>();
        Presented.Add(new(presenter, content, answer));
        NativeShown = true;
        return answer.Task;
    }

    public bool IsDialogShown(Window window) => NativeShown;

    private Action? _resume;

    public bool WaitForNavigation(Window window, Action resume)
    {
        if (!Navigating) return false;
        _resume = resume;
        return true;
    }

    /// <summary>The navigation finishes: the queue's wait ends once (as a navigator's BecameIdle).</summary>
    public void FinishNavigation()
    {
        Navigating = false;
        var resume = _resume;
        _resume = null;
        resume?.Invoke();
    }

    public bool ScheduleFrame(Window window, Action check)
    {
        Frames.Add(check);
        return true;
    }

    /// <summary>As on Android: the turn after an answer is posted (see <see cref="RunPosted"/>).</summary>
    public bool PostAfterAnswer { get; set; }

    public List<Action> Posted { get; } = [];

    public void AfterAnswer(Window window, Action next)
    {
        if (PostAfterAnswer) Posted.Add(next);
        else next();
    }

    public void RunPosted()
    {
        var posted = Posted.ToList();
        Posted.Clear();
        foreach (var next in posted) next();
    }

    /// <summary>The dialog shown last fails after it was presented (an asynchronous presentation failure).</summary>
    public void FailAnswer(Exception error) => Presented[^1].Answer.SetException(error);

    /// <summary>The user answers the dialog shown last; the native UI is still dismissing.</summary>
    public void Answer(object? result) => Presented[^1].Answer.SetResult(result);

    /// <summary>The platform finishes dismissing the dialog, and a frame passes.</summary>
    public void Dismiss()
    {
        NativeShown = false;
        RunFrame();
    }

    public void RunFrame()
    {
        var frames = Frames.ToList();
        Frames.Clear();
        foreach (var frame in frames) frame();
    }

    public string Titles => string.Join(",", Presented.Select(p => p.Content switch
    {
        AlertContent a => a.Title,
        ActionSheetContent s => s.Title,
        PromptContent p2 => p2.Title,
        _ => "?",
    }));
}

/// <summary>Captures a component's hooks' results.</summary>
public sealed record DialogsProbeProps(Action<Dialogs> Report);

public sealed class DialogsProbe : Component<DialogsProbeProps>
{
    public override Element Render()
    {
        Props.Report(UseDialogs());
        return Label("probe");
    }
}

public sealed record DeclarativeProps(bool IsOpen, List<string> Closed, string Title = "Confirm", bool Throw = false);

public sealed class DeclarativeAlert : Component<DeclarativeProps>
{
    public override Element Render()
    {
        // The callback captures this render's values, as an app's "delete this item" does.
        var (title, closed, throws) = (Props.Title, Props.Closed, Props.Throw);
        return Alert(title, "Sure?", "Yes", "No") with
        {
            IsOpen = Props.IsOpen,
            OnClosed = ok =>
            {
                closed.Add($"{title}:{ok}");
                if (throws) throw new InvalidOperationException("closed boom");
            },
        };
    }
}

/// <summary>The README's dialog snippets.</summary>
public sealed class ReadmeDeleteButton : Component<Action>
{
    public override Element Render()
    {
        var dialogs = UseDialogs();
        return Button("Delete", async () =>
        {
            if (await dialogs.Alert("Delete?", "This can't be undone.", "Delete", "Cancel")) Props();
        });
    }
}

public sealed class ReadmeRename : Component<Action<string>>
{
    public override Element Render()
    {
        var (renaming, setRenaming) = UseState(false);
        return VStack(
            Button("Rename", () => setRenaming(true)),
            Prompt("Rename", "New name") with
            {
                IsOpen = renaming,
                InitialValue = "Untitled",
                OnClosed = name =>
                {
                    setRenaming(false);
                    if (name is not null) Props(name);
                },
            });
    }
}

public class DialogTests
{
    // One Application for the suite: a second would replace Application.Current, which the navigators read.
    internal static Application App => ShellTests.App;

    private sealed class Harness
    {
        public TestHost Host { get; } = new(MauiHandlers.CreateRegistry());
        public Window Window { get; }
        public FakeDialogPlatform Platform { get; } = new();
        public List<Exception> Errors { get; } = [];

        public Harness(Window? window = null)
        {
            Window = window ?? new Window(new ContentPage { Title = "root" }) { Parent = App };
            DialogQueue.For(Window).Platform = Platform;
            Host.Root.UnhandledException += Errors.Add;
        }

        public Harness Show(Element element)
        {
            Host.Render(MauiContexts.Window.Provide(Window, element));
            Host.Scheduler.Drain();
            return this;
        }

        public Dialogs Dialogs()
        {
            Dialogs? dialogs = null;
            Show(Component<DialogsProbe, DialogsProbeProps>(new(d => dialogs = d)));
            return dialogs!;
        }

        public DialogQueue Queue => DialogQueue.For(Window);
    }

    private static void Destroy(Window window) => ((IWindow)window).Destroying();

    // A dialog task that never completes fails the test rather than hanging it.
    private static Task Soon(Task task) => task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

    private static Task<T> Soon<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

    // ── The queue ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Dialogs_AreShownInOrder_OneAtATime_AfterTheNativeDialogHasGone()
    {
        var h = new Harness();
        var dialogs = h.Dialogs();
        var first = dialogs.Alert("first", null, "Yes", "No");
        var second = dialogs.ActionSheet("second", "Cancel", null, "A", "B");
        var third = dialogs.Prompt("third", "Name?");

        Assert.Equal("first", h.Platform.Titles);
        Assert.Equal(2, h.Queue.WaitingCount);

        // The answer arrives while the native alert is still dismissing: the next one waits for it.
        h.Platform.Answer(true);
        Assert.True(await Soon(first));
        Assert.Equal("first", h.Platform.Titles);
        Assert.True(h.Queue.WaitingForFrame);
        h.Platform.RunFrame();
        Assert.Equal("first", h.Platform.Titles);

        h.Platform.Dismiss();
        Assert.Equal("first,second", h.Platform.Titles);
        h.Platform.Answer("B");
        h.Platform.Dismiss();
        Assert.Equal("B", await Soon(second));

        Assert.Equal("first,second,third", h.Platform.Titles);
        h.Platform.Answer("Ada");
        Assert.Equal("Ada", await Soon(third));
        Assert.Null(h.Queue.Showing);
    }

    [Fact]
    public async Task TheOneButtonAlert_CompletesWhenClosed()
    {
        var h = new Harness();
        var closed = h.Dialogs().Alert("info", "Saved.", "OK");
        var shown = Assert.Single(h.Platform.Presented);
        Assert.Equal(new AlertContent("info", "Saved.", null, "OK"), shown.Content);
        Assert.False(closed.IsCompleted);
        h.Platform.Answer(false);
        await Soon(closed);
    }

    [Fact]
    public void Queues_ArePerWindow()
    {
        var a = new Harness();
        var b = new Harness();
        _ = a.Dialogs().Alert("in a", null, "OK");
        _ = b.Dialogs().Alert("in b", null, "OK");
        Assert.Equal("in a", a.Platform.Titles);
        Assert.Equal("in b", b.Platform.Titles);
    }

    [Fact]
    public async Task AFailedPresentation_CompletesWithTheCancelAnswer_AndTheQueueMovesOn()
    {
        var h = new Harness();
        var dialogs = h.Dialogs();
        h.Platform.Throw = new InvalidOperationException("no alerts here");
        // Safe in an async void handler: never faults.
        Assert.False(await Soon(dialogs.Alert("fails", null, "Yes", "No")));
        h.Platform.Throw = new InvalidOperationException("no sheets here");
        Assert.Null(await Soon(dialogs.ActionSheet("fails", "Cancel", null, "A")));
        // No boundary above the component: logged only, the tree stays mounted.
        h.Host.Scheduler.Drain();
        Assert.Empty(h.Errors);
        Assert.IsType<Label>(h.Host.RootView);

        var next = dialogs.Alert("next", null, "Yes", "No");
        Assert.Equal("next", h.Platform.Titles);
        h.Platform.Answer(true);
        Assert.True(await Soon(next));
    }

    [Fact]
    public async Task AFailedPresentation_IsReportedToTheCallersBoundary_WhenItHasOne()
    {
        var h = new Harness();
        Dialogs? dialogs = null;
        h.Show(ErrorBoundary(Component<DialogsProbe, DialogsProbeProps>(new(d => dialogs = d)), e => Label($"caught: {e.Message}")));
        h.Platform.Throw = new InvalidOperationException("no alerts here");
        Assert.Null(await Soon(dialogs!.Prompt("fails", null)));
        h.Host.Scheduler.Drain();
        Assert.Equal("caught: no alerts here", Assert.IsType<Label>(h.Host.RootView).Text);
    }

    [Fact]
    public async Task AFailedPresentation_ShowsTheNextQueuedDialog()
    {
        var h = new Harness();
        var dialogs = h.Dialogs();
        var first = dialogs.Alert("first", null, "OK");
        var second = dialogs.Alert("second", null, "Yes", "No");
        var third = dialogs.Alert("third", null, "OK");
        h.Platform.Answer(false);
        h.Platform.Throw = new InvalidOperationException("second fails");
        h.Platform.Dismiss();
        await Soon(first);
        Assert.False(await Soon(second));
        Assert.Equal("first,third", h.Platform.Titles);
        Assert.False(third.IsCompleted);
    }

    [Fact]
    public async Task NoPage_IsAFailedPresentation()
    {
        var window = new Window { Parent = App };
        var h = new Harness(window);
        Assert.False(await Soon(h.Dialogs().Alert("nowhere", null, "Yes", "No")));
    }

    [Fact]
    public async Task DestroyingTheWindow_CompletesTheShownAndQueuedDialogs_WithTheCancelAnswer()
    {
        var h = new Harness();
        var dialogs = h.Dialogs();
        var shown = dialogs.Alert("shown", null, "OK");
        var queued = dialogs.Alert("queued", null, "Yes", "No");
        var sheet = dialogs.ActionSheet("sheet", "Cancel", null, "A");
        Destroy(h.Window);
        await Soon(shown);
        Assert.False(await Soon(queued));
        Assert.Null(await Soon(sheet));
        // The late answer of the dialog that was on screen changes nothing.
        h.Platform.Answer(true);
        Assert.True(shown.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ARequestAfterDestroying_IsSettledAtOnce_UntilTheWindowIsCreatedAgain()
    {
        var h = new Harness();
        var dialogs = h.Dialogs();
        Destroy(h.Window);
        Assert.False(await Soon(dialogs.Alert("too late", null, "Yes", "No")));
        Assert.Empty(h.Platform.Presented);
        Assert.True(DialogQueue.For(h.Window).IsDestroyed);

        // MAUI creates the window again (an Android activity recreated): it gets a new queue.
        ((IWindow)h.Window).Created();
        var fresh = DialogQueue.For(h.Window);
        Assert.False(fresh.IsDestroyed);
        fresh.Platform = h.Platform;
        var again = dialogs.Alert("again", null, "Yes", "No");
        Assert.Equal("again", h.Platform.Titles);
        h.Platform.Answer(true);
        Assert.True(await Soon(again));
    }

    [Fact]
    public async Task ARequestFromALaterDestroyingHandler_IsSettledAtOnce()
    {
        var h = new Harness();
        var dialogs = h.Dialogs();
        Task<bool>? requested = null;
        h.Window.Destroying += (_, _) => requested = dialogs.Alert("while destroying", null, "Yes", "No");
        Destroy(h.Window);
        Assert.False(await Soon(requested!));
        Assert.Empty(h.Platform.Presented);
    }

    private sealed class RefusingDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => true;
        public bool Dispatch(Action action) => false;
        public bool DispatchDelayed(TimeSpan delay, Action action) => false;
        public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
    }

    [Fact]
    public async Task ARefusedEnqueue_SettlesTheRequest_AndTerminatesTheQueue()
    {
        var h = new Harness();
        var dialogs = h.Dialogs();
        var shown = dialogs.Alert("shown", null, "Yes", "No");
        Assert.Equal("shown", h.Platform.Titles);
        // The app is shutting down: the dispatcher refuses work.
        h.Queue.Dispatcher = new RefusingDispatcher();
        Assert.False(await Soon(dialogs.Alert("refused", null, "Yes", "No")));
        // Nothing more can run on the dispatcher: the dialog on screen is settled too.
        Assert.False(await Soon(shown));
        Assert.True(h.Queue.IsTerminal);
        h.Platform.Answer(true);
        Assert.False(await Soon(shown));
    }

    private sealed class SwitchableDispatcher : IDispatcher
    {
        public bool Refuse { get; set; }
        public bool IsDispatchRequired => true;

        public bool Dispatch(Action action)
        {
            if (Refuse) return false;
            action();
            return true;
        }

        public bool DispatchDelayed(TimeSpan delay, Action action) => Dispatch(action);
        public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
    }

    [Fact]
    public async Task ARefusedAnswerDispatch_TerminatesTheQueue_LeavingNoTaskStranded()
    {
        var h = new Harness();
        var dispatcher = new SwitchableDispatcher();
        h.Queue.Dispatcher = dispatcher;
        var dialogs = h.Dialogs();
        var shown = dialogs.Alert("shown", null, "Yes", "No");
        var queued = dialogs.Prompt("queued", null);
        Assert.Equal("shown", h.Platform.Titles);

        dispatcher.Refuse = true;
        h.Platform.Answer(true);
        Assert.True(await Soon(shown));
        Assert.Null(await Soon(queued));
        Assert.True(h.Queue.IsTerminal);
        Assert.Null(h.Queue.Showing);
        Assert.Equal(0, h.Queue.WaitingCount);

        // Even once the dispatcher accepts work again, the queue reports terminal: a request is settled at once.
        dispatcher.Refuse = false;
        Assert.False(await Soon(dialogs.Alert("later", null, "Yes", "No")));
        Assert.Equal("shown", h.Platform.Titles);
    }

    private sealed class RefusableScheduler : IRenderScheduler
    {
        private readonly Queue<Action> _queue = new();
        public bool Refuse { get; set; }
        public bool CheckAccess() => true;

        public void Post(Action work)
        {
            if (Refuse) throw new InvalidOperationException("The MAUI dispatcher refused the render work.");
            _queue.Enqueue(work);
        }

        public void Drain()
        {
            while (_queue.TryDequeue(out var work)) work();
        }
    }

    [Fact]
    public async Task AFailureReportTheRenderThreadRefuses_StillCompletesTheTask_AndTheQueueMovesOn()
    {
        var h = new Harness();
        var scheduler = new RefusableScheduler();
        Dialogs? dialogs = null;
        var root = new ReactorRoot(MauiHandlers.CreateRegistry(), scheduler, _ => { });
        root.Render(MauiContexts.Window.Provide(h.Window,
            ErrorBoundary(Component<DialogsProbe, DialogsProbeProps>(new(d => dialogs = d)), e => Label("caught"))));
        scheduler.Drain();
        var failing = dialogs!.Alert("fails later", null, "Yes", "No");
        var next = dialogs.Alert("next", null, "Yes", "No");

        // The presentation fails asynchronously while the render thread refuses the boundary report.
        scheduler.Refuse = true;
        try
        {
            h.Platform.FailAnswer(new InvalidOperationException("presentation failed"));
            Assert.False(await Soon(failing));
            h.Platform.Dismiss();
            Assert.Equal("fails later,next", h.Platform.Titles);
            h.Platform.Answer(true);
            Assert.True(await Soon(next));
        }
        finally
        {
            // The root stays in hot reload's list of live roots until it is collected.
            scheduler.Refuse = false;
            root.Unmount();
        }
    }

    [Fact]
    public void ThePresenter_IsResolvedWhenTheDialogIsDequeued_TheTopModal()
    {
        var h = new Harness();
        var dialogs = h.Dialogs();
        _ = dialogs.Alert("on the page", null, "OK");
        _ = dialogs.Alert("on the modal", null, "OK");
        var modal = new ContentPage { Title = "modal" };
        _ = h.Window.Navigation.PushModalAsync(modal, false);
        Assert.Same(modal, Assert.Single(h.Window.Navigation.ModalStack));

        h.Platform.Answer(false);
        h.Platform.Dismiss();
        Assert.Same(h.Window.Page, h.Platform.Presented[0].Presenter);
        Assert.Same(modal, h.Platform.Presented[1].Presenter);
    }

    [Fact]
    public void ADialog_IsHeldWhileANavigationRuns_AndShownWhenItFinishes()
    {
        var h = new Harness();
        h.Platform.Navigating = true;
        _ = h.Dialogs().Alert("held", null, "OK");
        Assert.Empty(h.Platform.Presented);
        Assert.Empty(h.Platform.Frames);
        h.Platform.FinishNavigation();
        Assert.Equal("held", h.Platform.Titles);
    }

    [Fact]
    public void OnAndroid_TheNextDialog_WaitsForThePlatformsTurnAfterTheAnswer()
    {
        var h = new Harness();
        h.Platform.PostAfterAnswer = true;
        var dialogs = h.Dialogs();
        var first = dialogs.Alert("first", null, "OK");
        _ = dialogs.Alert("second", null, "OK");
        h.Platform.Answer(false);
        // Android reports no dialog in a presentation chain; the dismissal happens in the posted turn.
        h.Platform.NativeShown = false;
        Assert.True(first.IsCompleted);
        Assert.Equal("first", h.Platform.Titles);
        h.Platform.RunPosted();
        Assert.Equal("first,second", h.Platform.Titles);
    }

    [Fact]
    public void ARunningReactorNavigation_InThePageOrAModal_HoldsDialogsUntilItIsIdle()
    {
        var (stack, root) = SettledStack();
        var window = new Window(stack) { Parent = App };
        var resumed = 0;
        Assert.False(MauiDialogPlatform.Instance.WaitForNavigation(window, () => resumed++));
        // A pop the platform made keeps the navigator running while MAUI's stack catches up.
        stack.Navigator.NotePlatformPop(root);
        stack.Navigator.Run();
        Assert.False(stack.Navigator.IsIdle);
        Assert.True(MauiDialogPlatform.Instance.WaitForNavigation(window, () => resumed++));
        for (var i = 0; i < 400 && resumed == 0; i++) Thread.Sleep(5);
        Assert.True(stack.Navigator.IsIdle);
        Assert.Equal(1, resumed);

        var (modal, modalRoot) = SettledStack();
        var other = new Window(new ContentPage()) { Parent = App };
        _ = other.Navigation.PushModalAsync(modal, false);
        Assert.False(MauiDialogPlatform.Instance.WaitForNavigation(other, () => resumed++));
        modal.Navigator.NotePlatformPop(modalRoot);
        modal.Navigator.Run();
        Assert.True(MauiDialogPlatform.Instance.WaitForNavigation(other, () => resumed++));

        static (Handlers.ReactorNavigationPage, Page) SettledStack()
        {
            var stack = new Handlers.ReactorNavigationPage();
            stack.Navigator = new Handlers.StackNavigator(stack);
            var root = new ContentPage { Title = "root" };
            stack.Navigator.SetDesired([root], []);
            for (var i = 0; i < 200 && !stack.Navigator.IsIdle; i++) Thread.Sleep(5);
            Assert.True(stack.Navigator.IsIdle);
            return (stack, root);
        }
    }

    [Fact]
    public async Task ALateWindow_IsFoundWhenTheHostedPageGetsOne()
    {
        Dialogs? dialogs = null;
        var page = new ReactorPage(Component<DialogsProbe, DialogsProbeProps>(new(d => dialogs = d)));
        var platform = new FakeDialogPlatform();
        var requested = dialogs!.Alert("early", null, "Yes", "No");
        Assert.False(requested.IsCompleted);

        var window = new Window { Parent = App };
        DialogQueue.For(window).Platform = platform;
        Assert.Empty(platform.Presented);
        window.Page = page;
        Assert.Equal("early", platform.Titles);
        Assert.Same(page, platform.Presented[0].Presenter);
        platform.Answer(true);
        Assert.True(await Soon(requested));
    }

    [Fact]
    public async Task AHostedPageThatLeavesItsWindow_SendsLaterDialogsToItsNextWindow()
    {
        Dialogs? dialogs = null;
        var page = new ReactorPage(Component<DialogsProbe, DialogsProbeProps>(new(d => dialogs = d)));
        var first = new Window { Parent = App };
        var firstPlatform = new FakeDialogPlatform();
        DialogQueue.For(first).Platform = firstPlatform;
        first.Page = page;
        first.Page = new ContentPage();
        var requested = dialogs!.Alert("after leaving", null, "Yes", "No");
        Assert.Empty(firstPlatform.Presented);

        var second = new Window { Parent = App };
        var secondPlatform = new FakeDialogPlatform();
        DialogQueue.For(second).Platform = secondPlatform;
        second.Page = page;
        Assert.Equal("after leaving", secondPlatform.Titles);
        secondPlatform.Answer(true);
        Assert.True(await Soon(requested));
    }

    [Fact]
    public async Task UnmountingBeforeTheWindowIsKnown_CompletesTheWaitingRequests()
    {
        Dialogs? dialogs = null;
        var page = new ReactorPage(Component<DialogsProbe, DialogsProbeProps>(new(d => dialogs = d)));
        var requested = dialogs!.Alert("early", null, "Yes", "No");
        page.Unmount();
        Assert.False(await Soon(requested));
    }

    [Fact]
    public void Dialogs_AreStableAcrossRenders()
    {
        var h = new Harness();
        var seen = new List<Dialogs>();
        h.Show(Component<DialogsProbe, DialogsProbeProps>(new(d => seen.Add(d))));
        h.Show(Component<DialogsProbe, DialogsProbeProps>(new(d => seen.Insert(seen.Count, d))));
        Assert.Equal(2, seen.Count);
        Assert.Same(seen[0], seen[1]);
    }

    // ── Declarative dialogs ─────────────────────────────────────────────────

    [Fact]
    public void ADeclarativeAlert_IsQueuedOncePerOpen_AndReportsItsAnswerOnce()
    {
        var h = new Harness();
        var closed = new List<string>();
        h.Show(Component<DeclarativeAlert, DeclarativeProps>(new(false, closed)));
        Assert.Empty(h.Platform.Presented);

        h.Show(Component<DeclarativeAlert, DeclarativeProps>(new(true, closed)));
        h.Show(Component<DeclarativeAlert, DeclarativeProps>(new(true, closed)));
        var shown = Assert.Single(h.Platform.Presented);
        Assert.Equal(new AlertContent("Confirm", "Sure?", "Yes", "No"), shown.Content);

        h.Platform.Answer(true);
        h.Platform.Dismiss();
        Assert.Equal(["Confirm:True"], closed);
        // Still open (the app hasn't closed it): not shown again.
        h.Show(Component<DeclarativeAlert, DeclarativeProps>(new(true, closed)));
        Assert.Single(h.Platform.Presented);

        // Closing and re-opening shows it again.
        h.Show(Component<DeclarativeAlert, DeclarativeProps>(new(false, closed)));
        h.Show(Component<DeclarativeAlert, DeclarativeProps>(new(true, closed)));
        Assert.Equal(2, h.Platform.Presented.Count);
        h.Platform.Answer(false);
        Assert.Equal(["Confirm:True", "Confirm:False"], closed);
    }

    [Fact]
    public void ADeclarativeDialog_ClosedBeforeItIsShown_IsRemovedFromTheQueue()
    {
        var h = new Harness();
        var blocker = h.Dialogs().Alert("blocker", null, "OK");
        var closed = new List<string>();
        h.Show(Component<DeclarativeAlert, DeclarativeProps>(new(true, closed)));
        Assert.Equal(1, h.Queue.WaitingCount);
        h.Show(Component<DeclarativeAlert, DeclarativeProps>(new(false, closed)));

        h.Platform.Answer(false);
        h.Platform.Dismiss();
        Assert.Equal("blocker", h.Platform.Titles);
        Assert.Equal(0, h.Queue.WaitingCount);
        Assert.Empty(closed);
    }

    [Fact]
    public void ADeclarativeDialog_UnmountedWhileShown_DropsItsAnswer()
    {
        var h = new Harness();
        var closed = new List<string>();
        h.Show(Component<DeclarativeAlert, DeclarativeProps>(new(true, closed)));
        Assert.Single(h.Platform.Presented);
        h.Show(Label("gone"));
        // MAUI can't dismiss it: it stays until the user answers, and that answer is dropped.
        h.Platform.Answer(true);
        Assert.Empty(closed);
    }

    [Fact]
    public void ADeclarativeDialog_TurnedOffOrReopenedWhileShown_DropsTheEarlierAnswer()
    {
        var h = new Harness();
        var closed = new List<string>();
        h.Show(Component<DeclarativeAlert, DeclarativeProps>(new(true, closed, "one")));
        h.Show(Component<DeclarativeAlert, DeclarativeProps>(new(false, closed, "one")));
        h.Show(Component<DeclarativeAlert, DeclarativeProps>(new(true, closed, "two")));
        Assert.Equal("one", h.Platform.Titles);
        Assert.Equal(1, h.Queue.WaitingCount);

        // The first open's answer is stale; the re-open is shown next, with the latest content.
        h.Platform.Answer(true);
        Assert.Empty(closed);
        h.Platform.Dismiss();
        Assert.Equal("one,two", h.Platform.Titles);
        h.Platform.Answer(false);
        Assert.Equal(["two:False"], closed);
    }

    [Fact]
    public void AnErrorFromOnClosed_GoesToTheElementsBoundary()
    {
        var h = new Harness();
        var closed = new List<string>();
        Element Tree(bool open) => ErrorBoundary(
            VStack(Label("content"), Component<DeclarativeAlert, DeclarativeProps>(new(open, closed, Throw: true))),
            error => Label($"caught: {error.Message}"));
        h.Show(Tree(true));
        h.Platform.Answer(true);
        h.Host.Scheduler.Drain();
        Assert.Equal(["Confirm:True"], closed);
        Assert.Equal("caught: closed boom", Assert.IsType<Label>(h.Host.RootView).Text);
        Assert.Empty(h.Errors);
    }

    [Fact]
    public void AFailedDeclarativePresentation_GoesToTheElementsBoundary_AndTheQueueMovesOn()
    {
        var h = new Harness();
        h.Platform.Throw = new InvalidOperationException("cannot show");
        var closed = new List<string>();
        h.Show(ErrorBoundary(Component<DeclarativeAlert, DeclarativeProps>(new(true, closed)), error => Label($"caught: {error.Message}")));
        h.Host.Scheduler.Drain();
        Assert.Equal("caught: cannot show", Assert.IsType<Label>(h.Host.RootView).Text);
        h.Platform.Throw = null;
        _ = h.Dialogs().Alert("next", null, "OK");
        Assert.Equal("next", h.Platform.Titles);
    }

    [Fact]
    public void DestroyingTheWindow_DropsADeclarativeDialog()
    {
        var h = new Harness();
        var closed = new List<string>();
        h.Show(Component<DeclarativeAlert, DeclarativeProps>(new(true, closed)));
        Destroy(h.Window);
        h.Platform.Answer(true);
        h.Host.Scheduler.Drain();
        Assert.Empty(closed);
        Assert.Empty(h.Errors);
    }

    [Fact]
    public void ADeclarativeDialog_OpenBeforeItsWindowIsKnown_AndClosedWhenItArrives_IsNeverShown()
    {
        var h = new Harness();
        var closed = new List<string>();
        Element Tree(Window? window, bool open) =>
            MauiContexts.Window.Provide(window, Component<DeclarativeAlert, DeclarativeProps>(new(open, closed)));
        h.Host.Render(Tree(null, true));
        h.Host.Scheduler.Drain();
        // The window arrives in the same render that closes the dialog.
        h.Host.Render(Tree(h.Window, false));
        h.Host.Scheduler.Drain();
        Assert.Empty(h.Platform.Presented);
        Assert.Equal(0, h.Queue.WaitingCount);
        Assert.Empty(closed);
    }

    [Fact]
    public void ADeclarativeDialogsAnswer_GoesToTheOnClosedOfTheContentShown()
    {
        var h = new Harness();
        var closed = new List<string>();
        h.Show(Component<DeclarativeAlert, DeclarativeProps>(new(true, closed, "Delete A?")));
        Assert.Equal("Delete A?", h.Platform.Titles);
        // The app re-renders the open dialog for another item while "Delete A?" is on screen.
        h.Show(Component<DeclarativeAlert, DeclarativeProps>(new(true, closed, "Delete B?")));
        h.Platform.Answer(true);
        Assert.Equal(["Delete A?:True"], closed);
    }

    // ── The README's snippets ───────────────────────────────────────────────

    [Fact]
    public void TheReadmeSnippets_AskAndAct()
    {
        var h = new Harness();
        var deleted = 0;
        h.Show(Component<ReadmeDeleteButton, Action>(() => deleted++));
        ((IButtonController)h.Host.View<Button>()).SendClicked();
        h.Platform.Answer(true);
        h.Platform.Dismiss();
        // The handler's continuation runs on the thread pool.
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref deleted) == 1, 2000));

        var names = new List<string>();
        h.Show(Component<ReadmeRename, Action<string>>(names.Add));
        var stack = h.Host.View<VerticalStackLayout>();
        ((IButtonController)stack.Children[0]).SendClicked();
        h.Host.Scheduler.Drain();
        var prompt = Assert.IsType<PromptContent>(h.Platform.Presented[^1].Content);
        Assert.Equal("Untitled", prompt.InitialValue);
        h.Platform.Answer("Report");
        h.Host.Scheduler.Drain();
        Assert.Equal(["Report"], names);
    }
}
