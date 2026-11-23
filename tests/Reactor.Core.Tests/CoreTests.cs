using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using static Microsoft.UI.Reactor.CoreFactories;

namespace Microsoft.UI.Reactor.Core.Tests;

public sealed class Probe
{
    public Dictionary<string, Action<int>> Setters { get; } = [];
    public Dictionary<string, int> Renders { get; } = [];
    public List<string> Log { get; } = [];

    public void CountRender(string name) => Renders[name] = Renders.GetValueOrDefault(name) + 1;
}

public sealed record CounterProps(string Label, Probe Probe);

public sealed class Counter : Component<CounterProps>
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        Props.Probe.Setters[Props.Label] = setCount;
        Props.Probe.CountRender(Props.Label);
        UseEffect(() => () => Props.Probe.Log.Add($"cleanup {Props.Label}"));
        return new TextElement($"{Props.Label}:{count}");
    }
}

public sealed record ListProps(string[] Labels, Probe Probe);

public sealed class KeyedList : Component<ListProps>
{
    public override Element Render() =>
        new BoxElement([.. Props.Labels.Select(label => Component<Counter, CounterProps>(new(label, Props.Probe)).WithKey(label))]);
}

public class ReconcilerTests
{
    [Fact]
    public void Mount_BuildsTheNativeTree_ThroughComponentsAndGroups()
    {
        var host = new TestHost().Render(
            new BoxElement(
                new TextElement("a"),
                Group(new TextElement("b"), null, new TextElement("c")),
                RenderEachTime(_ => new TextElement("d"))));

        Assert.Equal("box", host.RootView!.Kind);
        Assert.Equal(["a", "b", "c", "d"], host.RootView.Children.Select(v => v.Text));
    }

    [Fact]
    public void KeyedReorder_KeepsComponentStateAndNativeControls()
    {
        var probe = new Probe();
        var host = new TestHost().Render(Component<KeyedList, ListProps>(new(["a", "b", "c"], probe)));
        probe.Setters["b"](5);
        host.Scheduler.Drain();
        var before = host.RootView!.Children.ToList();
        var mountedBefore = host.Renderer.Mounted.Count;

        host.Render(Component<KeyedList, ListProps>(new(["c", "b", "a"], probe)));

        Assert.Equal(["c:0", "b:5", "a:0"], host.RootView.Children.Select(v => v.Text));
        Assert.Same(before[2], host.RootView.Children[0]);
        Assert.Same(before[1], host.RootView.Children[1]);
        Assert.Same(before[0], host.RootView.Children[2]);
        Assert.Equal(mountedBefore, host.Renderer.Mounted.Count);
    }

    [Fact]
    public void RemovingAKeyedChild_UnmountsItAndRunsItsEffectCleanup()
    {
        var probe = new Probe();
        var host = new TestHost().Render(Component<KeyedList, ListProps>(new(["a", "b"], probe)));
        var b = host.RootView!.Children[1];

        host.Render(Component<KeyedList, ListProps>(new(["a"], probe)));

        Assert.Equal(["a:0"], host.RootView.Children.Select(v => v.Text));
        Assert.True(b.Unmounted);
        Assert.Equal(["cleanup b"], probe.Log);
    }

    [Fact]
    public void ChangingAnUnkeyedChildsType_ReplacesTheControl()
    {
        var host = new TestHost().Render(new BoxElement(new TextElement("x")));
        var text = host.RootView!.Children[0];

        host.Render(new BoxElement(new InputElement("x")));

        Assert.True(text.Unmounted);
        Assert.Equal("input", Assert.Single(host.RootView.Children).Kind);
    }

    [Fact]
    public void DuplicateSiblingKeys_Throw()
    {
        var host = new TestHost();

        var error = Assert.Throws<InvalidOperationException>(() =>
            host.Render(new BoxElement(new TextElement("1").WithKey("k"), new TextElement("2").WithKey("k"))));
        Assert.Contains("'k'", error.Message);
    }
}

