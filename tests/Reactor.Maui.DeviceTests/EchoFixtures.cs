using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;
using Microsoft.UI.Reactor.Hosting;
using static Microsoft.UI.Reactor.Maui.Factories;
using static Reactor.Maui.DeviceTests.FixtureContext;

namespace Reactor.Maui.DeviceTests;

/// <summary>
/// Echo characterization (spec 067 §12.3): what a controlled control reports when Reactor writes
/// it, when the platform coerces the write, when the user changes it and the app refuses, and after
/// it is unmounted, on each platform and for both renderers.
/// </summary>
public static partial class Fixtures
{
    private static IEnumerable<Fixture> EchoFixtures() => Renderers.SelectMany(r => new Fixture[]
    {
        new($"Echo_ProgrammaticWrites_ReportNothing_NowOrLater [{r.Name}]", c => EchoProgrammatic(c, r.Create())),
        new($"Echo_CoercedWrites_ShowTheCoercedValue_AndReportNothing [{r.Name}]", c => EchoCoerced(c, r.Create())),
        new($"Echo_InputTheAppRefuses_IsReportedOnce_AndRestored [{r.Name}]", c => EchoRefused(c, r.Create())),
        new($"Echo_InputAfterUnmount_IsNotReported [{r.Name}]", c => EchoAfterUnmount(c, r.Create())),
    }).Append(new Fixture("Page_Disappearing_IsReportedWhileDeclared_NotAfterTheAppRemovesIt", PageRemovedByTheApp));

    private sealed class EchoLog
    {
        public List<string> Calls { get; } = [];
        public Action<int>? SetPhase { get; set; }
    }

    private sealed record EchoProps(EchoLog Log, Func<int, EchoLog, Element> Arrange);

    private sealed class EchoApp : Component<EchoProps>
    {
        public override Element Render()
        {
            var (phase, setPhase) = UseState(0);
            Props.Log.SetPhase = setPhase;
            return Props.Arrange(phase, Props.Log);
        }
    }

    private static readonly string[] PickerItems = ["zero", "one", "two", "three"];

    // Every controlled control, showing values that depend on the phase. Phases 3 and 4 are equal.
    private static Element AllControls(int phase, EchoLog log)
    {
        var p = Math.Min(phase, 3);
        var on = p % 2 == 1;
        var calls = log.Calls;
        return VStack(
            Entry($"text {p}", v => calls.Add($"entry {v}")).AutomationId("entry"),
            Editor($"text {p}", v => calls.Add($"editor {v}")).AutomationId("editor"),
            SearchBar($"text {p}", v => calls.Add($"search {v}")).AutomationId("search"),
            CheckBox(on, v => calls.Add($"check {v}")).AutomationId("check"),
            Switch(on, v => calls.Add($"switch {v}")).AutomationId("switch"),
            Slider(p / 10.0, v => calls.Add($"slider {v}")).AutomationId("slider"),
            Stepper(p * 10, v => calls.Add($"stepper {v}")).AutomationId("stepper"),
            Picker(PickerItems, p, v => calls.Add($"picker {v}")).AutomationId("picker"),
            DatePicker(new DateTime(2026, 1, 1 + p), v => calls.Add($"date {v:d}")).AutomationId("date"),
            TimePicker(TimeSpan.FromHours(8 + p), v => calls.Add($"time {v}")).AutomationId("time"),
            RadioButton("radio", on, v => calls.Add($"radio {v}")).AutomationId("radio"));
    }

    private static async Task EchoProgrammatic(FixtureContext c, ElementHandlerRegistry registry)
    {
        var log = new EchoLog();
        c.Show(Component<EchoApp, EchoProps>(new(log, AllControls)), registry);
        var entry = await Loaded<IEntry>(c, "entry");
        await c.Settle(300);
        Check(log.Calls.Count == 0, $"mounting reports nothing, got [{string.Join(", ", log.Calls)}]");

        // One write per render, two renders back to back in one batch, then a render that writes nothing.
        foreach (var step in new[] { new[] { 1 }, [2, 3], [4] })
        {
            foreach (var phase in step) log.SetPhase!(phase);
            await c.WaitFor(() => Platform.Text(entry) == $"text {Math.Min(step[^1], 3)}", $"phase {step[^1]} to render");
            await c.Settle(400);
            Check(log.Calls.Count == 0, $"phase {step[^1]}: programmatic writes report nothing, now or later; got [{string.Join(", ", log.Calls)}]");
        }

        Equal(true, c.ById<ISwitch>("switch").IsOn, "switch");
        Equal(true, c.ById<ICheckBox>("check").IsChecked, "check box");
        Close(0.3, c.ById<ISlider>("slider").Value, "slider", 1e-9);
        Close(30, c.ById<IStepper>("stepper").Value, "stepper", 1e-9);
        Equal(3, c.ById<IPicker>("picker").SelectedIndex, "picker");
        Equal(new DateTime(2026, 1, 4), c.ById<IDatePicker>("date").Date, "date");
        Equal(TimeSpan.FromHours(11), c.ById<ITimePicker>("time").Time, "time");
        Equal(true, c.ById<IRadioButton>("radio").IsChecked, "radio");
    }

