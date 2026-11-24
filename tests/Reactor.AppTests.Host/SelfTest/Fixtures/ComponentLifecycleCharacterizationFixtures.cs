using System.Diagnostics.Tracing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Reactor.Hosting;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 067 §11.4 step 0: characterization of the component lifecycle, captured from the reconciler
/// before phase 2b moves the component runtime into the core. Each scenario records one ordered log
/// of what an app and a trace listener can observe: renders, effects, cleanups, rendered output, the
/// host logger's messages and the component-related <c>ReactorEventSource</c> events (delivered
/// synchronously, so they interleave in order). The log is compared with the golden captured on the
/// unchanged code. Each scenario runs untraced (the hot path) and traced, since both are refactored.
/// A difference is a behavior change: the extraction must not need these goldens edited.
/// </summary>
internal static class ComponentLifecycleCharacterization
{
    // ── Recording ────────────────────────────────────────────────────────

    private static readonly Lock Gate = new();
    private static List<string>? s_log;

    internal static void Log(string entry)
    {
        // Non-ASCII (the fallback's warning sign, an em dash in a log message) as '?', so a golden
        // reads the same wherever it was captured.
        var ascii = new StringBuilder(entry.Length);
        foreach (var c in entry) ascii.Append(c < 128 ? c : '?');
        lock (Gate) s_log?.Add(ascii.ToString());
    }

    private static readonly HashSet<string> TracedEvents =
    [
        nameof(ReactorEventSource.ComponentRenderStart), nameof(ReactorEventSource.ComponentRenderStop),
        nameof(ReactorEventSource.ComponentRendered), nameof(ReactorEventSource.ComponentUnmount),
        nameof(ReactorEventSource.EffectsFlushStart), nameof(ReactorEventSource.EffectsFlushStop),
        nameof(ReactorEventSource.RenderError), nameof(ReactorEventSource.HotReloadStateMigrated),
        nameof(ReactorEventSource.StateChange),
    ];

