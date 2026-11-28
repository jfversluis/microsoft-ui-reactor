using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Maui;
using Microsoft.UI.Reactor.Maui.Direct;
using static Microsoft.UI.Reactor.Maui.Factories;
using static Reactor.Maui.DeviceTests.FixtureContext;
using MauiButton = Microsoft.Maui.Controls.Button;
using MauiLabel = Microsoft.Maui.Controls.Label;

namespace Reactor.Maui.DeviceTests;

/// <summary>Platform readback for the spec 071 characterizations: text colors, fonts, the platform's appearance.</summary>
public static class LookPlatform
{
    /// <summary>The color the platform draws a text view's text in, resolved for the view's own appearance.</summary>
    public static Rgba? TextColor(IView view)
    {
        switch (view.Handler?.PlatformView)
        {
#if IOS || MACCATALYST
            case UIKit.UILabel label:
                return Of(label.TextColor, label.TraitCollection);
            case UIKit.UIButton button:
                return Of(button.CurrentTitleColor, button.TraitCollection);
            case UIKit.UITextField field:
                return Of(field.TextColor, field.TraitCollection);
#elif ANDROID
            case Android.Widget.TextView text:
                return Of(text.CurrentTextColor);
#endif
            default:
                return null;
        }
    }

    public static string Describe(Rgba? color) => color is { } c ? $"#{(int)Math.Round(c.R * 255):X2}{(int)Math.Round(c.G * 255):X2}{(int)Math.Round(c.B * 255):X2}{(c.A < 0.999 ? $"@{c.A:0.00}" : "")}" : "-";

    /// <summary>The platform font a text view draws with, as the platform names it.</summary>
    public static string Font(IView view)
    {
        switch (view.Handler?.PlatformView)
        {
#if IOS || MACCATALYST
            case UIKit.UILabel label:
                return Describe(label.Font);
            case UIKit.UIButton button:
                return Describe(button.TitleLabel?.Font);
            case UIKit.UITextField field:
                return Describe(field.Font);
#elif ANDROID
            case Android.Widget.TextView text:
                return Describe(text.Typeface, text.TextSize / Density(text));
#endif
            default:
                return "-";
        }
    }

    /// <summary>The width, in points, the platform's text measurement gives <paramref name="sample"/> in the view's font.</summary>
    public static double TextWidth(IView view, string sample)
    {
        switch (view.Handler?.PlatformView)
        {
#if IOS || MACCATALYST
            case UIKit.UILabel label:
                return Width(label.Font, sample);
            case UIKit.UIButton button when button.TitleLabel?.Font is { } font:
                return Width(font, sample);
#elif ANDROID
            case Android.Widget.TextView text:
                return text.Paint!.MeasureText(sample) / Density(text);
#endif
            default:
                return double.NaN;
        }
    }

    /// <summary>Whether the platform shows the view in its dark appearance (iOS trait, Android night mode of the view's context).</summary>
    public static string Appearance(IView view)
    {
        switch (view.Handler?.PlatformView)
        {
#if IOS || MACCATALYST
            case UIKit.UIView native:
                return $"trait {native.TraitCollection.UserInterfaceStyle}, window override {native.Window?.OverrideUserInterfaceStyle}";
#elif ANDROID
            case Android.Views.View native:
                var mode = native.Context?.Resources?.Configuration?.UiMode & Android.Content.Res.UiMode.NightMask;
                var app = Android.App.Application.Context.Resources?.Configuration?.UiMode & Android.Content.Res.UiMode.NightMask;
                return $"view night {mode == Android.Content.Res.UiMode.NightYes}, application context night {app == Android.Content.Res.UiMode.NightYes}, AppCompat mode {AndroidX.AppCompat.App.AppCompatDelegate.DefaultNightMode}";
#endif
            default:
                return "-";
        }
    }

    /// <summary>MAUI's font manager's answer for a family, the way the stock handlers ask it.</summary>
    public static string ResolveFamily(string? family, double size = 17)
    {
        var services = IPlatformApplication.Current?.Services;
        var manager = services?.GetService<IFontManager>();
        var registrar = services?.GetService<IFontRegistrar>();
        var registered = family is null ? "n/a" : registrar?.GetFont(family) ?? "null";
        var font = Microsoft.Maui.Font.OfSize(family, size);
#if IOS || MACCATALYST
        var native = manager?.GetFont(font);
        return $"registrar '{registered}', manager {Describe(native)}";
#elif ANDROID
        var native = manager?.GetTypeface(font);
        return $"registrar '{registered}', manager {Describe(native, size)}";
#else
        return $"registrar '{registered}'";
#endif
    }

#if IOS || MACCATALYST
    private static Rgba? Of(UIKit.UIColor? color, UIKit.UITraitCollection traits)
    {
        if (color is null) return null;
        color.GetResolvedColor(traits).GetRGBA(out var r, out var g, out var b, out var a);
        return new Rgba(r, g, b, a);
    }

    private static string Describe(UIKit.UIFont? font) => font is null ? "-" : $"{font.Name} [{font.FamilyName}] {font.PointSize:0.#}pt";

    private static double Width(UIKit.UIFont font, string sample) =>
        new Foundation.NSAttributedString(sample, new UIKit.UIStringAttributes { Font = font }).Size.Width;
#elif ANDROID
    private static Rgba Of(int argb) =>
        new(((argb >> 16) & 0xFF) / 255.0, ((argb >> 8) & 0xFF) / 255.0, (argb & 0xFF) / 255.0, ((argb >> 24) & 0xFF) / 255.0);

