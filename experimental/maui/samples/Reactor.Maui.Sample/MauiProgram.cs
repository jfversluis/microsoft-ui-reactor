using Microsoft.UI.Reactor.Maui.Hosting;

namespace Reactor.Maui.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"));
        return builder.Build();
    }
}

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var page = new ReactorPage(Microsoft.UI.Reactor.Maui.Factories.Component<HomePage>());
        var window = new Window(page) { Title = "Reactor on MAUI" };
        // Stops the clock's timer: effect cleanups only run when the tree unmounts.
        window.Destroying += (_, _) => page.Unmount();
        return window;
    }
}
