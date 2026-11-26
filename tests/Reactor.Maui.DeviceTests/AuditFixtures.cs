using Microsoft.Maui.Graphics;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Maui;
using static Microsoft.UI.Reactor.Maui.Factories;
using static Reactor.Maui.DeviceTests.FixtureContext;

namespace Reactor.Maui.DeviceTests;

// Spec 070 §4.14 (4c-3b): the 🧪 rows on the platform.
public static partial class Fixtures
{
    private static IEnumerable<Fixture> AuditFixtures() =>
    [
        .. Renderers.SelectMany(r => new Fixture[]
        {
            new($"ActivityIndicator_Platform [{r.Name}]", c => ActivityIndicatorPlatform(c, r.Create())),
            new($"ProgressBar_Platform [{r.Name}]", c => ProgressBarPlatform(c, r.Create())),
            new($"DirectToggleColors [{r.Name}]", c => ToggleColors(c, r.Create())),
            new($"GraphicsView_Redraw [{r.Name}]", c => GraphicsViewRedraw(c, r.Create())),
            new($"FlexLayout_Geometry [{r.Name}]", c => FlexGeometry(c, r.Create())),
            new($"AbsoluteLayout_Geometry [{r.Name}]", c => AbsoluteGeometry(c, r.Create())),
            new($"FlyoutPage_IsPresented [{r.Name}]", c => FlyoutIsPresented(c, r.Create())),
            new($"FlyoutPage_Split [{r.Name}]", c => FlyoutSplit(c, r.Create())),
            new($"FlyoutPage_SplitRotated [{r.Name}]", c => FlyoutSplitRotated(c, r.Create())),
            new($"ToolbarItems_Platform [{r.Name}]", c => ToolbarPlatform(c, r.Create())),
        }),
    ];

    // ── Platform reads (the properties MAUI's mappers write; spec 070 §4.14.5) ──

    private static string Read(Rgba? color) => color is { } c ? c.ToString() : "none";

#if IOS || MACCATALYST
    private static Rgba? Rgb(UIKit.UIColor? color)
    {
        if (color is null) return null;
        color.GetRGBA(out var r, out var g, out var b, out var a);
        return new Rgba(r, g, b, a);
    }
#elif ANDROID
    private static Rgba Rgb(int argb) => new(((argb >> 16) & 0xFF) / 255.0, ((argb >> 8) & 0xFF) / 255.0, (argb & 0xFF) / 255.0, ((argb >> 24) & 0xFF) / 255.0);

    private static Rgba? FilterColor(Android.Graphics.Drawables.Drawable? drawable) =>
        drawable?.ColorFilter switch
        {
            null => null,
            Android.Graphics.BlendModeColorFilter blend when OperatingSystem.IsAndroidVersionAtLeast(29) => Rgb(blend.Color),
            var other => throw new CheckFailedException($"an unreadable color filter: {other.GetType().Name}"),
        };

    private static readonly int[] Checked = [Android.Resource.Attribute.StateChecked, Android.Resource.Attribute.StateEnabled];

    /// <summary>
    /// The average color a drawable draws (its visible pixels, alpha ignored): what a color filter does
    /// to it, where the drawable doesn't report its filter (MAUI's SetColorFilter on a spinner or a
    /// switch's track and thumb).
    /// </summary>
    private static Rgba? DrawnColor(Android.Graphics.Drawables.Drawable? drawable)
    {
        if (drawable is null) return null;
        const int Size = 96;
        using var bitmap = Android.Graphics.Bitmap.CreateBitmap(Size, Size, Android.Graphics.Bitmap.Config.Argb8888!)!;
        using var canvas = new Android.Graphics.Canvas(bitmap);
        var bounds = drawable.Bounds;
        drawable.SetBounds(0, 0, Size, Size);
        drawable.Draw(canvas);
        drawable.SetBounds(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        double r = 0, g = 0, b = 0;
        var count = 0;
        for (var y = 0; y < Size; y += 2)
            for (var x = 0; x < Size; x += 2)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (((pixel >> 24) & 0xFF) < 25) continue;
                r += ((pixel >> 16) & 0xFF) / 255.0;
                g += ((pixel >> 8) & 0xFF) / 255.0;
                b += (pixel & 0xFF) / 255.0;
                count++;
            }
        return count == 0 ? null : new Rgba(r / count, g / count, b / count, 1);
    }

