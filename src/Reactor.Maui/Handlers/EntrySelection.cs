using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>
/// An entry's selection as the platform shows it: what <c>OnSelectionChanged</c> reports.
/// </summary>
internal readonly record struct PlatformSelectionState(int Cursor, int Length, string Text)
{
    public bool SameSelection(PlatformSelectionState other) => Cursor == other.Cursor && Length == other.Length;

    public string SelectedText =>
        Cursor >= 0 && Length > 0 && Cursor + Length <= Text.Length ? Text.Substring(Cursor, Length) : "";
}

/// <summary>Reads an entry's selection from its platform view, not from the virtual view's fields.</summary>
internal static class PlatformSelection
{
    /// <summary>
    /// Replaces the platform readback in headless tests. An <see cref="AsyncLocal{T}"/>, so each test's
    /// stub stays in its own flow while tests run in parallel.
    /// </summary>
    internal static readonly AsyncLocal<Func<IView, PlatformSelectionState?>?> TestReader = new();

    public static PlatformSelectionState? Read(IView view)
    {
        if (TestReader.Value is { } reader) return reader(view);
#if ANDROID
        if (view.Handler?.PlatformView is global::Android.Widget.EditText edit)
        {
            var text = edit.Text ?? "";
            var start = Math.Max(0, Math.Min(edit.SelectionStart, edit.SelectionEnd));
            var end = Math.Max(edit.SelectionStart, edit.SelectionEnd);
            return new(start, Math.Max(0, end - start), text);
        }
#elif IOS || MACCATALYST
        if (view.Handler?.PlatformView is global::UIKit.UITextField field)
        {
            var text = field.Text ?? "";
            if (field.SelectedTextRange is not { } range) return new(text.Length, 0, text);
            var start = (int)field.GetOffsetFromPosition(field.BeginningOfDocument, range.Start);
            var length = (int)field.GetOffsetFromPosition(range.Start, range.End);
            return new(start, Math.Max(0, length), text);
        }
#elif WINDOWS
        if (view.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox box)
            return new(box.SelectionStart, box.SelectionLength, box.Text ?? "");
#endif
        return null;
    }
}

/// <summary>
/// Reports an entry's selection changes once per burst against one baseline (spec 070 §2.8). The
/// baseline is the selection Reactor last knew the platform to show: read when the platform view
/// appears, set to a write's effective value, and to whatever each flush observes. A flush reads the
/// platform and reports when that differs; an ambiguous change is reported rather than swallowed.
/// </summary>
internal sealed class SelectionTracker
{
    private static readonly ConditionalWeakTable<object, SelectionTracker> Trackers = new();

    private readonly IView _view;
    private PlatformSelectionState? _baseline;
    private bool _posted;
    private int _applyDepth;
    private bool _notifiedDuringApply;
    private Action? _flush;

    private SelectionTracker(IView view) => _view = view;

    public static SelectionTracker For(IView view) => Trackers.GetValue(view, static v => new SelectionTracker((IView)v));

    public static SelectionTracker? Find(IView view) => Trackers.TryGetValue(view, out var tracker) ? tracker : null;

    /// <summary>Whether the element reports or declares the selection, so tracking is worth its cost.</summary>
    public static bool IsUsed(EntryElement element) =>
        element.OnSelectionChanged is not null || element.CursorPosition is not null || element.SelectionLength is not null;

    /// <summary>
    /// The platform view appeared (a handler connected): what it shows is the baseline. MAUI sets a
    /// Core view's handler before the handler creates and maps its platform view, so with
    /// <paramref name="context"/> an observation that finds no platform view yet is posted once.
    /// </summary>
    public void ObservePlatform(HandlerContext<EntryElement>? context = null)
    {
        if (PlatformSelection.Read(_view) is { } state) _baseline = state;
        else if (context is { } c) c.PostReport(() => ObservePlatform());
    }

    /// <summary>A programmatic write: its effective value, clamped to the text as the platform clamps, is the baseline.</summary>
    public void Wrote(int cursor, int length, string text)
    {
        var c = Math.Clamp(cursor, 0, text.Length);
        _baseline = new PlatformSelectionState(c, Math.Clamp(length, 0, text.Length - c), text);
    }