public sealed class TwoStates : Component<Probe>
{
    public override Element Render()
    {
        var (a, setA) = UseState(0);
        var (b, setB) = UseState(0);
        Props.Setters["a"] = setA;
        Props.Setters["b"] = setB;
        Props.CountRender("two");
        return new TextElement($"{a},{b}");
    }
}

public sealed class Parent : Component<Probe>
{
    public override Element Render()
    {
        var (tick, setTick) = UseState(0);
        Props.Setters["tick"] = setTick;
        return new BoxElement(
            new TextElement($"tick {tick}"),
            Component<Counter, CounterProps>(new("child", Props)),
            Component<Propless>());
    }
}

public sealed class Propless : Component
{
    public static int Renders;

    public override Element Render()
    {
        Renders++;
        return new TextElement("propless");
    }
}

public class StateTests
{
    [Fact]
    public void SetState_RendersOnlyWhenTheFlushRuns_AndBatchesSetters()
    {
        var probe = new Probe();
        var host = new TestHost().Render(Component<TwoStates, Probe>(probe));

        probe.Setters["a"](1);
        probe.Setters["b"](2);

        Assert.Equal("0,0", host.RootView!.Text);
        Assert.Equal(1, host.Scheduler.PendingCount);
        host.Scheduler.Drain();
        Assert.Equal("1,2", host.RootView.Text);
        Assert.Equal(2, probe.Renders["two"]);
    }

    [Fact]
    public void SetState_WithAnEqualValue_DoesNotRender()
    {
        var probe = new Probe();
        var host = new TestHost().Render(Component<TwoStates, Probe>(probe));

        probe.Setters["a"](0);
        host.Scheduler.Drain();

        Assert.Equal(1, probe.Renders["two"]);
    }

    [Fact]
    public void SetState_FromAnotherThread_IsPostedToTheRenderThread()
    {
        var probe = new Probe();
        var host = new TestHost().Render(Component<TwoStates, Probe>(probe));

        var worker = new Thread(() => probe.Setters["a"](7));
        worker.Start();
        worker.Join();

        Assert.Equal("0,0", host.RootView!.Text);
        host.Scheduler.Drain();
        Assert.Equal("7,0", host.RootView.Text);
    }

    [Fact]
    public void ParentRerender_SkipsChildrenWhosePropsAreEqual_AndProplessChildren()
    {
        var probe = new Probe();
        Propless.Renders = 0;
        var host = new TestHost().Render(Component<Parent, Probe>(probe));

        probe.Setters["tick"](1);
        host.Scheduler.Drain();

        Assert.Equal("tick 1", host.RootView!.Children[0].Text);
        Assert.Equal(1, probe.Renders["child"]);
        Assert.Equal(1, Propless.Renders);
    }

    [Fact]
    public void SetState_OnTwoComponents_PostsOneFlush()
    {
        var probe = new Probe();
        var host = new TestHost().Render(Component<Parent, Probe>(probe));

        probe.Setters["tick"](1);
        probe.Setters["child"](2);

        Assert.Equal(1, host.Scheduler.PendingCount);
        host.Scheduler.Drain();
        Assert.Equal(["tick 1", "child:2", "propless"], host.RootView!.Children.Select(v => v.Text));
    }

    [Fact]
    public void ChildSetState_RerendersOnlyTheChild_AndPatchesTheNativeTree()
    {
        var probe = new Probe();
        var host = new TestHost().Render(Component<Parent, Probe>(probe));

        probe.Setters["child"](3);
        host.Scheduler.Drain();

        Assert.Equal(["tick 0", "child:3", "propless"], host.RootView!.Children.Select(v => v.Text));
    }
}

public sealed record EffectProps(int Dependency, Probe Probe, Func<bool> RootIsAttached);

