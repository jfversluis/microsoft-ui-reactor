using Microsoft.UI.Reactor.Maui.Direct;

namespace Reactor.Maui.DeviceTests;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() =>
        MauiApp.CreateBuilder()
            .UseMauiApp<App>()
            .UseReactorDirectViews()
            .Build();
}

/// <summary>Runs the device fixtures once the window is up, then exits with the result.</summary>
public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new ContentPage()) { Title = "Reactor device tests" };
        var started = false;
        window.Created += (_, _) =>
        {
            if (started) return;
            started = true;
            // Let the first page finish loading before the fixtures start swapping pages.
            window.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500),
                () => _ = Runner.RunAsync(window, Environment.GetEnvironmentVariable("REACTOR_DEVICE_TEST_FILTER")));
        };
        return window;
    }
}
