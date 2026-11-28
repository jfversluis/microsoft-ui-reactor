using System.Diagnostics;
using Microsoft.UI.Reactor.Maui;
using Microsoft.UI.Reactor.Maui.Direct;
using static Reactor.Maui.DeviceTests.FixtureContext;
using MauiButton = Microsoft.Maui.Controls.Button;
using MauiEntry = Microsoft.Maui.Controls.Entry;
using MauiLabel = Microsoft.Maui.Controls.Label;

namespace Reactor.Maui.DeviceTests;

/// <summary>A logical-only stand-in for a direct child in a Controls layout's logical children (G1 candidate S2), and an
/// owned effective-enabled adapter (G3). Not a view: it has no handler and is never in a layout's view list.</summary>
public sealed class LogicalStandIn : VisualElement
{
}

// Spec 071 §2.1, the fourth review round of 938621cb: inserting into a Controls layout with direct children without
// detaching siblings (G1, G4), and inherited enabling without writing a child's manual layer (G3). On demand.
public static partial class Fixtures
{
    private static IEnumerable<Fixture> LookRound4CharacterizationFixtures() =>
    [
        .. new[] { "S0 naive Insert", "S1 suffix removed and re-added", "S2 logical stand-ins", "S3 Controls wrappers" }
            .Select((name, i) => new Fixture($"Characterize_Look_MixedInsert [{name}]", c => LookMixedInsert(c, i), 60, OnDemand: true)),
        new("Characterize_Look_MixedInsertCost", LookMixedInsertCost, 120, OnDemand: true),
        new("Characterize_Look_EnablingLayers", LookEnablingLayers, 60, OnDemand: true),
    ];

    private static DirectLabel NewDirectLabel(string text)
    {
        var label = new DirectLabel();
        label.Apply(new LabelElement(text));
        return label;
    }

    /// <summary>The views' texts in the platform's own child order, the native truth of the layout's order.</summary>
    private static string NativeOrder(IView layout)
    {
        var names = new List<string>();
        switch (layout.Handler?.PlatformView)
        {
#if IOS || MACCATALYST
            case UIKit.UIView native:
                foreach (var sub in native.Subviews) names.Add(Describe(sub));
                break;
#elif ANDROID
            case Android.Views.ViewGroup native:
                for (var i = 0; i < native.ChildCount; i++) names.Add(Describe(native.GetChildAt(i)!));
                break;
#endif
        }
        return string.Join(" | ", names);
#if IOS || MACCATALYST
        static string Describe(UIKit.UIView v) => v switch
        {
            UIKit.UILabel l => l.Text ?? "",
            UIKit.UITextField f => $"entry({f.Text})",
            _ when v.Subviews.Length == 1 => Describe(v.Subviews[0]),
            _ => v.GetType().Name,
        };
#elif ANDROID
        static string Describe(Android.Views.View v) => v switch
        {
            Android.Widget.EditText e => $"entry({e.Text})",
            Android.Widget.TextView t => t.Text ?? "",
            Android.Views.ViewGroup g when g.ChildCount == 1 => Describe(g.GetChildAt(0)!),
            _ => v.GetType().Name,
        };
#endif
    }

    private static string NativeFocus(IView entry)
    {
        switch (entry.Handler?.PlatformView)
        {
#if IOS || MACCATALYST
            case UIKit.UITextField field:
                var range = field.SelectedTextRange;
                var start = range is null ? -1 : (int)field.GetOffsetFromPosition(field.BeginningOfDocument, range.Start);
                var length = range is null ? -1 : (int)field.GetOffsetFromPosition(range.Start, range.End);
                return $"first responder {field.IsFirstResponder}, selection {start}+{length}";
#elif ANDROID
            case Android.Widget.EditText edit:
                var ime = OperatingSystem.IsAndroidVersionAtLeast(30) && edit.RootWindowInsets is { } insets ? insets.IsVisible(Android.Views.WindowInsets.Type.Ime()).ToString() : "n/a";
                return $"focused {edit.IsFocused}, selection {edit.SelectionStart}+{edit.SelectionEnd - edit.SelectionStart}, IME shown {ime}";
#endif
            default:
                return "-";
        }
    }

#if ANDROID
    private sealed class AttachCounter : Java.Lang.Object, Android.Views.View.IOnAttachStateChangeListener
    {
        public int Attached, Detached;
        public void OnViewAttachedToWindow(Android.Views.View? v) => Attached++;
        public void OnViewDetachedFromWindow(Android.Views.View? v) => Detached++;
    }

