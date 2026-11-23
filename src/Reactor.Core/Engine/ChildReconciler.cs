using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Microsoft.UI.Reactor.Core;

// AI-HINT: React-like child list diffing. Two strategies:
//   Unkeyed: O(n) positional match by index — simple but can't detect reorders.
//   Keyed:   4-phase algorithm — (1) common prefix, (2) common suffix,
//            (3) pure insert/remove middle, (4) LIS-based minimal moves.
//   LIS finds the longest subsequence of old children that are already in order
//   in the new list; only children NOT in the LIS need to be moved.
//   Everything renderer-specific (mounting, skip policy, exit transitions, ambient
//   animation) goes through THost, a struct the JIT specializes the engine for.

/// <summary>
/// The framework-neutral child reconciler (spec 067): diffs a container's old and new child
/// elements and edits its live native children to match, with keyed reconciliation by
/// longest increasing subsequence (LIS).
///
/// Strategies:
///   1. Unkeyed children: positional reconciliation (match by index), or by slot when a
///      conditional child shifted indices
///   2. Keyed children: prefix/suffix stripping + LIS for minimal moves
/// </summary>
/// <typeparam name="TElement">The renderer's element base type.</typeparam>
/// <typeparam name="TNative">The renderer's native control type.</typeparam>
/// <typeparam name="THost">The renderer's host; a struct, so calls into it are direct.</typeparam>
internal static class ChildReconciler<TElement, TNative, THost>
    where TElement : ReactorElement
    where TNative : class
    where THost : struct, IChildReconcileHost<TElement, TNative>
{
    // The host returns natives as object (IChildReconcileHost's remarks say why); a host only
    // returns its own natives, so these convert back without a runtime type check.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TNative? MountVia(in THost host, TElement element, Action requestRerender) =>
        Unsafe.As<TNative>(host.Mount(element, requestRerender));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TNative? UpdateVia(in THost host, TElement oldElement, TElement newElement, TNative control, Action requestRerender, out bool unmountedByUpdate) =>
        Unsafe.As<TNative>(host.UpdateChildTracked(oldElement, newElement, control, requestRerender, out unmountedByUpdate));

    /// <summary>
    /// Reconciles old and new child element arrays against a container's live children.
    /// </summary>
    // <snippet:child-diff>
    internal static void Reconcile(
        TElement[] oldChildren,
        TElement[] newChildren,
        IChildList<TNative> children,
        THost host,
        Action requestRerender,
        TNative? parentControl = null)
    {
        // Filter out nulls and empty elements, keeping each kept child's slot (ChildSlots)
        var oldFiltered = Filter(oldChildren, host, out var oldSlots);
        var newFiltered = Filter(newChildren, host, out var newSlots);

        bool hasKeys = ChildReconcilerShared.HasAnyKeys(oldFiltered) || ChildReconcilerShared.HasAnyKeys(newFiltered);

        // Duplicate sibling keys are matched to the wrong controls by the keyed path below;
        // report them (deduplicated per panel) rather than let that happen silently. Only when
        // the key sequence changed: an unchanged one was already checked when it first
        // appeared, here or at mount, so a stable keyed panel's re-render pays one compare
        // per child (the prefix walk below does the same) rather than a duplicate scan.
        if (hasKeys && newFiltered.Length > 1 && !ChildReconcilerShared.SameKeys(oldFiltered, newFiltered))
            host.ReportDuplicateSiblingKeys(newFiltered, parentControl);

        // Children that lead to a component which updated its own state. Their elements can be
        // unchanged — a reused instance, a memoized wrapper, a reused children array — and the
        // skip arms below must still descend into them, or the pass never reaches the
        // component. Resolved once here (default when nothing in the pass self-triggered).
        var dirty = host.ResolveDirtyChildIndices(parentControl, children);

        if (hasKeys)
            ReconcileKeyed(oldFiltered, newFiltered, children, host, requestRerender, dirty);
        // The slot walk also steps past controls an earlier pass left playing an exit
        // transition in this collection (they inflate its count); the index walk would pair
        // every later child with the wrong control.
        else if (!ChildSlots.AlignByIndex(oldSlots, oldFiltered.Length, newSlots, newFiltered.Length)
            || host.HoldsExitingControlIn(children))
            ReconcileSlotted(oldFiltered, oldSlots, newFiltered, newSlots, children, host, requestRerender, dirty);
        else
            ReconcilePositional(oldFiltered, newFiltered, children, host, requestRerender, parentControl, dirty);
    }
    // </snippet:child-diff>

    /// <summary>
    /// Unkeyed reconciliation when a conditional child (null / an empty element / a resized
    /// group) shifted indices between renders: children are matched by <see cref="ChildSlots">slot</see>,
    /// so the siblings after a toggled child are updated in place instead of each being compared
    /// with the child that used to sit at its index. Both arrays' slots increase, so this is one
    /// merge pass: O(old + new), with no reorder. It also steps past controls still in the
    /// collection only to play an exit transition, which belong to no old child.
    /// </summary>
    private static void ReconcileSlotted(
        TElement[] oldChildren,
        long[]? oldSlots,
        TElement[] newChildren,
        long[]? newSlots,
        IChildList<TNative> children,
        THost host,
        Action requestRerender,
        in DirtyChildIndices dirty,
        int start = 0,
        int startLive = 0)
    {
        // `start`/`startLive`: where the positional walk hands over after an exit transition
        // re-inserted a replaced control (the children before it are already reconciled).
        int oi = start, ni = start;
        // The live index the next old child sits at. It advances past every control the
        // collection holds for a child just handled — including an old control an exit
        // transition left in place (removal) or re-inserted after its replacement.
        int live = startLive;
        // The old child's index in the collection as it was when `dirty` was resolved: the
        // old children plus any control an earlier pass left exiting.
        int resolved = start;
        bool skipExiting = host.HoldsExitingControlIn(children);
        while (oi < oldChildren.Length || ni < newChildren.Length)
        {
            if (skipExiting && oi < oldChildren.Length)
            {
                while (live < children.Count && host.IsExiting(children.Get(live)))
                {
                    live++;
                    resolved++;
                }
            }
            long oldSlot = oi < oldChildren.Length ? ChildSlots.At(oldSlots, oi) : long.MaxValue;
            long newSlot = ni < newChildren.Length ? ChildSlots.At(newSlots, ni) : long.MaxValue;
            int before = children.Count;
            if (oldSlot < newSlot)
            {
                // Old child whose slot is gone.
                if (live < before)
                {
                    host.RemoveChildWithExitTransition(children, live);
                    live += 1 + (children.Count - before);
                }
                oi++;
                resolved++;
            }
            else if (oldSlot == newSlot && live < before)
            {
                UpdateChildAt(oldChildren[oi], newChildren[ni], resolved, live, children, host, requestRerender, dirty);
                live += 1 + (children.Count - before);
                oi++;
                resolved++;
                ni++;
            }
            else
            {
                // New slot — or a kept slot whose control the collection no longer holds
                // (the positional walk stops there too), which mounts afresh.
                if (oldSlot == newSlot)
                {
                    oi++;
                    resolved++;
                }
                var newEl = newChildren[ni];
                var ctrl = MountVia(in host, newEl, requestRerender);
                if (ctrl is not null)
                {
                    children.Insert(live, ctrl);
                    if (host.HasAmbientAnimation) host.OnInsertedUnderAmbient(ctrl, newEl);
                    live++;
                }
                ni++;
            }
        }
    }

    /// <summary>
    /// Positional reconciliation: match children by index.
    /// O(max(old, new)) — no reorder detection, but simple and fast.
    /// </summary>
    private static void ReconcilePositional(
        TElement[] oldChildren,
        TElement[] newChildren,
        IChildList<TNative> children,
        THost host,
        Action requestRerender,
        TNative? parentControl,
        in DirtyChildIndices dirty)
    {
        int childCount = children.Count; // cache to avoid repeated native calls
        int common = Math.Min(oldChildren.Length, newChildren.Length);

        // Spec 034 §C — structural skip of untouched child ranges. When a memoizing producer
        // published a hint naming the only indices that differ (by reference) from the previous
        // render, update just those and skip the O(count) walk. Required gates (each preserves
        // correctness):
        //   • counts match on both element sides AND the live collection equals that count (no
        //     in-flight exit/enter animation has inflated it), so every changed index maps 1:1
        //     to an existing control;
        //   • no animation ambient (insert / move / exit reshape the child list);
        //   • no hot-reload force pass is active — else an untouched, reference-equal wrapper
        //     cell must still re-render through the wrapper to pick up an edited method body;
        //   • the host vouches for the hint (TryGetChangedIndices): present for THIS array,
        //     diffed against THIS old array, and no cell needs re-resolution (e.g. theme);
        //   • no child of the container leads to a component that updated its own state: an
        //     untouched, reference-equal cell holding such a component must be descended into,
        //     and only the full walk does that.
        if (!host.HasAmbientAnimation
            && oldChildren.Length == newChildren.Length
            && childCount == newChildren.Length
            && !host.ForceFullRenderActive
            && dirty.IsEmpty
            && host.TryGetChangedIndices(oldChildren, newChildren, out var changed)
            && (parentControl is null || !host.ShouldDeclineSkip(parentControl)))
        {
            int visited = 0;
            for (int k = 0; k < changed.Length; k++)
            {
                int idx = changed[k];
                if ((uint)idx >= (uint)common) continue; // defensive against a bad hint
                if (UpdateCommonChild(idx, oldChildren, newChildren, children, host, requestRerender, dirty))
                {
                    ReconcileSlotted(oldChildren, null, newChildren, null, children, host, requestRerender,
                        dirty, start: idx + 1, startLive: idx + 2);
                    return;
                }
                visited++;
            }
            // Untouched indices are reference-equal and skipped wholesale. Base the
            // skipped-element diagnostic on indices ACTUALLY visited (not the raw hint
            // length) so a defensively-ignored out-of-range index can't skew it or drive it
            // negative. The untouched cells are reference-equal old↔new, so no skipped-child
            // refresh is needed for them; only the changed indices can carry a new closure.
            host.CountSkipped(common - visited);
            return;
        }

        // Update common children in place
        for (int i = 0; i < common; i++)
        {
            if (i >= childCount) break;
            if (UpdateCommonChild(i, oldChildren, newChildren, children, host, requestRerender, dirty))
            {
                // An exit transition re-inserted the replaced control right after its
                // replacement, so every later child's control moved one index: finish with the
                // slot walk, which steps past it.
                ReconcileSlotted(oldChildren, null, newChildren, null, children, host, requestRerender,
                    dirty, start: i + 1, startLive: i + 2);
                return;
            }
        }

        // Remove excess old children (from end to start to keep indices stable). Use the live
        // children.Count — NOT the cached childCount — because ReplaceChildWithExitTransition in
        // the common loop may have re-inserted elements for exit animation.
        for (int i = children.Count - 1; i >= common; i--)
        {
            host.RemoveChildWithExitTransition(children, i);
        }

        // Insert new children beyond old count
        for (int i = common; i < newChildren.Length; i++)
        {
            var ctrl = MountVia(in host, newChildren[i], requestRerender);
            if (ctrl is not null)
            {
                children.Insert(children.Count, ctrl);
                if (host.HasAmbientAnimation) host.OnInsertedUnderAmbient(ctrl, newChildren[i]);
            }
        }
    }

    /// <summary>
    /// Reconciles a single common (overlapping) child position in place. Shared by the full
    /// positional walk and the structural-skip fast path so both honour identical skip / update /
    /// type-mismatch semantics.
    /// </summary>
    private static bool UpdateCommonChild(
        int i,
        TElement[] oldChildren,
        TElement[] newChildren,
        IChildList<TNative> children,
        THost host,
        Action requestRerender,
        in DirtyChildIndices dirty)
        => UpdateChildAt(oldChildren[i], newChildren[i], i, i, children, host, requestRerender, dirty);

    /// <summary>
    /// Reconciles one matched old/new child pair whose control sits at <paramref name="i"/> in
    /// <paramref name="children"/>. <paramref name="dirtyIndex"/> is the old child's index when
    /// <paramref name="dirty"/> was resolved — the same as <paramref name="i"/> except in the
    /// slot-matched walk, which inserts and removes around it. Returns true when an exit transition
    /// re-inserted the replaced control right after its replacement, so the collection grew by one.
    /// </summary>
    private static bool UpdateChildAt(
        TElement oldEl,
        TElement newEl,
        int dirtyIndex,
        int i,
        IChildList<TNative> children,
        THost host,
        Action requestRerender,
        in DirtyChildIndices dirty)
    {
        // Early skip: if the element is structurally identical (and the host's skip contract
        // allows it), avoid reading the native child at all. On WinUI that saves the COM
        // round-trips of IVector.GetAt plus the property diff inside Update. A child that leads
        // to a self-triggered component (`dirty`) declines the skip, so the pass can descend
        // into it; membership is an integer compare, and empty when nothing self-triggered.
        TNative? existingControl = null;
        if (host.CanSkipUpdate(oldEl, newEl)
            && !host.ForceRenderThroughWrapper(newEl)
            && (dirty.IsEmpty || !dirty.Contains(dirtyIndex, i, children, host, ref existingControl)))
        {
            if (!host.HasActiveContextValues
                || !host.ShouldDeclineSkip(existingControl ??= children.Get(i)))
            {
                host.CountSkipped(1);
                // A skipped child can still need its native state refreshed (for example its
                // event trampolines pointed at the new render's callbacks). Fetch the control
                // only then.
                if (host.NeedsRefreshOnSkip(oldEl, newEl))
                    host.RefreshSkippedChildState(existingControl ??= children.Get(i), oldEl, newEl);
                return false;
            }
        }

        if (host.CanUpdate(oldEl, newEl))
        {
            existingControl ??= children.Get(i);
            var replacement = UpdateVia(in host, oldEl, newEl, existingControl, requestRerender, out var unmountedByUpdate);
            if (replacement is not null)
            {
                // Child type changed at runtime — replace in place
                if (!unmountedByUpdate) host.UnmountChild(existingControl);
                children.Replace(i, replacement);
            }
            return false;
        }

        // Type mismatch — mount new, replace old with exit transition support
        var newControl = MountVia(in host, newEl, requestRerender);
        if (newControl is not null)
            return host.ReplaceChildWithExitTransition(children, i, newControl);
        host.UnmountChild(children.Get(i));
        return false;
    }

    /// <summary>
    /// Keyed reconciliation using prefix/suffix stripping + LIS.
    /// Minimizes native operations for reordered lists.
    /// </summary>
    private static void ReconcileKeyed(
        TElement[] oldChildren,
        TElement[] newChildren,
        IChildList<TNative> children,
        THost host,
        Action requestRerender,
        in DirtyChildIndices dirty)
    {
        var map = new KeyedPanelMap(oldChildren, children, host);
        try
        {
            ReconcileKeyedCore(oldChildren, newChildren, children, host, requestRerender, dirty, ref map);
        }
        finally
        {
            map.Release();
        }
    }

    /// <summary>
    /// Old logical child index → physical index in the collection, for the keyed pass. Logical
    /// children can include an unrealized transparent child and exclude retained exits; when
    /// neither is present the two coincide and the map reads nothing. Otherwise a forward cursor
    /// resolves it on demand, reading each physical child at most once and keeping the control
    /// each slot claimed (<see cref="ControlAt"/>), so the prefix and suffix walks read no more
    /// controls than the identity path does. A pass whose children all skip without touching the
    /// collection never consults the map, so it neither advances the cursor nor probes for exits.
    /// Resolving ahead of a mutation is sound: the prefix and suffix walks only Replace
    /// (count-neutral) a slot the cursor has already passed, after which that slot is never
    /// looked up again, and every count-changing mutation follows <see cref="Resolve"/>.
    /// </summary>
    private struct KeyedPanelMap
    {
        private readonly TElement[] _oldChildren;
        private readonly IChildList<TNative> _children;
        private readonly THost _host;
        private int[]? _map;
        // The control each resolved slot claimed (null for an absent slot).
        private TNative?[]? _controls;
        private Dictionary<string, int>? _siblingKeys;
        // Logical children mapped so far; the next physical index to read; the control at
        // that index when it has been read but not claimed.
        private int _resolved;
        private int _physical;
        private TNative? _pending;
        // 0 = not yet decided, 1 = identity, 2 = mapped.
        private byte _identity;

        /// <summary>The collection's count when the pass began; also the absent-slot sentinel.</summary>
        internal readonly int ChildCount;

        internal KeyedPanelMap(TElement[] oldChildren, IChildList<TNative> children, THost host)
        {
            _oldChildren = oldChildren;
            _children = children;
            _host = host;
            _map = null;
            _controls = null;
            _siblingKeys = null;
            _resolved = 0;
            _physical = 0;
            _pending = null;
            ChildCount = children.Count;
            _identity = ChildCount == oldChildren.Length ? (byte)0 : (byte)2;
        }

        /// <summary>
        /// Logical and physical indices coincide. Decided on first use, before the pass
        /// mutates the collection, so a pass that never needs an index never probes for exits.
        /// </summary>
        private bool IsIdentity
        {
            get
            {
                if (_identity == 0)
                    _identity = _host.HoldsExitingControlIn(_children) ? (byte)2 : (byte)1;
                return _identity == 1;
            }
        }

        internal int At(int oldIndex)
        {
            if (IsIdentity) return oldIndex;
            if (oldIndex >= _resolved) ResolveThrough(oldIndex + 1);
            return _map![oldIndex];
        }

        /// <summary>
        /// The control the cursor read for old child <paramref name="oldIndex"/> when it mapped
        /// it, or <see langword="null"/> when it read none (identity map, or an absent slot).
        /// </summary>
        internal TNative? MappedControl(int oldIndex)
        {
            At(oldIndex);
            return _controls?[oldIndex];
        }

        /// <summary>
        /// The realized control of old child <paramref name="oldIndex"/>, reusing the read the
        /// cursor made to map it. Only for a child whose <see cref="At"/> is in range.
        /// </summary>
        internal TNative ControlAt(int oldIndex) =>
            MappedControl(oldIndex) ?? _children.Get(At(oldIndex));

        /// <summary>The full map, or <see langword="null"/> when indices coincide.</summary>
        internal int[]? Resolve()
        {
            if (IsIdentity) return null;
            ResolveThrough(_oldChildren.Length);
            return _map;
        }

        internal void Release()
        {
            if (_siblingKeys is not null) ChildReconcilerShared.ReturnKeyIndexDict(_siblingKeys);
            if (_map is not null) ArrayPool<int>.Shared.Return(_map);
            // Cleared on return so the pool does not root the controls.
            if (_controls is not null) ArrayPool<TNative?>.Shared.Return(_controls, clearArray: true);
            _siblingKeys = null;
            _map = null;
            _controls = null;
            _pending = null;
        }

        private void ResolveThrough(int end)
        {
            var oldChildren = _oldChildren;
            if (_map is null)
            {
                _map = ArrayPool<int>.Shared.Rent(oldChildren.Length);
                _controls = ArrayPool<TNative?>.Shared.Rent(oldChildren.Length);
                _siblingKeys = ChildReconcilerShared.RentKeyIndexDict(oldChildren.Length);
                for (int i = 0; i < oldChildren.Length; i++)
                    if (oldChildren[i].Key is { } siblingKey)
                        _siblingKeys[siblingKey] = i;
            }

            // Keyed controls carry their owner's key, so an absent slot must not consume
            // the next sibling: it leaves the control it read pending for the next one.
            int count = ChildCount;
            var controls = _controls!;
            bool skipExiting = _host.HasExitingControls;
            for (; _resolved < end; _resolved++)
            {
                int logical = _resolved;
                _map[logical] = count;
                controls[logical] = null;
                if (_physical >= count) continue;
                var control = _pending ??= _children.Get(_physical);
                while (skipExiting && _host.IsExiting(control))
                {
                    _pending = null;
                    if (++_physical >= count) break;
                    control = _pending = _children.Get(_physical);
                }
                if (_physical >= count) continue;
                if (_host.TryGetMountedKey(control, out var controlKey))
                {
                    // An unkeyed transparent child must not claim a keyed sibling, but its own
                    // realized output can carry a key that is not a sibling key.
                    if (oldChildren[logical].Key is { } key
                        ? controlKey != key
                        : controlKey is not null && _siblingKeys!.ContainsKey(controlKey))
                        continue;
                }
                _map[logical] = _physical++;
                controls[logical] = control;
                _pending = null;
            }
        }
    }

    // Seeds the control the map already read, so a ProbeEachChild check does not read it again.
    private static bool DirtyContains(in DirtyChildIndices dirty, ref KeyedPanelMap map, int oldIndex,
        IChildList<TNative> children, THost host, ref TNative? control)
    {
        if (dirty.IsEmpty) return false;
        int panelIdx = map.At(oldIndex);
        control ??= map.MappedControl(oldIndex);
        return dirty.Contains(panelIdx, children, host, ref control);
    }

    private static void ReconcileKeyedCore(
        TElement[] oldChildren,
        TElement[] newChildren,
        IChildList<TNative> children,
        THost host,
        Action requestRerender,
        in DirtyChildIndices dirty,
        ref KeyedPanelMap map)
    {
        int oldLen = oldChildren.Length;
        int newLen = newChildren.Length;

        // Cache the child count once. Both the prefix and suffix loops only ever Replace
        // (count-neutral) or skip, so the panel count is stable across them.
        int childCount = map.ChildCount;

        // Phase 1: Common prefix
        int prefixLen = 0;
        while (prefixLen < oldLen && prefixLen < newLen &&
               ChildReconcilerShared.KeyMatch(oldChildren[prefixLen], newChildren[prefixLen]) &&
               host.CanUpdate(oldChildren[prefixLen], newChildren[prefixLen]))
        {
            var oldEl = oldChildren[prefixLen];
            var newEl = newChildren[prefixLen];

            // Early skip: when the element is structurally identical, avoid reading the native
            // child and the full property diff — exactly mirroring the positional path. The
            // keyed prefix is the steady-state path for stable lists (e.g. grid rows whose keys
            // never change), so without this they re-diff every tick. The panel index is
            // resolved only where the skip reads the collection, so a skip that never touches it
            // never advances the map's cursor.
            TNative? existingControl = null;
            if (host.CanSkipUpdate(oldEl, newEl)
                && !host.ForceRenderThroughWrapper(newEl)
                && (dirty.IsEmpty || !DirtyContains(dirty, ref map, prefixLen, children, host, ref existingControl))
                && (!host.HasActiveContextValues
                    || map.At(prefixLen) >= childCount
                    || !host.ShouldDeclineSkip(existingControl ??= map.ControlAt(prefixLen))))
            {
                host.CountSkipped(1);
                if (host.NeedsRefreshOnSkip(oldEl, newEl) && map.At(prefixLen) < childCount)
                    host.RefreshSkippedChildState(existingControl ??= map.ControlAt(prefixLen), oldEl, newEl);
                prefixLen++;
                continue;
            }

            // Update in place
            int panelIdx = map.At(prefixLen);
            if (panelIdx < childCount)
            {
                existingControl ??= map.ControlAt(prefixLen);
                var replacement = UpdateVia(in host, oldEl, newEl, existingControl, requestRerender, out var unmountedByUpdate);
                if (replacement is not null)
                {
                    if (!unmountedByUpdate) host.UnmountChild(existingControl);
                    children.Replace(panelIdx, replacement);
                }
            }
            prefixLen++;
        }

        // Phase 2: Common suffix
        int suffixLen = 0;
        while (suffixLen < (oldLen - prefixLen) && suffixLen < (newLen - prefixLen) &&
               ChildReconcilerShared.KeyMatch(oldChildren[oldLen - 1 - suffixLen], newChildren[newLen - 1 - suffixLen]) &&
               host.CanUpdate(oldChildren[oldLen - 1 - suffixLen], newChildren[newLen - 1 - suffixLen]))
        {
            // Update in place (from end)
            int oldIdx = oldLen - 1 - suffixLen;
            int newIdx = newLen - 1 - suffixLen;
            var oldEl = oldChildren[oldIdx];
            var newEl = newChildren[newIdx];

            // Early skip: same fast-path as the prefix loop, applied from the end of the list.
            TNative? existingControl = null;
            if (host.CanSkipUpdate(oldEl, newEl)
                && !host.ForceRenderThroughWrapper(newEl)
                && (dirty.IsEmpty || !DirtyContains(dirty, ref map, oldIdx, children, host, ref existingControl))
                && (!host.HasActiveContextValues
                    || map.At(oldIdx) < 0
                    || map.At(oldIdx) >= childCount
                    || !host.ShouldDeclineSkip(existingControl ??= map.ControlAt(oldIdx))))
            {
                host.CountSkipped(1);
                if (host.NeedsRefreshOnSkip(oldEl, newEl)
                    && map.At(oldIdx) >= 0
                    && map.At(oldIdx) < childCount)
                    host.RefreshSkippedChildState(existingControl ??= map.ControlAt(oldIdx), oldEl, newEl);
                suffixLen++;
                continue;
            }

            int panelIdx = map.At(oldIdx);
            if (panelIdx >= 0 && panelIdx < childCount)
            {
                existingControl ??= map.ControlAt(oldIdx);
                var replacement = UpdateVia(in host, oldEl, newEl, existingControl, requestRerender, out var unmountedByUpdate);
                if (replacement is not null)
                {
                    if (!unmountedByUpdate) host.UnmountChild(existingControl);
                    children.Replace(panelIdx, replacement);
                }
            }
            suffixLen++;
        }

        // Phase 3: Middle section
        int oldStart = prefixLen;
        int oldEnd = oldLen - suffixLen;
        int newStart = prefixLen;
        int newEnd = newLen - suffixLen;

        int oldMidLen = oldEnd - oldStart;
        int newMidLen = newEnd - newStart;

        if (oldMidLen == 0 && newMidLen == 0)
            return; // Prefix + suffix covered everything

        // Every branch below positions against the old children, before its first mutation.
        int[]? oldToPanel = map.Resolve();

        if (oldMidLen == 0)
        {
            // Only insertions
            int panelIndex = ChildReconcilerShared.InsertionPanelIndex(oldStart, oldLen, oldToPanel, childCount);
            for (int i = 0; i < newMidLen; i++)
            {
                var ctrl = MountVia(in host, newChildren[newStart + i], requestRerender);
                if (ctrl is not null)
                {
                    children.Insert(panelIndex++, ctrl);
                    if (host.HasAmbientAnimation) host.OnInsertedUnderAmbient(ctrl, newChildren[newStart + i]);
                }
            }
            return;
        }

        if (newMidLen == 0)
        {
            // Only removals (from end to start)
            for (int i = oldMidLen - 1; i >= 0; i--)
            {
                int panelIdx = ChildReconcilerShared.OldPanelIndex(oldStart + i, oldToPanel);
                if (panelIdx < children.Count)
                {
                    host.RemoveChildWithExitTransition(children, panelIdx);
                }
            }
            return;
        }

        // Middle section requires key mapping + LIS
        ReconcileKeyedMiddle(oldChildren, newChildren, oldStart, oldMidLen, newStart, newMidLen,
            suffixLen, children, host, requestRerender, oldToPanel);
    }

    /// <summary>
    /// Keyed middle section reconciliation using LIS for minimal moves.
    /// </summary>
    private static void ReconcileKeyedMiddle(
        TElement[] oldChildren, TElement[] newChildren,
        int oldStart, int oldMidLen, int newStart, int newMidLen,
        int suffixLen,
        IChildList<TNative> children,
        THost host,
        Action requestRerender,
        int[]? oldToPanel)
    {
        // Pool the per-call working buffers. The reconciler recurses via UpdateChild below, so
        // each (re-entrant) call must own distinct buffers — ArrayPool and the dict pool
        // guarantee that, whereas a single ThreadStatic scratch would corrupt the outer diff.
        var intPool = ArrayPool<int>.Shared;
        var boolPool = ArrayPool<bool>.Shared;
        var oldKeyMap = ChildReconcilerShared.RentKeyIndexDict(oldMidLen);
        int[] newToOld = intPool.Rent(newMidLen);
        bool[] matched = boolPool.Rent(oldMidLen);
        bool[] inLis = boolPool.Rent(newMidLen);
        // Live panel index of each surviving old item, keyed by old-relative index (-1 when
        // absent). Positions are tracked here and mutated in lock-step with every Insert/Move
        // so a survivor's current control is always resolvable without a re-scan — and never
        // patched by a not-yet-reached final index.
        int[] oldRelToPanel = intPool.Rent(oldMidLen);
        int originalChildCount = children.Count;
        try
        {
            // A rented bool array can carry stale `true` markers; `matched` must default to
            // false for the unmatched-removal pass below.
            Array.Clear(matched, 0, oldMidLen);

            // Unkeyed children match by absolute position; they need no synthetic string
            // unless an explicit key can collide with the legacy fallback namespace.
            bool legacyFallback = false;
            for (int i = 0; i < oldMidLen; i++)
            {
                var key = oldChildren[oldStart + i].Key;
                if (key is null) continue;
                oldKeyMap[key] = i;
                legacyFallback |= key.StartsWith("__pos_", StringComparison.Ordinal);
            }
            if (legacyFallback)
            {
                // Preserve last-wins shadowing even though CanUpdate later rejects
                // keyed/unkeyed and runtime-type mismatches.
                oldKeyMap.Clear();
                for (int i = 0; i < oldMidLen; i++)
                    oldKeyMap[GetKey(oldChildren[oldStart + i], oldStart + i)] = i;
            }

            // Map new keys to old indices
            for (int i = 0; i < newMidLen; i++)
            {
                var newChild = newChildren[newStart + i];
                var key = legacyFallback ? GetKey(newChild, newStart + i) : newChild.Key;
                int oldIdx = newStart + i - oldStart;
                bool found = key is not null
                    ? oldKeyMap.TryGetValue(key, out oldIdx)
                    : (uint)oldIdx < (uint)oldMidLen;
                if (found && host.CanUpdate(oldChildren[oldStart + oldIdx], newChild))
                {
                    newToOld[i] = oldIdx;
                    matched[oldIdx] = true;
                }
                else
                {
                    newToOld[i] = -1;
                }
            }

            // Compute LIS on newToOld into a pooled membership mask.
            ChildReconcilerShared.ComputeLISInto(newToOld, newMidLen, inLis);

            // Step 1: Remove unmatched old items (reverse order for stable indices)
            for (int i = oldMidLen - 1; i >= 0; i--)
            {
                if (!matched[i])
                {
                    int panelIdx = ChildReconcilerShared.OldPanelIndex(oldStart + i, oldToPanel);
                    int before = children.Count;
                    oldRelToPanel[i] = 0;
                    if (panelIdx < before)
                    {
                        host.RemoveChildWithExitTransition(children, panelIdx);
                        oldRelToPanel[i] = before - children.Count;
                    }
                }
            }

            // Only completed removals compact the collection; a deferred exit keeps its
            // physical slot. Derive survivor positions from the pre-pass mapping instead of
            // tags: an exiting control can share a key with its live replacement, and an
            // unkeyed child's fallback key is its logical index, not its physical one.
            int removed = 0;
            for (int i = 0; i < oldMidLen; i++)
            {
                if (!matched[i])
                {
                    removed += oldRelToPanel[i];
                    oldRelToPanel[i] = -1;
                }
                else
                {
                    int panelIdx = ChildReconcilerShared.OldPanelIndex(oldStart + i, oldToPanel);
                    oldRelToPanel[i] = panelIdx < originalChildCount ? panelIdx - removed : -1;
                }
            }

            // Step 2: position + patch via the canonical right-to-left LIS walk.
            // `initialAnchor` is the start of the suffix (end of the middle); each item is
            // placed immediately before its already-positioned right neighbour, and every
            // survivor is patched at its CURRENT live position resolved from the model, with the
            // model kept exact across every Insert/Move.
            int initialAnchor = oldToPanel is null ? children.Count - suffixLen
                : ChildReconcilerShared.InsertionPanelIndex(oldStart + oldMidLen, oldChildren.Length, oldToPanel, originalChildCount) - removed;
            var sink = new HostKeyedMiddleSink(
                oldChildren, newChildren, oldStart, newStart,
                children, host, requestRerender);
            ChildReconcilerShared.RunKeyedMiddleCore(ref sink, newToOld, inLis, newMidLen, oldMidLen, initialAnchor, oldRelToPanel);
        }
        finally
        {
            // Return every pooled buffer on all exits (including exceptions). The dict pool
            // clears entries on return, so element-key references never leak across frames.
            // The int/bool buffers hold no references and have their used range fully
            // (re)initialized before any read, so they return dirty (clearArray: false) and
            // skip an avoidable O(rented-capacity) wipe on this hot path.
            ChildReconcilerShared.ReturnKeyIndexDict(oldKeyMap);
            intPool.Return(newToOld);
            intPool.Return(oldRelToPanel);
            boolPool.Return(matched);
            boolPool.Return(inLis);
        }
    }

    /// <summary>
    /// The production <see cref="IKeyedMiddleSink"/>: drives the renderer's controls through
    /// the host. A <c>readonly struct</c> so <see cref="ChildReconcilerShared.RunKeyedMiddleCore{TSink}"/>
    /// stays allocation- and virtual-dispatch-free.
    /// </summary>
    private readonly struct HostKeyedMiddleSink : IKeyedMiddleSink
    {
        private readonly TElement[] _oldChildren;
        private readonly TElement[] _newChildren;
        private readonly int _oldStart;
        private readonly int _newStart;
        private readonly IChildList<TNative> _children;
        private readonly THost _host;
        private readonly Action _requestRerender;

        public HostKeyedMiddleSink(
            TElement[] oldChildren, TElement[] newChildren, int oldStart, int newStart,
            IChildList<TNative> children, THost host, Action requestRerender)
        {
            _oldChildren = oldChildren;
            _newChildren = newChildren;
            _oldStart = oldStart;
            _newStart = newStart;
            _children = children;
            _host = host;
            _requestRerender = requestRerender;
        }

        public bool MountInsert(int newIdx, int panelIdx)
        {
            var ctrl = MountVia(in _host, _newChildren[_newStart + newIdx], _requestRerender);
            if (ctrl is null) return false;
            _children.Insert(panelIdx, ctrl);
            if (_host.HasAmbientAnimation) _host.OnInsertedUnderAmbient(ctrl, _newChildren[_newStart + newIdx]);
            return true;
        }

        public void MoveExisting(int fromIdx, int toIdx)
        {
            var moved = _children.Get(fromIdx);
            _children.Move(fromIdx, toIdx);
            // Spec 042 §6 — an implicit offset animation on the moved child so the reorder reads
            // visually under an ambient transaction.
            if (_host.HasAmbientAnimation)
                _host.OnMovedUnderAmbient(moved);
        }

        public void Patch(int oldRelIdx, int newIdx, int panelIdx)
        {
            if (panelIdx >= _children.Count) return;
            // The keyed-MIDDLE patch intentionally has NO CanSkipUpdate early-skip arm (unlike
            // the positional / keyed prefix+suffix arms): it unconditionally routes every
            // reordered survivor through the host's update, whose element-level shallow skip is
            // where a renderer re-resolves theme-dependent state. A future optimization that adds
            // a skip here must mirror that re-resolution.
            var existing = _children.Get(panelIdx);
            var replacement = UpdateVia(in _host, 
                _oldChildren[_oldStart + oldRelIdx],
                _newChildren[_newStart + newIdx],
                existing,
                _requestRerender,
                out var unmountedByUpdate);
            if (replacement is not null)
            {
                if (!unmountedByUpdate) _host.UnmountChild(existing);
                _children.Replace(panelIdx, replacement);
            }
        }
    }

    internal static TElement[] Filter(TElement[] elements, THost host, out long[]? slots)
    {
        // The DSL container factories already dropped null / empty elements and registered the
        // slots they came from; a hand-built children array that still holds them gets its
        // slots from the raw indices here.
        var registered = ChildSlots.Get(elements);

        // Single count pass; the result array is then sized exactly and filled in one more
        // pass. The common no-null case returns the input untouched.
        int keep = 0;
        for (int i = 0; i < elements.Length; i++)
        {
            if (elements[i] is { } e && !host.IsEmpty(e)) keep++;
        }
        if (keep == elements.Length)
        {
            slots = registered;
            return elements;
        }

        var result = new TElement[keep];
        slots = new long[keep];
        int j = 0;
        for (int i = 0; i < elements.Length; i++)
        {
            if (elements[i] is { } e && !host.IsEmpty(e))
            {
                slots[j] = registered?[i] ?? ChildSlots.ForIndex(i);
                result[j++] = e;
            }
        }
        return result;
    }

    internal static string GetKey(TElement element, int positionalIndex) =>
        ChildReconcilerShared.GetKey(element, positionalIndex);
}

