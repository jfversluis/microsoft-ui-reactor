using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui;
using Microsoft.UI.Reactor.Maui.Hosting;
using static Microsoft.UI.Reactor.Maui.Factories;

namespace Reactor.Maui.Sample;

public sealed record Demo(string Route, string Title, string Summary);

public static class Demos
{
    public static readonly IReadOnlyList<Demo> All =
    [
        new("counter", "Counter", "UseState and a click handler"),
        new("clock", "Effect", "UseEffect with cleanup, state set from a background timer"),
        new("todos", "Todos", "A controlled Entry, keyed rows and stable callbacks"),
        new("controls", "Controls", "Inputs, pickers and value controls, all controlled"),
        new("list", "CollectionView", "A virtualized list with keyed items and selection"),
    ];

    public static Element Render(string route) => route switch
    {
        "counter" => Component<CounterCard>(),
        "clock" => Component<ClockCard>(),
        "todos" => Component<TodoCard>(),
        "controls" => Component<ControlsDemo>(),
        "list" => Component<ListDemo>(),
        _ => Label($"Unknown demo '{route}'"),
    };
}

/// <summary>The app: tabs, with a navigation stack held in component state in the first tab.</summary>
public sealed class GalleryApp : Component
{
    public override Element Render()
    {
        var (tab, setTab) = UseState(0);
        return Tabs(tab, setTab,
            Component<DemoStack>(),
            Component<AboutPage>());
    }
}

public sealed class DemoStack : Component
{
    public override Element Render()
    {
        var stack = UseRouteStack("home");
        // REACTOR_SAMPLE_ROUTE=<demo> opens a demo at startup (for screenshots and smoke runs).
        UseEffect(() =>
        {
            if (Environment.GetEnvironmentVariable("REACTOR_SAMPLE_ROUTE") is { } route && Demos.All.Any(d => d.Route == route))
                stack.Push(route);
        });
        return NavigationStack(
            stack.Items.Select(route => route == "home"
                ? Page("Reactor on MAUI", Component<DemoList, Action<string>>(stack.Push)).WithKey(route)
                : Page(Demos.All.First(d => d.Route == route).Title, DemoContent(route)).WithKey(route)),
            stack.PopTo) with { Title = "Demos" };
    }

    // A virtualized list scrolls itself and needs a bounded height; the other demos scroll as a page.
    private static Element DemoContent(string route) => route == "list"
        ? VStack(Demos.Render(route)).Margin(16)
        : ScrollView(VStack(Demos.Render(route)).Margin(24));
}

public sealed class DemoList : Component<Action<string>>
{
    public override Element Render() =>
        ScrollView(VStack(12, [
            Label("Components, hooks and keyed reconciliation from a renderer-neutral core.").Margin(0, 0, 0, 8),
            .. Demos.All.Select(demo =>
                Border(VStack(4,
                        Label(demo.Title).FontSize(18).Bold(),
                        Label(demo.Summary).Opacity(0.7)))
                    .Padding(16)
                    .Stroke(Colors.Gray.WithAlpha(0.4f))
                    .CornerRadius(8)
                    .OnTap(() => Props(demo.Route))
                    .AutomationId($"Demo_{demo.Route}")
                    .WithKey(demo.Route)),
        ]).Margin(24));
}

/// <summary>App-level hooks: the theme and the window size re-render this page when they change.</summary>
public sealed class AboutPage : Component
{
    public override Element Render()
    {
        var theme = this.UseAppTheme();
        var size = this.UseWindowSize();
        var renderer = Environment.GetEnvironmentVariable("REACTOR_RENDERER") == "direct" ? "direct views + Controls" : "Controls";
        return Page("About", ScrollView(VStack(12,
            Label("About").FontSize(24).Bold(),
            Label($"Theme: {theme}").AutomationId("ThemeLabel"),
            Label($"Window: {size.Width:0} × {size.Height:0}").AutomationId("WindowSizeLabel"),
            Label($"Renderer: {renderer}").AutomationId("RendererLabel"),
            Button("Toggle theme", () =>
            {
                if (Application.Current is { } app)
                    app.UserAppTheme = theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
            }).AutomationId("ToggleThemeButton")
        ).Margin(24)));
    }
}
