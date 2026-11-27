namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>
/// One reading of a carousel's platform (spec 070 §5.4): the item whose cell is nearest the
/// carousel's center, whether that cell is centered, where it is (to compare frames), and the
/// platform's motion flags.
/// </summary>
/// <param name="Shown">The item index (in the published items) of the cell nearest the center.</param>
/// <param name="Aligned">That cell's center is within a point of the carousel's center.</param>
/// <param name="Offset">That cell's center minus the carousel's center, in points.</param>
/// <param name="Moving">A motion flag is set: iOS's page scroller tracking, dragging or decelerating; Android's scroll state.</param>
/// <param name="UserDragging">A finger moves the carousel: iOS tracking or dragging; Android dragging.</param>
internal readonly record struct CarouselSample(int Shown, bool Aligned, double Offset, bool Moving, bool UserDragging);

/// <summary>A carousel's platform, as the position controller reads and moves it (spec 070 §5.4).</summary>
internal interface ICarouselPort
{
    /// <summary>The native view, as an identity token: a handler replacement changes it.</summary>
    object? Owner { get; }

    /// <summary>Reads the platform; <see langword="null"/> while no item cell is laid out.</summary>
    CarouselSample? Sample();

    /// <summary>Scrolls through MAUI's <c>ItemsView.ScrollTo</c>, centered.</summary>
    void ScrollTo(int index, bool animate);

    /// <summary>Runs <paramref name="completion"/> once MAUI's and the platform's work for the items changes
    /// published so far has finished; <see langword="false"/> when it can't (no native view yet).</summary>
    bool Barrier(Action completion);

    /// <summary>Runs <paramref name="callback"/> on the next display frame.</summary>
    void NextFrame(Action callback);

    /// <summary>Calls <paramref name="onInteraction"/> when the platform's scroller starts or changes an interaction.</summary>
    void Watch(Action onInteraction);
}

/// <summary>
/// The controlled position of a carousel (spec 070 §5.4). The platform's resting item is the truth:
/// Reactor writes only at rest (through <c>ScrollTo</c>), publishes items and layout changes only at
/// rest, and reports the item the platform rests on when it differs from the declaration. Nothing is
/// inferred about who moved the carousel; Reactor's own projections are recognized by their target.
/// </summary>
internal sealed class CarouselPositionController
{
    private readonly Func<int?> _declared;
    private readonly Func<bool> _animated;
    private readonly Action<Action> _writeSuppressed;
    private readonly Action<int> _report;
    private readonly Action _publish;
    private ICarouselPort? _port;

    private int _generation;
    private bool _retired;

    // Items and layout transactions (rule 3).
    private bool _transactionPending;
    private int _epoch;
    private int _barrierFor = -1;
    private bool _barrierPending;
    private bool _windowInput;

    // Projections (rule 2).
    private int? _ownTarget;
    private bool _userDuringOwn;
    private bool _inOwnScroll;

    // The frame poll (rule 4).
    private bool _polling;
    private int _stableFrames;
    private int _idleFrames;
    private CarouselSample? _last;

    // The item the platform last rested on (a verified rest, or the mount's position): a rest on another
    // item is input; a rest on the same one with another declaration is a declaration to project.
    private int _lastRest;

    // A report is on its way to the app: nothing is projected until the commit that answers it.
    private bool _awaitingAnswer;
    private int _ownRetries;

    // The next projection jumps: it follows an items transaction (rule 3).
    private bool _jumpNext;

    /// <param name="declared">The live declaration, clamped to the published items; <see langword="null"/> for none.</param>
    /// <param name="animated">Whether a declared change animates (the element's <c>IsScrollAnimated</c>).</param>
    /// <param name="writeSuppressed">Runs a write in the handler's echo scope.</param>
    /// <param name="report">Reports a resting item, an index in the published items (posted, through <c>Live</c>, then restores; it must always restore, so the answer's commit arrives).</param>
    /// <param name="publish">Publishes the live element's items and layout to MAUI, in the echo scope.</param>
    /// <param name="mounted">The position written at mount: where the platform rests first.</param>
    public CarouselPositionController(Func<int?> declared, Func<bool> animated, Action<Action> writeSuppressed, Action<int> report, Action publish, int mounted)
    {
        _lastRest = mounted;
        _declared = declared;
        _animated = animated;
        _writeSuppressed = writeSuppressed;
        _report = report;
        _publish = publish;
    }

    /// <summary>The platform's port; set lazily by the handler, or by tests.</summary>
    public ICarouselPort? Port
    {
        get => _port;
        set
        {
            if (ReferenceEquals(_port, value)) return;
            _port = value;
            // A new platform view (handler replacement): the old one's barrier, poll and projection retire.
            _generation++;
            _polling = false;
            _barrierPending = false;
            _barrierFor = -1;
            _windowInput = false;
            _ownTarget = null;
            if (_port is null) return;
            _port.Watch(OnTrigger);
            // A new platform view: look at where it rests (a declaration may have changed before it existed).
            ArmPoll();
        }
    }