    private sealed class RecordingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        // Warnings and errors: the reconciler's debug lines carry timings.
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            ComponentLifecycleCharacterization.Log($"log:{logLevel}:{formatter(state, exception)}" + (exception is null ? "" : $"[{exception.GetType().Name}]"));
        }
    }

    /// <summary>
    /// Runs <paramref name="scenario"/> with a fresh log, untraced and then traced, and checks each
    /// step's log against its golden. <paramref name="scenario"/> calls the step callback after each
    /// step it wants pinned.
    /// </summary>
    private static async Task RunAsync(Harness h, string name, IReadOnlyDictionary<string, string[]> goldens,
        Func<ReactorHost, Func<string, Task>, Task> scenario)
    {
        foreach (var traced in new[] { false, true })
        {
            var mode = traced ? "traced" : "untraced";
            lock (Gate) s_log = [];
            using var subscription = traced
                ? ReactorTrace.Subscribe(e =>
                {
                    if (!TracedEvents.Contains(e.EventName)) return;
                    var strings = e.Payload.OfType<string>();
                    if (e.EventName == nameof(ReactorEventSource.StateChange))
                        strings = strings.Append(e.Payload.OfType<bool>().FirstOrDefault() ? "changed" : "same");
                    Log($"ev:{e.EventName}({string.Join(",", strings)})");
                }, EventLevel.Verbose, (EventKeywords)(-1))
                : null;
            // The events are enabled once a listener exists; NativeAOT strips EventSource.
            if (traced && !ComponentRenderTrace.IsEnabled)
            {
                h.Skip($"Lifecycle_{name}_{mode}", "EventSource disabled (NativeAOT)");
                lock (Gate) s_log = null;
                continue;
            }

            var target = new Microsoft.UI.Xaml.Controls.Border();
            h.SetContent(target);
            var host = new ReactorHost(h.Window, new RecordingLogger()) { ContentTarget = target };
            try
            {
                await scenario(host, async step =>
                {
                    await host.WaitForIdleAsync();
                    string[] observed;
                    lock (Gate)
                    {
                        observed = [.. s_log!];
                        s_log!.Clear();
                    }
                    var key = $"{step}/{mode}";
                    var check = $"Lifecycle_{name}_{step}_{mode}";
                    Check(h, check, goldens.TryGetValue(key, out var expected) ? expected : null, observed);
                });
            }
            finally
            {
                host.Dispose();
                lock (Gate) s_log = null;
                h.SetContent(null);
            }
        }
    }

    /// <summary>
    /// Compares a step's log with its golden. A mismatch names the first entry that differs, with
    /// its neighbors; a missing golden prints every entry on its own line (the test runner shortens
    /// long details), so the golden can be read back from the log.
    /// </summary>
    private static void Check(Harness h, string check, string[]? expected, string[] observed)
    {
        if (expected is null)
        {
            h.Check(check, false, $"no golden recorded; {observed.Length} entries follow");
            for (var i = 0; i < observed.Length; i++)
                h.Check($"{check}#{i:D2}", false, $"golden entry: {observed[i]}");
            return;
        }
        var length = Math.Max(expected.Length, observed.Length);
        for (var i = 0; i < length; i++)
        {
            var e = i < expected.Length ? expected[i] : "<none>";
            var o = i < observed.Length ? observed[i] : "<none>";
            if (e == o) continue;
            h.Check(check, false, $"entry {i} of {observed.Length} differs (golden has {expected.Length}). " +
                $"expected: {e} | observed: {o} | previous: {(i > 0 ? observed[i - 1] : "<start>")}");
            return;
        }
        h.Check(check, true);
    }

    /// <summary>The rendered texts under the host, in tree order (first line of each), so the output is part of the log.</summary>
    private static void LogOutput(Microsoft.UI.Xaml.DependencyObject root)
    {
        var texts = new List<string>();
        void Walk(Microsoft.UI.Xaml.DependencyObject node)
        {
            if (node is Microsoft.UI.Xaml.Controls.TextBlock tb) texts.Add(tb.Text.Split('\n')[0].Trim());
            var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++) Walk(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i));
        }
        Walk(root);
        Log("out:" + string.Join(";", texts));
    }

    // ── Scenario 1: class, function and memo components through mount, updates and unmount ──

    private sealed class LcProps : Component<int>
    {
        public override Element Render()
        {
            Log($"R:Props({Props})");
            UseEffect(() => { Log($"E:Props({Props})"); return () => Log("C:Props"); }, Props);
            return TextBlock($"props {Props}");
        }
    }

    private sealed class LcPropless : Component
    {
        internal static Action<int>? SetState;
        public override Element Render()
        {
            var (s, set) = UseState(0);
            SetState = set;
            Log($"R:Propless({s})");
            UseEffect(() => { Log($"E:Propless({s})"); return () => Log("C:Propless"); }, s);
            return VStack(TextBlock($"propless {s}"), Component<LcLeaf, string>("in-propless"));
        }
    }

    private sealed class LcAlways : Component
    {
        protected internal override bool ShouldUpdate() => true;
        public override Element Render()
        {
            Log("R:Always");
            UseEffect(() => Log("E:Always"));
            return TextBlock("always");
        }
    }

    private sealed class LcLeaf : Component<string>
    {
        public override Element Render()
        {
            Log($"R:Leaf({Props})");
            UseEffect(() => { Log($"E:Leaf({Props})"); return () => Log($"C:Leaf({Props})"); }, Props);
            return TextBlock($"leaf {Props}");
        }
    }

    internal sealed class Kinds(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => ComponentLifecycleCharacterization.RunAsync(H, "Kinds", KindsGoldens, async (host, step) =>
        {
            Action<int>? setN = null;
            Action<bool>? setShow = null;
            host.Mount(ctx =>
            {
                var (n, sn) = ctx.UseState(0);
                var (show, ss) = ctx.UseState(true);
                setN = sn;
                setShow = ss;
                Log($"R:Root({n},{show})");
                ctx.UseEffect(() => { Log($"E:Root({n})"); return () => Log("C:Root"); }, n);
                return VStack(
                    TextBlock($"root {n}"),
                    Component<LcProps, int>(n / 2),
                    show ? Component<LcPropless>() : TextBlock("hidden"),
                    Component<LcAlways>(),
                    RenderEachTime(c =>
                    {
                        var (f, _) = c.UseState(7);
                        Log($"R:Func({f})");
                        c.UseEffect(() => Log("E:Func"));
                        return Component<LcLeaf, string>("in-func");
                    }),
                    Memo(c =>
                    {
                        Log($"R:Memo({n % 2})");
                        c.UseEffect(() => { Log("E:Memo"); return () => Log("C:Memo"); }, n % 2);
                        return TextBlock($"memo {n % 2}");
                    }, n % 2));
            });
            await step("mount");
            LogOutput(host.ContentTarget!);
            await step("mount-output");

            setN!(1);
            await step("root-1");
            setN!(2);
            await step("root-2");
            LcPropless.SetState!(5);
            await step("propless-self");
            setShow!(false);
            await step("unmount-propless");
            LogOutput(host.ContentTarget!);
            await step("final-output");
            host.Mount(_ => TextBlock("replaced"));
            await step("replace-root");
        });
    }

    // ── Scenario 2: errors outside and inside a boundary, in render and in effects ──

    internal static int s_failMode;

    private sealed class LcThrower : Component<string>
    {
        public override Element Render()
        {
            Log($"R:Thrower({Props})");
            var mode = Volatile.Read(ref s_failMode);
            if (mode == 1 && Props.StartsWith("render", StringComparison.Ordinal))
                throw new InvalidOperationException($"render failed {Props}");
            UseEffect(() =>
            {
                Log($"E:Thrower({Props})");
                if (Volatile.Read(ref s_failMode) == 1 && Props.StartsWith("effect", StringComparison.Ordinal))
                    throw new InvalidOperationException($"effect failed {Props}");
            });
            return TextBlock($"thrower {Props}");
        }
    }

    internal sealed class Errors(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => ComponentLifecycleCharacterization.RunAsync(H, "Errors", ErrorsGoldens, async (host, step) =>
        {
            Volatile.Write(ref s_failMode, 0);
            Action<int>? setN = null;
            host.Mount(ctx =>
            {
                var (n, sn) = ctx.UseState(0);
                setN = sn;
                Log($"R:Root({n})");
                return VStack(
                    Component<LcThrower, string>("render-loose"),
                    Component<LcThrower, string>("effect-loose"),
                    RenderEachTime(c =>
                    {
                        Log("R:FuncThrower");
                        if (Volatile.Read(ref s_failMode) == 1) throw new InvalidOperationException("func failed");
                        return TextBlock("func ok");
                    }),
                    Memo(c =>
                    {
                        Log("R:MemoThrower");
                        if (Volatile.Read(ref s_failMode) == 1) throw new InvalidOperationException("memo failed");
                        return TextBlock("memo ok");
                    }, n),
                    ErrorBoundary(
                        VStack(TextBlock($"bounded {n}"), Component<LcThrower, string>("render-bounded")),
                        ex => TextBlock($"fallback {ex.Message}")),
                    ErrorBoundary(
                        Component<LcThrower, string>("effect-bounded"),
                        ex => TextBlock($"fallback {ex.Message}")));
            });
            await step("mount");

            Volatile.Write(ref s_failMode, 1);
            setN!(1);
            await step("update-fails");
            LogOutput(host.ContentTarget!);
            await step("update-fails-output");

            Volatile.Write(ref s_failMode, 0);
            setN!(2);
            await step("recover");
            LogOutput(host.ContentTarget!);
            await step("recover-output");

            // Mount-time failures: a fresh tree whose components throw on their first render.
            Volatile.Write(ref s_failMode, 1);
            host.Mount(_ => VStack(
                Component<LcThrower, string>("render-mount"),
                ErrorBoundary(Component<LcThrower, string>("render-mount-bounded"), ex => TextBlock($"fallback {ex.Message}")),
                ErrorBoundary(VStack(TextBlock("sibling"), RenderEachTime(_ => throw new InvalidOperationException("descendant failed"))),
                    ex => TextBlock($"fallback {ex.Message}"))));
            await step("mount-fails");
            LogOutput(host.ContentTarget!);
            await step("mount-fails-output");
            Volatile.Write(ref s_failMode, 0);
        });
    }

    // ── Scenario 3: a hook-order change in a child during a hot-reload pass ──

    private static int s_shape;

    private sealed class LcReshaped : Component
    {
        public override Element Render()
        {
            Log($"R:Reshaped({Volatile.Read(ref s_shape)})");
            if (Volatile.Read(ref s_shape) == 0)
            {
                var (v, _) = UseState(1);
                UseEffect(() => { Log("E:Reshaped0"); return () => Log("C:Reshaped0"); }, v);
                return TextBlock("shape 0");
            }
            UseEffect(() => { Log("E:Reshaped1"); return () => Log("C:Reshaped1"); }, "x");
            UseState(2);
            return TextBlock("shape 1");
        }
    }

    internal sealed class HotReloadRetry(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => ComponentLifecycleCharacterization.RunAsync(H, "HotReload", HotReloadGoldens, async (host, step) =>
        {
            Volatile.Write(ref s_shape, 0);
            host.Mount(_ => VStack(Component<LcReshaped>(), Component<LcLeaf, string>("sibling")));
            await step("mount");

            Volatile.Write(ref s_shape, 1);
            HotReloadService.UpdateApplication(null);
            host.RequestRender(force: true);
            await step("hot-reload");
            LogOutput(host.ContentTarget!);
            await step("hot-reload-output");
            Volatile.Write(ref s_shape, 0);
        });
    }

    // ── Scenario 4: a state change from another thread, and requests batched on the UI thread ──
    // (Several worker-thread changes are not pinned: whether they share a pass depends on timing.)

    internal sealed class Rerender(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => ComponentLifecycleCharacterization.RunAsync(H, "Rerender", RerenderGoldens, async (host, step) =>
        {
            var uiThread = Environment.CurrentManagedThreadId;
            Action<int>? setSafe = null;
            Action<int>? setChild = null;
            host.Mount(ctx =>
            {
                var (n, set) = ctx.UseState(0, threadSafe: true);
                setSafe = set;
                Log($"R:Root({n},{(Environment.CurrentManagedThreadId == uiThread ? "ui" : "other")})");
                return VStack(
                    TextBlock($"root {n}"),
                    RenderEachTime(c =>
                    {
                        var (m, sm) = c.UseState(0, threadSafe: true);
                        setChild = sm;
                        Log($"R:Child({m},{(Environment.CurrentManagedThreadId == uiThread ? "ui" : "other")})");
                        return TextBlock($"child {m}");
                    }));
            });
            await step("mount");

            await Task.Run(() => setChild!(1));
            await step("child-from-worker");
            // Two requests from the UI thread before the pass runs: one render.
            setChild!(3);
            setSafe!(3);
            await step("batched-on-ui");
        });
    }

    // ── Goldens, captured on the unchanged reconciler (spec 067 phase 2b, step 0) ──

    private static readonly Dictionary<string, string[]> KindsGoldens = new();
    private static readonly Dictionary<string, string[]> ErrorsGoldens = new();
    private static readonly Dictionary<string, string[]> HotReloadGoldens = new();
    private static readonly Dictionary<string, string[]> RerenderGoldens = new();
}
