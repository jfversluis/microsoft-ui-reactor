using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using static Microsoft.UI.Reactor.CoreFactories;

namespace Microsoft.UI.Reactor.Core.Tests;

/// <summary>
/// The navigation model the renderers share (spec 069 §2.2–§2.3), driven through the neutral harness:
/// the core's stack and lifecycle hook state, and a handle over an injected scheduler.
/// </summary>
public class NavigationModelTests
{
    private sealed class Recorder
    {
        public List<string> Log { get; } = [];
        public HashSet<string> Refuse { get; } = [];

        public NavigationStack<string> Stack(string initial = "home")
        {
            var stack = new NavigationStack<string>(initial)
            {
                OnChanged = () => Log.Add("changed"),
                Guard = c =>
                {
                    Log.Add($"guard {c.Route}>{c.TargetRoute} {c.Mode}");
                    if (Refuse.Contains("guard")) c.Cancel();
                },
                LifecycleGuard = c =>
                {
                    Log.Add($"lifecycle {c.Route}>{c.TargetRoute} {c.Mode}");
                    if (Refuse.Contains("lifecycle")) c.Cancel();
                },
            };
            return stack;
        }

        public string Take()
        {
            var text = string.Join("; ", Log);
            Log.Clear();
            return text;
        }
    }

    private static string Show(NavigationStack<string> stack) =>
        $"[{string.Join(",", stack.BackStack)}] {stack.Current} [{string.Join(",", stack.ForwardStack)}]";

    [Fact]
    public void Operations_MoveRoutesBetweenTheStacks_AndRunTheLifecycleGuardFirst()
    {
        var r = new Recorder();
        var stack = r.Stack();

        Assert.True(stack.Push("a"));
        Assert.True(stack.Push("b"));
        Assert.Equal("[home,a] b []", Show(stack));
        Assert.Equal("lifecycle home>a Push; guard home>a Push; changed; lifecycle a>b Push; guard a>b Push; changed", r.Take());

        Assert.True(stack.Pop());
        Assert.Equal("[home] a [b]", Show(stack));
        Assert.True(stack.Forward());
        Assert.Equal("[home,a] b []", Show(stack));
        Assert.True(stack.Replace("c"));
        Assert.Equal("[home,a] c []", Show(stack));
        Assert.True(stack.PopTo(route => route == "home"));
        Assert.Equal("[] home [c,a]", Show(stack));
        Assert.True(stack.Reset("z"));
        Assert.Equal("[] z []", Show(stack));
        Assert.Equal(
            "lifecycle b>a Pop; guard b>a Pop; changed; lifecycle a>b Forward; guard a>b Forward; changed; "
            + "lifecycle b>c Replace; guard b>c Replace; changed; lifecycle c>home Pop; guard c>home Pop; changed; "
            + "lifecycle home>z Reset; guard home>z Reset; changed",
            r.Take());
    }

    [Fact]
    public void NothingToGoTo_ReturnsFalse_WithoutAskingTheGuards()
    {
        var r = new Recorder();
        var stack = r.Stack();

        Assert.False(stack.Pop());
        Assert.False(stack.Forward());
        Assert.False(stack.PopTo(_ => true));
        Assert.Empty(r.Log);
    }

    [Fact]
    public void ACancellingGuard_ChangesNothing_AndTheLifecycleGuardCanStopTheOtherOne()
    {
        var r = new Recorder();
        var stack = r.Stack();
        stack.Push("a");
        r.Take();

        r.Refuse.Add("lifecycle");
        Assert.False(stack.Push("b"));
        Assert.Equal("lifecycle a>b Push", r.Take());

        r.Refuse.Clear();
        r.Refuse.Add("guard");
        Assert.False(stack.Pop());
        Assert.False(stack.Replace("x"));
        Assert.False(stack.Reset("x"));
        Assert.Equal("[home] a []", Show(stack));
        Assert.DoesNotContain("changed", r.Take());
    }

    [Fact]
    public void PopToIndex_PopsToThatPosition_AndRejectsPositionsOutsideTheBackStack()
    {
        var r = new Recorder();
        var stack = r.Stack();
        stack.Push("a");
        stack.Push("b");
        stack.Push("c");
        r.Take();

        Assert.False(stack.PopToIndex(-1));
        Assert.False(stack.PopToIndex(3));
        Assert.Empty(r.Log);

        Assert.True(stack.PopToIndex(1));
        Assert.Equal("[home] a [c,b]", Show(stack));
        Assert.Equal("lifecycle c>a Pop; guard c>a Pop; changed", r.Take());

        r.Refuse.Add("guard");
        Assert.False(stack.PopToIndex(0));
        Assert.Equal("[home] a [c,b]", Show(stack));
    }

