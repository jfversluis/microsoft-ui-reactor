using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Maui;
using static Microsoft.UI.Reactor.Maui.Factories;
using static Reactor.Maui.DeviceTests.FixtureContext;
using MauiButton = Microsoft.Maui.Controls.Button;
using MauiLabel = Microsoft.Maui.Controls.Label;

namespace Reactor.Maui.DeviceTests;

// Spec 071 §2.1, the third review round of f63bbb5f: the Android repair hook's ownership and generation (F1), and
// MAUI's own enabling under Reactor (F2). On demand.
public static partial class Fixtures
{
    private static IEnumerable<Fixture> LookRound3CharacterizationFixtures() =>
    [
        new("Characterize_Look_RepairHook", LookRepairHook, 90, OnDemand: true),
        .. Renderers.Select(r => new Fixture($"Characterize_Look_MauiEnabling [{r.Name}]", c => LookMauiEnabling(c, r.Create()), 60, OnDemand: true)),
    ];

#if ANDROID
    /// <summary>
    /// A stand-in for the proposed hook (F1): live views registered as owned, each invocation rechecking
    /// registration, handler identity, the handler's liveness and IsSet, then resolving a reference for the
    /// current generation (built on the view's handler context with MAUI's recipe) and writing it natively.
    /// </summary>
    private sealed class RepairHookModel
    {
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<IView, IViewHandler> _owned = new();
        private readonly Dictionary<int, Android.Content.Res.ColorStateList> _snapshots = [];
        public int Generation;
        public int Invocations, Writes, Skipped, SnapshotsBuilt;
        public readonly List<string> Log = [];

        public void Own(IView view) => _owned.AddOrUpdate(view, view.Handler!);
        public void Disown(IView view) => _owned.Remove(view);

        public void Announce()
        {
            Generation++;
            _snapshots.Clear();
        }

        public void OnMapped(IElement element, string key)
        {
            if (key != "TextColor" || element is not MauiButton button) return;
            Invocations++;
            string? reason = null;
            if (!_owned.TryGetValue(button, out var ownedHandler)) reason = "not owned (unmounted or never registered)";
            else if (!ReferenceEquals(ownedHandler, button.Handler)) reason = "another handler";
            else if (button.Handler?.PlatformView is not Android.Widget.Button native) reason = "no platform view";
            else if (button.IsSet(MauiButton.TextColorProperty)) reason = "a MAUI source owns the value";
            else
            {
                var context = button.Handler.MauiContext!.Context!;
                if (!_snapshots.TryGetValue(Generation, out var list))
                {
                    using var reference = new Microsoft.Maui.Platform.MauiMaterialButton(context);
                    list = reference.TextColors!;
                    _snapshots[Generation] = list;
                    SnapshotsBuilt++;
                }
                native.SetTextColor(list);
                Writes++;
                Log.Add($"generation {Generation}: wrote {Csl(list)}");
                return;
            }
            Skipped++;
            Log.Add($"generation {Generation}: skipped, {reason}");
        }
    }
#endif