    private static Element Coerced(int phase, EchoLog log)
    {
        var calls = log.Calls;
        return VStack(
            Slider(phase == 0 ? 0.5 : 1.5, v => calls.Add($"slider {v}")).AutomationId("slider"),
            Stepper(phase == 0 ? 50 : 150, v => calls.Add($"stepper {v}")).AutomationId("stepper"),
            (Entry(phase == 0 ? "ab" : "abcdef", v => calls.Add($"entry {v}")) with { MaxLength = 3 }).AutomationId("entry"),
            Picker(PickerItems, phase == 0 ? 1 : 9, v => calls.Add($"picker {v}")).AutomationId("picker"));
    }

    private static async Task EchoCoerced(FixtureContext c, ElementHandlerRegistry registry)
    {
        var log = new EchoLog();
        c.Show(Component<EchoApp, EchoProps>(new(log, Coerced)), registry);
        var entry = await Loaded<IEntry>(c, "entry");
        await c.Settle(300);
        log.SetPhase!(1);
        var slider = c.ById<ISlider>("slider");
        var stepper = c.ById<IStepper>("stepper");
        var picker = c.ById<IPicker>("picker");
        // The control shows the coerced value and nothing is reported, as on WinUI (spec 067 §14):
        // the app's state keeps the value it declared. Re-rendering it neither loops nor reports.
        for (var phase = 1; phase <= 2; phase++)
        {
            if (phase == 2) log.SetPhase!(2);
            await c.Settle(600);
            var shown = $"slider {slider.Value}, stepper {stepper.Value}, entry '{entry.Text}' / '{Platform.Text(entry)}', picker {picker.SelectedIndex}";
            Console.WriteLine($"#   phase {phase}: {shown}; calls [{string.Join(", ", log.Calls)}]");
            Equal("slider 1, stepper 100, entry 'abc' / 'abc', picker 3", shown, $"phase {phase}: the coerced values");
            Check(log.Calls.Count == 0, $"phase {phase}: a coerced write reports nothing, got [{string.Join(", ", log.Calls)}]");
        }
        Check(c.UnhandledErrors.Count == 0, $"no unhandled errors, got {string.Join("; ", c.UnhandledErrors.Select(e => e.Message))}");
    }

    // The app logs input and never accepts it.
    private static Element Refusing(int phase, EchoLog log)
    {
        var calls = log.Calls;
        return VStack(
            Entry("fixed", v => calls.Add($"entry {v}")).AutomationId("entry"),
            CheckBox(false, v => calls.Add($"check {v}")).AutomationId("check"),
            Switch(false, v => calls.Add($"switch {v}")).AutomationId("switch"),
            Slider(0.2, v => calls.Add($"slider {v}")).AutomationId("slider"),
            Picker(PickerItems, 1, v => calls.Add($"picker {v}")).AutomationId("picker"));
    }

    private static async Task EchoRefused(FixtureContext c, ElementHandlerRegistry registry)
    {
        var log = new EchoLog();
        c.Show(Component<EchoApp, EchoProps>(new(log, Refusing)), registry);
        var entry = await Loaded<IEntry>(c, "entry");
        var check = c.ById<ICheckBox>("check");
        var toggle = c.ById<ISwitch>("switch");
        var slider = c.ById<ISlider>("slider");
        var picker = c.ById<IPicker>("picker");
        await c.Settle(300);

        Platform.Type(entry, "typed");
        Platform.Toggle(toggle, true);
        check.IsChecked = true;
        slider.Value = 0.9;
        picker.SelectedIndex = 3;
        await c.WaitFor(() => Platform.Text(entry) == "fixed" && !Platform.IsOn(toggle) && !check.IsChecked
            && Math.Abs(slider.Value - 0.2) < 1e-9 && picker.SelectedIndex == 1, "every control to return to its declared value");
        await c.Settle(400);
        Equal("entry typed, switch True, check True, slider 0.9, picker 3", string.Join(", ", log.Calls), "each input is reported once, restores are not");
        Equal("fixed", Platform.Text(entry), "the platform field after settling");
        Check(!Platform.IsOn(toggle), "the platform switch after settling");
    }

    private static Element Unmountable(int phase, EchoLog log)
    {
        var calls = log.Calls;
        return VStack(
            Entry("a", v => calls.Add($"entry {v}")).AutomationId("entry"),
            Switch(false, v => calls.Add($"switch {v}")).AutomationId("switch"),
            Slider(0.2, v => calls.Add($"slider {v}")).AutomationId("slider"));
    }

