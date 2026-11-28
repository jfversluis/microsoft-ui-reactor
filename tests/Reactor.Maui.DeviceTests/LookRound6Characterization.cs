using System.Diagnostics;
using Microsoft.Maui.Layouts;
using Microsoft.UI.Reactor.Maui;
using Microsoft.UI.Reactor.Maui.Direct;
using Reactor.Maui.DeviceTests.Shadow;
using static Reactor.Maui.DeviceTests.FixtureContext;
using MauiButton = Microsoft.Maui.Controls.Button;
using MauiEntry = Microsoft.Maui.Controls.Entry;
using MauiLabel = Microsoft.Maui.Controls.Label;

namespace Reactor.Maui.DeviceTests;

// J1's negative control: plain subclasses, as round 5's ProjectedStackLayout was.
public sealed class PlainSubVerticalStackLayout : VerticalStackLayout { }
public sealed class PlainSubHorizontalStackLayout : HorizontalStackLayout { }
public sealed class PlainSubGrid : Grid { }
public sealed class PlainSubFlexLayout : FlexLayout { }
public sealed class PlainSubAbsoluteLayout : AbsoluteLayout { }

/// <summary>
/// The bridge as a <see cref="Layout"/> (J3): MAUI's input-transparency cascade walks <c>Parent</c> only through
/// <c>IInputTransparentContainerElement</c>, which is internal and which <see cref="Layout"/> implements. Its own
/// <c>IsEnabled</c>, <c>InputTransparent</c> and <c>CascadeInputTransparent</c> are local values Reactor writes (the
/// direct view's declaration). With <see cref="Represents"/>, it is the managed node for its direct view in a stock
/// layout's enumeration (J1 candidate b); otherwise it stays out of enumeration (round 5's T3b) and pulls MAUI's
/// propagated state when its logical parent changes, now including <c>CascadeInputTransparent</c>.
/// </summary>
public sealed class LayoutBridge : Layout, IVisualTreeElement
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IView, LayoutBridge> Bridges = new();
    private Microsoft.Maui.Controls.Element? _watched;

    /// <summary>The empty style shared by every shield (round 7, K4: one dictionary per node, one style object).</summary>
    public static readonly Style ShieldStyle = new(typeof(LayoutBridge));

    /// <summary>Handler attachments and layout-manager calls on any bridge (round 7, K4); must stay 0.</summary>
    public static int Materializations, LayoutCalls;

    public LayoutBridge(IView presented, bool shield = false)
    {
        Presented = presented;
        Bridges.AddOrUpdate(presented, this);
        // The earliest a per-node shield can exist: after the base constructors (round 7, K2).
        if (shield) Resources = new ResourceDictionary { ShieldStyle };
    }

    /// <summary>A bridge whose own dictionary holds <paramref name="nodeShield"/> (round 8, L1: the node's private shield).</summary>
    public LayoutBridge(IView presented, Style nodeShield) : this(presented)
    {
        Resources = new ResourceDictionary { nodeShield };
    }

    public static LayoutBridge? Of(IView view) => Bridges.TryGetValue(view, out var bridge) ? bridge : null;

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        if (args.NewHandler is not null) Materializations++;
        base.OnHandlerChanging(args);
    }

    public IView Presented { get; }
    public bool Represents { get; init; }
    public bool Pull { get; init; } = true;
    public bool WatchCascade { get; init; } = true;
    public int Pulls;

    protected override ILayoutManager CreateLayoutManager() => new NoLayout();

    // Representing (round 7, K3): the presented view's own visual children, empty for a leaf; a child direct view
    // with a bridge is represented by its bridge.
    IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => Represents
        ? [.. ((IVisualTreeElement)Presented).GetVisualChildren().Select(v => v is DirectView dv && Bridges.TryGetValue(dv, out var b) ? b : v)]
        : [.. ((IElementController)this).LogicalChildren];

    IVisualTreeElement? IVisualTreeElement.GetVisualParent() => Represents ? Parent : Presented as IVisualTreeElement;

    protected override void OnParentSet()
    {
        base.OnParentSet();
        if (_watched is not null) _watched.PropertyChanged -= OnParentChanged;
        _watched = Parent;
        if (_watched is not null) _watched.PropertyChanged += OnParentChanged;
    }

    private void OnParentChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!Pull) return;
        if (e.PropertyName is nameof(IsEnabled) or nameof(InputTransparent) or nameof(Window) or nameof(FlowDirection) or nameof(Visual)
            || (WatchCascade && e.PropertyName == nameof(CascadeInputTransparent)))
        {
            Pulls++;
            base.OnParentSet();
        }
    }

    private sealed class NoLayout : ILayoutManager
    {
        public Size Measure(double widthConstraint, double heightConstraint) { LayoutCalls++; return Size.Zero; }
        public Size ArrangeChildren(Rect bounds) { LayoutCalls++; return Size.Zero; }
    }
}

