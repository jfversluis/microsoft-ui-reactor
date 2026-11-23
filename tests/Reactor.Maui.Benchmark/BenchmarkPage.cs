using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Graphics;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Maui;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;

namespace Reactor.Maui.Benchmark;

/// <summary>
/// Renders the same element tree through the Controls renderer and the direct renderer and
/// times each operation: the Reactor render (reconcile plus property writes and native view
/// creation) followed by a forced MAUI measure and arrange of the whole tree. Renderers are
/// interleaved so drift (thermal, JIT, GC) affects both equally.
/// </summary>
public sealed class BenchmarkPage : ContentPage
{
    private const int Rows = 1000;
    private const int Warmup = 2;
    private const int Measured = 7;
    private const int ChangedRow = Rows / 2;
    private const int SettleMs = 250;
    private const int GcSettleMs = 1000;

    private readonly Grid _host = [];
    private readonly Label _status = new() { Text = "Starting…", Margin = new Thickness(12) };
    private bool _started;

    public BenchmarkPage()
    {
        Content = new Grid
        {
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)],
            Children = { _status, _host },
        };
        Microsoft.Maui.Controls.Grid.SetRow(_host, 1);
        Loaded += async (_, _) =>
        {
            if (_started) return;
            _started = true;
            try
            {
                await RunAsync();
            }
            catch (Exception ex)
            {
                Emit($"REACTOR_BENCH_ERROR {ex}");
                _status.Text = ex.ToString();
            }
        };
    }

    private sealed record Renderer(string Name, ReactorRoot Root, Shape Shape);

    /// <summary>How rows are built. Default compares the two renderers; anchor mode compares component shapes.</summary>
    private enum Shape { PlainRows, ComponentRows, AnchoredComponentRows }

    // REACTOR_BENCH_MODE=anchor measures what a native anchor per component (spec 067 §5) would
    // cost: both legs use the direct renderer and render each row as a component; the anchored leg
    // wraps each component's output in a single-child pass-through layout, as an anchor would.
    private static readonly bool AnchorMode = Environment.GetEnvironmentVariable("REACTOR_BENCH_MODE") == "anchor";

    private sealed record RowProps(int Index, string Value, bool Anchored);

    private sealed class RowComponent : Component<RowProps>
    {
        public override Element Render()
        {
            var row = HStack(8, Label($"Row {Props.Index}").Width(80), Label(Props.Value), Button("Edit"));
            return Props.Anchored ? VStack(row) : row;
        }
    }

    /// <summary>Layout facts that must be identical across renderers; they prove both did the same work.</summary>
    private sealed record Check(double LabelWidth, double LabelFrameWidth, double LastRowY, double TotalHeight);

    private sealed record Sample(double RenderMs, double LayoutMs, long AllocatedBytes, int Collections, Check Check)
    {
        public double TotalMs => RenderMs + LayoutMs;
    }

    private enum Scenario { Mount, UpdateAll, UpdateOne, Reverse }

    private async Task RunAsync()
    {
        await Task.Delay(500);
        // Layout is forced at the host's real size, so it is the same pass the next frame would run.
        // Measuring at another size would make the forced and real passes invalidate each other's
        // native measure caches, adding a full re-measure to whichever one runs next.
        var bounds = new Size(_host.Width > 0 ? _host.Width : 400, _host.Height > 0 ? _host.Height : 800);
        var scheduler = new ImmediateScheduler();
        var renderers = AnchorMode
            ? new[]
            {
                new Renderer("components", new ReactorRoot(DirectHandlers.CreateRegistry(), scheduler, SetRoot), Shape.ComponentRows),
                new Renderer("anchored", new ReactorRoot(DirectHandlers.CreateRegistry(), scheduler, SetRoot), Shape.AnchoredComponentRows),
            }
            : new[]
            {
                new Renderer("controls", new ReactorRoot(MauiHandlers.CreateRegistry(), scheduler, SetRoot), Shape.PlainRows),
                new Renderer("direct", new ReactorRoot(DirectHandlers.CreateRegistry(), scheduler, SetRoot), Shape.PlainRows),
            };

        var results = new Dictionary<(string, Scenario), List<Sample>>();
        var scenarios = Enum.GetValues<Scenario>();
        for (var iteration = 0; iteration < Warmup + Measured; iteration++)
        {
            foreach (var scenario in scenarios)
            {
                // Alternate which renderer goes first, so neither always runs on a warmer cache.
                var order = iteration % 2 == 0 ? renderers : renderers.AsEnumerable().Reverse();
                foreach (var renderer in order)
                {
                    _status.Text = $"Iteration {iteration + 1}/{Warmup + Measured}: {scenario} ({renderer.Name})";
                    var sample = await RunScenarioAsync(renderer, scenario, bounds);
                    if (iteration >= Warmup)
                    {
                        if (!results.TryGetValue((renderer.Name, scenario), out var list))
                            results[(renderer.Name, scenario)] = list = [];
                        list.Add(sample);
                    }
                }
            }
        }

        Report(results, bounds, renderers[0].Name, renderers[1].Name);
    }

    private async Task<Sample> RunScenarioAsync(Renderer renderer, Scenario scenario, Size bounds)
    {
        var (name, root, shape) = (renderer.Name, renderer.Root, renderer.Shape);
        // Untimed setup: every scenario except Mount starts from a mounted, laid-out tree.
        IView? lastRowBefore = null;
        if (scenario != Scenario.Mount)
        {
            root.Render(Tree(shape, version: 0, changedRowVersion: 0, reversed: false));
            Layout(bounds);
            lastRowBefore = RowView(Rows - 1);
            await Task.Delay(SettleMs);
        }

        var next = scenario switch
        {
            Scenario.Mount => Tree(shape, 0, 0, false),
            Scenario.UpdateAll => Tree(shape, 1, 1, false),
            Scenario.UpdateOne => Tree(shape, 0, 1, false),
            _ => Tree(shape, 0, 0, true),
        };

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        // On Android each .NET GC also starts a concurrent Java GC on another thread. With
        // thousands of views alive it runs for hundreds of milliseconds, and the UI thread blocks
        // on it at its next JNI peer access, which would land inside the timer. Let it finish.
        await Task.Delay(GcSettleMs);

        var collectionsBefore = GC.CollectionCount(0);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        BeginTrace($"bench {name} {scenario} render");
        root.Render(next);
        EndTrace();
        var rendered = Stopwatch.GetTimestamp();
        BeginTrace($"bench {name} {scenario} layout");
        var size = Layout(bounds);
        EndTrace();
        var laidOut = Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        var sample = new Sample(
            Stopwatch.GetElapsedTime(start, rendered).TotalMilliseconds,
            Stopwatch.GetElapsedTime(rendered, laidOut).TotalMilliseconds,
            allocated,
            GC.CollectionCount(0) - collectionsBefore,
            Validate(scenario, shape, size, lastRowBefore));

        // Untimed teardown, and a pause so the platform can process removals and draw.
        root.Unmount();
        await Task.Delay(SettleMs);
        return sample;
    }

    private IView RowView(int index) => ((IList<IView>)_host[0])[index];

    // The row's own HStack, inside the anchor when there is one.
    private IList<IView> RowContent(int index, Shape shape)
    {
        var row = RowView(index);
        return (IList<IView>)(shape == Shape.AnchoredComponentRows ? ((IList<IView>)row)[0] : row);
    }

    /// <summary>
    /// Checks, outside the timer, that the operation really completed: the changed label shows the
    /// expected text, it and the last row were arranged, and a reorder moved the existing native rows.
    /// Any failure aborts the run. The returned facts are compared across renderers in the report.
    /// </summary>
    private Check Validate(Scenario scenario, Shape shape, Size size, IView? lastRowBefore)
    {
        var rowIndex = scenario == Scenario.UpdateOne ? ChangedRow : 0;
        var row = RowContent(rowIndex, shape);
        var label = row[1];
        var expectedRow = scenario == Scenario.Reverse ? Rows - 1 - rowIndex : rowIndex;
        var expectedText = scenario is Scenario.UpdateAll or Scenario.UpdateOne
            ? $"value {expectedRow}, revision 1"
            : $"v{expectedRow}";
        var actualText = ((ILabel)label).Text;
        if (actualText != expectedText)
            throw new InvalidOperationException($"{scenario}: row {rowIndex} shows '{actualText}', expected '{expectedText}'.");

        if (scenario == Scenario.Reverse && !ReferenceEquals(RowView(0), lastRowBefore))
            throw new InvalidOperationException("Reverse: the first row is not the native view that was last before the reorder.");

        var lastRow = RowView(Rows - 1);
        if (label.Frame.Width <= 0 || lastRow.Frame.Y <= 0)
            throw new InvalidOperationException(
                $"{scenario}: layout did not complete (label frame {label.Frame}, last row frame {lastRow.Frame}).");

        return new Check(label.DesiredSize.Width, label.Frame.Width, lastRow.Frame.Y, size.Height);
    }

    private Size Layout(Size bounds)
    {
        var host = (IView)_host;
        var size = host.Measure(bounds.Width, bounds.Height);
        host.Arrange(new Rect(Point.Zero, bounds));
#if IOS || MACCATALYST
        // On iOS, Arrange only sets the host's bounds; descendants are arranged in LayoutSubviews.
        // Run that pass now so it is inside the timer. Android arranges children inside Arrange.
        (_host.Handler?.PlatformView as UIKit.UIView)?.LayoutIfNeeded();
#endif
        return size;
    }

    private void SetRoot(object? native)
    {
        _host.Clear();
        if (native is not null) _host.Add((IView)native);
    }

    // Each row's value text differs in length between versions, so an update changes its measured size.
    private static Element Tree(Shape shape, int version, int changedRowVersion, bool reversed)
    {
        var rows = new Element[Rows];
        for (var i = 0; i < Rows; i++)
        {
            var rowVersion = i == ChangedRow ? changedRowVersion : version;
            var value = rowVersion == 0 ? $"v{i}" : $"value {i}, revision {rowVersion}";
            rows[reversed ? Rows - 1 - i : i] = shape == Shape.PlainRows
                ? HStack(8, Label($"Row {i}").Width(80), Label(value), Button("Edit")).WithKey(i.ToString())
                : Component<RowComponent, RowProps>(new(i, value, shape == Shape.AnchoredComponentRows)).WithKey(i.ToString());
        }
        return VStack(2, rows);
    }

    private void Report(Dictionary<(string Renderer, Scenario Scenario), List<Sample>> results, Size bounds, string baseline, string variant)
    {
        var platform = DeviceInfo.Current;
        var environment = new
        {
            platform = platform.Platform.ToString(),
            device = $"{platform.Manufacturer} {platform.Model}",
            deviceType = platform.DeviceType.ToString(),
            os = platform.VersionString,
            runtime = RuntimeInformation.FrameworkDescription,
            dynamicCode = RuntimeFeature.IsDynamicCodeCompiled,
            mode = AnchorMode ? "anchor" : "renderers",
            baseline,
            variant,
            rows = Rows,
            measured = Measured,
            width = bounds.Width,
            height = bounds.Height,
        };
        Emit($"REACTOR_BENCH_ENV {JsonSerializer.Serialize(environment)}");

        var table = new StringBuilder();
        table.AppendLine($"{environment.device} · {environment.platform} {environment.os} · {Rows} rows · median of {Measured}");
        foreach (var scenario in Enum.GetValues<Scenario>())
        {
            var a = Summarize(results[(baseline, scenario)]);
            var b = Summarize(results[(variant, scenario)]);
            var line = new Dictionary<string, object>
            {
                ["scenario"] = scenario.ToString(),
                [baseline] = a,
                [variant] = b,
                ["totalDelta"] = Delta(a.TotalMs, b.TotalMs),
                ["allocDelta"] = Delta(a.AllocatedKB, b.AllocatedKB),
                ["layoutMatches"] = a.Checks.Count == 1 && a.Checks.SetEquals(b.Checks),
            };
            Emit($"REACTOR_BENCH_RESULT {JsonSerializer.Serialize(line)}");
            table.AppendLine($"{scenario}: {baseline} {a.TotalMs:F1} ms / {a.AllocatedKB:F0} KB, {variant} {b.TotalMs:F1} ms / {b.AllocatedKB:F0} KB ({line["totalDelta"]}, alloc {line["allocDelta"]}){((bool)line["layoutMatches"] ? "" : " LAYOUT MISMATCH")}");
        }
        Emit("REACTOR_BENCH_DONE");
        _status.Text = table.ToString();
    }

    private static Summary Summarize(List<Sample> samples) => new(
        Median(samples.Select(s => s.RenderMs)),
        Median(samples.Select(s => s.LayoutMs)),
        Median(samples.Select(s => s.TotalMs)),
        Median(samples.Select(s => (double)s.AllocatedBytes)) / 1024,
        samples.Min(s => s.TotalMs),
        samples.Max(s => s.TotalMs),
        samples.Select(s => s.Check).ToHashSet(),
        samples.Select(s => $"{s.RenderMs:F0}+{s.LayoutMs:F0}/gc{s.Collections}").ToArray());

    // Checks holds every distinct layout outcome; a healthy run has exactly one, shared by both renderers.
    private sealed record Summary(double RenderMs, double LayoutMs, double TotalMs, double AllocatedKB, double MinMs, double MaxMs, HashSet<Check> Checks, string[] SamplesMs);

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    private static string Delta(double baseline, double value) =>
        baseline == 0 ? "n/a" : $"{(value - baseline) / baseline * 100:+0;-0}%";

    private static void Emit(string line) => Console.WriteLine(line);

    // Sections show up in Perfetto/systrace captures on Android, to attribute time inside a phase.
    private static void BeginTrace(string name)
    {
#if ANDROID
        Android.OS.Trace.BeginSection(name);
#endif
    }

    private static void EndTrace()
    {
#if ANDROID
        Android.OS.Trace.EndSection();
#endif
    }

    /// <summary>Everything runs on the UI thread and no state is set, so nothing is ever posted.</summary>
    private sealed class ImmediateScheduler : IRenderScheduler
    {
        public bool CheckAccess() => true;
        public void Post(Action work) => work();
    }
}
