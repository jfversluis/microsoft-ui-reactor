using Microsoft.Maui.Controls.Shapes;
using Microsoft.UI.Reactor.Hosting;
using MauiActivityIndicator = Microsoft.Maui.Controls.ActivityIndicator;
using MauiBoxView = Microsoft.Maui.Controls.BoxView;
using MauiButton = Microsoft.Maui.Controls.Button;
using MauiCheckBox = Microsoft.Maui.Controls.CheckBox;
using MauiDatePicker = Microsoft.Maui.Controls.DatePicker;
using MauiEditor = Microsoft.Maui.Controls.Editor;
using MauiEntry = Microsoft.Maui.Controls.Entry;
using MauiGraphicsView = Microsoft.Maui.Controls.GraphicsView;
using MauiImage = Microsoft.Maui.Controls.Image;
using MauiImageButton = Microsoft.Maui.Controls.ImageButton;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiPicker = Microsoft.Maui.Controls.Picker;
using MauiProgressBar = Microsoft.Maui.Controls.ProgressBar;
using MauiRadioButton = Microsoft.Maui.Controls.RadioButton;
using MauiSearchBar = Microsoft.Maui.Controls.SearchBar;
using MauiSlider = Microsoft.Maui.Controls.Slider;
using MauiStepper = Microsoft.Maui.Controls.Stepper;
using MauiSwitch = Microsoft.Maui.Controls.Switch;
using MauiTimePicker = Microsoft.Maui.Controls.TimePicker;
using MauiWebView = Microsoft.Maui.Controls.WebView;

namespace Microsoft.UI.Reactor.Maui.Handlers;

// Controlled-value handlers follow one pattern: Apply writes the element's value inside
// WriteSuppressed, so the change event that write raises is not reported; the event handler
// reports genuine input through the live element (null once unmounted) and then calls RestoreAfterEvent, which puts
// the declared value back if the callback did not accept the input.

internal sealed class LabelHandler : ViewHandler<LabelElement, MauiLabel>
{
    protected override MauiLabel Create(HandlerContext<LabelElement> context, LabelElement element) => new();

    protected override void Apply(HandlerContext<LabelElement> context, MauiLabel view, LabelElement? previous, LabelElement next)
    {
        if (previous?.Text != next.Text) view.Text = next.Text;
        Props.ApplyText(view, previous, next);
        Props.Set(view, MauiLabel.TextDecorationsProperty, previous?.TextDecorations, next.TextDecorations);
        Props.Set(view, MauiLabel.HorizontalTextAlignmentProperty, previous?.HorizontalTextAlignment, next.HorizontalTextAlignment);
        Props.Set(view, MauiLabel.VerticalTextAlignmentProperty, previous?.VerticalTextAlignment, next.VerticalTextAlignment);
        Props.Set(view, MauiLabel.LineBreakModeProperty, previous?.LineBreakMode, next.LineBreakMode);
        Props.Set(view, MauiLabel.MaxLinesProperty, previous?.MaxLines, next.MaxLines);
        Props.Set(view, MauiLabel.LineHeightProperty, previous?.LineHeight, next.LineHeight);
        Props.Set(view, MauiLabel.PaddingProperty, previous?.Padding, next.Padding);
        Props.Set(view, MauiLabel.TextTransformProperty, previous?.TextTransform, next.TextTransform);
        Props.Set(view, MauiLabel.TextTypeProperty, previous?.TextType, next.TextType);
    }
}

internal sealed class ImageHandler : ViewHandler<ImageElement, MauiImage>
{
    protected override MauiImage Create(HandlerContext<ImageElement> context, ImageElement element) => new();

    protected override void Apply(HandlerContext<ImageElement> context, MauiImage view, ImageElement? previous, ImageElement next)
    {
        Props.SetImage(view, MauiImage.SourceProperty, previous?.Source, next.Source, previous is null);
        Props.Set(view, MauiImage.AspectProperty, previous?.Aspect, next.Aspect);
        Props.Set(view, MauiImage.IsAnimationPlayingProperty, previous?.IsAnimationPlaying, next.IsAnimationPlaying);
    }
}

