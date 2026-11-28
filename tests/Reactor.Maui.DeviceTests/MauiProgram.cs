using Microsoft.Extensions.Logging;
using Microsoft.UI.Reactor.Maui.Direct;

namespace Reactor.Maui.DeviceTests;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder()
            .UseMauiApp<App>()
            .UseReactorDirectViews()
            // Spec 071 (4d-1): registered fonts, with the aliases the look fixtures use.
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });
        // What MAUI logs (a missing font, a binding failure), for the characterizations.
        builder.Logging.AddProvider(CapturedLogs.Instance);
        builder.Logging.AddFilter<CapturedLogs>(null, LogLevel.Debug);
        return builder.Build();
    }
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
