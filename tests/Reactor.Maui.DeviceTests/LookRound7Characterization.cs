using System.Runtime.CompilerServices;
using Microsoft.Maui.Layouts;
using Microsoft.UI.Reactor.Maui;
using Microsoft.UI.Reactor.Maui.Direct;
using Reactor.Maui.DeviceTests.Shadow;
using static Reactor.Maui.DeviceTests.FixtureContext;
using MauiButton = Microsoft.Maui.Controls.Button;
using MauiLabel = Microsoft.Maui.Controls.Label;

namespace Reactor.Maui.DeviceTests;

/// <summary>
/// Round 7, K1 option (1): a uniquely named projected layout publishes the stock type's implicit style under its own
/// implicit key, the same <see cref="Style"/> object, in a merged dictionary of its own resources. Two trackers
/// read MAUI's dynamic-resource resolution: one parented to the layout (the stock key, so the layout's own
/// resources count) and one parented to the layout's parent (the layout's own key and its style classes, so the
/// alias never reads itself). An upstream style keyed by the projected type wins over the alias, as a first key
/// does. A style-class list is republished with a wrapper per non-derived stock-type style
/// (<c>new Style(projected) { BasedOn = s }</c>), the original list after them, because
/// <c>Style.CanBeAppliedTo</c> compares type identity.
/// </summary>
public sealed class ImplicitAlias
{
    private const string ClassPrefix = "Microsoft.Maui.Controls.StyleClass.";
    private static readonly BindableProperty StockProperty = BindableProperty.CreateAttached("AliasStock", typeof(object), typeof(ImplicitAlias), null, propertyChanged: Changed);
    private static readonly BindableProperty OwnProperty = BindableProperty.CreateAttached("AliasOwn", typeof(object), typeof(ImplicitAlias), null, propertyChanged: Changed);
    private static readonly Dictionary<string, BindableProperty> ClassProperties = [];
    private static readonly Dictionary<Type, ConditionalWeakTable<Style, Style>> Wrappers = [];

    private readonly Layout _layout;
    private readonly Type _stock;
    private readonly string _ownKey;
    private readonly ResourceDictionary _aliases = [];
    private readonly Tracker _inner, _outer;
    private string[] _classes = [];
    public int Recomputes;

    private sealed class Tracker(ImplicitAlias owner) : Microsoft.Maui.Controls.Element
    {
        public ImplicitAlias Owner { get; } = owner;
    }

    public ImplicitAlias(Layout layout, Type stock)
    {
        _layout = layout;
        _stock = stock;
        _ownKey = layout.GetType().FullName!;
        layout.Resources.MergedDictionaries.Add(_aliases);
        _inner = new Tracker(this) { Parent = layout };
        _inner.SetDynamicResource(StockProperty, stock.FullName);
        _outer = new Tracker(this);
        _outer.SetDynamicResource(OwnProperty, _ownKey);
        layout.ParentChanged += (_, _) => { _outer.Parent = layout.Parent; Recompute(); };
        Recompute();
    }

    /// <summary>Reactor's <c>.StyleClass</c> modifier would call this after writing the classes.</summary>
    public void SyncClasses()
    {
        _classes = [.. _layout.StyleClass ?? []];
        foreach (var name in _classes)
        {
            if (!ClassProperties.TryGetValue(name, out var property))
                ClassProperties[name] = property = BindableProperty.CreateAttached("AliasClass" + name, typeof(object), typeof(ImplicitAlias), null, propertyChanged: Changed);
            _outer.SetDynamicResource(property, ClassPrefix + name);
        }
        Recompute();
    }

    private static void Changed(BindableObject bindable, object oldValue, object newValue) => ((Tracker)bindable).Owner.Recompute();

    private void Recompute()
    {
        // The trackers resolve Application.Resources as soon as they subscribe, before both exist.
        if (_inner is null || _outer is null) return;
        Recomputes++;
        var own = _outer.GetValue(OwnProperty) as Style;
        var stock = _inner.GetValue(StockProperty) as Style;
        Publish(_ownKey, own ?? stock);
        foreach (var name in _classes)
        {
            var list = _outer.GetValue(ClassProperties[name]) as IList<Style>;
            Publish(ClassPrefix + name, list is null ? null : (IList<Style>)[.. list.Where(NeedsWrapper).Select(Wrap), .. list]);
        }
    }

    private bool NeedsWrapper(Style style) => style.TargetType == _stock && !style.ApplyToDerivedTypes;

    private Style Wrap(Style style)
    {
        var type = _layout.GetType();
        if (!Wrappers.TryGetValue(type, out var table)) Wrappers[type] = table = new();
        return table.GetValue(style, s => new Style(type) { BasedOn = s });
    }

