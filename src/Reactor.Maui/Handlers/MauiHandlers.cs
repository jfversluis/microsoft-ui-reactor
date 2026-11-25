using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>The Controls renderer: every element renders to a <c>Microsoft.Maui.Controls</c> view.</summary>
public static class MauiHandlers
{
    public static ElementHandlerRegistry CreateRegistry() => new ElementHandlerRegistry()
        .Register(new LabelHandler())
        .Register(new ImageHandler())
        .Register(ControlDescriptors.BoxView())
        .Register(ControlDescriptors.ActivityIndicator())
        .Register(ControlDescriptors.ProgressBar())
        .Register(new BorderHandler())
        .Register(new GraphicsViewHandler())
        .Register(new WebViewHandler())
        .Register(new ButtonHandler())
        .Register(new ImageButtonHandler())
        .Register(new EntryHandler())
        .Register(ControlDescriptors.Editor())
        .Register(ControlDescriptors.SearchBar())
        .Register(ControlDescriptors.CheckBox())
        .Register(ControlDescriptors.Switch())
        .Register(new SliderHandler())
        .Register(new StepperHandler())
        .Register(new PickerHandler())
        .Register(new DatePickerHandler())
        .Register(new TimePickerHandler())
        .Register(ControlDescriptors.RadioButton())
        .Register(new VStackHandler())
        .Register(new HStackHandler())
        .Register(new GridHandler())
        .Register(new FlexLayoutHandler())
        .Register(new AbsoluteLayoutHandler())
        .Register(new ScrollViewHandler())
        .Register(new RefreshViewHandler())
        .Register(new SwipeViewHandler())
        .Register(new CollectionViewHandler())
        .Register(new CarouselViewHandler())
        .Register(new PageHandler())
        .Register(new NavigationStackHandler())
        .Register(new TabsHandler())
        .Register(new FlyoutHandler())
        .Register(new ShellHandler())
        .Register(new TabBarHandler())
        .Register(new FlyoutItemHandler())
        .Register(new ShellMenuItemHandler())
        .Register(new ShellTabHandler())
        .Register(new ShellContentHandler())
        .Register(new ShellSectionStackHandler());

    /// <summary>
    /// Hosts any native view in a Controls slot typed as <see cref="View"/>; a view from another
    /// renderer is wrapped in a reused Grid.
    /// </summary>
    internal static View AsView(object native) => Props.AsView(native);
}
