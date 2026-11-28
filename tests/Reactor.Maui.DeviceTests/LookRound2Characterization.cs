using System.Diagnostics;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui;
using Microsoft.UI.Reactor.Maui.Direct;
using static Microsoft.UI.Reactor.Maui.Factories;
using static Reactor.Maui.DeviceTests.FixtureContext;
using MauiButton = Microsoft.Maui.Controls.Button;
using MauiEntry = Microsoft.Maui.Controls.Entry;
using MauiLabel = Microsoft.Maui.Controls.Label;

namespace Reactor.Maui.DeviceTests;

// Spec 071 §2.1, the second review round of f72f44ae: where a Controls view's color comes from (E1), Android's
// default resolver measured inside the theme change and against fresh handler-created views (E1, E2), and what
// MAUI shows around a suppressed nested theme set, plus when the platform's appearance settles and on which
// thread the change is raised (E9, E10). On demand.
public static partial class Fixtures
{
    private static IEnumerable<Fixture> LookRound2CharacterizationFixtures() =>
    [
        new("Characterize_Look_ValueSources", LookValueSources, 60, OnDemand: true),
        new("Characterize_Look_AndroidResolver [app]", c => LookAndroidResolver(c, system: false), 90, OnDemand: true),
        new("Characterize_Look_AndroidResolver [system]", c => LookAndroidResolver(c, system: true), 120, OnDemand: true),
        new("Characterize_Look_Divergence", LookDivergence, 90, OnDemand: true),
    ];

    /// <summary>A Button style whose only TextColor setter is in the Disabled state: re-enabling maps TextColor back to null.</summary>
    private static Style DisabledOnlyTextStyle()
    {
        var style = new Style(typeof(MauiButton));
        var groups = new VisualStateGroupList();
        var common = new VisualStateGroup { Name = "CommonStates" };
        common.States.Add(new VisualState { Name = "Normal" });
        var disabled = new VisualState { Name = "Disabled" };
        disabled.Setters.Add(new Setter { Property = MauiButton.TextColorProperty, Value = Microsoft.Maui.Graphics.Colors.Yellow });
        common.States.Add(disabled);
        groups.Add(common);
        style.Setters.Add(new Setter { Property = VisualStateManager.VisualStateGroupsProperty, Value = groups });
        return style;
    }

    private static Style LabelColorStyle(Color color, bool themed = false)
    {
        var style = new Style(typeof(MauiLabel));
        style.Setters.Add(new Setter
        {
            Property = MauiLabel.TextColorProperty,
            Value = themed ? new AppThemeBinding { Light = color, Dark = Microsoft.Maui.Graphics.Colors.Orange } : color,
        });
        return style;
    }