    private static async Task EchoAfterUnmount(FixtureContext c, ElementHandlerRegistry registry)
    {
        var log = new EchoLog();
        c.Show(Component<EchoApp, EchoProps>(new(log, Unmountable)), registry);
        var entry = await Loaded<IEntry>(c, "entry");
        var toggle = c.ById<ISwitch>("switch");
        var slider = c.ById<ISlider>("slider");
        await c.Settle(300);

        // Positive control: the same input, while mounted, is reported.
        entry.Text = "before";
        await c.Settle(300);
        Equal("entry before", string.Join(", ", log.Calls), "input while mounted");
        log.Calls.Clear();

        c.Unmount();
        await c.Settle(300);
        Check(log.Calls.Count == 0, $"unmounting reports nothing, got [{string.Join(", ", log.Calls)}]");

        entry.Text = "after";
        toggle.IsOn = true;
        slider.Value = 0.7;
        await c.Settle(300);
        Check(log.Calls.Count == 0, $"input after unmount reaches no callback, got [{string.Join(", ", log.Calls)}]");
        Check(c.UnhandledErrors.Count == 0, $"no unhandled errors, got {string.Join("; ", c.UnhandledErrors.Select(e => e.Message))}");
    }

    private sealed record LifecycleProps(List<string> Log, Action<Action<int>> Publish);

    // A navigation stack of 1-3 declared pages; the detail page logs its lifecycle.
    private sealed class LifecycleStack : Component<LifecycleProps>
    {
        public override Element Render()
        {
            var (depth, setDepth) = UseState(1);
            Props.Publish(setDepth);
            var log = Props.Log;
            Element[] pages =
            [
                Page("home", Label("home")).WithKey("home"),
                Page("detail", Label("detail")).WithKey("detail") with
                {
                    OnAppearing = () => log.Add("appearing"),
                    OnDisappearing = () => log.Add("disappearing"),
                },
                Page("more", Label("more")).WithKey("more"),
            ];
            return NavigationStack(pages[..depth]);
        }
    }

    // A page the app removes is unmounted before the platform pops it, so its Disappearing is not
    // reported; a page that is covered while still declared reports it (spec 067 §14).
    private static async Task PageRemovedByTheApp(FixtureContext c)
    {
        var log = new List<string>();
        Action<int>? setDepth = null;
        c.Show(Component<LifecycleStack, LifecycleProps>(new(log, s => setDepth = s)));
        var navigation = await LoadedPage<NavigationPage>(c);
        await c.Settle();

        async Task Show(int depth, string title)
        {
            setDepth!(depth);
            await c.WaitFor(() => navigation.Navigation.NavigationStack.Count == depth
                && navigation.CurrentPage?.Title == title && navigation.CurrentPage.IsLoaded, $"{title} to be shown", 20000);
            await c.Settle(800);
        }

        await Show(2, "detail");
        Equal("appearing", string.Join(", ", log), "the detail page is shown");

        // Positive control: covered by a declared page, then uncovered, while still declared.
        await Show(3, "more");
        await Show(2, "detail");
        Equal("appearing, disappearing, appearing", string.Join(", ", log), "covered and uncovered while declared");
        log.Clear();

        await Show(1, "home");
        Equal("", string.Join(", ", log), "after the app pops the page");
    }
}

public static partial class Fixtures
{
    // A theme change reaches every input view MAUI still knows about. One Reactor removed must not
    // crash it (Android: SIGSEGV in SearchBarHandler.MapPlaceholderColor on a released native view).
    private static IEnumerable<Fixture> TeardownFixtures() => Renderers.Select(r =>
        new Fixture($"Theme_ChangeAfterInputViewsAreRemoved_DoesNotCrash [{r.Name}]", c => ThemeAfterRemoval(c, r.Create())));

    private static async Task ThemeAfterRemoval(FixtureContext c, ElementHandlerRegistry registry)
    {
        var app = Application.Current!;
        var original = app.UserAppTheme;
        try
        {
            for (var round = 0; round < 12; round++)
            {
                c.Show(VStack(
                    SearchBar("s", _ => { }, placeholder: "search").AutomationId("search"),
                    SearchBar("t", _ => { }),
                    SearchBar("u", _ => { }, placeholder: "more"),
                    Entry("e", _ => { }, placeholder: "entry"),
                    Editor("d", _ => { }, placeholder: "editor")), registry);
                var search = (VisualElement)await Loaded<ISearchBar>(c, "search");
                await c.Settle(200);
                await c.WaitFor(() => search.Handler is not null, "the search bar to have a handler while shown");
                c.Unmount();
                // The mechanism: a removed input view is disconnected, so an app-wide event can't reach its native views.
                await c.WaitFor(() => search.Handler is null, "the removed search bar's handler to be disconnected");
                for (var i = 0; i < 3; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
#if ANDROID
                    Java.Lang.JavaSystem.Gc();
#endif
                    await c.Settle(200);
                }
                app.UserAppTheme = round % 2 == 0 ? AppTheme.Dark : AppTheme.Light;
                await c.Settle(300);
            }
        }
        finally
        {
            app.UserAppTheme = original;
        }
        Check(c.UnhandledErrors.Count == 0, $"no unhandled errors, got {string.Join("; ", c.UnhandledErrors.Select(e => e.Message))}");
    }
}