    private sealed class HierarchyCounter : Java.Lang.Object, Android.Views.ViewGroup.IOnHierarchyChangeListener
    {
        public int Added, Removed;
        public void OnChildViewAdded(Android.Views.View? parent, Android.Views.View? child) => Added++;
        public void OnChildViewRemoved(Android.Views.View? parent, Android.Views.View? child) => Removed++;
    }
#endif

    /// <summary>
    /// G1: a Controls VerticalStackLayout holding [direct "d0", a focused Entry with a selection, direct "d2",
    /// Controls "c3"]; a Controls label inserted at index 1 by one strategy. Recorded: whether it throws, the native
    /// order, the entry's focus, selection and IME, its Loaded/Unloaded, focus, handler and native-attach counts,
    /// native identity, and every child's frame.
    /// </summary>
    private static async Task LookMixedInsert(FixtureContext c, int strategy)
    {
        var log = new LookLog { Phase = new[] { "S0", "S1", "S2", "S3" }[strategy] };
        try
        {
            var layout = new VerticalStackLayout { Spacing = 4 };
            var host = new Grid { Children = { layout } };
            c.ShowNative(host);
            var d0 = NewDirectLabel("d0");
            var entry = new MauiEntry { Text = "hello world", AutomationId = "mixed-entry" };
            var d2 = NewDirectLabel("d2");
            var c3 = new MauiLabel { Text = "c3" };
            IView Wrap(DirectView direct)
            {
                if (strategy != 3) { direct.AttachToControlsParent(layout); return direct; }
                var wrapper = new Grid();
                wrapper.Add(direct);
                direct.AttachToControlsParent(wrapper);
                return wrapper;
            }
            void AddChild(IView child)
            {
                layout.Add(child);
                if (strategy == 2 && child is DirectView) layout.AddLogicalChild(new LogicalStandIn());
            }
            AddChild(Wrap(d0));
            AddChild(entry);
            AddChild(Wrap(d2));
            AddChild(c3);
            await NativeLoaded(c, c3);
            await c.Settle(200);

            entry.Focus();
            await c.WaitFor(() => entry.IsFocused, "the entry to focus", 3000);
            entry.CursorPosition = 2;
            entry.SelectionLength = 3;
            await c.Settle(500);
            var counts = new Dictionary<string, int>();
            void Count(string what) => counts[what] = counts.GetValueOrDefault(what) + 1;
            entry.Loaded += (_, _) => Count("Loaded");
            entry.Unloaded += (_, _) => Count("Unloaded");
            entry.Focused += (_, _) => Count("Focused");
            entry.Unfocused += (_, _) => Count("Unfocused");
            entry.HandlerChanging += (_, _) => Count("HandlerChanging");
            entry.ParentChanged += (_, _) => Count("ParentChanged");
            var nativeBefore = entry.Handler!.PlatformView;
            var handlerBefore = entry.Handler;
#if ANDROID
            var attach = new AttachCounter();
            ((Android.Views.View)nativeBefore!).AddOnAttachStateChangeListener(attach);
            var hierarchy = new HierarchyCounter();
            ((Android.Views.ViewGroup)layout.Handler!.PlatformView!).SetOnHierarchyChangeListener(hierarchy);
#endif
            log.Add($"before: order {NativeOrder(layout)}; entry {NativeFocus(entry)}, IsFocused {entry.IsFocused}, CursorPosition {entry.CursorPosition}+{entry.SelectionLength}");

            var inserted = new MauiLabel { Text = "inserted" };
            var clock = Stopwatch.StartNew();
            try
            {
                switch (strategy)
                {
                    case 1:
                        var suffix = new List<IView>();
                        while (layout.Count > 1) { suffix.Insert(0, layout[layout.Count - 1]); layout.RemoveAt(layout.Count - 1); }
                        layout.Add(inserted);
                        foreach (var child in suffix) layout.Add(child);
                        break;
                    default:
                        layout.Insert(1, inserted);
                        break;
                }
                log.Add($"insert took {clock.Elapsed.TotalMilliseconds:0.00} ms");
            }
            catch (Exception ex)
            {
                log.Add($"insert threw {ex.GetType().Name}: {ex.Message}");
            }
            await c.Settle(600);
            log.Add($"after: order {NativeOrder(layout)}; entry {NativeFocus(entry)}, IsFocused {entry.IsFocused}, CursorPosition {entry.CursorPosition}+{entry.SelectionLength}");
            log.Add($"entry events: {(counts.Count == 0 ? "none" : string.Join(", ", counts.Select(kv => $"{kv.Key}×{kv.Value}")))}; same native view {ReferenceEquals(nativeBefore, entry.Handler?.PlatformView)}, same handler {ReferenceEquals(handlerBefore, entry.Handler)}");
#if ANDROID
            log.Add($"native: entry detached {attach.Detached}, attached {attach.Attached}; layout children removed {hierarchy.Removed}, added {hierarchy.Added}");
#endif
            log.Add($"logical children: {string.Join(", ", ((IElementController)layout).LogicalChildren.Select(e => e switch { MauiLabel l => l.Text, MauiEntry => "entry", LogicalStandIn => "stand-in", Grid => "wrapper", _ => e.GetType().Name }))}");
            log.Add($"frames: {string.Join(", ", new (string, IView)[] { ("d0", d0), ("inserted", inserted), ("entry", entry), ("d2", d2), ("c3", c3) }.Select(v => $"{v.Item1} {Platform.WindowFrame(v.Item2)}"))}");
            Check(true, "measured");
        }
        finally
        {
            log.Dump("inserting into a Controls layout with direct children");
        }
    }