// Spec 071 §2.1, the sixth review round of 2da9d63c: implicit-style identity of the projected layouts (J1), a stable
// factory policy (J2), input transparency through the bridge (J3). On demand.
public static partial class Fixtures
{
    private static string[] LayoutKinds => ["VerticalStackLayout", "HorizontalStackLayout", "Grid", "FlexLayout", "AbsoluteLayout"];

    private static IEnumerable<Fixture> LookRound6CharacterizationFixtures() =>
    [
        new("Characterize_Look_ImplicitStyleIdentity", LookImplicitStyleIdentity, 120, OnDemand: true),
        new("Characterize_Look_Projection [a stock layout]", c => LookProjection(c, 'a'), 60, OnDemand: true),
        new("Characterize_Look_Projection [b stock layout, representing bridge]", c => LookProjection(c, 'b'), 60, OnDemand: true),
        new("Characterize_Look_Projection [c shadow layout, bridge]", c => LookProjection(c, 'c'), 60, OnDemand: true),
        new("Characterize_Look_FirstDirectChild [projection-capable from mount]", c => LookFirstDirectChild(c, replace: false), 60, OnDemand: true),
        new("Characterize_Look_FirstDirectChild [replaced on first direct child]", c => LookFirstDirectChild(c, replace: true), 60, OnDemand: true),
        new("Characterize_Look_FirstDirectChild [stock layout, representing bridge]", c => LookFirstDirectChild(c, replace: false, stock: true), 60, OnDemand: true),
        .. new[] { "P plain MAUI", "A previous anchoring", "V VisualElement bridge", "L Layout bridge", "M Layout bridge without the cascade watch", "S stock layout, representing Layout bridge, no pull" }
            .Select(name => new Fixture($"Characterize_Look_InputTransparency [{name}]", c => LookInputTransparency(c, name[0]), 90, OnDemand: true)),
        new("Characterize_Look_StyleShield", LookStyleShield, 60, OnDemand: true),
    ];

    private static Layout StockLayout(string kind) => kind switch
    {
        "VerticalStackLayout" => new VerticalStackLayout(),
        "HorizontalStackLayout" => new HorizontalStackLayout(),
        "Grid" => new Grid(),
        "FlexLayout" => new FlexLayout(),
        _ => new AbsoluteLayout(),
    };

    private static Layout PlainSubLayout(string kind) => kind switch
    {
        "VerticalStackLayout" => new PlainSubVerticalStackLayout(),
        "HorizontalStackLayout" => new PlainSubHorizontalStackLayout(),
        "Grid" => new PlainSubGrid(),
        "FlexLayout" => new PlainSubFlexLayout(),
        _ => new PlainSubAbsoluteLayout(),
    };

    private static Style LayoutStyle(Type target, bool derived, params (BindableProperty Property, object Value)[] setters)
    {
        var style = new Style(target) { ApplyToDerivedTypes = derived };
        foreach (var (property, value) in setters) style.Setters.Add(new Setter { Property = property, Value = value });
        return style;
    }

