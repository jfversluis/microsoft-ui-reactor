using Microsoft.UI.Reactor.Hosting;
using static Microsoft.UI.Reactor.CoreFactories;

namespace Microsoft.UI.Reactor.Core.Tests;

/// <summary>A fake element that carries a ref, as a MAUI view element does (spec 070 §2.4).</summary>
public sealed record RefTextElement(string Text, bool ThrowOnUpdate = false) : Element, IRefTarget
{
    internal ElementRefCell? Cell { get; init; }
    ElementRefCell? IRefTarget.RefCell => Cell;
}

/// <summary>The engine's ref binding, on the fake renderer, so it holds for any handler.</summary>
public class RefBindingTests
{
    private sealed class RefTextHandler : IElementHandler<RefTextElement, FakeView>
    {
        public FakeView Mount(HandlerContext<RefTextElement> context, RefTextElement element)
        {
            var view = new FakeView("ref");
            view.SetText(element.Text);
            return view;
        }

        public void Update(HandlerContext<RefTextElement> context, RefTextElement oldElement, RefTextElement newElement, FakeView control)
        {
            if (newElement.ThrowOnUpdate) throw new InvalidOperationException("update failed");
            control.SetText(newElement.Text);
        }
    }

    private static ReactorRoot Root(out ManualScheduler scheduler, out Func<FakeView?> rootView)
    {
        var registry = new FakeRenderer().CreateRegistry().Register(new RefTextHandler());
        FakeView? view = null;
        scheduler = new ManualScheduler();
        var root = new ReactorRoot(registry, scheduler, native => view = (FakeView?)native);
        rootView = () => view;
        return root;
    }

    private static ElementRefCell NewCell() => new(typeof(FakeView));

    private static RefTextElement Text(string text, ElementRefCell? cell = null) => new(text) { Cell = cell };

    [Fact]
    public void ARefMovedBetweenSiblings_EndsOnTheNewElement_InEitherOrder()
    {
        var root = Root(out _, out var view);
        var cell = NewCell();
        root.Render(new BoxElement(Text("a", cell), Text("b")));
        var (a, b) = (view()!.Children[0], view()!.Children[1]);
        Assert.Same(a, cell.Current);

        root.Render(new BoxElement(Text("a"), Text("b", cell)));
        Assert.Same(b, cell.Current);

        root.Render(new BoxElement(Text("a", cell), Text("b")));
        Assert.Same(a, cell.Current);
    }

    [Fact]
    public void ARemountInPlace_KeepsTheNewBinding()
    {
        var root = Root(out _, out var view);
        var cell = NewCell();
        root.Render(new BoxElement(Text("a", cell) with { Key = "1" }));
        var first = cell.Current;

        // A new key at the same position: the new node mounts before the old one unmounts.
        root.Render(new BoxElement(Text("a", cell) with { Key = "2" }));

        Assert.NotSame(first, cell.Current);
        Assert.Same(view()!.Children[0], cell.Current);
    }

    [Fact]
    public void AKeyedMove_KeepsTheView()
    {
        var root = Root(out _, out var view);
        var cell = NewCell();
        root.Render(new BoxElement(Text("a") with { Key = "a" }, Text("b", cell) with { Key = "b" }));
        var b = cell.Current;

        root.Render(new BoxElement(Text("b", cell) with { Key = "b" }, Text("a") with { Key = "a" }));

        Assert.Same(b, cell.Current);
        Assert.Same(view()!.Children[0], b);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AThrowingUpdate_DuringARemovalOrSwap_ReleasesTheBoundRef(bool swap)
    {
        var root = Root(out _, out _);
        var old = NewCell();
        var replacement = NewCell();
        Element Tree(RefTextElement e) => new BoxElement(ErrorBoundary(e, _ => new TextElement("fallback")));
        root.Render(Tree(Text("a", old)));
        Assert.NotNull(old.Current);

        root.Render(Tree(new RefTextElement("a", ThrowOnUpdate: true) { Cell = swap ? replacement : null }));

        Assert.Null(old.Current);
        Assert.Null(replacement.Current);
    }

    [Fact]
    public void ABoundaryFallback_ReleasesTheSubtreesRefs()
    {
        var root = Root(out _, out var view);
        var cell = NewCell();
        root.Render(ErrorBoundary(new BoxElement(Text("a", cell), new ExplodingElement("never")), _ => new TextElement("fallback")));

        Assert.Equal("fallback", view()!.Text);
        Assert.Null(cell.Current);
    }

    [Fact]
    public void AWrongTypedRef_FailsTheMount_AndBindsNothing()
    {
        var root = Root(out _, out var view);
        var cell = new ElementRefCell(typeof(string));
        string? caught = null;
        root.Render(ErrorBoundary(Text("a", cell), e => new TextElement(caught = e.Message)));

        Assert.Contains("ElementRef<String>", caught);
        Assert.Null(cell.Current);
    }
}
