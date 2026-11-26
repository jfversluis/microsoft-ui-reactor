using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiPicker = Microsoft.Maui.Controls.Picker;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>Spec 070 §3.9: a label's formatted runs.</summary>
public class LabelInlineTests
{
    private static TestHost Host() => new(MauiHandlers.CreateRegistry());

    private static TapGestureRecognizer Recognizer(Span span) => Assert.Single(span.GestureRecognizers.OfType<TapGestureRecognizer>());

    [Fact]
    public void Inlines_BecomeSpans_WithTheirProperties()
    {
        var host = Host().Render(Label(
            Run("Total ").FontSize(14).TextColor(Colors.Gray),
            Run("$5").Bold().BackgroundColor(Colors.Yellow).CharacterSpacing(2).LineHeight(1.5).TextTransform(TextTransform.Uppercase),
            Hyperlink(" details", () => { })));
        var label = host.View<MauiLabel>();
        var spans = label.FormattedText.Spans;

        Assert.Equal(["Total ", "$5", " details"], spans.Select(s => s.Text));
        Assert.Equal(14, spans[0].FontSize);
        Assert.Equal(Colors.Gray, spans[0].TextColor);
        Assert.Equal(FontAttributes.Bold, spans[1].FontAttributes);
        Assert.Equal(Colors.Yellow, spans[1].BackgroundColor);
        Assert.Equal(2, spans[1].CharacterSpacing);
        Assert.Equal(1.5, spans[1].LineHeight);
        Assert.Equal(TextTransform.Uppercase, spans[1].TextTransform);
        Assert.Equal(TextDecorations.None, spans[0].TextDecorations);
        // A hyperlink is underlined unless it declares otherwise; only it gets a recognizer.
        Assert.Equal(TextDecorations.Underline, spans[2].TextDecorations);
        Assert.Empty(spans[0].GestureRecognizers);
        Recognizer(spans[2]);
    }

    [Fact]
    public void AnUnchangedCount_UpdatesTheSpansInPlace_AndClearsRemovedProperties()
    {
        var host = Host().Render(Label(Run("a").TextColor(Colors.Red).FontSize(20), Run("b")));
        var label = host.View<MauiLabel>();
        var text = label.FormattedText;
        var spans = text.Spans.ToArray();

        host.Render(Label(Run("a2"), Run("b").TextColor(Colors.Blue)));

        Assert.Same(text, label.FormattedText);
        Assert.Same(spans[0], label.FormattedText.Spans[0]);
        Assert.Same(spans[1], label.FormattedText.Spans[1]);
        Assert.Equal("a2", spans[0].Text);
        Assert.Null(spans[0].TextColor);
        Assert.False(spans[0].IsSet(Span.FontSizeProperty));
        Assert.Equal(Colors.Blue, spans[1].TextColor);
    }

    [Fact]
    public void AChangedCount_RebuildsTheFormattedText()
    {
        var host = Host().Render(Label(Run("a")));
        var label = host.View<MauiLabel>();
        var text = label.FormattedText;

        host.Render(Label(Run("a"), Run("b")));

        Assert.NotSame(text, label.FormattedText);
        Assert.Equal(["a", "b"], label.FormattedText.Spans.Select(s => s.Text));
    }

    [Fact]
    public void InlinesAndText_ReplaceEachOther()
    {
        var host = Host().Render(Label("plain"));
        var label = host.View<MauiLabel>();
        Assert.Equal("plain", label.Text);
        Assert.Null(label.FormattedText);

        host.Render(Label(Run("rich")));
        Assert.Equal("rich", label.FormattedText!.Spans[0].Text);

        host.Render(Label("plain again"));
        Assert.Null(label.FormattedText);
        Assert.Equal("plain again", label.Text);
    }

    [Fact]
    public void AHyperlinksDeclaredDecoration_Wins()
    {
        var host = Host().Render(Label(Hyperlink("x", () => { }).Underline(false), Hyperlink("y", () => { }).Strikethrough()));
        var spans = host.View<MauiLabel>().FormattedText.Spans;
        Assert.Equal(TextDecorations.None, spans[0].TextDecorations);
        Assert.Equal(TextDecorations.Strikethrough, spans[1].TextDecorations);
    }

