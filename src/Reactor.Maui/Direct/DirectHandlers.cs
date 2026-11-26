using Microsoft.Maui.Handlers;
using Microsoft.Maui.Hosting;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Maui.Handlers;
using ControlsHandlers = Microsoft.UI.Reactor.Maui.Handlers;

namespace Microsoft.UI.Reactor.Maui.Direct;

/// <summary>
/// The hybrid renderer. Primitives and layouts render as <see cref="DirectView"/>s that MAUI's stock
/// handlers draw without a Controls object; everything else, and any element that uses a setting
/// only a Controls view supports (styles, gestures, shadows, <c>.Set</c>…), renders as a regular
/// Controls view. Both kinds mix freely in one tree.
/// </summary>
public static class DirectHandlers
{
    public static ElementHandlerRegistry CreateRegistry() => MauiHandlers.CreateRegistry()
        .Register(Hybrid(new LabelHandler(), new ControlsHandlers.LabelHandler(),
            static e => e.LineBreakMode is not null || e.MaxLines is not null || e.TextType is not null || e.TextTransform is not null))
        .Register(Hybrid(new ButtonHandler(), new ControlsHandlers.ButtonHandler(), static e => e.ImageSource is not null))
        .Register(Hybrid(new EntryHandler(), new ControlsHandlers.EntryHandler(), static _ => false))
        .Register(Hybrid(new CheckBoxHandler(), ControlsHandlers.ControlDescriptors.CheckBox(), static _ => false))
        .Register(Hybrid(new SwitchHandler(), ControlsHandlers.ControlDescriptors.Switch(), static _ => false))
        .Register(Hybrid(new SliderHandler(), new ControlsHandlers.SliderHandler(), static _ => false))
        .Register(Hybrid(new ProgressBarHandler(), ControlsHandlers.ControlDescriptors.ProgressBar(), static _ => false))
        .Register(Hybrid(new ActivityIndicatorHandler(), ControlsHandlers.ControlDescriptors.ActivityIndicator(), static _ => false))
        .Register(Hybrid(new ImageHandler(), new ControlsHandlers.ImageHandler(), static _ => false))
        .Register(Hybrid(new StackHandler<VStackElement>(StackOrientation.Vertical, static e => e.Spacing), new VStackHandler(), static _ => false))
        .Register(Hybrid(new StackHandler<HStackElement>(StackOrientation.Horizontal, static e => e.Spacing), new HStackHandler(), static _ => false))
        .Register(Hybrid(new GridHandler(), new ControlsHandlers.GridHandler(), static _ => false));

