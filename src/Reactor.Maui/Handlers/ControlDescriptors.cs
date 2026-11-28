using MauiActivityIndicator = Microsoft.Maui.Controls.ActivityIndicator;
using MauiBoxView = Microsoft.Maui.Controls.BoxView;
using MauiCheckBox = Microsoft.Maui.Controls.CheckBox;
using MauiEditor = Microsoft.Maui.Controls.Editor;
using MauiIndicatorView = Microsoft.Maui.Controls.IndicatorView;
using MauiProgressBar = Microsoft.Maui.Controls.ProgressBar;
using MauiSearchBar = Microsoft.Maui.Controls.SearchBar;
using MauiSwitch = Microsoft.Maui.Controls.Switch;

namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>The built-in controls written as <see cref="ControlDescriptor{TElement, TView}"/>s (spec 067 §16).</summary>
internal static class ControlDescriptors
{
    public static ControlDescriptor<BoxViewElement, MauiBoxView> BoxView() =>
        ControlDescriptor.Create<BoxViewElement, MauiBoxView>()
            .Property(MauiBoxView.ColorProperty, static e => e.Color)
            .Property(MauiBoxView.CornerRadiusProperty, static e => e.CornerRadius);

    public static ControlDescriptor<ActivityIndicatorElement, MauiActivityIndicator> ActivityIndicator() =>
        ControlDescriptor.Create<ActivityIndicatorElement, MauiActivityIndicator>()
            .OneWay(static e => e.IsRunning, static (v, running) => v.IsRunning = running)
            .Property(MauiActivityIndicator.ColorProperty, static e => e.Color);

    public static ControlDescriptor<ProgressBarElement, MauiProgressBar> ProgressBar()
    {
        return ControlDescriptor.Create<ProgressBarElement, MauiProgressBar>()
#if ANDROID
            // MAUI's own type, so implicit ProgressBar styles apply; the color clear knows it as Reactor's.
            .Event(static (_, view) => ProgressColorClear.Own(view))
#endif
            .OneWay(static e => e.Progress, static (v, progress) => v.Progress = progress)
            .Property(MauiProgressBar.ProgressColorProperty, static e => e.ProgressColor);
    }

    /// <summary>
    /// Page indicators (spec 070 §5.5): the position is controlled, clamped to the count (MAUI accepts any
    /// value, and each platform shows something different beyond it). A write's change event is raised
    /// inside the write; a tap's inside the platform's action (measured, §5.1).
    /// </summary>
    public static ControlDescriptor<IndicatorViewElement, MauiIndicatorView> IndicatorView() =>
        ControlDescriptor.Create<IndicatorViewElement, MauiIndicatorView>()
            .OneWay(static e => Math.Max(0, e.Count), static (v, count) => v.Count = count)
            .Property(MauiIndicatorView.IndicatorColorProperty, static e => e.IndicatorColor)
            .Property(MauiIndicatorView.SelectedIndicatorColorProperty, static e => e.SelectedIndicatorColor)
            .Property(MauiIndicatorView.IndicatorSizeProperty, static e => e.IndicatorSize)
            .Property(MauiIndicatorView.IndicatorsShapeProperty, static e => e.IndicatorsShape)
            .Property(MauiIndicatorView.MaximumVisibleProperty, static e => e.MaximumVisible)
            .OneWay(static e => e.HideSingle, static (v, hide) => v.HideSingle = hide)
            .Controlled(
                static e => e.Count <= 0 ? 0 : Math.Clamp(e.Position, 0, e.Count - 1),
                static (v, position) => v.Position = position,
                static (v, report) => v.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MauiIndicatorView.Position)) report(v.Position);
                },
                static e => e.OnPositionChanged,
                static v => v.Position);

    public static ControlDescriptor<CheckBoxElement, MauiCheckBox> CheckBox() =>
        ControlDescriptor.Create<CheckBoxElement, MauiCheckBox>()
            .Property(MauiCheckBox.ColorProperty, static e => e.Color)
            .Controlled(
                static e => e.IsChecked,
                static (v, isChecked) => v.IsChecked = isChecked,
                static (v, report) => v.CheckedChanged += (_, e) => report(e.Value),
                static e => e.OnCheckedChanged,
                static v => v.IsChecked);

    public static ControlDescriptor<SwitchElement, MauiSwitch> Switch() =>
        ControlDescriptor.Create<SwitchElement, MauiSwitch>()
            .Property(MauiSwitch.OnColorProperty, static e => e.OnColor)
            .Property(MauiSwitch.ThumbColorProperty, static e => e.ThumbColor)
            .Controlled(
                static e => e.IsToggled,
                static (v, on) => v.IsToggled = on,
                static (v, report) => v.Toggled += (_, e) => report(e.Value),
                static e => e.OnToggled,
                static v => v.IsToggled);

    public static ControlDescriptor<EditorElement, MauiEditor> Editor() =>
        ControlDescriptor.Create<EditorElement, MauiEditor>()
            .TextProperties()
            .Property(MauiEditor.PlaceholderProperty, static e => e.Placeholder)
            .Property(MauiEditor.PlaceholderColorProperty, static e => e.PlaceholderColor)
            .Property(MauiEditor.IsReadOnlyProperty, static e => e.IsReadOnly)
            .Property(MauiEditor.MaxLengthProperty, static e => e.MaxLength)
            .Property(MauiEditor.KeyboardProperty, static e => e.Keyboard)
            .Property(MauiEditor.AutoSizeProperty, static e => e.AutoSize)
            .Controlled(
                static e => e.Text,
                static (v, text) => v.Text = text,
                static (v, report) => v.TextChanged += (_, e) => report(e.NewTextValue ?? ""),
                static e => e.OnTextChanged,
                static v => v.Text ?? "")
            .Event(static (context, v) => v.Completed += (_, _) => context.Live?.OnCompleted?.Invoke());

    public static ControlDescriptor<SearchBarElement, MauiSearchBar> SearchBar() =>
        ControlDescriptor.Create<SearchBarElement, MauiSearchBar>()
            .TextProperties()
            .Property(MauiSearchBar.PlaceholderProperty, static e => e.Placeholder)
            .Controlled(
                static e => e.Text,
                static (v, text) => v.Text = text,
                static (v, report) => v.TextChanged += (_, e) => report(e.NewTextValue ?? ""),
                static e => e.OnTextChanged,
                static v => v.Text ?? "")
            .Event(static (context, v) => v.SearchButtonPressed += (_, _) => context.Live?.OnSearch?.Invoke(v.Text ?? ""));
}
