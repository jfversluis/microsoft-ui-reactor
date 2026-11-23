using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Reactor.Core.Internal;

namespace Microsoft.UI.Reactor.Core;

// AI-HINT: The keyed/positional child diff lives in the framework-neutral core
//   (src/Reactor.Core/Engine/ChildReconciler.cs, spec 067), generic over the element type, the
//   native control type and a struct host. This file is WinUI's side: the façade the reconciler,
//   handlers and tests call, and WinUIChildHost, which answers the engine's questions the way
//   the WinUI reconciler always has (skip contract, exit transitions, ambient animation).

/// <summary>
/// WinUI's entry point to the framework-neutral child reconciler
/// (<see cref="ChildReconciler{TElement, TNative, THost}"/>). Reconciles a container's old and new
/// child elements against its live <see cref="IChildCollection"/>: positional for unkeyed children,
/// prefix/suffix stripping plus longest-increasing-subsequence moves for keyed ones.
/// </summary>
internal static class ChildReconciler
{
    /// <summary>
    /// Reconciles old and new child element arrays against a Panel's Children collection.
    /// </summary>
    internal static void Reconcile(
        Element[] oldChildren,
        Element[] newChildren,
        IChildCollection children,
        Reconciler reconciler,
        Action requestRerender,
        UIElement? parentControl = null)
    {
        // Spec 042 §6 — read the active Animations.Animate ambient once per reconcile so insert /
        // move / unmount paths can apply the same kind without re-reading AsyncLocal for every
        // child. Stays null in the overwhelmingly common no-ambient case.
        var ambient = AnimationAmbient.Current;
        AnimationKind? ambientKind = ambient is { HasEffect: true } ? ambient.Kind : null;

        ChildReconciler<Element, UIElement, WinUIChildHost>.Reconcile(
            oldChildren, newChildren, children, new WinUIChildHost(reconciler, ambientKind), requestRerender, parentControl);
    }

    internal static Element[] Filter(Element[] elements, out long[]? slots) =>
        ChildReconciler<Element, UIElement, WinUIChildHost>.Filter(elements, default, out slots);

    /// <inheritdoc cref="ChildReconcilerShared.ComputeLISInto"/>
    internal static void ComputeLISInto(int[] arr, int length, bool[] inLis) =>
        ChildReconcilerShared.ComputeLISInto(arr, length, inLis);

    /// <inheritdoc cref="ChildReconcilerShared.ComputeLIS"/>
    internal static HashSet<int> ComputeLIS(int[] arr) => ChildReconcilerShared.ComputeLIS(arr);

    /// <summary>
    /// The keyed-middle sink, as the correctness oracle in the test suite implements it. Same
    /// contract as the neutral core's <c>IKeyedMiddleSink</c>, which it extends.
    /// </summary>
    internal interface IKeyedMiddleSink : global::Microsoft.UI.Reactor.Core.IKeyedMiddleSink
    {
    }

    /// <inheritdoc cref="ChildReconcilerShared.RunKeyedMiddleCore{TSink}"/>
    internal static void RunKeyedMiddleCore<TSink>(
        ref TSink sink,
        int[] newToOld, bool[] inLis,
        int newMidLen, int oldMidLen,
        int initialAnchor, int[] oldRelToPanel)
        where TSink : struct, IKeyedMiddleSink
        => ChildReconcilerShared.RunKeyedMiddleCore(ref sink, newToOld, inLis, newMidLen, oldMidLen, initialAnchor, oldRelToPanel);

    // Reached by reflection from PanelChildReconciliationTests.
    private static bool HasAnyKeys(Element[] elements) =>
        ChildReconcilerShared.HasAnyKeys(elements);

    private static bool KeyMatch(Element a, Element b) =>
        ChildReconcilerShared.KeyMatch(a, b);

    private static string GetKey(Element element, int positionalIndex) =>
        ChildReconcilerShared.GetKey(element, positionalIndex);

