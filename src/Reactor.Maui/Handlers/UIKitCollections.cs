#if IOS || MACCATALYST
namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>
/// The UIKit collection view under a MAUI items view, and the barrier behind its pending batch updates;
/// shared by the selection verifier (spec 070 §3.4) and the carousel port (§5.4).
/// </summary>
internal static class UIKitCollections
{
    /// <summary>The first <c>UICollectionView</c> in <paramref name="view"/>'s platform view, or <see langword="null"/>.</summary>
    public static UIKit.UICollectionView? Find(Microsoft.Maui.Controls.View view)
    {
        if (view.Handler?.PlatformView is not UIKit.UIView root) return null;
        var pending = new Stack<UIKit.UIView>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (next is UIKit.UICollectionView found) return found;
            foreach (var child in next.Subviews) pending.Push(child);
        }
        return null;
    }

    /// <summary>
    /// Runs <paramref name="completion"/> after the batch updates MAUI has queued on the collection view
    /// (an empty <c>PerformBatchUpdates</c>, ordered behind them); <see langword="false"/> without a native view.
    /// </summary>
    public static bool Barrier(Microsoft.Maui.Controls.View view, Action completion)
    {
        if (Find(view) is not { } native) return false;
        native.PerformBatchUpdates(null, _ => completion());
        return true;
    }
}
#endif