public sealed class EffectChild : Component<EffectProps>
{
    public override Element Render()
    {
        var dependency = Props.Dependency;
        UseEffect(() =>
        {
            Props.Probe.Log.Add($"run {dependency} attached={Props.RootIsAttached()}");
            return () => Props.Probe.Log.Add($"cleanup {dependency}");
        }, dependency);
        return new TextElement("child");
    }
}

public sealed class EffectParent : Component<EffectProps>
{
    public override Element Render()
    {
        UseEffect(() => Props.Probe.Log.Add("parent"));
        return new BoxElement(Component<EffectChild, EffectProps>(Props));
    }
}

public sealed record HookOrderProps(bool ExtraHook);

public sealed class ConditionalHook : Component<HookOrderProps>
{
    public override Element Render()
    {
        UseState(0);
        if (Props.ExtraHook) UseState(1);
        return new TextElement("x");
    }
}

public class EffectTests
{
    [Fact]
    public void Effects_RunAfterCommit_ChildrenFirst_AndReRunOnlyWhenDependenciesChange()
    {
        var probe = new Probe();
        var host = new TestHost();
        bool Attached() => host.RootView is not null;

        host.Render(Component<EffectParent, EffectProps>(new(1, probe, Attached)));
        Assert.Equal(["run 1 attached=True", "parent"], probe.Log);

        probe.Log.Clear();
        host.Render(Component<EffectParent, EffectProps>(new(1, probe, Attached)));
        Assert.Empty(probe.Log);

        host.Render(Component<EffectParent, EffectProps>(new(2, probe, Attached)));
        Assert.Equal(["cleanup 1", "run 2 attached=True"], probe.Log);

        probe.Log.Clear();
        host.Root.Unmount();
        Assert.Equal(["cleanup 2"], probe.Log);
        Assert.Null(host.RootView);
    }

    [Fact]
    public void CallingADifferentNumberOfHooks_Throws()
    {
        var host = new TestHost().Render(Component<ConditionalHook, HookOrderProps>(new(false)));

        Assert.Throws<HookOrderException>(() => host.Render(Component<ConditionalHook, HookOrderProps>(new(true))));
    }
}

public sealed class UppercaseForm : Component<List<string>>
{
    public override Element Render()
    {
        var (text, setText) = UseState("");
        return new BoxElement(
            new InputElement(text, value =>
            {
                Props.Add(value);
                setText(value.ToUpperInvariant());
            }),
            new TextElement(text));
    }
}

public class EchoTests
{
    [Fact]
    public void ControlledInput_ReportsUserInputOnce_AndReconcilerWritesDoNotEcho()
    {
        var changes = new List<string>();
        var host = new TestHost().Render(Component<UppercaseForm, List<string>>(changes));
        var input = host.RootView!.Children[0];

        input.SimulateTyping("abc");
        host.Scheduler.Drain();

        Assert.Equal("ABC", input.Text);
        Assert.Equal("ABC", host.RootView.Children[1].Text);
        Assert.Equal(["abc"], changes);
    }
}

public class CoreBoundaryTests
{
    [Fact]
    public void Core_ReferencesNoUiFramework()
    {
        var references = typeof(ReactorRoot).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.NotEmpty(references);
        Assert.All(references, name => Assert.StartsWith("System.", name));
    }
}

public sealed class SettlesDuringRender : Component<Probe>
{
    public override Element Render()
    {
        var (value, setValue) = UseState(0);
        Props.Setters["value"] = setValue;
        if (value == 1) setValue(2);
        UseEffect(() =>
        {
            Props.Log.Add($"run {value}");
            return () => Props.Log.Add($"cleanup {value}");
        }, value);
        return new TextElement($"value {value}");
    }
}

public sealed record ToggleProps(string Label, bool InitiallyVisible, Probe Probe);

public sealed class Toggle : Component<ToggleProps>
{
    public override Element? Render()
    {
        var (visible, setVisible) = UseState(Props.InitiallyVisible);
        Props.Probe.Setters[Props.Label] = v => setVisible(v == 1);
        return visible ? new TextElement(Props.Label) : null;
    }
}

