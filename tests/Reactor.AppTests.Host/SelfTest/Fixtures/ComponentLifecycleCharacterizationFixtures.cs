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
                    // A state change made on another thread reaches the host through the
                    // dispatcher; let it arrive before asking whether the host is idle.
                    await DispatcherHop();
                    await host.WaitForIdleAsync();
                    string[] observed;
                    lock (Gate)
                    {
                        observed = [.. s_log!];
                        s_log!.Clear();
                    }
                    var key = $"{step}/{mode}";
                    var check = $"Lifecycle_{name}_{step}_{mode}";
                    // A NativeAOT build has its own golden where it legitimately differs ("@aot").
                    string[]? expected = !global::System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported
                        && goldens.TryGetValue(key + "@aot", out var aotGolden)
                        ? aotGolden
                        : goldens.TryGetValue(key, out var golden) ? golden : null;
                    Check(h, check, expected, observed);
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

    /// <summary>Completes after everything already queued on the UI dispatcher has run.</summary>
    private static Task DispatcherHop()
    {
        var done = new TaskCompletionSource();
        var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (!queue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => done.SetResult()))
            done.SetResult();
        return done.Task;
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

    /// <summary>
    /// The rendered texts under the host, in tree order (first line of each), so the output is part
    /// of the log. Walks the logical tree (a control's content and children), not the visual one: a
    /// templated control's parts exist only after layout, which would make the log timing-dependent.
    /// </summary>
    private static void LogOutput(Microsoft.UI.Xaml.UIElement root)
    {
        var texts = new List<string>();
        void Walk(object? node)
        {
            switch (node)
            {
                case Microsoft.UI.Xaml.Controls.TextBlock tb:
                    texts.Add(tb.Text.Split('\n')[0].Trim());
                    break;
                case Microsoft.UI.Xaml.Controls.Border b:
                    Walk(b.Child);
                    break;
                case Microsoft.UI.Xaml.Controls.Panel p:
                    foreach (var child in p.Children) Walk(child);
                    break;
                case Microsoft.UI.Xaml.Controls.ContentControl cc:
                    Walk(cc.Content);
                    break;
                case Microsoft.UI.Xaml.Controls.UserControl uc:
                    Walk(uc.Content);
                    break;
            }
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
            }, Props);
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
                // Props carry n, so the class throwers re-render (and re-run their effect) on each update.
                return VStack(
                    Component<LcThrower, string>($"render-loose {n}"),
                    Component<LcThrower, string>($"effect-loose {n}"),
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
                        VStack(TextBlock($"bounded {n}"), Component<LcThrower, string>($"render-bounded {n}")),
                        ex => TextBlock($"fallback {ex.Message}")),
                    ErrorBoundary(
                        Component<LcThrower, string>($"effect-bounded {n}"),
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

            // Mount-time failures: keyed, so nothing from the previous tree is reused and each
            // component throws on its first render.
            Volatile.Write(ref s_failMode, 1);
            host.Mount(_ => VStack(
                Component<LcThrower, string>("render-mount") with { Key = "m1" },
                Component<LcThrower, string>("effect-mount") with { Key = "m2" },
                RenderEachTime(_ => throw new InvalidOperationException("func mount failed")) with { Key = "m3" },
                Memo(_ => throw new InvalidOperationException("memo mount failed"), 1) with { Key = "m4" },
                ErrorBoundary(Component<LcThrower, string>("render-mount-bounded"), ex => TextBlock($"fallback {ex.Message}")) with { Key = "m5" },
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

    // ── Scenario 5: the re-render re-entrancy cap (TASK-059) through the real wrapper ──
    // A state change climbs from the component through its ancestors to the host. The climb is one
    // request: a chain far deeper than the cap must render. A request that really re-enters (here
    // the host's request sets the state again, synchronously) must still hit the cap, after exactly
    // Max requests, and 200 components deep: counting the climb stops it before the first request
    // reaches the host, and climbing by recursion holds Max x 200 callback frames, enough to
    // overflow the stack. The deep chain runs after the loop, so a counter the throw failed to
    // release would fail it too.

    private sealed class LcNest : Component<int>
    {
        internal static Action<int>? SetLeaf;
        public override Element Render()
        {
            if (Props > 0) return Component<LcNest, int>(Props - 1);
            var (v, set) = UseState(0);
            SetLeaf = set;
            return TextBlock($"leaf {v}");
        }
    }

    internal sealed class RerenderDepthCap(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var target = new Microsoft.UI.Xaml.Controls.Border();
            H.SetContent(target);
            using var host = new ReactorHost(H.Window) { ContentTarget = target };
            try
            {
                string? Change(int value)
                {
                    try { LcNest.SetLeaf!(value); return null; }
                    catch (InvalidOperationException ex) { return ex.Message; }
                }

                // A loop 200 components deep, behind a host whose request re-enters at once.
                using (var reconciler = new Reconciler())
                {
                    var requests = 0;
                    reconciler.Mount(Component<LcNest, int>(200), () => LcNest.SetLeaf!(1000 + ++requests));
                    var loop = Change(1);
                    H.Check("Lifecycle_DepthCap_LoopHitsTheCap",
                        loop?.StartsWith("Render loop detected", StringComparison.Ordinal) == true, $"observed: {loop ?? "no exception"}");
                    H.Check("Lifecycle_DepthCap_LoopCountsRequestsNotWrappers",
                        requests == Microsoft.UI.Reactor.Core.RerenderReentrancy.Max, $"requests={requests}");
                }

                host.Mount(_ => Component<LcNest, int>(200));
                await host.WaitForIdleAsync();
                var deep = Change(2);
                await host.WaitForIdleAsync();
                H.Check("Lifecycle_DepthCap_DeepChainIsNoLoop", deep is null, $"observed: {deep}");
                H.Check("Lifecycle_DepthCap_DeepChainRenders", ContainsText(target, "leaf 2"));
            }
            finally
            {
                H.SetContent(null);
            }
        }

        private static bool ContainsText(Microsoft.UI.Xaml.UIElement root, string text)
        {
            lock (Gate) s_log = [];
            LogOutput(root);
            string output;
            lock (Gate) { output = s_log![0]; s_log = null; }
            return output.Split(';').Contains(text) || output == "out:" + text;
        }
    }

    // ── Goldens, captured on the unchanged reconciler (spec 067 phase 2b, step 0) ──

    private static readonly Dictionary<string, string[]> KindsGoldens = new()
    {
        ["final-output/traced"] =
        [
            "out:root 2;props 1;hidden;always;leaf in-func;memo 0",
        ],
        ["final-output/untraced"] =
        [
            "out:root 2;props 1;hidden;always;leaf in-func;memo 0",
        ],
        ["mount-output/traced"] =
        [
            "out:root 0;props 0;propless 0;leaf in-propless;always;leaf in-func;memo 0",
        ],
        ["mount-output/untraced"] =
        [
            "out:root 0;props 0;propless 0;leaf in-propless;always;leaf in-func;memo 0",
        ],
        ["mount/traced"] =
        [
            "R:Root(0,True)",
            "ev:ComponentRendered(FuncElement,mount)",
            "R:Props(0)",
            "E:Props(0)",
            "ev:ComponentRendered(LcProps,mount)",
            "R:Propless(0)",
            "E:Propless(0)",
            "ev:ComponentRendered(LcPropless,mount)",
            "R:Leaf(in-propless)",
            "E:Leaf(in-propless)",
            "ev:ComponentRendered(LcLeaf,mount)",
            "R:Always",
            "E:Always",
            "ev:ComponentRendered(LcAlways,mount)",
            "R:Func(7)",
            "E:Func",
            "ev:ComponentRendered(FuncElement,mount)",
            "R:Leaf(in-func)",
            "E:Leaf(in-func)",
            "ev:ComponentRendered(LcLeaf,mount)",
            "R:Memo(0)",
            "E:Memo",
            "ev:ComponentRendered(MemoElement,mount)",
            "E:Root(0)",
        ],
        ["mount/untraced"] =
        [
            "R:Root(0,True)",
            "R:Props(0)",
            "E:Props(0)",
            "R:Propless(0)",
            "E:Propless(0)",
            "R:Leaf(in-propless)",
            "E:Leaf(in-propless)",
            "R:Always",
            "E:Always",
            "R:Func(7)",
            "E:Func",
            "R:Leaf(in-func)",
            "E:Leaf(in-func)",
            "R:Memo(0)",
            "E:Memo",
            "E:Root(0)",
        ],
        ["propless-self/traced"] =
        [
            "ev:StateChange(UseState,Int32,changed)",
            "R:Root(2,True)",
            "ev:ComponentRendered(FuncElement,state)",
            "ev:ComponentRenderStart(LcPropless,self)",
            "R:Propless(5)",
            "ev:EffectsFlushStart(LcPropless)",
            "C:Propless",
            "E:Propless(5)",
            "ev:EffectsFlushStop(LcPropless)",
            "ev:ComponentRenderStop(LcPropless)",
            "ev:ComponentRendered(LcPropless,state)",
            "ev:ComponentRenderStart(LcAlways,parent)",
            "R:Always",
            "ev:EffectsFlushStart(LcAlways)",
            "ev:EffectsFlushStop(LcAlways)",
            "ev:ComponentRenderStop(LcAlways)",
            "ev:ComponentRendered(LcAlways,parent)",
            "ev:ComponentRenderStart(FuncElement,parent)",
            "R:Func(7)",
            "ev:EffectsFlushStart(FuncElement)",
            "ev:EffectsFlushStop(FuncElement)",
            "ev:ComponentRenderStop(FuncElement)",
            "ev:ComponentRendered(FuncElement,parent)",
        ],
        ["propless-self/untraced"] =
        [
            "R:Root(2,True)",
            "R:Propless(5)",
            "C:Propless",
            "E:Propless(5)",
            "R:Always",
            "R:Func(7)",
        ],
        ["replace-root/traced"] =
        [
            "C:Root",
            "ev:ComponentRendered(FuncElement,mount)",
            "ev:ComponentUnmount(LcProps)",
            "C:Props",
            "ev:ComponentUnmount(LcAlways)",
            "ev:ComponentUnmount(FuncElement)",
            "ev:ComponentUnmount(LcLeaf)",
            "C:Leaf(in-func)",
            "ev:ComponentUnmount(MemoElement)",
            "C:Memo",
        ],
        ["replace-root/untraced"] =
        [
            "C:Root",
            "C:Props",
            "C:Leaf(in-func)",
            "C:Memo",
        ],
        ["root-1/traced"] =
        [
            "ev:StateChange(UseState,Int32,changed)",
            "R:Root(1,True)",
            "ev:ComponentRendered(FuncElement,state)",
            "ev:ComponentRenderStart(LcAlways,parent)",
            "R:Always",
            "ev:EffectsFlushStart(LcAlways)",
            "ev:EffectsFlushStop(LcAlways)",
            "ev:ComponentRenderStop(LcAlways)",
            "ev:ComponentRendered(LcAlways,parent)",
            "ev:ComponentRenderStart(FuncElement,parent)",
            "R:Func(7)",
            "ev:EffectsFlushStart(FuncElement)",
            "ev:EffectsFlushStop(FuncElement)",
            "ev:ComponentRenderStop(FuncElement)",
            "ev:ComponentRendered(FuncElement,parent)",
            "ev:ComponentRenderStart(MemoElement,parent)",
            "R:Memo(1)",
            "ev:EffectsFlushStart(MemoElement)",
            "C:Memo",
            "E:Memo",
            "ev:EffectsFlushStop(MemoElement)",
            "ev:ComponentRenderStop(MemoElement)",
            "ev:ComponentRendered(MemoElement,props)",
            "C:Root",
            "E:Root(1)",
        ],
        ["root-1/untraced"] =
        [
            "R:Root(1,True)",
            "R:Always",
            "R:Func(7)",
            "R:Memo(1)",
            "C:Memo",
            "E:Memo",
            "C:Root",
            "E:Root(1)",
        ],
        ["root-2/traced"] =
        [
            "ev:StateChange(UseState,Int32,changed)",
            "R:Root(2,True)",
            "ev:ComponentRendered(FuncElement,state)",
            "ev:ComponentRenderStart(LcProps,parent)",
            "R:Props(1)",
            "ev:EffectsFlushStart(LcProps)",
            "C:Props",
            "E:Props(1)",
            "ev:EffectsFlushStop(LcProps)",
            "ev:ComponentRenderStop(LcProps)",
            "ev:ComponentRendered(LcProps,props)",
            "ev:ComponentRenderStart(LcAlways,parent)",
            "R:Always",
            "ev:EffectsFlushStart(LcAlways)",
            "ev:EffectsFlushStop(LcAlways)",
            "ev:ComponentRenderStop(LcAlways)",
            "ev:ComponentRendered(LcAlways,parent)",
            "ev:ComponentRenderStart(FuncElement,parent)",
            "R:Func(7)",
            "ev:EffectsFlushStart(FuncElement)",
            "ev:EffectsFlushStop(FuncElement)",
            "ev:ComponentRenderStop(FuncElement)",
            "ev:ComponentRendered(FuncElement,parent)",
            "ev:ComponentRenderStart(MemoElement,parent)",
            "R:Memo(0)",
            "ev:EffectsFlushStart(MemoElement)",
            "C:Memo",
            "E:Memo",
            "ev:EffectsFlushStop(MemoElement)",
            "ev:ComponentRenderStop(MemoElement)",
            "ev:ComponentRendered(MemoElement,props)",
            "C:Root",
            "E:Root(2)",
        ],
        ["root-2/untraced"] =
        [
            "R:Root(2,True)",
            "R:Props(1)",
            "C:Props",
            "E:Props(1)",
            "R:Always",
            "R:Func(7)",
            "R:Memo(0)",
            "C:Memo",
            "E:Memo",
            "C:Root",
            "E:Root(2)",
        ],
        ["unmount-propless/traced"] =
        [
            "ev:StateChange(UseState,Boolean,changed)",
            "R:Root(2,False)",
            "ev:ComponentRendered(FuncElement,state)",
            "ev:ComponentUnmount(LcPropless)",
            "C:Propless",
            "ev:ComponentUnmount(LcLeaf)",
            "C:Leaf(in-propless)",
            "ev:ComponentRenderStart(LcAlways,parent)",
            "R:Always",
            "ev:EffectsFlushStart(LcAlways)",
            "ev:EffectsFlushStop(LcAlways)",
            "ev:ComponentRenderStop(LcAlways)",
            "ev:ComponentRendered(LcAlways,parent)",
            "ev:ComponentRenderStart(FuncElement,parent)",
            "R:Func(7)",
            "ev:EffectsFlushStart(FuncElement)",
            "ev:EffectsFlushStop(FuncElement)",
            "ev:ComponentRenderStop(FuncElement)",
            "ev:ComponentRendered(FuncElement,parent)",
        ],
        ["unmount-propless/untraced"] =
        [
            "R:Root(2,False)",
            "C:Propless",
            "C:Leaf(in-propless)",
            "R:Always",
            "R:Func(7)",
        ],
    };

    private static readonly Dictionary<string, string[]> ErrorsGoldens = new()
    {
        ["mount-fails-output/traced"] =
        [
            "out:? Render error: InvalidOperationException: render failed render-mount;? Render error: InvalidOperationException: effect failed effect-mount;? Render error: InvalidOperationException: func mount failed;? Render error: InvalidOperationException: memo mount failed;fallback render failed render-mount-bounded;fallback descendant failed",
        ],
        ["mount-fails-output/untraced"] =
        [
            "out:? Render error: InvalidOperationException: render failed render-mount;? Render error: InvalidOperationException: effect failed effect-mount;? Render error: InvalidOperationException: func mount failed;? Render error: InvalidOperationException: memo mount failed;fallback render failed render-mount-bounded;fallback descendant failed",
        ],
        ["mount-fails/traced"] =
        [
            "ev:ComponentRendered(FuncElement,mount)",
            "ev:ComponentUnmount(LcThrower)",
            "ev:ComponentRendered(FuncElement,mount)",
            "ev:RenderError(FuncElement,InvalidOperationException,)",
            "log:Warning:ErrorBoundary caught render error during update[InvalidOperationException]",
            "ev:ComponentUnmount(FuncElement)",
            "ev:ComponentUnmount(LcThrower)",
            "ev:ComponentUnmount(MemoElement)",
            "ev:ComponentUnmount(FuncElement)",
            "ev:ComponentUnmount(LcThrower)",
            "ev:ComponentUnmount(LcThrower)",
            "R:Thrower(render-mount-bounded)",
            "ev:ComponentRendered(LcThrower,mount)",
            "ev:RenderError(LcThrower,InvalidOperationException,)",
            "log:Warning:ErrorBoundary caught render error[InvalidOperationException]",
            "ev:ComponentUnmount(LcThrower)",
            "ev:ComponentRendered(MemoElement,mount)",
            "log:Error:MemoComponent Render() threw during mount[InvalidOperationException]",
            "ev:RenderError(MemoElement,InvalidOperationException,)",
            "ev:ComponentRendered(FuncElement,mount)",
            "log:Error:FuncComponent Render() threw during mount[InvalidOperationException]",
            "ev:RenderError(FuncElement,InvalidOperationException,)",
            "R:Thrower(effect-mount)",
            "E:Thrower(effect-mount)",
            "ev:ComponentRendered(LcThrower,mount)",
            "log:Error:Component Render() threw during mount: LcThrower[InvalidOperationException]",
            "ev:RenderError(LcThrower,InvalidOperationException,)",
            "R:Thrower(render-mount)",
            "ev:ComponentRendered(LcThrower,mount)",
            "log:Error:Component Render() threw during mount: LcThrower[InvalidOperationException]",
            "ev:RenderError(LcThrower,InvalidOperationException,)",
        ],
        ["mount-fails/untraced"] =
        [
            "log:Warning:ErrorBoundary caught render error during update[InvalidOperationException]",
            "R:Thrower(render-mount-bounded)",
            "log:Warning:ErrorBoundary caught render error[InvalidOperationException]",
            "log:Error:MemoComponent Render() threw during mount[InvalidOperationException]",
            "log:Error:FuncComponent Render() threw during mount[InvalidOperationException]",
            "R:Thrower(effect-mount)",
            "E:Thrower(effect-mount)",
            "log:Error:Component Render() threw during mount: LcThrower[InvalidOperationException]",
            "R:Thrower(render-mount)",
            "log:Error:Component Render() threw during mount: LcThrower[InvalidOperationException]",
        ],
        ["mount/traced"] =
        [
            "R:Root(0)",
            "ev:ComponentRendered(FuncElement,mount)",
            "R:Thrower(render-loose 0)",
            "E:Thrower(render-loose 0)",
            "ev:ComponentRendered(LcThrower,mount)",
            "R:Thrower(effect-loose 0)",
            "E:Thrower(effect-loose 0)",
            "ev:ComponentRendered(LcThrower,mount)",
            "R:FuncThrower",
            "ev:ComponentRendered(FuncElement,mount)",
            "R:MemoThrower",
            "ev:ComponentRendered(MemoElement,mount)",
            "R:Thrower(render-bounded 0)",
            "E:Thrower(render-bounded 0)",
            "ev:ComponentRendered(LcThrower,mount)",
            "R:Thrower(effect-bounded 0)",
            "E:Thrower(effect-bounded 0)",
            "ev:ComponentRendered(LcThrower,mount)",
        ],
        ["mount/untraced"] =
        [
            "R:Root(0)",
            "R:Thrower(render-loose 0)",
            "E:Thrower(render-loose 0)",
            "R:Thrower(effect-loose 0)",
            "E:Thrower(effect-loose 0)",
            "R:FuncThrower",
            "R:MemoThrower",
            "R:Thrower(render-bounded 0)",
            "E:Thrower(render-bounded 0)",
            "R:Thrower(effect-bounded 0)",
            "E:Thrower(effect-bounded 0)",
        ],
        ["recover-output/traced"] =
        [
            "out:thrower render-loose 2;thrower effect-loose 2;func ok;memo ok;bounded 2;thrower render-bounded 2;thrower effect-bounded 2",
        ],
        ["recover-output/untraced"] =
        [
            "out:thrower render-loose 2;thrower effect-loose 2;func ok;memo ok;bounded 2;thrower render-bounded 2;thrower effect-bounded 2",
        ],
        ["recover/traced"] =
        [
            "ev:StateChange(UseState,Int32,changed)",
            "R:Root(2)",
            "ev:ComponentRendered(FuncElement,state)",
            "ev:ComponentRenderStart(LcThrower,parent)",
            "R:Thrower(render-loose 2)",
            "ev:EffectsFlushStart(LcThrower)",
            "E:Thrower(render-loose 2)",
            "ev:EffectsFlushStop(LcThrower)",
            "ev:ComponentRenderStop(LcThrower)",
            "ev:ComponentRendered(LcThrower,props)",
            "ev:ComponentRenderStart(LcThrower,parent)",
            "R:Thrower(effect-loose 2)",
            "ev:EffectsFlushStart(LcThrower)",
            "E:Thrower(effect-loose 2)",
            "ev:EffectsFlushStop(LcThrower)",
            "ev:ComponentRenderStop(LcThrower)",
            "ev:ComponentRendered(LcThrower,props)",
            "ev:ComponentRenderStart(FuncElement,parent)",
            "R:FuncThrower",
            "ev:EffectsFlushStart(FuncElement)",
            "ev:EffectsFlushStop(FuncElement)",
            "ev:ComponentRenderStop(FuncElement)",
            "ev:ComponentRendered(FuncElement,parent)",
            "ev:ComponentRenderStart(MemoElement,parent)",
            "R:MemoThrower",
            "ev:EffectsFlushStart(MemoElement)",
            "ev:EffectsFlushStop(MemoElement)",
            "ev:ComponentRenderStop(MemoElement)",
            "ev:ComponentRendered(MemoElement,props)",
            "R:Thrower(render-bounded 2)",
            "E:Thrower(render-bounded 2)",
            "ev:ComponentRendered(LcThrower,mount)",
            "R:Thrower(effect-bounded 2)",
            "E:Thrower(effect-bounded 2)",
            "ev:ComponentRendered(LcThrower,mount)",
        ],
        ["recover/untraced"] =
        [
            "R:Root(2)",
            "R:Thrower(render-loose 2)",
            "E:Thrower(render-loose 2)",
            "R:Thrower(effect-loose 2)",
            "E:Thrower(effect-loose 2)",
            "R:FuncThrower",
            "R:MemoThrower",
            "R:Thrower(render-bounded 2)",
            "E:Thrower(render-bounded 2)",
            "R:Thrower(effect-bounded 2)",
            "E:Thrower(effect-bounded 2)",
        ],
        ["update-fails-output/traced"] =
        [
            "out:? Render error: InvalidOperationException: render failed render-loose 1;? Render error: InvalidOperationException: effect failed effect-loose 1;? Render error: InvalidOperationException: func failed;? Render error: InvalidOperationException: memo failed;fallback render failed render-bounded 1;fallback effect failed effect-bounded 1",
        ],
        ["update-fails-output/untraced"] =
        [
            "out:? Render error: InvalidOperationException: render failed render-loose 1;? Render error: InvalidOperationException: effect failed effect-loose 1;? Render error: InvalidOperationException: func failed;? Render error: InvalidOperationException: memo failed;fallback render failed render-bounded 1;fallback effect failed effect-bounded 1",
        ],
        ["update-fails/traced"] =
        [
            "ev:StateChange(UseState,Int32,changed)",
            "R:Root(1)",
            "ev:ComponentRendered(FuncElement,state)",
            "ev:ComponentRenderStart(LcThrower,parent)",
            "R:Thrower(render-loose 1)",
            "ev:ComponentRendered(LcThrower,props)",
            "log:Error:Component Render() threw: LcThrower[InvalidOperationException]",
            "ev:RenderError(LcThrower,InvalidOperationException,)",
            "ev:ComponentRenderStop(LcThrower)",
            "ev:ComponentRenderStart(LcThrower,parent)",
            "R:Thrower(effect-loose 1)",
            "ev:EffectsFlushStart(LcThrower)",
            "E:Thrower(effect-loose 1)",
            "ev:EffectsFlushStop(LcThrower)",
            "ev:ComponentRendered(LcThrower,props)",
            "log:Error:Component Render() threw: LcThrower[InvalidOperationException]",
            "ev:RenderError(LcThrower,InvalidOperationException,)",
            "ev:ComponentRenderStop(LcThrower)",
            "ev:ComponentRenderStart(FuncElement,parent)",
            "R:FuncThrower",
            "ev:ComponentRendered(FuncElement,parent)",
            "log:Error:Component Render() threw: FuncElement[InvalidOperationException]",
            "ev:RenderError(FuncElement,InvalidOperationException,)",
            "ev:ComponentRenderStop(FuncElement)",
            "ev:ComponentRenderStart(MemoElement,parent)",
            "R:MemoThrower",
            "ev:ComponentRendered(MemoElement,props)",
            "log:Error:Component Render() threw: MemoElement[InvalidOperationException]",
            "ev:RenderError(MemoElement,InvalidOperationException,)",
            "ev:ComponentRenderStop(MemoElement)",
            "ev:ComponentRenderStart(LcThrower,parent)",
            "R:Thrower(render-bounded 1)",
            "ev:ComponentRendered(LcThrower,props)",
            "ev:RenderError(LcThrower,InvalidOperationException,)",
            "log:Warning:ErrorBoundary caught render error during update[InvalidOperationException]",
            "ev:ComponentUnmount(LcThrower)",
            "ev:ComponentRenderStart(LcThrower,parent)",
            "R:Thrower(effect-bounded 1)",
            "ev:EffectsFlushStart(LcThrower)",
            "E:Thrower(effect-bounded 1)",
            "ev:EffectsFlushStop(LcThrower)",
            "ev:ComponentRendered(LcThrower,props)",
            "ev:RenderError(LcThrower,InvalidOperationException,)",
            "log:Warning:ErrorBoundary caught render error during update[InvalidOperationException]",
            "ev:ComponentUnmount(LcThrower)",
        ],
        ["update-fails/untraced"] =
        [
            "R:Root(1)",
            "R:Thrower(render-loose 1)",
            "log:Error:Component Render() threw: LcThrower[InvalidOperationException]",
            "R:Thrower(effect-loose 1)",
            "E:Thrower(effect-loose 1)",
            "log:Error:Component Render() threw: LcThrower[InvalidOperationException]",
            "R:FuncThrower",
            "log:Error:Component Render() threw: FuncElement[InvalidOperationException]",
            "R:MemoThrower",
            "log:Error:Component Render() threw: MemoElement[InvalidOperationException]",
            "R:Thrower(render-bounded 1)",
            "log:Warning:ErrorBoundary caught render error during update[InvalidOperationException]",
            "R:Thrower(effect-bounded 1)",
            "E:Thrower(effect-bounded 1)",
            "log:Warning:ErrorBoundary caught render error during update[InvalidOperationException]",
        ],
    };

    private static readonly Dictionary<string, string[]> HotReloadGoldens = new()
    {
        ["hot-reload-output/traced"] =
        [
            "out:shape 1;leaf sibling",
        ],
        ["hot-reload-output/untraced"] =
        [
            "out:shape 1;leaf sibling",
        ],
        ["hot-reload/traced"] =
        [
            "ev:ComponentRendered(FuncElement,hotReload)",
            "ev:ComponentRenderStart(LcReshaped,self)",
            "R:Reshaped(1)",
            "ev:ComponentRendered(LcReshaped,hotReload)",
            "log:Warning:Hot reload: hook order/type changed in child component ? resetting state and re-rendering: LcReshaped[HookOrderException]",
            "C:Reshaped0",
            "R:Reshaped(1)",
            "ev:EffectsFlushStart(LcReshaped)",
            "E:Reshaped1",
            "ev:EffectsFlushStop(LcReshaped)",
            "ev:ComponentRenderStop(LcReshaped)",
            "ev:ComponentRendered(LcReshaped,hotReload)",
            "ev:ComponentRenderStart(LcLeaf,self)",
            "R:Leaf(sibling)",
            "ev:EffectsFlushStart(LcLeaf)",
            "ev:EffectsFlushStop(LcLeaf)",
            "ev:ComponentRenderStop(LcLeaf)",
            "ev:ComponentRendered(LcLeaf,hotReload)",
        ],
        ["hot-reload/untraced"] =
        [
            "R:Reshaped(1)",
            "log:Warning:Hot reload: hook order/type changed in child component ? resetting state and re-rendering: LcReshaped[HookOrderException]",
            "C:Reshaped0",
            "R:Reshaped(1)",
            "E:Reshaped1",
            "R:Leaf(sibling)",
        ],
        ["mount/traced"] =
        [
            "ev:ComponentRendered(FuncElement,mount)",
            "R:Reshaped(0)",
            "E:Reshaped0",
            "ev:ComponentRendered(LcReshaped,mount)",
            "R:Leaf(sibling)",
            "E:Leaf(sibling)",
            "ev:ComponentRendered(LcLeaf,mount)",
        ],
        ["mount/untraced"] =
        [
            "R:Reshaped(0)",
            "E:Reshaped0",
            "R:Leaf(sibling)",
            "E:Leaf(sibling)",
        ],
    };

    private static readonly Dictionary<string, string[]> RerenderGoldens = new()
    {
        ["batched-on-ui/traced"] =
        [
            "ev:StateChange(UseState,Int32,changed)",
            "ev:StateChange(UseState,Int32,changed)",
            "R:Root(3,ui)",
            "ev:ComponentRendered(FuncElement,state)",
            "ev:ComponentRenderStart(FuncElement,self)",
            "R:Child(3,ui)",
            "ev:EffectsFlushStart(FuncElement)",
            "ev:EffectsFlushStop(FuncElement)",
            "ev:ComponentRenderStop(FuncElement)",
            "ev:ComponentRendered(FuncElement,state)",
        ],
        ["batched-on-ui/untraced"] =
        [
            "R:Root(3,ui)",
            "R:Child(3,ui)",
        ],
        ["child-from-worker/traced"] =
        [
            "ev:StateChange(UseState,Int32,changed)",
            "R:Root(0,ui)",
            "ev:ComponentRendered(FuncElement,state)",
            "ev:ComponentRenderStart(FuncElement,self)",
            "R:Child(1,ui)",
            "ev:EffectsFlushStart(FuncElement)",
            "ev:EffectsFlushStop(FuncElement)",
            "ev:ComponentRenderStop(FuncElement)",
            "ev:ComponentRendered(FuncElement,state)",
        ],
        ["child-from-worker/untraced"] =
        [
            "R:Root(0,ui)",
            "R:Child(1,ui)",
        ],
        ["mount/traced"] =
        [
            "R:Root(0,ui)",
            "ev:ComponentRendered(FuncElement,mount)",
            "R:Child(0,ui)",
            "ev:ComponentRendered(FuncElement,mount)",
        ],
        ["mount/untraced"] =
        [
            "R:Root(0,ui)",
            "R:Child(0,ui)",
        ],
    };

}