    /// <summary>
    /// <c>BindableObject.IsSet</c> for a label's TextColor by where its value comes from: nothing, a local value, an
    /// implicit style, an explicit style, a themed style setter, an AppThemeBinding, a DynamicResource, a local value
    /// cleared over an implicit style, and a Disabled visual state (active, then unapplied).
    /// </summary>
    private static async Task LookValueSources(FixtureContext c)
    {
        var log = new LookLog();
        try
        {
            var none = new MauiLabel { Text = "none" };
            var local = new MauiLabel { Text = "local", TextColor = Microsoft.Maui.Graphics.Colors.Red };
            var implicitHost = new VerticalStackLayout();
            implicitHost.Resources.Add(LabelColorStyle(Microsoft.Maui.Graphics.Colors.Green));
            var implicitLabel = new MauiLabel { Text = "implicit" };
            var clearedOverImplicit = new MauiLabel { Text = "cleared over implicit", TextColor = Microsoft.Maui.Graphics.Colors.Red };
            implicitHost.Add(implicitLabel);
            implicitHost.Add(clearedOverImplicit);
            var explicitLabel = new MauiLabel { Text = "explicit", Style = LabelColorStyle(Microsoft.Maui.Graphics.Colors.Purple) };
            var themedSetter = new MauiLabel { Text = "themed setter", Style = LabelColorStyle(Microsoft.Maui.Graphics.Colors.Purple, themed: true) };
            var binding = new MauiLabel { Text = "binding" };
            binding.SetAppThemeColor(MauiLabel.TextColorProperty, LightRed, DarkBlue);
            var dynamicLabel = new MauiLabel { Text = "dynamic" };
            var vsmButton = new MauiButton { Text = "vsm", Style = DisabledButtonStyle(isImplicit: false) };
            var plainButton = new MauiButton { Text = "plain button" };
            var root = new VerticalStackLayout { Children = { none, local, implicitHost, explicitLabel, themedSetter, binding, dynamicLabel, vsmButton, plainButton } };
            root.Resources["LookAccent"] = Microsoft.Maui.Graphics.Colors.Teal;
            dynamicLabel.SetDynamicResource(MauiLabel.TextColorProperty, "LookAccent");
            c.ShowNative(root);
            await NativeLoaded(c, plainButton);
            clearedOverImplicit.ClearValue(MauiLabel.TextColorProperty);
            await c.Settle(200);
            (string, BindableObject, BindableProperty)[] probes =
            [
                ("none", none, MauiLabel.TextColorProperty), ("local", local, MauiLabel.TextColorProperty),
                ("implicit style", implicitLabel, MauiLabel.TextColorProperty), ("local cleared over an implicit style", clearedOverImplicit, MauiLabel.TextColorProperty),
                ("explicit style", explicitLabel, MauiLabel.TextColorProperty), ("themed style setter", themedSetter, MauiLabel.TextColorProperty),
                ("AppThemeBinding", binding, MauiLabel.TextColorProperty), ("DynamicResource", dynamicLabel, MauiLabel.TextColorProperty),
                ("button TextColor, explicit style with a TextColor setter, enabled", vsmButton, MauiButton.TextColorProperty),
                ("button TextColor, no style", plainButton, MauiButton.TextColorProperty),
                ("button BackgroundColor, no style", plainButton, VisualElement.BackgroundColorProperty),
            ];
            foreach (var (name, target, property) in probes)
                log.Add($"{name}: IsSet {target.IsSet(property)}, value {(target.GetValue(property) as Color)?.ToArgbHex() ?? "null"}");
            Check(!none.IsSet(MauiLabel.TextColorProperty) && local.IsSet(MauiLabel.TextColorProperty), "IsSet tells a local value from none (the instrument)");

            vsmButton.IsEnabled = false;
            await c.Settle(300);
            log.Add($"button with a Disabled state, disabled: TextColor IsSet {vsmButton.IsSet(MauiButton.TextColorProperty)}, value {vsmButton.TextColor?.ToArgbHex() ?? "null"}");
            vsmButton.IsEnabled = true;
            await c.Settle(300);
            log.Add($"the same, re-enabled (the state unapplied): TextColor IsSet {vsmButton.IsSet(MauiButton.TextColorProperty)}, value {vsmButton.TextColor?.ToArgbHex() ?? "null"}");
        }
        finally
        {
            log.Dump("where a TextColor comes from, and IsSet");
        }
    }

#if ANDROID
    private sealed record Snapshot3(string Text, string? Tint, string? Hint);

    private static string Csl(Android.Content.Res.ColorStateList? list)
    {
        if (list is null) return "null";
        static string H(int argb) => $"#{(argb >> 16) & 0xFF:X2}{(argb >> 8) & 0xFF:X2}{argb & 0xFF:X2}@{((argb >> 24) & 0xFF) / 255.0:0.00}";
        return $"{H(list.DefaultColor)}/disabled {H(list.GetColorForState([-Android.Resource.Attribute.StateEnabled], new Android.Graphics.Color(list.DefaultColor)))}";
    }

    private static string Night(Android.Content.Context? context) =>
        context?.Resources?.Configuration is { } configuration && (configuration.UiMode & Android.Content.Res.UiMode.NightMask) == Android.Content.Res.UiMode.NightYes ? "night" : "day";

    private static string ContextChain(Android.Content.Context? context)
    {
        var names = new List<string>();
        for (var current = context; current is not null && names.Count < 6; current = (current as Android.Content.ContextWrapper)?.BaseContext)
            names.Add($"{current.GetType().Name}({Night(current)})");
        return string.Join(" > ", names);
    }
#endif

