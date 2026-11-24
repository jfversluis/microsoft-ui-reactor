using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Core;

/// <summary>
/// Spec 067 §11.2: the component render kernel, driven through a fake renderer host with no WinUI
/// types. Each test pins one arm: success, props installation per mode, the in-tree fallback for
/// render and effect failures, rethrow under a boundary or for fatal errors, propagation, and the
/// hot-reload hook-order retry (update only).
/// </summary>
public class NeutralComponentRenderKernelTests
{
    private sealed record Leaf(string Text) : ReactorElement;

    private sealed record ComponentEl(object? Props) : ReactorElement, IComponentElement;

    private sealed record FuncEl(Func<RenderContext, ReactorElement?> Body) : ReactorElement, IRenderFunctionElement
    {
        public ReactorElement? Render(RenderContext context) => Body(context);
    }

    private sealed class PropsComponent(Func<PropsComponent, ReactorElement?> body) : ReactorComponent, IPropsReceiver
    {
        public string? Props;
        public override ReactorElement? Render() => body(this);
        public void SetProps(object props) => Props = (string)props;
        public object? PropsBoxed => Props;
        public Type PropsType => typeof(string);
    }

    private struct FakeHost : IComponentRenderHost
    {
        public List<string> Log;
        public bool Boundary;
        public Exception? Propagating;
        public bool Observes;
        public Action<RenderContext>? EffectBody;

        public ReactorElement? Render(ReactorComponent? component, IRenderFunctionElement? function, RenderContext context)
        {
            Log.Add(component is not null ? "render:component" : "render:function");
            return component is not null ? component.Render() : function!.Render(context);
        }

        public void FlushEffects(RenderContext context)
        {
            Log.Add("effects");
            EffectBody?.Invoke(context);
        }

        public bool UnderBoundary => Boundary;
        public bool IsPropagating(Exception error) => ReferenceEquals(error, Propagating);
        public void OnRethrow(Exception error) => Log.Add($"rethrow:{error.Message}");
        public ReactorElement OnFallback(Exception error, bool inEffects)
        {
            Log.Add($"fallback:{error.Message}:{(inEffects ? "effects" : "render")}");
            return new Leaf("fallback");
        }
        public bool ObservesUnhandled => Observes;
        public void OnUnhandled(Exception error) => Log.Add($"unhandled:{error.Message}");
        public void OnHookOrderRetry(HookOrderException error) => Log.Add("retry");
        public void OnHookOrderReset() => Log.Add("reset");
    }

    private static FakeHost NewHost() => new() { Log = [] };

    private static bool Render(ref FakeHost host, ComponentNode node, ReactorElement element, ComponentRenderMode mode,
        out ReactorElement? rendered)
        => ComponentRenderKernel.TryRender(ref host, node, element, mode, () => { }, new ContextScope(), owner: null, out rendered);

    [Fact]
    public void Success_RendersThenFlushesEffects()
    {
        var host = NewHost();
        var node = new ComponentNode { Context = new RenderContext() };
        Assert.True(Render(ref host, node, new FuncEl(_ => new Leaf("ok")), ComponentRenderMode.Mount, out var rendered));
        Assert.Equal(new Leaf("ok"), rendered);
        Assert.Equal(["render:function", "effects"], host.Log);
    }

    [Fact]
    public void UpdateInstallsPropsInsideTheRender_MountLeavesThemToTheRenderer()
    {
        var component = new PropsComponent(c => new Leaf(c.Props ?? "none"));
        var node = new ComponentNode { Component = component };

        var host = NewHost();
        Render(ref host, node, new ComponentEl("p1"), ComponentRenderMode.Mount, out var mounted);
        Assert.Equal(new Leaf("none"), mounted);

        Render(ref host, node, new ComponentEl("p2"), ComponentRenderMode.Update, out var updated);
        Assert.Equal(new Leaf("p2"), updated);
    }

    [Fact]
    public void AFailureOutsideEveryBoundary_RendersTheFallback_NamingTheFailingPhase()
    {
        var host = NewHost();
        var throwsInRender = new FuncEl(_ => throw new InvalidOperationException("r"));
        Render(ref host, new ComponentNode { Context = new RenderContext() }, throwsInRender, ComponentRenderMode.Update, out var fromRender);

        var effectHost = NewHost();
        effectHost.EffectBody = _ => throw new InvalidOperationException("e");
        Render(ref effectHost, new ComponentNode { Context = new RenderContext() }, new FuncEl(_ => new Leaf("ok")),
            ComponentRenderMode.Mount, out var fromEffects);

        Assert.Equal(new Leaf("fallback"), fromRender);
        Assert.Equal(["render:function", "fallback:r:render"], host.Log);
        Assert.Equal(new Leaf("fallback"), fromEffects);
        Assert.Equal(["render:function", "effects", "fallback:e:effects"], effectHost.Log);
    }