    /// <summary>
    /// J1: implicit styles on a stock layout (the oracle), a plain subclass (round 5's projection, the negative
    /// control) and a shadow subclass whose full name is the stock type's (candidate c'), for each of the five layout
    /// kinds the direct registry can create with direct children. Each candidate sits alone in a ContentView whose
    /// resources hold the styles, with a Controls button child. Scenarios: the kind's implicit style; one that
    /// disables (XAML disabling); a Layout style with and without ApplyToDerivedTypes; a themed setter in Light and
    /// Dark; a declared override, then cleared; the style removed and re-added; the kind's style beside a derived
    /// Layout style.
    /// </summary>
    private static async Task LookImplicitStyleIdentity(FixtureContext c)
    {
        var app = Application.Current!;
        var original = app.UserAppTheme;
        var log = new LookLog { Phase = "identity" };
        var mismatches = 0;
        try
        {
            app.UserAppTheme = AppTheme.Light;
            var probe = ShadowLayouts.Create("VerticalStackLayout");
            log.Add($"shadow type: FullName '{probe.GetType().FullName}' == stock '{typeof(VerticalStackLayout).FullName}': {probe.GetType().FullName == typeof(VerticalStackLayout).FullName}; is VerticalStackLayout {probe is VerticalStackLayout}; exact type stock {probe.GetType() == typeof(VerticalStackLayout)}; assembly {probe.GetType().Assembly.GetName().Name}");

            foreach (var kind in LayoutKinds)
            {
                var stockType = StockLayout(kind).GetType();
                var scenarios = new (string Name, Func<Style[]> Styles, Func<Layout, Task<string>>? Steps)[]
                {
                    ($"implicit {kind}", () => [LayoutStyle(stockType, false, (Layout.PaddingProperty, new Thickness(7)), (VisualElement.BackgroundColorProperty, Colors.Teal))], null),
                    ($"implicit {kind}, IsEnabled false", () => [LayoutStyle(stockType, false, (VisualElement.IsEnabledProperty, false))], null),
                    ("implicit Layout", () => [LayoutStyle(typeof(Layout), false, (Layout.PaddingProperty, new Thickness(9)))], null),
                    ("implicit Layout, ApplyToDerivedTypes", () => [LayoutStyle(typeof(Layout), true, (Layout.PaddingProperty, new Thickness(11)), (VisualElement.BackgroundColorProperty, Colors.Orange))], null),
                    ($"implicit {kind} and derived Layout", () => [LayoutStyle(stockType, false, (Layout.PaddingProperty, new Thickness(7))), LayoutStyle(typeof(Layout), true, (Layout.PaddingProperty, new Thickness(11)))], null),
                    ($"implicit {kind}, themed", () => [LayoutStyle(stockType, false, (VisualElement.BackgroundColorProperty, new AppThemeBinding { Light = LightRed, Dark = DarkBlue }))],
                        async l => { app.UserAppTheme = AppTheme.Dark; await c.Settle(250); var dark = Read(l); app.UserAppTheme = AppTheme.Light; await c.Settle(250); return $"Dark: {dark}; Light again: {Read(l)}"; }),
                    ($"implicit {kind}, declared override then cleared", () => [LayoutStyle(stockType, false, (Layout.PaddingProperty, new Thickness(7)))],
                        async l => { l.Padding = new Thickness(3); await c.Settle(50); var local = Read(l); l.ClearValue(Layout.PaddingProperty); await c.Settle(50); return $"declared 3: {local}; cleared: {Read(l)}"; }),
                    ($"implicit {kind}, replaced, restored, removed", () => [LayoutStyle(stockType, false, (Layout.PaddingProperty, new Thickness(7)))],
                        async l =>
                        {
                            var host = (ContentView)l.Parent;
                            var key = stockType.FullName!;
                            var style = (Style)host.Resources[key];
                            host.Resources[key] = LayoutStyle(stockType, false, (Layout.PaddingProperty, new Thickness(13)));
                            await c.Settle(50);
                            var replaced = Read(l);
                            host.Resources[key] = style;
                            await c.Settle(50);
                            var restored = Read(l);
                            host.Resources.Remove(key);
                            await c.Settle(50);
                            return $"replaced (13): {replaced}; restored (7): {restored}; removed: {Read(l)}";
                        }),
                };
                foreach (var (name, styles, steps) in scenarios)
                {
                    log.Phase = name;
                    var candidates = new (string Name, Layout Layout)[] { ("stock", StockLayout(kind)), ("plain subclass", PlainSubLayout(kind)), ("shadow", ShadowLayouts.Create(kind)) };
                    var outer = new VerticalStackLayout();
                    foreach (var (_, layout) in candidates)
                    {
                        var host = new ContentView { Content = layout };
                        foreach (var style in styles()) host.Resources.Add(style);
                        layout.Add(new MauiButton { Text = "child" });
                        outer.Add(host);
                    }
                    c.ShowNative(outer);
                    await NativeLoaded(c, (View)candidates[2].Layout[0]);
                    var results = new List<string>();
                    foreach (var (candidate, layout) in candidates)
                    {
                        var read = Read(layout);
                        if (steps is not null) read += $"; {await steps(layout)}";
                        results.Add(read);
                    }
                    var oracle = results[0];
                    log.Add($"stock: {oracle}");
                    for (var i = 1; i < candidates.Length; i++)
                    {
                        var equal = results[i] == oracle;
                        if (!equal && candidates[i].Name == "shadow") mismatches++;
                        log.Add($"{candidates[i].Name}: {(equal ? "equal" : $"DIFFERS: {results[i]}")}");
                    }
                    c.ShowNative(new ContentView());
                    await c.Settle(50);
                }
            }
            log.Phase = "summary";
            log.Add($"shadow mismatches against stock: {mismatches}");
            Check(true, "measured");
        }
        finally
        {
            app.UserAppTheme = original;
            log.Dump("implicit-style identity of projected layouts");
        }

        static string Read(Layout l) =>
            $"padding {l.Padding.Left}, background {(l.BackgroundColor is { } bg ? bg.ToArgbHex() : "null")}, enabled {l.IsEnabled}, child enabled {((VisualElement)l[0]).IsEnabled}";
    }

    private static string ProjectionName(IVisualTreeElement node) => node switch
    {
        LayoutBridge b => $"bridge({ProjectionName((IVisualTreeElement)b.Presented)})",
        MauiLabel l => l.Text,
        MauiButton b => b.Text,
        IView { AutomationId: { Length: > 0 } id } => id,
        _ => node.GetType().Name,
    };