internal sealed class BoxViewHandler : ViewHandler<BoxViewElement, MauiBoxView>
{
    protected override MauiBoxView Create(HandlerContext<BoxViewElement> context, BoxViewElement element) => new();

    protected override void Apply(HandlerContext<BoxViewElement> context, MauiBoxView view, BoxViewElement? previous, BoxViewElement next)
    {
        Props.SetRef(view, MauiBoxView.ColorProperty, previous?.Color, next.Color);
        Props.Set(view, MauiBoxView.CornerRadiusProperty, previous?.CornerRadius, next.CornerRadius);
    }
}

internal sealed class ActivityIndicatorHandler : ViewHandler<ActivityIndicatorElement, MauiActivityIndicator>
{
    protected override MauiActivityIndicator Create(HandlerContext<ActivityIndicatorElement> context, ActivityIndicatorElement element) => new();

    protected override void Apply(HandlerContext<ActivityIndicatorElement> context, MauiActivityIndicator view, ActivityIndicatorElement? previous, ActivityIndicatorElement next)
    {
        if (previous?.IsRunning != next.IsRunning) view.IsRunning = next.IsRunning;
        Props.SetRef(view, MauiActivityIndicator.ColorProperty, previous?.Color, next.Color);
    }
}

internal sealed class ProgressBarHandler : ViewHandler<ProgressBarElement, MauiProgressBar>
{
    protected override MauiProgressBar Create(HandlerContext<ProgressBarElement> context, ProgressBarElement element) => new();

    protected override void Apply(HandlerContext<ProgressBarElement> context, MauiProgressBar view, ProgressBarElement? previous, ProgressBarElement next)
    {
        if (previous?.Progress != next.Progress) view.Progress = next.Progress;
        Props.SetRef(view, MauiProgressBar.ProgressColorProperty, previous?.ProgressColor, next.ProgressColor);
    }
}

internal sealed class GraphicsViewHandler : ViewHandler<GraphicsViewElement, MauiGraphicsView>
{
    protected override MauiGraphicsView Create(HandlerContext<GraphicsViewElement> context, GraphicsViewElement element) => new();

    protected override void Apply(HandlerContext<GraphicsViewElement> context, MauiGraphicsView view, GraphicsViewElement? previous, GraphicsViewElement next)
    {
        if (!ReferenceEquals(view.Drawable, next.Drawable)) view.Drawable = next.Drawable;
        // The drawable usually reads render-time state, so every update repaints.
        else view.Invalidate();
    }
}

internal sealed class WebViewHandler : ViewHandler<WebViewElement, MauiWebView>
{
    protected override MauiWebView Create(HandlerContext<WebViewElement> context, WebViewElement element)
    {
        var view = new MauiWebView();
        view.Navigated += (_, e) => context.Live?.OnNavigated?.Invoke(e.Url);
        return view;
    }

    protected override void Apply(HandlerContext<WebViewElement> context, MauiWebView view, WebViewElement? previous, WebViewElement next)
    {
        if (previous is not null && previous.Url == next.Url && previous.Html == next.Html) return;
        view.Source = next.Html is { } html
            ? new HtmlWebViewSource { Html = html }
            : next.Url is { } url ? new UrlWebViewSource { Url = url } : null;
    }
}

internal sealed class ButtonHandler : ViewHandler<ButtonElement, MauiButton>
{
    protected override MauiButton Create(HandlerContext<ButtonElement> context, ButtonElement element)
    {
        var view = new MauiButton();
        // Subscribed once; reads the current element so a re-render never needs to re-subscribe.
        view.Clicked += (_, _) => context.Live?.OnClick?.Invoke();
        return view;
    }

    protected override void Apply(HandlerContext<ButtonElement> context, MauiButton view, ButtonElement? previous, ButtonElement next)
    {
        if (previous?.Text != next.Text) view.Text = next.Text;
        Props.ApplyText(view, previous, next);
        Props.SetImage(view, MauiButton.ImageSourceProperty, previous?.ImageSource, next.ImageSource, previous is null);
        Props.Set(view, MauiButton.CornerRadiusProperty, previous?.CornerRadius, next.CornerRadius);
        Props.SetRef(view, MauiButton.BorderColorProperty, previous?.BorderColor, next.BorderColor);
        Props.Set(view, MauiButton.BorderWidthProperty, previous?.BorderWidth, next.BorderWidth);
        Props.Set(view, MauiButton.PaddingProperty, previous?.Padding, next.Padding);
    }
}

