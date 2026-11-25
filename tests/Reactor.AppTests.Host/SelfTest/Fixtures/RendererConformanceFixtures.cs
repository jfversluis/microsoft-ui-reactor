using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 067 §11.3, §13.6: renderer conformance for child identity. The same scenarios, with the
/// same expected texts, run on MAUI's engine (tests/Reactor.Maui.Engine.Tests,
/// IdentityConformanceTests). Each mounts tallies, sets their state, re-renders a different
/// arrangement from the root's own state, and reads which tallies kept theirs.
/// </summary>
internal static class RendererConformance
{
    private sealed class Probe
    {
        public Dictionary<string, Action<int>> Setters { get; } = [];
    }

    private sealed record TallyProps(string Label, Probe Probe);

    private sealed class Tally : Component<TallyProps>
    {
        public override Element Render()
        {
            var (count, setCount) = UseState(0);
            Props.Probe.Setters[Props.Label] = setCount;
            return TextBlock($"{Props.Label}={count}");
        }
    }

    private sealed class OtherTally : Component<TallyProps>
    {
        public override Element Render()
        {
            var (count, setCount) = UseState(0);
            Props.Probe.Setters[Props.Label] = setCount;
            return TextBlock($"{Props.Label}={count}");
        }
    }

    private sealed record ArrangementProps(Func<int, Probe, Element> Arrange, Probe Probe);

    private sealed class Arrangement : Component<ArrangementProps>
    {
        public override Element Render()
        {
            var (phase, setPhase) = UseState(0);
            Props.Probe.Setters["phase"] = setPhase;
            return Props.Arrange(phase, Props.Probe);
        }
    }

    private sealed record EffectingProps(int Dependency, List<string> Log);