    /// <summary>Every node under <paramref name="h"/> from the window, as the device runner walks (no dedup).</summary>
    private static void LogTraversal(FixtureContext c, IVisualTreeElement h, LookLog log)
    {
        var order = new List<string>();
        var mismatches = new List<string>();
        var seen = new Dictionary<IVisualTreeElement, int>(ReferenceEqualityComparer.Instance);
        var inH = false;
        void Walk(IVisualTreeElement node, IVisualTreeElement? by)
        {
            var entering = ReferenceEquals(node, h);
            if (entering) inH = true;
            if (inH)
            {
                order.Add(ProjectionName(node));
                seen[node] = seen.GetValueOrDefault(node) + 1;
                var parent = node.GetVisualParent();
                if (!entering && !ReferenceEquals(parent, by))
                    mismatches.Add($"{ProjectionName(node)}: by {ProjectionName(by!)}, parent {(parent is null ? "null" : ProjectionName(parent))}");
            }
            foreach (var child in node.GetVisualChildren()) Walk(child, node);
            if (entering) inH = false;
        }
        Walk(c.Window, null);
        log.Add($"traversal under H: {string.Join(" > ", order)}");
        log.Add($"duplicates: {(seen.Any(kv => kv.Value > 1) ? string.Join(", ", seen.Where(kv => kv.Value > 1).Select(kv => ProjectionName(kv.Key))) : "none")}; bridges enumerated {seen.Keys.Count(k => k is LayoutBridge)}; direct views enumerated {seen.Keys.Count(k => k is DirectView)}; parent mismatches: {(mismatches.Count == 0 ? "none" : string.Join("; ", mismatches))}");
    }

    /// <summary>
    /// J1 (a), (b), (c'): H → [A, D, C]; D (direct) → [B, E, F]; F (direct) → [G, I]; A, C, B, G Controls. (a) a stock
    /// H and no bridge: what a stock layout enumerates. (b) a stock H with Layout bridges that represent their direct
    /// views in its enumeration and don't pull. (c) a shadow H with Layout bridges that stay out of enumeration and
    /// pull. For each: the traversal, Window, a BindingContext changed after mount, an implicit button style, H
    /// disabled, then a Controls view inserted at view index 1 and one added at the end (view-index logical
    /// insertion, today's MAUI).
    /// </summary>
    private static async Task LookProjection(FixtureContext c, char mode)
    {
        var log = new LookLog { Phase = mode.ToString() };
        try
        {
            var h = mode == 'c' ? ShadowLayouts.Create("VerticalStackLayout") : new VerticalStackLayout();
            h.AutomationId = "H";
            var buttonStyle = new Style(typeof(MauiButton));
            buttonStyle.Setters.Add(new Setter { Property = MauiButton.TextColorProperty, Value = Colors.Yellow });
            h.Resources.Add(buttonStyle);
            var a = new MauiLabel { Text = "A" };
            var cl = new MauiLabel { Text = "C" };
            var d = new DirectStackLayout(StackOrientation.Vertical) { AutomationId = "D" };
            var f = new DirectStackLayout(StackOrientation.Vertical) { AutomationId = "F" };
            var b = new MauiButton { Text = "B" };
            var g = new MauiButton { Text = "G" };
            var e = NewDirectLabel("E");
            var i = NewDirectLabel("I");
            h.Add(a);
            h.Add(d);
            d.Parent = h;
            LayoutBridge? bridgeD = null, bridgeF = null;
            if (mode != 'a')
            {
                bridgeD = new LayoutBridge(d) { Represents = mode == 'b', Pull = mode == 'c' };
                bridgeF = new LayoutBridge(f) { Represents = mode == 'b', Pull = mode == 'c' };
                h.InsertLogicalChild(1, bridgeD);
                bridgeD.AddLogicalChild(b);
                bridgeD.AddLogicalChild(bridgeF);
                bridgeF.AddLogicalChild(g);
            }
            h.Add(cl);
            d.Add(b);
            d.Add(e);
            d.Add(f);
            f.Add(g);
            f.Add(i);
            c.ShowNative(new Grid { Children = { h } });
            await NativeLoaded(c, g);
            await c.Settle(300);

            log.Add($"H.GetVisualChildren(): {string.Join(", ", ((IVisualTreeElement)h).GetVisualChildren().Select(ProjectionName))}; H's views: {string.Join(", ", h.Select(v => ProjectionName((IVisualTreeElement)v)))}");
            LogTraversal(c, h, log);
            log.Add($"Window: B {b.Window is not null}, G {g.Window is not null}; B text {LookPlatform.Describe(LookPlatform.TextColor(b))} (style yellow)");
            h.BindingContext = "ctx";
            log.Add($"BindingContext after mount: B '{b.BindingContext}', G '{g.BindingContext}'");
            h.IsEnabled = false;
            await c.Settle(150);
            log.Add($"H disabled: B {b.IsEnabled}, G {g.IsEnabled}{(bridgeD is null ? "" : $"; pulls {bridgeD.Pulls}")}");
            h.IsEnabled = true;
            await c.Settle(150);
            log.Add($"H re-enabled: B {b.IsEnabled}, G {g.IsEnabled}");
            h.Insert(1, new MauiLabel { Text = "X" });
            h.Add(new MauiLabel { Text = "Y" });
            await c.Settle(200);
            log.Add($"after Insert(1, X) and Add(Y): logical {string.Join(", ", ((IElementController)h).LogicalChildren.Select(x => ProjectionName(x)))}");
            LogTraversal(c, h, log);
            Check(true, "measured");
        }
        finally
        {
            log.Dump("projection candidates");
        }
    }

