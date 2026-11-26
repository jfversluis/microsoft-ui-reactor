using System.Runtime.CompilerServices;

namespace Microsoft.UI.Reactor.Maui;

/// <summary>
/// A window's dialogs, shown one at a time (spec 069 §5.1). Every operation runs on the window's
/// dispatcher. The presenter is resolved when a dialog is dequeued: the window's top modal, else its
/// page. The next dialog waits until the previous one's native UI has gone (MAUI completes the answer
/// before the platform has finished dismissing it) and while one of Reactor's navigators runs.
/// Destroying the window settles every dialog it holds, and every one requested afterwards, until
/// MAUI creates the window again (an Android activity recreated).
/// </summary>
internal sealed class DialogQueue
{
    private static readonly ConditionalWeakTable<Window, DialogQueue> Queues = new();

    private readonly Window _window;
    private readonly List<DialogRequest> _waiting = [];
    private IDispatcher? _dispatcher;
    private DialogRequest? _showing;
    private bool _frameScheduled;
    private bool _waitingForNavigation;
    private bool _destroyed;

    private DialogQueue(Window window)
    {
        _window = window;
        window.Destroying += OnDestroying;
    }

    /// <summary>The queue of <paramref name="window"/>, created on first use.</summary>
    public static DialogQueue For(Window window) => Queues.GetValue(window, static w => new DialogQueue(w));

    /// <summary>The platform the dialogs are shown on; headless tests replace it.</summary>
    internal IDialogPlatform Platform { get; set; } = MauiDialogPlatform.Instance;

    /// <summary>The dispatcher the queue runs on: the window's; tests replace it.</summary>
    internal IDispatcher? Dispatcher
    {
        get => _dispatcher ?? _window.Dispatcher;
        set => _dispatcher = value;
    }

    /// <summary>The dialog being shown (its answer not yet given).</summary>
    internal DialogRequest? Showing => _showing;

    /// <summary>The dialogs waiting, first to be shown first.</summary>
    internal int WaitingCount => _waiting.Count;

    /// <summary>Whether the queue is waiting for a frame to check again.</summary>
    internal bool WaitingForFrame => _frameScheduled;

    /// <summary>Whether the window was destroyed (and not created again).</summary>
    internal bool IsDestroyed => _destroyed;

    public void Enqueue(DialogRequest request) => OnDispatcher(() =>
    {
        if (_destroyed)
        {
            request.Cancel();
            return;
        }
        _waiting.Add(request);
        Pump();
    }, request.Cancel);

    /// <summary>
    /// Runs <paramref name="work"/> on the dispatcher, or <paramref name="refused"/> on this thread when
    /// the dispatcher refuses it (the app is shutting down), so no request is left unsettled.
    /// </summary>
    private void OnDispatcher(Action work, Action refused)
    {
        if (Dispatcher is { IsDispatchRequired: true } dispatcher)
        {
            if (!dispatcher.Dispatch(work)) refused();
        }
        else
        {
            work();
        }
    }

    private void Pump()
    {
        while (!_destroyed && _showing is null && !_frameScheduled && !_waitingForNavigation)
        {
            // A declarative dialog made stale before it was shown is dropped without a word.
            _waiting.RemoveAll(static r => r.IsStale);
            if (_waiting.Count == 0) return;
            if (Platform.IsDialogShown(_window))
            {
                _frameScheduled = true;
                if (!Platform.ScheduleFrame(_window, OnFrame)) CancelAll();
                return;
            }
            if (Platform.WaitForNavigation(_window, OnNavigationFinished))
            {
                _waitingForNavigation = true;
                return;
            }

            var request = _waiting[0];
            _waiting.RemoveAt(0);
            Task<object?> answer;
            try
            {
                var presenter = Presenter()
                    ?? throw new InvalidOperationException("The window has no page to present the dialog on.");
                answer = Platform.Present(presenter, request.Content());
            }
            catch (Exception ex)
            {
                request.Fail(ex);
                continue;
            }
            _showing = request;
            answer.ContinueWith(t => OnDispatcher(() => Answered(request, t), () => Settle(request, t)), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private void OnFrame()
    {
        _frameScheduled = false;
        Pump();
    }

    // Raised as the navigator finishes; posted, so the dialog isn't presented from inside its loop.
    private void OnNavigationFinished()
    {
        if (Dispatcher is not { } dispatcher || !dispatcher.Dispatch(() =>
            {
                _waitingForNavigation = false;
                Pump();
            }))
        {
            CancelAll();
        }
    }

    private Page? Presenter()
    {
        var modals = _window.Navigation.ModalStack;
        return modals.Count > 0 ? modals[^1] : _window.Page;
    }

    private void Answered(DialogRequest request, Task<object?> answer)
    {
        if (!ReferenceEquals(_showing, request)) return;
        Settle(request, answer);
        // The next dialog waits until this one has been dismissed: the platform's turn after the answer
        // (Android), then no dialog left in the presentation chain (iOS and Mac Catalyst).
        Platform.AfterAnswer(_window, () =>
        {
            if (ReferenceEquals(_showing, request)) _showing = null;
            Pump();
        });
    }

    private static void Settle(DialogRequest request, Task<object?> answer)
    {
        if (answer.IsFaulted) request.Fail(answer.Exception!.InnerExceptions.Count == 1 ? answer.Exception.InnerException! : answer.Exception);
        else if (answer.IsCanceled) request.Cancel();
        else request.Answer(answer.Result);
    }

    private void CancelAll()
    {
        var cancelled = new List<DialogRequest>(_waiting.Count + 1);
        if (_showing is { } showing) cancelled.Add(showing);
        cancelled.AddRange(_waiting);
        _showing = null;
        _waiting.Clear();
        foreach (var request in cancelled) request.Cancel();
    }

    private void OnDestroying(object? sender, EventArgs e)
    {
        _destroyed = true;
        CancelAll();
        // Kept as the window's queue, so a request made later (even by a later Destroying handler) is
        // settled at once rather than shown on a dead window. MAUI may create the window again.
        _window.Destroying -= OnDestroying;
        _window.Created += OnCreated;
    }

    private void OnCreated(object? sender, EventArgs e)
    {
        _window.Created -= OnCreated;
        Queues.Remove(_window);
    }
}