    private sealed class Effecting : Component<EffectingProps>
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
            return TextBlock($"effect {dependency}");
        }
    }

    private sealed record FailingProps(bool Throw, List<string> Log);

    private sealed class FailingChild : Component<FailingProps>
    {
        public override Element Render()
        {
            var log = Props.Log;
            UseEffect(() => () => log.Add("failing cleanup"));
            if (Props.Throw) throw new InvalidOperationException("failing child failed");
            return TextBlock("ok");
        }
    }

    private static Element T(string label, Probe probe) => Component<Tally, TallyProps>(new(label, probe));

    private static Element K(string label, Probe probe) => T(label, probe) with { Key = label };

    internal sealed class Identity(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Check("KeyedSiblingsSwapped_KeepTheirState", ["b=2", "a=1"], (phase, p) => phase == 0
                ? VStack(K("a", p), K("b", p))
                : VStack(K("b", p), K("a", p)), ("a", 1), ("b", 2));

            await Check("AnUnkeyedChildMovedPastAKeyedOne_IsANewChild", ["k=2", "u=0"], (phase, p) => phase == 0
                ? VStack(T("u", p), K("k", p))
                : VStack(K("k", p), T("u", p)), ("u", 1), ("k", 2));

            await Check("AConditionalChildBeforeAnUnkeyedOne_DoesNotResetIt", ["x", "c=3"], (phase, p) =>
                VStack(phase == 1 ? TextBlock("x") : null, T("c", p)), ("c", 3));

            await Check("AnUnkeyedChildThatChangesType_IsANewChild", ["t=0"], (phase, p) => VStack(phase == 0
                ? T("t", p)
                : Component<OtherTally, TallyProps>(new("t", p))), ("t", 4));

            await Check("UnkeyedSiblingsAtTheSamePositions_KeepTheirState_WhenOneIsAppended", ["a=1", "b=2", "n=0"], (phase, p) => phase == 0
                ? VStack(T("a", p), T("b", p))
                : VStack(T("a", p), T("b", p), T("n", p)), ("a", 1), ("b", 2));
        }

        internal static void CollectTexts(Microsoft.UI.Xaml.DependencyObject node, List<string> texts)
        {
            if (node is Microsoft.UI.Xaml.Controls.TextBlock text)
            {
                texts.Add(text.Text);
                return;
            }
            var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
                CollectTexts(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i), texts);
        }

        private async Task Check(string name, string[] expected, Func<int, Probe, Element> arrange, params (string Label, int Value)[] state)
        {
            var target = new Microsoft.UI.Xaml.Controls.Border();
            H.SetContent(target);
            try
            {
                using var host = new ReactorHost(H.Window) { ContentTarget = target };
                var probe = new Probe();
                host.Mount(_ => Component<Arrangement, ArrangementProps>(new(arrange, probe)));
                await host.WaitForIdleAsync();
                foreach (var (label, value) in state) probe.Setters[label](value);
                await host.WaitForIdleAsync();
                probe.Setters["phase"](1);
                await host.WaitForIdleAsync();

                var texts = new List<string>();
                CollectTexts(target, texts);
                H.Check($"Conformance_Identity_{name}", texts.SequenceEqual(expected),
                    $"expected [{string.Join(", ", expected)}], got [{string.Join(", ", texts)}]");
            }
            finally
            {
                H.SetContent(null);
            }
        }
    }

    /// <summary>Spec 067 §13.7: effects and error boundaries, mirrored by MAUI's LifecycleConformanceTests.</summary>
    internal sealed class Lifecycle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var (effectLog, _) = await Script((phase, _, log) => VStack(phase switch
            {
                0 or 1 => Component<Effecting, EffectingProps>(new(1, log)),
                2 => Component<Effecting, EffectingProps>(new(2, log)),
                _ => null,
            }), phases: 4);
            H.Check("Conformance_Lifecycle_EffectDependencies",
                effectLog.SequenceEqual(["run 1", "cleanup 1", "run 2", "cleanup 2"]), $"got [{string.Join(", ", effectLog)}]");

            var (boundaryLog, texts) = await Script((phase, p, log) => VStack(
                ErrorBoundary(Component<FailingChild, FailingProps>(new(phase == 1, log)), _ => TextBlock("fallback")),
                Component<Tally, TallyProps>(new("s", p))), phases: 3, (0, "s", 5));
            string Show(int phase) => string.Join(", ", texts[phase]);
            H.Check("Conformance_Lifecycle_BoundaryBeforeFailure", texts[0].SequenceEqual(["ok", "s=5"]), Show(0));
            H.Check("Conformance_Lifecycle_BoundaryShowsFallbackKeepsSibling", texts[1].SequenceEqual(["fallback", "s=5"]), Show(1));
            H.Check("Conformance_Lifecycle_BoundaryRetriesWhenRerendered", texts[2].SequenceEqual(["ok", "s=5"]), Show(2));
            H.Check("Conformance_Lifecycle_BoundaryRunsFailedCleanups",
                boundaryLog.SequenceEqual(["failing cleanup"]), $"got [{string.Join(", ", boundaryLog)}]");
        }

        private async Task<(List<string> Log, List<string[]> Texts)> Script(
            Func<int, Probe, List<string>, Element> arrange, int phases, params (int AfterPhase, string Label, int Value)[] state)
        {
            var target = new Microsoft.UI.Xaml.Controls.Border();
            H.SetContent(target);
            var log = new List<string>();
            var texts = new List<string[]>();
            try
            {
                using var host = new ReactorHost(H.Window) { ContentTarget = target };
                var probe = new Probe();
                host.Mount(_ => Component<Arrangement, ArrangementProps>(new((phase, p) => arrange(phase, p, log), probe)));
                await host.WaitForIdleAsync();
                for (var phase = 0; phase < phases; phase++)
                {
                    if (phase > 0) probe.Setters["phase"](phase);
                    await host.WaitForIdleAsync();
                    foreach (var (after, label, value) in state)
                        if (after == phase) probe.Setters[label](value);
                    await host.WaitForIdleAsync();
                    var phaseTexts = new List<string>();
                    Identity.CollectTexts(target, phaseTexts);
                    texts.Add([.. phaseTexts]);
                }
            }
            finally
            {
                H.SetContent(null);
            }
            return (log, texts);
        }
    }
}