    /// <summary>
    /// J2: a direct-registry Controls layout [Entry e1, Label l2, Entry e3 (focused, with a selection)] gains its first
    /// direct child at index 1. Projection-capable from mount: a shadow layout, the direct view inserted and its
    /// bridge inserted at the slot. Replaced (the negative control): a stock layout whose children move to a new
    /// shadow layout put in its place. Recorded: the layout's identity, e3's focus, selection, IME and events, native
    /// attach and hierarchy counts, the traversal and the frames.
    /// </summary>
    private static async Task LookFirstDirectChild(FixtureContext c, bool replace, bool stock = false)
    {
        var log = new LookLog { Phase = replace ? "replaced" : stock ? "stock" : "from mount" };
        try
        {
            Layout h = replace || stock ? new VerticalStackLayout() : ShadowLayouts.Create("VerticalStackLayout");
            h.AutomationId = "H";
            var host = new Grid { Children = { h } };
            var e1 = new MauiEntry { Text = "first entry", AutomationId = "e1" };
            var l2 = new MauiLabel { Text = "l2" };
            var e3 = new MauiEntry { Text = "hello world", AutomationId = "e3" };
            h.Add(e1);
            h.Add(l2);
            h.Add(e3);
            c.ShowNative(host);
            await NativeLoaded(c, e3);
            e3.Focus();
            await c.WaitFor(() => e3.IsFocused, "e3 to focus", 3000);
            e3.CursorPosition = 2;
            e3.SelectionLength = 3;
            await c.Settle(500);
            var counts = new Dictionary<string, int>();
            void Count(string what) => counts[what] = counts.GetValueOrDefault(what) + 1;
            e3.Loaded += (_, _) => Count("Loaded");
            e3.Unloaded += (_, _) => Count("Unloaded");
            e3.Focused += (_, _) => Count("Focused");
            e3.Unfocused += (_, _) => Count("Unfocused");
            e3.HandlerChanging += (_, _) => Count("HandlerChanging");
            e3.ParentChanged += (_, _) => Count("ParentChanged");
            e1.ParentChanged += (_, _) => Count("e1 ParentChanged");
            var nativeBefore = e3.Handler!.PlatformView;
            var layoutNativeBefore = h.Handler!.PlatformView;
#if ANDROID
            var attach = new AttachCounter();
            ((Android.Views.View)nativeBefore!).AddOnAttachStateChangeListener(attach);
            var hierarchy = new HierarchyCounter();
            ((Android.Views.ViewGroup)layoutNativeBefore!).SetOnHierarchyChangeListener(hierarchy);
#endif
            log.Add($"before: e3 {NativeFocus(e3)}, IsFocused {e3.IsFocused}, selection {e3.CursorPosition}+{e3.SelectionLength}; events on subscribing (MAUI raises Loaded for a late subscriber): {string.Join(", ", counts.Select(kv => $"{kv.Key}×{kv.Value}"))}");
            counts.Clear();

            var d = new DirectStackLayout(StackOrientation.Vertical) { AutomationId = "D" };
            var b = new MauiButton { Text = "B" };
            var clock = Stopwatch.StartNew();
            var target = h;
            if (replace)
            {
                target = ShadowLayouts.Create("VerticalStackLayout");
                target.AutomationId = "H'";
                var moved = h.ToList();
                h.Clear();
                foreach (var v in moved) target.Add(v);
                host.Remove(h);
                host.Add(target);
            }
            target.Insert(1, d);
            d.Parent = target;
            var bridge = new LayoutBridge(d, shield: stock) { Represents = stock, Pull = !stock };
            target.InsertLogicalChild(1, bridge);
            bridge.AddLogicalChild(b);
            d.Add(b);
            log.Add($"transition took {clock.Elapsed.TotalMilliseconds:0.00} ms");
            await c.Settle(600);
            log.Add($"after: same layout {ReferenceEquals(target, h)}, same layout native {ReferenceEquals(target.Handler?.PlatformView, layoutNativeBefore)}; e3 {NativeFocus(e3)}, IsFocused {e3.IsFocused}, selection {e3.CursorPosition}+{e3.SelectionLength}");
            log.Add($"events: {(counts.Count == 0 ? "none" : string.Join(", ", counts.Select(kv => $"{kv.Key}×{kv.Value}")))}; e3 same native {ReferenceEquals(nativeBefore, e3.Handler?.PlatformView)}");
#if ANDROID
            log.Add($"native: e3 detached {attach.Detached}, attached {attach.Attached}; layout children removed {hierarchy.Removed}, added {hierarchy.Added}");
#endif
            log.Add($"B: Window {b.Window is not null}, loaded {b.IsLoaded}; logical {string.Join(", ", ((IElementController)target).LogicalChildren.Select(x => ProjectionName(x)))}");
            LogTraversal(c, target, log);
            log.Add($"frames: {string.Join(", ", new (string, IView)[] { ("e1", e1), ("D", d), ("B", b), ("l2", l2), ("e3", e3) }.Select(v => $"{v.Item1} {Platform.WindowFrame(v.Item2)}"))}");
            Check(true, "measured");
        }
        finally
        {
            log.Dump("a layout's first direct child");
        }
    }

