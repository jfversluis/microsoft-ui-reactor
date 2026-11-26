using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;
using Microsoft.UI.Reactor.Maui.Hosting;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiEntry = Microsoft.Maui.Controls.Entry;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiWebView = Microsoft.Maui.Controls.WebView;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>A handler that records commands; enough for headless code that needs a connected view.</summary>
internal sealed class StubViewHandler : IViewHandler
{
    public object? PlatformView { get; set; } = new object();
    public List<(string Command, object? Args)> Commands { get; } = [];
    public bool FocusResult { get; set; } = true;
    public IView? VirtualView { get; private set; }
    IElement? IElementHandler.VirtualView => VirtualView;
    public IMauiContext? MauiContext { get; private set; }
    public bool HasContainer { get; set; }
    public object? ContainerView => null;

    public void SetMauiContext(IMauiContext mauiContext) => MauiContext = mauiContext;
    public void SetVirtualView(IElement view) => VirtualView = (IView)view;
    public Action<string>? OnUpdateValue { get; set; }
    public void UpdateValue(string property) => OnUpdateValue?.Invoke(property);
    public Action<string>? OnCommand { get; set; }

    public void Invoke(string command, object? args = null)
    {
        Commands.Add((command, args));
        OnCommand?.Invoke(command);
        if (args is FocusRequest request) request.TrySetResult(FocusResult);
    }

    public void DisconnectHandler() => VirtualView = null;
    public Size GetDesiredSize(double widthConstraint, double heightConstraint) => Size.Zero;
    public void PlatformArrange(Rect frame) { }
}

public sealed record ThrowingProps(bool ThrowOnUpdate, bool ThrowOnSync);

/// <summary>A view element whose handler can fail an update or a child synchronization.</summary>
public sealed record FlakyElement(ThrowingProps Fail, params Element?[] Children) : ViewElement;

internal sealed class FlakyHandler : Microsoft.UI.Reactor.Hosting.IElementHandler<FlakyElement, Grid>
{
    public Grid Mount(HandlerContext<FlakyElement> context, FlakyElement element) => [];

    public void Update(HandlerContext<FlakyElement> context, FlakyElement oldElement, FlakyElement newElement, Grid control)
    {
        if (newElement.Fail.ThrowOnUpdate) throw new InvalidOperationException("update failed");
    }

    public IChildrenStrategy<FlakyElement, Grid>? Children { get; } = new FlakyChildren();

    private sealed class FlakyChildren : IChildrenStrategy<FlakyElement, Grid>
    {
        public IReadOnlyList<Element?> GetChildren(FlakyElement element) => element.Children;

        public void Sync(Grid control, IReadOnlyList<object> natives)
        {
            if (FailSync) throw new InvalidOperationException("sync failed");
            control.Clear();
            foreach (var native in natives) control.Add(Props.AsView(native));
        }
    }

    // Set by the test around the render that should fail; Sync has no element.
    [ThreadStatic] internal static bool FailSync;
}

public class ElementRefTests
{
    private static TestHost Controls() => new(MauiHandlers.CreateRegistry());
    private static TestHost Direct() => new(DirectHandlers.CreateRegistry());

    public static TheoryData<string> Registries => ["controls", "direct"];

    private static TestHost Host(string registry) => registry == "direct" ? Direct() : Controls();

    [Theory]
    [MemberData(nameof(Registries))]
    public void Mount_SetsCurrentToTheView_AndUnmountClearsIt(string registry)
    {
        var label = TypedElementRef.Create<IView>();
        var host = Host(registry).Render(VStack(Label("a").Ref(label), Label("b")));
        var stack = (IList<IView>)host.RootView!;
        Assert.Same(stack[0], label.Current);

        host.Render(VStack(Label("b")));

        Assert.Null(label.Current);
    }

    private sealed class EffectProbe : Component<List<IView?>>
    {
        public override Element Render()
        {
            var entry = UseElementRef<IView>();
            UseEffect(() => Props.Add(entry.Current), Array.Empty<object>());
            return Entry("x").Ref(entry);
        }
    }

    [Fact]
    public void Current_IsSetBeforeTheMountEffectsRun_AndTheHookReturnsTheSameRef()
    {
        var seen = new List<IView?>();
        var host = Controls().Render(Component<EffectProbe, List<IView?>>(seen));
        host.Scheduler.Drain();

        Assert.Same(host.RootView, Assert.Single(seen));
    }

