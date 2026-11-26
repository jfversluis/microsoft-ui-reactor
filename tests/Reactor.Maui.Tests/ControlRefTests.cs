using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiEntry = Microsoft.Maui.Controls.Entry;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiRefreshView = Microsoft.Maui.Controls.RefreshView;
using MauiScrollView = Microsoft.Maui.Controls.ScrollView;
using MauiWebView = Microsoft.Maui.Controls.WebView;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>A stand-in for the platform's text field selection, read by <see cref="PlatformSelection"/>.</summary>
internal sealed class FakeSelection
{
    public int Cursor;
    public int Length;
    public string Text = "";

    public void Set(int cursor, int length, string? text = null)
    {
        Cursor = cursor;
        Length = length;
        if (text is not null) Text = text;
    }

    public IDisposable Install()
    {
        PlatformSelection.TestReader.Value = _ => new PlatformSelectionState(Cursor, Length, Text);
        return new Reset();
    }

    private sealed class Reset : IDisposable
    {
        public void Dispose() => PlatformSelection.TestReader.Value = null;
    }
}

public class EntrySelectionTests
{
    public static TheoryData<string> Registries => ["controls", "direct"];

    private static TestHost Host(string registry) =>
        new(registry == "direct" ? DirectHandlers.CreateRegistry() : MauiHandlers.CreateRegistry());

    private static ITextInput Input(TestHost host) => (ITextInput)host.RootView!;

    // What MAUI's handler does when the platform selection changes: cursor, then length, as two writes.
    private static void UserMoves(TestHost host, FakeSelection platform, int cursor, int length)
    {
        platform.Set(cursor, platform.Length);
        Input(host).CursorPosition = cursor;
        platform.Set(cursor, length);
        Input(host).SelectionLength = length;
    }

    private static EntryElement Field(string text, List<string> reports, int? cursor = null, int? length = null) =>
        Entry(text, _ => { }) with
        {
            CursorPosition = cursor,
            SelectionLength = length,
            OnSelectionChanged = (selected, c, l) => reports.Add($"{selected}|{c}|{l}"),
        };

