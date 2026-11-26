using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiBorder = Microsoft.Maui.Controls.Border;
using MauiContentView = Microsoft.Maui.Controls.ContentView;
using MauiEllipse = Microsoft.Maui.Controls.Shapes.Ellipse;
using MauiImage = Microsoft.Maui.Controls.Image;
using MauiPath = Microsoft.Maui.Controls.Shapes.Path;
using MauiPolyline = Microsoft.Maui.Controls.Shapes.Polyline;

namespace Microsoft.UI.Reactor.Maui.Tests;

// The README's shapes, brushes and images snippets (spec 070 §4), compiled and rendered.
public class ReadmeVisualsSnippetTests
{
    private sealed class AvatarStore
    {
        public int Opened;
        public Stream OpenAvatar(string id) { Opened++; return new MemoryStream(); }
    }

    private sealed record User(string Id);

    private static TestHost Host() => new(MauiHandlers.CreateRegistry());

    [Fact]
    public void TheShapesSnippets_Render()
    {
        var selected = true;
        double[] values = [0.2, 0.8, 0.5];
        var indicator = Grid("auto", "auto",
            Ellipse().Stroke(Colors.DarkGray, 0.5).Fill(Colors.White).Size(16, 16).Center(),
            selected ? Ellipse().Stroke(Colors.SteelBlue).Fill(Colors.SteelBlue).Size(8, 8).Center() : null);
        var sparkline = Polyline([.. values.Select((v, i) => new Point(i * 8, 40 - v * 40))])
            .Stroke(LinearGradientBrush(new(0, 0), new(1, 0), Colors.SkyBlue, Colors.Navy), 2)
            .StrokeLineJoin(PenLineJoin.Round);
        var badge = Path2D("M 0,10 L 10,0 L 20,10 L 10,20 Z").Fill(Colors.Orange).Aspect(Stretch.Uniform).Size(24, 24);

        var grid = Host().Render(indicator).View<Grid>();
        Assert.Equal(2, grid.Children.OfType<MauiEllipse>().Count());
        var line = Host().Render(sparkline).View<MauiPolyline>();
        Assert.Equal(3, line.Points.Count);
        Assert.IsType<LinearGradientBrush>(line.Stroke);
        Assert.NotNull(Host().Render(badge).View<MauiPath>().Data);
    }

    [Fact]
    public void TheBrushAndImageSnippets_Render()
    {
        Element content = Label("x");
        var card = ContentView(Label("Today")).Padding(16)
            .Background(LinearGradientBrush(new(0, 0), new(0, 1), (Colors.SkyBlue, 0), (Colors.Navy, 1)));
        var ring = Border(content).Stroke(RadialGradientBrush(new(0.5, 0.5), 0.5, Colors.White, Colors.Gold), 2);
        Assert.IsType<LinearGradientBrush>(Host().Render(card).View<MauiContentView>().Background);
        Assert.IsType<RadialGradientBrush>(Host().Render(ring).View<MauiBorder>().Stroke);

        var store = new AvatarStore();
        var user = new User("42");
        var host = Host().Render(Image(StreamImage($"avatar:{user.Id}", () => store.OpenAvatar(user.Id))));
        var image = host.View<MauiImage>();
        var source = image.Source;
        host.Render(Image(StreamImage($"avatar:{user.Id}", () => store.OpenAvatar(user.Id))));
        Assert.Same(source, image.Source);
        Assert.Equal(0, store.Opened);
    }
}