    private void Publish(string key, object? value)
    {
        if (_aliases.TryGetValue(key, out var current))
        {
            if (ReferenceEquals(current, value)) return;
            if (current is IList<Style> a && value is IList<Style> b && a.SequenceEqual(b)) return;
        }
        else if (value is null) return;
        _aliases[key] = value;
    }
}

public interface IAliased
{
    ImplicitAlias Alias { get; }
}

public sealed class AliasedVerticalStackLayout : VerticalStackLayout, IVisualTreeElement, IAliased
{
    public AliasedVerticalStackLayout() => Alias = new ImplicitAlias(this, typeof(VerticalStackLayout));
    public ImplicitAlias Alias { get; }
    IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => [.. this.OfType<IVisualTreeElement>()];
    IVisualTreeElement? IVisualTreeElement.GetVisualParent() => Parent;
}

public sealed class AliasedHorizontalStackLayout : HorizontalStackLayout, IVisualTreeElement, IAliased
{
    public AliasedHorizontalStackLayout() => Alias = new ImplicitAlias(this, typeof(HorizontalStackLayout));
    public ImplicitAlias Alias { get; }
    IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => [.. this.OfType<IVisualTreeElement>()];
    IVisualTreeElement? IVisualTreeElement.GetVisualParent() => Parent;
}

public sealed class AliasedGrid : Grid, IVisualTreeElement, IAliased
{
    public AliasedGrid() => Alias = new ImplicitAlias(this, typeof(Grid));
    public ImplicitAlias Alias { get; }
    IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => [.. this.OfType<IVisualTreeElement>()];
    IVisualTreeElement? IVisualTreeElement.GetVisualParent() => Parent;
}

public sealed class AliasedFlexLayout : FlexLayout, IVisualTreeElement, IAliased
{
    public AliasedFlexLayout() => Alias = new ImplicitAlias(this, typeof(FlexLayout));
    public ImplicitAlias Alias { get; }
    IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => [.. this.OfType<IVisualTreeElement>()];
    IVisualTreeElement? IVisualTreeElement.GetVisualParent() => Parent;
}

public sealed class AliasedAbsoluteLayout : AbsoluteLayout, IVisualTreeElement, IAliased
{
    public AliasedAbsoluteLayout() => Alias = new ImplicitAlias(this, typeof(AbsoluteLayout));
    public ImplicitAlias Alias { get; }
    IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => [.. this.OfType<IVisualTreeElement>()];
    IVisualTreeElement? IVisualTreeElement.GetVisualParent() => Parent;
}

/// <summary>Counts attachments per target (K1 behaviors, K2 shield timing).</summary>
public sealed class CountingBehavior : Behavior<VisualElement>
{
    public static readonly ConditionalWeakTable<BindableObject, StrongBox<int>> Attached = new();
    public static int Of(BindableObject target) => Attached.TryGetValue(target, out var box) ? box.Value : 0;
    protected override void OnAttachedTo(VisualElement bindable) { Attached.GetOrCreateValue(bindable).Value++; base.OnAttachedTo(bindable); }
}

/// <summary>An attached property whose changes are counted per target: an observable style-setter callback (K2).</summary>
public static class StyleProbe
{
    public static readonly ConditionalWeakTable<BindableObject, StrongBox<int>> Changes = new();
    public static readonly BindableProperty MarkProperty = BindableProperty.CreateAttached("Mark", typeof(int), typeof(StyleProbe), 0,
        propertyChanged: (b, _, _) => Changes.GetOrCreateValue(b).Value++);
    public static int Of(BindableObject target) => Changes.TryGetValue(target, out var box) ? box.Value : 0;
}

// Spec 071 §2.1, the seventh review round of 3e8770bb: the style matrix for every projection option (K1), the
// shield's timing (K2), leaf bridges with every direct primitive (K3) and bridge inertness (K4). On demand.
public static partial class Fixtures
{
    private static string[] MatrixCandidates => ["stock", "plain subclass", "shadow (c')", "aliased (1)"];

    private static IEnumerable<Fixture> LookRound7CharacterizationFixtures() =>
    [
        .. LayoutKinds.Select(kind => new Fixture($"Characterize_Look_StyleMatrix [{kind}]", c => LookStyleMatrix(c, kind), 300, OnDemand: true)),
        new("Characterize_Look_ShieldTiming", LookShieldTiming, 60, OnDemand: true),
        new("Characterize_Look_LeafBridges [(2) stock layouts, representing bridges]", c => LookLeafBridges(c, aliased: false), 90, OnDemand: true),
        new("Characterize_Look_LeafBridges [(1) aliased layouts, bridges out of enumeration]", c => LookLeafBridges(c, aliased: true), 90, OnDemand: true),
    ];