    /// <summary>
    /// Spec 042 §6 — implicit Offset animation on a moved child so the reorder reads visually.
    /// Mirrors the per-container offset animation in <c>Reconciler.Update.cs:StartMoveOffsetAnimation</c>
    /// for the templated-list path; same curve resolution by kind.
    /// </summary>
    internal static void ApplyAmbientMove(UIElement ctrl, AnimationKind kind)
    {
        var curve = AnimationKindMap.ToCurve(kind);
        if (curve is null) return;
        try
        {
            var visual = Animation.AnimationHelper.GetElementVisual(ctrl);
            var compositor = visual.Compositor;
            var anim = Animation.AnimationHelper.CreateVector3ImplicitAnimation(compositor, "Offset", curve);
            var coll = compositor.CreateImplicitAnimationCollection();
            coll["Offset"] = anim;
            visual.ImplicitAnimations = coll;
        }
        catch
        {
            // Composition can throw in headless / disposing contexts.
            // Animation is non-critical — correctness is preserved.
        }
    }
}

/// <summary>
/// WinUI's answers to the framework-neutral child reconciler. A <c>readonly struct</c> holding
/// the reconciler and the ambient animation read for this reconcile, so the JIT binds the engine's
/// per-child calls directly to it. Those calls are typed <see cref="ReactorElement"/> and
/// <see cref="object"/> (see <see cref="IChildReconcileHost"/>); the engine only passes WinUI's own
/// elements and controls, so they are reinterpreted with <c>Unsafe.As</c>, not type-checked.
/// </summary>
internal readonly struct WinUIChildHost : IChildReconcileHost<Element, UIElement>
{
    private readonly Reconciler _reconciler;
    private readonly AnimationKind? _ambientKind;

    internal WinUIChildHost(Reconciler reconciler, AnimationKind? ambientKind)
    {
        _reconciler = reconciler;
        _ambientKind = ambientKind;
    }

    public object? Mount(ReactorElement element, Action requestRerender) => _reconciler.Mount(Unsafe.As<Element>(element), requestRerender);

    public object? UpdateChildTracked(ReactorElement oldElement, ReactorElement newElement, object control, Action requestRerender, out bool unmountedByUpdate) =>
        _reconciler.UpdateChildTracked(Unsafe.As<Element>(oldElement), Unsafe.As<Element>(newElement), Unsafe.As<UIElement>(control), requestRerender, out unmountedByUpdate);

    public void UnmountChild(object control) => _reconciler.UnmountChild(Unsafe.As<UIElement>(control));

    public bool CanUpdate(ReactorElement oldElement, ReactorElement newElement) => _reconciler.CanUpdate(Unsafe.As<Element>(oldElement), Unsafe.As<Element>(newElement));

    // Issue #675 — the skip contract lives entirely in Element.CanSkipUpdate (shared by the
    // positional + keyed prefix/suffix arms): it declines the skip for any element carrying
    // ThemeBindings OR ResourceOverrides.ThemeRefs, so a theme-sensitive child falls through to
    // UpdateChild → Update, whose element-level shallow-skip re-resolves the themed value.
    public bool CanSkipUpdate(ReactorElement oldElement, ReactorElement newElement) => Element.CanSkipUpdate(Unsafe.As<Element>(oldElement), Unsafe.As<Element>(newElement));

    public bool ForceFullRenderActive => _reconciler.ForceFullRenderActive;

    public bool ForceRenderThroughWrapper(ReactorElement newElement) => _reconciler.ForceRenderThroughWrapper(Unsafe.As<Element>(newElement));

    public bool HasActiveContextValues => _reconciler.HasActiveContextValues;

    public bool ShouldDeclineSkip(object control) => _reconciler.ShouldDeclineSkip(Unsafe.As<UIElement>(control));

    // Refresh the Tag when the element carries callbacks: the skip short-circuits Update, so
    // without this the event trampoline keeps dispatching through the previous render's closure
    // (stale state). #721 — the gesture/drag slots are excluded from the skip predicate, so their
    // cached dispatch closures are refreshed here too.
    public bool NeedsRefreshOnSkip(ReactorElement oldReactorElement, ReactorElement newReactorElement)
    {
        var oldElement = Unsafe.As<Element>(oldReactorElement);
        var newElement = Unsafe.As<Element>(newReactorElement);
        return newElement.HasCallbacks
            || Reconciler.IsFlyoutDecorator(newElement)
            || Reconciler.HasGestureOrDragSlots(newElement.Modifiers)
            || Reconciler.HasGestureOrDragSlots(oldElement.Modifiers)
            || Reconciler.CallSiteChangedOnSkip(oldElement, newElement);
    }

    public void RefreshSkippedChildState(object control, ReactorElement oldElement, ReactorElement newElement)
    {
        if (control is FrameworkElement fe)
            _reconciler.RefreshSkippedChildState(fe, Unsafe.As<Element>(oldElement), Unsafe.As<Element>(newElement));
    }

    public void CountSkipped(int count) => _reconciler.DebugElementsSkipped += count;

    // PR-C (spec 034 §C): a hint present for THIS array (a CWT hit also proves Filter returned
    // the same reference, so no null/EmptyElement shifted the index space), no theme-sensitive
    // cell (else an untouched, reference-equal cell could still need ApplyThemeBindings /
    // ApplyResourceOverrides re-resolved), and the reconciler's OLD array IS the array the hint's
    // indices were diffed against.
    public bool TryGetChangedIndices(Element[] oldChildren, Element[] newChildren, out int[] changedIndices)
    {
        if (ChildDiffHints.TryGet(newChildren, out var hint)
            && !hint.AnyThemeSensitive
            && hint.PreviousChildren.TryGetTarget(out var hintPrev)
            && ReferenceEquals(oldChildren, hintPrev))
        {
            changedIndices = hint.ChangedIndices;
            return true;
        }
        changedIndices = null!;
        return false;
    }

    public DirtyChildIndices ResolveDirtyChildIndices(UIElement? parentControl, IChildList<UIElement> children) =>
        _reconciler.ResolveDirtyChildIndices(parentControl, children);

    public bool IsOnDirtyAncestorPath(UIElement control) => _reconciler.IsOnDirtyAncestorPath(control);

    public bool HasExitingControls => _reconciler.HasExitingControls;

    public bool HoldsExitingControlIn(IChildList<UIElement> children) => _reconciler.HoldsExitingControlIn(children);

    public bool IsExiting(object control) => _reconciler.IsExiting(Unsafe.As<UIElement>(control));

    public void RemoveChildWithExitTransition(IChildList<UIElement> children, int index) =>
        _reconciler.RemoveChildWithExitTransition(children, index);

    public bool ReplaceChildWithExitTransition(IChildList<UIElement> children, int index, UIElement newControl) =>
        _reconciler.ReplaceChildWithExitTransition(children, index, newControl);

    public bool TryGetMountedKey(object control, out string? key)
    {
        if (control is FrameworkElement fe)
        {
            key = Reconciler.GetMountedElement(fe)?.Key;
            return true;
        }
        key = null;
        return false;
    }

    public void ReportDuplicateSiblingKeys(Element[] children, UIElement? parentControl) =>
        KeyedListDiff.ReportDuplicateSiblingKeys(children, _reconciler, parentControl);

    public bool IsEmpty(ReactorElement element) => element is EmptyElement;

    public bool HasAmbientAnimation => _ambientKind is not null;

    // Spec 042 §6 — when an Animations.Animate transaction is active and the newly-mounted child
    // has no explicit .Transition(...) modifier, apply the default fade-up enter so the structural
    // change reads visually. Per-element transitions continue to win when set.
    public void OnInsertedUnderAmbient(object control, ReactorElement element)
    {
        if (_ambientKind is not { } kind) return;
        if (Unsafe.As<Element>(element).ElementTransition is not null) return;
        Reconciler.ApplyAmbientEnterAnimation(Unsafe.As<UIElement>(control), kind);
    }

    public void OnMovedUnderAmbient(object control)
    {
        if (_ambientKind is { } kind)
            ChildReconciler.ApplyAmbientMove(Unsafe.As<UIElement>(control), kind);
    }
}
