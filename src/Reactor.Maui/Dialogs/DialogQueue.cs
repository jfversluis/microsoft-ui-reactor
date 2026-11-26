using System.Runtime.CompilerServices;

namespace Microsoft.UI.Reactor.Maui;

/// <summary>
/// A window's dialogs, shown one at a time (spec 069 §5.1). Every operation runs on the window's
/// dispatcher. The presenter is resolved when a dialog is dequeued: the window's top modal, else its
/// page. The next dialog waits until the previous one's native UI has gone (MAUI completes the answer
/// before the platform has finished dismissing it) and while a navigation transition runs; both are
/// checked once per frame. Destroying the window cancels every dialog it holds.
/// </summary>
internal sealed class DialogQueue
{
    private static readonly ConditionalWeakTable<Window, DialogQueue> Queues = new();

    private readonly Window _window;
    private readonly List<DialogRequest> _waiting = [];
    private DialogRequest? _showing;
    private bool _afterOwnDialog;
    private bool _frameScheduled;
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

    /// <summary>The dialog being shown (its answer not yet given).</summary>
    internal DialogRequest? Showing => _showing;

    /// <summary>The dialogs waiting, first to be shown first.</summary>
    internal int WaitingCount => _waiting.Count;

    /// <summary>Whether the queue is waiting for a frame to check again.</summary>
    internal bool WaitingForFrame => _frameScheduled;

    public void Enqueue(DialogRequest request) => OnDispatcher(() =>
    {
        if (_destroyed)
        {
            request.Cancel();
            return;
        }
        _waiting.Add(request);
        Pump();
    });

    private void OnDispatcher(Action work)
    {
        if (_window.Dispatcher is { IsDispatchRequired: true } dispatcher) dispatcher.Dispatch(work);
        else work();
    }

    private void Pump()
    {
        while (!_destroyed && _showing is null)
        {
            // A declarative dialog made stale before it was shown is dropped without a word.
            _waiting.RemoveAll(static r => r.IsStale);
            if (_waiting.Count == 0 || _frameScheduled) return;
            if (Platform.IsDialogShown(_window, _afterOwnDialog) || Platform.IsNavigating(_window))
            {
                _frameScheduled = true;
                Platform.ScheduleFrame(_window, OnFrame);
                return;
            }
            _afterOwnDialog = false;

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
            // Synchronously when the answer arrives on the dispatcher, so the barrier is checked at once.
            answer.ContinueWith(t => OnDispatcher(() => Answered(request, t)), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private void OnFrame()
    {
        _frameScheduled = false;
        Pump();
    }

    private Page? Presenter()
    {
        var modals = _window.Navigation.ModalStack;
        return modals.Count > 0 ? modals[^1] : _window.Page;
    }

    private void Answered(DialogRequest request, Task<object?> answer)
    {
        if (!ReferenceEquals(_showing, request)) return;
        _showing = null;
        _afterOwnDialog = true;
        if (answer.IsFaulted) request.Fail(answer.Exception!.InnerExceptions.Count == 1 ? answer.Exception.InnerException! : answer.Exception);
        else if (answer.IsCanceled) request.Cancel();
        else request.Answer(answer.Result);
        Pump();
    }

    private void OnDestroying(object? sender, EventArgs e)
    {
        _window.Destroying -= OnDestroying;
        _destroyed = true;
        // A window MAUI brings back (an Android activity recreated) gets a new queue.
        Queues.Remove(_window);
        var cancelled = new List<DialogRequest>(_waiting.Count + 1);
        if (_showing is { } showing) cancelled.Add(showing);
        cancelled.AddRange(_waiting);
        _showing = null;
        _waiting.Clear();
        foreach (var request in cancelled) request.Cancel();
    }
}