    private static Layout AliasedLayout(string kind) => kind switch
    {
        "VerticalStackLayout" => new AliasedVerticalStackLayout(),
        "HorizontalStackLayout" => new AliasedHorizontalStackLayout(),
        "Grid" => new AliasedGrid(),
        "FlexLayout" => new AliasedFlexLayout(),
        _ => new AliasedAbsoluteLayout(),
    };

    private static Layout MatrixLayout(string kind, string candidate) => candidate switch
    {
        "stock" => StockLayout(kind),
        "plain subclass" => PlainSubLayout(kind),
        "shadow (c')" => ShadowLayouts.Create(kind),
        _ => AliasedLayout(kind),
    };

    private static void SetClasses(Layout layout, params string[] classes)
    {
        layout.StyleClass = classes;
        (layout as IAliased)?.Alias.SyncClasses();
    }

    private static string Pad(Layout l) => $"{l.Padding.Left}";
    private static string PadBg(Layout l) => $"{l.Padding.Left}/{(l.BackgroundColor is { } bg ? bg.ToArgbHex() : "-")}";

    private sealed record MatrixCase(string Name, Func<FixtureContext, string, Func<Layout>, Task<string>> Run);

    /// <summary>One candidate alone in a ContentView holding <paramref name="styles"/>, with a Controls button child.</summary>
    private static async Task<(ContentView Host, Layout Layout)> MountCandidate(FixtureContext c, Func<Layout> make, params Style[] styles)
    {
        var host = new ContentView();
        foreach (var style in styles) host.Resources[style.Class is { Length: > 0 } cls ? $"Microsoft.Maui.Controls.StyleClass.{cls}" : style.TargetType.FullName!] = style.Class is { Length: > 0 } ? new List<Style> { style } : style;
        var layout = make();
        layout.Add(new MauiButton { Text = "child" });
        host.Content = layout;
        c.ShowNative(host);
        await NativeLoaded(c, (View)layout[0]);
        return (host, layout);
    }

    private static Style S(Type target, bool derived = false, string? cls = null, params (BindableProperty, object)[] setters)
    {
        var style = LayoutStyle(target, derived, setters);
        if (cls is not null) style.Class = cls;
        return style;
    }

