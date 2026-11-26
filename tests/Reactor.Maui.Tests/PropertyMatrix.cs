using System.Reflection;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>
/// One element property, exercised by <see cref="PropertyMatrixTests"/> (spec 070 §4.8): set (A),
/// updated in place with one write (B), an equal render with no write, reapplied (A), and cleared back
/// to a fresh view's value with no local value left.
/// </summary>
public sealed class PropertyCase
{
    public required Type ElementType { get; init; }
    public required string Property { get; init; }

    /// <summary>The element with the property unset (or at its default).</summary>
    public required Func<Element> Base { get; init; }
    public required Func<Element> WithA { get; init; }
    public required Func<Element> WithB { get; init; }

    /// <summary>An element equal to <see cref="WithB"/> by the property's own comparison, built separately.</summary>
    public required Func<Element> WithBAgain { get; init; }

    /// <summary>Reads the value the native view shows (a Controls view or a direct view).</summary>
    public required Func<object, object?> Read { get; init; }

    public required object? A { get; init; }
    public required object? B { get; init; }

    /// <summary>Compares read values; <see cref="object.Equals(object?, object?)"/> by default.</summary>
    public Func<object?, object?, bool> Same { get; init; } = static (x, y) => Equals(x, y);

    /// <summary>The Controls property written; its <c>PropertyChanged</c> count is the write count.</summary>
    public required BindableProperty NativeProperty { get; init; }

    /// <summary>For an element the direct registry renders as a direct view: the property name its view remaps.</summary>
    public string? DirectProperty { get; init; }

    /// <summary>Whether unsetting clears the property (no local value); otherwise the default is written.</summary>
    public bool Clears { get; init; } = true;

    public string Key => $"{ElementType.Name}.{Property}";

    public override string ToString() => Key;
}

/// <summary>One callback of an element, exercised on its native view (spec 070 §4.8).</summary>
public sealed class EventCase
{
    public required Type ElementType { get; init; }
    public required string Property { get; init; }

    /// <summary>The element, whose callback reports what it received through the given action.</summary>
    public required Func<Action<string>, Element> Create { get; init; }

    /// <summary>Raises the event on the native view.</summary>
    public required Action<object> Trigger { get; init; }

    /// <summary>What the callback receives for one <see cref="Trigger"/>.</summary>
    public string Payload { get; init; } = "";

    public string Key => $"{ElementType.Name}.{Property}";

    public override string ToString() => Key;
}

/// <summary>
/// Evidence for a property the matrix can't express (a child slot, a controlled value): the test that
/// covers it, the facets it covers and the mutation that turned it red (spec 070 §4.8).
/// </summary>
public sealed record CoveredClaim(Type ElementType, string Property, Type TestClass, string TestMethod, string Facets, string MutationEvidence)
{
    public string Key => $"{ElementType.Name}.{Property}";
}

/// <summary>A property the census deliberately doesn't require evidence for, with the reason.</summary>
public sealed record ExcludedClaim(Type ElementType, string Property, string Reason)
{
    public string Key => $"{ElementType.Name}.{Property}";
}

public class PropertyMatrixTests
{
    public static readonly (string Name, Func<ElementHandlerRegistry> Create)[] Registries =
    [
        ("controls", MauiHandlers.CreateRegistry),
        ("direct", DirectHandlers.CreateRegistry),
    ];