internal sealed class ImageButtonHandler : ViewHandler<ImageButtonElement, MauiImageButton>
{
    protected override MauiImageButton Create(HandlerContext<ImageButtonElement> context, ImageButtonElement element)
    {
        var view = new MauiImageButton();
        view.Clicked += (_, _) => context.Live?.OnClick?.Invoke();
        return view;
    }

    protected override void Apply(HandlerContext<ImageButtonElement> context, MauiImageButton view, ImageButtonElement? previous, ImageButtonElement next)
    {
        Props.SetImage(view, MauiImageButton.SourceProperty, previous?.Source, next.Source, previous is null);
        Props.Set(view, MauiImageButton.AspectProperty, previous?.Aspect, next.Aspect);
        Props.Set(view, MauiImageButton.CornerRadiusProperty, previous?.CornerRadius, next.CornerRadius);
        Props.Set(view, MauiImageButton.PaddingProperty, previous?.Padding, next.Padding);
    }
}

internal sealed class EntryHandler : ViewHandler<EntryElement, MauiEntry>
{
    protected override MauiEntry Create(HandlerContext<EntryElement> context, EntryElement element)
    {
        var view = new MauiEntry();
        view.TextChanged += (_, e) =>
        {
            if (context.IsEchoing(view)) return;
            context.Live?.OnTextChanged?.Invoke(e.NewTextValue ?? "");
            context.RestoreAfterEvent();
        };
        view.Completed += (_, _) => context.Live?.OnCompleted?.Invoke();
        return view;
    }

    protected override void Apply(HandlerContext<EntryElement> context, MauiEntry view, EntryElement? previous, EntryElement next)
    {
        Props.ApplyText(view, previous, next);
        Props.SetRef(view, MauiEntry.PlaceholderProperty, previous?.Placeholder, next.Placeholder);
        Props.SetRef(view, MauiEntry.PlaceholderColorProperty, previous?.PlaceholderColor, next.PlaceholderColor);
        Props.Set(view, MauiEntry.IsPasswordProperty, previous?.IsPassword, next.IsPassword);
        Props.Set(view, MauiEntry.IsReadOnlyProperty, previous?.IsReadOnly, next.IsReadOnly);
        Props.Set(view, MauiEntry.MaxLengthProperty, previous?.MaxLength, next.MaxLength);
        Props.SetRef(view, MauiEntry.KeyboardProperty, previous?.Keyboard, next.Keyboard);
        Props.Set(view, MauiEntry.ReturnTypeProperty, previous?.ReturnType, next.ReturnType);
        Props.Set(view, MauiEntry.ClearButtonVisibilityProperty, previous?.ClearButtonVisibility, next.ClearButtonVisibility);
        Props.Set(view, MauiEntry.HorizontalTextAlignmentProperty, previous?.HorizontalTextAlignment, next.HorizontalTextAlignment);
        Props.Set(view, MauiEntry.IsSpellCheckEnabledProperty, previous?.IsSpellCheckEnabled, next.IsSpellCheckEnabled);
        // Compared with the control, not the old element: the user may have typed since the last render.
        if ((view.Text ?? "") != next.Text) context.WriteSuppressed(view, () => view.Text = next.Text);
    }
}

internal sealed class EditorHandler : ViewHandler<EditorElement, MauiEditor>
{
    protected override MauiEditor Create(HandlerContext<EditorElement> context, EditorElement element)
    {
        var view = new MauiEditor();
        view.TextChanged += (_, e) =>
        {
            if (context.IsEchoing(view)) return;
            context.Live?.OnTextChanged?.Invoke(e.NewTextValue ?? "");
            context.RestoreAfterEvent();
        };
        view.Completed += (_, _) => context.Live?.OnCompleted?.Invoke();
        return view;
    }