    private sealed class TransparencyTopology
    {
        public required Layout H;
        public required MauiButton B;
        public required IView E;
        public required Action<bool> SetDOwn;
        public required Action<bool> SetEOwn;
        public required Func<string> Describe;
        public Func<int>? EClicks;
    }

    /// <summary>
    /// J3: input transparency from a Controls layout H through a direct layout D to a Controls button B and a direct
    /// button E (in P, D and E are a VerticalStackLayout and a Controls button). A backdrop button fills the cell
    /// under H. Each case sets the inputs after mount, then hit-tests natively at B's and E's centres: UIKit's
    /// window hit test on iOS and Mac Catalyst; on Android, a touch dispatched to the decor view, counting clicks.
    /// P plain MAUI (the oracle); A the current product anchoring (B.Parent = H, not a logical child); V round 5's
    /// VisualElement bridge (pull); L a Layout bridge that pulls CascadeInputTransparent too, with the direct views'
    /// effective transparency computed from it as Reactor would.
    /// </summary>
    private static async Task LookInputTransparency(FixtureContext c, char topology)
    {
        var log = new LookLog { Phase = topology.ToString() };
        try
        {
            var back = new MauiButton { Text = "back" };
            var backClicks = 0;
            back.Clicked += (_, _) => backClicks++;
            var bClicks = 0;
            var t = BuildTransparencyTopology(topology);
            t.B.Clicked += (_, _) => bClicks++;
            t.H.VerticalOptions = LayoutOptions.Start;
            var root = new Grid { back, t.H };
            c.ShowNative(root);
            await NativeLoaded(c, t.B);
            await c.Settle(300);

            async Task<string> HitAt(IView target)
            {
#if IOS || MACCATALYST
                var native = (UIKit.UIView)target.Handler!.PlatformView!;
                var point = native.ConvertPointToView(new CoreGraphics.CGPoint(native.Bounds.Width / 2, native.Bounds.Height / 2), null);
                var hit = native.Window!.HitTest(point, null);
                var names = new Dictionary<UIKit.UIView, string>(ReferenceEqualityComparer.Instance);
                void Name(IView v, string n) { if (v.Handler?.PlatformView is UIKit.UIView pv) names[pv] = n; if (v.Handler is IViewHandler { ContainerView: UIKit.UIView cv }) names[cv] = n; }
                Name(t.B, "B");
                Name(back, "back");
                Name(t.E, "E");
                for (var v = hit; v is not null; v = v.Superview)
                    if (names.TryGetValue(v, out var n)) return n;
                await Task.CompletedTask;
                return hit is null ? "none" : $"other({hit.GetType().Name})";
#elif ANDROID
                var native = (Android.Views.View)target.Handler!.PlatformView!;
                var activity = (Android.App.Activity)c.Window.Handler!.PlatformView!;
                var decor = activity.Window!.DecorView;
                var at = new int[2];
                native.GetLocationInWindow(at);
                var origin = new int[2];
                decor.GetLocationInWindow(origin);
                float x = at[0] - origin[0] + native.Width / 2f, y = at[1] - origin[1] + native.Height / 2f;
                var (b0, back0, e0) = (bClicks, backClicks, t.EClicks!());
                var now = Android.OS.SystemClock.UptimeMillis();
                using (var down = Android.Views.MotionEvent.Obtain(now, now, Android.Views.MotionEventActions.Down, x, y, 0)!)
                {
                    decor.DispatchTouchEvent(down);
                }
                using (var up = Android.Views.MotionEvent.Obtain(now, now + 60, Android.Views.MotionEventActions.Up, x, y, 0)!)
                {
                    decor.DispatchTouchEvent(up);
                }
                await c.Settle(350);
                var got = new List<string>();
                if (bClicks > b0) got.Add("B");
                if (backClicks > back0) got.Add("back");
                if (t.EClicks!() > e0) got.Add("E");
                return got.Count == 0 ? "none" : string.Join("+", got);
#else
                await Task.CompletedTask;
                return "n/a";
#endif
            }

            async Task Reset()
            {
                t.H.InputTransparent = false;
                t.H.CascadeInputTransparent = true;
                t.B.ClearValue(VisualElement.InputTransparentProperty);
                t.SetDOwn(false);
                t.SetEOwn(false);
                await c.Settle(200);
            }

            async Task Case(string name, Func<Task> arrange)
            {
                await Reset();
                await arrange();
                await c.Settle(250);
                log.Add($"{name}: tap B → {await HitAt(t.B)}, tap E → {await HitAt(t.E)}; B.InputTransparent {t.B.InputTransparent}; {t.Describe()}");
            }

            await Case("c1 nothing transparent", () => Task.CompletedTask);
            await Case("c2 H transparent, cascade", () => { t.H.InputTransparent = true; return Task.CompletedTask; });
            await Case("c2' c2 then undone", async () => { t.H.InputTransparent = true; await c.Settle(250); t.H.InputTransparent = false; });
            await Case("c3 H transparent, no cascade", () => { t.H.CascadeInputTransparent = false; t.H.InputTransparent = true; return Task.CompletedTask; });
            await Case("c4 B transparent", () => { t.B.InputTransparent = true; return Task.CompletedTask; });
            await Case("c5 H transparent, cascade, B explicitly false", () => { t.H.InputTransparent = true; t.B.InputTransparent = false; return Task.CompletedTask; });
            await Case("c6 D transparent (cascade default)", () => { t.SetDOwn(true); return Task.CompletedTask; });
            await Case("c7 H transparent, cascade false → true", async () => { t.H.CascadeInputTransparent = false; t.H.InputTransparent = true; await c.Settle(250); t.H.CascadeInputTransparent = true; });
            await Reset();
            Check(true, "measured");
        }
        finally
        {
            log.Dump("input transparency through a direct layout");
        }
    }

