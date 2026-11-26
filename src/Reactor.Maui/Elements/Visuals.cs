using Microsoft.Maui.Controls.Shapes;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Maui;

// ── Shapes (spec 070 §4.3) ──────────────────────────────────────────────────

/// <summary>
/// What every shape has: a fill, a stroke and its style, and how the geometry stretches into the
/// shape's box (<see cref="Aspect"/>). Unset (<c>null</c>) values keep MAUI's defaults; a value removed
/// on a later render is cleared back to the default. Brushes are compared by value and are immutable
/// once rendered: render a new brush to change one.
/// </summary>
public abstract record ShapeElement : ViewElement
{
    public Brush? Fill { get; init; }
    public Brush? Stroke { get; init; }
    public double? StrokeThickness { get; init; }

    /// <summary>Dash and gap lengths, in multiples of the stroke thickness. Copied when set, so changing the array afterwards changes nothing.</summary>
    public IReadOnlyList<double>? StrokeDashArray { get; init => field = ValueList.Copy(value); }

    public double? StrokeDashOffset { get; init; }
    public PenLineCap? StrokeLineCap { get; init; }
    public PenLineJoin? StrokeLineJoin { get; init; }
    public double? StrokeMiterLimit { get; init; }

    /// <summary>How the geometry stretches into the shape's box (MAUI's <c>Aspect</c>, a <see cref="Stretch"/>).</summary>
    public Stretch? Aspect { get; init; }
}

/// <summary>A rectangle filling its box, with optionally rounded corners (<see cref="RadiusX"/>, <see cref="RadiusY"/>).</summary>
public sealed record RectangleElement : ShapeElement
{
    public double? RadiusX { get; init; }
    public double? RadiusY { get; init; }
}

/// <summary>An ellipse filling its box.</summary>
public sealed record EllipseElement : ShapeElement;

/// <summary>A rectangle with a radius per corner, filling its box.</summary>
public sealed record RoundRectangleElement(CornerRadius CornerRadius) : ShapeElement;

/// <summary>A line from (<see cref="X1"/>, <see cref="Y1"/>) to (<see cref="X2"/>, <see cref="Y2"/>).</summary>
public sealed record LineElement : ShapeElement
{
    public double X1 { get; init; }
    public double Y1 { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }
}

/// <summary>A closed shape through <see cref="Points"/>.</summary>
public sealed record PolygonElement : ShapeElement
{
    /// <summary>The vertices. Copied when set, so changing the array afterwards changes nothing.</summary>
    public IReadOnlyList<Point> Points { get; init => field = ValueList.Copy(value) ?? []; } = [];

    public FillRule? FillRule { get; init; }
}

/// <summary>An open line through <see cref="Points"/>.</summary>
public sealed record PolylineElement : ShapeElement
{
    /// <summary>The vertices. Copied when set, so changing the array afterwards changes nothing.</summary>
    public IReadOnlyList<Point> Points { get; init => field = ValueList.Copy(value) ?? []; } = [];

    public FillRule? FillRule { get; init; }
}

/// <summary>
/// A path (WinUI's <c>Path2D</c>). <see cref="PathDataString"/> is path markup, converted only when it
/// changes; it wins over <see cref="Data"/>, which is used when the markup is rejected. <see cref="Data"/>
/// and <see cref="RenderTransform"/> are MAUI objects compared by reference: create them once.
/// </summary>
public sealed record PathElement : ShapeElement
{
    public string? PathDataString { get; init; }
    public Geometry? Data { get; init; }
    public Transform? RenderTransform { get; init; }
}

// ── Containers ──────────────────────────────────────────────────────────────

/// <summary>A plain container for one child, with optional padding (MAUI's <c>ContentView</c>).</summary>
public sealed record ContentViewElement(Element? Content) : ViewElement
{
    public Thickness? Padding { get; init; }
}

/// <summary>Immutable copies of the lists elements hold, so a declaration can't change after it is made.</summary>
internal static class ValueList
{
    public static IReadOnlyList<T>? Copy<T>(IReadOnlyList<T>? value) => value switch
    {
        null => null,
        { Count: 0 } => [],
        ValueList<T> owned => owned,
        _ => new ValueList<T>([.. value]),
    };
}

/// <summary>A list nobody else holds the storage of.</summary>
internal sealed class ValueList<T>(T[] items) : IReadOnlyList<T>
{
    public T this[int index] => items[index];
    public int Count => items.Length;
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)items).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => items.GetEnumerator();
}