    /// <summary>Registers MAUI's stock handlers for the direct view types.</summary>
    public static MauiAppBuilder UseReactorDirectViews(this MauiAppBuilder builder) =>
        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<DirectLabel, DirectLabelHandler>();
            handlers.AddHandler<DirectButton, Microsoft.Maui.Handlers.ButtonHandler>();
            handlers.AddHandler<DirectEntry, Microsoft.Maui.Handlers.EntryHandler>();
            handlers.AddHandler<DirectCheckBox, Microsoft.Maui.Handlers.CheckBoxHandler>();
            handlers.AddHandler<DirectSwitch, Microsoft.Maui.Handlers.SwitchHandler>();
            handlers.AddHandler<DirectSlider, Microsoft.Maui.Handlers.SliderHandler>();
            handlers.AddHandler<DirectProgressBar, Microsoft.Maui.Handlers.ProgressBarHandler>();
            handlers.AddHandler<DirectActivityIndicator, Microsoft.Maui.Handlers.ActivityIndicatorHandler>();
            handlers.AddHandler<DirectImage, Microsoft.Maui.Handlers.ImageHandler>();
            handlers.AddHandler<DirectStackLayout, LayoutHandler>();
            handlers.AddHandler<DirectGrid, LayoutHandler>();
        });

    private static HybridHandler<TElement, TDirect, TControls> Hybrid<TElement, TDirect, TControls>(
        Reactor.Hosting.IElementHandler<TElement, TDirect> direct,
        Reactor.Hosting.IElementHandler<TElement, TControls> controls,
        Func<TElement, bool> needsControls)
        where TElement : ViewElement
        where TDirect : DirectView
        where TControls : View
        => new(direct, controls, needsControls);

    // ── Direct handlers ─────────────────────────────────────────────────────

    private abstract class DirectHandler<TElement, TView> : Reactor.Hosting.IElementHandler<TElement, TView>
        where TElement : ViewElement
        where TView : DirectView
    {
        public TView Mount(HandlerContext<TElement> context, TElement element)
        {
            var view = Create(element);
            Wire(context, view);
            Apply(context, view, null, element);
            view.ApplyModifiers(null, element.Modifiers);
            return view;
        }

        public void Update(HandlerContext<TElement> context, TElement oldElement, TElement newElement, TView control)
        {
            Apply(context, control, oldElement, newElement);
            control.ApplyModifiers(oldElement.Modifiers, newElement.Modifiers);
        }

        public virtual IChildrenStrategy<TElement, TView>? Children => null;

        protected abstract TView Create(TElement element);

        protected virtual void Wire(HandlerContext<TElement> context, TView view) { }

        protected abstract void Apply(HandlerContext<TElement> context, TView view, TElement? previous, TElement next);
    }

    private sealed class LabelHandler : DirectHandler<LabelElement, DirectLabel>
    {
        protected override DirectLabel Create(LabelElement element) => new();

        protected override void Apply(HandlerContext<LabelElement> context, DirectLabel view, LabelElement? previous, LabelElement next) => view.Apply(next);
    }

    private sealed class ButtonHandler : DirectHandler<ButtonElement, DirectButton>
    {
        protected override DirectButton Create(ButtonElement element) => new();

        protected override void Wire(HandlerContext<ButtonElement> context, DirectButton view) =>
            view.OnClicked = () => context.Live?.OnClick?.Invoke();

        protected override void Apply(HandlerContext<ButtonElement> context, DirectButton view, ButtonElement? previous, ButtonElement next) => view.Apply(next);
    }

    private sealed class EntryHandler : DirectHandler<EntryElement, DirectEntry>
    {
        protected override DirectEntry Create(EntryElement element) => new();

        protected override void Wire(HandlerContext<EntryElement> context, DirectEntry view)
        {
            view.OnTextChanged = text =>
            {
                if (context.IsEchoing(view)) return;
                context.Live?.OnTextChanged?.Invoke(text);
                context.RestoreAfterEvent();
            };
            view.OnCompleted = () => context.Live?.OnCompleted?.Invoke();
            view.OnSelectionChanged = () => SelectionTracker.Find(view)?.Notified(context);
            view.HandlerChanged += () => SelectionTracker.Find(view)?.ObservePlatform(context);
        }

        protected override void Apply(HandlerContext<EntryElement> context, DirectEntry view, EntryElement? previous, EntryElement next)
        {
            var selection = SelectionTracker.IsUsed(next) ? SelectionTracker.For(view) : SelectionTracker.Find(view);
            if (selection is null)
            {
                view.Apply(next);
                if (view.Text != next.Text) context.WriteSuppressed(view, () => view.SetText(next.Text));
                return;
            }
            if (previous is null || !SelectionTracker.IsUsed(previous)) selection.ObservePlatform();
            // One echo scope for everything that can move the selection (spec 070 §2.8).
            selection.BeginApply();
            var wrote = false;
            try
            {
                context.WriteSuppressed(view, () =>
                {
                    view.Apply(next);
                    wrote = SelectionTracker.InputChanged(previous, next);
                    if (view.Text != next.Text)
                    {
                        view.SetText(next.Text);
                        wrote = true;
                    }
                    if ((next.CursorPosition != previous?.CursorPosition || next.SelectionLength != previous?.SelectionLength)
                        && (next.CursorPosition is not null || next.SelectionLength is not null))
                    {
                        view.SetSelection(next.CursorPosition, next.SelectionLength);
                        selection.Wrote(view.CursorPosition, view.SelectionLength, view.Text);
                        wrote = true;
                    }
                });
            }
            finally
            {
                selection.EndApply(context, wrote);
            }
        }
    }

    private sealed class CheckBoxHandler : DirectHandler<CheckBoxElement, DirectCheckBox>
    {
        protected override DirectCheckBox Create(CheckBoxElement element) => new();

        protected override void Wire(HandlerContext<CheckBoxElement> context, DirectCheckBox view) =>
            view.OnCheckedChanged = value =>
            {
                if (context.IsEchoing(view)) return;
                context.Live?.OnCheckedChanged?.Invoke(value);
                context.RestoreAfterEvent();
            };

        protected override void Apply(HandlerContext<CheckBoxElement> context, DirectCheckBox view, CheckBoxElement? previous, CheckBoxElement next)
        {
            view.Apply(next);
            if (view.IsChecked != next.IsChecked) context.WriteSuppressed(view, () => view.SetChecked(next.IsChecked));
        }
    }

    private sealed class SwitchHandler : DirectHandler<SwitchElement, DirectSwitch>
    {
        protected override DirectSwitch Create(SwitchElement element) => new();

        protected override void Wire(HandlerContext<SwitchElement> context, DirectSwitch view) =>
            view.OnToggled = value =>
            {
                if (context.IsEchoing(view)) return;
                context.Live?.OnToggled?.Invoke(value);
                context.RestoreAfterEvent();
            };

        protected override void Apply(HandlerContext<SwitchElement> context, DirectSwitch view, SwitchElement? previous, SwitchElement next)
        {
            view.Apply(next);
            if (view.IsOn != next.IsToggled) context.WriteSuppressed(view, () => view.SetOn(next.IsToggled));
        }
    }

    private sealed class SliderHandler : DirectHandler<SliderElement, DirectSlider>
    {
        protected override DirectSlider Create(SliderElement element) => new();

        protected override void Wire(HandlerContext<SliderElement> context, DirectSlider view)
        {
            view.OnValueChanged = value =>
            {
                if (context.IsEchoing(view)) return;
                context.Live?.OnValueChanged?.Invoke(value);
                context.RestoreAfterEvent();
            };
            view.OnDragCompleted = () => context.Live?.OnDragCompleted?.Invoke();
        }

        protected override void Apply(HandlerContext<SliderElement> context, DirectSlider view, SliderElement? previous, SliderElement next)
        {
            view.Apply(next);
            if (view.Minimum != next.Minimum || view.Maximum != next.Maximum || view.Value != next.Value)
                context.WriteSuppressed(view, () => view.SetRange(next.Minimum, next.Maximum, next.Value));
        }
    }

    private sealed class ProgressBarHandler : DirectHandler<ProgressBarElement, DirectProgressBar>
    {
        protected override DirectProgressBar Create(ProgressBarElement element) => new();

        protected override void Apply(HandlerContext<ProgressBarElement> context, DirectProgressBar view, ProgressBarElement? previous, ProgressBarElement next) => view.Apply(next);
    }

    private sealed class ActivityIndicatorHandler : DirectHandler<ActivityIndicatorElement, DirectActivityIndicator>
    {
        protected override DirectActivityIndicator Create(ActivityIndicatorElement element) => new();

        protected override void Apply(HandlerContext<ActivityIndicatorElement> context, DirectActivityIndicator view, ActivityIndicatorElement? previous, ActivityIndicatorElement next) => view.Apply(next);
    }

    private sealed class ImageHandler : DirectHandler<ImageElement, DirectImage>
    {
        protected override DirectImage Create(ImageElement element) => new();

        protected override void Apply(HandlerContext<ImageElement> context, DirectImage view, ImageElement? previous, ImageElement next) => view.Apply(previous, next);
    }

    private sealed class StackHandler<TElement>(StackOrientation orientation, Func<TElement, double> getSpacing) : DirectHandler<TElement, DirectStackLayout>
        where TElement : LayoutElement
    {
        protected override DirectStackLayout Create(TElement element) => new(orientation);

        protected override void Apply(HandlerContext<TElement> context, DirectStackLayout view, TElement? previous, TElement next)
        {
            view.SetSpacing(getSpacing(next));
            view.SetPadding(next.Padding);
        }

        public override IChildrenStrategy<TElement, DirectStackLayout> Children { get; } =
            ChildrenStrategy.List<TElement, DirectStackLayout, IView>(static e => e.Children, static stack => stack);
    }

    private sealed class GridHandler : DirectHandler<GridElement, DirectGrid>
    {
        protected override DirectGrid Create(GridElement element) => new();

        protected override void Apply(HandlerContext<GridElement> context, DirectGrid view, GridElement? previous, GridElement next)
        {
            view.Apply(next);
            view.SetPadding(next.Padding);
        }

        public override IChildrenStrategy<GridElement, DirectGrid> Children { get; } =
            ChildrenStrategy.List<GridElement, DirectGrid, IView>(static e => e.Children, static grid => grid);
    }
}

