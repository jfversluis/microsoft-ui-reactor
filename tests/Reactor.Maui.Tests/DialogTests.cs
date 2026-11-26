using Microsoft.UI.Reactor.Core;
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

    public bool IsDialogShown(Window window, bool afterOwnDialog) => NativeShown;

    public bool IsNavigating(Window window) => Navigating;

    public void ScheduleFrame(Window window, Action check) => Frames.Add(check);

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
    public override Element Render() =>
        Alert(Props.Title, "Sure?", "Yes", "No") with
        {
            IsOpen = Props.IsOpen,
            OnClosed = ok =>
            {
                Props.Closed.Add($"{Props.Title}:{ok}");
                if (Props.Throw) throw new InvalidOperationException("closed boom");
            },
        };
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
    internal static readonly Application App = new();

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
    public async Task AFailedPresentation_FaultsTheTask_AndTheQueueMovesOn()
    {
        var h = new Harness();
        var dialogs = h.Dialogs();
        h.Platform.Throw = new InvalidOperationException("no alerts here");
        var failed = dialogs.Alert("fails", null, "OK");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Soon(failed));
        Assert.Equal("no alerts here", error.Message);

        h.Platform.Throw = null;
        var next = dialogs.Alert("next", null, "Yes", "No");
        Assert.Equal("next", h.Platform.Titles);
        h.Platform.Answer(false);
        Assert.False(await Soon(next));
    }

    [Fact]
    public async Task AFailedPresentation_ShowsTheNextQueuedDialog()
    {
        var h = new Harness();
        var dialogs = h.Dialogs();
        var first = dialogs.Alert("first", null, "OK");
        var second = dialogs.Alert("second", null, "OK");
        var third = dialogs.Alert("third", null, "OK");
        h.Platform.Answer(false);
        h.Platform.Throw = new InvalidOperationException("second fails");
        h.Platform.Dismiss();
        await Soon(first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Soon(second));
        Assert.Equal("first,third", h.Platform.Titles);
        Assert.False(third.IsCompleted);
    }

    [Fact]
    public async Task NoPage_IsAFailedPresentation()
    {
        var window = new Window { Parent = App };
        var h = new Harness(window);
        var failed = h.Dialogs().Alert("nowhere", null, "OK");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Soon(failed));
        Assert.Contains("no page", error.Message);
    }

    [Fact]
    public async Task DestroyingTheWindow_CancelsTheShownAndQueuedDialogs()
    {
        var h = new Harness();
        var dialogs = h.Dialogs();
        var shown = dialogs.Alert("shown", null, "OK");
        var queued = dialogs.Alert("queued", null, "Yes", "No");
        Destroy(h.Window);
        await Assert.ThrowsAsync<TaskCanceledException>(() => Soon(shown));
        await Assert.ThrowsAsync<TaskCanceledException>(() => Soon(queued));
        // The late answer of the dialog that was on screen changes nothing.
        h.Platform.Answer(false);
        Assert.True(shown.IsCanceled);
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
    public void ADialog_IsHeldWhileANavigationRuns()
    {
        var h = new Harness();
        h.Platform.Navigating = true;
        _ = h.Dialogs().Alert("held", null, "OK");
        Assert.Empty(h.Platform.Presented);
        h.Platform.RunFrame();
        Assert.Empty(h.Platform.Presented);
        h.Platform.Navigating = false;
        h.Platform.RunFrame();
        Assert.Equal("held", h.Platform.Titles);
    }

    [Fact]
    public void ARunningReactorNavigation_InThePageOrAModal_HoldsDialogs()
    {
        var (stack, root) = SettledStack();
        var window = new Window(stack) { Parent = App };
        Assert.False(MauiDialogPlatform.Instance.IsNavigating(window));
        // A pop the platform made keeps the navigator running while MAUI's stack catches up.
        stack.Navigator.NotePlatformPop(root);
        stack.Navigator.Run();
        Assert.False(stack.Navigator.IsIdle);
        Assert.True(MauiDialogPlatform.Instance.IsNavigating(window));

        var (modal, modalRoot) = SettledStack();
        var other = new Window(new ContentPage()) { Parent = App };
        _ = other.Navigation.PushModalAsync(modal, false);
        Assert.False(MauiDialogPlatform.Instance.IsNavigating(other));
        modal.Navigator.NotePlatformPop(modalRoot);
        modal.Navigator.Run();
        Assert.True(MauiDialogPlatform.Instance.IsNavigating(other));

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
    public async Task ALateWindow_IsFoundWhenTheHostedPageLoads()
    {
        Dialogs? dialogs = null;
        var page = new ReactorPage(Component<DialogsProbe, DialogsProbeProps>(new(d => dialogs = d)));
        var platform = new FakeDialogPlatform();
        var requested = dialogs!.Alert("early", null, "Yes", "No");
        Assert.False(requested.IsCompleted);

        var window = new Window { Parent = App };
        DialogQueue.For(window).Platform = platform;
        Assert.Empty(platform.Presented);
        // Headless, a page is loaded when it gets a window.
        window.Page = page;
        Assert.True(page.IsLoaded);
        Assert.Equal("early", platform.Titles);
        Assert.Same(page, platform.Presented[0].Presenter);
        platform.Answer(true);
        Assert.True(await Soon(requested));
    }

    [Fact]
    public async Task UnmountingBeforeTheWindowIsKnown_CancelsTheWaitingRequests()
    {
        Dialogs? dialogs = null;
        var page = new ReactorPage(Component<DialogsProbe, DialogsProbeProps>(new(d => dialogs = d)));
        var requested = dialogs!.Alert("early", null, "OK");
        page.Unmount();
        await Assert.ThrowsAsync<TaskCanceledException>(() => Soon(requested));
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
