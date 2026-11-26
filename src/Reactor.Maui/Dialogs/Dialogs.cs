using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Hosting;

namespace Microsoft.UI.Reactor.Maui;

/// <summary>
/// MAUI's page dialogs for a component's window, from <c>UseDialogs()</c> (spec 069 §5.1). The object
/// is stable across renders, so event handlers and effects can capture it. Dialogs are shown one at a
/// time per window, on the window's top modal or else its page, in the order they were requested, from
/// any thread. When the window is destroyed, the tasks of dialogs not yet answered end with a
/// <see cref="TaskCanceledException"/>; a dialog that couldn't be shown faults its task.
/// </summary>
public sealed class Dialogs
{
    private readonly Lock _gate = new();
    private Window? _window;
    private List<DialogRequest>? _pending;
    private bool _closed;

    internal Dialogs()
    {
    }

    /// <summary>An informational alert with one button.</summary>
    public Task Alert(string title, string? message, string close)
    {
        ArgumentException.ThrowIfNullOrEmpty(close);
        return Request<object?>(new AlertContent(title, message, null, close), static _ => null);
    }

    /// <summary>An alert with two buttons; true when the user chose <paramref name="accept"/>.</summary>
    public Task<bool> Alert(string title, string? message, string accept, string cancel)
    {
        ArgumentException.ThrowIfNullOrEmpty(cancel);
        return Request(new AlertContent(title, message, accept, cancel), static r => r is true);
    }

    /// <summary>An action sheet; the text of the button the user chose, or <see langword="null"/> when dismissed.</summary>
    public Task<string?> ActionSheet(string? title, string? cancel, string? destruction, params string[] buttons) =>
        Request(new ActionSheetContent(title, cancel, destruction, buttons ?? []), static r => r as string);

    /// <summary>A prompt for one line of text; the text, or <see langword="null"/> when the user cancelled.</summary>
    public Task<string?> Prompt(
        string title,
        string? message,
        string accept = "OK",
        string cancel = "Cancel",
        string? placeholder = null,
        int maxLength = -1,
        Keyboard? keyboard = null,
        string? initialValue = null) =>
        Request(new PromptContent(title, message, accept, cancel, placeholder, maxLength, keyboard, initialValue), static r => r as string);

    private Task<T> Request<T>(DialogContent content, Func<object?, T> convert)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new DialogRequest(
            () => content,
            answer => completion.TrySetResult(convert(answer)),
            error => completion.TrySetException(error),
            () => completion.TrySetCanceled()));
        return completion.Task;
    }

    /// <summary>Queues a request on the window, or keeps it until the window is known.</summary>
    internal void Enqueue(DialogRequest request)
    {
        Window? window;
        lock (_gate)
        {
            window = _window;
            if (window is null)
            {
                if (_closed)
                {
                    // The component unmounted before its window was known: it never will be.
                    request.Cancel();
                    return;
                }
                (_pending ??= []).Add(request);
                return;
            }
        }
        DialogQueue.For(window).Enqueue(request);
    }

    /// <summary>The window is known (or the hosted page moved to another one): requests go there from now on.</summary>
    internal void Attach(Window window)
    {
        List<DialogRequest>? pending;
        lock (_gate)
        {
            if (ReferenceEquals(_window, window) && _pending is null) return;
            _window = window;
            pending = _pending;
            _pending = null;
        }
        if (pending is null) return;
        var queue = DialogQueue.For(window);
        foreach (var request in pending) queue.Enqueue(request);
    }

    /// <summary>The component unmounted: requests still waiting for a window are cancelled.</summary>
    internal void Close()
    {
        List<DialogRequest>? pending;
        lock (_gate)
        {
            _closed = true;
            pending = _pending;
            _pending = null;
        }
        if (pending is null) return;
        foreach (var request in pending) request.Cancel();
    }

    /// <summary>The <c>UseDialogs</c> hook.</summary>
    internal static Dialogs Use(RenderContext context)
    {
        var window = context.UseContext(MauiContexts.Window);
        var page = context.UseContext(MauiContexts.Page);
        var holder = context.UseRef<Dialogs?>(null);
        var dialogs = holder.Current ??= new Dialogs();
        // Known at render for a ReactorWindow, so a dialog requested before the effects run goes straight there.
        if (WindowTracking.Current(window, page) is { } now) dialogs.Attach(now);
        context.UseEffect(() => WindowTracking.Watch(window, page, dialogs.Attach), window, page);
        context.UseEffect(() => (Action)dialogs.Close, Array.Empty<object>());
        return dialogs;
    }
}