    [Theory]
    [MemberData(nameof(Registries))]
    public void ADeclaredSelection_IsWrittenAtMountAndWhenItChanges_NeverRestored(string registry)
    {
        var platform = new FakeSelection { Text = "hello world" };
        using var _ = platform.Install();
        var reports = new List<string>();
        var host = Host(registry).Render(Field("hello world", reports, 2, 1));
        Assert.Equal((2, 1), (Input(host).CursorPosition, Input(host).SelectionLength));

        UserMoves(host, platform, 5, 0);
        host.Render(Field("hello world", reports, 2, 1));
        Assert.Equal((5, 0), (Input(host).CursorPosition, Input(host).SelectionLength));

        host.Render(Field("hello world", reports, 3, 1));
        Assert.Equal((3, 1), (Input(host).CursorPosition, Input(host).SelectionLength));

        UserMoves(host, platform, 4, 0);
        host.Render(Field("hello world", reports, null, null));
        Assert.Equal((4, 0), (Input(host).CursorPosition, Input(host).SelectionLength));
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void AProgrammaticWrite_ReportsNothing(string registry)
    {
        var platform = new FakeSelection { Text = "hello" };
        using var _ = platform.Install();
        var reports = new List<string>();
        var host = Host(registry).Render(Field("hello", reports));
        host.Scheduler.Drain();

        // The platform applies the write (synchronously here, or after the apply: either way before the flush).
        host.Render(Field("hello", reports, 1, 3));
        platform.Set(1, 3);
        host.Scheduler.Drain();
        host.Render(Field("hello", reports, 4, 0));
        host.Render(Field("hello", reports, 0, 2));
        platform.Set(0, 2);
        host.Scheduler.Drain();

        Assert.Empty(reports);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void SplitNotifications_GiveOneReport_WithBothFinalValues(string registry)
    {
        var platform = new FakeSelection { Text = "hello" };
        using var _ = platform.Install();
        var reports = new List<string>();
        var host = Host(registry).Render(Field("hello", reports));
        host.Scheduler.Drain();

        UserMoves(host, platform, 1, 3);
        host.Scheduler.Drain();

        Assert.Equal(["ell|1|3"], reports);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void AWriteThenACaretMovedByText_ThenAMoveBackToTheWrite_AreAllReported(string registry)
    {
        var platform = new FakeSelection { Text = "0123456789" };
        using var _ = platform.Install();
        var reports = new List<string>();
        var host = Host(registry).Render(Field("0123456789", reports));
        host.Scheduler.Drain();

        host.Render(Field("0123456789", reports, 8, 0));
        // Before the flush, a new declared text moves the platform's caret to its end.
        const string longer = "0123456789abcdefghij";
        host.Render(Field(longer, reports, 8, 0));
        platform.Set(20, 0, longer);
        Input(host).CursorPosition = 20;
        host.Scheduler.Drain();
        Assert.Equal(["|20|0"], reports);

        UserMoves(host, platform, 8, 0);
        host.Scheduler.Drain();
        Assert.Equal(["|20|0", "|8|0"], reports);
    }

    [Theory]
    [InlineData("controls", "0123", 4)]
    [InlineData("controls", "0123456789AB", 8)]
    [InlineData("direct", "0123", 4)]
    [InlineData("direct", "0123456789AB", 8)]
    public void TextThatChangesBetweenTheWriteAndTheFlush_ReportsTheActualSelectionOnce(string registry, string textAtExecution, int clampedCursor)
    {
        var platform = new FakeSelection { Text = "0123456789" };
        using var _ = platform.Install();
        var reports = new List<string>();
        var host = Host(registry).Render(Field("0123456789", reports));
        host.Scheduler.Drain();

        host.Render(Field("0123456789", reports, 8, 0));
        // Android runs a focused SetSelection later and clamps it to the text it finds then.
        platform.Set(clampedCursor, 0, textAtExecution);
        host.Scheduler.Drain();
        host.Scheduler.Drain();

        Assert.Equal(clampedCursor == 8 ? [] : [$"|{clampedCursor}|0"], reports);
    }

    [Fact]
    public void ANotificationInsideAnApply_IsObservedOnlyAfterTheApply_AndWhatThePlatformDeferredBeforeIt()
    {
        var platform = new FakeSelection { Text = "hello" };
        using var _ = platform.Install();
        var reports = new List<string>();
        var host = Host("controls").Render(Field("hello", reports));
        host.Scheduler.Drain();
        var entry = (MauiEntry)host.RootView!;
        // Android: each focused write (cursor, then length) posts a SetSelection, which runs after the apply.
        entry.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MauiEntry.CursorPosition) or nameof(MauiEntry.SelectionLength))
            {
                var (cursor, length) = (entry.CursorPosition, entry.SelectionLength);
                host.Scheduler.Post(() => platform.Set(cursor, length));
            }
        };

        host.Render(Field("hello", reports, 3, 1));
        host.Scheduler.Drain();

        Assert.Empty(reports);
        Assert.Equal((3, 1), (platform.Cursor, platform.Length));
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void ATextRestoreThatMovesTheCaret_IsReportedAsTheRealSelection(string registry)
    {
        var platform = new FakeSelection { Text = "ab" };
        using var _ = platform.Install();
        var reports = new List<string>();
        var texts = new List<string>();
        // The app refuses typing: its text stays "ab".
        EntryElement Refusing() => Entry("ab", texts.Add) with { OnSelectionChanged = (s, c, l) => reports.Add($"{s}|{c}|{l}") };
        var host = Host(registry).Render(Refusing());
        host.Scheduler.Drain();

        // The user types "c" at the end: the platform shows "abc" with the caret after it.
        platform.Set(3, 0, "abc");
        Input(host).Text = "abc";
        Input(host).CursorPosition = 3;
        Assert.Equal(["abc"], texts);
        // The restore writes "ab" back; the platform's caret ends at 2.
        platform.Set(2, 0, "ab");
        host.Scheduler.Drain();

        Assert.Equal("ab", Input(host).Text);
        Assert.Equal("|2|0", reports[^1]);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void AnInputTypeChangeThatMovesTheSelectionSilently_IsObservedAfterTheApply(string registry)
    {
        var platform = new FakeSelection { Text = "hello world" };
        using var _ = platform.Install();
        var reports = new List<string>();
        var host = Host(registry).Render(Field("hello world", reports, 6, 5));
        platform.Set(6, 5);
        host.Scheduler.Drain();

        // UIKit selects everything when the password mode changes, without a notification MAUI forwards.
        host.Render(Field("hello world", reports, 6, 5) with { IsPassword = true });
        platform.Set(0, 11);
        host.Scheduler.Drain();

        Assert.Equal(["hello world|0|11"], reports);
    }

    [Theory]
    [InlineData("controls", false)]
    [InlineData("controls", true)]
    [InlineData("direct", false)]
    [InlineData("direct", true)]
    public void AMaxLengthTruncationThePlatformMakes_IsReportedAsText_WithOrWithoutSelectionTracking(string registry, bool tracked)
    {
        var platform = new FakeSelection { Text = "abcdefgh" };
        using var _ = platform.Install();
        var texts = new List<string>();
        EntryElement Field(int? maxLength) => Entry("abcdefgh", texts.Add) with
        {
            MaxLength = maxLength,
            OnSelectionChanged = tracked ? (_, _, _) => { } : null,
        };
        var host = Host(registry).Render(Field(null));
        // What the platform does when MaxLength shrinks under the text: it truncates the text and the
        // handler writes the result back to the virtual view.
        void Truncate() => Input(host).Text = (Input(host).Text ?? "")[..Math.Min(5, (Input(host).Text ?? "").Length)];
        if (host.RootView is MauiEntry entry)
            entry.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MauiEntry.MaxLength)) Truncate(); };
        else
            ((DirectEntry)host.RootView!).Handler = new StubViewHandler { OnUpdateValue = p => { if (p == nameof(ITextInput.MaxLength)) Truncate(); } };

