using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Hosting;

namespace Reactor.Maui.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseReactorDirectViews()
            .ConfigureFonts(fonts => fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"));
        return builder.Build();
    }
}

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        // REACTOR_RENDERER=direct renders primitives and layouts as direct views (MAUI Core
        // interfaces, no Controls objects); pages, lists and the rest stay on Controls.
        var direct = Environment.GetEnvironmentVariable("REACTOR_RENDERER") == "direct";
        return new ReactorWindow(
            Microsoft.UI.Reactor.Maui.Factories.Component<GalleryApp>(),
            direct ? DirectHandlers.CreateRegistry() : null)
        {
            Title = direct ? "Reactor on MAUI (direct)" : "Reactor on MAUI",
        };
    }
}
