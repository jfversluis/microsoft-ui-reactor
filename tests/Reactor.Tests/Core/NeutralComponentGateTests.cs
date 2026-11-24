using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Core;

/// <summary>
/// Spec 067 §11.2: the component skip gate, driven through elements and components that carry no
/// WinUI type, so the gate is shown to work for any renderer.
/// </summary>
public class NeutralComponentGateTests
{
    private sealed record Leaf : ReactorElement;

    private sealed record ComponentEl(object? Props) : ReactorElement, IComponentElement;

    private sealed record MemoEl(object?[]? Dependencies) : ReactorElement, IMemoElement
    {
        public ReactorElement? Render(RenderContext context) => new Leaf();
    }

    private sealed record FuncEl : ReactorElement, IRenderFunctionElement
    {
        public ReactorElement? Render(RenderContext context) => new Leaf();
    }

    private sealed class Propless(bool shouldUpdate) : ReactorComponent
    {
        public override ReactorElement? Render() => new Leaf();
        protected internal override bool ShouldUpdate() => shouldUpdate;
    }

    private sealed class WithProps : ReactorComponent, IPropsReceiver, IPropsComparable
    {
        public string? Props;
        public List<(object? Old, object? New)> Compared = [];
        public override ReactorElement? Render() => new Leaf();
        public void SetProps(object props) => Props = (string)props;
        public object? PropsBoxed => Props;
        public Type PropsType => typeof(string);
        public bool CompareProps(object? oldProps, object? newProps)
        {
            Compared.Add((oldProps, newProps));
            return !Equals(oldProps, newProps);
        }
    }

    private static readonly Context<int> Theme = new(0);

    /// <summary>A node whose render read <see cref="Theme"/> as <paramref name="seen"/>.</summary>
    private static RenderContext ContextThatRead(int seen)
    {
        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?> { [Theme] = seen });
        var context = new RenderContext();
        context.BeginRender(() => { }, scope);
        context.UseContext(Theme);
        return context;
    }

    private static ContextScope ScopeWith(int theme)
    {
        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?> { [Theme] = theme });
        return scope;
    }

    [Fact]
    public void PropsComponent_SkipsOnEqualProps_RendersOnChangedProps()
    {
        var component = new WithProps();
        var node = new ComponentNode { Component = component, PreviousProps = "a" };

        var same = ComponentGate.Evaluate(node, new ComponentEl("a"), new ContextScope());
        var changed = ComponentGate.Evaluate(node, new ComponentEl("b"), new ContextScope());

        Assert.True(same.Gated && same.Skip);
        Assert.Equal(ComponentGateReason.Context, same.Reason);
        Assert.True(changed.Gated && !changed.Skip);
        Assert.Equal(ComponentGateReason.Props, changed.Reason);
        // The baseline is the node's previous props, not the instance's current ones.
        Assert.Equal([("a", "a"), ("a", "b")], component.Compared);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ProplessComponent_FollowsShouldUpdate(bool shouldUpdate, bool skip)
    {
        var node = new ComponentNode { Component = new Propless(shouldUpdate) };
        var result = ComponentGate.Evaluate(node, new ComponentEl(null), new ContextScope());
        Assert.True(result.Gated);
        Assert.Equal(skip, result.Skip);
        Assert.Equal(ComponentGateReason.Parent, result.Reason);
    }

    [Fact]
    public void AContextValueTheComponentRead_DeclinesTheSkip()
    {
        var component = new Propless(shouldUpdate: false);
        // The class component's own context is the one that read the value.
        var ctx = component.Context;
        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?> { [Theme] = 1 });
        ctx.BeginRender(() => { }, scope);
        ctx.UseContext(Theme);
        var node = new ComponentNode { Component = component };

        var unchanged = ComponentGate.Evaluate(node, new ComponentEl(null), ScopeWith(1));
        var changed = ComponentGate.Evaluate(node, new ComponentEl(null), ScopeWith(2));

        Assert.True(unchanged.Skip);
        Assert.False(changed.Skip);
        Assert.Equal(ComponentGateReason.Context, changed.Reason);
        Assert.False(ComponentGate.HasConsumedContextChanged(node, ScopeWith(1)));
        Assert.True(ComponentGate.HasConsumedContextChanged(node, ScopeWith(2)));
    }

    [Fact]
    public void Memo_SkipsOnEqualDependencies_RendersOnChangedOnes_AndNullMeansOnce()
    {
        var node = new ComponentNode { Context = ContextThatRead(0), MemoDependencies = [1, "x"] };
        var equal = ComponentGate.Evaluate(node, new MemoEl([1, "x"]), ScopeWith(0));
        var differ = ComponentGate.Evaluate(node, new MemoEl([2, "x"]), ScopeWith(0));
        var contextChanged = ComponentGate.Evaluate(node, new MemoEl([1, "x"]), ScopeWith(5));

        Assert.True(equal.Gated && equal.Skip);
        Assert.True(!differ.Skip && differ.Reason == ComponentGateReason.Props);
        Assert.True(!contextChanged.Skip && contextChanged.Reason == ComponentGateReason.Context);

        var once = new ComponentNode { Context = new RenderContext(), MemoDependencies = null };
        Assert.True(ComponentGate.Evaluate(once, new MemoEl(null), new ContextScope()).Skip);
        Assert.False(ComponentGate.Evaluate(once, new MemoEl([1]), new ContextScope()).Skip);
    }

    [Fact]
    public void FunctionComponents_AndMismatchedElements_AreNotGated()
    {
        Assert.False(ComponentGate.Evaluate(new ComponentNode { Context = new RenderContext() }, new FuncEl(), new ContextScope()).Gated);
        // A class node handed a memo element (and the reverse) has no gate to apply.
        Assert.False(ComponentGate.Evaluate(new ComponentNode { Component = new Propless(false) }, new MemoEl([1]), new ContextScope()).Gated);
        Assert.False(ComponentGate.Evaluate(new ComponentNode { Context = new RenderContext() }, new ComponentEl(null), new ContextScope()).Gated);
    }

    [Fact]
    public void RefreshSkippedProps_InstallsTheNewPropsAsTheBaseline_OnlyForPropsComponents()
    {
        var component = new WithProps { Props = "old" };
        var node = new ComponentNode { Component = component, PreviousProps = "old" };
        ComponentGate.RefreshSkippedProps(node, new ComponentEl("new"));
        Assert.Equal("new", component.Props);
        Assert.Equal("new", node.PreviousProps);

        // Null props leave both alone, as does a propless component.
        ComponentGate.RefreshSkippedProps(node, new ComponentEl(null));
        Assert.Equal("new", node.PreviousProps);
        var propless = new ComponentNode { Component = new Propless(false), PreviousProps = "kept" };
        ComponentGate.RefreshSkippedProps(propless, new ComponentEl("ignored"));
        Assert.Equal("kept", propless.PreviousProps);
    }
}
