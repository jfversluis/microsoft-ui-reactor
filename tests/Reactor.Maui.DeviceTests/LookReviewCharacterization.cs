using System.Diagnostics;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Maui;
using Microsoft.UI.Reactor.Maui.Direct;
using static Microsoft.UI.Reactor.Maui.Factories;
using static Reactor.Maui.DeviceTests.FixtureContext;
using MauiButton = Microsoft.Maui.Controls.Button;
using MauiEntry = Microsoft.Maui.Controls.Entry;
using MauiLabel = Microsoft.Maui.Controls.Label;

namespace Reactor.Maui.DeviceTests;

/// <summary>Platform readback for the review characterizations of spec 071 §2.1 (D5, D7, D11, D12).</summary>
public static class LookReviewPlatform
{
    /// <summary>Whether the platform view takes input: UIControl.Enabled and user interaction; Android's View.Enabled.</summary>
    public static string Enabled(IView view) => view.Handler?.PlatformView switch
    {
#if IOS || MACCATALYST
        UIKit.UIControl control => $"enabled {control.Enabled}, interaction {control.UserInteractionEnabled}",
        UIKit.UIView native => $"interaction {native.UserInteractionEnabled}",
#elif ANDROID
        Android.Views.View native => $"enabled {native.Enabled}",
#endif
        _ => "-",
    };

    /// <summary>An Entry's placeholder color as the platform draws it.</summary>
    public static Rgba? PlaceholderColor(IView view)
    {
        switch (view.Handler?.PlatformView)
        {
#if IOS || MACCATALYST
            case UIKit.UITextField field when field.AttributedPlaceholder is { Length: > 0 } placeholder:
                var attributes = placeholder.GetAttributes(0, out _);
                if (attributes[UIKit.UIStringAttributeKey.ForegroundColor] is UIKit.UIColor color)
                {
                    color.GetResolvedColor(field.TraitCollection).GetRGBA(out var r, out var g, out var b, out var a);
                    return new Rgba(r, g, b, a);
                }
                return null;
#elif ANDROID
            case Android.Widget.EditText edit:
                var argb = edit.CurrentHintTextColor;
                return new Rgba(((argb >> 16) & 0xFF) / 255.0, ((argb >> 8) & 0xFF) / 255.0, (argb & 0xFF) / 255.0, ((argb >> 24) & 0xFF) / 255.0);
#endif
            default:
                return null;
        }
    }

#if ANDROID
    private static string Hex(int argb) =>
        $"#{(argb >> 16) & 0xFF:X2}{(argb >> 8) & 0xFF:X2}{argb & 0xFF:X2}@{((argb >> 24) & 0xFF) / 255.0:0.00}";

    private static string Describe(Android.Content.Res.ColorStateList? list) => list is null
        ? "null"
        : $"{Hex(list.DefaultColor)} (disabled {Hex(list.GetColorForState([-Android.Resource.Attribute.StateEnabled], new Android.Graphics.Color(list.DefaultColor)))})";

    private static string Attribute(Android.Content.Context context, int attribute)
    {
        var value = new Android.Util.TypedValue();
        if (context.Theme?.ResolveAttribute(attribute, value, true) != true) return "unresolved";
        if (value.ResourceId != 0)
        {
            try { return Describe(AndroidX.Core.Content.ContextCompat.GetColorStateList(context, value.ResourceId)); }
            catch (Exception ex) { return $"resource {value.ResourceId:X} ({ex.GetType().Name})"; }
        }
        return value.Type is >= Android.Util.DataType.FirstColorInt and <= Android.Util.DataType.LastColorInt ? Hex(value.Data) : $"type {value.Type}";
    }