/// <summary>
/// Renders an element as a direct view, or as a Controls view when it uses a setting only Controls
/// supports. Switching between the two remounts the element (<see cref="CanUpdate"/>).
/// </summary>
internal sealed class HybridHandler<TElement, TDirect, TControls> : Reactor.Hosting.IElementHandler<TElement, object>
    where TElement : ViewElement
    where TDirect : DirectView
    where TControls : View
{
    private readonly Reactor.Hosting.IElementHandler<TElement, TDirect> _direct;
    private readonly Reactor.Hosting.IElementHandler<TElement, TControls> _controls;
    private readonly Func<TElement, bool> _needsControls;
    private readonly IChildrenStrategy<TElement, TDirect>? _directChildren;
    private readonly IChildrenStrategy<TElement, TControls>? _controlsChildren;

    public HybridHandler(
        Reactor.Hosting.IElementHandler<TElement, TDirect> direct,
        Reactor.Hosting.IElementHandler<TElement, TControls> controls,
        Func<TElement, bool> needsControls)
    {
        _direct = direct;
        _controls = controls;
        _needsControls = needsControls;
        _directChildren = direct.Children;
        _controlsChildren = controls.Children;
        Children = _directChildren is null && _controlsChildren is null ? null : new HybridChildren(this);
    }

    // A ref whose type the direct view isn't (ElementRef<Entry>) needs the Controls view (spec 070 §2.5).
    private bool UseDirect(TElement element) =>
        !(element.Modifiers?.NeedsControls ?? false) && !_needsControls(element)
        && (element.Modifiers?.Ref is not { } cell || cell.ExpectedType.IsAssignableFrom(typeof(TDirect)));

    public object Mount(HandlerContext<TElement> context, TElement element) =>
        UseDirect(element) ? _direct.Mount(context, element) : _controls.Mount(context, element);

    public void Update(HandlerContext<TElement> context, TElement oldElement, TElement newElement, object control)
    {
        if (control is TDirect d) _direct.Update(context, oldElement, newElement, d);
        else _controls.Update(context, oldElement, newElement, (TControls)control);
    }

    public void Unmount(object control)
    {
        if (control is TDirect d) _direct.Unmount(d);
        else _controls.Unmount((TControls)control);
    }

    public bool CanUpdate(TElement oldElement, TElement newElement) => UseDirect(oldElement) == UseDirect(newElement);

    public IChildrenStrategy<TElement, object>? Children { get; }

    private sealed class HybridChildren(HybridHandler<TElement, TDirect, TControls> owner) : IChildrenStrategy<TElement, object>
    {
        public IReadOnlyList<Element?> GetChildren(TElement element) =>
            (owner._directChildren?.GetChildren(element) ?? owner._controlsChildren?.GetChildren(element))!;

        public void Sync(object control, IReadOnlyList<object> natives)
        {
            if (control is TDirect d) owner._directChildren!.Sync(d, natives);
            else owner._controlsChildren!.Sync((TControls)control, natives);
        }
    }
}
