using Microsoft.UI.Reactor.Hosting;
using static Microsoft.UI.Reactor.CoreFactories;

namespace Microsoft.UI.Reactor.Core.Tests;

// ── Context ──────────────────────────────────────────────────────────────────

public static class Theme
{
    public static readonly Context<string> Current = new("light");
}

public sealed class ThemeReader : Component<Probe>
{
    public override Element Render()
    {
        var theme = UseContext(Theme.Current);
        Props.CountRender("reader");
        return new TextElement(theme);
    }
}

/// <summary>Never re-renders from its parent, so context changes must reach the reader directly.</summary>
public sealed class Wall : Component
{
    public static Probe? Probe;

    public override Element Render() => new BoxElement(Component<ThemeReader, Probe>(Probe!));
}

public sealed class ThemeRoot : Component<Probe>
{
    public override Element Render()
    {
        var (theme, setTheme) = UseState("dark");
        Props.Setters["theme"] = v => setTheme(v == 0 ? "dark" : "blue");
        return Theme.Current.Provide(theme, Component<Wall>());
    }
}

public class ContextTests
{
    [Fact]
    public void Reader_GetsTheNearestProvidedValue_OrTheDefault()
    {
        var probe = new Probe();
        var host = new TestHost().Render(new BoxElement(
            Component<ThemeReader, Probe>(probe),
            Theme.Current.Provide("dark", Theme.Current.Provide("nested", Component<ThemeReader, Probe>(probe)))));

        Assert.Equal(["light", "nested"], host.RootView!.Children.Select(v => v.Text));
    }

    [Fact]
    public void ProviderChange_RerendersReaders_ThroughAComponentThatSkipsRerendering()
    {
        var probe = new Probe();
        Wall.Probe = probe;
        var host = new TestHost().Render(Component<ThemeRoot, Probe>(probe));
        Assert.Equal("dark", host.RootView!.Children[0].Text);

        probe.Setters["theme"](1);
        host.Scheduler.Drain();

        Assert.Equal("blue", host.RootView.Children[0].Text);
        Assert.Equal(2, probe.Renders["reader"]);
    }
}

// ── Memo ─────────────────────────────────────────────────────────────────────

public class MemoTests
{
    [Fact]
    public void Memo_RerendersOnlyWhenADependencyChanges()
    {
        var renders = 0;
        Element Tree(int dep, string unrelated) => new BoxElement(
            new TextElement(unrelated),
            Memo(_ => { renders++; return new TextElement($"memo {dep}"); }, dep));

        var host = new TestHost().Render(Tree(1, "a"));
        host.Render(Tree(1, "b"));
        Assert.Equal(1, renders);

        host.Render(Tree(2, "b"));
        Assert.Equal(2, renders);
        Assert.Equal("memo 2", host.RootView!.Children[1].Text);
    }
}

// ── Error boundaries and recovery ───────────────────────────────────────────

public sealed record ThrowerProps(Probe Probe, bool ThrowOnRender = false, bool ThrowInEffect = false);

public sealed class Thrower : Component<ThrowerProps>
{
    public override Element Render()
    {
        var (fail, setFail) = UseState(Props.ThrowOnRender);
        Props.Probe.Setters["fail"] = v => setFail(v == 1);
        UseEffect(() =>
        {
            Props.Probe.Log.Add("thrower effect");
            if (Props.ThrowInEffect) throw new InvalidOperationException("effect failed");
            return () => Props.Probe.Log.Add("thrower cleanup");
        });
        if (fail) throw new InvalidOperationException("render failed");
        return new TextElement("ok");
    }
}

public sealed class Sibling : Component<Probe>
{
    public override Element Render()
    {
        var (n, setN) = UseState(0);
        Props.Setters["sibling"] = setN;
        UseEffect(() => () => Props.Log.Add("sibling cleanup"));
        return new TextElement($"sibling {n}");
    }
}

public class ErrorBoundaryTests
{
    private static Element Fallback(Exception ex) => new TextElement($"fallback: {ex.Message}");