    /// <summary>
    /// What Android's defaults are now, read several ways without touching the views on screen: the existing
    /// views' cached color lists, fresh views on the existing context, the theme's attributes on that context,
    /// and fresh views on a context re-themed for the current configuration.
    /// </summary>
    public static IEnumerable<string> Defaults(Android.Views.View existingLabel, Android.Views.View? existingButton, Android.Views.View? existingEntry)
    {
        var context = existingLabel.Context!;
        var night = (context.Resources!.Configuration!.UiMode & Android.Content.Res.UiMode.NightMask) == Android.Content.Res.UiMode.NightYes;
        yield return $"existing context night {night}; existing label TextColors {Describe((existingLabel as Android.Widget.TextView)?.TextColors)}" +
            (existingButton is Android.Widget.Button b ? $"; existing button TextColors {Describe(b.TextColors)}, BackgroundTintList {Describe(b.BackgroundTintList)}" : "") +
            (existingEntry is Android.Widget.EditText e ? $"; existing entry HintTextColors {Describe(e.HintTextColors)}, TextColors {Describe(e.TextColors)}" : "");
        yield return $"fresh views on the existing context: TextView {Describe(new Android.Widget.TextView(context).TextColors)}, " +
            $"MaterialButton text {Describe(new Google.Android.Material.Button.MaterialButton(context).TextColors)} tint {Describe(new Google.Android.Material.Button.MaterialButton(context).BackgroundTintList)}, " +
            $"EditText hint {Describe(new Android.Widget.EditText(context).HintTextColors)}";
        var colorPrimary = context.Resources.GetIdentifier("colorPrimary", "attr", context.PackageName);
        yield return $"existing context's theme attributes: textColorPrimary {Attribute(context, Android.Resource.Attribute.TextColorPrimary)}, " +
            $"textColorSecondary {Attribute(context, Android.Resource.Attribute.TextColorSecondary)}, textColorHint {Attribute(context, Android.Resource.Attribute.TextColorHint)}, " +
            $"colorPrimary {(colorPrimary == 0 ? "no id" : Attribute(context, colorPrimary))}";
        var themeId = context.Resources.GetIdentifier("Maui.MainTheme.NoActionBar", "style", context.PackageName);
        var configured = context.CreateConfigurationContext(context.Resources.Configuration)!;
        var themed = new AndroidX.AppCompat.View.ContextThemeWrapper(configured, themeId);
        yield return $"fresh views on a context re-themed for the current configuration (theme {themeId:X}): TextView {Describe(new Android.Widget.TextView(themed).TextColors)}, " +
            $"MaterialButton text {Describe(new Google.Android.Material.Button.MaterialButton(themed).TextColors)} tint {Describe(new Google.Android.Material.Button.MaterialButton(themed).BackgroundTintList)}, " +
            $"EditText hint {Describe(new Android.Widget.EditText(themed).HintTextColors)}; theme attributes: textColorPrimary {Attribute(themed, Android.Resource.Attribute.TextColorPrimary)}, " +
            $"textColorSecondary {Attribute(themed, Android.Resource.Attribute.TextColorSecondary)}";
    }
#endif
}

// Spec 071 §2.1, the review round of bc46b5ea: re-entrant theme sets (D5), the Disabled state's precedence and
// inherited IsEnabled (D7), Android's defaults after a theme change (D11), and forced and system themes (D12).
// On demand, like the rest of the look characterizations.
public static partial class Fixtures
{
    private static IEnumerable<Fixture> LookReviewCharacterizationFixtures() =>
    [
        new("Characterize_Look_Reentrancy", LookReentrancy, 60, OnDemand: true),
        new("Characterize_Look_Disabled_Plain", LookDisabledPlain, 60, OnDemand: true),
        .. Renderers.Select(r => new Fixture($"Characterize_Look_Disabled_Reactor [{r.Name}]", c => LookDisabledReactor(c, r.Create()), 60, OnDemand: true)),
        .. Renderers.Select(r => new Fixture($"Characterize_Look_Defaults [{r.Name}]", c => LookDefaults(c, r.Create(), system: false), 90, OnDemand: true)),
        new("Characterize_Look_Defaults_System [direct]", c => LookDefaults(c, DirectHandlers.CreateRegistry(), system: true), 120, OnDemand: true),
        new("Characterize_Look_ForcedTheme", c => LookForcedTheme(c, stale: false), 180, OnDemand: true),
        new("Characterize_Look_ForcedTheme_SystemChangedWhileForced", c => LookForcedTheme(c, stale: true), 180, OnDemand: true),
    ];