    private static Rgba? TintColor(Android.Content.Res.ColorStateList? list) => list is null ? null : Rgb(list.GetColorForState(Checked, Android.Graphics.Color.Transparent));
#endif

    private static (bool Running, Rgba? Color) IndicatorState(IView view)
    {
        var native = Platform.View(view);
#if IOS || MACCATALYST
        var spinner = (UIKit.UIActivityIndicatorView)native!;
        return (spinner.IsAnimating && !spinner.Hidden, Rgb(spinner.Color));
#elif ANDROID
        var bar = (Android.Widget.ProgressBar)native!;
        // MAUI's filter is on the drawable container's current child (the animated vector).
        return (bar.Visibility == Android.Views.ViewStates.Visible, FilterColor(bar.IndeterminateDrawable?.Current));
#else
        throw new CheckFailedException("unsupported platform");
#endif
    }

    private static string IndicatorDiagnostics(IView view)
    {
#if ANDROID
        var bar = (Android.Widget.ProgressBar)Platform.View(view)!;
        var drawable = bar.IndeterminateDrawable;
        return $"; drawable {drawable?.GetType().Name} ({drawable?.Class.Name}), filter {drawable?.ColorFilter?.GetType().Name}, current {drawable?.Current?.Class.Name} filter {drawable?.Current?.ColorFilter?.GetType().Name}, " +
            $"tint {bar.IndeterminateTintList?.DefaultColor}, drawn {Read(DrawnColor(drawable))}, size {bar.Width}x{bar.Height}, snapshot {Read(Snapshot.Capture(view).Average())}";
#else
        return "";
#endif
    }

    private static (double Progress, Rgba? Color) ProgressState(IView view)
    {
        var native = Platform.View(view);
#if IOS || MACCATALYST
        var bar = (UIKit.UIProgressView)native!;
        return (bar.Progress, Rgb(bar.ProgressTintColor));
#elif ANDROID
        var bar = (Android.Widget.ProgressBar)native!;
        // The filled part as drawn (MAUI sets a tint list but clears a color filter).
        return (bar.Progress / 10000.0, Snapshot.Capture(view).At(0.1, 0.5));
#else
        throw new CheckFailedException("unsupported platform");
#endif
    }

    private static Rgba? CheckColor(IView view)
    {
        var native = Platform.View(view);
#if IOS || MACCATALYST
        return ((Microsoft.Maui.Platform.MauiCheckBox)native!).CheckBoxTintColor is { } color ? Rgba.Of(color) : null;
#elif ANDROID
        return TintColor(AndroidX.Core.Widget.CompoundButtonCompat.GetButtonTintList((Android.Widget.CompoundButton)native!));
#else
        throw new CheckFailedException("unsupported platform");
#endif
    }

    private static (Rgba? Track, Rgba? Thumb) SwitchColors(IView view)
    {
        var native = Platform.View(view);
#if IOS || MACCATALYST
        var toggle = (UIKit.UISwitch)native!;
        return (Rgb(toggle.OnTintColor), Rgb(toggle.ThumbTintColor));
#elif ANDROID
        return native switch
        {
            Google.Android.Material.MaterialSwitch.MaterialSwitch material => (TintColor(material.TrackTintList), TintColor(material.ThumbTintList)),
            AndroidX.AppCompat.Widget.SwitchCompat compat => (DrawnColor(compat.TrackDrawable), DrawnColor(compat.ThumbDrawable)),
            _ => throw new CheckFailedException($"an unexpected switch: {native?.GetType().Name}"),
        };
#else
        throw new CheckFailedException("unsupported platform");
#endif
    }

