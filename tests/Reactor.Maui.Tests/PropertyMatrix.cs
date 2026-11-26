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
public sealed record PropertyCase
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

    /// <summary>
    /// The object the property is written on, when it isn't the native view itself (a label's span for a
    /// rich-text run); its <c>PropertyChanged</c> count is the write count and its local value is checked.
    /// </summary>
    public Func<object, BindableObject>? Target { get; init; }

    /// <summary>A fresh native to read the default from, when the native has no parameterless constructor or needs a shape.</summary>
    public Func<object, object>? Fresh { get; init; }

    /// <summary>What the native shows once the property is unset, when that isn't a fresh native's value.</summary>
    public Func<object?>? Cleared { get; init; }

    /// <summary>The one registry the case runs on, when the other renders it differently (stated in the claim's comment).</summary>
    public string? OnlyRegistry { get; init; }

    public string Key => $"{ElementType.Name}.{Property}";

    public override string ToString() => Key;
}

/// <summary>
/// A controlled value and its change callback (spec 067 §14), exercised by
/// <see cref="PropertyMatrixTests.ControlledValue_IsWrittenUnreported_InputReportedOnce_RefusedRestored"/>.
/// </summary>
public sealed class ControlledCase
{
    public required Type ElementType { get; init; }
    public required string Property { get; init; }

    /// <summary>The change callback the case also claims (it receives the input).</summary>
    public required string Callback { get; init; }

    /// <summary>The element declaring <c>value</c>, whose callback reports what it received.</summary>
    public required Func<object, Action<string>, Element> Create { get; init; }

    /// <summary>The value the native shows.</summary>
    public required Func<object, object?> Read { get; init; }

    /// <summary>The user's input of a value, as the platform handler would write it.</summary>
    public required Action<object, object> Input { get; init; }

    /// <summary>Three distinct values: mounted, declared next, and the user's input.</summary>
    public required object A { get; init; }
    public required object B { get; init; }
    public required object C { get; init; }

    /// <summary>What the callback receives for the input C.</summary>
    public required string Payload { get; init; }

    /// <summary>The Controls property written (its PropertyChanged count).</summary>
    public required string WriteName { get; init; }

    /// <summary>For a direct view, the remapped name, when it isn't <see cref="WriteName"/>.</summary>
    public string? DirectWriteName { get; init; }

    public string Key => $"{ElementType.Name}.{Property}";
    public string CallbackKey => $"{ElementType.Name}.{Callback}";

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
                if (c.OnlyRegistry is null || c.OnlyRegistry == registry) data.Add(c.Key, registry);
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

