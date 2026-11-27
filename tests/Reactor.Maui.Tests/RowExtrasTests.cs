using Microsoft.Maui.Layouts;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiAbsoluteLayout = Microsoft.Maui.Controls.AbsoluteLayout;
using MauiDatePicker = Microsoft.Maui.Controls.DatePicker;
using MauiFlexLayout = Microsoft.Maui.Controls.FlexLayout;
using MauiLabel = Microsoft.Maui.Controls.Label;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>What spec 070 §4.7 asks of the 🧪 rows beyond the property matrix.</summary>
public class RowExtrasTests
{
    public static TheoryData<string> Registries() => ["controls", "direct"];

    private static TestHost Host(string registry) =>
        new(PropertyMatrixTests.Registries.Single(r => r.Name == registry).Create());

    [Theory]
    [MemberData(nameof(Registries))]
    public void FlexChildValues_AreSetUpdatedAndCleared_AndMakeADirectChildAControlsView(string registry)
    {
        var host = Host(registry).Render(FlexLayout(Label("a").Flex(grow: 1, basis: new FlexBasis(0.5f, true), alignSelf: FlexAlignSelf.Center)));
        var layout = host.View<MauiFlexLayout>();
        var child = Assert.IsType<MauiLabel>(layout[0]);
        Assert.Equal((1f, new FlexBasis(0.5f, true), FlexAlignSelf.Center), (MauiFlexLayout.GetGrow(child), MauiFlexLayout.GetBasis(child), MauiFlexLayout.GetAlignSelf(child)));

        host.Render(FlexLayout(Label("a").Flex(grow: 2, shrink: 0)));
        Assert.Same(child, layout[0]);
        Assert.Equal((2f, 0f), (MauiFlexLayout.GetGrow(child), MauiFlexLayout.GetShrink(child)));
        Assert.False(child.IsSet(MauiFlexLayout.BasisProperty));
        Assert.False(child.IsSet(MauiFlexLayout.AlignSelfProperty));

        host.Render(FlexLayout(Label("a")));
        if (registry == "direct")
        {
            // Without the values, a direct label again.
            Assert.IsType<DirectLabel>(layout[0]);
        }
        else
        {
            Assert.Same(child, layout[0]);
            Assert.False(child.IsSet(MauiFlexLayout.GrowProperty));
            Assert.False(child.IsSet(MauiFlexLayout.ShrinkProperty));
        }
    }

    [Theory]
    [MemberData(nameof(Registries))]
    public void AbsoluteChildValues_AreSetUpdatedAndCleared_AndMakeADirectChildAControlsView(string registry)
    {
        var host = Host(registry).Render(AbsoluteLayout(Label("a").Absolute(new Rect(0, 0, 0.5, 0.5), AbsoluteLayoutFlags.All)));
        var layout = host.View<MauiAbsoluteLayout>();
        var child = Assert.IsType<MauiLabel>(layout[0]);
        Assert.Equal((new Rect(0, 0, 0.5, 0.5), AbsoluteLayoutFlags.All), (MauiAbsoluteLayout.GetLayoutBounds(child), MauiAbsoluteLayout.GetLayoutFlags(child)));

        host.Render(AbsoluteLayout(Label("a").Absolute(new Rect(10, 20, 30, 40))));
        Assert.Same(child, layout[0]);
        Assert.Equal(new Rect(10, 20, 30, 40), MauiAbsoluteLayout.GetLayoutBounds(child));
        Assert.Equal(AbsoluteLayoutFlags.None, MauiAbsoluteLayout.GetLayoutFlags(child));

        host.Render(AbsoluteLayout(Label("a")));
        if (registry == "direct")
        {
            Assert.IsType<DirectLabel>(layout[0]);
        }
        else
        {
            Assert.Same(child, layout[0]);
            Assert.False(child.IsSet(MauiAbsoluteLayout.LayoutBoundsProperty));
            Assert.False(child.IsSet(MauiAbsoluteLayout.LayoutFlagsProperty));
        }
    }

    private static DatePickerElement Dated(DateTime date, DateTime min, DateTime max, List<DateTime> reports) =>
        DatePicker(date, reports.Add) with { MinimumDate = min, MaximumDate = max };

    [Fact]
    public void DatePicker_TheRangeAndTheDate_ChangedInOneRender_InEitherDirection()
    {
        var reports = new List<DateTime>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Dated(new(2024, 6, 1), new(2024, 1, 1), new(2024, 12, 31), reports));
        var picker = host.View<MauiDatePicker>();

