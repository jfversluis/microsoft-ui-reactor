using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Maui.Hosting;

namespace Microsoft.UI.Reactor.Maui;

/// <summary>
/// MAUI's page dialogs for a component's window, from <c>UseDialogs()</c> (spec 069 §5.1). The object
/// is stable across renders, so event handlers and effects can capture it. Dialogs are shown one at a
/// time per window, on the window's top modal or else its page, in the order they were requested, from
/// any thread. The tasks never fault and are never cancelled, so they are safe to await in an
/// <c>async</c> event handler: a dialog the user can't answer (its window was destroyed, or it couldn't
/// be shown) completes with the cancel answer (<see langword="false"/>, or <see langword="null"/>). A
/// dialog that couldn't be shown is logged, and reported to the component's error boundary if it has one.
/// </summary>
public sealed class Dialogs
{
    private readonly Lock _gate = new();
    private Window? _window;
    private List<DialogRequest>? _pending;
    private bool _closed;

    internal Dialogs(Node? owner = null) => Owner = owner;

    /// <summary>The component that called <c>UseDialogs</c>, whose boundary a failed presentation is reported to.</summary>
    internal Node? Owner { get; }

    /// <summary>An informational alert with one button.</summary>
    public Task Alert(string title, string? message, string close)
    {
        ArgumentException.ThrowIfNullOrEmpty(close);
        return Request<object?>(new AlertContent(title, message, null, close), static _ => null, null);
    }

    /// <summary>An alert with two buttons; true when the user chose <paramref name="accept"/>.</summary>
    public Task<bool> Alert(string title, string? message, string accept, string cancel)
    {
        ArgumentException.ThrowIfNullOrEmpty(cancel);
        return Request(new AlertContent(title, message, accept, cancel), static r => r is true, false);
    }

    /// <summary>An action sheet; the text of the button the user chose, or <see langword="null"/> when dismissed.</summary>
    public Task<string?> ActionSheet(string? title, string? cancel, string? destruction, params string[] buttons) =>
        Request(new ActionSheetContent(title, cancel, destruction, buttons ?? []), static r => r as string, null);

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
        Request(new PromptContent(title, message, accept, cancel, placeholder, maxLength, keyboard, initialValue), static r => r as string, null);

    private Task<T> Request<T>(DialogContent content, Func<object?, T> convert, T cancelAnswer)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new DialogRequest(
            () => content,
            answer => completion.TrySetResult(convert(answer)),
            error =>
            {
                ReportFailure(error);
                completion.TrySetResult(cancelAnswer);
            },
            () => completion.TrySetResult(cancelAnswer)));
        return completion.Task;
    }

    private void ReportFailure(Exception error)
    {
        DiagnosticLog.SwallowedError(LogCategory.Hosting, "Dialogs.Present", error);
        if (Owner is { Unmounted: false } owner && HookNodes.HasBoundaryAbove(owner)) owner.Root.ReportCallbackError(owner, error);
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

    /// <summary>The hosted page left <paramref name="window"/>: requests wait for its next window.</summary>
    internal void Detach(Window window)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_window, window)) _window = null;
        }
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
        var node = HookNodes.Current(context, "UseDialogs");
        var window = context.UseContext(MauiContexts.Window);
        var page = context.UseContext(MauiContexts.Page);
        var holder = context.UseRef<Dialogs?>(null);
        var dialogs = holder.Current ??= new Dialogs(node);
        // Known at render for a ReactorWindow, so a dialog requested before the effects run goes straight
        // there. The queue is created now, so it subscribes to Destroying before the tree's own handlers.
        if (WindowTracking.Current(window, page) is { } now)
        {
            DialogQueue.For(now);
            dialogs.Attach(now);
        }
        context.UseEffect(() => WindowTracking.Watch(window, page, dialogs.Attach, dialogs.Detach), window, page);
        context.UseEffect(() => (Action)dialogs.Close, Array.Empty<object>());
        return dialogs;
    }
}