    public static TheoryData<string, string> PropertyCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var c in MatrixCases.Properties)
            foreach (var (registry, _) in Registries)
                data.Add(c.Key, registry);
        return data;
    }

    public static TheoryData<string, string> EventCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var c in MatrixCases.Events)
            foreach (var (registry, _) in Registries)
                data.Add(c.Key, registry);
        return data;
    }

    private static ElementHandlerRegistry Registry(string name) => Registries.Single(r => r.Name == name).Create();

    [Theory]
    [MemberData(nameof(PropertyCases))]
    public void Property_IsSetUpdatedAndCleared(string key, string registry)
    {
        var c = MatrixCases.Properties.Single(p => p.Key == key);
        var host = new TestHost(Registry(registry)).Render(c.WithA());
        var native = host.RootView!;
        var direct = native as DirectView;
        Assert.True(direct is null || c.DirectProperty is not null, $"{key} rendered a direct view the case doesn't read");
        Assert.True(direct is not null || native is BindableObject, $"{key} rendered a {native.GetType().Name}");

        var fresh = Activator.CreateInstance(native.GetType())!;
        // Before reading: a property with a default-value creator counts as set once read.
        var freshIsSet = fresh is BindableObject freshBindable && freshBindable.IsSet(c.NativeProperty);
        var defaultValue = c.Read(fresh);
        Assert.False(c.Same(c.A, c.B), $"{key}: A and B must differ");
        Assert.False(c.Same(c.A, defaultValue), $"{key}: A must differ from the default, or the clear proves nothing");

        void Expect(object? expected, string step) =>
            Assert.True(c.Same(expected, c.Read(native)), $"{key} [{registry}] {step}: expected {expected}, read {c.Read(native)}");

        Expect(c.A, "set");
        if (direct is null) Assert.True(((BindableObject)native).IsSet(c.NativeProperty), $"{key}: set leaves a local value");

        var writes = 0;
        if (direct is null)
        {
            ((BindableObject)native).PropertyChanged += (_, e) => { if (e.PropertyName == c.NativeProperty.PropertyName) writes++; };
        }
        else
        {
            direct.Handler = new StubViewHandler { OnUpdateValue = name => { if (name == c.DirectProperty) writes++; } };
        }

        host.Render(c.WithB());
        Assert.Same(native, host.RootView);
        Expect(c.B, "update");
        if (direct is null) Assert.Equal(1, writes);
        else Assert.True(writes >= 1, $"{key}: the direct view remapped {c.DirectProperty}");

        writes = 0;
        host.Render(c.WithBAgain());
        Expect(c.B, "equal render");
        Assert.Equal(0, writes);

        host.Render(c.WithA());
        Expect(c.A, "reapply");

        host.Render(c.Base());
        Assert.Same(native, host.RootView);
        // As a fresh view: no local value, or the one its constructor sets (MAUI's rectangles set Aspect).
        if (direct is null && c.Clears)
            Assert.True(freshIsSet == ((BindableObject)native).IsSet(c.NativeProperty), $"{key}: clearing leaves a local value only where a fresh view has one");
        Expect(defaultValue, "clear");
    }

    [Theory]
    [MemberData(nameof(EventCases))]
    public void Event_ReachesTheCurrentCallback_AndNothingAfterUnmount(string key, string registry)
    {
        var c = MatrixCases.Events.Single(e => e.Key == key);
        var log = new List<string>();
        var host = new TestHost(Registry(registry)).Render(c.Create(payload => log.Add("first " + payload)));
        var native = host.RootView!;

        c.Trigger(native);
        host.Scheduler.Drain();
        Assert.Equal(["first " + c.Payload], log);

        log.Clear();
        host.Render(c.Create(payload => log.Add("second " + payload)));
        Assert.Same(native, host.RootView);
        c.Trigger(native);
        host.Scheduler.Drain();
        Assert.Equal(["second " + c.Payload], log);

        log.Clear();
        host.Root.Unmount();
        c.Trigger(native);
        host.Scheduler.Drain();
        Assert.Empty(log);
    }
}

/// <summary>
/// The element census (spec 070 §4.8): every public element property has exactly one claim, a matrix
/// case, a covered-by claim naming a discovered test with its facets and mutation evidence, a pending
/// entry from the frozen 4c-3b baseline, or an exclusion with a reason.
/// </summary>
public class ElementCensusTests
{
    /// <summary>Value records elements hold, censused with the elements.</summary>
    private static readonly Type[] ValueRecords =
        [typeof(SwipeItemData), typeof(ToolbarAction), typeof(RichTextRun), typeof(RichTextHyperlink), typeof(ItemGroup), typeof(ShellBackButton)];

    /// <summary>The elements 4c-3a added or made ✅: none of their properties may be pending.</summary>
    private static readonly Type[] CoveredIn4c3a =
        [typeof(RectangleElement), typeof(EllipseElement), typeof(RoundRectangleElement), typeof(LineElement),
         typeof(PolygonElement), typeof(PolylineElement), typeof(PathElement), typeof(ContentViewElement),
         typeof(BorderElement), typeof(ImageElement), typeof(ImageButtonElement)];

    // The first run's count; an enumeration that finds fewer is broken (or an element was removed on purpose).
    private const int TypeFloor = 58;