    private static double Density(Android.Views.View view) => view.Context?.Resources?.DisplayMetrics?.Density ?? 1;

    private static string Describe(Android.Graphics.Typeface? typeface, double size)
    {
        if (typeface is null) return "-";
        var known = typeface.Equals(Android.Graphics.Typeface.Default) ? "Default"
            : typeface.Equals(Android.Graphics.Typeface.DefaultBold) ? "DefaultBold"
            : typeface.Equals(Android.Graphics.Typeface.SansSerif) ? "SansSerif"
            : typeface.Equals(Android.Graphics.Typeface.Serif) ? "Serif"
            : typeface.Equals(Android.Graphics.Typeface.Monospace) ? "Monospace"
            : $"typeface#{typeface.GetHashCode():X8}";
        var system = OperatingSystem.IsAndroidVersionAtLeast(34) ? typeface.SystemFontFamilyName ?? "none" : "n/a";
        return $"{known} (style {typeface.Style}, weight {(OperatingSystem.IsAndroidVersionAtLeast(28) ? typeface.Weight : -1)}, system family {system}) {size:0.#}sp";
    }
#endif
}

/// <summary>Captures what MAUI logs (a missing font, a binding failure), so the characterizations can show whether MAUI reports it.</summary>
public sealed class CapturedLogs : ILoggerProvider
{
    public static readonly CapturedLogs Instance = new();
    private readonly ConcurrentQueue<string> _lines = new();

    public static void Clear() => Instance._lines.Clear();

    public static IReadOnlyList<string> Lines => [.. Instance._lines];

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose() { }

    private sealed class Logger(CapturedLogs owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (owner._lines.Count > 500) return;
            owner._lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)}{(exception is null ? "" : $" ({exception.GetType().Name}: {exception.Message})")}");
        }
    }
}

// Spec 071 §2.1 (4d-1): characterizations of how MAUI applies themes, styles and fonts, on Controls views,
// on Reactor's two registries and on the anchored Controls views inside direct layouts, before the theme value
// and direct-view styling are designed. They log what they measure and assert only that the measurement ran
// (each oracle has a positive control). On demand: REACTOR_DEVICE_TEST_FILTER=Characterize_Look.
public static partial class Fixtures
{
    private static IEnumerable<Fixture> LookCharacterizationFixtures() =>
    [
        new("Characterize_Look_ThemeChange_Controls", LookThemeChangeControls, 60, OnDemand: true),
        .. Renderers.Select(r => new Fixture($"Characterize_Look_ThemeChange_Reactor [{r.Name}]", c => LookThemeChangeReactor(c, r.Create()), 60, OnDemand: true)),
        .. Renderers.Select(r => new Fixture($"Characterize_Look_ImplicitStyles [{r.Name}]", c => LookImplicitStyles(c, r.Create()), 60, OnDemand: true)),
        new("Characterize_Look_ThemeCost", LookThemeCost, 240, OnDemand: true),
        new("Characterize_Look_Fonts", LookFonts, 90, OnDemand: true),
        new("Characterize_Look_SystemTheme", LookSystemTheme, 120, OnDemand: true),
    ];

    private static readonly Color LightRed = Color.FromArgb("#FF0000");
    private static readonly Color DarkBlue = Color.FromArgb("#0000FF");

    /// <summary>A timeline: each line has the time, the phase, and what happened.</summary>
    private sealed class LookLog
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        public readonly List<string> Lines = [];
        public string Phase = "setup";

        public void Add(string what) => Lines.Add($"{_clock.Elapsed.TotalMilliseconds,8:0.0}ms [{Phase}] {what}");