    /// <summary>
    /// G1, G4: the cost of one insertion at index 1 of a Controls layout with a direct first child and 300 Controls
    /// labels after it, for S1 (suffix removed and re-added) and S2 (stand-ins), and of mounting 300 direct labels with
    /// stand-ins (S2) or in wrappers (S3) against bare. Android also counts the platform's child adds and removes.
    /// </summary>
    private static async Task LookMixedInsertCost(FixtureContext c)
    {
        const int count = 300;
        var log = new LookLog();
        try
        {
            foreach (var strategy in new[] { 1, 2 })
            {
                log.Phase = strategy == 1 ? "S1" : "S2";
                var times = new List<double>();
                string ops = "";
                for (var round = 0; round < 4; round++)
                {
                    var layout = new VerticalStackLayout();
                    var direct = NewDirectLabel("d0");
                    direct.AttachToControlsParent(layout);
                    layout.Add(direct);
                    if (strategy == 2) layout.AddLogicalChild(new LogicalStandIn());
                    for (var i = 0; i < count; i++) layout.Add(new MauiLabel { Text = $"c{i}" });
                    c.ShowNative(new ScrollView { Content = layout });
                    await c.WaitFor(() => layout.Handler?.PlatformView is not null && layout.Width > 0, "the layout");
                    await c.Settle(300);
#if ANDROID
                    var hierarchy = new HierarchyCounter();
                    ((Android.Views.ViewGroup)layout.Handler!.PlatformView!).SetOnHierarchyChangeListener(hierarchy);
#endif
                    var inserted = new MauiLabel { Text = "inserted" };
                    var clock = Stopwatch.StartNew();
                    if (strategy == 1)
                    {
                        var suffix = new List<IView>();
                        while (layout.Count > 1) { suffix.Insert(0, layout[layout.Count - 1]); layout.RemoveAt(layout.Count - 1); }
                        layout.Add(inserted);
                        foreach (var child in suffix) layout.Add(child);
                    }
                    else layout.Insert(1, inserted);
                    times.Add(clock.Elapsed.TotalMilliseconds);
                    await c.Drain(2);
                    await c.Settle(200);
#if ANDROID
                    ops = $"; platform children removed {hierarchy.Removed}, added {hierarchy.Added}";
#endif
                }
                var sorted = times.Skip(1).OrderBy(t => t).ToArray();
                log.Add($"one insertion before {count} Controls labels: median {sorted[sorted.Length / 2]:0.00} ms (rounds {string.Join(", ", times.Select(t => t.ToString("0.00")))}){ops}");
            }

            foreach (var strategy in new[] { 0, 2, 3 })
            {
                log.Phase = strategy switch { 0 => "bare", 2 => "S2", _ => "S3" };
                var times = new List<double>();
                for (var round = 0; round < 4; round++)
                {
                    var layout = new VerticalStackLayout();
                    c.ShowNative(new ScrollView { Content = layout });
                    await c.WaitFor(() => layout.Handler?.PlatformView is not null, "the layout");
                    var clock = Stopwatch.StartNew();
                    for (var i = 0; i < count; i++)
                    {
                        var direct = NewDirectLabel($"d{i}");
                        if (strategy == 3)
                        {
                            var wrapper = new Grid();
                            wrapper.Add(direct);
                            direct.AttachToControlsParent(wrapper);
                            layout.Add(wrapper);
                        }
                        else
                        {
                            direct.AttachToControlsParent(layout);
                            layout.Add(direct);
                            if (strategy == 2) layout.AddLogicalChild(new LogicalStandIn());
                        }
                    }
                    await c.WaitFor(() => layout.Height > 0, "the layout to measure");
                    await c.Drain(2);
                    times.Add(clock.Elapsed.TotalMilliseconds);
                    await c.Settle(200);
                }
                var sorted = times.Skip(1).OrderBy(t => t).ToArray();
                log.Add($"mounting {count} direct labels in a Controls layout, through first layout: median {sorted[sorted.Length / 2]:0.0} ms (rounds {string.Join(", ", times.Select(t => t.ToString("0.0")))})");
            }
            Check(true, "measured");
        }
        finally
        {
            log.Dump("mixed-layout costs");
        }
    }