    /// <summary>Whether a keyboard, password or MaxLength change, which can move the platform's selection, is in this apply.</summary>
    public static bool InputChanged(EntryElement? previous, EntryElement next) =>
        previous is not null
        && (!Equals(previous.Keyboard, next.Keyboard) || previous.IsPassword != next.IsPassword || previous.MaxLength != next.MaxLength);

    /// <summary>
    /// Applies an entry element with its selection tracked (spec 070 §2.8), for both handlers.
    /// <paramref name="applyRest"/> applies everything but the selection exactly as it does without
    /// tracking (the text in its own echo scope, input-type and MaxLength outside one), so a text change
    /// the platform makes, such as a MaxLength truncation, still reaches <c>OnTextChanged</c>; it returns
    /// whether it wrote the text. The selection is written, clamped to zero, when the declaration changes.
    /// </summary>
    public static void Apply(HandlerContext<EntryElement> context, IView view, EntryElement? previous, EntryElement next,
        Func<bool> applyRest, Action<int?, int?> writeSelection, Func<(int Cursor, int Length, string Text)> readSelection)
    {
        var selection = IsUsed(next) ? For(view) : Find(view);
        if (selection is null)
        {
            applyRest();
            return;
        }
        if (previous is null || !IsUsed(previous)) selection.ObservePlatform();
        selection.BeginApply();
        var wrote = false;
        try
        {
            // An input-type or MaxLength change can move the selection on its own (Android restores only
            // the cursor; UIKit can select all), so it is observed like a text write.
            wrote = applyRest() | InputChanged(previous, next);
            if ((next.CursorPosition != previous?.CursorPosition || next.SelectionLength != previous?.SelectionLength)
                && (next.CursorPosition is not null || next.SelectionLength is not null))
            {
                // A Controls entry rejects a negative value (InputView validates it); a direct one clamps.
                context.WriteSuppressed(view, () => writeSelection(
                    next.CursorPosition is { } c ? Math.Max(0, c) : null,
                    next.SelectionLength is { } l ? Math.Max(0, l) : null));
                var (cursor, length, text) = readSelection();
                selection.Wrote(cursor, length, text);
                wrote = true;
            }
        }
        finally
        {
            selection.EndApply(context, wrote);
        }
    }

    // Separate from the echo scope: the scope covers only the text and selection writes, so a text
    // change the platform makes during an input-type update is still reported, while every selection
    // notification during the whole apply waits for the observation after it.
    public void BeginApply() => _applyDepth++;

    /// <summary>
    /// Ends an apply. One that wrote the text or the selection, or saw a selection notification, posts
    /// the observation, after anything the platform deferred during the apply.
    /// </summary>
    public void EndApply(HandlerContext<EntryElement> context, bool wrote)
    {
        if (--_applyDepth > 0) return;
        var post = wrote || _notifiedDuringApply;
        _notifiedDuringApply = false;
        if (post) Post(context);
    }

    /// <summary>The platform (or the virtual view) reported a selection change.</summary>
    public void Notified(HandlerContext<EntryElement> context)
    {
        if (_applyDepth > 0)
        {
            _notifiedDuringApply = true;
            return;
        }
        Post(context);
    }

    private void Post(HandlerContext<EntryElement> context)
    {
        if (_posted || !context.IsMounted) return;
        _posted = true;
        _flush ??= () => Flush(context);
        context.PostReport(_flush);
    }

    private void Flush(HandlerContext<EntryElement> context)
    {
        // Detached first: a notification raised by the callback, or a render it causes, starts a new burst.
        _posted = false;
        if (PlatformSelection.Read(_view) is not { } observed) return;
        var changed = _baseline is not { } baseline || !baseline.SameSelection(observed);
        var hadBaseline = _baseline is not null;
        _baseline = observed;
        if (!changed || !hadBaseline) return;
        context.Live?.OnSelectionChanged?.Invoke(observed.SelectedText, observed.Cursor, observed.Length);
    }
}
