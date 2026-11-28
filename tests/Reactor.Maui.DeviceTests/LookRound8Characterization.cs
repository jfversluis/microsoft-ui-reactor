using Microsoft.UI.Reactor.Maui.Direct;
using static Reactor.Maui.DeviceTests.FixtureContext;

namespace Reactor.Maui.DeviceTests;

/// <summary>
/// Round 8, L1: who owns the shield. Reactor's application-level constructor shield is an entry under the reserved
/// key (the bridge type's full name). <see cref="Ensure"/> runs before each construction: with
/// <c>validate</c> it replaces any entry that isn't Reactor's own, unmodified, empty style (and records why);
/// without it, it checks only that the key exists (round 7's design, the negative control).
/// </summary>
public static class ShieldOwner
{
    public static string Key => typeof(LayoutBridge).FullName!;
    public static Style? Owned;
    public static readonly List<string> Reports = [];

    public static bool IsPristine(Style style) =>
        style.TargetType == typeof(LayoutBridge) && style.Setters.Count == 0 && style.Behaviors.Count == 0
        && style.Triggers.Count == 0 && style.BasedOn is null && style.BaseResourceKey is null
        && string.IsNullOrEmpty(style.Class) && !style.ApplyToDerivedTypes;

    public static void Ensure(Application app, bool validate)
    {
        var found = app.Resources.TryGetValue(Key, out var value);
        if (found && !validate) return;
        if (found && ReferenceEquals(value, Owned) && IsPristine(Owned!)) return;
        if (found)
            Reports.Add(ReferenceEquals(value, Owned) ? "the owned shield was modified" : $"a non-owned entry ({value?.GetType().Name ?? "null"}) under the reserved key");
        Owned = new Style(typeof(LayoutBridge));
        // The root dictionary's own entries take precedence over its merged dictionaries.
        app.Resources[Key] = Owned;
    }

    public static void Reset(Application app)
    {
        app.Resources.Remove(Key);
        Owned = null;
        Reports.Clear();
    }
}

// Spec 071 §2.1, the eighth review round of f640cf88: shield ownership (L1). On demand.
public static partial class Fixtures
{
    private static IEnumerable<Fixture> LookRound8CharacterizationFixtures() =>
    [
        new("Characterize_Look_ShieldOwnership [round 7: one shared object, key check]", c => LookShieldOwnership(c, round8: false), 120, OnDemand: true),
        new("Characterize_Look_ShieldOwnership [round 8: private node shields, validated owner]", c => LookShieldOwnership(c, round8: true), 120, OnDemand: true),
    ];

    private static Style CountingLayoutStyle(Type target, bool derived)
    {
        var style = S(target, derived, null, (StyleProbe.MarkProperty, 1), (Layout.PaddingProperty, new Thickness(21)));
        style.Behaviors.Add(new CountingBehavior());
        return style;
    }

    /// <summary>
    /// L1: with live bridges in the page, each mutation an app can make to the shield or the reserved key, then a new
    /// bridge constructed after Reactor's <see cref="ShieldOwner.Ensure"/>. Live nodes: two bridges with node
    /// shields (round 7: the same object as the application entry; round 8: a private style each) and one bridge
    /// with no node shield, built under the application entry (the positive control: the mutation path is live).
    /// Counts: behavior attaches / probe-setter changes / padding.
    /// </summary>
    private static async Task LookShieldOwnership(FixtureContext c, bool round8)
    {
        var app = Application.Current!;
        var originalResources = app.Resources;
        var log = new LookLog { Phase = round8 ? "round 8" : "round 7" };
        string Count(Layout l) => $"{CountingBehavior.Of(l)}/{StyleProbe.Of(l)}/{l.Padding.Left}";
        var scenarios = new (string Name, Action Mutate)[]
        {
            ("S1 the application shield's BasedOn set in place", () => ((Style)app.Resources[ShieldOwner.Key]).BasedOn = CountingLayoutStyle(typeof(Layout), true)),
            ("S2 a setter and a behavior added to the application shield in place", () =>
            {
                var shield = (Style)app.Resources[ShieldOwner.Key];
                shield.Setters.Add(new Setter { Property = StyleProbe.MarkProperty, Value = 1 });
                shield.Behaviors.Add(new CountingBehavior());
            }),
            ("S3 Application.Resources replaced by one with a derived Layout style", () => app.Resources = new ResourceDictionary { CountingLayoutStyle(typeof(Layout), true) }),
            ("S4 Application.Resources replaced by one whose merged dictionary holds a bridge-type style", () =>
            {
                var replacement = new ResourceDictionary();
                replacement.MergedDictionaries.Add(new ResourceDictionary { CountingLayoutStyle(typeof(LayoutBridge), false) });
                app.Resources = replacement;
            }),
            ("S5 an app style for the bridge type under the reserved key", () => app.Resources[ShieldOwner.Key] = CountingLayoutStyle(typeof(LayoutBridge), false)),
        };
        try
        {
            foreach (var (name, mutate) in scenarios)
            {
                log.Phase = name;
                ShieldOwner.Ensure(app, validate: round8);
                Style NodeShield() => round8 ? new Style(typeof(LayoutBridge)) : ShieldOwner.Owned!;
                var live1 = new LayoutBridge(new DirectStackLayout(StackOrientation.Vertical), NodeShield());
                var live2 = new LayoutBridge(new DirectStackLayout(StackOrientation.Vertical), NodeShield());
                var appOnly = new LayoutBridge(new DirectStackLayout(StackOrientation.Vertical));
                var grid = new Grid { Children = { new Label { Text = name } } };
                var host = new ContentView { Content = grid };
                foreach (var b in new[] { live1, live2, appOnly }) host.AddLogicalChild(b);
                c.ShowNative(host);
                await NativeLoaded(c, grid);
                log.Add($"before: live {Count(live1)}, {Count(live2)}; application-shield-only {Count(appOnly)}");

                mutate();
                await c.Settle(150);
                log.Add($"after the mutation: live {Count(live1)}, {Count(live2)}; application-shield-only (positive control) {Count(appOnly)}; a stock Grid {Count(grid)}");

                ShieldOwner.Ensure(app, validate: round8);
                var built = new LayoutBridge(new DirectStackLayout(StackOrientation.Vertical), NodeShield());
                var constructed = Count(built);
                host.AddLogicalChild(built);
                await c.Settle(150);
                log.Add($"a bridge constructed next: after construction {constructed}, parented {Count(built)}; live {Count(live1)}, {Count(live2)}; reports: {(ShieldOwner.Reports.Count == 0 ? "none" : string.Join("; ", ShieldOwner.Reports))}");

                c.ShowNative(new ContentView());
                if (!ReferenceEquals(app.Resources, originalResources)) app.Resources = originalResources;
                ShieldOwner.Reset(app);
                await c.Settle(50);
            }
            Check(true, "measured");
        }
        finally
        {
            if (!ReferenceEquals(app.Resources, originalResources)) app.Resources = originalResources;
            ShieldOwner.Reset(app);
            log.Dump("shield ownership");
        }
    }
}