    /// <summary>
    /// Android (E1, E2): inside RequestedThemeChanged, app- or system-started, the existing views' contexts and
    /// reference views built with MAUI's own recipes (MauiTextView, MauiMaterialButton, which wraps its context in
    /// MauiMaterialContextThemeWrapper, MauiAppCompatEditText) on each target's context and on the handler's base
    /// context, timed cold and warm and disposed; compared with fresh handler-created views mounted after the change.
    /// Then a repair: the snapshot list written to a view with no MAUI value, re-applied by an appended mapper after
    /// MAUI clears (a Disabled state unapplied), and kept off views whose value comes from a style or binding.
    /// </summary>
    private static async Task LookAndroidResolver(FixtureContext c, bool system)
    {
#if !ANDROID
        await Task.CompletedTask;
        throw new SkipException("Android's default resolver");
#else
        var app = Application.Current!;
        var original = app.UserAppTheme;
        var log = new LookLog();
        var repaired = new System.Runtime.CompilerServices.ConditionalWeakTable<Android.Views.View, Android.Content.Res.ColorStateList>();
        Action<IElement, string>? previousHook = s_lookMapped;
        HookLookMappers();
        try
        {
            app.UserAppTheme = system ? AppTheme.Unspecified : AppTheme.Light;
            await c.Settle(300);
            var plainLabel = new MauiLabel { Text = "plain label" };
            var plainButton = new MauiButton { Text = "plain button" };
            var stateButton = new MauiButton { Text = "state button" };
            var vsmButton = new MauiButton { Text = "vsm button", Style = DisabledOnlyTextStyle() };
            var hintEntry = new MauiEntry { Placeholder = "hint" };
            var implicitHost = new VerticalStackLayout();
            implicitHost.Resources.Add(LabelColorStyle(Microsoft.Maui.Graphics.Colors.Green));
            var styledLabel = new MauiLabel { Text = "implicit style" };
            var clearedToStyle = new MauiLabel { Text = "cleared to the style", TextColor = Microsoft.Maui.Graphics.Colors.Red };
            implicitHost.Add(styledLabel);
            implicitHost.Add(clearedToStyle);
            var themedLabel = new MauiLabel { Text = "themed setter", Style = LabelColorStyle(Microsoft.Maui.Graphics.Colors.Purple, themed: true) };
            var stack = new VerticalStackLayout { Spacing = 4, Children = { plainLabel, plainButton, stateButton, vsmButton, hintEntry, implicitHost, themedLabel } };
            c.ShowNative(stack);
            await NativeLoaded(c, themedLabel);
            var start = app.RequestedTheme;
            var target = Other(start);
            var nativeLabel = (Android.Widget.TextView)plainLabel.Handler!.PlatformView!;
            var nativeButton = (Android.Widget.Button)plainButton.Handler!.PlatformView!;
            var nativeEntry = (Android.Widget.EditText)hintEntry.Handler!.PlatformView!;
            var baseContext = plainLabel.Handler.MauiContext!.Context!;
            log.Add($"contexts: label {ContextChain(nativeLabel.Context)}; button {ContextChain(nativeButton.Context)}; entry {ContextChain(nativeEntry.Context)}; handler base {ContextChain(baseContext)}");
            log.Add($"before: label {Csl(nativeLabel.TextColors)}, button text {Csl(nativeButton.TextColors)} tint {Csl(nativeButton.BackgroundTintList)}, entry hint {Csl(nativeEntry.HintTextColors)}");

            var inEvent = new List<string>();
            Snapshot3? labelSnapshot = null, buttonSnapshot = null;
            Android.Content.Res.ColorStateList? labelList = null, buttonTextList = null;
            void OnChanged(object? s, AppThemeChangedEventArgs e)
            {
                inEvent.Add($"event {e.RequestedTheme} on {(MainThread.IsMainThread ? "the main thread" : $"thread {Environment.CurrentManagedThreadId}")}; contexts now: label {ContextChain(nativeLabel.Context)}, button {ContextChain(nativeButton.Context)}");
                var clock = Stopwatch.StartNew();
                using (var refLabel = new Microsoft.Maui.Platform.MauiTextView(nativeLabel.Context!))
                {
                    labelList = refLabel.TextColors;
                    labelSnapshot = new(Csl(refLabel.TextColors), null, null);
                }
                var labelCold = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                using (var refButton = new Microsoft.Maui.Platform.MauiMaterialButton(baseContext))
                {
                    buttonTextList = refButton.TextColors;
                    buttonSnapshot = new(Csl(refButton.TextColors), Csl(refButton.BackgroundTintList), null);
                }
                var buttonCold = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                string buttonOnItsContext;
                using (var refButton2 = new Google.Android.Material.Button.MaterialButton(nativeButton.Context!))
                    buttonOnItsContext = $"{Csl(refButton2.TextColors)} tint {Csl(refButton2.BackgroundTintList)}";
                string hint;
                using (var refEntry = new Microsoft.Maui.Platform.MauiAppCompatEditText(nativeEntry.Context!))
                    hint = Csl(refEntry.HintTextColors);
                var rest = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                using (var warm = new Microsoft.Maui.Platform.MauiTextView(nativeLabel.Context!)) _ = warm.TextColors;
                var labelWarm = clock.Elapsed.TotalMilliseconds;
                inEvent.Add($"in the event: MauiTextView on the label's context {labelSnapshot.Text} (cold {labelCold:0.00} ms, warm {labelWarm:0.00} ms); MauiMaterialButton recipe on the handler's base context text {buttonSnapshot.Text} tint {buttonSnapshot.Tint} ({buttonCold:0.00} ms); MaterialButton on the button's own (wrapped) context {buttonOnItsContext}; MauiAppCompatEditText hint {hint} ({rest:0.00} ms for the last two)");
            }
            app.RequestedThemeChanged += OnChanged;
            try
            {
                if (system)
                {
                    Console.WriteLine($"REACTOR-LOOK-TOGGLE-SYSTEM {(target == AppTheme.Dark ? "dark" : "light")}");
                    var clock = Stopwatch.StartNew();
                    while (app.RequestedTheme != target && clock.ElapsedMilliseconds < 30000) await Task.Delay(16);
                    if (app.RequestedTheme != target) throw new SkipException("no host flipped the system appearance (REACTOR-LOOK-TOGGLE-SYSTEM)");
                }
                else app.UserAppTheme = target;
                foreach (var line in inEvent) log.Add(line);
                Check(labelSnapshot is not null, "the in-event snapshot ran (instrument)");
                await c.Settle(600);

                // What MAUI itself creates after the change: fresh handler-created views.
                var freshLabel = new MauiLabel { Text = "fresh label" };
                var freshButton = new MauiButton { Text = "fresh button" };
                var freshEntry = new MauiEntry { Placeholder = "fresh hint" };
                stack.Add(freshLabel);
                stack.Add(freshButton);
                stack.Add(freshEntry);
                await NativeLoaded(c, freshEntry);
                var fl = (Android.Widget.TextView)freshLabel.Handler!.PlatformView!;
                var fb = (Android.Widget.Button)freshButton.Handler!.PlatformView!;
                var fe = (Android.Widget.EditText)freshEntry.Handler!.PlatformView!;
                log.Add($"fresh handler-created views after the change: label {Csl(fl.TextColors)}, button text {Csl(fb.TextColors)} tint {Csl(fb.BackgroundTintList)}, entry hint {Csl(fe.HintTextColors)}");
                log.Add($"existing views after the change: label {Csl(nativeLabel.TextColors)}, button text {Csl(nativeButton.TextColors)} tint {Csl(nativeButton.BackgroundTintList)}, entry hint {Csl(nativeEntry.HintTextColors)}");

                // The repair, only where no MAUI source supplies the value.
                (string, BindableObject, BindableProperty, Android.Widget.TextView)[] candidates =
                [
                    ("plain label", plainLabel, MauiLabel.TextColorProperty, nativeLabel),
                    ("implicit style", styledLabel, MauiLabel.TextColorProperty, (Android.Widget.TextView)styledLabel.Handler!.PlatformView!),
                    ("cleared to the style", clearedToStyle, MauiLabel.TextColorProperty, (Android.Widget.TextView)clearedToStyle.Handler!.PlatformView!),
                    ("themed setter", themedLabel, MauiLabel.TextColorProperty, (Android.Widget.TextView)themedLabel.Handler!.PlatformView!),
                ];
                clearedToStyle.ClearValue(MauiLabel.TextColorProperty);
                await c.Settle(200);
                foreach (var (name, view, property, native) in candidates)
                {
                    var own = !view.IsSet(property);
                    if (own) { native.SetTextColor(labelList); repaired.AddOrUpdate(native, labelList!); }
                    log.Add($"{name}: IsSet {view.IsSet(property)} → {(own ? "repaired" : "left to MAUI")}: {LookPlatform.Describe(LookPlatform.TextColor((IView)view))}");
                }
                var nativeState = (Android.Widget.Button)stateButton.Handler!.PlatformView!;
                nativeState.SetTextColor(buttonTextList);
                repaired.AddOrUpdate(nativeState, buttonTextList!);
                var nativeVsm = (Android.Widget.Button)vsmButton.Handler!.PlatformView!;
                repaired.AddOrUpdate(nativeVsm, buttonTextList!);
                nativeVsm.SetTextColor(buttonTextList);

                // MAUI re-maps TextColor with null when a Disabled state unapplies; the hook re-applies the list after it.
                var hooked = 0;
                s_lookMapped = (element, key) =>
                {
                    if (key != "TextColor" || element is not ITextStyle { TextColor: null } || (element as IView)?.Handler?.PlatformView is not Android.Widget.TextView native) return;
                    if (repaired.TryGetValue(native, out var list)) { native.SetTextColor(list); hooked++; }
                };
                foreach (var enabled in new[] { false, true })
                {
                    stateButton.IsEnabled = enabled;
                    vsmButton.IsEnabled = enabled;
                    await c.Settle(300);
                    log.Add($"IsEnabled {enabled}: state button (repaired, no style) text {LookPlatform.Describe(LookPlatform.TextColor(stateButton))}; vsm button (repaired, a TextColor setter only in its Disabled state) text {LookPlatform.Describe(LookPlatform.TextColor(vsmButton))}; hook re-applies so far {hooked}");
                }
                s_lookMapped = previousHook;
                vsmButton.IsEnabled = false;
                await c.Settle(300);
                vsmButton.IsEnabled = true;
                await c.Settle(300);
                log.Add($"control without the hook: vsm button re-enabled text {LookPlatform.Describe(LookPlatform.TextColor(vsmButton))} (MAUI's cached list)");
            }
            finally
            {
                app.RequestedThemeChanged -= OnChanged;
                s_lookMapped = previousHook;
            }
            if (system)
            {
                Console.WriteLine($"REACTOR-LOOK-TOGGLE-SYSTEM {(start == AppTheme.Dark ? "dark" : "light")}");
                var clock = Stopwatch.StartNew();
                while (app.RequestedTheme != start && clock.ElapsedMilliseconds < 30000) await Task.Delay(16);
            }
        }
        finally
        {
            app.UserAppTheme = original;
            log.Dump($"Android's default resolver, {(system ? "system" : "app")}-started");
        }
#endif
    }

