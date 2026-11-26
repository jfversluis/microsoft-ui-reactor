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
    // Destroyed, or a dispatch was refused: every request is settled at once from then on.
    private volatile bool _terminal;
    private bool _awaitingCreated;
    private readonly Lock _gate = new();

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

    /// <summary>Whether the queue settles every request at once: its window was destroyed, or its dispatcher refused work.</summary>
    internal bool IsTerminal => _terminal;

    public void Enqueue(DialogRequest request)
    {
        if (_terminal)
        {
            request.Cancel();
            return;
        }
        OnDispatcher(() =>
        {
            if (_terminal)
            {
                request.Cancel();
                return;
            }
            lock (_gate) _waiting.Add(request);
            Pump();
        }, () =>
        {
            request.Cancel();
            Terminate();
        });
    }

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
        while (!_terminal && _showing is null && !_frameScheduled && !_waitingForNavigation)
        {
            DialogRequest request;
            lock (_gate)
            {
                // A declarative dialog made stale before it was shown is dropped without a word.
                _waiting.RemoveAll(static r => r.IsStale);
                if (_waiting.Count == 0) return;
                request = _waiting[0];
            }
            if (Platform.IsDialogShown(_window))
            {
                _frameScheduled = true;
                if (!Platform.ScheduleFrame(_window, OnFrame)) Terminate();
                return;
            }
            if (Platform.WaitForNavigation(_window, OnNavigationFinished))
            {
                _waitingForNavigation = true;
                return;
            }

            lock (_gate)
            {
                if (_waiting.Count == 0 || !ReferenceEquals(_waiting[0], request)) continue;
                _waiting.RemoveAt(0);
            }
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
            answer.ContinueWith(t => OnDispatcher(() => Answered(request, t), () =>
                {
                    // Nothing more can run on the dispatcher: settle this answer and everything queued.
                    Settle(request, t);
                    Terminate();
                }), CancellationToken.None,
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
            Terminate();
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

    /// <summary>
    /// Settles the dialog shown and every queued one with its cancel answer, and every later request at
    /// once. The queue stays the window's (a tombstone) until MAUI creates the window again. Safe from
    /// any thread: it also runs where a refused dispatch left the work.
    /// </summary>
    private void Terminate()
    {
        List<DialogRequest> cancelled;
        lock (_gate)
        {
            _terminal = true;
            cancelled = new List<DialogRequest>(_waiting.Count + 1);
            if (_showing is { } showing) cancelled.Add(showing);
            cancelled.AddRange(_waiting);
            _showing = null;
            _waiting.Clear();
            if (!_awaitingCreated)
            {
                _awaitingCreated = true;
                _window.Created += OnCreated;
            }
        }
        foreach (var request in cancelled) request.Cancel();
    }

    private void OnDestroying(object? sender, EventArgs e)
    {
        _destroyed = true;
        _window.Destroying -= OnDestroying;
        // Kept as the window's queue, so a request made later (even by a later Destroying handler) is
        // settled at once rather than shown on a dead window.
        Terminate();
    }

    private void OnCreated(object? sender, EventArgs e)
    {
        _window.Created -= OnCreated;
        Queues.Remove(_window);
    }
}