    [Fact]
    public void UnderABoundary_OrForAFatalError_TheFailureIsReportedAndRethrown()
    {
        var host = NewHost();
        host.Boundary = true;
        Assert.Throws<InvalidOperationException>(() =>
        {
            var h = host;
            Render(ref h, new ComponentNode { Context = new RenderContext() },
                new FuncEl(_ => throw new InvalidOperationException("b")), ComponentRenderMode.Mount, out _);
            host = h;
        });

        var fatalHost = NewHost();
        Assert.Throws<OutOfMemoryException>(() =>
        {
            var h = fatalHost;
            try
            {
                Render(ref h, new ComponentNode { Context = new RenderContext() },
                    new FuncEl(_ => throw new OutOfMemoryException("oom")), ComponentRenderMode.Mount, out _);
            }
            finally { fatalHost = h; }
        });
        Assert.Equal(["render:function", "rethrow:oom"], fatalHost.Log);
    }

    [Fact]
    public void APropagatingError_PassesThroughUnreported_SeenOnlyByAnObservingHost()
    {
        var error = new InvalidOperationException("p");
        var host = NewHost();
        host.Propagating = error;
        host.Observes = true;
        var log = host.Log;
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() =>
        {
            var h = host;
            Render(ref h, new ComponentNode { Context = new RenderContext() }, new FuncEl(_ => throw error),
                ComponentRenderMode.Update, out _);
        }));
        Assert.Equal(["render:function", "unhandled:p"], log);
    }

    [Fact]
    public void AHookOrderChangeDuringAHotReloadPass_ResetsAndRendersOnce_OnUpdateOnly()
    {
        var shape = 0;
        ReactorElement? Body(RenderContext ctx)
        {
            if (shape == 0) ctx.UseState(1);
            else ctx.UseRef("now a ref");
            return new Leaf($"shape {shape}");
        }

        var context = new RenderContext();
        var node = new ComponentNode { Context = context };
        var host = NewHost();
        Render(ref host, node, new FuncEl(Body), ComponentRenderMode.Mount, out _);

        shape = 1;
        HotReloadState.WithinUpdatePass = true;
        try
        {
            host.Log.Clear();
            Render(ref host, node, new FuncEl(Body), ComponentRenderMode.Update, out var retried);
            Assert.Equal(new Leaf("shape 1"), retried);
            Assert.Equal(["render:function", "retry", "reset", "render:function", "effects"], host.Log);

            // The same change on a mount (a fresh context with the old shape) is not retried.
            shape = 0;
            var mountContext = new RenderContext();
            var mountNode = new ComponentNode { Context = mountContext };
            Render(ref host, mountNode, new FuncEl(Body), ComponentRenderMode.Mount, out _);
            shape = 1;
            host.Log.Clear();
            Render(ref host, mountNode, new FuncEl(Body), ComponentRenderMode.Mount, out var notRetried);
            Assert.Equal(new Leaf("fallback"), notRetried);
            Assert.DoesNotContain("retry", host.Log);
        }
        finally { HotReloadState.WithinUpdatePass = false; }

        // Outside a pass, an update doesn't retry either.
        var outside = new ComponentNode { Context = new RenderContext() };
        shape = 0;
        Render(ref host, outside, new FuncEl(Body), ComponentRenderMode.Mount, out _);
        shape = 1;
        host.Log.Clear();
        Render(ref host, outside, new FuncEl(Body), ComponentRenderMode.Update, out var noPass);
        Assert.Equal(new Leaf("fallback"), noPass);
    }

    [Fact]
    public void AHotReloadRetryThatFailsAgain_FallsBackInsteadOfLooping()
    {
        var host = NewHost();
        var node = new ComponentNode { Context = new RenderContext() };
        HotReloadState.WithinUpdatePass = true;
        try
        {
            Render(ref host, node, new FuncEl(_ => throw new HookOrderException("always")), ComponentRenderMode.Update,
                out var rendered);
            Assert.Equal(new Leaf("fallback"), rendered);
        }
        finally { HotReloadState.WithinUpdatePass = false; }
        Assert.Equal(["render:function", "retry", "reset", "render:function", "fallback:always:render"], host.Log);
    }

    [Fact]
    public void ANodeWithNothingToRender_RendersNothing()
    {
        var host = NewHost();
        Assert.False(Render(ref host, new ComponentNode { Context = new RenderContext() }, new Leaf("not a function"),
            ComponentRenderMode.Update, out var rendered));
        Assert.Null(rendered);
        Assert.Empty(host.Log);
        Assert.False(Render(ref host, new ComponentNode(), new FuncEl(_ => new Leaf("x")), ComponentRenderMode.Update, out _));
    }

    [Fact]
    public void RerenderReentrancy_ThrowsPastTheCap()
    {
        for (var i = 0; i < RerenderReentrancy.Max; i++) RerenderReentrancy.Enter();
        try { Assert.Throws<InvalidOperationException>(RerenderReentrancy.ThrowIfTooDeep); }
        finally { for (var i = 0; i < RerenderReentrancy.Max; i++) RerenderReentrancy.Exit(); }
        RerenderReentrancy.ThrowIfTooDeep();
    }
}