    /// <summary>
    /// Android, F1's three cases against a stand-in for the hook, each with a hook-less control: (a) mounted disabled
    /// under a Disabled-only TextColor setter, Light→Dark while disabled, enabled; (b) repaired in Dark, a Disabled
    /// setter takes over, Dark→Light while it holds, re-enabled; (c) disowned (Reactor's unmount) before MAUI maps.
    /// </summary>
    private static async Task LookRepairHook(FixtureContext c)
    {
#if !ANDROID
        await Task.CompletedTask;
        throw new SkipException("Android's repair hook");
#else
        var app = Application.Current!;
        var original = app.UserAppTheme;
        var log = new LookLog();
        var model = new RepairHookModel();
        var previous = s_lookMapped;
        HookLookMappers();
        void OnTheme(object? s, AppThemeChangedEventArgs e) => model.Announce();
        app.RequestedThemeChanged += OnTheme;
        s_lookMapped = model.OnMapped;
        string Text(IView v) => LookPlatform.Describe(LookPlatform.TextColor(v));
        try
        {
            app.UserAppTheme = AppTheme.Light;
            await c.Settle(300);

            // (a) Mounted disabled, Light→Dark while disabled, enabled.
            log.Phase = "a";
            var a = new MauiButton { Text = "a", IsEnabled = false, Style = DisabledOnlyTextStyle() };
            var aControl = new MauiButton { Text = "a control", IsEnabled = false, Style = DisabledOnlyTextStyle() };
            // (b) Repaired in Dark, then a Disabled setter takes ownership.
            var b = new MauiButton { Text = "b" };
            var bControl = new MauiButton { Text = "b control (retained list)" };
            // (c) Disowned before the mapping callback.
            var cOwned = new MauiButton { Text = "c owned" };
            var cGone = new MauiButton { Text = "c unmounted" };
            var stack = new VerticalStackLayout { Spacing = 4, Children = { a, aControl, b, bControl, cOwned, cGone } };
            c.ShowNative(stack);
            await NativeLoaded(c, cGone);
            model.Own(a);
            model.Own(b);
            model.Own(cOwned);
            model.Own(cGone);
            log.Add($"mounted, Light: a {Text(a)} (disabled, the state's yellow), a control {Text(aControl)}");
            app.UserAppTheme = AppTheme.Dark;
            await c.Settle(400);
            log.Add($"Light→Dark while disabled: a {Text(a)}, invocations {model.Invocations}, writes {model.Writes}");
            a.IsEnabled = true;
            aControl.IsEnabled = true;
            await c.Settle(300);
            log.Add($"enabled under Dark: a {Text(a)} (writes {model.Writes}); a control without the hook {Text(aControl)}");
            foreach (var line in model.Log) log.Add("  hook: " + line);
            model.Log.Clear();

            log.Phase = "b";
            // Repair b in Dark the way a theme pass would: invoke the mapping (MAUI maps TextColor with no value).
            b.Handler!.UpdateValue(nameof(ITextStyle.TextColor));
            var retained = ((Android.Widget.Button)bControl.Handler!.PlatformView!);
            using (var darkReference = new Microsoft.Maui.Platform.MauiMaterialButton(bControl.Handler.MauiContext!.Context!))
                retained.SetTextColor(darkReference.TextColors);
            var retainedList = retained.TextColors;
            log.Add($"repaired in Dark: b {Text(b)}, b control {Text(bControl)}");
            b.Style = DisabledOnlyTextStyle();
            bControl.Style = DisabledOnlyTextStyle();
            b.IsEnabled = false;
            bControl.IsEnabled = false;
            await c.Settle(300);
            log.Add($"a Disabled setter takes over: b {Text(b)} IsSet {b.IsSet(MauiButton.TextColorProperty)}");
            app.UserAppTheme = AppTheme.Light;
            await c.Settle(400);
            log.Add($"Dark→Light while the setter holds: b {Text(b)}");
            b.IsEnabled = true;
            bControl.IsEnabled = true;
            await c.Settle(300);
            // The control replays the list it retained from Dark, as a hook keyed by "previously repaired" would.
            retained.SetTextColor(retainedList);
            await c.Settle(100);
            log.Add($"re-enabled under Light: b {Text(b)}; b control replaying the retained Dark list {Text(bControl)}");
            using (var fresh = new Microsoft.Maui.Platform.MauiMaterialButton(b.Handler!.MauiContext!.Context!))
                log.Add($"a fresh reference under Light: {Csl(fresh.TextColors)}");
            foreach (var line in model.Log) log.Add("  hook: " + line);
            model.Log.Clear();

            log.Phase = "c";
            model.Disown(cGone);
            var before = model.Writes;
            cGone.Handler!.UpdateValue(nameof(ITextStyle.TextColor));
            var afterGone = model.Writes;
            cOwned.Handler!.UpdateValue(nameof(ITextStyle.TextColor));
            log.Add($"disowned, then MAUI maps: writes {afterGone - before}; the owned positive control: writes {model.Writes - afterGone}");
            foreach (var line in model.Log) log.Add("  hook: " + line);
            log.Add($"totals: invocations {model.Invocations}, writes {model.Writes}, skipped {model.Skipped}, snapshots built {model.SnapshotsBuilt}");
            Check(model.Writes > 0, "the hook wrote at least once (instrument)");
        }
        finally
        {
            s_lookMapped = previous;
            app.RequestedThemeChanged -= OnTheme;
            app.UserAppTheme = original;
            log.Dump("the repair hook's ownership and generation");
        }
#endif
    }

