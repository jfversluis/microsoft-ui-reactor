namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// A renderer's view of one container's live native children, as the child reconciler edits them.
/// WinUI wraps a panel's <c>UIElementCollection</c>; MAUI wraps a layout's child list.
/// </summary>
/// <typeparam name="TNative">The renderer's native control type.</typeparam>
internal interface IChildList<TNative> where TNative : class
{
    int Count { get; }
    TNative Get(int index);
    void Insert(int index, TNative element);
    void RemoveAt(int index);

    /// <summary>Moves the child at <paramref name="oldIndex"/> so it ends up at <paramref name="newIndex"/>.</summary>
    void Move(int oldIndex, int newIndex);

    void Replace(int index, TNative element);

    int IndexOf(TNative element)
    {
        for (int i = 0; i < Count; i++)
        {
            if (ReferenceEquals(Get(i), element))
                return i;
        }
        return -1;
    }
}

/// <summary>
/// The per-child half of what the framework-neutral child reconciler (spec 067) needs from a
/// renderer: mounting, updating and unmounting children, and the renderer's skip, transition and
/// animation policy. Implemented by a <c>struct</c> passed through a generic constraint.
/// </summary>
/// <remarks>
/// Deliberately non-generic, typed <see cref="ReactorElement"/> and <see cref="object"/>. The
/// engine's element and native types are reference types, so the JIT compiles one shared body for
/// every renderer, and a call through an interface generic over them can't be bound to the host's
/// method at compile time: it becomes a runtime-resolved indirect call that is never inlined. These
/// run several times per child on every pass, so through this interface they bind directly to the
/// host struct and inline. The engine only ever passes a host its own elements and natives, so an
/// implementation may cast them back with <c>Unsafe.As</c>; methods returning a native return one
/// of the renderer's own.
/// </remarks>
internal interface IChildReconcileHost
{
    // ── Mount / update / unmount ───────────────────────────────────────────

    /// <summary>Mounts <paramref name="element"/>, or returns null when it produces no control.</summary>
    object? Mount(ReactorElement element, Action requestRerender);

    /// <summary>
    /// Updates <paramref name="control"/> from <paramref name="oldElement"/> to
    /// <paramref name="newElement"/>. Returns a replacement control when the update had to remount,
    /// with <paramref name="unmountedByUpdate"/> telling whether the old control was already unmounted.
    /// </summary>
    object? UpdateChildTracked(ReactorElement oldElement, ReactorElement newElement, object control, Action requestRerender, out bool unmountedByUpdate);

    void UnmountChild(object control);

    /// <summary>Whether <paramref name="newElement"/> can update the control <paramref name="oldElement"/> mounted.</summary>
    bool CanUpdate(ReactorElement oldElement, ReactorElement newElement);

    // ── Skip policy ────────────────────────────────────────────────────────

    /// <summary>Whether an unchanged child may skip its update entirely.</summary>
    bool CanSkipUpdate(ReactorElement oldElement, ReactorElement newElement);

    /// <summary>Hot reload is forcing a render through every wrapper (no structural skip).</summary>
    bool ForceFullRenderActive { get; }

    /// <summary>Whether <paramref name="newElement"/> must render even though it is unchanged.</summary>
    bool ForceRenderThroughWrapper(ReactorElement newElement);

    /// <summary>Whether context values are active, so a skip has to ask <see cref="ShouldDeclineSkip"/>.</summary>
    bool HasActiveContextValues { get; }

    bool ShouldDeclineSkip(object control);

    /// <summary>
    /// Whether a skipped child still needs <see cref="RefreshSkippedChildState"/> (for example to
    /// point its event trampolines at the new render's callbacks).
    /// </summary>
    bool NeedsRefreshOnSkip(ReactorElement oldElement, ReactorElement newElement);

    void RefreshSkippedChildState(object control, ReactorElement oldElement, ReactorElement newElement);

    /// <summary>Diagnostics: <paramref name="count"/> children were skipped.</summary>
    void CountSkipped(int count);

    // ── Exit transitions ───────────────────────────────────────────────────

    /// <summary>Whether any control anywhere is playing an exit transition.</summary>
    bool HasExitingControls { get; }

    bool IsExiting(object control);

    // ── Keys ───────────────────────────────────────────────────────────────

    /// <summary>The key of the element <paramref name="control"/> renders, when the renderer tracks it.</summary>
    bool TryGetMountedKey(object control, out string? key);

    // ── Filtering ──────────────────────────────────────────────────────────

    /// <summary>Whether <paramref name="element"/> renders nothing and is dropped from the child list.</summary>
    bool IsEmpty(ReactorElement element);

    // ── Ambient animation (spec 042 §6) ────────────────────────────────────

    /// <summary>Whether an ambient animation transaction is active for this reconcile.</summary>
    bool HasAmbientAnimation { get; }

    /// <summary>A child was mounted and inserted under an ambient transaction.</summary>
    void OnInsertedUnderAmbient(object control, ReactorElement element);

    /// <summary>A child was moved under an ambient transaction.</summary>
    void OnMovedUnderAmbient(object control);
}