    public static TheoryData<string, string> ControlledCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var c in MatrixCases.Controlled)
            foreach (var (registry, _) in Registries)
                data.Add(c.Key, registry);
        return data;
    }

    private static ElementHandlerRegistry Registry(string name) => Registries.Single(r => r.Name == name).Create();

    [Theory]
    [MemberData(nameof(ControlledCases))]
    public void ControlledValue_IsWrittenUnreported_InputReportedOnce_RefusedRestored(string key, string registry)
    {
        var c = MatrixCases.Controlled.Single(p => p.Key == key);
        var log = new List<string>();
        var host = new TestHost(Registry(registry)).Render(c.Create(c.A, log.Add));
        host.Scheduler.Drain();
        var native = host.RootView!;
        Assert.Equal(c.A, c.Read(native));
        Assert.Empty(log);

        var writes = 0;
        if (native is DirectView direct)
            direct.Handler = new StubViewHandler { OnUpdateValue = name => { if (name == (c.DirectWriteName ?? c.WriteName)) writes++; } };
        else
            ((BindableObject)native).PropertyChanged += (_, e) => { if (e.PropertyName == c.WriteName) writes++; };

        // A declared change is written, unreported.
        host.Render(c.Create(c.B, log.Add));
        host.Scheduler.Drain();
        Assert.Same(native, host.RootView);
        Assert.Equal(c.B, c.Read(native));
        Assert.True(writes >= 1, $"{key} [{registry}]: the declared change was written");
        Assert.Empty(log);

        // An equal render writes nothing.
        writes = 0;
        host.Render(c.Create(c.B, log.Add));
        host.Scheduler.Drain();
        Assert.Equal(0, writes);

        // Input is reported once; the app doesn't accept it, so the declaration comes back.
        c.Input(native, c.C);
        host.Scheduler.Drain();
        Assert.Equal([c.Payload], log);
        Assert.Equal(c.B, c.Read(native));

        // Accepted: the app renders the input, which is kept and not reported again.
        log.Clear();
        c.Input(native, c.C);
        host.Render(c.Create(c.C, log.Add));
        host.Scheduler.Drain();
        Assert.Equal([c.Payload], log);
        Assert.Equal(c.C, c.Read(native));

        // Nothing after unmount (the earlier reports are the positive control).
        log.Clear();
        host.Root.Unmount();
        c.Input(native, c.A);
        host.Scheduler.Drain();
        Assert.Empty(log);
    }

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

        var fresh = c.Fresh?.Invoke(native) ?? Activator.CreateInstance(native.GetType())!;
        BindableObject? TargetOf(object view) => c.Target is { } target ? target(view) : view as BindableObject;
        // Before reading: a property with a default-value creator counts as set once read.
        var freshIsSet = TargetOf(fresh) is { } freshBindable && freshBindable.IsSet(c.NativeProperty);
        var defaultValue = c.Read(fresh);
        Assert.False(c.Same(c.A, c.B), $"{key}: A and B must differ");
        Assert.False(c.Same(c.A, defaultValue), $"{key}: A must differ from the default, or the clear proves nothing");

        void Expect(object? expected, string step) =>
            Assert.True(c.Same(expected, c.Read(native)), $"{key} [{registry}] {step}: expected {expected}, read {c.Read(native)}");

        Expect(c.A, "set");
        if (direct is null) Assert.True(TargetOf(native)!.IsSet(c.NativeProperty), $"{key}: set leaves a local value");

        var writes = 0;
        if (direct is null)
        {
            TargetOf(native)!.PropertyChanged += (_, e) => { if (e.PropertyName == c.NativeProperty.PropertyName) writes++; };
        }
        else
        {
            // A remapped property, or a command (a direct layout's spacing only invalidates its measure).
            direct.Handler = new StubViewHandler
            {
                OnUpdateValue = name => { if (name == c.DirectProperty) writes++; },
                OnCommand = name => { if (name == c.DirectProperty) writes++; },
            };
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
            Assert.True(freshIsSet == TargetOf(native)!.IsSet(c.NativeProperty), $"{key}: clearing leaves a local value only where a fresh view has one");
        Expect(c.Cleared is { } cleared ? cleared() : defaultValue, "clear");
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
/// entry of a later phase's frozen key list, or an exclusion with a reason.
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
        foreach (var c in MatrixCases.Controlled)
        {
            Add(c.Key, "controlled");
            Add(c.CallbackKey, "controlled callback");
        }
        foreach (var c in MatrixCases.Covered) Add(c.Key, $"covered by {c.TestClass.Name}.{c.TestMethod}");
        foreach (var c in MatrixCases.Excluded) Add(c.Key, "excluded");
        foreach (var (key, phase) in CensusBaseline.Pending) Add(key, "pending " + phase);
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

    private static string Hash(IEnumerable<string> keys) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join("\n", keys.Order(StringComparer.Ordinal)))));

    /// <summary>
    /// What is wrong with a pending list (spec 070 §4.14.2): an entry outside the original baseline, a
    /// phase other than a later one, a key outside its phase's frozen list, or a repeat. Exact keys, not
    /// types: a 4c-3b property of a 4c-3c type (PageElement.ToolbarItems) can't stay pending.
    /// </summary>
    public static List<string> PendingViolations(IReadOnlyList<(string Key, string Phase)> pending)
    {
        var phases = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["4c-3c"] = CensusBaseline.Phase4c3c.ToHashSet(StringComparer.Ordinal),
            ["4c-4"] = CensusBaseline.Phase4c4.ToHashSet(StringComparer.Ordinal),
        };
        var original = CensusBaseline.Original.ToHashSet(StringComparer.Ordinal);
        var violations = new List<string>();
        foreach (var (key, phase) in pending)
        {
            if (!original.Contains(key)) violations.Add($"{key}: not in the original baseline");
            if (!phases.TryGetValue(phase, out var keys)) violations.Add($"{key}: phase {phase} has no pending properties left");
            else if (!keys.Contains(key)) violations.Add($"{key}: not one of phase {phase}'s frozen keys");
        }
        violations.AddRange(pending.GroupBy(p => p.Key, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => $"{g.Key}: pending twice"));
        return violations;
    }

    [Fact]
    public void ThePendingList_HoldsOnlyLaterPhasesFrozenKeys_AndOnlyShrinks()
    {
        // The original list and the phases' key lists are frozen: their hashes are pinned.
        Assert.Equal(CensusBaseline.OriginalHash, Hash(CensusBaseline.Original));
        Assert.Equal(CensusBaseline.Phase4c3cHash, Hash(CensusBaseline.Phase4c3c));
        Assert.Equal(CensusBaseline.Phase4c4Hash, Hash(CensusBaseline.Phase4c4));

        // Its size is the ceiling, lowered with each removal.
        Assert.Equal(CensusBaseline.Ceiling, CensusBaseline.Pending.Length);
        var violations = PendingViolations(CensusBaseline.Pending);
        Assert.True(violations.Count == 0, "Pending entries that can't be pending:\n" + string.Join("\n", violations));

        var covered = CoveredIn4c3a.Select(t => t.Name + ".").ToArray();
        var coveredPending = CensusBaseline.Pending.Where(p => covered.Any(n => p.Key.StartsWith(n, StringComparison.Ordinal))).ToList();
        Assert.True(coveredPending.Count == 0, "Properties of elements 4c-3a covered can't be pending:\n" + string.Join("\n", coveredPending));
    }

    [Fact]
    public void ThePendingCheck_RejectsA4c3bKeyHidingBehindA4c3cType()
    {
        // PageElement is a 4c-3c type, but its ToolbarItems is 4c-3b's (spec 070 §4.14.4). Leaving it
        // pending while another PageElement property is covered (removed from the list) must fail.
        var doctored = CensusBaseline.Pending.Where(p => p.Key != "PageElement.Title")
            .Append(("PageElement.ToolbarItems", "4c-3c")).ToList();
        Assert.Contains(PendingViolations(doctored), v => v.StartsWith("PageElement.ToolbarItems:", StringComparison.Ordinal));

        // And under its own phase: nothing of 4c-3b's may remain.
        Assert.Contains(PendingViolations([("SwitchElement.OnColor", "4c-3b")]), v => v.StartsWith("SwitchElement.OnColor:", StringComparison.Ordinal));
        // Positive control: a frozen 4c-3c key under its phase is fine.
        Assert.Empty(PendingViolations([("PageElement.Title", "4c-3c")]));
    }
}