    public static IReadOnlyList<(Type Type, PropertyInfo Property)> Enumerate()
    {
        var assembly = typeof(ViewElement).Assembly;
        var types = assembly.GetExportedTypes()
            .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && typeof(Element).IsAssignableFrom(t)
                && t.Namespace is { } ns && ns.StartsWith("Microsoft.UI.Reactor.Maui", StringComparison.Ordinal))
            .Concat(ValueRecords)
            .Distinct()
            .OrderBy(t => t.Name, StringComparer.Ordinal);
        var result = new List<(Type, PropertyInfo)>();
        foreach (var type in types)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.SetMethod is not { IsPublic: true }) continue;
                var declaring = property.DeclaringType!;
                // The core's members (Key) and the shared modifiers (4d's census) aren't this census's.
                if (declaring.Assembly != assembly || declaring.Namespace == typeof(Element).Namespace || property.Name == nameof(ViewElement.Modifiers)) continue;
                result.Add((type, property));
            }
        }
        return result;
    }

    private static Dictionary<string, List<string>> Claims()
    {
        var claims = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Add(string key, string source)
        {
            if (!claims.TryGetValue(key, out var list)) claims[key] = list = [];
            list.Add(source);
        }
        foreach (var c in MatrixCases.Properties) Add(c.Key, "matrix");
        foreach (var c in MatrixCases.Events) Add(c.Key, "event");
        foreach (var c in MatrixCases.Covered) Add(c.Key, $"covered by {c.TestClass.Name}.{c.TestMethod}");
        foreach (var c in MatrixCases.Excluded) Add(c.Key, "excluded");
        foreach (var key in CensusBaseline.Pending) Add(key, "pending 4c-3b");
        return claims;
    }

    [Fact]
    public void EveryElementProperty_HasExactlyOneClaim()
    {
        var census = Enumerate();
        var keys = census.Select(p => $"{p.Type.Name}.{p.Property.Name}").ToHashSet(StringComparer.Ordinal);
        var claims = Claims();

        var unclaimed = keys.Where(k => !claims.ContainsKey(k)).Order(StringComparer.Ordinal).ToList();
        var stale = claims.Keys.Where(k => !keys.Contains(k)).Order(StringComparer.Ordinal).ToList();
        var doubled = claims.Where(c => c.Value.Count > 1).Select(c => $"{c.Key} ({string.Join(", ", c.Value)})").Order(StringComparer.Ordinal).ToList();

        Assert.True(unclaimed.Count == 0, "Unclaimed properties:\n" + string.Join("\n", unclaimed));
        Assert.True(stale.Count == 0, "Claims naming no property:\n" + string.Join("\n", stale));
        Assert.True(doubled.Count == 0, "Properties claimed more than once:\n" + string.Join("\n", doubled));
    }

    [Fact]
    public void TheCensus_FindsWhatItMust()
    {
        var census = Enumerate();
        var types = census.Select(p => p.Type).Distinct().Count();
        Assert.True(types >= TypeFloor, $"the census found {types} element types, fewer than the floor {TypeFloor}");
        var keys = census.Select(p => $"{p.Type.Name}.{p.Property.Name}").ToHashSet(StringComparer.Ordinal);
        Assert.Contains("ButtonElement.BorderColor", keys);
        Assert.Contains("EntryElement.ReturnType", keys);
        Assert.Contains("EllipseElement.Fill", keys);
        Assert.Contains("SwipeItemData.Icon", keys);
        Assert.DoesNotContain("LabelElement.Modifiers", keys);
    }

    [Fact]
    public void CoveredClaims_NameDiscoveredTests_WithFacetsAndMutationEvidence()
    {
        foreach (var claim in MatrixCases.Covered)
        {
            var method = claim.TestClass.GetMethod(claim.TestMethod, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            Assert.True(method is not null, $"{claim.Key}: {claim.TestClass.Name}.{claim.TestMethod} doesn't exist");
            Assert.True(method!.GetCustomAttributes(typeof(FactAttribute), inherit: true).Length > 0,
                $"{claim.Key}: {claim.TestClass.Name}.{claim.TestMethod} isn't a discovered test");
            Assert.False(string.IsNullOrWhiteSpace(claim.Facets), $"{claim.Key}: no facets listed");
            Assert.False(string.IsNullOrWhiteSpace(claim.MutationEvidence), $"{claim.Key}: no mutation evidence");
        }
        foreach (var exclusion in MatrixCases.Excluded)
            Assert.False(string.IsNullOrWhiteSpace(exclusion.Reason), $"{exclusion.Key}: no reason");
    }

    [Fact]
    public void ThePendingBaseline_OnlyShrinks_AndHoldsNothingNew()
    {
        // The original list is frozen: its hash is pinned.
        var original = string.Join("\n", CensusBaseline.Original.Order(StringComparer.Ordinal));
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(original)));
        Assert.Equal(CensusBaseline.OriginalHash, hash);

        // The pending list is a subset of it, its size is the ceiling (lowered with each removal), no repeats.
        Assert.Equal(CensusBaseline.Ceiling, CensusBaseline.Pending.Length);
        Assert.Equal(CensusBaseline.Pending.Length, CensusBaseline.Pending.Distinct(StringComparer.Ordinal).Count());
        var added = CensusBaseline.Pending.Except(CensusBaseline.Original, StringComparer.Ordinal).ToList();
        Assert.True(added.Count == 0, "Pending entries not in the original baseline:\n" + string.Join("\n", added));

        var covered = CoveredIn4c3a.Select(t => t.Name + ".").ToArray();
        var coveredPending = CensusBaseline.Pending.Where(k => covered.Any(n => k.StartsWith(n, StringComparison.Ordinal))).ToList();
        Assert.True(coveredPending.Count == 0, "Properties of elements 4c-3a covered can't be pending:\n" + string.Join("\n", coveredPending));
    }
}