    /// <summary>
    /// A theme set from inside the theme change (MAUI suppresses the nested change while <c>_themeChangedFiring</c>):
    /// from a subscriber before and after a stand-in for Reactor's, and from a property-changed handler that runs
    /// while MAUI applies its bindings (before the event). What each subscriber sees (the event's argument and
    /// <c>RequestedTheme</c>), what MAUI's bindings and the platform show afterwards, and how it recovers.
    /// </summary>
    private static async Task LookReentrancy(FixtureContext c)
    {
        var app = Application.Current!;
        var original = app.UserAppTheme;
        var log = new LookLog();
        try
        {
            var bound = new MauiLabel { Text = "bound" };
            bound.SetAppThemeColor(MauiLabel.TextColorProperty, LightRed, DarkBlue);
            var plain = new MauiLabel { Text = "plain" };
            c.ShowNative(new VerticalStackLayout { Children = { bound, plain } });
            await NativeLoaded(c, plain);
            string State() => $"RequestedTheme {app.RequestedTheme}, UserAppTheme {app.UserAppTheme}, bound {LookPlatform.Describe(LookPlatform.TextColor(bound))}, plain {LookPlatform.Describe(LookPlatform.TextColor(plain))} ({LookPlatform.Appearance(plain)})";

            foreach (var where in new[] { "before", "after", "binding", "none" })
            {
                app.UserAppTheme = AppTheme.Light;
                await c.Settle(400);
                log.Phase = $"nested set {where}";
                var events = 0;
                var nestedDone = false;
                void Nested(string by)
                {
                    if (nestedDone) return;
                    nestedDone = true;
                    log.Add($"{by} sets UserAppTheme = Light inside the change to Dark");
                    app.UserAppTheme = AppTheme.Light;
                    log.Add($"the nested set returned: {State()}");
                }
                void Before(object? s, AppThemeChangedEventArgs e) { events++; log.Add($"subscriber before: arg {e.RequestedTheme}, {State()}"); if (where == "before") Nested("subscriber before"); }
                void Reactor(object? s, AppThemeChangedEventArgs e) => log.Add($"stand-in for Reactor: arg {e.RequestedTheme}, {State()}");
                void After(object? s, AppThemeChangedEventArgs e) { log.Add($"subscriber after: arg {e.RequestedTheme}, {State()}"); if (where == "after") Nested("subscriber after"); }
                void OnBinding(object? s, System.ComponentModel.PropertyChangedEventArgs e)
                {
                    if (e.PropertyName == "TextColor" && where == "binding") Nested("a TextColor property-changed handler (during MAUI's binding pass)");
                }
                app.RequestedThemeChanged += Before;
                app.RequestedThemeChanged += Reactor;
                app.RequestedThemeChanged += After;
                bound.PropertyChanged += OnBinding;
                try
                {
                    app.UserAppTheme = AppTheme.Dark;
                    log.Add($"the outer set returned, {events} event(s): {State()}");
                    await c.Drain(2);
                    await c.Settle(600);
                    log.Add($"settled, {events} event(s): {State()}");
                    app.UserAppTheme = AppTheme.Light;
                    await c.Settle(400);
                    log.Add($"recovery, UserAppTheme = Light again, {events} event(s): {State()}");
                    app.UserAppTheme = AppTheme.Unspecified;
                    await c.Settle(400);
                    log.Add($"recovery, UserAppTheme = Unspecified, {events} event(s): {State()}");
                }
                finally
                {
                    app.RequestedThemeChanged -= Before;
                    app.RequestedThemeChanged -= Reactor;
                    app.RequestedThemeChanged -= After;
                    bound.PropertyChanged -= OnBinding;
                }
            }
        }
        finally
        {
            app.UserAppTheme = original;
            log.Dump("re-entrant theme sets");
        }
        await c.Settle(300);
    }