    private static IEnumerable<MatrixCase> MatrixCases(Application app)
    {
        var pad = Layout.PaddingProperty;
        var bg = VisualElement.BackgroundColorProperty;
        // I1–I8: round 6's implicit scenarios.
        yield return new("I1 the kind's style", async (c, k, make) => PadBg((await MountCandidate(c, make, S(StockLayout(k).GetType(), false, null, (pad, new Thickness(7)), (bg, Colors.Teal)))).Layout));
        yield return new("I2 the kind's style, IsEnabled false", async (c, k, make) => { var (_, l) = await MountCandidate(c, make, S(StockLayout(k).GetType(), false, null, (VisualElement.IsEnabledProperty, false))); return $"{l.IsEnabled}/{((VisualElement)l[0]).IsEnabled}"; });
        yield return new("I3 Layout style", async (c, k, make) => Pad((await MountCandidate(c, make, S(typeof(Layout), false, null, (pad, new Thickness(9))))).Layout));
        yield return new("I4 Layout style, derived", async (c, k, make) => PadBg((await MountCandidate(c, make, S(typeof(Layout), true, null, (pad, new Thickness(11)), (bg, Colors.Orange)))).Layout));
        yield return new("I5 the kind's style beside a derived Layout style", async (c, k, make) => Pad((await MountCandidate(c, make, S(StockLayout(k).GetType(), false, null, (pad, new Thickness(7))), S(typeof(Layout), true, null, (pad, new Thickness(11))))).Layout));
        yield return new("I6 themed setter: Light, Dark, Light", async (c, k, make) =>
        {
            var (_, l) = await MountCandidate(c, make, S(StockLayout(k).GetType(), false, null, (bg, new AppThemeBinding { Light = LightRed, Dark = DarkBlue })));
            var light = PadBg(l);
            app.UserAppTheme = AppTheme.Dark; await c.Settle(200);
            var dark = PadBg(l);
            app.UserAppTheme = AppTheme.Light; await c.Settle(200);
            return $"{light} {dark} {PadBg(l)}";
        });
        yield return new("I7 declared 3, cleared", async (c, k, make) =>
        {
            var (_, l) = await MountCandidate(c, make, S(StockLayout(k).GetType(), false, null, (pad, new Thickness(7))));
            var before = Pad(l);
            l.Padding = new Thickness(3); var local = Pad(l);
            l.ClearValue(pad);
            return $"{before} {local} {Pad(l)}";
        });
        yield return new("I8 replaced, restored, removed", async (c, k, make) =>
        {
            var type = StockLayout(k).GetType();
            var (host, l) = await MountCandidate(c, make, S(type, false, null, (pad, new Thickness(7))));
            var original = host.Resources[type.FullName!];
            var first = Pad(l);
            host.Resources[type.FullName!] = S(type, false, null, (pad, new Thickness(13))); await c.Settle(50);
            var replaced = Pad(l);
            host.Resources[type.FullName!] = original; await c.Settle(50);
            var restored = Pad(l);
            host.Resources.Remove(type.FullName!); await c.Settle(50);
            return $"{first} {replaced} {restored} {Pad(l)}";
        });
        // C: style classes.
        yield return new("C1 class, not derived", async (c, k, make) => { var (h, l) = await MountCandidate(c, make, S(StockLayout(k).GetType(), false, "k", (pad, new Thickness(15)))); SetClasses(l, "k"); await c.Settle(80); return Pad(l); });
        yield return new("C2 class, derived", async (c, k, make) => { var (h, l) = await MountCandidate(c, make, S(StockLayout(k).GetType(), true, "k", (pad, new Thickness(15)))); SetClasses(l, "k"); await c.Settle(80); return Pad(l); });
        yield return new("C3 implicit 7 and class 15", async (c, k, make) => { var t = StockLayout(k).GetType(); var (h, l) = await MountCandidate(c, make, S(t, false, null, (pad, new Thickness(7))), S(t, false, "k", (pad, new Thickness(15)))); SetClasses(l, "k"); await c.Settle(80); return Pad(l); });
        yield return new("C4 class 15, declared 3", async (c, k, make) => { var (h, l) = await MountCandidate(c, make, S(StockLayout(k).GetType(), false, "k", (pad, new Thickness(15)))); SetClasses(l, "k"); await c.Settle(80); l.Padding = new Thickness(3); return Pad(l); });
        yield return new("C5 class on a stock descendant of the same kind", async (c, k, make) =>
        {
            var (h, l) = await MountCandidate(c, make, S(StockLayout(k).GetType(), false, "k", (pad, new Thickness(15))));
            SetClasses(l, "k");
            var inner = StockLayout(k);
            inner.StyleClass = ["k"];
            l.Add(inner);
            await c.Settle(150);
            return $"{Pad(l)} inner {Pad(inner)}";
        });
        yield return new("C6 class list replaced", async (c, k, make) =>
        {
            var t = StockLayout(k).GetType();
            var (h, l) = await MountCandidate(c, make, S(t, false, "k", (pad, new Thickness(15))));
            SetClasses(l, "k"); await c.Settle(80);
            var first = Pad(l);
            h.Resources["Microsoft.Maui.Controls.StyleClass.k"] = new List<Style> { S(t, false, "k", (pad, new Thickness(16))) };
            await c.Settle(80);
            return $"{first} {Pad(l)}";
        });
        // E: an explicit style.
        yield return new("E1 explicit stock-type style", async (c, k, make) => { var (h, l) = await MountCandidate(c, make); l.Style = S(StockLayout(k).GetType(), false, null, (pad, new Thickness(27))); await c.Settle(50); return Pad(l); });
        // R: runtime styles from GetType().
        yield return new("R1 a style from GetType() beside the kind's style; a stock sibling", async (c, k, make) =>
        {
            var t = StockLayout(k).GetType();
            var host = new ContentView();
            host.Resources[t.FullName!] = S(t, false, null, (pad, new Thickness(7)));
            var l = make();
            l.Add(new MauiButton { Text = "child" });
            var sibling = StockLayout(k);
            sibling.Add(new MauiButton { Text = "sibling" });
            host.Content = new StackLayout { Children = { l, sibling } };
            c.ShowNative(host);
            await NativeLoaded(c, (View)sibling[0]);
            CapturedLogs.Clear();
            string outcome;
            try { host.Resources[l.GetType().FullName!] = S(l.GetType(), false, null, (pad, new Thickness(17))); outcome = "ok"; }
            catch (Exception ex) { outcome = $"threw {ex.GetType().Name}"; }
            await c.Settle(80);
            var warnings = CapturedLogs.Lines.Count(x => x.Contains("TargetType", StringComparison.Ordinal));
            return $"{outcome}: candidate {Pad(l)}, stock sibling {Pad(sibling)}, TargetType warnings {warnings}";
        });
        yield return new("R2 BasedOn: a GetType() style based on the stock style", async (c, k, make) =>
        {
            var t = StockLayout(k).GetType();
            var (h, l) = await MountCandidate(c, make, S(t, false, null, (pad, new Thickness(7))));
            try
            {
                var derived = S(l.GetType(), false, null, (bg, Colors.Teal));
                derived.BasedOn = (Style)h.Resources[t.FullName!];
                h.Resources[l.GetType().FullName!] = derived;
                await c.Settle(80);
                return PadBg(l);
            }
            catch (Exception ex) { return $"threw {ex.GetType().Name}"; }
        });
        yield return new("R3 reverse BasedOn: a stock-type style based on a GetType() style", (c, k, make) =>
        {
            var l = make();
            try { _ = new Style(StockLayout(k).GetType()) { BasedOn = new Style(l.GetType()) }; return Task.FromResult("ok"); }
            catch (Exception ex) { return Task.FromResult($"threw {ex.GetType().Name}"); }
        });
        // A: Application.Resources present at construction.
        yield return new("A1 Application.Resources style present at construction, with a behavior", async (c, k, make) =>
        {
            var key = StockLayout(k).GetType().FullName!;
            var style = S(StockLayout(k).GetType(), false, null, (pad, new Thickness(19)));
            style.Behaviors.Add(new CountingBehavior());
            app.Resources[key] = style;
            try
            {
                var (_, l) = await MountCandidate(c, make);
                await c.Settle(50);
                return $"{Pad(l)} behavior attached {CountingBehavior.Of(l)}";
            }
            finally { app.Resources.Remove(key); }
        });
        // N: nested and moved scopes.
        yield return new("N1 nested: inner 9 over outer 7; inner → 11; outer → 13", async (c, k, make) =>
        {
            var t = StockLayout(k).GetType();
            var outer = new ContentView();
            outer.Resources[t.FullName!] = S(t, false, null, (pad, new Thickness(7)));
            var inner = new ContentView();
            inner.Resources[t.FullName!] = S(t, false, null, (pad, new Thickness(9)));
            var l = make();
            l.Add(new MauiButton { Text = "child" });
            inner.Content = l;
            outer.Content = inner;
            c.ShowNative(outer);
            await NativeLoaded(c, (View)l[0]);
            var first = Pad(l);
            inner.Resources[t.FullName!] = S(t, false, null, (pad, new Thickness(11))); await c.Settle(50);
            var innerReplaced = Pad(l);
            outer.Resources[t.FullName!] = S(t, false, null, (pad, new Thickness(13))); await c.Settle(50);
            return $"{first} {innerReplaced} {Pad(l)}";
        });
        yield return new("N2 moved to a scope without the style and back", async (c, k, make) =>
        {
            var t = StockLayout(k).GetType();
            var styled = new ContentView();
            styled.Resources[t.FullName!] = S(t, false, null, (pad, new Thickness(7)));
            var bare = new ContentView();
            var l = make();
            l.Add(new MauiButton { Text = "child" });
            styled.Content = l;
            c.ShowNative(new StackLayout { Children = { styled, bare } });
            await NativeLoaded(c, (View)l[0]);
            var first = Pad(l);
            styled.Content = null; bare.Content = l; await c.Settle(100);
            var moved = Pad(l);
            bare.Content = null; styled.Content = l; await c.Settle(100);
            return $"{first} {moved} {Pad(l)}";
        });
        // V: a visual state from the style; B: a behavior.
        yield return new("V1 the style's Disabled state: enabled, disabled, enabled", async (c, k, make) =>
        {
            var t = StockLayout(k).GetType();
            var style = S(t, false, null, (pad, new Thickness(7)));
            var groups = new VisualStateGroupList();
            var common = new VisualStateGroup { Name = "CommonStates" };
            common.States.Add(new VisualState { Name = "Normal" });
            var disabled = new VisualState { Name = "Disabled" };
            disabled.Setters.Add(new Setter { Property = bg, Value = Colors.Gray });
            common.States.Add(disabled);
            groups.Add(common);
            style.Setters.Add(new Setter { Property = VisualStateManager.VisualStateGroupsProperty, Value = groups });
            var (_, l) = await MountCandidate(c, make, style);
            var a = PadBg(l);
            l.IsEnabled = false; await c.Settle(50);
            var b = PadBg(l);
            l.IsEnabled = true; await c.Settle(50);
            return $"{a} {b} {PadBg(l)}";
        });
        yield return new("B1 the style's behavior: attached, then the style replaced", async (c, k, make) =>
        {
            var t = StockLayout(k).GetType();
            var style = S(t, false, null, (pad, new Thickness(7)));
            style.Behaviors.Add(new CountingBehavior());
            var (h, l) = await MountCandidate(c, make, style);
            var first = CountingBehavior.Of(l);
            h.Resources[t.FullName!] = S(t, false, null, (pad, new Thickness(8))); await c.Settle(50);
            return $"attached {first}, after replacement {CountingBehavior.Of(l)} (padding {Pad(l)})";
        });
    }

