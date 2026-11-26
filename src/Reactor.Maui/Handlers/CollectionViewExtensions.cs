using Microsoft.UI.Reactor.Maui.Handlers;

namespace Microsoft.UI.Reactor.Maui;

/// <summary>Commands for a <c>CollectionView</c> reached through a ref (spec 070 §3.4).</summary>
public static class CollectionViewExtensions
{
    /// <summary>
    /// Scrolls to the item with <paramref name="key"/> (in any group). MAUI's <c>ScrollTo(item, …)</c>
    /// can't take the app's item, because Reactor's items source holds its own boxes. Returns
    /// <see langword="false"/> when no item has the key.
    /// </summary>
    /// <exception cref="InvalidOperationException">The list wasn't rendered by Reactor.</exception>
    public static bool ScrollToKey(this CollectionView view, string key, ScrollToPosition position = ScrollToPosition.MakeVisible, bool animate = true)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view is not ReactorCollectionView reactor)
            throw new InvalidOperationException("ScrollToKey needs a CollectionView rendered by Reactor; call ScrollTo on other lists.");
        if (reactor.Host.FindWithGroup(key) is not { } found) return false;
        view.ScrollTo(found.Box, found.Group, position, animate);
        return true;
    }
}