    protected override void Apply(HandlerContext<EditorElement> context, MauiEditor view, EditorElement? previous, EditorElement next)
    {
        Props.ApplyText(view, previous, next);
        Props.SetRef(view, MauiEditor.PlaceholderProperty, previous?.Placeholder, next.Placeholder);
        Props.SetRef(view, MauiEditor.PlaceholderColorProperty, previous?.PlaceholderColor, next.PlaceholderColor);
        Props.Set(view, MauiEditor.IsReadOnlyProperty, previous?.IsReadOnly, next.IsReadOnly);
        Props.Set(view, MauiEditor.MaxLengthProperty, previous?.MaxLength, next.MaxLength);
        Props.SetRef(view, MauiEditor.KeyboardProperty, previous?.Keyboard, next.Keyboard);
        Props.Set(view, MauiEditor.AutoSizeProperty, previous?.AutoSize, next.AutoSize);
        if ((view.Text ?? "") != next.Text) context.WriteSuppressed(view, () => view.Text = next.Text);
    }
}

internal sealed class SearchBarHandler : ViewHandler<SearchBarElement, MauiSearchBar>
{
    protected override MauiSearchBar Create(HandlerContext<SearchBarElement> context, SearchBarElement element)
    {
        var view = new MauiSearchBar();
        view.TextChanged += (_, e) =>
        {
            if (context.IsEchoing(view)) return;
            context.Live?.OnTextChanged?.Invoke(e.NewTextValue ?? "");
            context.RestoreAfterEvent();
        };
        view.SearchButtonPressed += (_, _) => context.Live?.OnSearch?.Invoke(view.Text ?? "");
        return view;
    }

    protected override void Apply(HandlerContext<SearchBarElement> context, MauiSearchBar view, SearchBarElement? previous, SearchBarElement next)
    {
        Props.ApplyText(view, previous, next);
        Props.SetRef(view, MauiSearchBar.PlaceholderProperty, previous?.Placeholder, next.Placeholder);
        if ((view.Text ?? "") != next.Text) context.WriteSuppressed(view, () => view.Text = next.Text);
    }
}

internal sealed class CheckBoxHandler : ViewHandler<CheckBoxElement, MauiCheckBox>
{
    protected override MauiCheckBox Create(HandlerContext<CheckBoxElement> context, CheckBoxElement element)
    {
        var view = new MauiCheckBox();
        view.CheckedChanged += (_, e) =>
        {
            if (context.IsEchoing(view)) return;
            context.Live?.OnCheckedChanged?.Invoke(e.Value);
            context.RestoreAfterEvent();
        };
        return view;
    }

    protected override void Apply(HandlerContext<CheckBoxElement> context, MauiCheckBox view, CheckBoxElement? previous, CheckBoxElement next)
    {
        Props.SetRef(view, MauiCheckBox.ColorProperty, previous?.Color, next.Color);
        if (view.IsChecked != next.IsChecked) context.WriteSuppressed(view, () => view.IsChecked = next.IsChecked);
    }
}

internal sealed class SwitchHandler : ViewHandler<SwitchElement, MauiSwitch>
{
    protected override MauiSwitch Create(HandlerContext<SwitchElement> context, SwitchElement element)
    {
        var view = new MauiSwitch();
        view.Toggled += (_, e) =>
        {
            if (context.IsEchoing(view)) return;
            context.Live?.OnToggled?.Invoke(e.Value);
            context.RestoreAfterEvent();
        };
        return view;
    }

    protected override void Apply(HandlerContext<SwitchElement> context, MauiSwitch view, SwitchElement? previous, SwitchElement next)
    {
        Props.SetRef(view, MauiSwitch.OnColorProperty, previous?.OnColor, next.OnColor);
        Props.SetRef(view, MauiSwitch.ThumbColorProperty, previous?.ThumbColor, next.ThumbColor);
        if (view.IsToggled != next.IsToggled) context.WriteSuppressed(view, () => view.IsToggled = next.IsToggled);
    }
}

internal sealed class SliderHandler : ViewHandler<SliderElement, MauiSlider>
{
    protected override MauiSlider Create(HandlerContext<SliderElement> context, SliderElement element)
    {
        var view = new MauiSlider();
        view.ValueChanged += (_, e) =>
        {
            if (context.IsEchoing(view)) return;
            context.Live?.OnValueChanged?.Invoke(e.NewValue);
            context.RestoreAfterEvent();
        };
        view.DragCompleted += (_, _) => context.Live?.OnDragCompleted?.Invoke();
        return view;
    }