    /// <summary>
    /// J1, J3: what implicit styles reach that plain MAUI wouldn't have, and a shield. Under a ContentView whose
    /// resources hold a <c>Layout</c> style with ApplyToDerivedTypes (padding 21, IsEnabled false, InputTransparent
    /// true, CascadeInputTransparent false), then a <c>Grid</c> style with and without ApplyToDerivedTypes (padding 23,
    /// 25): Layout bridges bare (the positive control), with Reactor's three locals set before parenting, set after the
    /// style applied, and with a shield (an empty style keyed by the bridge's own type in its resources, which MAUI's
    /// first-match rule takes before any base type's style); a Controls Grid that is the shielded bridge's logical
    /// child (the shield mustn't hide the app's style from it); Reactor's <c>DirectViewHost</c> bare and shielded; and,
    /// for MAUI's own coercion, stock buttons with a local IsEnabled true and InputTransparent false under a button
    /// style setting the opposite, set before and after parenting.
    /// </summary>
    private static async Task LookStyleShield(FixtureContext c)
    {
        var log = new LookLog { Phase = "shield" };
        try
        {
            var scenarios = new (string Name, Style Style)[]
            {
                ("Layout, ApplyToDerivedTypes", LayoutStyle(typeof(Layout), true, (Layout.PaddingProperty, new Thickness(21)), (VisualElement.IsEnabledProperty, false), (VisualElement.InputTransparentProperty, true), (Layout.CascadeInputTransparentProperty, false))),
                ("Grid", LayoutStyle(typeof(Grid), false, (Layout.PaddingProperty, new Thickness(23)))),
                ("Grid, ApplyToDerivedTypes", LayoutStyle(typeof(Grid), true, (Layout.PaddingProperty, new Thickness(25)))),
            };
            foreach (var (name, style) in scenarios)
            {
                log.Phase = name;
                var host = new ContentView();
                host.Resources.Add(style);
                var buttonStyle = new Style(typeof(MauiButton));
                buttonStyle.Setters.Add(new Setter { Property = VisualElement.IsEnabledProperty, Value = false });
                buttonStyle.Setters.Add(new Setter { Property = VisualElement.InputTransparentProperty, Value = true });
                host.Resources.Add(buttonStyle);
                LayoutBridge NewBridge() => new(new DirectStackLayout(StackOrientation.Vertical));
                void Locals(Layout l) { l.IsEnabled = true; l.InputTransparent = false; l.CascadeInputTransparent = true; }
                var bare = NewBridge();
                var before = NewBridge();
                Locals(before);
                var after = NewBridge();
                var shielded = NewBridge();
                shielded.Resources = new ResourceDictionary { new Style(typeof(LayoutBridge)) };
                Locals(shielded);
                var inner = new Grid();
                foreach (var b in new[] { bare, before, after, shielded }) host.AddLogicalChild(b);
                shielded.AddLogicalChild(inner);
                var hostBare = (Layout)Microsoft.UI.Reactor.Maui.Handlers.Props.AsView(NewDirectLabel("hosted"));
                var hostShielded = (Layout)Microsoft.UI.Reactor.Maui.Handlers.Props.AsView(NewDirectLabel("hosted, shielded"));
                hostShielded.Resources = new ResourceDictionary { new Style(hostShielded.GetType()) };
                var buttonBefore = new MauiButton { Text = "before", IsEnabled = true, InputTransparent = false };
                var buttonAfter = new MauiButton { Text = "after" };
                var probe = new Grid { Children = { hostBare, hostShielded, buttonBefore, buttonAfter } };
                host.Content = probe;
                c.ShowNative(host);
                await NativeLoaded(c, probe);
                Locals(after);
                buttonAfter.IsEnabled = true;
                buttonAfter.InputTransparent = false;
                await c.Settle(100);
                string Describe(Layout l) => $"padding {l.Padding.Left}, enabled {l.IsEnabled}, transparent {l.InputTransparent}, cascade {l.CascadeInputTransparent}";
                log.Add($"bridge bare: {Describe(bare)}; locals before parenting: {Describe(before)}; locals after the style: {Describe(after)}; shielded: {Describe(shielded)}");
                log.Add($"the shielded bridge's Grid child: padding {inner.Padding.Left}; DirectViewHost bare: padding {hostBare.Padding.Left}, shielded: padding {hostShielded.Padding.Left}; a stock Grid: padding {probe.Padding.Left}");
                log.Add($"stock buttons under a style (IsEnabled false, InputTransparent true): locals before parenting: enabled {buttonBefore.IsEnabled}, transparent {buttonBefore.InputTransparent}; locals after: enabled {buttonAfter.IsEnabled}, transparent {buttonAfter.InputTransparent}");
                c.ShowNative(new ContentView());
                await c.Settle(50);
            }
            Check(true, "measured");
        }
        finally
        {
            log.Dump("styles reaching nodes plain MAUI doesn't have");
        }
    }