    private sealed class StableProbe : Component<List<ElementRef<IView>>>
    {
        public override Element Render()
        {
            var r = UseElementRef<IView>();
            Props.Add(r);
            var (n, setN) = UseState(0);
            return Button($"{n}", () => setN(n + 1)).Ref(r);
        }
    }

    [Fact]
    public void UseElementRef_ReturnsTheSameRefEveryRender()
    {
        var refs = new List<ElementRef<IView>>();
        var host = Controls().Render(Component<StableProbe, List<ElementRef<IView>>>(refs));
        ((Button)host.RootView!).SendClicked();
        host.Scheduler.Drain();

        Assert.Equal(2, refs.Count);
        Assert.Same(refs[0], refs[1]);
        Assert.Same(host.RootView, refs[1].Current);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void ARefMovedBetweenSiblings_NamesTheNewElement_InEitherOrder(string registry)
    {
        var moved = TypedElementRef.Create<IView>();
        var host = Host(registry).Render(VStack(Label("a").Ref(moved), Label("b")));
        var stack = (IList<IView>)host.RootView!;

        host.Render(VStack(Label("a"), Label("b").Ref(moved)));
        Assert.Same(stack[1], moved.Current);

        // The earlier sibling binds first, then the later one, which no longer declares it, releases.
        host.Render(VStack(Label("a").Ref(moved), Label("b")));
        Assert.Same(stack[0], moved.Current);
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void ARefRemovedOrSwapped_OnAMountedElement_IsReleased(string registry)
    {
        var first = TypedElementRef.Create<IView>();
        var second = TypedElementRef.Create<IView>();
        var host = Host(registry).Render(VStack(Label("a").Ref(first)));
        var label = ((IList<IView>)host.RootView!)[0];

        host.Render(VStack(Label("a").Ref(second)));
        Assert.Null(first.Current);
        Assert.Same(label, second.Current);

        host.Render(VStack(Label("a")));
        Assert.Null(second.Current);
    }

    [Fact]
    public void AKeyedReorder_KeepsTheView()
    {
        var b = TypedElementRef.Create<IView>();
        var host = Controls().Render(VStack(Label("a").WithKey("a"), Label("b").Ref(b).WithKey("b")));
        var view = b.Current;

        host.Render(VStack(Label("b").Ref(b).WithKey("b"), Label("a").WithKey("a")));

        Assert.NotNull(view);
        Assert.Same(view, b.Current);
        Assert.Same(view, ((IList<IView>)host.RootView!)[0]);
    }

    [Fact]
    public void TheRefsType_PicksTheRenderer_AndSwitchingItRemounts_AsAChild()
    {
        var any = TypedElementRef.Create<IView>();
        var entry = TypedElementRef.Create<MauiEntry>();
        var host = Direct().Render(VStack(Entry("x").Ref(any)));
        Assert.IsType<DirectEntry>(any.Current);

        host.Render(VStack(Entry("x").Ref(entry)));
        Assert.Null(any.Current);
        Assert.IsType<MauiEntry>(entry.Current);
        Assert.Same(((IList<IView>)host.RootView!)[0], entry.Current);

        host.Render(VStack(Entry("x").Ref(any)));
        Assert.Null(entry.Current);
        Assert.IsType<DirectEntry>(any.Current);
    }

    [Fact]
    public void TheRefsType_PicksTheRenderer_AndSwitchingItRemounts_AtTheRoot()
    {
        var any = TypedElementRef.Create<IView>();
        var entry = TypedElementRef.Create<MauiEntry>();
        var host = Direct().Render(Entry("x").Ref(any));
        Assert.IsType<DirectEntry>(any.Current);

        host.Render(Entry("x").Ref(entry));
        Assert.Null(any.Current);
        Assert.Same(host.RootView, entry.Current);

        host.Render(Entry("x").Ref(any));
        Assert.Null(entry.Current);
        Assert.Same(host.RootView, any.Current);
    }

    [Fact]
    public void ARefOfTheWrongType_IsAnErrorNamingBothTypes_AndBindsNothing()
    {
        var entry = TypedElementRef.Create<MauiEntry>();
        string? caught = null;
        Controls().Render(VStack(ErrorBoundary(Label("a").Ref(entry), e => Label(caught = e.Message))));

        Assert.NotNull(caught);
        Assert.Contains("ElementRef<Entry>", caught);
        Assert.Contains("LabelElement", caught);
        Assert.Null(entry.Current);
    }

    private static TestHost FlakyHost()
    {
        var registry = MauiHandlers.CreateRegistry();
        registry.Register(new FlakyHandler());
        return new TestHost(registry);
    }

    private static readonly ThrowingProps Fine = new(false, false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUpdateThatThrows_DuringARefRemovalOrSwap_ReleasesTheOldRef(bool swap)
    {
        var old = TypedElementRef.Create<IView>();
        var replacement = TypedElementRef.Create<IView>();
        Element Tree(FlakyElement flaky) => VStack(ErrorBoundary(flaky, _ => Label("fallback")));
        var host = FlakyHost().Render(Tree(new FlakyElement(Fine).Ref(old)));
        Assert.NotNull(old.Current);

        var failing = new FlakyElement(new(true, false));
        host.Render(Tree(swap ? failing.Ref(replacement) : failing));

        Assert.IsType<MauiLabel>(((IList<IView>)host.RootView!)[0]);
        Assert.Null(old.Current);
        Assert.Null(replacement.Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AChildSyncThatThrows_DuringARefRemovalOrSwap_ReleasesTheOldRef(bool swap)
    {
        var old = TypedElementRef.Create<IView>();
        var replacement = TypedElementRef.Create<IView>();
        Element Tree(FlakyElement flaky) => VStack(ErrorBoundary(flaky, _ => Label("fallback")));
        var host = FlakyHost().Render(Tree(new FlakyElement(Fine, Label("a")).Ref(old)));
        Assert.NotNull(old.Current);

        var failing = new FlakyElement(Fine, Label("b"));
        FlakyHandler.FailSync = true;
        try
        {
            host.Render(Tree(swap ? failing.Ref(replacement) : failing));
        }
        finally
        {
            FlakyHandler.FailSync = false;
        }

        Assert.IsType<MauiLabel>(((IList<IView>)host.RootView!)[0]);
        Assert.Null(old.Current);
        Assert.Null(replacement.Current);
    }

    [Fact]
    public void AnUpdateThatThrows_AtTheRoot_ReleasesTheRefWhenTheRootFails()
    {
        var old = TypedElementRef.Create<IView>();
        var host = FlakyHost().Render(new FlakyElement(Fine).Ref(old));
        var failures = new List<Exception>();
        host.Root.UnhandledException += failures.Add;

        host.Render(new FlakyElement(new(true, false)));

        Assert.Single(failures);
        Assert.Null(old.Current);
    }

    [Fact]
    public void ABoundarysFallback_ReleasesTheRefsOfTheSubtreeItReplaces()
    {
        var released = new List<object>();
        var registry = MauiHandlers.CreateRegistry();
        registry.ReleaseNative = released.Add;
        var entry = TypedElementRef.Create<IView>();
        var fail = false;
        Element Tree() => VStack(ErrorBoundary(Component<RefThenMaybeFail, RefProps>(new(entry, fail)), _ => Label("fallback")));
        var host = new TestHost(registry).Render(Tree());
        var mounted = entry.Current;
        Assert.NotNull(mounted);

        fail = true;
        host.Render(Tree());

        Assert.Equal([mounted], released);
        Assert.Null(entry.Current);
    }

    public sealed record RefProps(ElementRef<IView> Ref, bool Fail);

    private sealed class RefThenMaybeFail : Component<RefProps>
    {
        public override Element Render() =>
            Props.Fail ? throw new InvalidOperationException("render failed") : Entry("x").Ref(Props.Ref);
    }

    [Fact]
    public void ReactorPageUnmount_RunsTheCleanups_AndReleasesTheRefs()
    {
        var cleanups = new List<string>();
        var entry = TypedElementRef.Create<IView>();
        var page = new ReactorPage(Component<CleanupProbe, CleanupProps>(new(entry, cleanups)));
        Assert.NotNull(entry.Current);

        page.Unmount();

        Assert.Equal(["cleanup"], cleanups);
        Assert.Null(entry.Current);
    }

    public sealed record CleanupProps(ElementRef<IView> Ref, List<string> Cleanups);

    private sealed class CleanupProbe : Component<CleanupProps>
    {
        public override Element Render()
        {
            UseEffect(() => () => Props.Cleanups.Add("cleanup"), Array.Empty<object>());
            return Entry("x").Ref(Props.Ref);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ADirectViewFocuses_ThroughItsHandlersFocusCommand(bool result)
    {
        var host = Direct().Render(Entry("x"));
        var entry = (DirectEntry)host.RootView!;
        var handler = new StubViewHandler { FocusResult = result };
        Assert.False(entry.Focus(), "no handler, no focus");

        entry.Handler = handler;

        Assert.Equal(result, entry.Focus());
        entry.Unfocus();
        Assert.Equal([nameof(IView.Focus), nameof(IView.Unfocus)], handler.Commands.Select(c => c.Command));
    }

    [Fact]
    public void ARefMadeOutsideRender_BindsAndReleases()
    {
        var made = TypedElementRef.Create<MauiLabel>();
        Assert.Null(made.Current);
        var host = Controls().Render(Label("a").Ref(made));
        Assert.Same(host.RootView, made.Current);
        host.Root.Unmount();
        Assert.Null(made.Current);
    }
}

public class ItemRefTests
{
    public sealed record RefItem(string Id);

    public static readonly Dictionary<string, ElementRef<IView>> Refs = [];
    public static readonly List<string> Unmounted = [];

    private sealed class RefRow : Component<RefItem>
    {
        public override Element Render()
        {
            var r = UseElementRef<IView>();
            Refs[Props.Id] = r;
            UseEffect(() => () => Unmounted.Add(Props.Id), Array.Empty<object>());
            return Label(Props.Id).Ref(r);
        }
    }

    private static (TestHost Host, CollectionView View) Mount(bool reuse, params RefItem[] items)
    {
        Refs.Clear();
        Unmounted.Clear();
        var host = new TestHost(MauiHandlers.CreateRegistry())
            .Render(CollectionView(items, i => i.Id, i => Component<RefRow, RefItem>(i)) with { ReuseItemTrees = reuse });
        return (host, host.View<CollectionView>());
    }

    private static ContentView Realize(CollectionView view, int index)
    {
        var cell = (ContentView)view.ItemTemplate.CreateContent();
        cell.BindingContext = ((System.Collections.IList)view.ItemsSource)[index];
        return cell;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACellWhoseItemIsCleared_IsRetired_AndANewItemMountsAFreshTree(bool reuse)
    {
        var (host, view) = Mount(reuse, new("a"), new("b"));
        var cell = Realize(view, 0);
        host.Scheduler.Drain();
        var a = Refs["a"];
        Assert.Same(cell.Content, a.Current);

        cell.BindingContext = null;
        host.Scheduler.Drain();

        Assert.Equal(["a"], Unmounted);
        Assert.Null(a.Current);
        Assert.Null(cell.Content);

        cell.BindingContext = ((System.Collections.IList)view.ItemsSource)[1];
        host.Scheduler.Drain();
        Assert.NotSame(a, Refs["b"]);
        Assert.Same(cell.Content, Refs["b"].Current);
    }

    [Fact]
    public void ADirectRebind_KeepsTheTree_OnlyWithReuseItemTrees()
    {
        var (host, view) = Mount(true, new("a"), new("b"));
        var cell = Realize(view, 0);
        host.Scheduler.Drain();
        var a = Refs["a"];

        cell.BindingContext = ((System.Collections.IList)view.ItemsSource)[1];
        host.Scheduler.Drain();

        Assert.Empty(Unmounted);
        Assert.Same(a, Refs["b"]);
        Assert.Same(cell.Content, a.Current);
    }

    [Fact]
    public void ARemovedItemsCell_IsRetiredWhenTheListUnbindsIt()
    {
        var (host, view) = Mount(false, new("a"), new("b"));
        var cell = Realize(view, 0);
        host.Scheduler.Drain();
        var a = Refs["a"];
        Assert.NotNull(a.Current);

        host.Render(CollectionView(new RefItem[] { new("b") }, i => i.Id, i => Component<RefRow, RefItem>(i)));
        // What the platform does with a removed item's cell (iOS's TemplatedCell2.Unbind).
        cell.BindingContext = null;
        host.Scheduler.Drain();

        Assert.Equal(["a"], Unmounted);
        Assert.Null(a.Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheListsUnmount_RetiresEveryCell(bool reuse)
    {
        var (host, view) = Mount(reuse, new("a"), new("b"));
        Realize(view, 0);
        Realize(view, 1);
        host.Scheduler.Drain();
        var (a, b) = (Refs["a"], Refs["b"]);

        host.Render(Label("gone"));
        host.Scheduler.Drain();

        Assert.Equal(2, Unmounted.Count);
        Assert.Null(a.Current);
        Assert.Null(b.Current);
    }
}

/// <summary>RerenderAll reaches every live root in the process, so it must not run alongside other tests.</summary>
[Collection(nameof(NavigationHotReloadTests))]
public class ElementRefHotReloadTests
{
    public static bool UseEntryRef;
    public static readonly List<object> Seen = [];

    private sealed class RetypeProbe : Component
    {
        public static Action<int>? SetCount;

        public override Element Render()
        {
            var (count, setCount) = UseState(0);
            SetCount = setCount;
            object r = UseEntryRef ? UseElementRef<MauiEntry>() : UseElementRef<IView>();
            Seen.Add(r);
            var entry = Entry($"{count}");
            return VStack(
                r is ElementRef<MauiEntry> typed ? entry.Ref(typed) : entry.Ref((ElementRef<IView>)r),
                Component<CounterChild>());
        }
    }

    private sealed class CounterChild : Component
    {
        public static Action<int>? Set;

        public override Element Render()
        {
            var (n, setN) = UseState(0);
            Set = setN;
            return Label($"child {n}");
        }
    }

    [Fact]
    public void AnEditThatKeepsTheHooks_KeepsTheRef_AndOneThatRetypesIt_ReplacesIt()
    {
        UseEntryRef = false;
        Seen.Clear();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Component<RetypeProbe>());
        host.Scheduler.Drain();
        CounterChild.Set!(7);
        RetypeProbe.SetCount!(5);
        host.Scheduler.Drain();
        var stack = (IList<IView>)host.RootView!;
        var first = (ElementRef<IView>)Seen[^1];
        Assert.Same(stack[0], first.Current);

        ReactorRoot.RerenderAll();
        host.Scheduler.Drain();
        Assert.Same(first, Seen[^1]);
        Assert.Same(stack[0], first.Current);
        Assert.Equal("5", ((MauiEntry)stack[0]).Text);

        UseEntryRef = true;
        ReactorRoot.RerenderAll();
        host.Scheduler.Drain();

        var second = Assert.IsType<ElementRef<MauiEntry>>(Seen[^1]);
        Assert.Null(first.Current);
        Assert.Same(stack[0], second.Current);
        Assert.Equal("0", ((MauiEntry)stack[0]).Text);
        Assert.Equal("child 7", ((MauiLabel)stack[1]).Text);
        UseEntryRef = false;
    }
}

// The README's refs and web view snippets, compiled and rendered.
public sealed class ReadmeSearch : Component
{
    public override Element Render()
    {
        var (query, setQuery) = UseState("");
        var input = UseElementRef<MauiEntry>();
        var results = UseElementRef<ScrollView>();
        var first = UseElementRef<IView>();

        return VStack(8,
            Entry(query, setQuery, placeholder: "Search").Ref(input),
            HStack(8,
                Button("Clear", () => { setQuery(""); input.Current?.Focus(); }),
                Button("Top", async () =>
                {
                    if (results.Current is { } scroll) await scroll.ScrollToAsync(first, ScrollToPosition.Start, animated: true);
                })),
            ScrollView(VStack(Label("Results").Ref(first), Label($"Matches for '{query}'"))).Ref(results).Height(240));
    }
}

public sealed class ReadmeWeb : Component
{
    static bool IsDocs(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.IdnHost, "learn.microsoft.com", StringComparison.OrdinalIgnoreCase);

    public override Element Render()
    {
        var (requested, setRequested) = UseState("https://learn.microsoft.com/dotnet/maui");
        var (current, setCurrent) = UseState<string?>(null);   // what the view shows; never fed back into Url
        var web = UseElementRef<WebView>();
        return VStack(
            Button("Back", () => web.Current?.GoBack()),
            (WebView(requested) with
            {
                OnNavigating = e => e.Cancel = !IsDocs(e.Url),
                OnNavigated = e => { if (e.Result == WebNavigationResult.Success) setCurrent(e.Url); },
            }).Ref(web).Height(400));
    }
}

public class ReadmeRefSnippetTests
{
    [Fact]
    public void TheSearchSnippet_BindsItsRefs_AndItsButtonsRun()
    {
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Component<ReadmeSearch>());
        var stack = (IList<IView>)host.RootView!;
        var entry = (MauiEntry)stack[0];
        var entryHandler = new StubViewHandler();
        entry.Handler = entryHandler;
        ((ITextInput)entry).Text = "abc";
        host.Scheduler.Drain();
        var buttons = (IList<IView>)stack[1];
        // Lay the scroll view and its first label out, as a layout pass would.
        var scroll = (ScrollView)stack[2];
        var scrollHandler = new StubViewHandler();
        scroll.Handler = scrollHandler;
        ((IView)scroll).Arrange(new Rect(0, 0, 300, 240));
        var content = (VerticalStackLayout)scroll.Content!;
        content.Handler = new StubViewHandler();
        ((IView)content).Arrange(new Rect(0, 0, 300, 600));
        var first = (Label)content[0];
        first.Handler = new StubViewHandler();
        ((IView)first).Arrange(new Rect(0, 0, 300, 20));

        ((Button)buttons[0]).SendClicked();
        ((Button)buttons[1]).SendClicked();
        host.Scheduler.Drain();

        Assert.Equal("", entry.Text);
        Assert.Contains(entryHandler.Commands, c => c.Command == nameof(IView.Focus));
        Assert.Contains(scrollHandler.Commands, c => c.Command == nameof(IScrollView.RequestScrollTo));
    }

    [Fact]
    public void TheWebSnippet_RefusesOtherSites_AndKeepsTheRequestedUrl()
    {
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Component<ReadmeWeb>());
        var web = (IWebView)((IList<IView>)host.RootView!)[1];

        Assert.True(web.Navigating(WebNavigationEvent.NewPage, "https://example.com/"));
        Assert.True(web.Navigating(WebNavigationEvent.NewPage, "https://learn.microsoft.com.example.com/"));
        Assert.True(web.Navigating(WebNavigationEvent.NewPage, "http://learn.microsoft.com/"));
        Assert.False(web.Navigating(WebNavigationEvent.NewPage, "HTTPS://LEARN.MICROSOFT.COM/dotnet/"));
        Assert.False(web.Navigating(WebNavigationEvent.NewPage, "https://learn.microsoft.com/dotnet/"));
        web.Navigated(WebNavigationEvent.NewPage, "https://learn.microsoft.com/dotnet/", WebNavigationResult.Success);
        host.Scheduler.Drain();

        Assert.Equal("https://learn.microsoft.com/dotnet/maui", ((UrlWebViewSource)((MauiWebView)web).Source).Url);
    }
}

public class ElementFocusTests
{
    public sealed record FocusProps(List<(ElementRef<IView> Ref, Action Request)> Seen, bool Second);

    private sealed class FocusProbe : Component<FocusProps>
    {
        public override Element Render()
        {
            var focus = UseElementFocus();
            Props.Seen.Add(focus);
            return VStack(Entry("a").Ref(Props.Second ? TypedElementRef.Create<IView>() : focus.Ref), Entry("b").Ref(Props.Second ? focus.Ref : TypedElementRef.Create<IView>()));
        }
    }

    private static (TestHost Host, List<(ElementRef<IView> Ref, Action Request)> Seen, DirectEntry First, DirectEntry Second) Mount()
    {
        var seen = new List<(ElementRef<IView>, Action)>();
        var host = new TestHost(DirectHandlers.CreateRegistry()).Render(Component<FocusProbe, FocusProps>(new(seen, false)));
        var stack = (IList<IView>)host.RootView!;
        return (host, seen, (DirectEntry)stack[0], (DirectEntry)stack[1]);
    }

    private static bool Focused(StubViewHandler handler) => handler.Commands.Any(c => c.Command == nameof(IView.Focus));

    [Fact]
    public void ARequestBeforeTheView_IsReady_FocusesItWhenItIs_AndKeepsItsIdentity()
    {
        var (host, seen, first, _) = Mount();
        seen[0].Request();
        var handler = new StubViewHandler();
        first.Handler = handler;
        Assert.False(Focused(handler), "the readiness check runs on the next turn");
        host.Scheduler.Drain();
        Assert.True(Focused(handler));

        host.Render(Component<FocusProbe, FocusProps>(new(seen, false)));
        Assert.Same(seen[0].Request, seen[^1].Request);
    }

    [Fact]
    public void ANewerRequest_ReplacesAPendingOne()
    {
        var (host, seen, first, _) = Mount();
        seen[0].Request();
        seen[0].Request();
        var handler = new StubViewHandler();
        first.Handler = handler;
        host.Scheduler.Drain();
        Assert.Equal(1, handler.Commands.Count(c => c.Command == nameof(IView.Focus)));
    }

    [Fact]
    public void ARequest_IsDroppedWhenTheElementUnmounts()
    {
        var (host, seen, first, _) = Mount();
        seen[0].Request();
        host.Render(Label("gone"));
        var handler = new StubViewHandler();
        first.Handler = handler;
        host.Scheduler.Drain();
        Assert.False(Focused(handler));
    }

    [Fact]
    public void ARequest_IsDroppedWhenTheRefMovesToAnotherView()
    {
        var (host, seen, first, second) = Mount();
        seen[0].Request();
        host.Render(Component<FocusProbe, FocusProps>(new(seen, true)));
        Assert.Same(second, seen[0].Ref.Current);
        var handler = new StubViewHandler();
        first.Handler = handler;
        host.Scheduler.Drain();
        Assert.False(Focused(handler));
    }

    [Fact]
    public void ARequestFromAnotherThread_RunsOnTheRendersThread()
    {
        var (host, seen, first, _) = Mount();
        var handler = new StubViewHandler();
        first.Handler = handler;
        host.Scheduler.Drain();

        // A dedicated thread: a pool thread could be the one the test itself ran on.
        var other = new Thread(() => seen[0].Request());
        other.Start();
        other.Join();
        Assert.False(Focused(handler), "queued on the root's scheduler, not run on the other thread");

        host.Scheduler.Drain();
        Assert.True(Focused(handler));
    }
}

public class DirectSelectionBaselineTests
{
    [Fact]
    public void ADirectEntrysBaseline_IsObservedOnceItsPlatformViewExists()
    {
        var ready = false;
        var platform = new FakeSelection { Text = "hello" };
        PlatformSelection.TestReader.Value = _ => ready ? new PlatformSelectionState(platform.Cursor, platform.Length, platform.Text) : null;
        try
        {
            var reports = new List<string>();
            var host = new TestHost(DirectHandlers.CreateRegistry()).Render(Entry("hello", _ => { }) with
            {
                OnSelectionChanged = (_, c, l) => reports.Add($"{c},{l}"),
            });
            var entry = (DirectEntry)host.RootView!;
            host.Scheduler.Drain();

            // MAUI sets a Core view's handler before it creates and maps the platform view.
            entry.Handler = new StubViewHandler();
            ready = true;
            host.Scheduler.Drain();

            platform.Set(2, 1);
            entry.CursorPosition = 2;
            entry.SelectionLength = 1;
            host.Scheduler.Drain();

            Assert.Equal(["2,1"], reports);
        }
        finally
        {
            PlatformSelection.TestReader.Value = null;
        }
    }
}

// The README's UseElementFocus snippet.
public sealed class ReadmeFocus : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("");
        var (field, requestFocus) = UseElementFocus();
        UseEffect(requestFocus, Array.Empty<object>());
        return Entry(name, setName).Ref(field);
    }
}

public class ReadmeFocusSnippetTests
{
    [Fact]
    public void TheFocusSnippet_RequestsFocusFromItsFirstEffect_OnceTheViewIsReady()
    {
        var host = new TestHost(DirectHandlers.CreateRegistry());
        host.Render(Component<ReadmeFocus>());
        var entry = (DirectEntry)host.RootView!;
        var handler = new StubViewHandler();
        entry.Handler = handler;
        host.Scheduler.Drain();

        Assert.Contains(handler.Commands, c => c.Command == nameof(IView.Focus));
    }
}
