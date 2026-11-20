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
        // REACTOR_RENDERER=direct renders Label, Button, Entry and the stacks as direct views
        // (MAUI Core interfaces, no Controls objects); the rest stays on the Controls renderer.
        var direct = Environment.GetEnvironmentVariable("REACTOR_RENDERER") == "direct";
        var page = new ReactorPage(
            Microsoft.UI.Reactor.Maui.Factories.Component<HomePage>(),
            direct ? DirectHandlers.CreateRegistry() : null);
        var window = new Window(page) { Title = direct ? "Reactor on MAUI (direct)" : "Reactor on MAUI" };
        // Stops the clock's timer: effect cleanups only run when the tree unmounts.
        window.Destroying += (_, _) => page.Unmount();
        return window;
    }
}