    /// <summary>Test witnesses.</summary>
    internal int Projections { get; private set; }
    internal int Publications { get; private set; }
    internal int BarriersIssued { get; private set; }
    internal bool TransactionPending => _transactionPending;
    internal bool BarrierPending => _barrierPending;
    internal int? OwnTarget => _ownTarget;

    /// <summary>
    /// A commit. <paramref name="transaction"/>: the element's items structure or layout differs from
    /// what's published. Projects the declaration at rest; defers a transaction while the carousel moves.
    /// </summary>
    public void Commit(bool transaction)
    {
        if (_retired) return;
        _awaitingAnswer = false;
        if (transaction) _transactionPending = true;
        if (_transactionPending)
        {
            if (IsMoving()) ArmPoll();
            else ApplyTransaction();
            return;
        }
        Project(_animated());
    }

    /// <summary>A platform signal (an event outside Reactor's scope, a scroller interaction): look again.</summary>
    public void OnTrigger() => ArmPoll();

    /// <summary>MAUI's <c>ScrollToRequested</c>: from Reactor's own projection, or a ref command (input).</summary>
    public void OnScrollToRequested()
    {
        if (_retired || _inOwnScroll) return;
        // A ref's ScrollTo is input: it ends any own projection and wins over a pending one.
        _ownTarget = null;
        if (_barrierPending) _windowInput = true;
        ArmPoll();
    }

    /// <summary>Unmount or a handler that's gone: nothing is written, published or reported after this.</summary>
    public void Retire()
    {
        _retired = true;
        _generation++;
        _polling = false;
    }

    // Moving, or not yet verified at rest since the last signal.
    private bool IsMoving() => _polling || _port?.Sample() is { Moving: true };

    private void ApplyTransaction()
    {
        _transactionPending = false;
        _publish();
        Publications++;
        _epoch++;
        _barrierPending = true;
        _windowInput = false;
        // Indices moved with the items: any rest after the window that isn't the projection is input.
        _lastRest = -1;
        // The same-turn jump makes MAUI's Android carousel skip its deferred correction (its ScrollTo
        // counter), so no frame shows item 0; on iOS the published items already show the right item.
        if (_declared() is { } target) OwnScroll(target, animate: false);
        IssueBarrier();
    }

    private void IssueBarrier()
    {
        if (_port is not { } port) { _barrierPending = false; return; }
        var epoch = _epoch;
        var generation = _generation;
        var owner = port.Owner;
        _barrierFor = epoch;
        BarriersIssued++;
        if (!port.Barrier(() => BarrierDone(port, owner, epoch, generation)))
        {
            // No native view yet: nothing of MAUI's is pending either.
            _barrierFor = -1;
            _barrierPending = false;
        }
    }

    private void BarrierDone(ICarouselPort port, object? owner, int epoch, int generation)
    {
        if (_retired || generation != _generation || !ReferenceEquals(port, _port) || !ReferenceEquals(port.Owner, owner)) return;
        if (epoch != _epoch)
        {
            // Obsolete: a newer transaction owns the window. It neither projects nor releases.
            if (_barrierFor != _epoch) IssueBarrier();
            return;
        }
        _barrierFor = -1;
        _barrierPending = false;
        if (_windowInput)
        {
            // A drag, a settle or a ref command during the window: the user's, reported at rest.
            _windowInput = false;
            _ownTarget = null;
            ArmPoll();
            return;
        }
        if (_transactionPending) { Commit(false); return; }
        // MAUI's work for the change is done: the declaration is projected now, as a jump, unless the
        // platform moves (then at rest, still as a jump). Our own same-turn jump's poll doesn't defer it.
        _jumpNext = true;
        if (port.Sample() is { Moving: false } sample && !_awaitingAnswer)
        {
            _jumpNext = false;
            if (_declared() is { } target && (sample.Shown != target || !sample.Aligned)) OwnScroll(target, animate: false);
            else _lastRest = sample.Shown;
            return;
        }
        ArmPoll();
    }

    /// <summary>Projects the declaration if the carousel rests elsewhere or between items (rule 2).</summary>
    private void Project(bool animate)
    {
        if (_retired || _port is not { } port || _transactionPending || _barrierPending || _awaitingAnswer) return;
        if (port.Sample() is not { } sample)
        {
            ArmPoll();
            return;
        }
        if (sample.Moving || _polling)
        {
            // Moving (the user's, or an own projection still settling), or not yet verified at rest:
            // projected at rest (AtRest finds the declaration changed while the item stayed).
            ArmPoll();
            return;
        }
        if (_declared() is not { } target) return;
        if (sample.Shown != target || !sample.Aligned) OwnScroll(target, animate);
        else _lastRest = sample.Shown;
    }

