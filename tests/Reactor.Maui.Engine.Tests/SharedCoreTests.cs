using Microsoft.UI.Reactor.Hosting;
using static Microsoft.UI.Reactor.CoreFactories;

namespace Microsoft.UI.Reactor.Core.Tests;

// ── Behavior that depends on how the engine plugs into the shared core (spec 067 §13) ──

/// <summary>Equal by id only, as an entity often is.</summary>
public sealed class Session(int id, string name)
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public override bool Equals(object? obj) => obj is Session other && other.Id == Id;
    public override int GetHashCode() => Id;
}

public static class SessionContext
{
    public static readonly Context<Session?> Current = new(null);
}

public sealed class SessionReader : Component<Probe>
{
    public override Element Render()
    {
        var (tick, setTick) = UseState(0);
        Props.Setters["tick"] = setTick;
        var session = UseContext(SessionContext.Current);
        return new TextElement($"{session?.Name}:{tick}");
    }
}

public static class HookShape
{
    public static bool Extra;
}

public sealed class ShapeShifter : Component
{
    public override Element Render()
    {
        UseState(0);
        if (HookShape.Extra) UseState(1);
        return new TextElement(HookShape.Extra ? "two hooks" : "one hook");
    }
}

public sealed class Keeper : Component<Probe>
{
    public override Element Render()
    {
        var (value, setValue) = UseState(0);
        Props.Setters["keep"] = setValue;
        return new TextElement($"keep {value}");
    }
}

public sealed record CallbackProps(int Dependency, int Pass, List<Delegate> Seen);

public sealed class CallbackHolder : Component<CallbackProps>
{
    public override Element Render()
    {
        // Capturing, so each render passes a new delegate: a cached one is what the hook returns.
        var pass = Props.Pass;
        Props.Seen.Add(UseCallback<Action<int>>(_ => GC.KeepAlive(pass), Props.Dependency));
        Props.Seen.Add(UseCallback(() => GC.KeepAlive(pass), Props.Dependency));
        return new TextElement("callbacks");
    }
}

public sealed class NullableLabel : Component<string?>
{
    public override Element Render() => new TextElement(Props ?? "null");
}

public class SharedCoreTests
{
    [Fact]
    public void AReaderThatRendersForAnotherReason_SeesTheProvidersCurrentValue_EvenWhenItIsEqualToTheLastOne()
    {
        var probe = new Probe();
        var host = new TestHost();
        Element Tree(Session session) => SessionContext.Current.Provide(session, Component<SessionReader, Probe>(probe));

        host.Render(Tree(new Session(1, "old")));
        // Equal by id, so the reader isn't re-rendered by the change itself.
        host.Render(Tree(new Session(1, "new")));
        Assert.Equal("old:0", host.RootView!.Text);

        probe.Setters["tick"](1);
        host.Scheduler.Drain();

        Assert.Equal("new:1", host.RootView!.Text);
    }

    [Fact]
    public void UseCallback_KeepsItsIdentity_UntilADependencyChanges()
    {
        var seen = new List<Delegate>();
        var host = new TestHost();

        host.Render(Component<CallbackHolder, CallbackProps>(new(1, 0, seen)));
        host.Render(Component<CallbackHolder, CallbackProps>(new(1, 1, seen)));
        host.Render(Component<CallbackHolder, CallbackProps>(new(2, 2, seen)));

        // Any delegate type (Action<int>), and an Action through the core's typed overload.
        Assert.Equal(6, seen.Count);
        Assert.Same(seen[0], seen[2]);
        Assert.Same(seen[1], seen[3]);
        Assert.NotSame(seen[2], seen[4]);
        Assert.NotSame(seen[3], seen[5]);
    }

    [Fact]
    public void UseCallback_OnARenderContext_KeepsItsIdentity_UntilADependencyChanges()
    {
        var seen = new List<Func<int>>();
        var host = new TestHost();
        Element Tree(int dependency) => RenderEachTime(ctx =>
        {
            seen.Add(ctx.UseCallback<Func<int>>(() => dependency, dependency));
            return new TextElement("callbacks");
        });

        host.Render(Tree(1));
        host.Render(Tree(1));
        host.Render(Tree(2));

        Assert.Same(seen[0], seen[1]);
        Assert.NotSame(seen[1], seen[2]);
        Assert.Equal(2, seen[2]());
    }

    [Fact]
    public void PropsThatBecomeNull_AreInstalled()
    {
        var host = new TestHost().Render(Component<NullableLabel, string?>("x"));
        Assert.Equal("x", host.RootView!.Text);

        host.Render(Component<NullableLabel, string?>(null));

        Assert.Equal("null", host.RootView!.Text);
    }

    [Fact]
    public void AFetchThatCompletesAfterItWasSuperseded_IsDroppedOnTheRenderThread()
    {
        var sources = new Dictionary<int, TaskCompletionSource<string>>();
        TaskCompletionSource<string> Start(int id) =>
            sources[id] = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new TestHost();
        Element Tree(int id) => Component<Loader, LoaderProps>(new(Start, id, _ => { }));

        host.Render(Tree(1));
        // Fetch 1 completes on a pool thread; its result waits for the render thread.
        sources[1].SetResult("one");
        for (var i = 0; i < 400 && host.Scheduler.PendingCount == 0; i++) Thread.Sleep(5);
        Assert.True(host.Scheduler.PendingCount > 0);

        // Superseded before the render thread gets to it: cancelled there, so it is dropped.
        host.Render(Tree(2));
        host.Scheduler.Drain();

        Assert.Equal("loading", host.RootView!.Text);
    }
}