    /// <summary>
    /// F2: MAUI's own enabling under Reactor, both registries. A Controls Button disabled by its XAML style
    /// (<c>.Style</c>), one disabled by a local write (<c>.Set</c>), and a Controls layout disabled by its XAML style
    /// holding a button (direct or Controls by registry) and an anchored Controls button. Read back IsEnabled,
    /// IsSet, the native state and the IsEnabled property-changed events, across a Reactor parent disable and
    /// re-enable, an unrelated child render, and a local override written then cleared.
    /// </summary>
    private static async Task LookMauiEnabling(FixtureContext c, ElementHandlerRegistry registry)
    {
        var log = new LookLog();
        var disabledButton = new Style(typeof(MauiButton));
        disabledButton.Setters.Add(new Setter { Property = VisualElement.IsEnabledProperty, Value = false });
        var disabledLayout = new Style(typeof(VerticalStackLayout));
        disabledLayout.Setters.Add(new Setter { Property = VisualElement.IsEnabledProperty, Value = false });
        try
        {
            Element Tree(bool parentEnabled, string tick) => VStack(4,
                Button("xaml-disabled").Style(disabledButton).AutomationId("f2-styled"),
                Button("local-disabled").Set<ButtonElement, MauiButton>(b => b.IsEnabled = false).AutomationId("f2-local"),
                // The Controls child first: a direct child before a Controls child in a Controls layout makes MAUI's
                // Layout.Insert throw (InsertLogicalChild with the view index), a defect recorded in spec 071 §2.1.8.
                VStack(
                    Button("anchored in it").OnTap(() => { }).AutomationId("f2-anchored"),
                    Button("in a xaml-disabled layout").AutomationId("f2-child"),
                    Label(tick).AutomationId("f2-tick")).Style(disabledLayout).AutomationId("f2-layout")).IsEnabled(parentEnabled).AutomationId("f2-root");
            c.Show(Tree(true, "0"), registry);
            IButton styled;
            try { styled = await Loaded<IButton>(c, "f2-styled"); }
            catch (CheckFailedException)
            {
                log.Add($"not mounted; unhandled errors: {string.Join(" | ", c.UnhandledErrors.Select(e => e.ToString().Replace("\n", " // ").Substring(0, Math.Min(2500, e.ToString().Length))))}; visible: {string.Join(", ", c.FindAll<IView>().Select(v => $"{v.GetType().Name}[{v.AutomationId}]"))}");
                throw;
            }
            var local = c.ById<IButton>("f2-local");
            var layout = c.ById<IView>("f2-layout");
            // A Controls layout lists only Controls children as visual children (spec 065 §4), so a direct child is
            // found through the layout's own list.
            var child = c.TryFind<IButton>(v => v.AutomationId == "f2-child")
                ?? ((IEnumerable<IView>)layout).OfType<IButton>().First(v => v.AutomationId == "f2-child");
            var anchored = c.ById<IButton>("f2-anchored");
            await c.WaitFor(() => anchored.Handler is not null, "the views to have handlers");
            await c.Settle(300);
            var events = new List<string>();
            foreach (var (name, view) in new (string, IView)[] { ("styled", styled), ("local", local), ("layout", layout), ("child", child), ("anchored", anchored) })
                if (view is BindableObject bindable)
                    bindable.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(VisualElement.IsEnabled)) events.Add($"{name}→{((VisualElement)bindable).IsEnabled}"); };
            string Describe(string name, IView view) => view is VisualElement ve
                ? $"{name} ({view.GetType().Name}): IsEnabled {ve.IsEnabled}, IsSet {ve.IsSet(VisualElement.IsEnabledProperty)}, native {LookReviewPlatform.Enabled(view)}"
                : $"{name} ({view.GetType().Name}): native {LookReviewPlatform.Enabled(view)}";
            void Log(string when)
            {
                log.Add($"{when}: " + string.Join("; ", new (string, IView)[] { ("styled", styled), ("local", local), ("layout", layout), ("child", child), ("anchored", anchored) }.Select(v => Describe(v.Item1, v.Item2))) + $"; IsEnabled events [{string.Join(", ", events)}]");
                events.Clear();
            }
            Log("mounted, the Reactor parent enabled");
            c.Render(Tree(true, "1"));
            await c.Drain();
            await c.Settle(200);
            Log("an unrelated child render");
            c.Render(Tree(false, "1"));
            await c.Drain();
            await c.Settle(200);
            Log("the Reactor parent disabled");
            c.Render(Tree(true, "1"));
            await c.Drain();
            await c.Settle(200);
            Log("re-enabled");
            if (styled is VisualElement styledElement)
            {
                styledElement.IsEnabled = true;
                log.Add($"a local true written over the style: IsEnabled {styledElement.IsEnabled}, IsSet {styledElement.IsSet(VisualElement.IsEnabledProperty)}");
                styledElement.ClearValue(VisualElement.IsEnabledProperty);
                log.Add($"cleared: IsEnabled {styledElement.IsEnabled} (the style's value read back)");
                events.Clear();
            }
            if (layout is VisualElement source)
            {
                // The MAUI source changing outside Reactor's writes: the app enables the layout locally, then clears it.
                events.Clear();
                source.IsEnabled = true;
                await c.Settle(100);
                log.Add($"the layout enabled by a local write outside Reactor: {Describe("layout", layout)}; {Describe("anchored", anchored)}; {Describe("child", child)}; IsEnabled events [{string.Join(", ", events)}]");
                events.Clear();
                source.ClearValue(VisualElement.IsEnabledProperty);
                await c.Settle(100);
                log.Add($"cleared back to its style: {Describe("layout", layout)}; {Describe("anchored", anchored)}; {Describe("child", child)}; IsEnabled events [{string.Join(", ", events)}]");
                events.Clear();
            }
            if (layout is VisualElement layoutElement && child is VisualElement childElement)
            {
                childElement.IsEnabled = true;
                log.Add($"a local true on the child of the disabled layout: IsEnabled {childElement.IsEnabled} (MAUI's inheritance)");
                childElement.ClearValue(VisualElement.IsEnabledProperty);
                events.Clear();
            }
        }
        finally
        {
            log.Dump("MAUI's own enabling under Reactor");
            c.Unmount();
        }
        await c.Settle(300);
    }
}