    [Fact]
    public void RenderErrorOnMount_ShowsTheFallback_AndUnmountsWhatTheFailedSubtreeMounted()
    {
        var probe = new Probe();
        var host = new TestHost();

        host.Render(ErrorBoundary(new BoxElement(
            new TextElement("before"),
            Component<Thrower, ThrowerProps>(new(probe, ThrowOnRender: true))), Fallback));

        Assert.Equal("fallback: render failed", host.RootView!.Text);
        Assert.True(host.Renderer.Mounted.Single(v => v.Text == "before").Unmounted);
        Assert.True(host.Renderer.Mounted.Single(v => v.Kind == "box").Unmounted);
        Assert.Empty(probe.Log);
    }

    [Fact]
    public void RenderErrorDuringAFlush_ShowsTheFallback_AndOtherDirtyComponentsStillRender()
    {
        var probe = new Probe();
        var host = new TestHost().Render(new BoxElement(
            ErrorBoundary(Component<Thrower, ThrowerProps>(new(probe)), Fallback),
            Component<Sibling, Probe>(probe)));

        probe.Setters["fail"](1);
        probe.Setters["sibling"](5);
        host.Scheduler.Drain();

        Assert.Equal(["fallback: render failed", "sibling 5"], host.RootView!.Children.Select(v => v.Text));
        Assert.Contains("thrower cleanup", probe.Log);
    }

    [Fact]
    public void EffectError_ShowsTheFallback()
    {
        var probe = new Probe();
        var host = new TestHost().Render(new BoxElement(
            ErrorBoundary(Component<Thrower, ThrowerProps>(new(probe, ThrowInEffect: true)), Fallback)));

        Assert.Equal("fallback: effect failed", host.RootView!.Children[0].Text);
    }

    [Fact]
    public void BoundaryRerender_RetriesTheChild()
    {
        var probe = new Probe();
        var host = new TestHost().Render(new BoxElement(ErrorBoundary(new ExplodingElement("boom"), Fallback)));
        Assert.Equal("fallback: boom", host.RootView!.Children[0].Text);

        host.Render(new BoxElement(ErrorBoundary(new TextElement("recovered"), Fallback)));

        Assert.Equal("recovered", host.RootView.Children[0].Text);
    }

    [Fact]
    public void AnErrorAHandlerReports_FromAnotherThread_ShowsTheNearestFallback()
    {
        Action<Exception>? report = null;
        var host = new TestHost().Render(new BoxElement(ErrorBoundary(new ReporterElement(r => report = r), Fallback)));
        Assert.Equal("reporter", host.RootView!.Children[0].Kind);

        var worker = new Thread(() => report!(new InvalidOperationException("navigation failed")));
        worker.Start();
        worker.Join();
        host.Scheduler.Drain();

        Assert.Equal("fallback: navigation failed", host.RootView.Children[0].Text);
    }

    [Fact]
    public void AnErrorAHandlerReports_WithoutABoundary_IsUnhandled()
    {
        Action<Exception>? report = null;
        var host = new TestHost().Render(new BoxElement(new ReporterElement(r => report = r)));
        Exception? reported = null;
        host.Root.UnhandledException += ex => reported = ex;

        report!(new InvalidOperationException("navigation failed"));
        host.Scheduler.Drain();

        Assert.Equal("navigation failed", reported?.Message);
    }

    [Fact]
    public void ThrowingFallback_GoesToTheNextBoundaryUp()
    {
        var host = new TestHost().Render(ErrorBoundary(
            ErrorBoundary(new ExplodingElement("inner"), _ => throw new InvalidOperationException("fallback failed")),
            Fallback));

        Assert.Equal("fallback: fallback failed", host.RootView!.Text);
    }

    [Fact]
    public void UnhandledError_UnmountsTheWholeTree_RunsCleanups_AndIsReported()
    {
        var probe = new Probe();
        var host = new TestHost().Render(new BoxElement(
            Component<Sibling, Probe>(probe),
            Component<Thrower, ThrowerProps>(new(probe))));
        Exception? reported = null;
        host.Root.UnhandledException += ex => reported = ex;

        probe.Setters["fail"](1);
        host.Scheduler.Drain();

        Assert.Equal("render failed", reported?.Message);
        Assert.Null(host.RootView);
        Assert.Contains("sibling cleanup", probe.Log);
        Assert.Contains("thrower cleanup", probe.Log);
        Assert.All(host.Renderer.Mounted, v => Assert.True(v.Unmounted));
    }