    private static void SameColor(Rgba? expected, Rgba? actual, string what) =>
        Check(expected is null ? actual is null : actual is { } a && a.Distance(expected.Value) <= 0.1, $"{what}: expected {Read(expected)}, read {Read(actual)}");

    // Drawn and averaged colors (Android) carry shading and anti-aliasing, so they get a wider tolerance.
    private static void Near(Color expected, Rgba? actual, string what) =>
        Check(actual is { } a && a.Distance(Rgba.Of(expected)) <= (OperatingSystem.IsAndroid() ? 0.15 : Snapshot.Tolerance), $"{what}: expected {Rgba.Of(expected)}, read {Read(actual)}");

    // ── ActivityIndicator and ProgressBar ───────────────────────────────────

    private static async Task ActivityIndicatorPlatform(FixtureContext c, ElementHandlerRegistry registry)
    {
        Element Tree(bool running, Color? color) => Stage(
            (ActivityIndicator(running) with { Color = color }).AutomationId("subject"),
            ActivityIndicator(true).AutomationId("reference"));
        c.Show(Tree(true, Colors.Red), registry);
        var subject = await Sized(c, "subject");
        var reference = await Sized(c, "reference");
        var native = Platform.View(subject);
        Console.WriteLine($"# activity indicator: {native?.GetType().Name}, mounted {IndicatorState(subject)}, a fresh one {IndicatorState(reference)}{IndicatorDiagnostics(subject)}");
        Check(IndicatorState(subject).Running, "running at mount");
        Near(Colors.Red, IndicatorState(subject).Color, "the color at mount");

        c.Render(Tree(false, Colors.Red));
        await c.WaitFor(() => !IndicatorState(subject).Running, "the indicator to stop");
        c.Render(Tree(true, Colors.Blue));
        await c.WaitFor(() => IndicatorState(subject).Running, "the indicator to run again");
        Check(ReferenceEquals(native, Platform.View(subject)), "the same platform view");
        Near(Colors.Blue, IndicatorState(subject).Color, "the updated color");

        c.Render(Tree(true, null));
        await c.Settle();
        SameColor(IndicatorState(reference).Color, IndicatorState(subject).Color, "the cleared color, as a fresh indicator's");
    }

    private static async Task ProgressBarPlatform(FixtureContext c, ElementHandlerRegistry registry)
    {
        Element Tree(double progress, Color? color) => Stage(
            ProgressBar(progress).ProgressColorOrNull(color).AutomationId("subject").Width(200),
            ProgressBar(0.5).AutomationId("reference").Width(200));
        c.Show(Tree(0.4, Colors.Red), registry);
        var subject = await Sized(c, "subject");
        var reference = await Sized(c, "reference");
        Console.WriteLine($"# progress bar: {Platform.View(subject)?.GetType().Name}, mounted {ProgressState(subject)}, a fresh one {ProgressState(reference)}");
        Check(Math.Abs(ProgressState(subject).Progress - 0.4) < 0.01, $"progress 0.4 at mount, read {ProgressState(subject).Progress}");
        Near(Colors.Red, ProgressState(subject).Color, "the color at mount");

        c.Render(Tree(0.7, Colors.Blue));
        await c.WaitFor(() => Math.Abs(ProgressState(subject).Progress - 0.7) < 0.01, "progress 0.7");
        Near(Colors.Blue, ProgressState(subject).Color, "the updated color");

        c.Render(Tree(0.7, null));
        await c.Settle();
        SameColor(ProgressState(reference).Color, ProgressState(subject).Color, "the cleared color, as a fresh bar's");
    }

    private static ProgressBarElement ProgressColorOrNull(this ProgressBarElement bar, Color? color) => bar with { ProgressColor = color };

    // ── CheckBox and Switch colors (the direct views remap them) ────────────