public sealed record PartialProps(int Compared, int Ignored, Probe Probe);

public sealed class IgnoresAField : Component<PartialProps>
{
    public override Element Render()
    {
        var (tick, setTick) = UseState(0);
        Props.Probe.Setters["tick"] = setTick;
        return new TextElement($"{Props.Compared},{Props.Ignored},{tick}");
    }

    protected override bool ShouldUpdate(PartialProps? oldProps, PartialProps? newProps) =>
        oldProps?.Compared != newProps?.Compared;
}

public sealed class EffectAncestor : Component<Probe>
{
    public override Element Render()
    {
        var (value, setValue) = UseState(0);
        Props.Setters["ancestor"] = setValue;
        UseEffect(() => Props.Log.Add("ancestor"), value);
        return new BoxElement(Component<MemoizedMiddle, Probe>(Props));
    }
}

public sealed class MemoizedMiddle : Component<Probe>
{
    public override Element Render() => Component<EffectDescendant, Probe>(Props);
}

public sealed class EffectDescendant : Component<Probe>
{
    public override Element Render()
    {
        var (value, setValue) = UseState(0);
        Props.Setters["descendant"] = setValue;
        UseEffect(() => Props.Log.Add("descendant"), value);
        return new TextElement("descendant");
    }
}

public class ReviewRegressionTests
{
    [Fact]
    public void SetStateDuringRender_KeepsTheLatestEffectSubscribed()
    {
        var probe = new Probe();
        var host = new TestHost().Render(Component<SettlesDuringRender, Probe>(probe));

        probe.Setters["value"](1);
        host.Scheduler.Drain();

        Assert.Equal("value 2", host.RootView!.Text);
        Assert.Equal(["run 0", "cleanup 0", "run 2"], probe.Log);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BatchedSwapOfTheOnlyRootControl_IsValidatedAfterTheBatchSettles(bool showFirst)
    {
        var probe = new Probe();
        var host = new TestHost().Render(Group(
            Component<Toggle, ToggleProps>(new("A", true, probe)),
            Component<Toggle, ToggleProps>(new("B", false, probe))));

        if (showFirst) { probe.Setters["B"](1); probe.Setters["A"](0); }
        else { probe.Setters["A"](0); probe.Setters["B"](1); }
        host.Scheduler.Drain();

        Assert.Equal("B", host.RootView!.Text);
    }

    [Fact]
    public void SkippedRender_StillInstallsTheLatestProps()
    {
        var probe = new Probe();
        var host = new TestHost().Render(Component<IgnoresAField, PartialProps>(new(1, 1, probe)));

        host.Render(Component<IgnoresAField, PartialProps>(new(1, 2, probe)));
        Assert.Equal("1,1,0", host.RootView!.Text);

        probe.Setters["tick"](1);
        host.Scheduler.Drain();
        Assert.Equal("1,2,1", host.RootView.Text);
    }

    [Fact]
    public void Effects_RunDescendantFirst_WhenAMemoizedComponentSeparatesThem()
    {
        var probe = new Probe();
        var host = new TestHost().Render(Component<EffectAncestor, Probe>(probe));
        probe.Log.Clear();

        probe.Setters["ancestor"](1);
        probe.Setters["descendant"](1);
        host.Scheduler.Drain();

        Assert.Equal(["descendant", "ancestor"], probe.Log);
    }

    [Fact]
    public void ChangingTheRootKey_RemountsTheRoot()
    {
        var probe = new Probe();
        var host = new TestHost().Render(Component<Counter, CounterProps>(new("a", probe)).WithKey("1"));
        probe.Setters["a"](5);
        host.Scheduler.Drain();

        host.Render(Component<Counter, CounterProps>(new("a", probe)).WithKey("2"));

        Assert.Equal("a:0", host.RootView!.Text);
        Assert.Equal(["cleanup a"], probe.Log);
    }
}