    [Fact]
    public void UnhandledError_WithoutAHandler_IsRethrown()
    {
        var host = new TestHost();

        var error = Assert.Throws<InvalidOperationException>(() => host.Render(new BoxElement(new ExplodingElement("boom"))));

        Assert.Equal("boom", error.Message);
        Assert.Null(host.RootView);
    }

    [Fact]
    public void ThrowingCleanup_DoesNotStopTheRestOfTheTeardown()
    {
        var log = new List<string>();
        Element Leaf(string name, bool fail) => RenderEachTime(ctx =>
        {
            ctx.UseEffect(() => () =>
            {
                log.Add(name);
                if (fail) throw new InvalidOperationException($"{name} cleanup failed");
            });
            return new TextElement(name);
        });
        var host = new TestHost().Render(new BoxElement(Leaf("a", fail: true), Leaf("b", fail: false), Leaf("c", fail: true)));
        Exception? reported = null;
        host.Root.UnhandledException += ex => reported = ex;

        host.Root.Unmount();

        Assert.Equal(["a", "b", "c"], log);
        Assert.Equal(2, Assert.IsType<AggregateException>(reported).InnerExceptions.Count);
        Assert.All(host.Renderer.Mounted, v => Assert.True(v.Unmounted));
    }
}

// ── Controlled values, ForEach, resources, hot reload ───────────────────────

public sealed class RejectingInput : Component<List<string>>
{
    public override Element Render() => new InputElement("fixed", Props.Add);
}

public class ControlledRestoreTests
{
    [Fact]
    public void InputTheCallbackDoesNotAccept_IsRevertedAfterTheFlush()
    {
        var reported = new List<string>();
        var host = new TestHost().Render(Component<RejectingInput, List<string>>(reported));
        var input = host.RootView!;

        input.SimulateTyping("typed");
        Assert.Equal("typed", input.Text);
        host.Scheduler.Drain();

        Assert.Equal("fixed", input.Text);
        Assert.Equal(["typed"], reported);
    }
}

public class ForEachTests
{
    [Fact]
    public void ForEach_KeysItems_SoReorderKeepsNativeViews()
    {
        string[] items = ["a", "b", "c"];
        var host = new TestHost().Render(new BoxElement(ForEach(items, i => i, i => new TextElement(i))));
        var before = host.RootView!.Children.ToList();

        host.Render(new BoxElement(ForEach(items.Reverse(), i => i, i => new TextElement(i))));

        Assert.Equal([before[2], before[1], before[0]], host.RootView.Children);
    }
}

public sealed record LoaderProps(Func<int, TaskCompletionSource<string>> Source, int Id, Action<Resource<string>> Report);

public sealed class Loader : Component<LoaderProps>
{
    public override Element Render()
    {
        var resource = UseResource(_ => Props.Source(Props.Id).Task, Props.Id);
        Props.Report(resource);
        return new TextElement(resource.IsLoading ? "loading" : resource.Error?.Message ?? resource.Value!);
    }
}

public class ResourceTests
{
    [Fact]
    public void Resource_LoadsReloadsAndIgnoresSupersededResults()
    {
        var sources = new Dictionary<int, List<TaskCompletionSource<string>>>();
        TaskCompletionSource<string> Start(int id)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            (sources.TryGetValue(id, out var list) ? list : sources[id] = []).Add(tcs);
            return tcs;
        }
        Resource<string>? latest = null;
        var host = new TestHost();
        Element Tree(int id) => Component<Loader, LoaderProps>(new(Start, id, r => latest = r));

        host.Render(Tree(1));
        Assert.Equal("loading", host.RootView!.Text);

        host.Render(Tree(2));
        sources[2][0].SetResult("two");
        Settle(host, () => host.RootView!.Text == "two");
        Assert.Equal("two", host.RootView!.Text);

        // The fetch for id 1 was superseded; its late result must not replace the current one.
        sources[1][0].SetResult("stale");
        Thread.Sleep(50);
        host.Scheduler.Drain();
        Assert.Equal("two", host.RootView!.Text);