    /// <summary>
    /// G3: where MAUI's own IsEnabled inheritance applies and where Reactor's writes would land on the app's manual
    /// layer. (A) plain MAUI: a parent disabled, the app's local false on the child, the parent re-enabled. (B) the
    /// same with Reactor's inherited false written on the child's manual layer and cleared on re-enable. (C) an owned
    /// adapter as the MAUI parent of a Controls button inside a direct layout: inheritance, the app's local value,
    /// an implicit style and an AppThemeBinding reaching the button through it.
    /// </summary>
    private static async Task LookEnablingLayers(FixtureContext c)
    {
        var app = Application.Current!;
        var original = app.UserAppTheme;
        var log = new LookLog();
        try
        {
            // (A) and (B): Controls children of a Controls parent.
            var parent = new VerticalStackLayout();
            var plainChild = new MauiButton { Text = "plain MAUI child" };
            var overriddenChild = new MauiButton { Text = "child with Reactor's override" };
            parent.Add(plainChild);
            parent.Add(overriddenChild);
            var host = new Grid();
            host.Resources.Add(ButtonColorStyle());
            var directStack = new DirectStackLayout(StackOrientation.Vertical);
            var adapter = new LogicalStandIn();
            host.AddLogicalChild(adapter);
            var adapted = new MauiButton { Text = "adapted" };
            adapted.SetAppThemeColor(VisualElement.BackgroundColorProperty, LightRed, DarkBlue);
            adapter.AddLogicalChild(adapted);           // a logical child: Parent and IsEnabled propagation
            directStack.Add(adapted);
            // For contrast: Parent set without being a logical child (Reactor's anchoring today).
            var parentOnly = new LogicalStandIn();
            host.AddLogicalChild(parentOnly);
            var anchoredOnly = new MauiButton { Text = "parent only" };
            anchoredOnly.Parent = parentOnly;
            directStack.Add(anchoredOnly);
            var root = new VerticalStackLayout { Children = { parent, host } };
            host.Add(directStack);
            directStack.AttachToControlsParent(host);
            c.ShowNative(root);
            await NativeLoaded(c, adapted);
            await c.Settle(200);
            string State(MauiButton b) => $"IsEnabled {b.IsEnabled}, native {LookReviewPlatform.Enabled(b)}";

            log.Phase = "A plain MAUI";
            parent.IsEnabled = false;
            plainChild.IsEnabled = false; // the app's own local value, while the parent is disabled
            parent.IsEnabled = true;
            await c.Settle(200);
            log.Add($"parent disabled, the app sets the child's local false, parent re-enabled: child {State(plainChild)}");
            plainChild.ClearValue(VisualElement.IsEnabledProperty);

            log.Phase = "B Reactor's override";
            parent.IsEnabled = false;
            overriddenChild.SetValue(VisualElement.IsEnabledProperty, false); // Reactor's inherited false (manual layer)
            overriddenChild.IsEnabled = false;                               // the app's coincident local false
            parent.IsEnabled = true;
            overriddenChild.ClearValue(VisualElement.IsEnabledProperty);     // Reactor clears its override on re-enable
            await c.Settle(200);
            log.Add($"the same with Reactor's false on the child's manual layer and cleared on re-enable: child {State(overriddenChild)}");

            log.Phase = "C adapter";
            var events = 0;
            adapted.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(VisualElement.IsEnabled)) events++; };
            log.Add($"mounted through the adapter: {State(adapted)}; parent {adapted.Parent?.GetType().Name}; implicit style text {LookPlatform.Describe(LookPlatform.TextColor(adapted))}; background px {LookPlatform.Describe(Snapshot.Capture(adapted).At(0.1, 0.5))}");
            adapter.IsEnabled = false;
            parentOnly.IsEnabled = false;
            await c.Settle(200);
            log.Add($"adapter disabled: {State(adapted)}, IsEnabled events {events}; the parent-only contrast: {State(anchoredOnly)}");
            adapted.IsEnabled = false; // the app's local false
            adapter.IsEnabled = true;
            await c.Settle(200);
            log.Add($"the app's local false, adapter re-enabled: {State(adapted)} (the app's value kept, nothing cleared)");
            adapted.ClearValue(VisualElement.IsEnabledProperty);
            await c.Settle(200);
            log.Add($"the app clears its value: {State(adapted)}, IsEnabled events {events}");
            app.UserAppTheme = Other(app.RequestedTheme);
            await c.Settle(400);
            log.Add($"a theme change through the adapter: background px {LookPlatform.Describe(Snapshot.Capture(adapted).At(0.1, 0.5))}, theme {app.RequestedTheme}");
            Check(true, "measured");
        }
        finally
        {
            app.UserAppTheme = original;
            log.Dump("IsEnabled layers");
        }
    }

    private static Style ButtonColorStyle()
    {
        var style = new Style(typeof(MauiButton));
        style.Setters.Add(new Setter { Property = MauiButton.TextColorProperty, Value = Microsoft.Maui.Graphics.Colors.Yellow });
        return style;
    }
}
