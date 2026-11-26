using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Maui.Hosting;

namespace Microsoft.UI.Reactor.Maui;

/// <summary>What a dialog shows: one of MAUI's page dialogs.</summary>
internal abstract record DialogContent;

internal sealed record AlertContent(string Title, string? Message, string? Accept, string Cancel) : DialogContent;

internal sealed record ActionSheetContent(string? Title, string? Cancel, string? Destruction, string[] Buttons) : DialogContent;

internal sealed record PromptContent(
    string Title, string? Message, string Accept, string Cancel, string? Placeholder, int MaxLength, Keyboard? Keyboard, string? InitialValue)
    : DialogContent;

/// <summary>
/// One request in a window's dialog queue. The callbacks run on the window's dispatcher; only one of
/// them runs, once.
/// </summary>
internal sealed class DialogRequest(
    Func<DialogContent> content, Action<object?> answered, Action<Exception> failed, Action cancelled, Func<bool>? isStale = null)
{
    // 0 until settled; settling can race between the dispatcher and a thread whose dispatch was refused.
    private int _settled;

    /// <summary>What to show, read when the dialog is dequeued (a declarative dialog's latest content).</summary>
    public DialogContent Content() => content();

    /// <summary>A declarative dialog whose open is no longer the element's current one.</summary>
    public bool IsStale => isStale?.Invoke() == true;

    public void Answer(object? result) => Settle(() => answered(result));

    public void Fail(Exception error) => Settle(() => failed(error));

    public void Cancel() => Settle(cancelled);

    // A callback that throws (an error report whose dispatch is refused, say) is logged: it must never
    // abort the queue's work around it.
    private void Settle(Action callback)
    {
        if (Interlocked.Exchange(ref _settled, 1) != 0) return;
        try
        {
            callback();
        }
        catch (Exception ex)
        {
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "Dialogs.Settle", ex);
        }
    }
}

/// <summary>What the dialog queue needs from the platform; headless tests replace it.</summary>
internal interface IDialogPlatform
{
    /// <summary>Shows <paramref name="content"/> on <paramref name="presenter"/>; the task completes with the answer.</summary>
    Task<object?> Present(Page presenter, DialogContent content);

    /// <summary>Whether a native dialog is still on screen in <paramref name="window"/> (iOS and Mac Catalyst).</summary>
    bool IsDialogShown(Window window);

    /// <summary>
    /// Whether the window's page or one of its modals is in a navigation transition. When it is,
    /// <paramref name="resume"/> runs once, when that navigation has finished.
    /// </summary>
    bool WaitForNavigation(Window window, Action resume);

    /// <summary>Runs <paramref name="check"/> on the next frame; false when the dispatcher refused it.</summary>
    bool ScheduleFrame(Window window, Action check);

    /// <summary>Runs <paramref name="next"/> once the answered dialog has been dismissed, as far as the platform tells.</summary>
    void AfterAnswer(Window window, Action next);
}

/// <summary>MAUI's page dialogs, and the platform's view of what is on screen (spec 069 §5.1).</summary>
internal sealed class MauiDialogPlatform : IDialogPlatform
{
    public static readonly MauiDialogPlatform Instance = new();

    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    // The answer is passed on inside the platform's button callback (a synchronous continuation, which
    // an await would not give on a thread with a SynchronizationContext), so AfterAnswer alone decides
    // when the next dialog may be shown.
    public Task<object?> Present(Page presenter, DialogContent content) => content switch
    {
        AlertContent { Accept: null } alert => Map(presenter.DisplayAlertAsync(alert.Title, alert.Message, alert.Cancel), static () => false),
        AlertContent alert => Map(presenter.DisplayAlertAsync(alert.Title, alert.Message, alert.Accept, alert.Cancel)),
        ActionSheetContent sheet => Map(presenter.DisplayActionSheetAsync(sheet.Title, sheet.Cancel, sheet.Destruction, sheet.Buttons)),
        PromptContent prompt => Map(presenter.DisplayPromptAsync(
            prompt.Title, prompt.Message, prompt.Accept, prompt.Cancel, prompt.Placeholder, prompt.MaxLength,
            prompt.Keyboard ?? Keyboard.Default, prompt.InitialValue ?? "")),
        _ => throw new ArgumentOutOfRangeException(nameof(content)),
    };

    private static Task<object?> Map<T>(Task<T> task) =>
        task.ContinueWith(static t => (object?)t.GetAwaiter().GetResult(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private static Task<object?> Map(Task task, Func<object?> result) =>
        task.ContinueWith(t =>
        {
            t.GetAwaiter().GetResult();
            return result();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    public bool IsDialogShown(Window window)
    {
#if IOS || MACCATALYST
        // MAUI presents a dialog on the top view controller of the window's chain; until UIKit has
        // finished dismissing it, it is still in that chain.
        if (window.Handler?.PlatformView is UIKit.UIWindow { RootViewController: { } root })
        {
            for (var controller = root; controller is not null; controller = controller.PresentedViewController)
                if (controller is UIKit.UIAlertController) return true;
        }
#endif
        return false;
    }

    public void AfterAnswer(Window window, Action next)
    {
#if ANDROID
        // AppCompat's AlertDialog runs a button's callback from one posted message and posts its
        // dismissal right after (a list row's click dismisses synchronously after its callback), so a
        // turn posted from the callback runs once the dialog has been dismissed.
        if (window.Dispatcher is { } dispatcher && dispatcher.Dispatch(next)) return;
#endif
        next();
    }

    public bool WaitForNavigation(Window window, Action resume)
    {
        var running = WindowPageSetter.FindRunning(window.Page);
        if (running is null)
        {
            foreach (var modal in window.Navigation.ModalStack)
                if ((running = WindowPageSetter.FindRunning(modal)) is not null) break;
        }
        if (running is null) return false;
        void Idle()
        {
            running.BecameIdle -= Idle;
            resume();
        }
        running.BecameIdle += Idle;
        return true;
    }

    public bool ScheduleFrame(Window window, Action check) =>
        window.Dispatcher is { } dispatcher && dispatcher.DispatchDelayed(Frame, check);
}