    /// <summary>
    /// K1: every case of the style matrix on one layout kind, for each projection candidate: stock (the oracle), a
    /// plain subclass (negative control), the shadow full name (c') and the aliased subclass (1). Each candidate runs
    /// alone in its own tree; the log lists stock's observation and each other candidate's equality.
    /// </summary>
    private static async Task LookStyleMatrix(FixtureContext c, string kind)
    {
        var app = Application.Current!;
        var original = app.UserAppTheme;
        var log = new LookLog { Phase = kind };
        var mismatches = MatrixCandidates.ToDictionary(x => x, _ => new List<string>());
        try
        {
            app.UserAppTheme = AppTheme.Light;
            foreach (var test in MatrixCases(app))
            {
                log.Phase = test.Name;
                var results = new List<string>();
                foreach (var candidate in MatrixCandidates)
                {
                    string result;
                    try { result = await test.Run(c, kind, () => MatrixLayout(kind, candidate)); }
                    catch (Exception ex) { result = $"fixture threw {ex.GetType().Name}: {ex.Message}"; log.Add($"{candidate} threw: {ex.ToString().Replace('\n', ' ')[..Math.Min(900, ex.ToString().Length)]}"); }
                    results.Add(result);
                    c.ShowNative(new ContentView());
                    await c.Settle(30);
                }
                log.Add($"stock: {results[0]}");
                for (var i = 1; i < MatrixCandidates.Length; i++)
                {
                    if (results[i] != results[0]) mismatches[MatrixCandidates[i]].Add(test.Name.Split(' ')[0]);
                    log.Add($"{MatrixCandidates[i]}: {(results[i] == results[0] ? "equal" : $"DIFFERS: {results[i]}")}");
                }
            }
            log.Phase = "summary";
            foreach (var candidate in MatrixCandidates.Skip(1))
                log.Add($"{candidate}: {mismatches[candidate].Count} mismatches{(mismatches[candidate].Count == 0 ? "" : $" ({string.Join(", ", mismatches[candidate])})")}");
            Check(true, "measured");
        }
        finally
        {
            app.UserAppTheme = original;
            log.Dump($"style matrix, {kind}");
        }
    }