        // Forward: the new minimum is past the old maximum.
        host.Render(Dated(new(2026, 6, 1), new(2026, 1, 1), new(2026, 12, 31), reports));
        Assert.Equal((new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), new DateTime(2026, 6, 1)), (picker.MinimumDate, picker.MaximumDate, picker.Date));

        // Backward: the new maximum is before the old minimum.
        host.Render(Dated(new(2020, 6, 1), new(2020, 1, 1), new(2020, 12, 31), reports));
        Assert.Equal((new DateTime(2020, 1, 1), new DateTime(2020, 12, 31), new DateTime(2020, 6, 1)), (picker.MinimumDate, picker.MaximumDate, picker.Date));

        host.Scheduler.Drain();
        Assert.Empty(reports);
    }

    [Fact]
    public void Stepper_MovesToADisjointRange_InEitherDirection()
    {
        var reports = new List<double>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Stepper(5, reports.Add, 0, 10));
        var stepper = host.View<Microsoft.Maui.Controls.Stepper>();

        host.Render(Stepper(250, reports.Add, 200, 300));
        Assert.Equal((200d, 300d, 250d), (stepper.Minimum, stepper.Maximum, stepper.Value));

        host.Render(Stepper(5, reports.Add, 0, 10));
        Assert.Equal((0d, 10d, 5d), (stepper.Minimum, stepper.Maximum, stepper.Value));
        host.Scheduler.Drain();
        Assert.Empty(reports);
    }

    [Fact]
    public void AnImplicitProgressBarStyle_AppliesToReactorsProgressBar()
    {
        // Reactor renders MAUI's own ProgressBar (spec 070 §4.15), so an app's themed bars keep their style.
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(ProgressBar(0.5));
        var bar = host.View<Microsoft.Maui.Controls.ProgressBar>();
        var style = new Style(typeof(Microsoft.Maui.Controls.ProgressBar)) { Setters = { new Setter { Property = VisualElement.HeightRequestProperty, Value = 27d } } };
        var parent = new ContentView { Resources = new ResourceDictionary { style } };

        parent.Content = bar;

        Assert.Equal(27d, bar.HeightRequest);
    }
}

[CollectionDefinition(nameof(DatePickerWriteTests), DisableParallelization = true)]
public sealed class DatePickerWriteCollection;

/// <summary>
/// When the date picker writes (spec 070 §4.15): counted through the handler's process-wide write seam,
/// so these tests don't run alongside others.
/// </summary>
[Collection(nameof(DatePickerWriteTests))]
public class DatePickerWriteTests
{
    private static DatePickerElement Dated(DateTime date, DateTime min, DateTime max, List<DateTime> reports) =>
        DatePicker(date, reports.Add) with { MinimumDate = min, MaximumDate = max };

    [Fact]
    public void DatePicker_ADeclaredDateOutsideTheRange_ShowsMausCoercedDate_ReportsNothing_AndAnEqualRenderWritesNothing()
    {
        var reports = new List<DateTime>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Dated(new(2024, 6, 1), new(2024, 1, 1), new(2024, 12, 31), reports));
        var picker = host.View<MauiDatePicker>();
        var writes = 0;
        DatePickerHandler.DateWritten = view => { if (ReferenceEquals(view, picker)) writes++; };
        try
        {
            host.Render(Dated(new(2025, 3, 1), new(2024, 1, 1), new(2024, 12, 31), reports));
            Assert.Equal(new DateTime(2024, 12, 31), picker.Date); // MAUI clamps to the maximum
            Assert.Equal(1, writes); // positive control: the change was written

            writes = 0;
            host.Render(Dated(new(2025, 3, 1), new(2024, 1, 1), new(2024, 12, 31), reports));
            Assert.Equal(0, writes);

            // Input that moves the date is still restored to the clamped declaration.
            picker.Date = new DateTime(2024, 3, 3);
            host.Scheduler.Drain();
            Assert.Equal([new DateTime(2024, 3, 3)], reports);
            Assert.Equal(new DateTime(2024, 12, 31), picker.Date);
        }
        finally
        {
            DatePickerHandler.DateWritten = null;
        }
    }

    [Fact]
    public void ADeclaredDateWithATimeOfDay_IsWrittenOnce_NotOnEveryRender()
    {
        var reports = new List<DateTime>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Dated(new(2024, 6, 1), new(2024, 1, 1), new(2024, 12, 31), reports));
        var picker = host.View<MauiDatePicker>();
        var writes = 0;
        DatePickerHandler.DateWritten = view => { if (ReferenceEquals(view, picker)) writes++; };
        try
        {
            host.Render(Dated(new(2024, 7, 1, 10, 30, 0), new(2024, 1, 1), new(2024, 12, 31), reports));
            Assert.Equal(new DateTime(2024, 7, 1), picker.Date); // MAUI keeps the day
            Assert.Equal(1, writes);

            writes = 0;
            host.Render(Dated(new(2024, 7, 1, 10, 30, 0), new(2024, 1, 1), new(2024, 12, 31), reports));
            Assert.Equal(0, writes);
            host.Scheduler.Drain();
            Assert.Empty(reports);
        }
        finally
        {
            DatePickerHandler.DateWritten = null;
        }
    }
}