    protected override void Apply(HandlerContext<SliderElement> context, MauiSlider view, SliderElement? previous, SliderElement next)
    {
        Props.SetRef(view, MauiSlider.MinimumTrackColorProperty, previous?.MinimumTrackColor, next.MinimumTrackColor);
        Props.SetRef(view, MauiSlider.MaximumTrackColorProperty, previous?.MaximumTrackColor, next.MaximumTrackColor);
        Props.SetRef(view, MauiSlider.ThumbColorProperty, previous?.ThumbColor, next.ThumbColor);
        // Changing the range can coerce the value, so range and value are written together, suppressed.
        if (view.Minimum != next.Minimum || view.Maximum != next.Maximum || view.Value != next.Value)
            context.WriteSuppressed(view, () => Range.Write(view, MauiSlider.MinimumProperty, MauiSlider.MaximumProperty, MauiSlider.ValueProperty, next.Minimum, next.Maximum, next.Value));
    }
}

internal sealed class StepperHandler : ViewHandler<StepperElement, MauiStepper>
{
    protected override MauiStepper Create(HandlerContext<StepperElement> context, StepperElement element)
    {
        var view = new MauiStepper();
        view.ValueChanged += (_, e) =>
        {
            if (context.IsEchoing(view)) return;
            context.Live?.OnValueChanged?.Invoke(e.NewValue);
            context.RestoreAfterEvent();
        };
        return view;
    }

    protected override void Apply(HandlerContext<StepperElement> context, MauiStepper view, StepperElement? previous, StepperElement next)
    {
        if (view.Increment != next.Increment) view.Increment = next.Increment;
        if (view.Minimum != next.Minimum || view.Maximum != next.Maximum || view.Value != next.Value)
            context.WriteSuppressed(view, () => Range.Write(view, MauiStepper.MinimumProperty, MauiStepper.MaximumProperty, MauiStepper.ValueProperty, next.Minimum, next.Maximum, next.Value));
    }
}

/// <summary>Writes a minimum, maximum and value. The value goes last, so it is clamped to the new range.</summary>
internal static class Range
{
    public static void Write(BindableObject view, BindableProperty minimum, BindableProperty maximum, BindableProperty value, double min, double max, double current)
    {
        if (min > max) throw new ArgumentException($"Minimum ({min}) is greater than maximum ({max}).");
        view.SetValue(minimum, min);
        view.SetValue(maximum, max);
        view.SetValue(value, Math.Clamp(current, min, max));
    }
}

internal sealed class PickerHandler : ViewHandler<PickerElement, MauiPicker>
{
    protected override MauiPicker Create(HandlerContext<PickerElement> context, PickerElement element)
    {
        var view = new MauiPicker();
        view.SelectedIndexChanged += (_, _) =>
        {
            if (context.IsEchoing(view)) return;
            context.Live?.OnSelectedIndexChanged?.Invoke(view.SelectedIndex);
            context.RestoreAfterEvent();
        };
        return view;
    }

    protected override void Apply(HandlerContext<PickerElement> context, MauiPicker view, PickerElement? previous, PickerElement next)
    {
        Props.ApplyText(view, previous, next);
        Props.SetRef(view, MauiPicker.TitleProperty, previous?.Title, next.Title);
        var itemsChanged = previous is null || !previous.Items.SequenceEqual(next.Items);
        if (itemsChanged || view.SelectedIndex != next.SelectedIndex)
        {
            context.WriteSuppressed(view, () =>
            {
                // Replacing the items resets the selection, so the index is always written after.
                if (itemsChanged) view.ItemsSource = next.Items.ToList();
                view.SelectedIndex = next.SelectedIndex;
            });
        }
    }
}

internal sealed class DatePickerHandler : ViewHandler<DatePickerElement, MauiDatePicker>
{
    protected override MauiDatePicker Create(HandlerContext<DatePickerElement> context, DatePickerElement element)
    {
        var view = new MauiDatePicker();
        view.DateSelected += (_, e) =>
        {
            if (context.IsEchoing(view) || e.NewDate is not { } date) return;
            context.Live?.OnDateSelected?.Invoke(date);
            context.RestoreAfterEvent();
        };
        return view;
    }

