using Microsoft.UI.Reactor.Maui;
using Microsoft.UI.Reactor.Maui.Direct;
using static Reactor.Maui.DeviceTests.FixtureContext;
using MauiButton = Microsoft.Maui.Controls.Button;
using MauiLabel = Microsoft.Maui.Controls.Label;

namespace Reactor.Maui.DeviceTests;

/// <summary>A Controls stack whose visual projection lists its views (direct ones included) instead of its logical children (H1).</summary>
public sealed class ProjectedStackLayout : VerticalStackLayout, IVisualTreeElement
{
    IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => [.. this.OfType<IVisualTreeElement>()];
    IVisualTreeElement? IVisualTreeElement.GetVisualParent() => Parent;
}

/// <summary>
/// The combined bridge for one direct view (H1): a logical-only element that stands in for the direct view in its
/// Controls parent's logical children and is the logical parent of the direct layout's Controls children and of its
/// nested direct layouts' bridges. Its visual parent is the direct view it presents; it never presents itself in a
/// downward enumeration. With <see cref="Pull"/>, it re-pulls MAUI's propagated state when its logical parent's
/// changes, since a projected parent no longer pushes to it.
/// </summary>
public sealed class TestBridge(IView presented) : VisualElement, IVisualTreeElement
{
    private Microsoft.Maui.Controls.Element? _watched;
    public IView Presented { get; } = presented;
    public bool Pull { get; set; }
    public int Pulls;

    IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => [.. ((IElementController)this).LogicalChildren];
    IVisualTreeElement? IVisualTreeElement.GetVisualParent() => Presented as IVisualTreeElement;

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
        if (e.PropertyName is nameof(IsEnabled) or nameof(InputTransparent) or nameof(Window) or nameof(FlowDirection) or nameof(Visual))
        {
            Pulls++;
            // MAUI's own pull-and-push: from the parent (IsEnabled, Window, FlowDirection, Visual), then to this
            // bridge's logical children.
            base.OnParentSet();
        }
    }
}

// Spec 071 §2.1, the fifth review round of da45a0d0: one bridge topology for stand-ins and enabling adapters, with
// separate logical and visual projections (H1). On demand.
public static partial class Fixtures
{
    private static IEnumerable<Fixture> LookRound5CharacterizationFixtures() =>
    [
        new("Characterize_Look_Bridges [T1 separate stand-in and adapter]", c => LookBridges(c, 1), 60, OnDemand: true),
        new("Characterize_Look_Bridges [T3a one bridge, projected layout]", c => LookBridges(c, 2), 60, OnDemand: true),
        new("Characterize_Look_Bridges [T3b one bridge, projected layout, pull]", c => LookBridges(c, 3), 60, OnDemand: true),
    ];

    private static string NodeName(IVisualTreeElement node) => node switch
    {
        TestBridge b => $"bridge({NodeName((IVisualTreeElement)b.Presented)})",
        LogicalStandIn => "adapter",
        MauiLabel l => l.Text,
        MauiButton b => b.Text,
        IView { AutomationId: { Length: > 0 } id } => id,
        _ => node.GetType().Name,
    };

