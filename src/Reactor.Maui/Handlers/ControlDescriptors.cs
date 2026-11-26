using MauiActivityIndicator = Microsoft.Maui.Controls.ActivityIndicator;
using MauiBoxView = Microsoft.Maui.Controls.BoxView;
using MauiCheckBox = Microsoft.Maui.Controls.CheckBox;
using MauiEditor = Microsoft.Maui.Controls.Editor;
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

    public static ControlDescriptor<ProgressBarElement, MauiProgressBar> ProgressBar() =>
        ControlDescriptor.Create<ProgressBarElement, MauiProgressBar>()
            .OneWay(static e => e.Progress, static (v, progress) => v.Progress = progress)
            .Property(MauiProgressBar.ProgressColorProperty, static e => e.ProgressColor);

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