    /// <summary>
    /// K2, K4: when Application.Resources styles reach a node. A derived <c>Layout</c> style in Application.Resources
    /// sets an attached probe (counted per target) and carries a counting behavior. Bridges: shielded in their
    /// constructor, and bare (the positive control), with a stock Grid as a second positive control. Counted after
    /// construction, after parenting into the live page, after a theme switch and after a move; and the bridges'
    /// handler attachments and layout-manager calls.
    /// </summary>
    private static async Task LookShieldTiming(FixtureContext c)
    {
        var app = Application.Current!;
        var original = app.UserAppTheme;
        var log = new LookLog { Phase = "timing" };
        var key = typeof(Layout).FullName!;
        var style = S(typeof(Layout), true, null, (StyleProbe.MarkProperty, 1), (Layout.PaddingProperty, new Thickness(21)));
        style.Behaviors.Add(new CountingBehavior());
        app.Resources[key] = style;
        var materializations = LayoutBridge.Materializations;
        var layoutCalls = LayoutBridge.LayoutCalls;
        try
        {
            var shielded = new LayoutBridge(new DirectStackLayout(StackOrientation.Vertical), shield: true);
            var bare = new LayoutBridge(new DirectStackLayout(StackOrientation.Vertical));
            var grid = new Grid();
            // The app-level shield: the bridge type's own key in Application.Resources before construction.
            var appShieldKey = typeof(LayoutBridge).FullName!;
            app.Resources[appShieldKey] = LayoutBridge.ShieldStyle;
            var appShielded = new LayoutBridge(new DirectStackLayout(StackOrientation.Vertical), shield: true);
            string Counts() => $"node shield only: setter {StyleProbe.Of(shielded)}, behavior {CountingBehavior.Of(shielded)}, padding {shielded.Padding.Left}; app and node shield: setter {StyleProbe.Of(appShielded)}, behavior {CountingBehavior.Of(appShielded)}, padding {appShielded.Padding.Left}; bare: setter {StyleProbe.Of(bare)}, behavior {CountingBehavior.Of(bare)}, padding {bare.Padding.Left}; stock Grid: setter {StyleProbe.Of(grid)}, behavior {CountingBehavior.Of(grid)}, padding {grid.Padding.Left}";
            log.Add($"after construction: {Counts()}");
            var host = new ContentView();
            host.AddLogicalChild(shielded);
            host.AddLogicalChild(bare);
            host.AddLogicalChild(appShielded);
            log.Add($"parented to a detached ContentView: {Counts()}");
            host.Content = grid;
            c.ShowNative(host);
            await NativeLoaded(c, grid);
            log.Add($"in the live page: {Counts()}");
            app.UserAppTheme = app.RequestedTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
            await c.Settle(200);
            log.Add($"after a theme switch: {Counts()}");
            var other = new ContentView();
            host.RemoveLogicalChild(shielded);
            other.AddLogicalChild(shielded);
            host.Content = other;
            await c.Settle(200);
            log.Add($"shielded moved under another ContentView: {Counts()}");
            log.Add($"bridges' handler attachments {LayoutBridge.Materializations - materializations}, layout-manager calls {LayoutBridge.LayoutCalls - layoutCalls}");
            Check(true, "measured");
        }
        finally
        {
            app.Resources.Remove(key);
            app.Resources.Remove(typeof(LayoutBridge).FullName!);
            app.UserAppTheme = original;
            log.Dump("when app styles reach a bridge");
        }
    }