/// <summary>
/// The parts of the child reconciler that depend on no type parameter: the LIS, the keyed-middle
/// positioning walk and the pooled key maps.
/// </summary>
internal static class ChildReconcilerShared
{
    // Key helpers take ReactorElement rather than the engine's TElement: the engine's body is shared
    // code across reference-type instantiations, and a non-generic helper inlines into it freely.
    internal static bool HasAnyKeys(ReactorElement[] elements)
    {
        for (int i = 0; i < elements.Length; i++)
        {
            if (elements[i].Key is not null) return true;
        }
        return false;
    }

    internal static bool SameKeys(ReactorElement[] oldElements, ReactorElement[] newElements)
    {
        if (ReferenceEquals(oldElements, newElements)) return true;
        if (oldElements.Length != newElements.Length) return false;
        for (int i = 0; i < newElements.Length; i++)
        {
            if (!string.Equals(oldElements[i].Key, newElements[i].Key, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    internal static bool KeyMatch(ReactorElement a, ReactorElement b)
    {
        // Both must have the same key (or both null) AND same type
        if (a.GetType() != b.GetType()) return false;
        return a.Key == b.Key;
    }

    internal static int OldPanelIndex(int oldIndex, int[]? oldToPanel) =>
        oldToPanel is null ? oldIndex : oldToPanel[oldIndex];

    internal static int InsertionPanelIndex(int oldIndex, int oldLen, int[]? oldToPanel, int childCount)
    {
        if (oldToPanel is null) return oldIndex < oldLen ? oldIndex : childCount;
        for (int i = oldIndex; i < oldLen; i++)
            if (oldToPanel[i] < childCount) return oldToPanel[i];
        return childCount;
    }

    /// <summary>
    /// Canonical LIS-based keyed reconciliation of the middle section, processed right-to-left
    /// so each item is positioned immediately before its already-placed right neighbour (the
    /// live "anchor"). LIS members are left in place; only out-of-LIS movers are relocated,
    /// giving the minimal move count. Every survivor is patched at its CURRENT live position
    /// (resolved from <paramref name="oldRelToPanel"/>, which is kept exact across every
    /// structural mutation) rather than a final index the collection has not yet been
    /// rearranged into.
    /// </summary>
    /// <typeparam name="TSink">Concrete sink struct; a generic constraint keeps the calls
    /// devirtualized and allocation-free.</typeparam>
    /// <param name="sink">Side-effect surface that applies the mount/move/patch operations the
    /// walk decides on.</param>
    /// <param name="newToOld">For each new-middle slot, the old-relative index it reuses, or -1
    /// for a freshly-mounted item.</param>
    /// <param name="inLis">LIS membership mask over <paramref name="newToOld"/>.</param>
    /// <param name="newMidLen">Number of items in the new middle section.</param>
    /// <param name="oldMidLen">Number of items in the old middle section.</param>
    /// <param name="initialAnchor">Live index of the start of the suffix (end of the middle) —
    /// where right-most items are placed.</param>
    /// <param name="oldRelToPanel">Live index of each surviving old item by old-relative index
    /// (-1 if absent). Mutated in place to stay exact.</param>
    internal static void RunKeyedMiddleCore<TSink>(
        ref TSink sink,
        int[] newToOld, bool[] inLis,
        int newMidLen, int oldMidLen,
        int initialAnchor, int[] oldRelToPanel)
        where TSink : struct, IKeyedMiddleSink
    {
        int anchor = initialAnchor;
        for (int i = newMidLen - 1; i >= 0; i--)
        {
            int oldRel = newToOld[i];
            if (oldRel < 0)
            {
                // New item: mount + insert immediately before the anchor.
                if (sink.MountInsert(i, anchor))
                {
                    // Insertion at `anchor` shifts every tracked survivor at an index >= anchor
                    // one slot to the right.
                    for (int r = 0; r < oldMidLen; r++)
                        if (oldRelToPanel[r] >= anchor) oldRelToPanel[r]++;
                    // The new control now occupies `anchor` and is the anchor for the next
                    // (left) item, so `anchor` itself is unchanged.
                }
                continue;
            }

            int cur = oldRelToPanel[oldRel];
            if (cur < 0) continue; // Defensive: survivor not realized (skip).

            if (!inLis[i])
            {
                // Out-of-LIS mover: relocate immediately before the anchor. With Move's
                // final-position semantics, the destination is anchor-1 when the control
                // currently sits left of the anchor, else anchor itself.
                int to = cur < anchor ? anchor - 1 : anchor;
                if (cur != to)
                {
                    sink.MoveExisting(cur, to);
                    // Keep the model exact: a Move shifts the half-open range between the two
                    // endpoints by one slot.
                    if (cur < to)
                    {
                        for (int r = 0; r < oldMidLen; r++)
                        {
                            int p = oldRelToPanel[r];
                            if (p > cur && p <= to) oldRelToPanel[r] = p - 1;
                        }
                    }
                    else
                    {
                        for (int r = 0; r < oldMidLen; r++)
                        {
                            int p = oldRelToPanel[r];
                            if (p >= to && p < cur) oldRelToPanel[r] = p + 1;
                        }
                    }
                    oldRelToPanel[oldRel] = to;
                    cur = to;
                }
            }

            sink.Patch(oldRel, i, cur);
            anchor = cur;
        }
    }

    /// <summary>
    /// Compute the Longest Increasing Subsequence over the first <paramref name="length"/>
    /// entries of <paramref name="arr"/>, writing the membership mask into
    /// <paramref name="inLis"/> (<c>inLis[i] == true</c> iff index <c>i</c> participates in the
    /// LIS). Entries with value -1 are skipped (unmapped items). O(n log n) patience sorting.
    /// All working buffers are rented from <see cref="ArrayPool{T}"/>, so the hot reconcile path
    /// pays no per-call heap allocation.
    /// </summary>
    internal static void ComputeLISInto(int[] arr, int length, bool[] inLis)
    {
        if (length > 0) Array.Clear(inLis, 0, length);
        if (length == 0) return;

        var pool = ArrayPool<int>.Shared;
        int[] tails = pool.Rent(length);        // Smallest tail values
        int[] tailIndices = pool.Rent(length);  // arr indices matching tails
        int[] predecessors = pool.Rent(length); // back-pointers for reconstruction
        try
        {
            int tailsCount = 0;
            for (int i = 0; i < length; i++)
            {
                if (arr[i] == -1) continue; // Skip unmapped

                int val = arr[i];

                // Binary search for insertion position in tails[0..tailsCount).
                int lo = 0, hi = tailsCount;
                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;
                    if (tails[mid] < val) lo = mid + 1;
                    else hi = mid;
                }

                // Predecessor is the previous tail's arr-index (read before the tails[lo]
                // write — index lo-1 is unaffected by it).
                predecessors[i] = lo > 0 ? tailIndices[lo - 1] : -1;

                if (lo == tailsCount)
                {
                    tails[tailsCount] = val;
                    tailIndices[tailsCount] = i;
                    tailsCount++;
                }
                else
                {
                    tails[lo] = val;
                    tailIndices[lo] = i;
                }
            }

            // Backtrack from the last tail to mark LIS membership. Only non-skipped indices
            // ever enter the chain, so unwritten predecessor slots for skipped entries are never
            // read.
            if (tailsCount == 0) return;
            int idx = tailIndices[tailsCount - 1];
            while (idx != -1)
            {
                inLis[idx] = true;
                idx = predecessors[idx];
            }
        }
        finally
        {
            pool.Return(tails);
            pool.Return(tailIndices);
            pool.Return(predecessors);
        }
    }

    /// <summary>
    /// Compute Longest Increasing Subsequence, returning the set of participating indices.
    /// Convenience wrapper over <see cref="ComputeLISInto"/> for tests and set-shaped callers;
    /// the hot reconcile path uses the allocation-free <see cref="ComputeLISInto"/>.
    /// </summary>
    internal static HashSet<int> ComputeLIS(int[] arr)
    {
        var result = new HashSet<int>();
        if (arr.Length == 0) return result;

        var pool = ArrayPool<bool>.Shared;
        bool[] mask = pool.Rent(arr.Length);
        try
        {
            ComputeLISInto(arr, arr.Length, mask);
            for (int i = 0; i < arr.Length; i++)
                if (mask[i]) result.Add(i);
        }
        finally
        {
            pool.Return(mask, clearArray: true);
        }
        return result;
    }

    // Cache the per-Type display name used by the unkeyed GetKey fallback so we don't reflect
    // over the runtime type on every diff. Explicit keys are strongly preferred and bypass this
    // path entirely.
    private static readonly ConcurrentDictionary<Type, string> _typeNameCache = new();

    internal static string GetKey(ReactorElement element, int positionalIndex)
    {
        // Use explicit key if available, otherwise fall back to type+position
        if (element.Key is not null) return element.Key;
        var typeName = _typeNameCache.GetOrAdd(element.GetType(), static t => t.Name);
        return $"__pos_{positionalIndex}_{typeName}";
    }

    // ── Re-entrancy-safe Dictionary<string,int> pool ────────────────────
    // ReconcileKeyedMiddle needs a transient key→index map per call, and it recurses through
    // UpdateChild. A per-thread stack hands each (nested) call its own instance: rented dicts
    // are not in the pool, so an inner call can never grab a buffer the outer call is still
    // using. Pool size is capped so deep trees don't retain an unbounded number of dictionaries.
    [ThreadStatic] private static Stack<Dictionary<string, int>>? _keyIndexDictPool;
    private const int KeyIndexDictPoolCap = 16;

    internal static Dictionary<string, int> RentKeyIndexDict(int capacity)
    {
        var pool = _keyIndexDictPool;
        if (pool is { Count: > 0 })
        {
            var d = pool.Pop();
            if (capacity > 0) d.EnsureCapacity(capacity);
            return d;
        }
        return new Dictionary<string, int>(capacity > 0 ? capacity : 0, StringComparer.Ordinal);
    }

    internal static void ReturnKeyIndexDict(Dictionary<string, int> dict)
    {
        dict.Clear();
        var pool = _keyIndexDictPool ??= new Stack<Dictionary<string, int>>();
        if (pool.Count < KeyIndexDictPoolCap) pool.Push(dict);
    }
}

/// <summary>
/// Side-effect surface for <see cref="ChildReconcilerShared.RunKeyedMiddleCore{TSink}"/>. The
/// keyed-middle positioning algorithm is expressed once against this interface so the
/// production reconciler (real Mount/Update/Move) and the headless correctness oracle in the
/// test suite drive the identical index logic. Implemented as a <c>struct</c> behind a generic
/// constraint so the JIT devirtualizes the calls with no boxing on the hot path (and it stays
/// AOT/trim-clean — no reflection).
/// </summary>
internal interface IKeyedMiddleSink
{
    /// <summary>Mount new child <paramref name="newIdx"/> and insert it at
    /// <paramref name="panelIdx"/>. Returns <c>false</c> when nothing was inserted (mount
    /// produced no control) so the caller skips the model shift.</summary>
    bool MountInsert(int newIdx, int panelIdx);

    /// <summary>Move the existing control at <paramref name="fromIdx"/> to
    /// <paramref name="toIdx"/> using final-position semantics (matching
    /// <see cref="IChildList{TNative}.Move"/>).</summary>
    void MoveExisting(int fromIdx, int toIdx);

    /// <summary>Patch the surviving control for old-relative index <paramref name="oldRelIdx"/> —
    /// currently located at <paramref name="panelIdx"/> — against new child
    /// <paramref name="newIdx"/>.</summary>
    void Patch(int oldRelIdx, int newIdx, int panelIdx);
}
