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
    private bool _settled;

    /// <summary>What to show, read when the dialog is dequeued (a declarative dialog's latest content).</summary>
    public DialogContent Content() => content();

    /// <summary>A declarative dialog whose open is no longer the element's current one.</summary>
    public bool IsStale => isStale?.Invoke() == true;

    public void Answer(object? result)
    {
        if (Settle()) answered(result);
    }

    public void Fail(Exception error)
    {
        if (Settle()) failed(error);
    }

    public void Cancel()
    {
        if (Settle()) cancelled();
    }

    private bool Settle()
    {
        if (_settled) return false;
        _settled = true;
        return true;
    }
}

/// <summary>What the dialog queue needs from the platform; headless tests replace it.</summary>
internal interface IDialogPlatform
{
    /// <summary>Shows <paramref name="content"/> on <paramref name="presenter"/>; the task completes with the answer.</summary>
    Task<object?> Present(Page presenter, DialogContent content);

    /// <summary>
    /// Whether a native dialog is still on screen in <paramref name="window"/>.
    /// <paramref name="afterOwnDialog"/> is true while one of the queue's dialogs may still be dismissing.
    /// </summary>
    bool IsDialogShown(Window window, bool afterOwnDialog);

    /// <summary>Whether the window's page or one of its modals is in a navigation transition.</summary>
    bool IsNavigating(Window window);

    /// <summary>Runs <paramref name="check"/> on the next frame.</summary>
    void ScheduleFrame(Window window, Action check);
}

/// <summary>MAUI's page dialogs, and the platform's view of what is on screen (spec 069 §5.1).</summary>
internal sealed class MauiDialogPlatform : IDialogPlatform
{
    public static readonly MauiDialogPlatform Instance = new();

    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    public async Task<object?> Present(Page presenter, DialogContent content)
    {
        switch (content)
        {
            case AlertContent { Accept: null } alert:
                await presenter.DisplayAlertAsync(alert.Title, alert.Message, alert.Cancel);
                return false;
            case AlertContent alert:
                return await presenter.DisplayAlertAsync(alert.Title, alert.Message, alert.Accept, alert.Cancel);
            case ActionSheetContent sheet:
                return await presenter.DisplayActionSheetAsync(sheet.Title, sheet.Cancel, sheet.Destruction, sheet.Buttons);
            case PromptContent prompt:
                return await presenter.DisplayPromptAsync(
                    prompt.Title, prompt.Message, prompt.Accept, prompt.Cancel, prompt.Placeholder, prompt.MaxLength,
                    prompt.Keyboard ?? Keyboard.Default, prompt.InitialValue ?? "");
            default:
                throw new ArgumentOutOfRangeException(nameof(content));
        }
    }

    public bool IsDialogShown(Window window, bool afterOwnDialog)
    {
#if IOS || MACCATALYST
        // MAUI presents a dialog on the top view controller of the window's chain; until UIKit has
        // finished dismissing it, it is still in that chain.
        if (window.Handler?.PlatformView is UIKit.UIWindow { RootViewController: { } root })
        {
            for (var controller = root; controller is not null; controller = controller.PresentedViewController)
                if (controller is UIKit.UIAlertController) return true;
        }
        return false;
#elif ANDROID
        // An AlertDialog takes the window focus while it is shown and gives it back to the activity
        // once it is dismissed. Checked only after one of the queue's own dialogs: the activity can
        // lack focus for other reasons (another app's window beside it) while none of ours is shown.
        return afterOwnDialog && window.Handler?.PlatformView is global::Android.App.Activity { HasWindowFocus: false };
#else
        return false;
#endif
    }

    public bool IsNavigating(Window window)
    {
        if (WindowPageSetter.FindRunning(window.Page) is not null) return true;
        foreach (var modal in window.Navigation.ModalStack)
            if (WindowPageSetter.FindRunning(modal) is not null) return true;
        return false;
    }

    public void ScheduleFrame(Window window, Action check)
    {
        if (window.Dispatcher is not { } dispatcher || !dispatcher.DispatchDelayed(Frame, check))
            throw new InvalidOperationException("The window's dispatcher refused the dialog queue's work; the app is probably shutting down.");
    }
}