    /// <summary>
    /// K3, K4: a Controls stack H holding every direct primitive beside Controls views: [label A, direct label,
    /// button, entry, image, check box, switch, slider, progress bar, activity indicator, direct VStack (Controls
    /// button B, direct label), direct Grid (Controls label G), label C]. Mode (2): a stock H, each direct child with a
    /// representing bridge in its slot (leaves enumerate nothing), shielded. Mode (1): an aliased H, bridges out of
    /// enumeration. Recorded: the runner-style traversal and MAUI's own GetVisualTreeDescendants, a hit-test helper
    /// (GetVisualTreeElements at B), native taps on the direct button and on B, a removal and a re-insertion, and the
    /// bridges' handler attachments and layout-manager calls.
    /// </summary>
    private static async Task LookLeafBridges(FixtureContext c, bool aliased)
    {
        var log = new LookLog { Phase = aliased ? "(1)" : "(2)" };
        var materializations = LayoutBridge.Materializations;
        var layoutCalls = LayoutBridge.LayoutCalls;
        try
        {
            Layout h = aliased ? new AliasedVerticalStackLayout() : new VerticalStackLayout();
            h.AutomationId = "H";
            var directClicks = 0;
            var bClicks = 0;
            var label = NewDirectLabel("dLabel");
            var button = new DirectButton();
            button.Apply(new ButtonElement("dButton"));
            button.ApplyModifiers(null, new ViewModifiers { AutomationId = "dButton", Height = 50 }, mounting: true);
            button.OnClicked = () => directClicks++;
            var entry = new DirectEntry();
            entry.Apply(new EntryElement("dEntry"));
            var image = new DirectImage();
            image.Apply(null, new ImageElement(null));
            var check = new DirectCheckBox();
            check.Apply(new CheckBoxElement(true));
            var toggle = new DirectSwitch();
            toggle.Apply(new SwitchElement(true));
            var slider = new DirectSlider();
            slider.Apply(new SliderElement(0.5));
            var progress = new DirectProgressBar();
            progress.Apply(new ProgressBarElement(0.5));
            var spinner = new DirectActivityIndicator();
            spinner.Apply(new ActivityIndicatorElement(true));
            var stack = new DirectStackLayout(StackOrientation.Vertical) { AutomationId = "dStack" };
            var b = new MauiButton { Text = "B", HeightRequest = 50 };
            b.Clicked += (_, _) => bClicks++;
            var grid = new DirectGrid { AutomationId = "dGrid" };
            var g = new MauiLabel { Text = "G" };
            var a = new MauiLabel { Text = "A" };
            var cl = new MauiLabel { Text = "C" };
            var directs = new DirectView[] { label, button, entry, image, check, toggle, slider, progress, spinner, stack, grid };

            void AddDirect(DirectView view, int slot)
            {
                h.Insert(slot, view);
                view.Parent = h;
                var bridge = new LayoutBridge(view, shield: true) { Represents = !aliased };
                // Slot-aligned among H's logical children: the count of views before it (view-index semantics).
                h.InsertLogicalChild(Math.Min(slot, ((IElementController)h).LogicalChildren.Count), bridge);
            }
            h.Add(a);
            foreach (var d in directs) AddDirect(d, h.Count);
            h.Add(cl);
            var stackBridge = LayoutBridge.Of(stack)!;
            stackBridge.AddLogicalChild(b);
            stack.Add(b);
            stack.Add(NewDirectLabel("dInner"));
            var gridBridge = LayoutBridge.Of(grid)!;
            gridBridge.AddLogicalChild(g);
            grid.Add(g);
            c.ShowNative(new ScrollView { Content = h });
            await NativeLoaded(c, b);
            await c.Settle(300);

            string Name(IVisualTreeElement node) => node switch
            {
                LayoutBridge br => $"bridge({Name((IVisualTreeElement)br.Presented)})",
                MauiLabel l => l.Text,
                MauiButton btn => btn.Text,
                DirectLabel dl => $"d:{dl.Text}",
                DirectButton => "d:button",
                DirectEntry => "d:entry",
                DirectImage => "d:image",
                DirectCheckBox => "d:check",
                DirectSwitch => "d:switch",
                DirectSlider => "d:slider",
                DirectProgressBar => "d:progress",
                DirectActivityIndicator => "d:spinner",
                IView { AutomationId: { Length: > 0 } id } => id,
                _ => node.GetType().Name,
            };
            void Traverse(string when)
            {
                var order = new List<string>();
                var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
                var duplicates = 0;
                var mismatches = new List<string>();
                void Walk(IVisualTreeElement node, IVisualTreeElement? by)
                {
                    order.Add(Name(node));
                    if (!seen.Add(node)) duplicates++;
                    if (by is not null && !ReferenceEquals(node.GetVisualParent(), by)) mismatches.Add(Name(node));
                    foreach (var child in node.GetVisualChildren()) Walk(child, node);
                }
                Walk(h, null);
                // A tool's normalization for (2): a bridge stands for the view it presents.
                var normalized = order.Select(x => x.StartsWith("bridge(", StringComparison.Ordinal) ? x[7..^1] : x);
                log.Add($"{when}: traversal {string.Join(" > ", order)}");
                log.Add($"{when}: duplicates {duplicates}; bridges {order.Count(x => x.StartsWith("bridge(", StringComparison.Ordinal))}; parent mismatches: {(mismatches.Count == 0 ? "none" : string.Join(", ", mismatches))}; normalized {string.Join(" > ", normalized)}");
                try
                {
                    var descendants = ((IVisualTreeElement)h).GetVisualTreeDescendants();
                    log.Add($"{when}: MAUI GetVisualTreeDescendants {descendants.Count} nodes, bridges {descendants.Count(x => x is LayoutBridge)}");
                }
                catch (Exception ex) { log.Add($"{when}: GetVisualTreeDescendants threw {ex.GetType().Name}: {ex.Message}"); }
            }
            Traverse("mounted");

            try
            {
                var frame = Platform.WindowFrame(b);
                var hits = c.Window.GetVisualTreeElements(frame.Center.X, frame.Center.Y);
                log.Add($"GetVisualTreeElements at B: {string.Join(", ", hits.Select(Name))}");
            }
            catch (Exception ex) { log.Add($"GetVisualTreeElements threw {ex.GetType().Name}: {ex.Message}"); }

            async Task Tap(IView target)
            {
#if IOS || MACCATALYST
                var native = (UIKit.UIView)target.Handler!.PlatformView!;
                if (native is UIKit.UIControl control) control.SendActionForControlEvents(UIKit.UIControlEvent.TouchUpInside);
                await c.Settle(200);
#elif ANDROID
                var native = (Android.Views.View)target.Handler!.PlatformView!;
                var activity = (Android.App.Activity)c.Window.Handler!.PlatformView!;
                var decor = activity.Window!.DecorView;
                var at = new int[2];
                native.GetLocationInWindow(at);
                var origin = new int[2];
                decor.GetLocationInWindow(origin);
                float x = at[0] - origin[0] + native.Width / 2f, y = at[1] - origin[1] + native.Height / 2f;
                var now = Android.OS.SystemClock.UptimeMillis();
                using (var down = Android.Views.MotionEvent.Obtain(now, now, Android.Views.MotionEventActions.Down, x, y, 0)!) decor.DispatchTouchEvent(down);
                using (var up = Android.Views.MotionEvent.Obtain(now, now + 60, Android.Views.MotionEventActions.Up, x, y, 0)!) decor.DispatchTouchEvent(up);
                await c.Settle(350);
#else
                await Task.CompletedTask;
#endif
            }
            await Tap(button);
            await Tap(b);
            log.Add($"taps: direct button clicked {directClicks}, B clicked {bClicks}");

            // Lifecycle: the entry and its bridge removed, then re-inserted after the image; a new direct label at 1.
            var entryBridge = LayoutBridge.Of(entry)!;
            h.Remove(entry);
            h.RemoveLogicalChild(entryBridge);
            entry.Parent = null;
            var imageSlot = h.IndexOf(image) + 1;
            h.Insert(imageSlot, entry);
            entry.Parent = h;
            h.InsertLogicalChild(Math.Min(imageSlot, ((IElementController)h).LogicalChildren.Count), entryBridge);
            AddDirect(NewDirectLabel("dNew"), 1);
            await c.Settle(300);
            Traverse("after a removal, a re-insertion and an insertion");
            log.Add($"logical children of H: {string.Join(", ", ((IElementController)h).LogicalChildren.Select(x => Name(x)))}");
            log.Add($"bridges' handler attachments {LayoutBridge.Materializations - materializations}, layout-manager calls {LayoutBridge.LayoutCalls - layoutCalls}; B window {b.Window is not null}");
            Check(true, "measured");
        }
        finally
        {
            log.Dump("leaf bridges with every direct primitive");
        }
    }
}