    [Fact]
    public void RestoreState_RunsNoGuard_AndDetachDropsEveryDelegate()
    {
        var r = new Recorder();
        var stack = r.Stack();

        stack.RestoreState(["home", "a"], "b", ["c"]);
        Assert.Equal("[home,a] b [c]", Show(stack));
        Assert.Equal("changed", r.Take());

        stack.Detach();
        Assert.True(stack.Push("d"));
        Assert.Empty(r.Log);
    }

    [Fact]
    public void TheLifecycleHook_IsRegisteredInHookOrder_AndRefreshedEveryRender()
    {
        var context = new RenderContext();
        var calls = new List<string>();

        context.BeginRender(static () => { });
        context.UseState(0);
        NavigationLifecycleHooks.Use(context, onNavigatedTo: _ => calls.Add("first"));
        context.BeginRender(static () => { });
        context.UseState(0);
        NavigationLifecycleHooks.Use(context, onNavigatedTo: _ => calls.Add("second"));

        var hook = context.GetNavigationLifecycleHook();
        Assert.NotNull(hook);
        hook.OnNavigatedTo!(null!);
        Assert.Equal(["second"], calls);
        Assert.Null(hook.OnNavigatingFrom);

        // The hook where a state hook was: hooks must keep their order.
        context.BeginRender(static () => { });
        Assert.Throws<HookOrderException>(() => NavigationLifecycleHooks.Use(context));
    }

    [Fact]
    public void AComponentWithoutTheHook_HasNone()
    {
        var context = new RenderContext();
        context.BeginRender(static () => { });
        context.UseState(0);

        Assert.Null(context.GetNavigationLifecycleHook());
    }

    private sealed class LifecycleComponent : Component<List<RenderContext>>
    {
        public override Element Render()
        {
            UseNavigationLifecycle(onNavigatingFrom: static c => c.Cancel());
            Props.Add(Context);
            return new TextElement("page");
        }
    }

    [Fact]
    public void AComponentRenderedByTheEngine_ExposesItsHook()
    {
        var contexts = new List<RenderContext>();
        new TestHost().Render(Component<LifecycleComponent, List<RenderContext>>(contexts));

        var hook = Assert.Single(contexts).GetNavigationLifecycleHook();
        var context = new NavigatingFromContext("a", "b", NavigationMode.Push);
        hook!.OnNavigatingFrom!(context);
        Assert.True(context.IsCancelled);
    }

    [Fact]
    public void AHandle_RunsOffThreadCallsWholeOnItsInjectedScheduler()
    {
        var queue = new Queue<Action>();
        var nav = new NavigationHandle<string>(new NavigationStack<string>("home"), work =>
        {
            lock (queue) queue.Enqueue(work);
            return true;
        });
        var events = new List<string>();
        nav.Navigated += e => events.Add($"{e.Mode} {e.PreviousRoute}>{e.Route}");

        var scheduled = false;
        var worker = new Thread(() => scheduled = nav.Navigate("a") && nav.Reset("z"));
        worker.Start();
        worker.Join();

        Assert.True(scheduled);
        Assert.Equal("home", nav.CurrentRoute);
        Assert.Empty(events);
        while (queue.TryDequeue(out var work)) work();
        Assert.Equal("z", nav.CurrentRoute);
        Assert.Equal(["Push home>a", "Reset a>z"], events);
    }

    [Fact]
    public void AHandle_ThrowsOffThread_WhenItsSchedulerRefuses_OrThereIsNone()
    {
        var refusing = new NavigationHandle<string>(new NavigationStack<string>("home"), static _ => false);
        var orphan = new NavigationHandle<string>(new NavigationStack<string>("home"));
        Exception? refused = null, none = null;
        var worker = new Thread(() =>
        {
            refused = Record.Exception(() => refusing.GoBack());
            none = Record.Exception(() => orphan.Navigate("a"));
        });
        worker.Start();
        worker.Join();

        Assert.Contains("refused", Assert.IsType<InvalidOperationException>(refused).Message);
        Assert.Contains("no UI dispatcher", Assert.IsType<InvalidOperationException>(none).Message);
        Assert.Equal("home", orphan.CurrentRoute);
    }
}