    private static Style DisabledButtonStyle(bool isImplicit)
    {
        var style = new Style(typeof(MauiButton)) { CanCascade = isImplicit };
        style.Setters.Add(new Setter { Property = MauiButton.BackgroundColorProperty, Value = Microsoft.Maui.Graphics.Colors.Teal });
        style.Setters.Add(new Setter { Property = MauiButton.TextColorProperty, Value = Microsoft.Maui.Graphics.Colors.White });
        var groups = new VisualStateGroupList();
        var common = new VisualStateGroup { Name = "CommonStates" };
        common.States.Add(new VisualState { Name = "Normal" });
        var disabled = new VisualState { Name = "Disabled" };
        disabled.Setters.Add(new Setter { Property = MauiButton.BackgroundColorProperty, Value = Microsoft.Maui.Graphics.Colors.Gray });
        disabled.Setters.Add(new Setter { Property = MauiButton.TextColorProperty, Value = Microsoft.Maui.Graphics.Colors.Yellow });
        common.States.Add(disabled);
        groups.Add(common);
        style.Setters.Add(new Setter { Property = VisualStateManager.VisualStateGroupsProperty, Value = groups });
        return style;
    }

    private static string DescribeButton(string name, IView view)
    {
        var shot = view.Handler is null ? null : Snapshot.Capture(view);
        var state = view is VisualElement ve ? VisualStateManager.GetVisualStateGroups(ve)?.FirstOrDefault()?.CurrentState?.Name ?? "no groups" : "not VSM";
        var maui = view is VisualElement e ? $"maui IsEnabled {e.IsEnabled}, " : "";
        return $"{name} ({view.GetType().Name}): {maui}state {state}, native {LookReviewPlatform.Enabled(view)}, background px {LookPlatform.Describe(shot?.At(0.1, 0.5))}, text {LookPlatform.Describe(LookPlatform.TextColor(view))}";
    }

    /// <summary>
    /// Plain MAUI: a Disabled visual state against a local value (implicit style, explicit style, local groups), and
    /// IsEnabled inherited from a parent layout, including an explicit IsEnabled = true on the child.
    /// </summary>
    private static async Task LookDisabledPlain(FixtureContext c)
    {
        var log = new LookLog();
        try
        {
            var implicitHost = new VerticalStackLayout();
            implicitHost.Resources.Add(DisabledButtonStyle(isImplicit: true));
            var implicitLocal = new MauiButton { Text = "implicit + local red", BackgroundColor = Microsoft.Maui.Graphics.Colors.Red, TextColor = Microsoft.Maui.Graphics.Colors.Black };
            var implicitPlain = new MauiButton { Text = "implicit" };
            implicitHost.Add(implicitLocal);
            implicitHost.Add(implicitPlain);
            var explicitLocal = new MauiButton { Text = "explicit + local red", Style = DisabledButtonStyle(isImplicit: false), BackgroundColor = Microsoft.Maui.Graphics.Colors.Red, TextColor = Microsoft.Maui.Graphics.Colors.Black };
            var localGroups = new MauiButton { Text = "local groups + local red", BackgroundColor = Microsoft.Maui.Graphics.Colors.Red, TextColor = Microsoft.Maui.Graphics.Colors.Black };
            VisualStateManager.SetVisualStateGroups(localGroups, (VisualStateGroupList)DisabledButtonStyle(isImplicit: false).Setters.Single(s => s.Property == VisualStateManager.VisualStateGroupsProperty).Value);
            var parent = new VerticalStackLayout();
            parent.Resources.Add(DisabledButtonStyle(isImplicit: true));
            var child = new MauiButton { Text = "child" };
            parent.Add(child);
            var root = new VerticalStackLayout { Spacing = 4, Children = { implicitHost, explicitLocal, localGroups, parent } };
            c.ShowNative(root);
            await NativeLoaded(c, child);
            (string, IView)[] views = [("implicit+local", implicitLocal), ("implicit", implicitPlain), ("explicit+local", explicitLocal), ("local groups+local", localGroups), ("child", child)];
            void Log(string when) { foreach (var (n, v) in views) log.Add($"{when}: {DescribeButton(n, v)}"); }
            Log("enabled");

            log.Phase = "own disabled";
            foreach (var (_, v) in views.Take(4)) ((VisualElement)v).IsEnabled = false;
            await c.Settle(300);
            Log("IsEnabled = false");
            foreach (var (_, v) in views.Take(4)) ((VisualElement)v).IsEnabled = true;
            await c.Settle(300);
            Log("IsEnabled = true again");

            log.Phase = "inherited";
            parent.IsEnabled = false;
            await c.Settle(300);
            log.Add($"parent disabled: {DescribeButton("child", child)}");
            child.IsEnabled = true;
            await c.Settle(300);
            log.Add($"parent disabled, child IsEnabled = true set explicitly: {DescribeButton("child", child)}");
            parent.IsEnabled = true;
            await c.Settle(300);
            log.Add($"parent re-enabled: {DescribeButton("child", child)}");
            Check(true, "measured");
        }
        finally
        {
            log.Dump("the Disabled state in plain MAUI");
        }
    }

