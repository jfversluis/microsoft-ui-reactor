using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Core;

/// <summary>
/// Spec 067 §15: the handler lookup both renderers' registries share. WinUI's ControlRegistry keeps
/// the first registration (TryAdd); MAUI's ElementHandlerRegistry keeps the last (Set).
/// </summary>
public class NeutralHandlerTableTests
{
    private abstract record Base : ReactorElement;
    private record Middle : Base;
    private sealed record Leaf : Middle;
    private sealed record Other : ReactorElement;

    [Fact]
    public void AnExactRegistration_WinsOverABaseOne_AndTheNearestBaseWinsOverAFartherOne()
    {
        var table = new HandlerTable<string>();
        table.TryAdd(typeof(Base), "base", forDerivedTypes: true);
        table.TryAdd(typeof(Middle), "middle", forDerivedTypes: true);
        table.TryAdd(typeof(Leaf), "leaf");

        Assert.True(table.TryResolve(typeof(Leaf), out var leaf));
        Assert.Equal("leaf", leaf);
        // Middle has a registration for its derived types, not for itself; its own base resolves it.
        Assert.True(table.TryResolve(typeof(Middle), out var middle));
        Assert.Equal("base", middle);
        Assert.False(table.TryResolve(typeof(Other), out _));
    }

    [Fact]
    public void TryAdd_KeepsTheFirstRegistration_AndSetReplacesIt()
    {
        var table = new HandlerTable<string>();
        Assert.True(table.TryAdd(typeof(Leaf), "first"));
        Assert.False(table.TryAdd(typeof(Leaf), "second"));
        table.TryResolve(typeof(Leaf), out var kept);
        Assert.Equal("first", kept);

        table.Set(typeof(Leaf), "third");
        table.TryResolve(typeof(Leaf), out var replaced);
        Assert.Equal("third", replaced);
    }

    [Fact]
    public void ABaseRegisteredAfterAMissOrAHit_IsFoundOnTheNextLookup()
    {
        var table = new HandlerTable<string>();
        table.TryAdd(typeof(Other), "unrelated", forDerivedTypes: true);
        // Caches a miss for Leaf.
        Assert.False(table.TryResolve(typeof(Leaf), out _));

        table.TryAdd(typeof(Base), "base", forDerivedTypes: true);
        Assert.True(table.TryResolve(typeof(Leaf), out var viaBase));
        Assert.Equal("base", viaBase);

        // Caches a hit on Base; a nearer base registered later takes over.
        table.Set(typeof(Middle), "middle", forDerivedTypes: true);
        Assert.True(table.TryResolve(typeof(Leaf), out var viaMiddle));
        Assert.Equal("middle", viaMiddle);
    }

    [Fact]
    public void TheRegisteredTypes_AreListedByKind()
    {
        var table = new HandlerTable<string>();
        table.TryAdd(typeof(Leaf), "leaf");
        table.TryAdd(typeof(Base), "base", forDerivedTypes: true);

        Assert.Equal([typeof(Leaf)], table.ExactTypes);
        Assert.Equal([typeof(Base)], table.BaseTypes);
        Assert.True(table.ContainsExact(typeof(Leaf)) && !table.ContainsExact(typeof(Base)));
        Assert.True(table.ContainsBase(typeof(Base)) && !table.ContainsBase(typeof(Leaf)));
    }
}
