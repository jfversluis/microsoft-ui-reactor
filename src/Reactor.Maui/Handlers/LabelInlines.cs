using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Hosting;
using MauiLabel = Microsoft.Maui.Controls.Label;

namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>
/// A label's formatted text (spec 070 §3.9): one MAUI <see cref="Span"/> per inline, updated in place
/// while the number of inlines stays the same, rebuilt when it changes. A hyperlink's span carries a
/// tap recognizer that invokes only while it is current.
/// </summary>
internal static class LabelInlines
{
    private sealed class State
    {
        public FormattedString? Text;
        public readonly List<Span> Spans = [];
        // The recognizer currently installed on each span; a retained old one is not current.
        public readonly ConditionalWeakTable<Span, TapGestureRecognizer> Installed = new();
    }

    private static readonly ConditionalWeakTable<MauiLabel, State> States = new();

    /// <summary>Applies the label's text: its inlines when it has any, else its plain text.</summary>
    public static void Apply(HandlerContext<LabelElement> context, MauiLabel view, LabelElement? previous, LabelElement next)
    {
        if (next.Inlines is not { } inlines)
        {
            if (previous?.Inlines is not null)
            {
                // Setting Text clears MAUI's formatted text too; the state goes with it.
                States.Remove(view);
                view.FormattedText = null;
                view.Text = next.Text;
            }
            else if (previous?.Text != next.Text)
            {
                view.Text = next.Text;
            }
            return;
        }

        var state = States.GetValue(view, static _ => new State());
        var old = previous?.Inlines;
        if (state.Text is null || old is null || old.Length != inlines.Length || !ReferenceEquals(view.FormattedText, state.Text))
        {
            Rebuild(context, view, state, inlines);
            return;
        }
        for (var i = 0; i < inlines.Length; i++)
        {
            var span = state.Spans[i];
            ApplySpan(span, old[i], inlines[i]);
            SyncRecognizer(context, view, state, span, i, inlines[i]);
        }
    }

    private static void Rebuild(HandlerContext<LabelElement> context, MauiLabel view, State state, RichTextInline[] inlines)
    {
        var text = new FormattedString();
        state.Spans.Clear();
        for (var i = 0; i < inlines.Length; i++)
        {
            var span = new Span();
            ApplySpan(span, null, inlines[i]);
            text.Spans.Add(span);
            state.Spans.Add(span);
            SyncRecognizer(context, view, state, span, i, inlines[i]);
        }
        state.Text = text;
        view.FormattedText = text;
    }

    private static TextDecorations? Decorations(RichTextInline inline) =>
        inline.TextDecorations ?? (inline is RichTextHyperlink ? TextDecorations.Underline : null);

    private static void ApplySpan(Span span, RichTextInline? previous, RichTextInline next)
    {
        if (previous?.InlineText != next.InlineText) span.Text = next.InlineText;
        Props.Set(span, Span.FontSizeProperty, previous?.FontSize, next.FontSize);
        Props.Set(span, Span.FontAttributesProperty, previous?.FontAttributes, next.FontAttributes);
        Props.SetRef(span, Span.FontFamilyProperty, previous?.FontFamily, next.FontFamily);
        Props.SetRef(span, Span.TextColorProperty, previous?.TextColor, next.TextColor);
        Props.SetRef(span, Span.BackgroundColorProperty, previous?.BackgroundColor, next.BackgroundColor);
        Props.Set(span, Span.CharacterSpacingProperty, previous?.CharacterSpacing, next.CharacterSpacing);
        Props.Set(span, Span.TextDecorationsProperty, previous is null ? null : Decorations(previous), Decorations(next));
        Props.Set(span, Span.LineHeightProperty, previous?.LineHeight, next.LineHeight);
        Props.Set(span, Span.TextTransformProperty, previous?.TextTransform, next.TextTransform);
    }

    private static void SyncRecognizer(HandlerContext<LabelElement> context, MauiLabel view, State state, Span span, int index, RichTextInline inline)
    {
        var has = state.Installed.TryGetValue(span, out var installed);
        if (inline is RichTextHyperlink)
        {
            if (has) return;
            var recognizer = new TapGestureRecognizer();
            // MAUI raises Tapped with the label as the sender, so the recognizer checks its own identity.
            recognizer.Tapped += (_, _) =>
            {
                // Current only while this recognizer is the one on this span, the span is still at this
                // index of the label's formatted text, and the element is mounted.
                if (context.Live?.Inlines is not { } inlines) return;
                if (!ReferenceEquals(view.FormattedText, state.Text) || index >= state.Spans.Count || !ReferenceEquals(state.Spans[index], span)) return;
                if (!state.Installed.TryGetValue(span, out var current) || !ReferenceEquals(current, recognizer)) return;
                if (index < inlines.Length && inlines[index] is RichTextHyperlink link) link.OnClick();
            };
            span.GestureRecognizers.Add(recognizer);
            state.Installed.AddOrUpdate(span, recognizer);
        }
        else if (has)
        {
            span.GestureRecognizers.Remove(installed!);
            state.Installed.Remove(span);
        }
    }
}
