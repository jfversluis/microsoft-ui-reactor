using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;

namespace Microsoft.UI.Reactor.Maui;

/// <summary>Scrolling to an element named by a ref (spec 070 §2.6.1).</summary>
public static class ScrollViewExtensions
{
    private static readonly Task<bool> NotRequested = Task.FromResult(false);

    /// <summary>
    /// Scrolls <paramref name="scrollView"/> so the view <paramref name="target"/> names is at
    /// <paramref name="position"/>. Works for Controls and direct views on both renderers; MAUI's own
    /// <c>ScrollToAsync(Element, …)</c> is correct only when every layout between the target and the
    /// scroll view is a Controls layout.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> once the scroll finished at the requested position (or as far toward it as
    /// the content allows). <see langword="false"/> when nothing was requested (the ref is empty, a view
    /// between the target and the scroll view isn't laid out yet, or the orientation is
    /// <c>Neither</c>), or the scroll wasn't confirmed there: a later request superseded it, the scroll
    /// view went away, or the scroll ended elsewhere. <see langword="false"/> doesn't mean it didn't move.
    /// </returns>
    /// <exception cref="InvalidOperationException">The target isn't inside <paramref name="scrollView"/>.</exception>
    public static Task<bool> ScrollToAsync<T>(this ScrollView scrollView, ElementRef<T> target, ScrollToPosition position, bool animated)
        where T : class, IView
    {
        ArgumentNullException.ThrowIfNull(scrollView);
        ArgumentNullException.ThrowIfNull(target);
        if (!Enum.IsDefined(position)) throw new ArgumentException("position is not a valid ScrollToPosition", nameof(position));
        if (scrollView.Orientation == ScrollOrientation.Neither || target.Current is not IView view) return NotRequested;

        var chain = Chain(scrollView, view, out var allControls);
        if (!IsReady(scrollView, chain)) return NotRequested;

        Point point;
        Func<Task> dispatch;
        if (allControls)
        {
            // MAUI's own overload: matches MAUI by construction, insets included.
            var element = (VisualElement)view;
            point = scrollView.GetScrollPositionForElement(element, position);
            dispatch = () => scrollView.ScrollToAsync(element, position, animated);
        }
        else
        {
            var bounds = Bounds(chain);
            // MAUI 11's GetScrollPositionForElement applies the viewport and content-coordinate insets,
            // from internal providers. A parentless stand-in at the target's rectangle makes it run on
            // these coordinates: GetCoordinate of a parentless element is its own X and Y.
            var standIn = new BoxView();
            ((IView)standIn).Arrange(bounds);
            point = scrollView.GetScrollPositionForElement(standIn, position);
            dispatch = () => scrollView.ScrollToAsync(point.X, point.Y, animated);
        }

        return ScrollBridge.For(scrollView).Start(point, dispatch);
    }

    /// <summary>
    /// The visual chain from the target up to the scroll view's content, inclusive. Throws when the
    /// target isn't inside the scroll view.
    /// </summary>
    internal static List<IView> Chain(ScrollView scrollView, IView target, out bool allControls)
    {
        var chain = new List<IView>();
        var content = (IView?)scrollView.Content;
        for (IView? current = target; current is not null; current = VisualParent(current))
        {
            chain.Add(current);
            if (!ReferenceEquals(current, content)) continue;

            // Controls all the way: every view is a Controls view whose Controls parent is the next one
            // in the chain (so MAUI's own RealParent walk sees the same chain).
            allControls = true;
            for (var i = 0; i < chain.Count; i++)
            {
                var parent = i + 1 < chain.Count ? (object)chain[i + 1] : scrollView;
                if (chain[i] is not VisualElement visual || !ReferenceEquals(visual.Parent, parent)) allControls = false;
            }
            return chain;
        }
        throw new InvalidOperationException(
            $"The target {target.GetType().Name} isn't inside this ScrollView ({scrollView.GetType().Name}).");
    }

    private static IView? VisualParent(IView view) => view switch
    {
        DirectView direct => direct.Parent as IView,
        _ when DirectLayout.VisualParentOf(view) is { } layout => layout,
        Microsoft.Maui.Controls.Element { Parent: IView parent } => parent,
        _ => null,
    };

    /// <summary>Whether the scroll view and every view in the chain has a handler and has been arranged.</summary>
    internal static bool IsReady(ScrollView scrollView, List<IView> chain)
    {
        if (scrollView.Handler is null || scrollView.Width < 0 || scrollView.Height < 0) return false;
        foreach (var view in chain)
        {
            if (view.Handler is null) return false;
            var arranged = view switch
            {
                DirectView direct => direct.IsArranged,
                VisualElement visual => visual.Width >= 0 && visual.Height >= 0,
                _ => false,
            };
            if (!arranged) return false;
        }
        return true;
    }

    /// <summary>The target's rectangle in the coordinates MAUI's <c>GetCoordinate</c> produces: frame origins summed up to and including the content.</summary>
    internal static Rect Bounds(List<IView> chain)
    {
        double x = 0, y = 0;
        foreach (var view in chain)
        {
            x += view.Frame.X;
            y += view.Frame.Y;
        }
        return new Rect(x, y, chain[0].Frame.Width, chain[0].Frame.Height);
    }
}