    /// <summary>
    /// Through Reactor, with an app-level implicit Button style that has a Disabled state: a declared background on a
    /// disabled button, and a disabled parent layout above a button and an anchored Controls button.
    /// </summary>
    private static async Task LookDisabledReactor(FixtureContext c, ElementHandlerRegistry registry)
    {
        var app = Application.Current!;
        var log = new LookLog();
        app.Resources.Add(DisabledButtonStyle(isImplicit: true));
        try
        {
            Element Tree(bool parentEnabled) => VStack(4,
                Button("own disabled, declared red").BackgroundColor(Microsoft.Maui.Graphics.Colors.Red).IsEnabled(false).AutomationId("look-own"),
                VStack(
                    Button("child").AutomationId("look-child"),
                    Button("anchored child").OnTap(() => { }).AutomationId("look-anchored-child"),
                    Button("child, IsEnabled true").IsEnabled(true).AutomationId("look-explicit-child")).IsEnabled(parentEnabled).AutomationId("look-parent"));
            c.Show(Tree(parentEnabled: true), registry);
            var own = await Loaded<IButton>(c, "look-own");
            var child = c.ById<IButton>("look-child");
            var anchored = c.ById<IButton>("look-anchored-child");
            var explicitChild = c.ById<IButton>("look-explicit-child");
            var parent = c.ById<IView>("look-parent");
            await c.WaitFor(() => explicitChild.Handler is not null, "the buttons to have handlers");
            await c.Settle(300);
            (string, IView)[] views = [("own", own), ("child", child), ("anchored child", anchored), ("explicit child", explicitChild)];
            void Log(string when)
            {
                log.Add($"{when}: parent {parent.GetType().Name} native {LookReviewPlatform.Enabled(parent)}");
                foreach (var (n, v) in views) log.Add($"{when}: {DescribeButton(n, v)}");
            }
            Log("parent enabled");
            c.Render(Tree(parentEnabled: false));
            await c.Drain();
            await c.Settle(300);
            Log("parent disabled");
            c.Render(Tree(parentEnabled: true));
            await c.Drain();
            await c.Settle(300);
            Log("parent re-enabled");
        }
        finally
        {
            app.Resources.Remove(typeof(MauiButton).FullName!);
            log.Dump("the Disabled state through Reactor");
            c.Unmount();
        }
        await c.Settle(300);
    }

