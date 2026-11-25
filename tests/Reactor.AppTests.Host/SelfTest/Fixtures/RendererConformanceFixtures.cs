using RendererConformanceKit;
using Microsoft.UI.Reactor.Hosting;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 067 §13.6–13.7: renderer conformance. The scenarios and their expected results are
/// tests/_shared/RendererConformanceScenarios.cs, which MAUI's engine tests run too
/// (RendererConformanceTests). These fixtures drive them through a real <see cref="ReactorHost"/>.
/// </summary>
internal static class RendererConformance
{
    internal sealed class Identity(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            foreach (var scenario in RendererConformanceScenarios.Identity)
            {
                var target = new Microsoft.UI.Xaml.Controls.Border();
                H.SetContent(target);
                try
                {
                    using var host = new ReactorHost(H.Window) { ContentTarget = target };
                    var probe = new RendererConformanceKit.Probe();
                    host.Mount(_ => Component<Arrangement, ArrangementProps>(new(scenario.Arrange, probe)));
                    await host.WaitForIdleAsync();
                    foreach (var (label, value) in scenario.State) probe.Setters[label](value);
                    await host.WaitForIdleAsync();
                    CheckTexts($"Conformance_Identity_{scenario.Name}_Before", scenario.Before, target);

                    probe.Setters["phase"](1);
                    await host.WaitForIdleAsync();
                    CheckTexts($"Conformance_Identity_{scenario.Name}", scenario.After, target);
                }
                finally
                {
                    H.SetContent(null);
                }
            }
        }

        private void CheckTexts(string name, string[] expected, Microsoft.UI.Xaml.UIElement target)
        {
            var texts = Texts(target);
            H.Check(name, texts.SequenceEqual(expected), $"expected [{string.Join(", ", expected)}], got [{string.Join(", ", texts)}]");
        }
    }

    internal sealed class Lifecycle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            foreach (var scenario in RendererConformanceScenarios.Lifecycle)
            {
                var target = new Microsoft.UI.Xaml.Controls.Border();
                H.SetContent(target);
                try
                {
                    // Disposing the host runs its remaining components' cleanups; every check is
                    // made before that, the interval MAUI's runner observes too.
                    using var host = new ReactorHost(H.Window) { ContentTarget = target };
                    var probe = new RendererConformanceKit.Probe();
                    var log = new List<string>();
                    host.Mount(_ => Component<Arrangement, ArrangementProps>(new((phase, p) => scenario.Arrange(phase, p, log), probe)));
                    await host.WaitForIdleAsync();
                    for (var phase = 0; phase < scenario.Texts.Length; phase++)
                    {
                        if (phase > 0) probe.Setters["phase"](phase);
                        await host.WaitForIdleAsync();
                        foreach (var (after, label, value) in scenario.State)
                            if (after == phase) probe.Setters[label](value);
                        await host.WaitForIdleAsync();

                        var texts = Texts(target);
                        H.Check($"Conformance_Lifecycle_{scenario.Name}_Phase{phase}_Texts", texts.SequenceEqual(scenario.Texts[phase]),
                            $"expected [{string.Join(", ", scenario.Texts[phase])}], got [{string.Join(", ", texts)}]");
                        H.Check($"Conformance_Lifecycle_{scenario.Name}_Phase{phase}_Log", log.SequenceEqual(scenario.Logs[phase]),
                            $"expected [{string.Join(", ", scenario.Logs[phase])}], got [{string.Join(", ", log)}]");
                    }
                }
                finally
                {
                    H.SetContent(null);
                }
            }
        }
    }

    private static List<string> Texts(Microsoft.UI.Xaml.DependencyObject root)
    {
        var texts = new List<string>();
        Collect(root);
        return texts;

        void Collect(Microsoft.UI.Xaml.DependencyObject node)
        {
            if (node is Microsoft.UI.Xaml.Controls.TextBlock text)
            {
                texts.Add(text.Text);
                return;
            }
            var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
                Collect(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i));
        }
    }
}