        public void Dump(string title)
        {
            Note($"{title}:");
            foreach (var line in Lines) Note("    " + line);
            Lines.Clear();
        }
    }

    /// <summary>Runs a callback on every display frame (CADisplayLink, Choreographer) until disposed.</summary>
    private sealed class FrameWatch : IDisposable
    {
        private readonly Action _onFrame;
        private bool _stopped;
        public int Frames;

#if IOS || MACCATALYST
        private readonly CoreAnimation.CADisplayLink _link;

        public FrameWatch(Action onFrame)
        {
            _onFrame = onFrame;
            _link = CoreAnimation.CADisplayLink.Create(Tick);
            _link.AddToRunLoop(Foundation.NSRunLoop.Main, Foundation.NSRunLoopMode.Common);
        }

        public void Dispose()
        {
            _stopped = true;
            _link.Invalidate();
        }
#elif ANDROID
        public FrameWatch(Action onFrame)
        {
            _onFrame = onFrame;
            Android.Views.Choreographer.Instance!.PostFrameCallback(new Callback(this));
        }

        public void Dispose() => _stopped = true;

        private sealed class Callback(FrameWatch owner) : Java.Lang.Object, Android.Views.Choreographer.IFrameCallback
        {
            public void DoFrame(long frameTimeNanos)
            {
                if (owner._stopped) return;
                owner.Tick();
                Android.Views.Choreographer.Instance!.PostFrameCallback(this);
            }
        }
#else
        public FrameWatch(Action onFrame) => _onFrame = onFrame;

        public void Dispose() => _stopped = true;
#endif

        private void Tick()
        {
            if (_stopped) return;
            Frames++;
            _onFrame();
        }
    }

    // Every label and button mapping goes through these appended entries, so a fixture can count what a theme
    // change re-maps. Installed once; inert unless a fixture sets the callback.
    private static bool s_lookMappersHooked;
    private static Action<IElement, string>? s_lookMapped;

    private static void HookLookMappers()
    {
        if (s_lookMappersHooked) return;
        s_lookMappersHooked = true;
        foreach (var key in LabelHandler.Mapper.GetKeys().ToArray())
            LabelHandler.Mapper.AppendToMapping(key, (_, view) => s_lookMapped?.Invoke(view, key));
        foreach (var key in ButtonHandler.Mapper.GetKeys().ToArray())
            ButtonHandler.Mapper.AppendToMapping(key, (_, view) => s_lookMapped?.Invoke(view, key));
    }

    private static Style ThemedLabelStyle(Color light, Color dark, double? fontSize = null, string? fontFamily = null)
    {
        var style = new Style(typeof(MauiLabel));
        style.Setters.Add(new Setter { Property = MauiLabel.TextColorProperty, Value = new AppThemeBinding { Light = light, Dark = dark } });
        if (fontSize is { } size) style.Setters.Add(new Setter { Property = MauiLabel.FontSizeProperty, Value = size });
        if (fontFamily is not null) style.Setters.Add(new Setter { Property = MauiLabel.FontFamilyProperty, Value = fontFamily });
        return style;
    }

    private static string ColorSummary(IEnumerable<(string Name, IView View)> views) =>
        string.Join(", ", views.Select(v => $"{v.Name} {(v.View is MauiLabel l ? $"maui {l.TextColor?.ToArgbHex() ?? "null"} " : "")}native {LookPlatform.Describe(LookPlatform.TextColor(v.View))}"));

    private static AppTheme Other(AppTheme theme) => theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;

    /// <summary>
    /// Plain MAUI: when a theme change re-applies AppThemeBinding values (a binding on the view, an implicit style's
    /// setter, an explicit style's setter), relative to <c>RequestedThemeChanged</c>; what the handlers re-map; what a
    /// view without a color does; and a DynamicResource (not themed) for contrast.
    /// </summary>
    private static async Task LookThemeChangeControls(FixtureContext c)
    {
        HookLookMappers();
        var app = Application.Current!;
        var original = app.UserAppTheme;
        var log = new LookLog();
        var eventFired = false;
        try
        {
            var bound = new MauiLabel { Text = "bound" };
            bound.SetAppThemeColor(MauiLabel.TextColorProperty, LightRed, DarkBlue);
            var implicitHost = new VerticalStackLayout();
            implicitHost.Resources.Add(ThemedLabelStyle(Microsoft.Maui.Graphics.Colors.Green, Microsoft.Maui.Graphics.Colors.Orange, fontSize: 23));
            var implicitLabel = new MauiLabel { Text = "implicit" };
            implicitHost.Add(implicitLabel);
            var explicitLabel = new MauiLabel { Text = "explicit", Style = ThemedLabelStyle(Microsoft.Maui.Graphics.Colors.Purple, Microsoft.Maui.Graphics.Colors.Brown) };
            var plain = new MauiLabel { Text = "plain" };
            var dynamicLabel = new MauiLabel { Text = "dynamic" };
            var root = new VerticalStackLayout { Spacing = 4, Children = { bound, implicitHost, explicitLabel, plain, dynamicLabel } };
            root.Resources["LookAccent"] = Microsoft.Maui.Graphics.Colors.Teal;
            dynamicLabel.SetDynamicResource(MauiLabel.TextColorProperty, "LookAccent");
            var plainButton = new MauiButton { Text = "button" };
            root.Add(plainButton);
            (string, IView)[] views = [("bound", bound), ("implicit", implicitLabel), ("explicit", explicitLabel), ("plain", plain), ("dynamic", dynamicLabel), ("button", plainButton)];

            c.ShowNative(root);
            foreach (var (_, view) in views) await NativeLoaded(c, (View)view);
            log.Add($"loaded under {app.RequestedTheme} ({LookPlatform.Appearance(plain)}): {ColorSummary(views)}; implicit font size {implicitLabel.FontSize}");

            foreach (var (name, view) in views)
                ((BindableObject)view).PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is "TextColor" or "FontSize" or "Style" or "FontFamily" or "BackgroundColor")
                        log.Add($"{name}.{e.PropertyName} changed {(eventFired ? "after" : "before")} RequestedThemeChanged");
                };
            var mapped = new Dictionary<string, int>();
            s_lookMapped = (view, key) =>
            {
                var name = views.FirstOrDefault(v => ReferenceEquals(v.Item2, view)).Item1;
                if (name is null) return;
                mapped[$"{name}.{key}"] = mapped.GetValueOrDefault($"{name}.{key}") + 1;
            };
            void OnThemeChanged(object? sender, AppThemeChangedEventArgs e)
            {
                eventFired = true;
                log.Add($"RequestedThemeChanged({e.RequestedTheme}) runs; at that moment: {ColorSummary(views)}");
            }
            app.RequestedThemeChanged += OnThemeChanged;

            // Positive control for the mapper count: one property change maps that property once.
            log.Phase = "control";
            bound.Text = "bound!";
            log.Add($"positive control, a Text change mapped: {string.Join(", ", mapped.Select(m => $"{m.Key}×{m.Value}"))}");
            Check(mapped.GetValueOrDefault("bound.Text") == 1, "the mapper hook counts a Text change once");
            mapped.Clear();

            try
            {
                for (var round = 0; round < 3; round++)
                {
                    var target = round == 2 ? AppTheme.Unspecified : Other(app.RequestedTheme);
                    log.Phase = $"round {round} → {target}";
                    eventFired = false;
                    var clock = Stopwatch.StartNew();
                    app.UserAppTheme = target;
                    var setMs = clock.Elapsed.TotalMilliseconds;
                    log.Add($"UserAppTheme = {target} returned after {setMs:0.00} ms, event fired {eventFired}, RequestedTheme {app.RequestedTheme}; {ColorSummary(views)}");
                    log.Add($"re-mapped during the set: {(mapped.Count == 0 ? "nothing" : string.Join(", ", mapped.Select(m => $"{m.Key}×{m.Value}")))}");
                    mapped.Clear();
                    await c.Drain(1);
                    log.Add($"after one dispatch: {ColorSummary(views)}; re-mapped: {(mapped.Count == 0 ? "nothing" : string.Join(", ", mapped.Select(m => $"{m.Key}×{m.Value}")))}");
                    mapped.Clear();
                    await c.Settle(600);
                    log.Add($"settled ({LookPlatform.Appearance(plain)}): {ColorSummary(views)}; re-mapped late: {(mapped.Count == 0 ? "nothing" : string.Join(", ", mapped.Select(m => $"{m.Key}×{m.Value}")))}");
                    mapped.Clear();
                }
            }
            finally
            {
                app.RequestedThemeChanged -= OnThemeChanged;
                s_lookMapped = null;
            }
        }
        finally
        {
            app.UserAppTheme = original;
            log.Dump("theme change on plain Controls views");
        }
        await c.Settle(300);
    }

    private sealed class ThemeProbe : Component
    {
        public override Element Render()
        {
            var theme = UseAppTheme();
            return Label($"probe {theme}").TextColor(theme == AppTheme.Dark ? DarkBlue : LightRed).AutomationId("look-probe");
        }
    }

    private static readonly Style SharedThemedStyle = ThemedLabelStyle(Microsoft.Maui.Graphics.Colors.Purple, Microsoft.Maui.Graphics.Colors.Brown);

    /// <summary>
    /// Through Reactor, per registry: a label without a color, a Controls label styled with an AppThemeBinding
    /// (on the direct registry it is anchored: its Parent is the host, but it isn't a logical child), a Controls
    /// label with an AppThemeBinding set through <c>.Set</c>, and a <c>UseAppTheme</c> component. Frame by frame,
    /// which views show the new theme's color and when, relative to the theme set.
    /// </summary>
    private static async Task LookThemeChangeReactor(FixtureContext c, ElementHandlerRegistry registry)
    {
        var app = Application.Current!;
        var original = app.UserAppTheme;
        var log = new LookLog();
        try
        {
            c.Show(VStack(4,
                Label("plain").AutomationId("look-plain"),
                Label("styled").Style(SharedThemedStyle).AutomationId("look-styled"),
                Label("bound").Set<LabelElement, MauiLabel>(l => l.SetAppThemeColor(MauiLabel.TextColorProperty, LightRed, DarkBlue)).AutomationId("look-bound"),
                Component<ThemeProbe>()), registry);
            var plain = await Loaded<ILabel>(c, "look-plain");
            var styled = c.ById<ILabel>("look-styled");
            var bound = c.ById<ILabel>("look-bound");
            var probe = c.ById<ILabel>("look-probe");
            await c.WaitFor(() => probe.Handler is not null && styled.Handler is not null, "the labels to have handlers");
            (string, IView)[] views = [("plain", plain), ("styled", styled), ("bound", bound), ("probe", probe)];
            log.Add($"types: {string.Join(", ", views.Select(v => $"{v.Item1} {v.Item2.GetType().Name}"))}");
            log.Add($"styled parent {(styled as Microsoft.Maui.Controls.Element)?.Parent?.GetType().Name ?? "-"}, logical child of it: {((styled as Microsoft.Maui.Controls.Element)?.Parent as IElementController)?.LogicalChildren.Contains((Microsoft.Maui.Controls.Element)styled)}");
            log.Add($"mounted under {app.RequestedTheme}: {ColorSummary(views)}");

            for (var round = 0; round < 2; round++)
            {
                var target = Other(app.RequestedTheme);
                log.Phase = $"round {round} → {target}";
                var expected = Rgba.Of(target == AppTheme.Dark ? DarkBlue : LightRed);
                var firstNew = new Dictionary<string, int>();
                var plainBefore = LookPlatform.TextColor(plain);
                var watch = new FrameWatch(() => { });
                using (watch)
                {
                    void Sample(int frame)
                    {
                        foreach (var (name, view) in new[] { ("bound", (IView)bound), ("probe", probe) })
                            if (!firstNew.ContainsKey(name) && LookPlatform.TextColor(view) is { } color && color.Distance(expected) < 0.05)
                                firstNew[name] = frame;
                        // The platform's own appearance: a view without a color shows a system color that follows it.
                        if (!firstNew.ContainsKey("plain") && LookPlatform.TextColor(plain) is { } p && plainBefore is { } b && p.Distance(b) > 0.05)
                            firstNew["plain"] = frame;
                    }
                    using var sampler = new FrameWatch(() => Sample(watch.Frames));
                    var clock = Stopwatch.StartNew();
                    app.UserAppTheme = target;
                    log.Add($"set returned after {clock.Elapsed.TotalMilliseconds:0.00} ms; synchronously: {ColorSummary(views)}");
                    Sample(0);
                    await c.Drain(1);
                    log.Add($"after one dispatch ({watch.Frames} frames): {ColorSummary(views)}");
                    await c.Settle(600);
                    log.Add($"settled ({LookPlatform.Appearance(plain)}): {ColorSummary(views)}");
                    log.Add($"first frame showing the new color (0 = synchronously, before any frame): bound {(firstNew.TryGetValue("bound", out var fb) ? fb : -1)}, probe {(firstNew.TryGetValue("probe", out var fp) ? fp : -1)}, plain (the platform's appearance) {(firstNew.TryGetValue("plain", out var fa) ? fa : -1)}");
                    Check(firstNew.ContainsKey("bound") || firstNew.ContainsKey("probe"), "the frame sampler saw at least one view take the new color (positive control)");
                }
            }
        }
        finally
        {
            app.UserAppTheme = original;
            log.Dump("theme change through Reactor");
            c.Unmount();
        }
        await c.Settle(300);
    }

    /// <summary>
    /// Implicit styles in the application's resources (a Label and a Button style, with an AppThemeBinding, a font,
    /// a size, a corner radius and a Disabled visual state): which Reactor views take them on each registry, whether
    /// a theme change updates the anchored Controls view inside a direct layout, and what Reactor's clear restores.
    /// </summary>
    private static async Task LookImplicitStyles(FixtureContext c, ElementHandlerRegistry registry)
    {
        var app = Application.Current!;
        var original = app.UserAppTheme;
        var log = new LookLog();
        var labelStyle = ThemedLabelStyle(Microsoft.Maui.Graphics.Colors.Green, Microsoft.Maui.Graphics.Colors.Orange, fontSize: 23, fontFamily: "OpenSansSemibold");
        var buttonStyle = new Style(typeof(MauiButton));
        buttonStyle.Setters.Add(new Setter { Property = MauiButton.BackgroundColorProperty, Value = Microsoft.Maui.Graphics.Colors.Teal });
        buttonStyle.Setters.Add(new Setter { Property = MauiButton.TextColorProperty, Value = Microsoft.Maui.Graphics.Colors.White });
        buttonStyle.Setters.Add(new Setter { Property = MauiButton.CornerRadiusProperty, Value = 3 });
        var states = new VisualStateGroupList();
        var common = new VisualStateGroup { Name = "CommonStates" };
        common.States.Add(new VisualState { Name = "Normal" });
        var disabled = new VisualState { Name = "Disabled" };
        disabled.Setters.Add(new Setter { Property = MauiButton.BackgroundColorProperty, Value = Microsoft.Maui.Graphics.Colors.Gray });
        common.States.Add(disabled);
        states.Add(common);
        buttonStyle.Setters.Add(new Setter { Property = VisualStateManager.VisualStateGroupsProperty, Value = states });
        app.Resources.Add(labelStyle);
        app.Resources.Add(buttonStyle);
        try
        {
            static Element Tree(bool overrideColor) => VStack(4,
                Label("implicit").AutomationId("look-implicit"),
                overrideColor ? Label("override").TextColor(Microsoft.Maui.Graphics.Colors.Black).AutomationId("look-override") : Label("override").AutomationId("look-override"),
                Label("anchored").OnTap(() => { }).AutomationId("look-anchored"),
                Button("enabled").AutomationId("look-button"),
                Button("disabled").IsEnabled(false).AutomationId("look-disabled"));
            c.Show(Tree(overrideColor: true), registry);
            var implicitLabel = await Loaded<ILabel>(c, "look-implicit");
            var overrideLabel = c.ById<ILabel>("look-override");
            var anchored = c.ById<ILabel>("look-anchored");
            var button = c.ById<IButton>("look-button");
            var disabledButton = c.ById<IButton>("look-disabled");
            await c.WaitFor(() => disabledButton.Handler is not null && anchored.Handler is not null, "the views to have handlers");
            await c.Settle(300);
            (string, IView)[] views = [("implicit", implicitLabel), ("override", overrideLabel), ("anchored", anchored), ("button", button), ("disabled", disabledButton)];
            string Describe() => string.Join("; ", views.Select(v =>
            {
                var shot = v.Item2.Handler is null ? null : Snapshot.Capture(v.Item2);
                var bg = v.Item2 is IButton && shot is not null ? $" background px {LookPlatform.Describe(shot.At(0.04, 0.5))}" : "";
                return $"{v.Item1} ({v.Item2.GetType().Name}) text {LookPlatform.Describe(LookPlatform.TextColor(v.Item2))} font {LookPlatform.Font(v.Item2)}{bg}";
            }));
            log.Add($"mounted under {app.RequestedTheme}: {Describe()}");
            log.Add($"anchored parent {(anchored as Microsoft.Maui.Controls.Element)?.Parent?.GetType().Name ?? "-"}, logical child: {((anchored as Microsoft.Maui.Controls.Element)?.Parent as IElementController)?.LogicalChildren.Contains((Microsoft.Maui.Controls.Element)anchored)}");

            log.Phase = "clear";
            c.Render(Tree(overrideColor: false));
            await c.Drain();
            await c.Settle(200);
            log.Add($"override's TextColor removed by a render: override text {LookPlatform.Describe(LookPlatform.TextColor(overrideLabel))} (the style's light/dark green/orange, or the platform default)");

            for (var round = 0; round < 2; round++)
            {
                var target = Other(app.RequestedTheme);
                log.Phase = $"round {round} → {target}";
                app.UserAppTheme = target;
                await c.Drain(1);
                log.Add($"after one dispatch: {Describe()}");
                await c.Settle(600);
                log.Add($"settled: {Describe()}");
            }

            log.Phase = "style removed";
            app.Resources.Remove(typeof(MauiLabel).FullName!);
            app.Resources.Remove(typeof(MauiButton).FullName!);
            await c.Settle(300);
            log.Add($"implicit styles removed from the application: {Describe()}");
        }
        finally
        {
            app.Resources.Remove(typeof(MauiLabel).FullName!);
            app.Resources.Remove(typeof(MauiButton).FullName!);
            app.UserAppTheme = original;
            log.Dump("implicit styles on Reactor views");
            c.Unmount();
        }
        await c.Settle(300);
    }

    private sealed record CostProps(int Count);

    private sealed class ThemedRows : Component<CostProps>
    {
        public override Element Render()
        {
            var theme = UseAppTheme();
            var color = theme == AppTheme.Dark ? DarkBlue : LightRed;
            var rows = new Element?[Props.Count];
            for (var i = 0; i < rows.Length; i++) rows[i] = Label($"row {i}").TextColor(color).AutomationId(i == rows.Length - 1 ? "cost-last" : $"cost-{i}");
            return ScrollView(VStack(rows));
        }
    }

    private sealed class PlainRows : Component<CostProps>
    {
        public override Element Render()
        {
            var rows = new Element?[Props.Count];
            for (var i = 0; i < rows.Length; i++) rows[i] = Label($"row {i}").AutomationId(i == rows.Length - 1 ? "cost-last" : $"cost-{i}");
            return ScrollView(VStack(rows));
        }
    }

    /// <summary>
    /// The cost of a theme switch with 300 labels: plain MAUI with no themed values, with an AppThemeBinding on
    /// each, with an implicit themed style; Reactor re-rendering through <c>UseAppTheme</c> on each registry; and
    /// re-applying only the themed property without a render (what a theme value applied by the renderer would do)
    /// on each registry. Times: the synchronous set, and frames and milliseconds until the last label shows the new color.
    /// </summary>
    private static async Task LookThemeCost(FixtureContext c)
    {
        const int count = 300;
        var app = Application.Current!;
        var original = app.UserAppTheme;
        var results = new List<string>();

        async Task Measure(string name, Func<IView> last, Action<AppTheme>? reapply = null)
        {
            var syncs = new List<double>();
            var totals = new List<double>();
            var frames = new List<int>();
            for (var round = 0; round < 4; round++)
            {
                var target = Other(app.RequestedTheme);
                var expected = Rgba.Of(target == AppTheme.Dark ? DarkBlue : LightRed);
                var clock = Stopwatch.StartNew();
                double? done = null;
                var doneFrame = -1;
                FrameWatch? watch = null;
                watch = new FrameWatch(() =>
                {
                    if (done is null && LookPlatform.TextColor(last()) is { } col && col.Distance(expected) < 0.05)
                    {
                        done = clock.Elapsed.TotalMilliseconds;
                        doneFrame = watch!.Frames;
                    }
                });
                using (watch)
                {
                    app.UserAppTheme = target;
                    reapply?.Invoke(target);
                    syncs.Add(clock.Elapsed.TotalMilliseconds);
                    if (LookPlatform.TextColor(last()) is { } now && now.Distance(expected) < 0.05) (done, doneFrame) = (clock.Elapsed.TotalMilliseconds, 0);
                    var limit = Stopwatch.StartNew();
                    while (done is null && limit.ElapsedMilliseconds < 3000) await Task.Delay(16);
                }
                totals.Add(done ?? double.NaN);
                frames.Add(doneFrame);
                await c.Settle(300);
            }
            // The first switch warms caches; report the median of the other three.
            static double Median(IEnumerable<double> xs) { var s = xs.Skip(1).OrderBy(x => x).ToArray(); return s[s.Length / 2]; }
            results.Add($"{name}: set {Median(syncs):0.0} ms, new color on the last label after {Median(totals):0.0} ms at frame {frames.Skip(1).OrderBy(x => x).ElementAt(1)} (all rounds: sync [{string.Join(", ", syncs.Select(s => s.ToString("0.0")))}], total [{string.Join(", ", totals.Select(s => s.ToString("0.0")))}], frames [{string.Join(", ", frames)}])");
        }

        try
        {
            app.UserAppTheme = AppTheme.Light;
            // Plain MAUI, three shapes.
            foreach (var shape in new[] { "none", "binding", "implicit style" })
            {
                var stack = new VerticalStackLayout();
                if (shape == "implicit style") stack.Resources.Add(ThemedLabelStyle(LightRed, DarkBlue));
                MauiLabel? lastLabel = null;
                for (var i = 0; i < count; i++)
                {
                    var label = new MauiLabel { Text = $"row {i}" };
                    if (shape == "binding") label.SetAppThemeColor(MauiLabel.TextColorProperty, LightRed, DarkBlue);
                    if (shape == "none") label.TextColor = LightRed;
                    stack.Add(label);
                    lastLabel = label;
                }
                c.ShowNative(new ScrollView { Content = stack });
                await NativeLoaded(c, lastLabel!);
                await c.Settle(300);
                if (shape == "none")
                {
                    // No themed value: the switch itself, measured until the platform's appearance flips (an untimed baseline).
                    var labels = stack.Children.OfType<MauiLabel>().ToArray();
                    await Measure($"plain MAUI, {count} labels, no themed value, colors written by hand after the set", () => lastLabel!,
                        t => { foreach (var l in labels) l.TextColor = t == AppTheme.Dark ? DarkBlue : LightRed; });
                }
                else await Measure($"plain MAUI, {count} labels, {shape}", () => lastLabel!);
            }

            foreach (var (registryName, create) in Renderers)
            {
                c.Show(Component<ThemedRows, CostProps>(new(count)), create());
                var last = await Loaded<ILabel>(c, "cost-last");
                await c.Settle(300);
                await Measure($"Reactor [{registryName}], {count} labels, UseAppTheme re-render", () => last);
                c.Unmount();
                await c.Settle(200);

                // No render: the themed property written directly on each mounted view after the set.
                c.Show(Component<PlainRows, CostProps>(new(count)), create());
                var lastPlain = await Loaded<ILabel>(c, "cost-last");
                var all = c.FindAll<ILabel>(l => l.AutomationId?.StartsWith("cost-", StringComparison.Ordinal) == true).ToArray();
                Check(all.Length == count, $"found {all.Length} of {count} rows");
                await c.Settle(300);
                await Measure($"Reactor [{registryName}], {count} labels, themed color re-applied without a render", () => lastPlain, t =>
                {
                    var color = t == AppTheme.Dark ? DarkBlue : LightRed;
                    foreach (var label in all)
                    {
                        if (label is MauiLabel controls) controls.TextColor = color;
                        else if (label is DirectLabel direct) direct.Apply(new LabelElement(direct.Text) { TextColor = color });
                    }
                });
                c.Unmount();
                await c.Settle(200);
            }
        }
        finally
        {
            app.UserAppTheme = original;
            Note("theme switch costs (median of rounds 2-4):");
            foreach (var r in results) Note("    " + r);
        }
        await c.Settle(300);
    }

    /// <summary>
    /// Registered fonts (MauiProgram registers OpenSans-Regular.ttf as "OpenSansRegular" and OpenSans-Semibold.ttf as
    /// "OpenSansSemibold"): how each spelling of a family resolves on the platform, on a plain Controls label and on
    /// both registries; what a missing family does and whether MAUI reports it; bold and italic on a registered family.
    /// </summary>
    private static async Task LookFonts(FixtureContext c)
    {
        const string sample = "The quick brown fox 0123456789";
        string?[] families = [null, "OpenSansRegular", "OpenSans-Regular.ttf", "OpenSans-Regular", "Open Sans", "OpenSansSemibold", "NoSuchFont", "NoSuchFont.ttf"];
        var lines = new List<string>();
        CapturedLogs.Clear();
        // Positive control for the log capture: a line logged through the app's logger factory is captured.
        IPlatformApplication.Current?.Services.GetService<ILoggerFactory>()?.CreateLogger("ReactorLook").LogWarning("positive control: font capture");
        Check(CapturedLogs.Lines.Any(l => l.Contains("positive control: font capture", StringComparison.Ordinal)), "the log capture sees a warning logged through the app's logger factory");
        try
        {
            foreach (var family in families)
            {
                var clock = Stopwatch.StartNew();
                var resolved = LookPlatform.ResolveFamily(family);
                var first = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                LookPlatform.ResolveFamily(family);
                lines.Add($"font manager, '{family ?? "null"}': {resolved} (first {first:0.00} ms, again {clock.Elapsed.TotalMilliseconds:0.00} ms)");
            }

            // Plain Controls labels.
            var stack = new VerticalStackLayout();
            var labels = families.Select(f => new MauiLabel { Text = sample, FontFamily = f, FontSize = 17 }).ToList();
            var boldRegular = new MauiLabel { Text = sample, FontFamily = "OpenSansRegular", FontSize = 17, FontAttributes = FontAttributes.Bold };
            var italicRegular = new MauiLabel { Text = sample, FontFamily = "OpenSansRegular", FontSize = 17, FontAttributes = FontAttributes.Italic };
            foreach (var l in labels) stack.Add(l);
            stack.Add(boldRegular);
            stack.Add(italicRegular);
            c.ShowNative(stack);
            await NativeLoaded(c, italicRegular);
            var defaultWidth = LookPlatform.TextWidth(labels[0], sample);
            for (var i = 0; i < families.Length; i++)
                lines.Add($"Controls label '{families[i] ?? "null"}': {LookPlatform.Font(labels[i])}, width {LookPlatform.TextWidth(labels[i], sample):0.0} (default {defaultWidth:0.0})");
            lines.Add($"Controls label 'OpenSansRegular' Bold: {LookPlatform.Font(boldRegular)}, width {LookPlatform.TextWidth(boldRegular, sample):0.0}");
            lines.Add($"Controls label 'OpenSansRegular' Italic: {LookPlatform.Font(italicRegular)}, width {LookPlatform.TextWidth(italicRegular, sample):0.0}");
            Check(Math.Abs(LookPlatform.TextWidth(labels[1], sample) - defaultWidth) > 1, "a registered family measures differently from the default (positive control for the width oracle)");

            // Reactor, both registries.
            foreach (var (registryName, create) in Renderers)
            {
                var children = families.Select((f, i) => (Element?)(f is null ? Label(sample).FontSize(17) : Label(sample).FontSize(17).FontFamily(f)).AutomationId($"font-{i}")).ToList();
                children.Add(Label(sample).FontSize(17).FontFamily("OpenSansRegular").Bold().AutomationId("font-bold"));
                children.Add(Button(sample).FontSize(17).FontFamily("OpenSansSemibold").AutomationId("font-button"));
                c.Show(VStack([.. children]), create());
                await Loaded<IButton>(c, "font-button");
                await c.Settle(300);
                for (var i = 0; i < families.Length; i++)
                {
                    var view = c.ById<ILabel>($"font-{i}");
                    lines.Add($"Reactor [{registryName}] {view.GetType().Name} '{families[i] ?? "null"}': {LookPlatform.Font(view)}, width {LookPlatform.TextWidth(view, sample):0.0}");
                }
                var bold = c.ById<ILabel>("font-bold");
                lines.Add($"Reactor [{registryName}] {bold.GetType().Name} 'OpenSansRegular' Bold: {LookPlatform.Font(bold)}, width {LookPlatform.TextWidth(bold, sample):0.0}");
                var button = c.ById<IButton>("font-button");
                lines.Add($"Reactor [{registryName}] {button.GetType().Name} 'OpenSansSemibold': {LookPlatform.Font(button)}, width {LookPlatform.TextWidth(button, sample):0.0}");
                c.Unmount();
                await c.Settle(200);
            }
        }
        finally
        {
            Note("fonts:");
            foreach (var line in lines) Note("    " + line);
            var logged = CapturedLogs.Lines.Where(l => l.Contains("font", StringComparison.OrdinalIgnoreCase)).ToArray();
            Note($"MAUI's log lines mentioning fonts ({logged.Length}; {CapturedLogs.Lines.Count} lines captured in all):");
            foreach (var line in logged.Take(30)) Note("    " + line);
        }
    }

    /// <summary>
    /// A theme change the platform starts (the system appearance, with <c>UserAppTheme</c> unspecified), which an app
    /// can't cause itself: the fixture logs <c>REACTOR-LOOK-TOGGLE-SYSTEM</c> and the host flips the appearance
    /// (<c>xcrun simctl ui &lt;udid&gt; appearance</c>, <c>adb shell cmd uimode night</c>). Without a host it skips.
    /// Frame by frame, when MAUI's bindings, a <c>UseAppTheme</c> re-render and the platform's own colors change.
    /// </summary>
    private static async Task LookSystemTheme(FixtureContext c)
    {
        var app = Application.Current!;
        var original = app.UserAppTheme;
        var log = new LookLog();
        try
        {
            app.UserAppTheme = AppTheme.Unspecified;
            c.Show(VStack(4,
                Label("plain").AutomationId("look-plain"),
                Label("plain controls").OnTap(() => { }).AutomationId("look-plain-controls"),
                Label("bound").Set<LabelElement, MauiLabel>(l => l.SetAppThemeColor(MauiLabel.TextColorProperty, LightRed, DarkBlue)).AutomationId("look-bound"),
                Component<ThemeProbe>()), DirectHandlers.CreateRegistry());
            var plain = await Loaded<ILabel>(c, "look-plain");
            var plainControls = c.ById<ILabel>("look-plain-controls");
            var bound = c.ById<ILabel>("look-bound");
            var probe = c.ById<ILabel>("look-probe");
            await c.WaitFor(() => probe.Handler is not null && plainControls.Handler is not null, "the labels to have handlers");
            (string, IView)[] views = [("plain", plain), ("plain controls", plainControls), ("bound", bound), ("probe", probe)];
            log.Add($"mounted under {app.RequestedTheme} ({LookPlatform.Appearance(plain)}): {ColorSummary(views)}");

            for (var round = 0; round < 2; round++)
            {
                var from = app.RequestedTheme;
                log.Phase = $"round {round} from {from}";
                var before = views.ToDictionary(v => v.Item1, v => LookPlatform.TextColor(v.Item2));
                var firstNew = new Dictionary<string, int>();
                var changedAt = -1;
                FrameWatch? watch = null;
                void Sample()
                {
                    foreach (var (name, view) in views)
                        if (!firstNew.ContainsKey(name) && LookPlatform.TextColor(view) is { } now && before[name] is { } was && now.Distance(was) > 0.05)
                            firstNew[name] = watch?.Frames ?? 0;
                }
                void OnChanged(object? sender, AppThemeChangedEventArgs e)
                {
                    changedAt = watch?.Frames ?? 0;
                    log.Add($"RequestedThemeChanged({e.RequestedTheme}) at frame {changedAt} ({LookPlatform.Appearance(plain)}): {ColorSummary(views)}");
                    Sample();
                }
                app.RequestedThemeChanged += OnChanged;
                watch = new FrameWatch(Sample);
                try
                {
                    Console.WriteLine($"REACTOR-LOOK-TOGGLE-SYSTEM {(from == AppTheme.Dark ? "light" : "dark")}");
                    var clock = Stopwatch.StartNew();
                    while (app.RequestedTheme == from && clock.ElapsedMilliseconds < 30000) await Task.Delay(16);
                    if (app.RequestedTheme == from)
                    {
                        if (round == 0) throw new SkipException("no host flipped the system appearance (REACTOR-LOOK-TOGGLE-SYSTEM)");
                        throw new CheckFailedException("the host didn't flip the appearance back");
                    }
                    await c.Settle(800);
                    log.Add($"settled ({LookPlatform.Appearance(plain)}): {ColorSummary(views)}");
                    log.Add($"frames: event at {changedAt}; first frame each view showed another color (0 = before the first frame): {string.Join(", ", views.Select(v => $"{v.Item1} {(firstNew.TryGetValue(v.Item1, out var f) ? f : -1)}"))}");
                }
                finally
                {
                    app.RequestedThemeChanged -= OnChanged;
                    watch.Dispose();
                }
            }
        }
        finally
        {
            app.UserAppTheme = original;
            log.Dump("a theme change the platform starts");
            c.Unmount();
        }
    }
}