    protected override void Apply(HandlerContext<DatePickerElement> context, MauiDatePicker view, DatePickerElement? previous, DatePickerElement next)
    {
        Props.ApplyText(view, previous, next);
        Props.SetRef(view, MauiDatePicker.FormatProperty, previous?.Format, next.Format);
        if (previous?.MinimumDate != next.MinimumDate || previous?.MaximumDate != next.MaximumDate || view.Date != next.Date)
        {
            context.WriteSuppressed(view, () =>
            {
                Props.Set(view, MauiDatePicker.MinimumDateProperty, previous?.MinimumDate, next.MinimumDate);
                Props.Set(view, MauiDatePicker.MaximumDateProperty, previous?.MaximumDate, next.MaximumDate);
                view.Date = next.Date;
            });
        }
    }
}

internal sealed class TimePickerHandler : ViewHandler<TimePickerElement, MauiTimePicker>
{
    protected override MauiTimePicker Create(HandlerContext<TimePickerElement> context, TimePickerElement element)
    {
        var view = new MauiTimePicker();
        view.TimeSelected += (_, e) =>
        {
            if (context.IsEchoing(view) || e.NewTime is not { } time) return;
            context.Live?.OnTimeSelected?.Invoke(time);
            context.RestoreAfterEvent();
        };
        return view;
    }

    protected override void Apply(HandlerContext<TimePickerElement> context, MauiTimePicker view, TimePickerElement? previous, TimePickerElement next)
    {
        Props.ApplyText(view, previous, next);
        Props.SetRef(view, MauiTimePicker.FormatProperty, previous?.Format, next.Format);
        if (view.Time != next.Time) context.WriteSuppressed(view, () => view.Time = next.Time);
    }
}

internal sealed class RadioButtonHandler : ViewHandler<RadioButtonElement, MauiRadioButton>
{
    protected override MauiRadioButton Create(HandlerContext<RadioButtonElement> context, RadioButtonElement element)
    {
        var view = new MauiRadioButton();
        view.CheckedChanged += (_, e) =>
        {
            if (context.IsEchoing(view)) return;
            context.Live?.OnCheckedChanged?.Invoke(e.Value);
            context.RestoreAfterEvent();
        };
        return view;
    }

    protected override void Apply(HandlerContext<RadioButtonElement> context, MauiRadioButton view, RadioButtonElement? previous, RadioButtonElement next)
    {
        if (previous?.Content != next.Content) view.Content = next.Content;
        Props.ApplyText(view, previous, next);
        Props.SetRef(view, MauiRadioButton.GroupNameProperty, previous?.GroupName, next.GroupName);
        if (view.IsChecked != next.IsChecked) context.WriteSuppressed(view, () => view.IsChecked = next.IsChecked);
    }
}

internal sealed class BorderHandler : ViewHandler<BorderElement, Border>
{
    protected override Border Create(HandlerContext<BorderElement> context, BorderElement element) => new();

    protected override void Apply(HandlerContext<BorderElement> context, Border view, BorderElement? previous, BorderElement next)
    {
        if (previous is null || !Equals(previous.Stroke, next.Stroke))
        {
            if (next.Stroke is { } stroke) view.Stroke = new SolidColorBrush(stroke);
            else if (previous is not null) view.ClearValue(Border.StrokeProperty);
        }
        Props.Set(view, Border.StrokeThicknessProperty, previous?.StrokeThickness, next.StrokeThickness);
        Props.Set(view, Border.PaddingProperty, previous?.Padding, next.Padding);
        if (previous is null || previous.CornerRadius != next.CornerRadius)
        {
            if (next.CornerRadius is { } radius) view.StrokeShape = new RoundRectangle { CornerRadius = radius };
            else if (previous is not null) view.ClearValue(Border.StrokeShapeProperty);
        }
    }

    public override IChildrenStrategy<BorderElement, Border> Children { get; } =
        ChildrenStrategy.Single<BorderElement, Border, object>(e => e.Content, (border, content) => border.Content = content is null ? null : Props.AsView(content));
}