        latest!.Reload();
        Settle(host, () => sources[2].Count == 2);
        Assert.Equal("loading", host.RootView!.Text);
        Assert.Equal("two", latest!.Value);

        sources[2][1].SetException(new InvalidOperationException("offline"));
        Settle(host, () => host.RootView!.Text == "offline");
        Assert.Equal("offline", host.RootView!.Text);
    }

    // Completions arrive on thread-pool threads and are posted to the render thread, which is
    // this test's thread; the test must not await, or it could resume on another thread.
    private static void Settle(TestHost host, Func<bool> done)
    {
        for (var i = 0; i < 400 && !done(); i++)
        {
            host.Scheduler.Drain();
            if (done()) return;
            Thread.Sleep(5);
        }
        host.Scheduler.Drain();
    }
}

public sealed class ReloadCounter : Component
{
    public static int Renders;

    public override Element Render()
    {
        Renders++;
        return new TextElement("reload");
    }
}

/// <summary>
/// RerenderAll reaches every live root in the process, so it must not run alongside tests that
/// count renders.
/// </summary>
[CollectionDefinition(nameof(HotReloadTests), DisableParallelization = true)]
public sealed class HotReloadCollection;

[Collection(nameof(HotReloadTests))]
public class HotReloadTests
{
    [Fact]
    public void RerenderAll_RerendersComponentsThatWouldOtherwiseSkip()
    {
        ReloadCounter.Renders = 0;
        var host = new TestHost().Render(Component<ReloadCounter>());

        ReactorRoot.RerenderAll();
        host.Scheduler.Drain();

        Assert.Equal(2, ReloadCounter.Renders);
    }
}

// ── Regressions from the milestone 2 review ─────────────────────────────────

public static class Mode
{
    public static readonly Context<string> Current = new("a");
    public static readonly Context<string> Other = new("other default");
}

/// <summary>Visible only while the mode equals <see cref="Show"/>.</summary>
public sealed class ModeGated : Component<string>
{
    public override Element? Render() => UseContext(Mode.Current) == Props ? new TextElement(Props) : null;
}

public sealed class ModeRoot : Component<(Probe Probe, Func<Element> Body)>
{
    public override Element Render()
    {
        var (mode, setMode) = UseState("a");
        Props.Probe.Setters["mode"] = v => setMode(v == 0 ? "a" : "b");
        return Mode.Current.Provide(mode, Props.Body());
    }
}

/// <summary>Never re-renders from its parent, so context changes reach its subtree only through subscriptions.</summary>
public sealed class Skipper : Component<Func<Element>>
{
    protected override bool ShouldUpdate(Func<Element>? oldProps, Func<Element>? newProps) => false;
    public override Element Render() => Props();
}

public sealed class ThrowsOnB : Component
{
    public override Element Render() =>
        UseContext(Mode.Current) == "b" ? throw new InvalidOperationException("reader failed") : new TextElement("reader ok");
}

public sealed class FailingFallback : Component<Probe>
{
    public override Element Render()
    {
        UseEffect(() => { Props.Log.Add("fallback effect"); throw new InvalidOperationException("fallback effect failed"); });
        return new TextElement("inner fallback");
    }
}

public sealed class Splitter : Component<Probe>
{
    public override Element Render()
    {
        var (split, setSplit) = UseState(false);
        Props.Setters["split"] = v => setSplit(v == 1);
        return split ? Group(new TextElement("one"), new TextElement("two")) : new TextElement("single");
    }
}

public sealed class ContextSwitcher : Component<Probe>
{
    public override Element Render()
    {
        var (useOther, setUseOther) = UseState(false);
        Props.Setters["switch"] = v => setUseOther(v == 1);
        var value = UseContext(useOther ? Mode.Other : Mode.Current);
        Props.CountRender("switcher");
        return new TextElement(value);
    }
}

public sealed class CleanupThrower : Component<Probe>
{
    public override Element Render()
    {
        var (dep, setDep) = UseState(0);
        Props.Setters["dep"] = setDep;
        UseEffect(() => () =>
        {
            Props.Log.Add($"cleanup {dep}");
            throw new InvalidOperationException("cleanup failed");
        }, dep);
        return new TextElement($"dep {dep}");
    }
}