    [Fact]
    public void ATap_InvokesTheCurrentRendersCallback_AndNothingAfterUnmount()
    {
        var calls = new List<string>();
        var host = Host().Render(Label(Run("a"), Hyperlink("link", () => calls.Add("first"))));
        var label = host.View<MauiLabel>();
        var recognizer = Recognizer(label.FormattedText.Spans[1]);

        host.Render(Label(Run("a"), Hyperlink("link", () => calls.Add("second"))));
        Assert.Same(recognizer, Recognizer(label.FormattedText.Spans[1]));
        recognizer.SendTapped(label);
        Assert.Equal(["second"], calls);

        host.Root.Unmount();
        recognizer.SendTapped(label);
        Assert.Equal(["second"], calls);
    }

    [Fact]
    public void ARetiredRecognizer_InvokesNothing()
    {
        var calls = new List<string>();
        var host = Host().Render(Label(Hyperlink("one", () => calls.Add("r1"))));
        var label = host.View<MauiLabel>();
        var r1 = Recognizer(label.FormattedText.Spans[0]);

        // A changed count rebuilds the spans: another hyperlink now sits at index 0.
        host.Render(Label(Hyperlink("two", () => calls.Add("r2")), Run("tail")));
        var r2 = Recognizer(label.FormattedText.Spans[0]);
        r1.SendTapped(label);
        Assert.Empty(calls);
        r2.SendTapped(label);
        Assert.Equal(["r2"], calls);
    }

    [Fact]
    public void ARecognizerReplacedOnTheSameSpan_InvokesNothing()
    {
        var calls = new List<string>();
        var host = Host().Render(Label(Hyperlink("one", () => calls.Add("r1"))));
        var label = host.View<MauiLabel>();
        var span = label.FormattedText.Spans[0];
        var r1 = Recognizer(span);

        host.Render(Label(Run("one")));
        Assert.Empty(span.GestureRecognizers);
        host.Render(Label(Hyperlink("one", () => calls.Add("r2"))));
        Assert.Same(span, label.FormattedText.Spans[0]);
        var r2 = Recognizer(span);
        Assert.NotSame(r1, r2);

        r1.SendTapped(label);
        Assert.Empty(calls);
        r2.SendTapped(label);
        Assert.Equal(["r2"], calls);
    }

    [Fact]
    public void OnTheDirectRegistry_ALabelWithInlines_IsAControlsLabel()
    {
        var host = new TestHost(DirectHandlers.CreateRegistry()).Render(Label("plain"));
        Assert.IsType<DirectLabel>(host.RootView);

        host.Render(Label(Run("rich")));
        Assert.Equal("rich", host.View<MauiLabel>().FormattedText.Spans[0].Text);
    }
}

/// <summary>Spec 070 §3.8: a picker showing a text per item.</summary>
public class PickerDisplayTests
{
    private sealed record Project(string Id, string Name);

    [Fact]
    public void TheDisplayTexts_AreThePickersItems_AndEqualTextsWriteNothing()
    {
        var projects = new[] { new Project("1", "Alpha"), new Project("2", "Beta") };
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Picker(projects, p => p.Name, 1, title: "Project"));
        var picker = host.View<MauiPicker>();
        var source = picker.ItemsSource;
        Assert.Equal(["Alpha", "Beta"], source.Cast<string>());
        Assert.Equal(1, picker.SelectedIndex);
        Assert.Equal("Project", picker.Title);

        // New item instances with the same texts.
        host.Render(Picker(projects.Select(p => p with { }).ToArray(), p => p.Name, 1, title: "Project"));
        Assert.Same(source, picker.ItemsSource);

        host.Render(Picker(new[] { new Project("1", "Alpha"), new Project("2", "Beta 2") }, p => p.Name, 1, title: "Project"));
        Assert.Equal(["Alpha", "Beta 2"], picker.ItemsSource.Cast<string>());
        Assert.Equal(1, picker.SelectedIndex);
    }
}