    /// <summary>
    /// Android's defaults after a theme change (D11), on every platform for reference: views without a color, and
    /// views whose color Reactor clears after the change (MAUI restores the color list it cached at the first
    /// write), for a label's text, a button's text and background, and an entry's placeholder. On Android, the
    /// candidate native resolvers: fresh views and theme attributes on the existing context and on a context
    /// re-themed for the new configuration. <paramref name="system"/>: the host flips the system appearance instead.
    /// </summary>
    private static async Task LookDefaults(FixtureContext c, ElementHandlerRegistry registry, bool system)
    {
        var app = Application.Current!;
        var original = app.UserAppTheme;
        var log = new LookLog();
        try
        {
            app.UserAppTheme = system ? AppTheme.Unspecified : AppTheme.Light;
            await c.Settle(300);
            var start = app.RequestedTheme;
            Element Tree(bool colored) => VStack(4,
                Label("plain").AutomationId("look-plain"),
                colored ? Label("cleared").TextColor(Microsoft.Maui.Graphics.Colors.Red).AutomationId("look-cleared") : Label("cleared").AutomationId("look-cleared"),
                Button("button").AutomationId("look-button"),
                colored ? Button("cleared button").TextColor(Microsoft.Maui.Graphics.Colors.Red).BackgroundColor(Microsoft.Maui.Graphics.Colors.Yellow).AutomationId("look-cleared-button")
                    : Button("cleared button").AutomationId("look-cleared-button"),
                Button("disabled").IsEnabled(false).AutomationId("look-disabled"),
                colored ? Entry("", _ => { }, placeholder: "cleared hint") with { PlaceholderColor = Microsoft.Maui.Graphics.Colors.Red } : Entry("", _ => { }, placeholder: "cleared hint"));
            c.Show(Tree(colored: true), registry);
            var plain = await Loaded<ILabel>(c, "look-plain");
            var cleared = c.ById<ILabel>("look-cleared");
            var button = c.ById<IButton>("look-button");
            var clearedButton = c.ById<IButton>("look-cleared-button");
            var disabled = c.ById<IButton>("look-disabled");
            var entry = c.FindAll<IEntry>().First();
            await c.WaitFor(() => entry.Handler is not null && disabled.Handler is not null, "the views to have handlers");
            await c.Settle(300);
            string State() =>
                $"plain {LookPlatform.Describe(LookPlatform.TextColor(plain))}, cleared {LookPlatform.Describe(LookPlatform.TextColor(cleared))}, " +
                $"button text {LookPlatform.Describe(LookPlatform.TextColor(button))} bg px {LookPlatform.Describe(Snapshot.Capture(button).At(0.1, 0.5))}, " +
                $"cleared button text {LookPlatform.Describe(LookPlatform.TextColor(clearedButton))} bg px {LookPlatform.Describe(Snapshot.Capture(clearedButton).At(0.1, 0.5))}, " +
                $"disabled text {LookPlatform.Describe(LookPlatform.TextColor(disabled))} bg px {LookPlatform.Describe(Snapshot.Capture(disabled).At(0.1, 0.5))}, " +
                $"entry hint {LookPlatform.Describe(LookReviewPlatform.PlaceholderColor(entry))} ({LookPlatform.Appearance(plain)})";
            void Natives(string when)
            {
#if ANDROID
                foreach (var line in LookReviewPlatform.Defaults((Android.Views.View)plain.Handler!.PlatformView!, button.Handler?.PlatformView as Android.Views.View, entry.Handler?.PlatformView as Android.Views.View))
                    log.Add($"{when}: {line}");
#endif
            }
            log.Add($"mounted under {start}: {State()}");
            Natives("before");

            var target = Other(start);
            log.Phase = $"→ {target}";
            if (system)
            {
                Console.WriteLine($"REACTOR-LOOK-TOGGLE-SYSTEM {(target == AppTheme.Dark ? "dark" : "light")}");
                var clock = Stopwatch.StartNew();
                while (app.RequestedTheme != target && clock.ElapsedMilliseconds < 30000) await Task.Delay(16);
                if (app.RequestedTheme != target) throw new SkipException("no host flipped the system appearance (REACTOR-LOOK-TOGGLE-SYSTEM)");
            }
            else app.UserAppTheme = target;
            await c.Settle(600);
            log.Add($"after the change: {State()}");
            Natives("after");

            log.Phase = $"cleared under {target}";
            c.Render(Tree(colored: false));
            await c.Drain();
            await c.Settle(300);
            log.Add($"colors cleared by a render after the change: {State()}");

            if (system)
            {
                Console.WriteLine($"REACTOR-LOOK-TOGGLE-SYSTEM {(start == AppTheme.Dark ? "dark" : "light")}");
                var clock = Stopwatch.StartNew();
                while (app.RequestedTheme != start && clock.ElapsedMilliseconds < 30000) await Task.Delay(16);
                await c.Settle(600);
                log.Add($"system flipped back: {State()}");
            }
        }
        finally
        {
            app.UserAppTheme = original;
            log.Dump($"defaults after a {(system ? "system" : "UserAppTheme")} theme change");
            c.Unmount();
        }
        await c.Settle(300);
    }

