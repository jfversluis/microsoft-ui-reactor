using RendererConformanceKit;
using static Microsoft.UI.Reactor.CoreFactories;

namespace Microsoft.UI.Reactor.Core.Tests;

// ── Renderer conformance (spec 067 §13.6–13.7) ──────────────────────────────
// The scenarios and their expected results are tests/_shared/RendererConformanceScenarios.cs, which
// the WinUI selftest hosts run too (RendererConformance.Identity / .Lifecycle).

public class RendererConformanceTests
{
    public static TheoryData<string> IdentityNames => [.. RendererConformanceScenarios.Identity.Select(s => s.Name)];

    public static TheoryData<string> LifecycleNames => [.. RendererConformanceScenarios.Lifecycle.Select(s => s.Name)];

    private static string[] Texts(TestHost host) => [.. host.RootView!.Children.Select(c => c.Text ?? "")];

    [Theory]
    [MemberData(nameof(IdentityNames))]
    public void Identity(string name)
    {
        var scenario = RendererConformanceScenarios.Identity.Single(s => s.Name == name);
        var probe = new RendererConformanceKit.Probe();
        var host = new TestHost().Render(Component<Arrangement, ArrangementProps>(new(scenario.Arrange, probe)));
        foreach (var (label, value) in scenario.State) probe.Setters[label](value);
        host.Scheduler.Drain();
        Assert.Equal(scenario.Before, Texts(host));

        probe.Setters["phase"](1);
        host.Scheduler.Drain();
        Assert.Equal(scenario.After, Texts(host));
    }

    [Theory]
    [MemberData(nameof(LifecycleNames))]
    public void Lifecycle(string name)
    {
        var scenario = RendererConformanceScenarios.Lifecycle.Single(s => s.Name == name);
        var probe = new RendererConformanceKit.Probe();
        var log = new List<string>();
        var host = new TestHost().Render(Component<Arrangement, ArrangementProps>(new((phase, p) => scenario.Arrange(phase, p, log), probe)));
        for (var phase = 0; phase < scenario.Texts.Length; phase++)
        {
            if (phase > 0) probe.Setters["phase"](phase);
            host.Scheduler.Drain();
            foreach (var (after, label, value) in scenario.State)
                if (after == phase) probe.Setters[label](value);
            host.Scheduler.Drain();
            Assert.Equal(scenario.Texts[phase], Texts(host));
            Assert.Equal(scenario.Logs[phase], log);
        }
    }
}