    private void OwnScroll(int target, bool animate)
    {
        if (_port is not { } port) return;
        if (_jumpNext)
        {
            _jumpNext = false;
            animate = false;
        }
        _ownRetries = _ownTarget == target ? _ownRetries + 1 : 0;
        if (_ownRetries > 2)
        {
            // The platform doesn't land where it's asked to: stop, rather than scroll forever.
            Microsoft.UI.Reactor.Core.Diagnostics.DiagnosticLog.Warning(Microsoft.UI.Reactor.Core.Diagnostics.LogCategory.Reactor,
                "CarouselView.Position", $"The carousel didn't come to rest at item {target} after {_ownRetries} scrolls; giving up until the next commit.");
            _ownTarget = null;
            _ownRetries = 0;
            return;
        }
        _ownTarget = target;
        _userDuringOwn = false;
        Projections++;
        _inOwnScroll = true;
        try
        {
            _writeSuppressed(() => port.ScrollTo(target, animate));
        }
        finally
        {
            _inOwnScroll = false;
        }
        ArmPoll();
    }

    private void ArmPoll()
    {
        if (_retired || _polling || _port is not { } port) return;
        _polling = true;
        _stableFrames = 0;
        _idleFrames = 0;
        _last = null;
        var generation = _generation;
        port.NextFrame(() => Frame(port, generation));
    }

    private void Frame(ICarouselPort port, int generation)
    {
        if (_retired || generation != _generation || !ReferenceEquals(port, _port) || !_polling) return;
        var sample = port.Sample();
        if (sample is not { } now)
        {
            // Not laid out: keep looking for a while (a handler arriving), then stop.
            if (++_idleFrames > 600) { _polling = false; return; }
            port.NextFrame(() => Frame(port, generation));
            return;
        }
        if (now.UserDragging)
        {
            if (_ownTarget is not null) _userDuringOwn = true;
            if (_barrierPending) _windowInput = true;
        }
        var moved = now.Moving || _last is not { } last || last.Shown != now.Shown || Math.Abs(last.Offset - now.Offset) > 0.5;
        _last = now;
        if (moved)
        {
            _stableFrames = 0;
            port.NextFrame(() => Frame(port, generation));
            return;
        }
        // Two still frames make a rest; short of an own projection's target, a longer stillness (an
        // animation that hasn't started yet looks still too).
        var required = _ownTarget is { } own && !(now.Shown == own && now.Aligned) ? 12 : 2;
        if (++_stableFrames < required)
        {
            port.NextFrame(() => Frame(port, generation));
            return;
        }
        _polling = false;
        AtRest(now);
    }

    /// <summary>A verified rest (rule 4): apply a waiting transaction, end an own projection, or report.</summary>
    private void AtRest(CarouselSample sample)
    {
        if (_awaitingAnswer) return;
        if (_transactionPending)
        {
            // The user moved during the transaction's wait: that comes first (mapped to the new items
            // by key, by the handler); the answer's commit applies the transaction.
            if (sample.Shown != _lastRest && _ownTarget is null)
            {
                _lastRest = sample.Shown;
                _awaitingAnswer = true;
                _report(sample.Shown);
                return;
            }
            ApplyTransaction();
            return;
        }
        // The barrier's completion acts for the window.
        if (_barrierPending) return;
        var declared = _declared();
        if (_ownTarget is { } own && !_userDuringOwn)
        {
            // An own projection's settle (rule 5), at its target or short of it if a newer declaration
            // superseded it: never reported; the newest declaration is projected.
            _lastRest = sample.Shown;
            if (declared is { } d && (sample.Shown != d || !sample.Aligned))
            {
                // Keep the target for the retry count only when it's the same projection again.
                if (d != own) _ownTarget = null;
                OwnScroll(d, _animated());
            }
            else
            {
                _ownTarget = null;
                _ownRetries = 0;
            }
            return;
        }
        _ownTarget = null;
        _userDuringOwn = false;
        var moved = sample.Shown != _lastRest;
        _lastRest = sample.Shown;
        if (declared is not { } target) return;
        if (sample.Shown != target)
        {
            // Rested on another item than before: input, reported once (the restore that follows projects
            // the app's answer: refused, back to the declaration; accepted, aligned). Same item as before:
            // the declaration changed meanwhile, and is projected.
            if (moved)
            {
                _awaitingAnswer = true;
                _report(sample.Shown);
            }
            else OwnScroll(target, _animated());
            return;
        }
        // At the declared item, but between pages (rule 6): aligned without a report.
        if (!sample.Aligned) OwnScroll(target, animate: true);
    }
}