    private static async Task ToggleColors(FixtureContext c, ElementHandlerRegistry registry)
    {
        Element Tree(Color? check, Color? on, Color? thumb) => Stage(
            CheckBox(true).WithColor(check).AutomationId("check"),
            CheckBox(true).AutomationId("check-reference"),
            Switch(true).WithColors(on, thumb).AutomationId("toggle"),
            Switch(true).AutomationId("toggle-reference"));
        c.Show(Tree(Colors.Red, Colors.Red, Colors.Green), registry);
        var (check, checkReference) = (await Sized(c, "check"), await Sized(c, "check-reference"));
        var (toggle, toggleReference) = (await Sized(c, "toggle"), await Sized(c, "toggle-reference"));
        Console.WriteLine($"# toggles: {check.GetType().Name} on {Platform.View(check)?.GetType().Name} {Read(CheckColor(check))} (fresh {Read(CheckColor(checkReference))}); " +
            $"{toggle.GetType().Name} on {Platform.View(toggle)?.GetType().Name} {SwitchColors(toggle)} (fresh {SwitchColors(toggleReference)})");
        Near(Colors.Red, CheckColor(check), "the check box color");
        Near(Colors.Red, SwitchColors(toggle).Track, "the switch's on color");
        Near(Colors.Green, SwitchColors(toggle).Thumb, "the switch's thumb color");

        c.Render(Tree(Colors.Blue, Colors.Blue, Colors.Yellow));
        await c.Settle();
        Near(Colors.Blue, CheckColor(check), "the updated check box color");
        Near(Colors.Blue, SwitchColors(toggle).Track, "the updated on color");
        Near(Colors.Yellow, SwitchColors(toggle).Thumb, "the updated thumb color");

        c.Render(Tree(null, null, null));
        await c.Settle();
        SameColor(CheckColor(checkReference), CheckColor(check), "the cleared check box color, as a fresh one's");
        SameColor(SwitchColors(toggleReference).Track, SwitchColors(toggle).Track, "the cleared on color, as a fresh switch's");
        SameColor(SwitchColors(toggleReference).Thumb, SwitchColors(toggle).Thumb, "the cleared thumb color, as a fresh switch's");
    }

    private static CheckBoxElement WithColor(this CheckBoxElement box, Color? color) => box with { Color = color };

    private static SwitchElement WithColors(this SwitchElement toggle, Color? on, Color? thumb) => toggle with { OnColor = on, ThumbColor = thumb };

    // ── GraphicsView ────────────────────────────────────────────────────────

