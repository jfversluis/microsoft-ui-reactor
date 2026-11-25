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
    private sealed record LeafOfMiddle : Middle;
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
        // Both Middle and Base are registered for derived types; the nearer one wins.
        Assert.True(table.TryResolve(typeof(LeafOfMiddle), out var nearest));
        Assert.Equal("middle", nearest);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALookupThatOverlapsARegistration_LeavesNoStaleCacheEntry(bool hadAHit)
    {
        var table = new HandlerTable<string>();
        table.TryAdd(typeof(Other), "unrelated", forDerivedTypes: true);
        if (hadAHit) table.Set(typeof(Base), "old", forDerivedTypes: true);

        // The registration lands after the lookup walked the chain, before it caches what it found.
        table.BeforePublishForTesting = () =>
        {
            table.BeforePublishForTesting = null;
            table.Set(typeof(Base), "new", forDerivedTypes: true);
        };
        table.TryResolve(typeof(Leaf), out var during);
        Assert.Equal(hadAHit ? "old" : null, during);

        Assert.True(table.TryResolve(typeof(Leaf), out var after));
        Assert.Equal("new", after);
    }

    public class CollectibleBase;

    [Fact]
    public void ACollectibleTypesLookups_AreNotCached_SoItsLoadContextCanUnload()
    {
        var table = new HandlerTable<string>();
        table.TryAdd(typeof(CollectibleBase), "base", forDerivedTypes: true);

        var type = ResolveCollectibleTypes(table);
        for (var i = 0; i < 10 && type.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(type.IsAlive, "the table kept a collectible type alive");
    }

    // Resolves a hit and a miss for two types in a collectible assembly, keeping no reference.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference ResolveCollectibleTypes(HandlerTable<string> table)
    {
        var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new System.Reflection.AssemblyName("CollectibleHandlerTableTypes"), System.Reflection.Emit.AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("Types");
        var derived = module.DefineType("Derived", System.Reflection.TypeAttributes.Public, typeof(CollectibleBase)).CreateType();
        var unrelated = module.DefineType("Unrelated", System.Reflection.TypeAttributes.Public).CreateType();
        Assert.True(derived.IsCollectible);

        Assert.True(table.TryResolve(derived, out var hit));
        Assert.Equal("base", hit);
        Assert.False(table.TryResolve(unrelated, out _));
        // The types, not the AssemblyBuilder wrapper: a cache entry would hold the types.
        return new WeakReference(unrelated);
    }
}

