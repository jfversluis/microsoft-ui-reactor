using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// The provider stack as it stood when the reconciler last reconciled a virtualized
/// host (LazyVStack, ItemsView, ListView, TreeView, …). Those hosts realize rows from
/// WinUI callbacks — <c>IElementFactory.GetElement</c>, <c>ContainerContentChanging</c> —
/// that run during layout, outside the traversal that maintains <see cref="ContextScope"/>,
/// so the row mount replays this snapshot instead of reading whatever the live stack
/// holds at that moment.
/// </summary>
internal sealed class ContextCapture
{
    internal (ContextBase Context, object? Value)[] Entries { get; private set; } = [];

    /// <summary>
    /// Type of the element whose handler registered this capture, or null when it was
    /// registered outside element dispatch (then any element sharing the control may
    /// refresh it). Decorators and transparent wrappers that share the host's control
    /// are a different type, so they do not overwrite the snapshot at the wrong depth.
    /// </summary>
    internal Type? OwnerType { get; init; }

    internal bool IsOwnedBy(Element element) => OwnerType is null || OwnerType == element.GetType();

    /// <summary><see cref="ContextScope.SnapshotGeneration"/> when <see cref="Entries"/> was taken.</summary>
    internal long Generation { get; private set; } = long.MinValue;

    internal void Refresh(ContextScope scope)
    {
        if (!scope.Matches(Entries))
            Entries = scope.Snapshot();
        Generation = scope.SnapshotGeneration;
    }

    /// <summary>Reconcile pass in which the host was last reached (see <see cref="PassTheme"/>).</summary>
    internal long PassId { get; private set; } = -1;

    /// <summary>
    /// The subtree theme the host was reached with in pass <see cref="PassId"/>. Only
    /// meaningful while that pass is still in flight: afterwards the host's native
    /// <c>ActualTheme</c> is authoritative and is read live instead.
    /// </summary>
    internal ElementTheme PassTheme { get; private set; }

    /// <summary>
    /// The reconciler's theme generation when the host was last reached. An ancestor's
    /// <c>RequestedTheme</c> change advances it, so a skipped host is detected as needing
    /// to re-record its subtree theme (see <see cref="PassTheme"/>).
    /// </summary>
    internal long ThemeGeneration { get; private set; } = long.MinValue;

    internal void NotePass(long passId, ElementTheme theme, long themeGeneration)
    {
        PassId = passId;
        PassTheme = theme;
        ThemeGeneration = themeGeneration;
    }

    /// <summary>
    /// Releases the snapshot's provider values. The capture object can be aliased (an
    /// <see cref="ElementFactory{T}"/> holds the same instance), so a host leaving Reactor's
    /// ownership empties it rather than only dropping its own reference.
    /// </summary>
    internal void Clear()
    {
        Entries = [];
        Generation = long.MinValue;
    }

    /// <param name="scope">The live stack at an ancestor of (or at) the host.</param>
    internal bool IsStale(ContextScope scope)
        => Generation != scope.SnapshotGeneration || !scope.IsPrefixOf(Entries);
}