    private sealed class PaintingDrawable : IDrawable
    {
        public volatile int Draws;
        public Color Color = Colors.Red;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            Draws++;
            canvas.FillColor = Color;
            canvas.FillRectangle(dirtyRect);
        }
    }

    private static async Task GraphicsViewRedraw(FixtureContext c, ElementHandlerRegistry registry)
    {
        var drawable = new PaintingDrawable();
        Element Tree() => Stage(GraphicsView(drawable).Size(80, 60).HorizontalOptions(LayoutOptions.Start).AutomationId("canvas"));
        c.Show(Tree(), registry);
        var view = await Sized(c, "canvas");
        await c.WaitFor(() => drawable.Draws > 0, "the first draw");
        Snapshot.Capture(view).Expect(0.5, 0.5, Colors.Red, "the first drawing");

        // Nothing redraws by itself (the positive control for the redraw below).
        await c.Settle(500);
        var before = drawable.Draws;
        await c.Settle(500);
        Equal(before, drawable.Draws, "draws without a render");

        drawable.Color = Colors.Blue;
        c.Render(Tree());
        await c.WaitFor(() => drawable.Draws > before, "a redraw after the render");
        await c.Settle(200);
        Snapshot.Capture(view).Expect(0.5, 0.5, Colors.Blue, "the drawable's current color");
    }

    // ── FlexLayout and AbsoluteLayout ───────────────────────────────────────

    private static BoxViewElement Box(string id, double width = 40, double height = 20) =>
        BoxView(Colors.Gray).Size(width, height).AutomationId(id);

    private static async Task<Rect> FrameIn(FixtureContext c, string id, Rect container)
    {
        var frame = Platform.WindowFrame(await Sized(c, id));
        return new Rect(frame.X - container.X, frame.Y - container.Y, frame.Width, frame.Height);
    }

    private static void Near(double expected, double actual, string what) =>
        Check(Math.Abs(expected - actual) <= 1.5, $"{what}: expected {expected:0.#}, got {actual:0.#}");

    private static async Task FlexGeometry(FixtureContext c, ElementHandlerRegistry registry)
    {
        const double W = 300;
        c.Show(Stage(
            (FlexLayout(Box("a"), Box("b"), Box("c")) with
            {
                Direction = Microsoft.Maui.Layouts.FlexDirection.Row,
                JustifyContent = Microsoft.Maui.Layouts.FlexJustify.SpaceBetween,
                AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
            }).Size(W, 40).HorizontalOptions(LayoutOptions.Start).AutomationId("between"),
            // The grow child has no width of its own: a requested width would keep it 40 wide inside its slot.
            FlexLayout(Box("fixed"), BoxView(Colors.Gray).Height(20).AutomationId("grow").Flex(grow: 1)).Size(W, 20).HorizontalOptions(LayoutOptions.Start).AutomationId("grows"),
            (FlexLayout(Box("w1", 120), Box("w2", 120), Box("w3", 120)) with
            {
                Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
                AlignContent = Microsoft.Maui.Layouts.FlexAlignContent.Start,
            }).Size(W, 60).HorizontalOptions(LayoutOptions.Start).AutomationId("wraps"),
            FlexLayout(Label("one").AutomationId("d1"), Label("two").AutomationId("d2")).Size(W, 40).HorizontalOptions(LayoutOptions.Start).AutomationId("plain")), registry);

        var between = Platform.WindowFrame(await Sized(c, "between"));
        var (a, b, cc) = (await FrameIn(c, "a", between), await FrameIn(c, "b", between), await FrameIn(c, "c", between));
        Console.WriteLine($"# flex: between {between}; a {a}; b {b}; c {cc}");
        Near(0, a.X, "the first child at the start");
        Near(W, cc.Right, "the last child at the end");
        Near((W - 40) / 2, b.X, "the middle child centered between");
        Near(10, a.Y, "the child centered on the cross axis");

        var grows = Platform.WindowFrame(await Sized(c, "grows"));
        var grow = await FrameIn(c, "grow", grows);
        Near(40, grow.X, "the grow child after the fixed one");
        Near(W - 40, grow.Width, "the grow child filling the rest");

        var wraps = Platform.WindowFrame(await Sized(c, "wraps"));
        var (w2, w3) = (await FrameIn(c, "w2", wraps), await FrameIn(c, "w3", wraps));
        Near(0, w2.Y, "the second child on the first line");
        Near(0, w3.X, "the third child at the start of the next line");
        Near(20, w3.Y, "the third child on the second line");

        // Children without flex values are direct views on the direct registry, placed in order.
        // A Controls layout doesn't list direct children among its visual children, so they are read from it.
        var plainView = await Sized(c, "plain");
        var children = (IList<IView>)plainView;
        Console.WriteLine($"# flex plain children: {string.Join(", ", children.Select(v => v.GetType().Name))}");
        await c.WaitFor(() => children.Count == 2 && children.All(v => v.Handler?.PlatformView is not null && v.Frame.Width > 0), "the plain children laid out");
        var plain = Platform.WindowFrame(plainView);
        Rect Relative(IView view) { var f = Platform.WindowFrame(view); return new Rect(f.X - plain.X, f.Y - plain.Y, f.Width, f.Height); }
        var (d1, d2) = (Relative(children[0]), Relative(children[1]));
        Near(0, d1.X, "the first plain child at the start");
        Near(d1.Right, d2.X, "the second plain child after the first");
    }

    private static async Task AbsoluteGeometry(FixtureContext c, ElementHandlerRegistry registry)
    {
        const double W = 300, H = 200;
        c.Show(Stage(
            AbsoluteLayout(
                // No size of their own: the bounds size them (a requested size would center them inside).
                BoxView(Colors.Gray).AutomationId("fixed").Absolute(new Rect(10, 20, 50, 30)),
                BoxView(Colors.Gray).AutomationId("proportional").Absolute(new Rect(0.5, 0.5, 0.5, 0.5), Microsoft.Maui.Layouts.AbsoluteLayoutFlags.All),
                Box("plain", 30, 30)).Size(W, H).HorizontalOptions(LayoutOptions.Start).AutomationId("absolute")), registry);
        var layout = Platform.WindowFrame(await Sized(c, "absolute"));
        var (fixedFrame, proportional, plain) = (await FrameIn(c, "fixed", layout), await FrameIn(c, "proportional", layout), await FrameIn(c, "plain", layout));
        Console.WriteLine($"# absolute: layout {layout}; fixed {fixedFrame}; proportional {proportional}; plain {plain}");
        Near(10, fixedFrame.X, "the absolute x");
        Near(20, fixedFrame.Y, "the absolute y");
        Near(50, fixedFrame.Width, "the absolute width");
        Near(30, fixedFrame.Height, "the absolute height");
        Near(W / 2, proportional.Width, "the proportional width");
        Near(H / 2, proportional.Height, "the proportional height");
        Near(H / 4, proportional.Y, "the proportional y");
        Near(W / 4, proportional.X, "the proportional x");
        Near(0, plain.X, "a child without bounds at the origin");
    }

    // ── FlyoutPage ──────────────────────────────────────────────────────────

    private sealed record FlyoutProps(bool Accept, FlyoutLayoutBehavior? Behavior, List<bool> Reports);

    private sealed class FlyoutPageApp : Component<FlyoutProps>
    {
        public static Action<bool>? Set;

        public override Element Render()
        {
            var (presented, setPresented) = UseState(false);
            Set = setPresented;
            // The detail in a navigation stack: iOS puts the flyout's toggle in its bar.
            return Flyout(Page("Menu", Label("menu").AutomationId("flyout-content")), NavigationStack([Page("Detail", Label("detail"))]), presented, value =>
            {
                Props.Reports.Add(value);
                if (Props.Accept) setPresented(value);
            }) with { LayoutBehavior = Props.Behavior };
        }
    }

    private static bool Shown(FlyoutPage page) => FlyoutShown(page).StartsWith("yes", StringComparison.Ordinal);

    private static async Task FlyoutIsPresented(FixtureContext c, ElementHandlerRegistry registry)
    {
        // A popover flyout never splits, so this is the toggled path on every device (split mode is FlyoutPage_Split).
        foreach (var accept in new[] { true, false })
        {
            var reports = new List<bool>();
            var context = new FixtureContext(c.Window);
            context.Show(Component<FlyoutPageApp, FlyoutProps>(new(accept, FlyoutLayoutBehavior.Popover, reports)), registry);
            var page = await LoadedPage<FlyoutPage>(context);
            await context.Settle();
            Check(!Shown(page), $"closed at mount ({FlyoutShown(page)})");

            var log = new FlyoutLog();
            var opened = await TogglePlatform(context, page, log, open: true);
            await context.Settle(600);
            Equal("True", string.Join(",", reports), $"the user's opening reported once ({opened})");
            if (accept) Check(Shown(page) && page.IsPresented, $"accepted, it stays open ({FlyoutShown(page)})");
            else Check(!Shown(page) && !page.IsPresented, $"refused, it closes ({FlyoutShown(page)}, presented {page.IsPresented})");

            // A declared change: written, unreported.
            reports.Clear();
            FlyoutPageApp.Set!(!page.IsPresented);
            await context.Settle(600);
            Check(reports.Count == 0, $"a declared change reports nothing ({string.Join(",", reports)})");
            Check(Shown(page) == page.IsPresented, $"the flyout shows the declaration ({FlyoutShown(page)}, presented {page.IsPresented})");

            context.Unmount();
            if (page.IsPresented) await TogglePlatform(context, page, log, open: false);
            else await TogglePlatform(context, page, log, open: true);
            await context.Settle();
            Check(reports.Count == 0, "nothing after unmount");
            c.Pages.Set(new ContentPage());
            await c.Settle();
        }
    }

    private static async Task FlyoutSplit(FixtureContext c, ElementHandlerRegistry registry)
    {
        if (DeviceInfo.Idiom == DeviceIdiom.Phone) throw new SkipException("phones never split (FlyoutPage_IsPresented)");
        foreach (var behavior in new[] { FlyoutLayoutBehavior.Split, FlyoutLayoutBehavior.SplitOnPortrait })
        {
            var reports = new List<bool>();
            var context = new FixtureContext(c.Window);
            context.Show(Component<FlyoutPageApp, FlyoutProps>(new(false, behavior, reports)), registry);
            var page = await LoadedPage<FlyoutPage>(context);
            await context.Settle();
            Check(context.UnhandledErrors.Count == 0, $"{behavior}: no error at mount ({string.Join(" | ", context.UnhandledErrors.Select(e => e.Message))})");
            Check(((IFlyoutPageController)page).ShouldShowSplitMode, $"{behavior}: split (precondition)");
            Check(Shown(page), $"{behavior}: the flyout shown ({FlyoutShown(page)})");
            Check(reports.Count == 0, $"{behavior}: nothing reported at mount ({string.Join(",", reports)})");

            FlyoutPageApp.Set!(true);
            await context.Settle();
            FlyoutPageApp.Set!(false);
            await context.Settle();
            Check(context.UnhandledErrors.Count == 0, $"{behavior}: no error for declarations while split");
            Check(Shown(page), $"{behavior}: still shown");
            context.Unmount();
            c.Pages.Set(new ContentPage());
            await c.Settle();
        }

        // Leaving split by a behavior change, with each declaration.
        foreach (var declared in new[] { false, true })
        {
            var reports = new List<bool>();
            var context = new FixtureContext(c.Window);
            Element Tree(FlyoutLayoutBehavior behavior) =>
                Flyout(Page("Menu", Label("menu").AutomationId("flyout-content")), Page("Detail", Label("detail")), declared, reports.Add) with { LayoutBehavior = behavior };
            context.Show(Tree(FlyoutLayoutBehavior.Split), registry);
            var page = await LoadedPage<FlyoutPage>(context);
            await context.Settle();
            context.Render(Tree(FlyoutLayoutBehavior.Popover));
            await context.Settle(800);
            Check(!((IFlyoutPageController)page).ShouldShowSplitMode, "not split after the change (precondition)");
            Equal(declared, page.IsPresented, "IsPresented after leaving split");
            Equal(declared, Shown(page), $"the flyout shows the declaration after leaving split ({FlyoutShown(page)})");
            Check(reports.Count == 0, $"nothing reported ({string.Join(",", reports)})");
            Check(context.UnhandledErrors.Count == 0, "no error");
            context.Unmount();
            c.Pages.Set(new ContentPage());
            await c.Settle();
        }
    }

    private static async Task FlyoutSplitRotated(FixtureContext c, ElementHandlerRegistry registry)
    {
        RequireTablet();
        try
        {
            foreach (var behavior in new[] { FlyoutLayoutBehavior.Split, FlyoutLayoutBehavior.SplitOnLandscape })
            {
                await Rotate(c, landscape: false);
                var reports = new List<bool>();
                var context = new FixtureContext(c.Window);
                context.Show(Component<FlyoutPageApp, FlyoutProps>(new(false, behavior, reports)), registry);
                var page = await LoadedPage<FlyoutPage>(context);
                await context.Settle();
                var split = ((IFlyoutPageController)page).ShouldShowSplitMode;
                await Rotate(c, landscape: true);
                FlyoutPageApp.Set!(true);
                await context.Settle();
                FlyoutPageApp.Set!(false);
                await context.Settle();
                await Rotate(c, landscape: false);
                Console.WriteLine($"# flyout rotated {behavior}: split {split}, then {((IFlyoutPageController)page).ShouldShowSplitMode}, shown {FlyoutShown(page)}, reports [{string.Join(",", reports)}]");
                Check(context.UnhandledErrors.Count == 0, $"{behavior}: no error ({string.Join(" | ", context.UnhandledErrors.Select(e => e.Message))})");
                Check(reports.Count == 0, $"{behavior}: nothing reported");
                Equal(split, Shown(page), $"{behavior}: shown exactly when split");
                context.Unmount();
                c.Pages.Set(new ContentPage());
                await c.Settle();
            }
        }
        finally
        {
            await Rotate(c, landscape: false);
        }
    }

    // ── Toolbar items ───────────────────────────────────────────────────────

    private static async Task ToolbarPlatform(FixtureContext c, ElementHandlerRegistry registry)
    {
        var log = new List<string>();
        PageElement Screen(string tag, params string[] names) => Page("Toolbar", Label("body")) with
        {
            ToolbarItems = [.. names.Select(name => new ToolbarAction(name, () => log.Add($"{tag} {name}")))],
        };
        c.Show(NavigationStack([Screen("first", "A", "B")]), registry);
        await c.WaitFor(() => c.FindAll<ContentPage>().Any(p => p.ToolbarItems.Count == 2 && p.IsLoaded), "the toolbar");
        var page = c.FindAll<ContentPage>().First(p => p.ToolbarItems.Count == 2);
        await c.WaitFor(() => PlatformToolbar(page).Count == 2, "two items on the platform");
        Equal("A,B", string.Join(",", PlatformToolbar(page).Select(i => i.Title).Order()), "the platform items");

        // A callback-only render keeps the items; the platform item runs the current callback once.
        var nativeA = page.ToolbarItems[0];
        c.Render(NavigationStack([Screen("second", "A", "B")]));
        await c.Settle();
        Check(ReferenceEquals(nativeA, page.ToolbarItems[0]), "a callback-only render keeps the item");
        var invoked = PlatformToolbar(page).Single(i => i.Title == "A").Invoke();
        await c.Settle();
        Equal("second A", string.Join(",", log), $"the platform item ran the current callback once ({invoked})");

        // A removed item leaves the platform; the retired MAUI item invokes nothing.
        log.Clear();
        c.Render(NavigationStack([Screen("third", "B")]));
        await c.WaitFor(() => PlatformToolbar(page).Count == 1, "one item on the platform");
        Equal("B", PlatformToolbar(page).Single().Title, "the remaining item");
        ((IMenuItemController)nativeA).Activate();
        await c.Settle();
        Check(log.Count == 0, $"the retired item invokes nothing ({string.Join(",", log)})");
        PlatformToolbar(page).Single().Invoke();
        await c.Settle();
        Equal("third B", string.Join(",", log), "the remaining item runs its own callback (positive control)");

        // A secondary item: where the platform shows it, and its invocation.
        log.Clear();
        c.Render(NavigationStack([Screen("fourth", "B") with
        {
            ToolbarItems = [new ToolbarAction("B", () => log.Add("fourth B")), new ToolbarAction("More", () => log.Add("fourth More")) { Order = ToolbarItemOrder.Secondary }],
        }]));
        await c.WaitFor(() => PlatformToolbar(page).Any(i => i.Title == "More"), "the secondary item on the platform", 4000);
        Console.WriteLine($"# toolbar secondary: {PlatformToolbarWhere(page)}; items [{string.Join(",", PlatformToolbar(page).Select(i => i.Title))}]");
        var secondary = PlatformToolbar(page).Single(i => i.Title == "More").Invoke();
        await c.Settle();
        Equal("fourth More", string.Join(",", log), $"the secondary item ran its callback once ({secondary})");
    }
}
