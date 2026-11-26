using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Hosting;

namespace Microsoft.UI.Reactor.Maui;

/// <summary>The props of a declarative dialog's component.</summary>
internal sealed record DialogElementProps(DialogContent Content, bool IsOpen, Delegate? OnClosed);

/// <summary>
/// A declarative dialog (spec 069 §5.1): an element that renders nothing native. While
/// <see cref="IsOpen"/> is true the dialog is queued once, on the window's dialog queue that
/// <c>UseDialogs()</c> uses; the answer goes to <c>OnClosed</c>, and the app sets <see cref="IsOpen"/>
/// back to false. Setting it to false, re-opening or unmounting before the answer drops that answer
/// (a dialog already on screen stays until the user answers: MAUI can't dismiss one from code).
/// </summary>
public abstract record DialogElement : ComponentElement
{
    private protected DialogElement(DialogContent content)
        : base(typeof(DialogElementComponent), new DialogElementProps(content, false, null))
    {
    }

    private protected DialogElementProps Dialog => (DialogElementProps)Props!;

    /// <summary>Whether the dialog is requested. Re-opening (false, then true) shows it again.</summary>
    public bool IsOpen
    {
        get => Dialog.IsOpen;
        init => Props = Dialog with { IsOpen = value };
    }
}

/// <summary>An alert: <c>Alert(title, message, close)</c> or <c>Alert(title, message, accept, cancel)</c>.</summary>
public sealed record AlertElement : DialogElement
{
    internal AlertElement(string title, string? message, string? accept, string cancel)
        : base(new AlertContent(title, message, accept, cancel))
    {
        ArgumentException.ThrowIfNullOrEmpty(cancel);
    }

    private AlertContent Content => (AlertContent)Dialog.Content;

    public string Title { get => Content.Title; init => Props = Dialog with { Content = Content with { Title = value } }; }
    public string? Message { get => Content.Message; init => Props = Dialog with { Content = Content with { Message = value } }; }

    /// <summary>The accepting button, or <see langword="null"/> for a one-button alert.</summary>
    public string? Accept { get => Content.Accept; init => Props = Dialog with { Content = Content with { Accept = value } }; }

    /// <summary>The cancelling (or only) button.</summary>
    public string Cancel { get => Content.Cancel; init => Props = Dialog with { Content = Content with { Cancel = value } }; }

    /// <summary>Called with the answer: true for <see cref="Accept"/>, false for <see cref="Cancel"/>.</summary>
    public Action<bool>? OnClosed { get => (Action<bool>?)Dialog.OnClosed; init => Props = Dialog with { OnClosed = value }; }
}

/// <summary>An action sheet: <c>ActionSheet(title, cancel, destruction, buttons…)</c>.</summary>
public sealed record ActionSheetElement : DialogElement
{
    internal ActionSheetElement(string? title, string? cancel, string? destruction, string[] buttons)
        : base(new ActionSheetContent(title, cancel, destruction, buttons))
    {
    }

    private ActionSheetContent Content => (ActionSheetContent)Dialog.Content;

    public string? Title { get => Content.Title; init => Props = Dialog with { Content = Content with { Title = value } }; }
    public string? Cancel { get => Content.Cancel; init => Props = Dialog with { Content = Content with { Cancel = value } }; }
    public string? Destruction { get => Content.Destruction; init => Props = Dialog with { Content = Content with { Destruction = value } }; }
    public string[] Buttons { get => Content.Buttons; init => Props = Dialog with { Content = Content with { Buttons = value } }; }

    /// <summary>Called with the text of the button chosen, or <see langword="null"/> when the sheet was dismissed.</summary>
    public Action<string?>? OnClosed { get => (Action<string?>?)Dialog.OnClosed; init => Props = Dialog with { OnClosed = value }; }
}

/// <summary>A prompt for one line of text: <c>Prompt(title, message)</c>.</summary>
public sealed record PromptElement : DialogElement
{
    internal PromptElement(string title, string? message)
        : base(new PromptContent(title, message, "OK", "Cancel", null, -1, null, null))
    {
    }

    private PromptContent Content => (PromptContent)Dialog.Content;

    public string Title { get => Content.Title; init => Props = Dialog with { Content = Content with { Title = value } }; }
    public string? Message { get => Content.Message; init => Props = Dialog with { Content = Content with { Message = value } }; }
    public string Accept { get => Content.Accept; init => Props = Dialog with { Content = Content with { Accept = value } }; }
    public string Cancel { get => Content.Cancel; init => Props = Dialog with { Content = Content with { Cancel = value } }; }
    public string? Placeholder { get => Content.Placeholder; init => Props = Dialog with { Content = Content with { Placeholder = value } }; }

    /// <summary>The maximum length of the text, or -1 (the default) for none.</summary>
    public int MaxLength { get => Content.MaxLength; init => Props = Dialog with { Content = Content with { MaxLength = value } }; }

    public Keyboard? Keyboard { get => Content.Keyboard; init => Props = Dialog with { Content = Content with { Keyboard = value } }; }
    public string? InitialValue { get => Content.InitialValue; init => Props = Dialog with { Content = Content with { InitialValue = value } }; }

    /// <summary>Called with the text entered, or <see langword="null"/> when the user cancelled.</summary>
    public Action<string?>? OnClosed { get => (Action<string?>?)Dialog.OnClosed; init => Props = Dialog with { OnClosed = value }; }
}

/// <summary>
/// What a <see cref="DialogElement"/> renders: nothing, while it keeps its open on the window's dialog
/// queue. Each open has a token; the open is current while the token is, and only the current open's
/// answer reaches <c>OnClosed</c>, once.
/// </summary>
internal sealed class DialogElementComponent : Component<DialogElementProps>
{
    public override Element? Render()
    {
        var latest = UseRef(Props);
        latest.Current = Props;
        var current = UseRef<object?>(null);
        // A closed element's open is stale from this render on, before the window attach below can hand
        // a request still waiting for the window to the queue (and before the effect's cleanup runs).
        if (!Props.IsOpen) current.Current = null;
        var node = HookNodes.Current(Context, "A dialog element");
        var dialogs = Dialogs.Use(Context);

        UseEffect(() =>
        {
            if (!latest.Current.IsOpen) return static () => { };
            var open = new object();
            current.Current = open;
            bool IsCurrent() => ReferenceEquals(current.Current, open);
            // What the user is shown, read when the dialog is dequeued: its answer goes to that render's
            // OnClosed, so a dialog showing "Delete A?" never runs a later render's callback for B.
            DialogElementProps? shown = null;
            dialogs.Enqueue(new DialogRequest(
                () => (shown = latest.Current).Content,
                answer =>
                {
                    if (!IsCurrent()) return;
                    // Answered: the open stays requested until the app closes it, but is never shown again.
                    current.Current = null;
                    try
                    {
                        switch (shown?.OnClosed)
                        {
                            case Action<bool> onBool:
                                onBool(answer is true);
                                break;
                            case Action<string?> onText:
                                onText(answer as string);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        node.Root.ReportCallbackError(node, ex);
                    }
                },
                error =>
                {
                    if (!IsCurrent()) return;
                    current.Current = null;
                    node.Root.ReportCallbackError(node, error);
                },
                () =>
                {
                    // The window was destroyed: the dialog is dropped.
                    if (IsCurrent()) current.Current = null;
                },
                isStale: () => !IsCurrent()));
            return () =>
            {
                if (IsCurrent()) current.Current = null;
            };
        }, Props.IsOpen);
        return null;
    }
}
