using Microsoft.UI.Reactor.Maui.Direct;

namespace Reactor.Maui.Benchmark;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() =>
        MauiApp.CreateBuilder()
            .UseMauiApp<App>()
            .UseReactorDirectViews()
            .Build();
}

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new BenchmarkPage()) { Title = "Reactor renderer benchmark" };
}
