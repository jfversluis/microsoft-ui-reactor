extern alias MauiControls;

using System.Collections.Generic;
using System.Linq;
using Stock = MauiControls::Microsoft.Maui.Controls;

namespace Microsoft.Maui.Controls
{
    // Same full names as the stock layouts. Each lists its views (direct ones included) as its visual children,
    // like round 5's ProjectedStackLayout, instead of its logical children.
    internal sealed class VerticalStackLayout : Stock.VerticalStackLayout, IVisualTreeElement
    {
        IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => this.OfType<IVisualTreeElement>().ToList();
        IVisualTreeElement? IVisualTreeElement.GetVisualParent() => Parent;
    }

    internal sealed class HorizontalStackLayout : Stock.HorizontalStackLayout, IVisualTreeElement
    {
        IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => this.OfType<IVisualTreeElement>().ToList();
        IVisualTreeElement? IVisualTreeElement.GetVisualParent() => Parent;
    }

    internal sealed class Grid : Stock.Grid, IVisualTreeElement
    {
        IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => this.OfType<IVisualTreeElement>().ToList();
        IVisualTreeElement? IVisualTreeElement.GetVisualParent() => Parent;
    }

    internal sealed class FlexLayout : Stock.FlexLayout, IVisualTreeElement
    {
        IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => this.OfType<IVisualTreeElement>().ToList();
        IVisualTreeElement? IVisualTreeElement.GetVisualParent() => Parent;
    }

    internal sealed class AbsoluteLayout : Stock.AbsoluteLayout, IVisualTreeElement
    {
        IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => this.OfType<IVisualTreeElement>().ToList();
        IVisualTreeElement? IVisualTreeElement.GetVisualParent() => Parent;
    }
}

namespace Reactor.Maui.DeviceTests.Shadow
{
    /// <summary>Creates the shadow layouts; callers see them as the stock types.</summary>
    public static class ShadowLayouts
    {
        public static Stock.Layout Create(string kind) => kind switch
        {
            "VerticalStackLayout" => new global::Microsoft.Maui.Controls.VerticalStackLayout(),
            "HorizontalStackLayout" => new global::Microsoft.Maui.Controls.HorizontalStackLayout(),
            "Grid" => new global::Microsoft.Maui.Controls.Grid(),
            "FlexLayout" => new global::Microsoft.Maui.Controls.FlexLayout(),
            "AbsoluteLayout" => new global::Microsoft.Maui.Controls.AbsoluteLayout(),
            _ => throw new System.ArgumentOutOfRangeException(nameof(kind)),
        };

        /// <summary>Whether <paramref name="layout"/> is a shadow (same full name, different type).</summary>
        public static bool IsShadow(object layout) => layout.GetType().Assembly == typeof(ShadowLayouts).Assembly;
    }
}