    /// <summary>
    /// E9, E10: several bindings during a suppressed nested set; a plain AppThemeBinding and a Reactor tree mounted
    /// after the divergence; the thread the change is raised on; and, for a normal change, the frame at which the
    /// platform's appearance settles relative to the event.
    /// </summary>
    private static async Task LookDivergence(FixtureContext c)
    {
        var app = Application.Current!;
        var original = app.UserAppTheme;
        var log = new LookLog();
        try
        {
            app.UserAppTheme = AppTheme.Light;
            var labels = Enumerable.Range(0, 4).Select(i =>
            {
                var l = new MauiLabel { Text = $"bound {i}" };
                l.SetAppThemeColor(MauiLabel.TextColorProperty, LightRed, DarkBlue);
                return l;
            }).ToArray();
            var plain = new MauiLabel { Text = "plain" };
            var stack = new VerticalStackLayout { Children = { labels[0], labels[1], labels[2], labels[3], plain } };
            c.ShowNative(stack);
            await NativeLoaded(c, plain);

            // A normal change: the event's thread, and the frame the platform's appearance matches it.
            log.Phase = "normal change";
            var appearanceAtEvent = "";
            string thread = "";
            void Normal(object? s, AppThemeChangedEventArgs e)
            {
                appearanceAtEvent = LookPlatform.Appearance(plain);
                thread = $"{(MainThread.IsMainThread ? "main thread" : $"thread {Environment.CurrentManagedThreadId}")}, window dispatcher required {c.Window.Dispatcher.IsDispatchRequired}, app dispatcher required {app.Dispatcher.IsDispatchRequired}";
            }
            app.RequestedThemeChanged += Normal;
            var frames = new List<string>();
            var plainBefore = LookPlatform.TextColor(plain);
            int settledFrame = -1;
            FrameWatch? watch = null;
            var acting = false;
            var actionFrame = 0;
            watch = new FrameWatch(() =>
            {
                if (!acting || frames.Count >= 6) return;
                var n = watch!.Frames - actionFrame;
                var now = LookPlatform.TextColor(plain);
                if (settledFrame < 0 && now is { } p && plainBefore is { } b && p.Distance(b) > 0.05) settledFrame = n;
                frames.Add($"frame {n}: {LookPlatform.Appearance(plain)}");
            });
            using (watch)
            {
                actionFrame = watch.Frames;
                acting = true;
                app.UserAppTheme = AppTheme.Dark;
                log.Add($"event on the {thread}; appearance at the event: {appearanceAtEvent}; right after the set: {LookPlatform.Appearance(plain)}");
                await c.Settle(500);
            }
            app.RequestedThemeChanged -= Normal;
            foreach (var f in frames) log.Add(f);
            log.Add($"the platform's appearance (the uncolored label) settled at frame {settledFrame} (0 = before the first frame callback)");
            app.UserAppTheme = AppTheme.Light;
            await c.Settle(500);

            // A nested set while MAUI is applying the bindings: from bound 1's property-changed handler.
            log.Phase = "nested set";
            var nested = false;
            void OnColor(object? s, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName != "TextColor" || nested) return;
                nested = true;
                app.UserAppTheme = AppTheme.Light;
            }
            labels[1].PropertyChanged += OnColor;
            app.UserAppTheme = AppTheme.Dark;
            labels[1].PropertyChanged -= OnColor;
            await c.Settle(600);
            log.Add($"after a nested set from bound 1's handler: RequestedTheme {app.RequestedTheme}; {string.Join(", ", labels.Select((l, i) => $"bound {i} {LookPlatform.Describe(LookPlatform.TextColor(l))}"))}; plain {LookPlatform.Describe(LookPlatform.TextColor(plain))} ({LookPlatform.Appearance(plain)})");

            log.Phase = "after the divergence";
            var late = new MauiLabel { Text = "late" };
            late.SetAppThemeColor(MauiLabel.TextColorProperty, LightRed, DarkBlue);
            stack.Add(late);
            await NativeLoaded(c, late);
            log.Add($"a plain AppThemeBinding mounted after it: {LookPlatform.Describe(LookPlatform.TextColor(late))}");

            // A Reactor root created after the divergence (the renderer's first root, in an app).
            c.Show(VStack(
                Label("bound").Set<LabelElement, MauiLabel>(l => l.SetAppThemeColor(MauiLabel.TextColorProperty, LightRed, DarkBlue)).AutomationId("late-bound"),
                Component<ThemeProbe>()), DirectHandlers.CreateRegistry());
            var lateBound = await Loaded<ILabel>(c, "late-bound");
            var probe = c.ById<ILabel>("look-probe");
            await c.WaitFor(() => probe.Handler is not null, "the probe");
            await c.Settle(300);
            log.Add($"a Reactor root mounted after it: Controls label with an AppThemeBinding {LookPlatform.Describe(LookPlatform.TextColor(lateBound))}, UseAppTheme probe '{Platform.Text(probe)}' {LookPlatform.Describe(LookPlatform.TextColor(probe))}; platform {LookPlatform.Appearance(lateBound)}");
            c.Unmount();
        }
        finally
        {
            app.UserAppTheme = AppTheme.Unspecified;
            app.UserAppTheme = original;
            log.Dump("divergence, threads and settling");
        }
        await c.Settle(300);
    }
}
