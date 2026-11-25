// Renderer conformance scenarios (spec 067 §13.6–13.7): one table, compiled into both renderers'
// suites (tests/Reactor.Maui.Engine.Tests and the WinUI selftest hosts), so "same scenarios, same
// expected results" holds by construction. Each project supplies ConformanceDsl (Box, Text, Keyed,
// Comp, Boundary) for its own elements and a runner that drives its own host.
using Microsoft.UI.Reactor.Core;
using static RendererConformanceKit.ConformanceDsl;

namespace RendererConformanceKit;

public sealed class Probe
{
    public Dictionary<string, Action<int>> Setters { get; } = [];
}

public sealed record TallyProps(string Label, Probe Probe);

/// <summary>Shows "label=count"; its setter is published under its label.</summary>
public sealed class Tally : Component<TallyProps>
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        Props.Probe.Setters[Props.Label] = setCount;
        return Text($"{Props.Label}={count}");
    }
}

/// <summary>The same as <see cref="Tally"/>, as a different component type.</summary>
public sealed class OtherTally : Component<TallyProps>
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        Props.Probe.Setters[Props.Label] = setCount;
        return Text($"{Props.Label}={count}");
    }
}

public sealed record ArrangementProps(Func<int, Probe, Element> Arrange, Probe Probe);

/// <summary>The root: renders the arrangement for its own phase, whose setter is "phase".</summary>
public sealed class Arrangement : Component<ArrangementProps>
{
    public override Element Render()
    {
        var (phase, setPhase) = UseState(0);
        Props.Probe.Setters["phase"] = setPhase;
        return Props.Arrange(phase, Props.Probe);
    }
}

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
        return Text($"effect {dependency}");
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
        return Text("ok");
    }
}

/// <summary>
/// Mounts phase 0, sets <see cref="State"/>, checks <see cref="Before"/> (so a setter that never
/// landed can't pass an "is a new child" scenario), moves to phase 1 and checks <see cref="After"/>.
/// </summary>
public sealed record IdentityScenario(
    string Name, Func<int, Probe, Element> Arrange, (string Label, int Value)[] State, string[] Before, string[] After);

/// <summary>
/// Runs <see cref="Texts"/>.Length phases; after phase n has rendered and any <see cref="State"/>
/// for it is set, the texts must be <see cref="Texts"/>[n] and the log <see cref="Logs"/>[n].
/// </summary>
public sealed record LifecycleScenario(
    string Name, Func<int, Probe, List<string>, Element> Arrange, (int AfterPhase, string Label, int Value)[] State,
    string[][] Texts, string[][] Logs);

public static class RendererConformanceScenarios
{
    private static Element T(string label, Probe probe) => Comp<Tally, TallyProps>(new(label, probe));

    private static Element K(string label, Probe probe) => Keyed(T(label, probe), label);

    public static readonly IdentityScenario[] Identity =
    [
        new("KeyedSiblingsSwapped_KeepTheirState",
            (phase, p) => phase == 0 ? Box(K("a", p), K("b", p)) : Box(K("b", p), K("a", p)),
            [("a", 1), ("b", 2)], ["a=1", "b=2"], ["b=2", "a=1"]),

        // WinUI's rule: an unkeyed child among keyed ones is matched by its absolute position. Before
        // spec 067 §13.6 MAUI matched it by position among the unkeyed children and kept its state.
        new("AnUnkeyedChildMovedPastAKeyedOne_IsANewChild",
            (phase, p) => phase == 0 ? Box(T("u", p), K("k", p)) : Box(K("k", p), T("u", p)),
            [("u", 1), ("k", 2)], ["u=1", "k=2"], ["k=2", "u=0"]),

        new("AConditionalChildBeforeAnUnkeyedOne_DoesNotResetIt",
            (phase, p) => Box(phase == 1 ? Text("x") : null, T("c", p)),
            [("c", 3)], ["c=3"], ["x", "c=3"]),

        new("AnUnkeyedChildThatChangesType_IsANewChild",
            (phase, p) => Box(phase == 0 ? T("t", p) : Comp<OtherTally, TallyProps>(new("t", p))),
            [("t", 4)], ["t=4"], ["t=0"]),

        new("UnkeyedSiblingsAtTheSamePositions_KeepTheirState_WhenOneIsAppended",
            (phase, p) => phase == 0 ? Box(T("a", p), T("b", p)) : Box(T("a", p), T("b", p), T("n", p)),
            [("a", 1), ("b", 2)], ["a=1", "b=2"], ["a=1", "b=2", "n=0"]),
    ];

    // Effect timing relative to the commit and to other components is renderer policy (§13.7);
    // within one component the dependency rules are the contract.
    public static readonly LifecycleScenario[] Lifecycle =
    [
        new("AnEffect_RunsOnce_ThenCleansUpAndRerunsWhenItsDependencyChanges_AndCleansUpOnUnmount",
            (phase, _, log) => Box(phase switch
            {
                0 or 1 => Comp<Effecting, EffectingProps>(new(1, log)),
                2 => Comp<Effecting, EffectingProps>(new(2, log)),
                _ => null,
            }),
            [],
            [["effect 1"], ["effect 1"], ["effect 2"], []],
            [["run 1"], ["run 1"], ["run 1", "cleanup 1", "run 2"], ["run 1", "cleanup 1", "run 2", "cleanup 2"]]),

        new("ABoundary_ShowsItsFallback_RunsTheFailedSubtreesCleanups_KeepsSiblings_AndRetriesWhenRerendered",
            (phase, p, log) => Box(
                Boundary(Comp<FailingChild, FailingProps>(new(phase == 1, log)), _ => Text("fallback")),
                T("s", p)),
            [(0, "s", 5)],
            [["ok", "s=5"], ["fallback", "s=5"], ["ok", "s=5"]],
            [[], ["failing cleanup"], ["failing cleanup"]]),
    ];
}
