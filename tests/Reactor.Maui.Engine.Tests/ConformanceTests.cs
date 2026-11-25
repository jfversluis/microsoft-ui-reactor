using static Microsoft.UI.Reactor.CoreFactories;

namespace Microsoft.UI.Reactor.Core.Tests;

// ── Renderer conformance: identity (spec 067 §11.3, §13.6) ──────────────────
// The same scenarios run on WinUI (RendererConformance.Identity, selftest Conformance_Identity) with the
// same expected texts. Each mounts tallies, sets their state, re-renders a different arrangement
// from the root's own state, and reads which tallies kept theirs.

public sealed record TallyProps(string Label, Probe Probe);

public sealed class Tally : Component<TallyProps>
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        Props.Probe.Setters[Props.Label] = setCount;
        return new TextElement($"{Props.Label}={count}");
    }
}

/// <summary>A different component type with the same props, for the type-change scenario.</summary>
public sealed class OtherTally : Component<TallyProps>
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        Props.Probe.Setters[Props.Label] = setCount;
        return new TextElement($"{Props.Label}={count}");
    }
}

public sealed record ArrangementProps(Func<int, Probe, Element> Arrange, Probe Probe);

public sealed class Arrangement : Component<ArrangementProps>
{
    public override Element Render()
    {
        var (phase, setPhase) = UseState(0);
        Props.Probe.Setters["phase"] = setPhase;
        return Props.Arrange(phase, Props.Probe);
    }
}

public class IdentityConformanceTests
{
    private static Element T(string label, Probe probe) => Component<Tally, TallyProps>(new(label, probe));

    private static string[] Run(Func<int, Probe, Element> arrange, params (string Label, int Value)[] state)
    {
        var probe = new Probe();
        var host = new TestHost().Render(Component<Arrangement, ArrangementProps>(new(arrange, probe)));
        foreach (var (label, value) in state) probe.Setters[label](value);
        host.Scheduler.Drain();
        probe.Setters["phase"](1);
        host.Scheduler.Drain();
        return [.. host.RootView!.Children.Select(c => c.Text!)];
    }

    [Fact]
    public void KeyedSiblingsSwapped_KeepTheirState() =>
        Assert.Equal(["b=2", "a=1"], Run((phase, p) => phase == 0
            ? new BoxElement(T("a", p).WithKey("a"), T("b", p).WithKey("b"))
            : new BoxElement(T("b", p).WithKey("b"), T("a", p).WithKey("a")), ("a", 1), ("b", 2)));

    // WinUI's rule (an unkeyed child among keyed ones is matched by its absolute position) is the
    // contract. Before spec 067 §13.6 MAUI matched it by position among the unkeyed children and
    // kept its state here.
    [Fact]
    public void AnUnkeyedChildMovedPastAKeyedOne_IsANewChild() =>
        Assert.Equal(["k=2", "u=0"], Run((phase, p) => phase == 0
            ? new BoxElement(T("u", p), T("k", p).WithKey("k"))
            : new BoxElement(T("k", p).WithKey("k"), T("u", p)), ("u", 1), ("k", 2)));

    [Fact]
    public void AConditionalChildBeforeAnUnkeyedOne_DoesNotResetIt() =>
        Assert.Equal(["x", "c=3"], Run((phase, p) =>
            new BoxElement(phase == 1 ? new TextElement("x") : null, T("c", p)), ("c", 3)));

    [Fact]
    public void AnUnkeyedChildThatChangesType_IsANewChild() =>
        Assert.Equal(["t=0"], Run((phase, p) => new BoxElement(phase == 0
            ? T("t", p)
            : Component<OtherTally, TallyProps>(new("t", p))), ("t", 4)));

    [Fact]
    public void UnkeyedSiblingsAtTheSamePositions_KeepTheirState_WhenOneIsAppended() =>
        Assert.Equal(["a=1", "b=2", "n=0"], Run((phase, p) => phase == 0
            ? new BoxElement(T("a", p), T("b", p))
            : new BoxElement(T("a", p), T("b", p), T("n", p)), ("a", 1), ("b", 2)));
}

// ── Renderer conformance: effects and error boundaries (spec 067 §13.7) ──────
// Mirrored by RendererConformance.Lifecycle selftest on WinUI. Effect timing relative
// to the commit and to other components is renderer policy (§13.7); within one component, the
// dependency rules are the contract.

public sealed record EffectingProps(int Dependency, List<string> Log);

public sealed class Effecting : Component<EffectingProps>
{
    public override Element Render()
    {
        var dependency = Props.Dependency;
        var log = Props.Log;
        UseEffect(() =>
        {
            log.Add($"run {dependency}");
            return () => log.Add($"cleanup {dependency}");
        }, dependency);
        return new TextElement($"effect {dependency}");
    }
}

public sealed record FailingProps(bool Throw, List<string> Log);

public sealed class FailingChild : Component<FailingProps>
{
    public override Element Render()
    {
        var log = Props.Log;
        UseEffect(() => () => log.Add("failing cleanup"));
        if (Props.Throw) throw new InvalidOperationException("failing child failed");
        return new TextElement("ok");
    }
}

public class LifecycleConformanceTests
{
    private static (List<string> Log, List<string[]> TextsAfterEachPhase) Script(
        Func<int, Probe, List<string>, Element> arrange, int phases, params (int AfterPhase, string Label, int Value)[] state)
    {
        var probe = new Probe();
        var log = new List<string>();
        var host = new TestHost().Render(Component<Arrangement, ArrangementProps>(new((phase, p) => arrange(phase, p, log), probe)));
        var texts = new List<string[]>();
        for (var phase = 0; phase < phases; phase++)
        {
            if (phase > 0) probe.Setters["phase"](phase);
            host.Scheduler.Drain();
            foreach (var (after, label, value) in state)
                if (after == phase) probe.Setters[label](value);
            host.Scheduler.Drain();
            texts.Add([.. host.RootView!.Children.Select(c => c.Text ?? "")]);
        }
        return (log, texts);
    }

    [Fact]
    public void AnEffect_RunsOnce_ThenCleansUpAndRerunsWhenItsDependencyChanges_AndCleansUpOnUnmount()
    {
        var (log, _) = Script((phase, _, log) => new BoxElement(phase switch
        {
            0 or 1 => Component<Effecting, EffectingProps>(new(1, log)),
            2 => Component<Effecting, EffectingProps>(new(2, log)),
            _ => null,
        }), phases: 4);

        Assert.Equal(["run 1", "cleanup 1", "run 2", "cleanup 2"], log);
    }

    [Fact]
    public void ABoundary_ShowsItsFallback_RunsTheFailedSubtreesCleanups_KeepsSiblings_AndRetriesWhenRerendered()
    {
        var (log, texts) = Script((phase, p, log) => new BoxElement(
            ErrorBoundary(Component<FailingChild, FailingProps>(new(phase == 1, log)), _ => new TextElement("fallback")),
            Component<Tally, TallyProps>(new("s", p))), phases: 3, (0, "s", 5));

        Assert.Equal(["ok", "s=5"], texts[0]);
        Assert.Equal(["fallback", "s=5"], texts[1]);
        Assert.Equal(["ok", "s=5"], texts[2]);
        Assert.Equal(["failing cleanup"], log);
    }
}