    /// <summary>
    /// A forced user theme while the system changes (D12): the host sets the system dark; the app is Unspecified;
    /// the app forces Light; the host flips the system to light and back to dark; the app returns to Unspecified.
    /// What MAUI reports (events, PlatformAppTheme, RequestedTheme) and shows at each step. Ends with the system light.
    /// </summary>
    private static async Task LookForcedTheme(FixtureContext c, bool stale)
    {
        var app = Application.Current!;
        var original = app.UserAppTheme;
        var log = new LookLog();
        var events = new List<string>();
        void OnChanged(object? s, AppThemeChangedEventArgs e) => events.Add(e.RequestedTheme.ToString());
        app.RequestedThemeChanged += OnChanged;
        try
        {
            app.UserAppTheme = AppTheme.Unspecified;
            var bound = new MauiLabel { Text = "bound" };
            bound.SetAppThemeColor(MauiLabel.TextColorProperty, LightRed, DarkBlue);
            var plain = new MauiLabel { Text = "plain" };
            c.ShowNative(new VerticalStackLayout { Children = { bound, plain } });
            await NativeLoaded(c, plain);
            string State() => $"events [{string.Join(", ", events)}], PlatformAppTheme {app.PlatformAppTheme}, UserAppTheme {app.UserAppTheme}, RequestedTheme {app.RequestedTheme}, bound {LookPlatform.Describe(LookPlatform.TextColor(bound))}, plain {LookPlatform.Describe(LookPlatform.TextColor(plain))} ({LookPlatform.Appearance(plain)})";

            async Task<bool> FlipSystem(string target, AppTheme expect)
            {
                Console.WriteLine($"REACTOR-LOOK-TOGGLE-SYSTEM {target}");
                var clock = Stopwatch.StartNew();
                while (app.PlatformAppTheme != expect && clock.ElapsedMilliseconds < 8000) await Task.Delay(16);
                var seen = app.PlatformAppTheme == expect;
                await c.Settle(1500);
                return seen;
            }

            log.Phase = "1 system dark, app Unspecified";
            var first = await FlipSystem("dark", AppTheme.Dark);
            if (!first && app.PlatformAppTheme != AppTheme.Dark && events.Count == 0)
            {
                Console.WriteLine("REACTOR-LOOK-TOGGLE-SYSTEM light");
                throw new SkipException("no host flipped the system appearance (REACTOR-LOOK-TOGGLE-SYSTEM)");
            }
            log.Add($"PlatformAppTheme reached Dark: {first}; {State()}");
            log.Phase = "2 app forces Light";
            app.UserAppTheme = AppTheme.Light;
            await c.Settle(800);
            log.Add(State());
            log.Phase = "3 system light while forced";
            log.Add($"PlatformAppTheme reached Light: {await FlipSystem("light", AppTheme.Light)}; {State()}");
            if (!stale)
            {
                log.Phase = "4 system dark while forced";
                log.Add($"PlatformAppTheme reached Dark: {await FlipSystem("dark", AppTheme.Dark)}; {State()}");
            }
            // stale: the system stays light, which MAUI may not have observed while forced.
            log.Phase = stale ? "5 app back to Unspecified, the system light since step 3" : "5 app back to Unspecified";
            app.UserAppTheme = AppTheme.Unspecified;
            log.Add($"synchronously: {State()}");
            await c.Settle(1500);
            log.Add(State());
            await c.Settle(2000);
            log.Add($"2 s later: {State()}");
            log.Phase = "6 system light (restore)";
            log.Add($"PlatformAppTheme reached Light: {await FlipSystem("light", AppTheme.Light)}; {State()}");
        }
        finally
        {
            app.RequestedThemeChanged -= OnChanged;
            app.UserAppTheme = original;
            log.Dump("a forced user theme while the system changes");
        }
    }
}