/// <summary>
/// The per-pass half of the host contract: the calls that take the renderer's child list or
/// element arrays. Each runs once per reconcile (or once per structural change), so the runtime
/// lookup a generic interface costs in the engine's shared code doesn't matter here.
/// </summary>
/// <typeparam name="TElement">The renderer's element base type.</typeparam>
/// <typeparam name="TNative">The renderer's native control type.</typeparam>
internal interface IChildReconcileHost<TElement, TNative> : IChildReconcileHost, IDirtyPathProbe<TNative>
    where TElement : ReactorElement
    where TNative : class
{
    /// <summary>
    /// A memoizing producer's structural hint (spec 034 §C): when it names exactly which indices of
    /// <paramref name="newChildren"/> differ from <paramref name="oldChildren"/> and every other index
    /// is reference-equal and needs no re-resolution, returns those indices.
    /// </summary>
    bool TryGetChangedIndices(TElement[] oldChildren, TElement[] newChildren, out int[] changedIndices);

    // ── Pending work below unchanged children ──────────────────────────────

    DirtyChildIndices ResolveDirtyChildIndices(TNative? parentControl, IChildList<TNative> children);

    // ── Exit transitions ───────────────────────────────────────────────────

    bool HoldsExitingControlIn(IChildList<TNative> children);

    void RemoveChildWithExitTransition(IChildList<TNative> children, int index);

    /// <summary>
    /// Replaces the child at <paramref name="index"/> with <paramref name="newControl"/>. Returns
    /// true when an exit transition re-inserted the old control right after it (the list grew by one).
    /// </summary>
    bool ReplaceChildWithExitTransition(IChildList<TNative> children, int index, TNative newControl);

    // ── Keys ───────────────────────────────────────────────────────────────

    void ReportDuplicateSiblingKeys(TElement[] children, TNative? parentControl);
}

/// <summary>Whether a control lies on the path to a component that updated its own state.</summary>
internal interface IDirtyPathProbe<TNative> where TNative : class
{
    bool IsOnDirtyAncestorPath(TNative control);
}

/// <summary>
/// The indices of a container's children that lead to a component which updated its own state,
/// resolved once per container reconcile. The child-skip arms of the reconciler must descend into
/// these even when their elements are unchanged. <c>default</c> is the common case: no such child.
/// </summary>
internal readonly struct DirtyChildIndices
{
    // The single dirty index plus one, so that default(DirtyChildIndices) names no index.
    private readonly int _indexPlusOne;
    private readonly int[]? _indices;
    private readonly bool _probeEachChild;

    private DirtyChildIndices(int indexPlusOne, int[]? indices, bool probeEachChild)
    {
        _indexPlusOne = indexPlusOne;
        _indices = indices;
        _probeEachChild = probeEachChild;
    }

    /// <summary>
    /// The dirty children could not be located by index, so each skip-eligible child's control
    /// has to be checked against the dirty-ancestor path instead.
    /// </summary>
    internal static DirtyChildIndices ProbeEachChild => new(0, null, probeEachChild: true);

    internal static DirtyChildIndices At(int index) => new(index + 1, null, probeEachChild: false);

    /// <summary>Several dirty children. Sorts <paramref name="indices"/> in place so lookups can binary-search them.</summary>
    internal static DirtyChildIndices AtAll(int[] indices)
    {
        Array.Sort(indices);
        return new(0, indices, probeEachChild: false);
    }

    /// <summary>True when no child of the container leads to pending work.</summary>
    internal bool IsEmpty => _indexPlusOne == 0 && _indices is null && !_probeEachChild;

    /// <summary>
    /// Whether the child at <paramref name="panelIndex"/> leads to a self-triggered component.
    /// Only <see cref="ProbeEachChild"/> reads the child's control; it stores it in
    /// <paramref name="control"/> so the caller does not read it twice.
    /// </summary>
    internal bool Contains<TNative, TProbe>(int panelIndex, IChildList<TNative> children, TProbe probe, ref TNative? control)
        where TNative : class
        where TProbe : IDirtyPathProbe<TNative>
        => Contains(panelIndex, panelIndex, children, probe, ref control);

    /// <summary>
    /// As the overload above, for a child whose index when this was resolved
    /// (<paramref name="resolvedIndex"/>) differs from where its control sits now
    /// (<paramref name="liveIndex"/>).
    /// </summary>
    internal bool Contains<TNative, TProbe>(int resolvedIndex, int liveIndex, IChildList<TNative> children, TProbe probe, ref TNative? control)
        where TNative : class
        where TProbe : IDirtyPathProbe<TNative>
    {
        if (_indexPlusOne != 0)
            return resolvedIndex == _indexPlusOne - 1;
        if (_indices is not null)
            return Array.BinarySearch(_indices, resolvedIndex) >= 0;
        if (!_probeEachChild || (uint)liveIndex >= (uint)children.Count)
            return false;
        return probe.IsOnDirtyAncestorPath(control ??= children.Get(liveIndex));
    }

    public override string ToString() =>
        _indexPlusOne != 0 ? $"At({_indexPlusOne - 1})"
        : _indices is not null ? $"AtAll({string.Join(",", _indices)})"
        : _probeEachChild ? nameof(ProbeEachChild)
        : "None";
}