        host.Render(Field(5));

        Assert.Equal(["abcde"], texts);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void ANegativeDeclaredSelection_IsClampedToZero(string registry)
    {
        var platform = new FakeSelection { Text = "hello" };
        using var _ = platform.Install();
        var host = Host(registry).Render(Field("hello", [], -3, -1));
        Assert.Equal((0, 0), (Input(host).CursorPosition, Input(host).SelectionLength));
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void NothingIsReportedAfterUnmount(string registry)
    {
        var platform = new FakeSelection { Text = "hello" };
        using var _ = platform.Install();
        var reports = new List<string>();
        var host = Host(registry).Render(VStack(Field("hello", reports)));
        host.Scheduler.Drain();
        var input = (ITextInput)((IList<IView>)host.RootView!)[0];

        platform.Set(2, 0);
        input.CursorPosition = 2;
        host.Render(VStack());
        host.Scheduler.Drain();

        Assert.Empty(reports);
    }

    [Fact]
    public void ANotificationRaisedByTheCallback_StartsANewBurst()
    {
        var platform = new FakeSelection { Text = "hello" };
        using var _ = platform.Install();
        var reports = new List<string>();
        TestHost? host = null;
        var first = true;
        host = Host("controls").Render(Entry("hello", _ => { }) with
        {
            OnSelectionChanged = (s, c, l) =>
            {
                reports.Add($"{c}");
                if (!first) return;
                first = false;
                platform.Set(4, 0);
                Input(host!).CursorPosition = 4;
            },
        });
        host.Scheduler.Drain();

        UserMoves(host, platform, 2, 0);
        host.Scheduler.Drain();

        Assert.Equal(["2", "4"], reports);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void TextReportsStaySynchronous_WhenTheSelectionIsUsed(string registry)
    {
        var platform = new FakeSelection { Text = "a" };
        using var _ = platform.Install();
        var texts = new List<string>();
        var host = Host(registry).Render(Entry("a", texts.Add) with { OnSelectionChanged = (_, _, _) => { } });

        Input(host).Text = "ab";

        Assert.Equal(["ab"], texts);
        host.Scheduler.Drain();
        Assert.Equal("a", Input(host).Text);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void ReturnType_IsSetUpdatedAndCleared_AndCompletedIsReported(string registry)
    {
        var completed = 0;
        EntryElement Field(ReturnType? type) => Entry("a") with { ReturnType = type, OnCompleted = () => completed++ };
        var host = Host(registry).Render(Field(ReturnType.Search));
        var entry = (IEntry)host.RootView!;
        Assert.Equal(ReturnType.Search, entry.ReturnType);

        host.Render(Field(ReturnType.Done));
        Assert.Equal(ReturnType.Done, entry.ReturnType);

        host.Render(Field(null));
        Assert.Equal(ReturnType.Default, entry.ReturnType);

        entry.Completed();
        Assert.Equal(1, completed);
    }
}

public class WebViewTests
{
    private static TestHost Host() => new(MauiHandlers.CreateRegistry());

    private static bool Ask(TestHost host, string url) =>
        ((IWebView)FindWebView(host)).Navigating(WebNavigationEvent.NewPage, url);

    private static MauiWebView FindWebView(TestHost host) =>
        host.RootView as MauiWebView ?? (MauiWebView)((IList<IView>)host.RootView!)[0];

    [Fact]
    public void OnNavigating_CancelsWhatTheCallbackRefuses()
    {
        var asked = new List<string>();
        var host = Host().Render(WebView("https://a.test/") with
        {
            OnNavigating = e =>
            {
                asked.Add(e.Url);
                e.Cancel = e.Url.Contains("blocked");
            },
        });

        Assert.True(Ask(host, "https://a.test/blocked"));
        Assert.False(Ask(host, "https://a.test/fine"));
        Assert.Equal(["https://a.test/blocked", "https://a.test/fine"], asked);
    }

    [Fact]
    public void OnNavigating_IsNotCalledAfterUnmount_AndTheNavigationIsAllowed()
    {
        var asked = 0;
        var host = Host().Render(VStack(WebView("https://a.test/") with { OnNavigating = e => { asked++; e.Cancel = true; } }));
        var view = (IWebView)FindWebView(host);

        host.Render(VStack());

        Assert.False(view.Navigating(WebNavigationEvent.NewPage, "https://a.test/x"));
        Assert.Equal(0, asked);
    }

    [Fact]
    public void AThrowingOnNavigating_CancelsAndReachesTheBoundary()
    {
        string? caught = null;
        var host = Host().Render(VStack(ErrorBoundary(
            WebView("https://a.test/") with { OnNavigating = _ => throw new InvalidOperationException("guard failed") },
            e => Label(caught = e.Message))));
        var view = (IWebView)FindWebView(host);

        Assert.True(view.Navigating(WebNavigationEvent.NewPage, "https://a.test/x"));
        host.Scheduler.Drain();

        Assert.Equal("guard failed", caught);
    }

    [Fact]
    public void OnNavigated_IsPostedAfterThePlatformCallback_WithItsResult()
    {
        var completed = new List<string>();
        var host = Host().Render(WebView("https://a.test/") with { OnNavigated = e => completed.Add($"{e.Url} {e.Result}") });
        var view = (IWebView)FindWebView(host);

        view.Navigated(WebNavigationEvent.NewPage, "https://a.test/", WebNavigationResult.Success);
        view.Navigated(WebNavigationEvent.NewPage, "https://a.test/x", WebNavigationResult.Cancel);
        Assert.Empty(completed);

        host.Scheduler.Drain();
        Assert.Equal(["https://a.test/ Success", "https://a.test/x Cancel"], completed);
    }

    [Fact]
    public void APostedReport_StopsOnceACallbackUnmountsTheElement()
    {
        var completed = new List<string>();
        TestHost? host = null;
        host = Host().Render(VStack(WebView("https://a.test/") with
        {
            OnNavigated = e =>
            {
                completed.Add(e.Url);
                host!.Render(VStack());
            },
        }));
        var view = (IWebView)FindWebView(host);

        view.Navigated(WebNavigationEvent.NewPage, "https://a.test/1", WebNavigationResult.Success);
        view.Navigated(WebNavigationEvent.NewPage, "https://a.test/2", WebNavigationResult.Success);
        host.Scheduler.Drain();

        Assert.Equal(["https://a.test/1"], completed);
    }

    [Fact]
    public void AThrowingPostedReport_ReachesTheBoundary()
    {
        string? caught = null;
        var host = Host().Render(VStack(ErrorBoundary(
            WebView("https://a.test/") with { OnNavigated = _ => throw new InvalidOperationException("report failed") },
            e => Label(caught = e.Message))));
        var view = (IWebView)FindWebView(host);

        view.Navigated(WebNavigationEvent.NewPage, "https://a.test/", WebNavigationResult.Success);
        host.Scheduler.Drain();

        Assert.Equal("report failed", caught);
        Assert.IsType<MauiLabel>(((IList<IView>)host.RootView!)[0]);
    }

    [Fact]
    public void TheSource_LoadsWhenTheDeclarationChanges_AndHtmlWins()
    {
        var host = Host().Render(WebView("https://a.test/"));
        var view = FindWebView(host);
        var first = Assert.IsType<UrlWebViewSource>(view.Source);
        Assert.Equal("https://a.test/", first.Url);

        host.Render(WebView("https://a.test/"));
        Assert.Same(first, view.Source);

        host.Render(WebView("https://b.test/"));
        Assert.Equal("https://b.test/", Assert.IsType<UrlWebViewSource>(view.Source).Url);

        host.Render(new WebViewElement("https://b.test/", "<p>hi</p>"));
        Assert.Equal("<p>hi</p>", Assert.IsType<HtmlWebViewSource>(view.Source).Html);

        host.Render(new WebViewElement(null));
        Assert.Null(view.Source);
    }
}

public class ScrollAndRefreshTests
{
    public static TheoryData<string> Registries => ["controls", "direct"];

    private static TestHost Host(string registry = "controls") =>
        new(registry == "direct" ? DirectHandlers.CreateRegistry() : MauiHandlers.CreateRegistry());

    [Fact]
    public void ScrollView_SetsUpdatesAndClearsItsProperties_AndReportsScrolling()
    {
        var scrolled = new List<(double, double)>();
        ScrollViewElement Scroll(ScrollOrientation? orientation, ScrollBarVisibility? bars) =>
            ScrollView(Label("x")) with
            {
                Orientation = orientation,
                VerticalScrollBarVisibility = bars,
                HorizontalScrollBarVisibility = bars,
                OnScrolled = (x, y) => scrolled.Add((x, y)),
            };
        var host = Host().Render(Scroll(ScrollOrientation.Both, ScrollBarVisibility.Never));
        var view = host.View<MauiScrollView>();
        Assert.Equal((ScrollOrientation.Both, ScrollBarVisibility.Never, ScrollBarVisibility.Never),
            (view.Orientation, view.VerticalScrollBarVisibility, view.HorizontalScrollBarVisibility));

        host.Render(Scroll(ScrollOrientation.Horizontal, ScrollBarVisibility.Always));
        Assert.Equal((ScrollOrientation.Horizontal, ScrollBarVisibility.Always), (view.Orientation, view.VerticalScrollBarVisibility));

        host.Render(Scroll(null, null));
        Assert.Equal((ScrollOrientation.Vertical, ScrollBarVisibility.Default, ScrollBarVisibility.Default),
            (view.Orientation, view.VerticalScrollBarVisibility, view.HorizontalScrollBarVisibility));

        view.SetScrolledPosition(3, 40);
        Assert.Equal([(3.0, 40.0)], scrolled);
    }

    private sealed record RefreshProps(bool Accept, List<string> Log);

    private sealed class Refreshing : Component<RefreshProps>
    {
        public static Action<bool>? Set;

        public override Element Render()
        {
            var (refreshing, setRefreshing) = UseState(false);
            Set = setRefreshing;
            return RefreshView(Label("content"), refreshing, () =>
            {
                Props.Log.Add("refresh");
                if (Props.Accept) setRefreshing(true);
            }) with { RefreshColor = refreshing ? Colors.Red : null };
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RefreshView_ReportsAUserRefreshOnce_AndARefusalRestoresIt(bool accept)
    {
        var log = new List<string>();
        var host = Host().Render(Component<Refreshing, RefreshProps>(new(accept, log)));
        var view = host.View<MauiRefreshView>();

        ((IRefreshView)view).IsRefreshing = true;
        host.Scheduler.Drain();

        Assert.Equal(["refresh"], log);
        Assert.Equal(accept, view.IsRefreshing);
        Assert.Equal(accept ? Colors.Red : null, view.RefreshColor);
    }

    [Fact]
    public void RefreshView_DeclaredChanges_AreNotReported_AndNothingAfterUnmount()
    {
        var log = new List<string>();
        var host = Host().Render(VStack(Component<Refreshing, RefreshProps>(new(true, log))));
        var view = (MauiRefreshView)((IList<IView>)host.RootView!)[0];

        Refreshing.Set!(true);
        host.Scheduler.Drain();
        Assert.True(view.IsRefreshing);
        Assert.Equal(Colors.Red, view.RefreshColor);
        Refreshing.Set!(false);
        host.Scheduler.Drain();
        Assert.False(view.IsRefreshing);
        Assert.Null(view.RefreshColor);
        Assert.Empty(log);

        host.Render(VStack());
        ((IRefreshView)view).IsRefreshing = true;
        host.Scheduler.Drain();
        Assert.Empty(log);
    }

    [Fact]
    public void RefreshView_MountsRefreshing_WithoutAReport()
    {
        var log = new List<string>();
        var host = Host().Render(RefreshView(Label("x"), true, () => log.Add("refresh")));
        Assert.True(host.View<MauiRefreshView>().IsRefreshing);
        host.Scheduler.Drain();
        Assert.Empty(log);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void RefreshView_Content_IsSetReplacedAndCleared(string registry)
    {
        var host = Host(registry).Render(RefreshView(Label("a"), false, null));
        var view = host.View<MauiRefreshView>();
        var first = view.Content;
        Assert.NotNull(first);
        if (registry == "direct") Assert.IsType<DirectLabel>(Assert.Single((Grid)first!));

        host.Render(RefreshView(Button("b"), false, null));
        Assert.NotSame(first, view.Content);

        host.Render(RefreshView(null, false, null));
        Assert.Null(view.Content);
    }
}