    /// <summary>
    /// H1: H (a Controls stack) → [A, D, C]; D (direct) → [B, E, F]; F (nested direct) → [G, I]; A, C, B and G are
    /// Controls, D, E, F and I direct. Built off-window, then shown. Traversal from the window as the device runner
    /// does (no dedup): every node under H, duplicates, order, bridges seen; each node's GetVisualParent against the
    /// node that enumerated it; a BindingContext changed after mount; an implicit style and a theme change; H
    /// disabled and D's bridge disabled; Window and IsLoaded for B and G; H's native child count; bridges' handlers.
    /// </summary>
    private static async Task LookBridges(FixtureContext c, int topology)
    {
        var app = Application.Current!;
        var original = app.UserAppTheme;
        var log = new LookLog { Phase = topology switch { 1 => "T1", 2 => "T3a", _ => "T3b" } };
        try
        {
            app.UserAppTheme = AppTheme.Light;
            VerticalStackLayout h = topology == 1 ? new VerticalStackLayout() : new ProjectedStackLayout();
            h.AutomationId = "H";
            var buttonStyle = new Style(typeof(MauiButton));
            buttonStyle.Setters.Add(new Setter { Property = MauiButton.TextColorProperty, Value = Microsoft.Maui.Graphics.Colors.Yellow });
            h.Resources.Add(buttonStyle);
            var a = new MauiLabel { Text = "A" };
            var cLabel = new MauiLabel { Text = "C" };
            var d = new DirectStackLayout(StackOrientation.Vertical) { AutomationId = "D" };
            var f = new DirectStackLayout(StackOrientation.Vertical) { AutomationId = "F" };
            var b = new MauiButton { Text = "B" };
            var g = new MauiButton { Text = "G" };
            b.SetAppThemeColor(VisualElement.BackgroundColorProperty, LightRed, DarkBlue);
            var e = NewDirectLabel("E");
            var i = NewDirectLabel("I");
            TestBridge? bridgeD = null, bridgeF = null;
            LogicalStandIn? standInD = null, adapterD = null, adapterF = null;

            h.Add(a);
            h.Add(d);
            d.AttachToControlsParent(h);
            if (topology == 1)
            {
                standInD = new LogicalStandIn();
                h.AddLogicalChild(standInD);                // slot-aligned stand-in for D
                adapterD = new LogicalStandIn();
                adapterF = new LogicalStandIn();
                adapterD.AddLogicalChild(b);
                adapterF.AddLogicalChild(g);
                adapterD.AddLogicalChild(adapterF);
            }
            else
            {
                bridgeD = new TestBridge(d) { Pull = topology == 3 };
                bridgeF = new TestBridge(f) { Pull = topology == 3 };
                h.AddLogicalChild(bridgeD);                 // slot-aligned: one bridge is the stand-in and the adapter
                bridgeD.AddLogicalChild(b);
                bridgeD.AddLogicalChild(bridgeF);
                bridgeF.AddLogicalChild(g);
            }
            h.Add(cLabel);
            if (topology == 1) h.AddLogicalChild(adapterD!);   // round 4's reserved place for an adapter: after the slots
            d.Add(b);
            d.Add(e);
            d.Add(f);
            f.Add(g);
            f.Add(i);
            var host = new Grid { Children = { h } };
            c.ShowNative(host);
            await NativeLoaded(c, g);
            await c.Settle(300);

            // Traversal from the window, without dedup, as Runner.FindAll does.
            var order = new List<string>();
            var mismatches = new List<string>();
            var seen = new Dictionary<IVisualTreeElement, int>(ReferenceEqualityComparer.Instance);
            var inH = false;
            void Walk(IVisualTreeElement node, IVisualTreeElement? enumeratedBy)
            {
                var entering = ReferenceEquals(node, h);
                if (entering) inH = true;
                if (inH)
                {
                    order.Add(NodeName(node));
                    seen[node] = seen.GetValueOrDefault(node) + 1;
                    var parent = node.GetVisualParent();
                    if (!entering && !ReferenceEquals(parent, enumeratedBy))
                        mismatches.Add($"{NodeName(node)}: enumerated by {NodeName(enumeratedBy!)}, GetVisualParent {(parent is null ? "null" : NodeName(parent))}{(parent is not null && parent.GetVisualParent() is { } grand ? $" → {NodeName(grand)}" : "")}");
                }
                foreach (var child in node.GetVisualChildren()) Walk(child, node);
                if (entering) inH = false;
            }
            Walk(c.Window, null);
            log.Add($"traversal under H: {string.Join(" > ", order)}");
            log.Add($"duplicates: {(seen.Any(kv => kv.Value > 1) ? string.Join(", ", seen.Where(kv => kv.Value > 1).Select(kv => $"{NodeName(kv.Key)}×{kv.Value}")) : "none")}; bridges or adapters enumerated: {seen.Keys.Count(k => k is TestBridge or LogicalStandIn)}");
            log.Add($"parent mismatches: {(mismatches.Count == 0 ? "none" : string.Join("; ", mismatches))}");
            log.Add($"H's logical children: {string.Join(", ", ((IElementController)h).LogicalChildren.Select(x => NodeName(x)))}; H's native child count {NativeChildCount(h)}; bridge handlers: {(bridgeD is null ? $"stand-in {standInD!.Handler is null}, adapter {adapterD!.Handler is null}" : $"{bridgeD.Handler is null}, {bridgeF!.Handler is null}")} (null)");

            log.Add($"Window and IsLoaded: B {b.Window is not null}/{b.IsLoaded}, G {g.Window is not null}/{g.IsLoaded}, A {a.Window is not null}/{a.IsLoaded}");
            h.BindingContext = "ctx1";
            log.Add($"BindingContext set on H after mount: B '{b.BindingContext}', G '{g.BindingContext}'");
            h.BindingContext = "ctx2";
            log.Add($"changed: B '{b.BindingContext}', G '{g.BindingContext}'");
            log.Add($"implicit style from H: B text {LookPlatform.Describe(LookPlatform.TextColor(b))}, G text {LookPlatform.Describe(LookPlatform.TextColor(g))}");
            app.UserAppTheme = AppTheme.Dark;
            await c.Settle(400);
            log.Add($"theme Dark: B background px {LookPlatform.Describe(Snapshot.Capture(b).At(0.1, 0.5))}");

            h.IsEnabled = false;
            await c.Settle(200);
            log.Add($"H disabled: A {a.IsEnabled}, B {b.IsEnabled}, G {g.IsEnabled}{(bridgeD is not null ? $", bridge(D) {bridgeD.IsEnabled}, pulls {bridgeD.Pulls}" : $", stand-in {standInD!.IsEnabled}, adapter {adapterD!.IsEnabled}")}");
            h.IsEnabled = true;
            await c.Settle(200);
            log.Add($"H re-enabled: A {a.IsEnabled}, B {b.IsEnabled}, G {g.IsEnabled}");
            var owner = (VisualElement?)bridgeD ?? adapterD!;
            owner.IsEnabled = false; // Reactor's write for D's effective state
            await c.Settle(200);
            log.Add($"D's bridge (or adapter) disabled: B {b.IsEnabled}, G {g.IsEnabled}");
            owner.IsEnabled = true;
            await c.Settle(200);
            Check(true, "measured");
        }
        finally
        {
            app.UserAppTheme = original;
            log.Dump("bridge topology");
        }
    }

    private static int NativeChildCount(IView layout) => layout.Handler?.PlatformView switch
    {
#if IOS || MACCATALYST
        UIKit.UIView native => native.Subviews.Length,
#elif ANDROID
        Android.Views.ViewGroup native => native.ChildCount,
#endif
        _ => -1,
    };
}
