using Microsoft.UI.Reactor.Core.Diagnostics;

namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>A list's native selection, as the verification reads and fixes it (spec 070 §3.4).</summary>
internal interface ISelectionPort
{
    /// <summary>The native view the port reads, as an identity token; <see langword="null"/> without one.</summary>
    object? Owner { get; }

    /// <summary>Runs <paramref name="completion"/> after every batch already queued on the native view.</summary>
    bool Barrier(Action completion);

    /// <summary>The boxes the native view shows selected.</summary>
    IReadOnlyCollection<ItemBox> Selected();

    void Select(ItemBox box, bool selected);
}

/// <summary>
/// Verifies a list's native selection against its declaration behind MAUI's pending batches (spec 070
/// §3.4). MAUI's iOS SelectItem defers its native selection to a batch completion while the list isn't
/// loaded, and doesn't check there that the item is still the selected one, so an older deferred
/// selection can overwrite a newer one. Each commit schedules a verification; user input makes pending
/// work stale. Nothing infers which write caused what: a current completion compares states.
/// </summary>
internal sealed class NativeSelectionVerifier(
    ReactorCollectionView view, Func<CollectionViewElement?> live, Action<Action> writeSuppressed)
{
    private int _generation;
    private bool _outstanding;
    private int _fixedGeneration = -1;
    private bool _retired;
    private ISelectionPort? _port;
    private bool _portResolved;

    /// <summary>The platform's port: UIKit's on iOS and Mac Catalyst, none elsewhere. Tests set a stub.</summary>
    public ISelectionPort? Port
    {
        get
        {
            if (!_portResolved)
            {
                _portResolved = true;
                _port = PlatformPort(view);
            }
            return _port;
        }
        set
        {
            _port = value;
            _portResolved = true;
        }
    }

    /// <summary>Barriers issued (a test witness).</summary>
    public int Issued { get; private set; }

    /// <summary>Corrections applied (a test witness).</summary>
    public int Corrections { get; private set; }

    /// <summary>A commit happened: verify behind the batches queued so far, invalidating older work.</summary>
    public void Schedule()
    {
        if (_retired || Port is null) return;
        _generation++;
        if (!_outstanding) Issue();
    }

    /// <summary>User input arrived: any pending correction was computed before it and is stale.</summary>
    public void Invalidate() => _generation++;

    public void Retire() => _retired = true;

    private void Issue()
    {
        if (Port is not { } port || port.Owner is not { } owner) return;
        var generation = _generation;
        _outstanding = true;
        Issued++;
        if (!port.Barrier(() => Complete(port, owner, generation))) _outstanding = false;
    }

    private void Complete(ISelectionPort port, object owner, int generation)
    {
        _outstanding = false;
        // 1. Invalid owner: the list is gone or another handler (platform view) took over. Retired.
        if (_retired || !ReferenceEquals(Port, port) || !ReferenceEquals(port.Owner, owner) || live() is not { } element) return;
        // 2. Stale: newer work exists. Retired, and the current work is issued behind every batch since.
        if (generation != _generation)
        {
            Issue();
            return;
        }
        // 3. Current: compare the native selection with the declared effective one, as sets.
        var wanted = SelectionRules.Effective(element, view.Host);
        var native = port.Selected();
        var wantedSet = new HashSet<ItemBox>(wanted, ReferenceEqualityComparer.Instance);
        if (wantedSet.SetEquals(native))
        {
            _fixedGeneration = -1;
            return;
        }
        if (_fixedGeneration == generation)
        {
            // Fixed once and still different: stop, so a platform that never converges can't loop.
            _fixedGeneration = -1;
            DiagnosticLog.Warning(LogCategory.Reactor, "CollectionView.NativeSelection",
                "The native selection still differs from the declared one after a correction; giving up until the next commit.");
            return;
        }
        writeSuppressed(() =>
        {
            foreach (var box in native)
                if (!wantedSet.Contains(box)) port.Select(box, false);
            foreach (var box in wanted)
                if (!native.Contains(box)) port.Select(box, true);
        });
        Corrections++;
        _fixedGeneration = generation;
        Issue();
    }

    private static ISelectionPort? PlatformPort(ReactorCollectionView view)
    {
#if IOS || MACCATALYST
        return new UIKitSelectionPort(view);
#else
        return null;
#endif
    }

#if IOS || MACCATALYST
    private sealed class UIKitSelectionPort(ReactorCollectionView view) : ISelectionPort
    {
        public object? Owner => view.Handler?.PlatformView;

        private UIKit.UICollectionView? Native()
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

        public bool Barrier(Action completion)
        {
            if (Native() is not { } native) return false;
            native.PerformBatchUpdates(null, _ => completion());
            return true;
        }

        public IReadOnlyCollection<ItemBox> Selected()
        {
            var boxes = new HashSet<ItemBox>(ReferenceEqualityComparer.Instance);
            foreach (var path in Native()?.GetIndexPathsForSelectedItems() ?? [])
                if (view.Host.At((int)path.Section, (int)path.Item) is { } box) boxes.Add(box);
            return boxes;
        }

        public void Select(ItemBox box, bool selected)
        {
            if (Native() is not { } native || view.Host.PositionOf(box) is not { } at) return;
            var path = Foundation.NSIndexPath.FromItemSection(at.Item, at.Section);
            if (selected) native.SelectItem(path, false, UIKit.UICollectionViewScrollPosition.None);
            else native.DeselectItem(path, false);
        }
    }
#endif
}