[Collection(nameof(HotReloadTests))]
public class SharedCoreHotReloadTests
{
    [Fact]
    public void AHotReloadThatChangesAComponentsHooks_RendersIt_AndKeepsItsSiblingsState()
    {
        HookShape.Extra = false;
        try
        {
            var probe = new Probe();
            var host = new TestHost().Render(new BoxElement(Component<ShapeShifter>(), Component<Keeper, Probe>(probe)));
            probe.Setters["keep"](5);
            host.Scheduler.Drain();

            HookShape.Extra = true;
            ReactorRoot.RerenderAll();
            host.Scheduler.Drain();

            Assert.Equal(["two hooks", "keep 5"], host.RootView!.Children.Select(c => c.Text));
        }
        finally
        {
            HookShape.Extra = false;
        }
    }
}

public sealed class ThrowingCleanup : Component
{
    public override Element Render()
    {
        UseEffect(() => () => throw new InvalidOperationException("cleanup failed"));
        return new TextElement("a");
    }
}

public sealed class CachingParent : Component<Probe>
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        Props.Setters["parent"] = setCount;
        // The same element instance on every render, so the diff would skip it if it weren't dirty.
        var child = UseMemo(() => Component<Counter, CounterProps>(new("cached", Props)), Props);
        return new BoxElement(new TextElement($"parent {count}"), child);
    }
}

public class ChildDiffTests
{
    [Fact]
    public void AnUnchangedChildThatIsDirty_RendersOnce_WhenItsParentRerendersInTheSameBatch()
    {
        var probe = new Probe();
        var host = new TestHost().Render(Component<CachingParent, Probe>(probe));

        probe.Setters["parent"](1);
        probe.Setters["cached"](5);
        host.Scheduler.Drain();

        Assert.Equal(["parent 1", "cached:5"], host.RootView!.Children.Select(c => c.Text));
        Assert.Equal(2, probe.Renders["cached"]);
    }

    [Fact]
    public void TheCleanupFailuresOfSeveralRemovedChildren_AreReportedTogether_AfterTheRestIsInPlace()
    {
        var reported = new List<Exception>();
        var host = new TestHost();
        host.Root.UnhandledException += reported.Add;
        host.Render(new BoxElement(
            Component<ThrowingCleanup>().WithKey("a"), Component<ThrowingCleanup>().WithKey("b"), new TextElement("c").WithKey("c")));

        host.Render(new BoxElement(new TextElement("c").WithKey("c")));

        var aggregate = Assert.IsType<AggregateException>(Assert.Single(reported));
        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.Equal(["c"], host.RootView!.Children.Select(c => c.Text));
    }

    [Fact]
    public void AReorderThatFailsUnderABoundary_UnmountsEveryOldChildOnce()
    {
        var probe = new Probe();
        var host = new TestHost();
        Element Counter(string label) => Component<Counter, CounterProps>(new(label, probe)).WithKey(label);
        host.Render(new BoxElement(ErrorBoundary(new BoxElement(Counter("a"), Counter("b")), _ => new TextElement("fallback"))));

        host.Render(new BoxElement(ErrorBoundary(
            new BoxElement(Counter("b"), Counter("a"), new ExplodingElement("mount failed").WithKey("c")), _ => new TextElement("fallback"))));

        Assert.Equal(1, probe.Log.Count(l => l == "cleanup a"));
        Assert.Equal(1, probe.Log.Count(l => l == "cleanup b"));
        Assert.Equal(["fallback"], host.RootView!.Children.Select(c => c.Text));
        Assert.All(host.Renderer.Mounted.Where(v => v.Kind == "text" && v.Text != "fallback"), v => Assert.True(v.Unmounted));
    }

    // A navigation stack finds its pages and modals by slot position, so a null or empty page must
    // keep its slot even though the diff mounts nothing for it.
    [Fact]
    public void ANullOrEmptyChildOfASlottedHost_KeepsItsSlot()
    {
        var host = new TestHost().Render(new SlotsElement([new TextElement("home"), null], [new TextElement("modal")]));
        Assert.Equal([("home", "first"), ("modal", "second")], host.RootView!.Children.Select(c => (c.Text, c.Slot)));

        host.Render(new SlotsElement([new TextElement("home"), EmptyElement.Instance], [new TextElement("modal")]));
        Assert.Equal([("home", "first"), ("modal", "second")], host.RootView!.Children.Select(c => (c.Text, c.Slot)));
    }

    // The keyed diff unmounts a replaced child before it mounts the replacement; if that mount fails,
    // the unmounted child's cleanup failure is still reported.
    [Fact]
    public void ACleanupThatFails_IsReported_WhenTheReplacementsMountFailsToo()
    {
        var reported = new List<string>();
        var host = new TestHost();
        host.Root.UnhandledException += error => reported.Add(error.Message);
        host.Render(new BoxElement(Component<ThrowingCleanup>().WithKey("a")));

        host.Render(new BoxElement(new ExplodingElement("mount failed").WithKey("b")));

        Assert.Contains("cleanup failed", reported);
        Assert.Contains(reported, message => message.Contains("mount failed"));
    }
}