    private static TransparencyTopology BuildTransparencyTopology(char topology)
    {
        var b = new MauiButton { Text = "B", HeightRequest = 60 };
        if (topology == 'P')
        {
            var h = new VerticalStackLayout();
            var d = new VerticalStackLayout();
            var e = new MauiButton { Text = "E", HeightRequest = 60 };
            var eClicks = 0;
            e.Clicked += (_, _) => eClicks++;
            d.Add(b);
            d.Add(e);
            h.Add(d);
            return new TransparencyTopology
            {
                H = h, B = b, E = e, EClicks = () => eClicks,
                SetDOwn = v => d.InputTransparent = v,
                SetEOwn = v => e.InputTransparent = v,
                Describe = () => $"D' {d.InputTransparent}, E' {e.InputTransparent}",
            };
        }

        var layout = topology is 'A' or 'S' ? new VerticalStackLayout() : ShadowLayouts.Create("VerticalStackLayout");
        var direct = new DirectStackLayout(StackOrientation.Vertical) { AutomationId = "D" };
        var button = new DirectButton();
        button.Apply(new ButtonElement("E"));
        var directClicks = 0;
        button.OnClicked = () => directClicks++;
        ViewModifiers? dMods = null, eMods = null;
        var dOwn = false;
        var eOwn = false;
        void ApplyD(bool value) { var next = new ViewModifiers { InputTransparent = value }; direct.ApplyModifiers(dMods, next, mounting: dMods is null); dMods = next; }
        void ApplyE(bool value) { var next = new ViewModifiers { InputTransparent = value, Height = 60 }; button.ApplyModifiers(eMods, next, mounting: eMods is null); eMods = next; }
        ApplyD(false);
        ApplyE(false);
        layout.Add(direct);
        VisualElement? bridge = null;
        LayoutBridge? layoutBridge = null;
        switch (topology)
        {
            case 'A':
                direct.AttachToControlsParent(layout);
                break;
            case 'V':
                direct.Parent = layout;
                bridge = new TestBridge(direct) { Pull = true };
                layout.InsertLogicalChild(0, bridge);
                ((TestBridge)bridge).AddLogicalChild(b);
                break;
            default:
                direct.Parent = layout;
                bridge = layoutBridge = topology == 'S'
                    ? new LayoutBridge(direct, shield: true) { Represents = true, Pull = false }
                    : new LayoutBridge(direct) { WatchCascade = topology != 'M' };
                layout.InsertLogicalChild(0, layoutBridge);
                layoutBridge.AddLogicalChild(b);
                // What Reactor would do: the direct views' effective transparency from the bridge's MAUI coercion.
                layoutBridge.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(VisualElement.InputTransparent) or nameof(Layout.CascadeInputTransparent)) Effective();
                };
                break;
        }
        direct.Add(b);
        direct.Add(button);

        void Effective()
        {
            if (layoutBridge is null) { ApplyD(dOwn); ApplyE(eOwn); return; }
            ApplyD(layoutBridge.InputTransparent);
            ApplyE(eOwn || (layoutBridge.CascadeInputTransparent && layoutBridge.InputTransparent));
        }

        return new TransparencyTopology
        {
            H = layout, B = b, E = button, EClicks = () => directClicks,
            SetDOwn = v => { dOwn = v; if (bridge is not null) bridge.InputTransparent = v; Effective(); },
            SetEOwn = v => { eOwn = v; Effective(); },
            Describe = () => $"D native {direct.InputTransparent}, E native {button.InputTransparent}{(bridge is null ? "" : $", bridge {bridge.InputTransparent}{(layoutBridge is null ? "" : $" cascade {layoutBridge.CascadeInputTransparent}, pulls {layoutBridge.Pulls}")}")}",
        };
    }
}
