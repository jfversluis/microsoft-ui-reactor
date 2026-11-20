using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>The Controls renderer: every element renders to a <c>Microsoft.Maui.Controls</c> view.</summary>
public static class MauiHandlers
{
    public static ElementHandlerRegistry CreateRegistry() => new ElementHandlerRegistry()
        .Register(new LabelHandler())
        .Register(new ImageHandler())
        .Register(new BoxViewHandler())
        .Register(new ActivityIndicatorHandler())
        .Register(new ProgressBarHandler())
        .Register(new BorderHandler())
        .Register(new GraphicsViewHandler())
        .Register(new WebViewHandler())
        .Register(new ButtonHandler())
        .Register(new ImageButtonHandler())
        .Register(new EntryHandler())
        .Register(new EditorHandler())
        .Register(new SearchBarHandler())
        .Register(new CheckBoxHandler())
        .Register(new SwitchHandler())
        .Register(new SliderHandler())
        .Register(new StepperHandler())
        .Register(new PickerHandler())
        .Register(new DatePickerHandler())
        .Register(new TimePickerHandler())
        .Register(new RadioButtonHandler())
        .Register(new VStackHandler())
        .Register(new HStackHandler())
        .Register(new GridHandler())
        .Register(new FlexLayoutHandler())
        .Register(new AbsoluteLayoutHandler())
        .Register(new ScrollViewHandler())
        .Register(new RefreshViewHandler())
        .Register(new SwipeViewHandler())
        .Register(new CollectionViewHandler())
        .Register(new CarouselViewHandler());

    /// <summary>
    /// Hosts any native view in a Controls slot typed as <see cref="View"/>; a view from another
    /// renderer is wrapped in a reused Grid.
    /// </summary>
    internal static View AsView(object native) => Props.AsView(native);
}
