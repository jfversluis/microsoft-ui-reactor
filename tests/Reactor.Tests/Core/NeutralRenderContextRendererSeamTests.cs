using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Core;

/// <summary>
/// The render-context seams a renderer with deferred effects and a per-root render thread needs
/// (MAUI, spec 067 §13): effects scheduled more than once before a flush, the hook-count check,
/// and off-thread setters marshalled through the context's own render thread.
/// </summary>
public class NeutralRenderContextRendererSeamTests
{
    private static void Render(RenderContext context, int value, List<string> log, Action? rerender = null)
    {
        context.BeginRender(rerender ?? (() => { }));
        context.UseEffect(() =>
        {
            log.Add($"run {value}");
            return () => log.Add($"cleanup {value}");
        }, value);
    }

    [Fact]
    public void AnEffectScheduledTwiceBeforeTheFlush_KeepsTheCommittedCleanup_AndRunsTheLatestEffect()
    {
        var context = new RenderContext();
        var log = new List<string>();
        Render(context, 1, log);
        context.FlushEffects();

        Render(context, 2, log);
        Render(context, 3, log);
        Assert.True(context.HasPendingEffects);
        context.FlushEffects();

        Assert.Equal(["run 1", "cleanup 1", "run 3"], log);
        Assert.False(context.HasPendingEffects);
        log.Clear();
        context.RunCleanups();
        Assert.Equal(["cleanup 3"], log);
    }

    [Fact]
    public void ACleanupThatThrowsDuringTheFlush_IsNotRunAgainAtUnmount()
    {
        var context = new RenderContext();
        var runs = 0;
        void RenderWith(int value)
        {
            context.BeginRender(() => { });
            context.UseEffect(() => () =>
            {
                runs++;
                throw new InvalidOperationException("cleanup failed");
            }, value);
        }

        RenderWith(1);
        context.FlushEffects();
        RenderWith(2);
        Assert.Throws<InvalidOperationException>(context.FlushEffects);
        Assert.Equal(1, runs);

        // The second effect never ran, so only its staged cleanup is gone: nothing runs again.
        context.RunCleanupsIsolated(_ => { });
        Assert.Equal(1, runs);
    }

    [Fact]
    public void ValidateHookCount_RejectsADifferentCount_UntilTheHookStateIsCleared()
    {
        var context = new RenderContext();
        void RenderHooks(int count)
        {
            context.BeginRender(() => { });
            for (var i = 0; i < count; i++) context.UseRef(i);
        }

        RenderHooks(2);
        context.ValidateHookCount();
        RenderHooks(2);
        context.ValidateHookCount();

        RenderHooks(3);
        Assert.Throws<HookOrderException>(context.ValidateHookCount);
        RenderHooks(1);
        Assert.Throws<HookOrderException>(context.ValidateHookCount);

        context.ClearHookState();
        RenderHooks(1);
        context.ValidateHookCount();
    }

    [Fact]
    public void AnOffThreadSetter_MarshalsThroughTheContextsRenderThread()
    {
        var context = new RenderContext();
        var queued = new List<Action>();
        context.RenderThreadEnqueue = work => { lock (queued) queued.Add(work); return true; };
        var renders = 0;
        context.BeginRender(() => renders++);
        var (_, set) = context.UseState(0);

        // A dedicated thread: Task.Wait may run a not-yet-started task inline on this thread.
        var worker = new Thread(() => set(1));
        worker.Start();
        worker.Join();
        Assert.Single(queued);
        Assert.Equal(0, renders);

        queued[0]();
        Assert.Equal(1, renders);
        context.BeginRender(() => renders++);
        Assert.Equal(1, context.UseState(0).Value);
    }
}