public class ReviewRegressionTests2
{
    private static Element Fallback(Exception ex) => new TextElement($"fallback: {ex.Message}");

    [Fact]
    public void AFallbackWhoseEffectThrows_IsCaughtByTheNextBoundaryUp_NotByItself()
    {
        var probe = new Probe();
        var host = new TestHost().Render(ErrorBoundary(
            ErrorBoundary(new ExplodingElement("inner"), _ => Component<FailingFallback, Probe>(probe)),
            Fallback));

        Assert.Equal("fallback: fallback effect failed", host.RootView!.Text);
        Assert.Equal(["fallback effect"], probe.Log);
    }

    [Fact]
    public void ContextChange_ThroughASkippedComponent_SyncsSingleChildHostsAfterTheWholeBatch()
    {
        var probe = new Probe();
        var host = new TestHost().Render(Component<ModeRoot, (Probe, Func<Element>)>((probe, () =>
            Component<Skipper, Func<Element>>(() => new FrameElement(Group(
                Component<ModeGated, string>("b"),
                Component<ModeGated, string>("a")))))));
        Assert.Equal("a", Assert.Single(host.RootView!.Children).Text);

        probe.Setters["mode"](1);
        host.Scheduler.Drain();

        Assert.Equal("b", Assert.Single(host.RootView.Children).Text);
    }

    [Fact]
    public void AContextReaderThatThrows_IsCaughtByTheBoundaryAboveIt_EvenBelowASkippedComponent()
    {
        var probe = new Probe();
        var host = new TestHost().Render(Component<ModeRoot, (Probe, Func<Element>)>((probe, () =>
            new BoxElement(
                new TextElement("outside"),
                Component<Skipper, Func<Element>>(() => ErrorBoundary(Component<ThrowsOnB>(), Fallback))))));
        Exception? unhandled = null;
        host.Root.UnhandledException += ex => unhandled = ex;

        probe.Setters["mode"](1);
        host.Scheduler.Drain();

        Assert.Null(unhandled);
        Assert.Equal(["outside", "fallback: reader failed"], host.RootView!.Children.Select(v => v.Text));
    }

    [Fact]
    public void AHostThatRejectsItsChildren_IsTreatedLikeARenderError()
    {
        var probe = new Probe();
        var host = new TestHost().Render(new BoxElement(ErrorBoundary(new FrameElement(Component<Splitter, Probe>(probe)), Fallback)));
        Exception? unhandled = null;
        host.Root.UnhandledException += ex => unhandled = ex;

        probe.Setters["split"](1);
        host.Scheduler.Drain();

        Assert.Null(unhandled);
        Assert.StartsWith("fallback: FrameElement hosts a single child", host.RootView!.Children[0].Text);
    }

    [Fact]
    public void ACleanupThatThrows_RunsOnlyOnce()
    {
        var probe = new Probe();
        var host = new TestHost().Render(Component<CleanupThrower, Probe>(probe));
        var reported = new List<Exception>();
        host.Root.UnhandledException += reported.Add;

        // The cleanup throws while the effect re-runs; with no boundary that unmounts the tree,
        // which must not run the same cleanup a second time.
        probe.Setters["dep"](1);
        host.Scheduler.Drain();
        host.Root.Unmount();

        Assert.Equal(["cleanup 0"], probe.Log);
        Assert.Equal("cleanup failed", Assert.Single(reported).Message);
    }

    [Fact]
    public void AComponentThatStopsReadingAContext_IsNoLongerRerenderedByIt()
    {
        var probe = new Probe();
        var host = new TestHost().Render(Component<ModeRoot, (Probe, Func<Element>)>((probe, () =>
            Component<Skipper, Func<Element>>(() => Component<ContextSwitcher, Probe>(probe)))));

        probe.Setters["switch"](1);
        host.Scheduler.Drain();
        Assert.Equal("other default", host.RootView!.Text);
        var renders = probe.Renders["switcher"];

        probe.Setters["mode"](1);
        host.Scheduler.Drain();

        Assert.Equal(renders, probe.Renders["switcher"]);
    }
}
