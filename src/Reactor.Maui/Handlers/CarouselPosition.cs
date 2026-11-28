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

    /// <summary>Scrolls to an item, centered (iOS and Mac Catalyst: the page scroller's offset; Android: MAUI's <c>ItemsView.ScrollTo</c>).</summary>
    void ScrollTo(int index, bool animate);

    /// <summary>
    /// The platform says the port's last <see cref="ScrollTo"/> is still on its way: started and not
    /// finished, or not yet started. Bounded in time by the port (it turns false after a bound above
    /// the platform's longest scroll animation), so a scroll that never runs can't hold it forever.
    /// </summary>
    bool ScrollPending { get; }

    /// <summary>Runs <paramref name="completion"/> once MAUI's and the platform's work for the items changes
    /// published so far has finished; <see langword="false"/> when it can't (no native view yet).</summary>
    bool Barrier(Action completion);

    /// <summary>Runs <paramref name="callback"/> on the next display frame.</summary>
    void NextFrame(Action callback);

    /// <summary>Calls <paramref name="onInteraction"/> when the platform's scroller starts or changes an interaction.</summary>
    void Watch(Action onInteraction);
}

/// <summary>The motion region of the carousel's state machine (spec 070 §5.4).</summary>
internal enum CarouselMotion
{
    /// <summary>At a verified rest (or nothing seen yet); nothing polls.</summary>
    Resting,

    /// <summary>A signal arrived: the poll watches for the next verified rest; nothing is written.</summary>
    Watching,

    /// <summary>Reactor's own <c>ScrollTo</c> is in flight; its rest is never reported.</summary>
    OwnProjecting,

    /// <summary>A report is on its way to the app; nothing is projected or published until its answer.</summary>
    AwaitingAnswer,
}

/// <summary>
/// The controlled position of a carousel (spec 070 §5.4), an explicit state machine in two regions.
/// <para>
/// <b>Motion</b> (<see cref="CarouselMotion"/>): <c>Resting{rest}</c>, <c>Watching{input}</c>,
/// <c>OwnProjecting{target, scrolls}</c> and <c>AwaitingAnswer{seq}</c>. <b>Items</b>: published, with
/// a <i>deferred</i> transaction waiting for rest, and a <i>barrier window</i> open from a transaction
/// until its epoch's completion. The platform's resting item is the truth: Reactor writes only from a
/// verified rest (through the port's <c>ScrollTo</c>), publishes only at rest, and reports the item the
/// platform rests on when the rest is input. Input is what the platform shows: a finger
/// (<c>UserDragging</c>), motion while the port has no scroll of its own pending, or a ref's
/// <c>ScrollToRequested</c>. Nothing else is inferred about who moved the carousel.
/// </para>
/// </summary>
internal sealed class CarouselPositionController
{
    /// <summary>No verified rest since the indices changed (an items change, a new platform view): the next rest is projected unless input was seen.</summary>
    internal const int Unknown = -1;

    /// <summary>Scrolls toward one target before giving up on a platform that never lands there.</summary>
    internal const int MaxScrolls = 3;

    private const int IdleFrameLimit = 600;

    private readonly Func<int?> _declared;
    private readonly Func<bool> _animated;
    private readonly Action<Action> _writeSuppressed;
    private readonly Action<int, int> _report;
    private readonly Action _publish;
    private ICarouselPort? _port;
    private bool _retired;

    // ── Motion region ──
    private CarouselMotion _motion;
    private int _rest;
    private bool _input;
    private int _target;
    private bool _targetAnimate;
    private int _scrolls;
    private long _ownSince;
    private int _reportSeq;
    private bool _reportRan;
    private bool _inOwnScroll;

    // A declaration the platform never landed on: not projected again until the declaration or the items change.
    private int? _gaveUpOn;

    // The next projection is a jump: it follows an items transaction or a new platform view.
    private bool _jumpNext;

    // ── Items region ──
    private bool _deferred;
    private int _epoch;
    private int _barrierEpoch = -1;
    private int _settledEpoch;
    private bool _windowOpen;
    private bool _windowInput;

    // ── The poll (the clock of the motion region) ──
    private bool _polling;
    private int _pollId;
    private int _still;
    private int _idle;
    private CarouselSample? _last;

    /// <param name="declared">The live declaration, clamped to the published items; <see langword="null"/> for none.</param>
    /// <param name="animated">Whether a declared change animates (the element's <c>IsScrollAnimated</c>).</param>
    /// <param name="writeSuppressed">Runs a write in the handler's echo scope.</param>
    /// <param name="report">Reports a resting item (an index in the published items) with its sequence number. The handler posts it,
    /// calls <see cref="ReportRan"/> with the number when the post runs, and restores after it, so the answer's commit arrives.</param>
    /// <param name="publish">Publishes the live element's items and layout to MAUI, in the echo scope.</param>
    /// <param name="mounted">The position written at mount: where the platform rests first.</param>
    public CarouselPositionController(Func<int?> declared, Func<bool> animated, Action<Action> writeSuppressed, Action<int, int> report, Action publish, int mounted)
    {
        _rest = mounted;
        _declared = declared;
        _animated = animated;
        _writeSuppressed = writeSuppressed;
        _report = report;
        _publish = publish;
    }

    /// <summary>
    /// The platform port. Setting a new one (handler replacement) retires the old view's poll and barrier:
    /// the new view starts at MAUI's position, so its first rest is projected (a jump), not reported.
    /// </summary>
    public ICarouselPort? Port
    {
        get => _port;
        set
        {
            if (ReferenceEquals(_port, value)) return;
            _port = value;
            _pollId++;
            _polling = false;
            _windowOpen = false;
            _barrierEpoch = -1;
            _windowInput = false;
            _rest = Unknown;
            _input = false;
            _jumpNext = true;
            // A give-up was the old view's: the new one is asked again.
            _gaveUpOn = null;
            if (_motion is CarouselMotion.Watching or CarouselMotion.OwnProjecting) _motion = CarouselMotion.Resting;
            if (_port is null) return;
            _port.Watch(OnTrigger);
            if (_motion == CarouselMotion.Resting) _motion = CarouselMotion.Watching;
            ArmPoll();
        }
    }

    /// <summary>The clock (Stopwatch timestamps): the watchdog on the port's pending signal reads it; tests replace it.</summary>
    internal Func<long> Ticks { get; set; } = System.Diagnostics.Stopwatch.GetTimestamp;

    // Witnesses for tests and fixtures.
    internal CarouselMotion Motion => _motion;
    internal int Projections { get; private set; }
    internal int Publications { get; private set; }
    internal int BarriersIssued { get; private set; }
    internal bool TransactionPending => _deferred;
    internal bool Polling => _polling;
    internal bool AwaitingAnswer => _motion == CarouselMotion.AwaitingAnswer;
    internal bool BarrierPending => _windowOpen;
    internal int? OwnTarget => _motion == CarouselMotion.OwnProjecting ? _target : null;
    internal int? GaveUpOn => _gaveUpOn;

    /// <summary>
    /// A commit of the element (not its mount). <paramref name="transaction"/>: the items or layout differ
    /// from the published ones. Only the commit after the pending report ran answers it.
    /// </summary>
    public void Commit(bool transaction)
    {
        if (_retired) return;
        if (transaction) _deferred = true;
        if (_gaveUpOn is { } stuck && _declared() != stuck) _gaveUpOn = null;
        switch (_motion)
        {
            case CarouselMotion.AwaitingAnswer:
                if (!_reportRan) return;
                // The answer: from a frame-verified rest (a ref's animation may move it with no motion
                // flag), keeping any input seen while the report was out.
                _motion = CarouselMotion.Watching;
                if (_port is null) Rested(null);
                else ArmPoll();
                return;
            case CarouselMotion.Resting:
                Decide(_port?.Sample());
                return;
            case CarouselMotion.Watching:
                // Nothing laid out can't move under a finger: a transaction applies (an empty carousel's first items).
                if (_deferred && _port?.Sample() is null) ApplyTransaction();
                return;
            default:
                // OwnProjecting: its rest projects the newest declaration.
                return;
        }
    }

    /// <summary>The report numbered <paramref name="seq"/> was delivered: the next commit answers it.</summary>
    public void ReportRan(int seq)
    {
        if (_motion == CarouselMotion.AwaitingAnswer && seq == _reportSeq) _reportRan = true;
    }

    /// <summary>A MAUI event or a platform interaction: look at the platform until it rests.</summary>
    public void OnTrigger()
    {
        if (_retired) return;
        if (_motion == CarouselMotion.Resting)
        {
            _motion = CarouselMotion.Watching;
            _input = false;
        }
        ArmPoll();
    }

    /// <summary>MAUI's <c>ScrollToRequested</c>: outside Reactor's own scroll, a ref's command, which is input.</summary>
    public void OnScrollToRequested()
    {
        if (_retired || _inOwnScroll) return;
        if (_motion == CarouselMotion.Resting) _motion = CarouselMotion.Watching;
        Input();
        ArmPoll();
    }

    /// <summary>Unmount: nothing is written, published or reported after it.</summary>
    public void Retire()
    {
        _retired = true;
        _polling = false;
        _pollId++;
    }

    // Input observed: an own projection becomes the user's, a watched rest is input, the window has input.
    private void Input()
    {
        if (_motion == CarouselMotion.OwnProjecting) _motion = CarouselMotion.Watching;
        if (_motion is CarouselMotion.Watching or CarouselMotion.AwaitingAnswer) _input = true;
        if (_windowOpen) _windowInput = true;
        _jumpNext = false;
        // The user moved it: whatever Reactor gave up on is asked again once the input is answered.
        _gaveUpOn = null;
    }

    private bool IsInput(CarouselSample sample, ICarouselPort port) =>
        sample.UserDragging || sample.Moving && !OwnPending(port);

    // The port says its own scroll is on its way, believed for at most ScrollBridge.SettleBound after the
    // scroll started (a watchdog of the controller's own, whatever the port says).
    private bool OwnPending(ICarouselPort port) =>
        _motion == CarouselMotion.OwnProjecting
        && System.Diagnostics.Stopwatch.GetElapsedTime(_ownSince, Ticks()) <= ScrollBridge.SettleBound
        && port.ScrollPending;

    // ── At a verified rest ───────────────────────────────────────────────────

    private void Rested(CarouselSample? sample)
    {
        switch (_motion)
        {
            case CarouselMotion.OwnProjecting:
                OwnRested(sample);
                return;
            case CarouselMotion.Watching:
            case CarouselMotion.Resting:
                WatchRested(sample);
                return;
            default:
                // AwaitingAnswer: the answer starts a fresh verification.
                return;
        }
    }

    // An own projection's rest: never reported.
    private void OwnRested(CarouselSample? sample)
    {
        _motion = CarouselMotion.Resting;
        if (sample is not { } now)
        {
            _rest = Unknown;
            Decide(null);
            return;
        }
        if (_windowOpen)
        {
            // MAUI's work for the change isn't done: the completion projects.
            _rest = now.Shown == _target ? _target : Unknown;
            return;
        }
        if (now.Shown == _target && now.Aligned)
        {
            _rest = _target;
            Decide(now);
            return;
        }
        if (_declared() != _target)
        {
            // Superseded by a newer declaration: wherever it stopped isn't input.
            _rest = now.Shown;
            Decide(now);
            return;
        }
        if (_scrolls >= MaxScrolls)
        {
            Microsoft.UI.Reactor.Core.Diagnostics.DiagnosticLog.Warning(Microsoft.UI.Reactor.Core.Diagnostics.LogCategory.Reactor,
                "CarouselView.Position", $"The carousel didn't come to rest at item {_target} after {_scrolls} scrolls; it stays where it is until the declaration or the items change.");
            _gaveUpOn = _target;
            _rest = now.Shown;
            // A deferred transaction still applies; the target isn't asked again (the give-up guards it).
            Decide(now);
            return;
        }
        // Short of its target or off center (interrupted): asked again, as the original was.
        _motion = CarouselMotion.OwnProjecting;
        OwnScroll(_target, _targetAnimate);
    }

    private void WatchRested(CarouselSample? sample)
    {
        _motion = CarouselMotion.Resting;
        var input = _input;
        _input = false;
        if (sample is not { } now)
        {
            Decide(null);
            return;
        }
        // In the barrier window the completion decides (input there is kept in _windowInput).
        if (_windowOpen) return;
        var moved = _rest == Unknown ? input : now.Shown != _rest;
        // Moved, to an item other than the declared one: input, reported once. With a deferred
        // transaction the declaration is in the new items' indices: any move is reported first.
        if (moved && (_deferred || now.Shown != _declared()))
        {
            Report(now.Shown);
            return;
        }
        _rest = now.Shown;
        Decide(now);
    }

    private void Report(int shown)
    {
        _rest = shown;
        _motion = CarouselMotion.AwaitingAnswer;
        _reportRan = false;
        _gaveUpOn = null;
        _report(shown, ++_reportSeq);
    }

    // From Resting: apply a deferred transaction, or project the declaration.
    private void Decide(CarouselSample? sample)
    {
        if (sample is { } moving && (moving.Moving || moving.UserDragging))
        {
            // Something moves that no poll has verified: watch it (it isn't Reactor's).
            _motion = CarouselMotion.Watching;
            Input();
            ArmPoll();
            return;
        }
        // Input in the open window is reported (at its completion) and answered before the next
        // transaction publishes: publishing would move the user's item away (deviation 4).
        if (_deferred && !(_windowOpen && _windowInput))
        {
            ApplyTransaction();
            return;
        }
        if (_windowOpen || sample is not { } now) return;
        if (_rest != Unknown && now.Shown != _rest)
        {
            // It changed without a verified rest: verify first.
            _motion = CarouselMotion.Watching;
            ArmPoll();
            return;
        }
        _rest = now.Shown;
        if (_declared() is { } target && (now.Shown != target || !now.Aligned) && _gaveUpOn != target)
        {
            OwnScroll(target, _animated());
            return;
        }
        _jumpNext = false;
    }

    // ── Items transactions and their barriers ────────────────────────────────

    private void ApplyTransaction()
    {
        _deferred = false;
        _publish();
        Publications++;
        _epoch++;
        _windowInput = false;
        _gaveUpOn = null;
        // Indices moved with the items: the same-turn jump's rest (OwnRested) sets the rest, Unknown
        // unless it landed; without a port, the port's arrival does.
        // The same-turn jump makes MAUI's Android carousel skip its deferred correction (its ScrollTo
        // counter), so no frame shows item 0; on iOS the published items already show the right item.
        if (_declared() is { } target) OwnScroll(target, animate: false);
        _jumpNext = true;
        IssueBarrier();
    }

    private void IssueBarrier()
    {
        if (_port is not { } port)
        {
            _windowOpen = false;
            return;
        }
        var epoch = _epoch;
        var owner = port.Owner;
        _barrierEpoch = epoch;
        _windowOpen = true;
        BarriersIssued++;
        if (!port.Barrier(() => BarrierDone(port, owner, epoch)))
        {
            // No native view yet: nothing of MAUI's is pending either.
            _barrierEpoch = -1;
            _windowOpen = false;
        }
    }

    private void BarrierDone(ICarouselPort port, object? owner, int epoch)
    {
        if (_retired || !ReferenceEquals(port, _port) || !ReferenceEquals(port.Owner, owner)) return;
        if (epoch != _epoch)
        {
            // Obsolete: a newer transaction owns the window. It neither projects nor closes it; it
            // reissues the current epoch's barrier when that one couldn't be issued.
            if (_barrierEpoch != _epoch && _settledEpoch != _epoch) IssueBarrier();
            return;
        }
        _settledEpoch = epoch;
        _barrierEpoch = -1;
        _windowOpen = false;
        if (_windowInput)
        {
            // Input during the window (a drag, a settle, a ref command): the user's, reported at rest.
            _windowInput = false;
            if (_motion is CarouselMotion.Resting or CarouselMotion.OwnProjecting) _motion = CarouselMotion.Watching;
            if (_motion == CarouselMotion.Watching) _input = true;
            ArmPoll();
            return;
        }
        // Without input, whatever the platform shows is MAUI's processing: the declaration is projected (a
        // jump). Motion starting just now is Decide's to see. Watching or OwnProjecting: their rest decides.
        if (_motion == CarouselMotion.Resting)
        {
            _rest = Unknown;
            Decide(port.Sample());
        }
    }

    // ── Writes ───────────────────────────────────────────────────────────────

    private void OwnScroll(int target, bool animate)
    {
        if (_port is not { } port) return;
        if (_jumpNext)
        {
            _jumpNext = false;
            animate = false;
        }
        _scrolls = _motion == CarouselMotion.OwnProjecting && _target == target ? _scrolls + 1 : 1;
        _motion = CarouselMotion.OwnProjecting;
        _target = target;
        _targetAnimate = animate;
        _input = false;
        _ownSince = Ticks();
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

    // ── The poll ─────────────────────────────────────────────────────────────

    private void ArmPoll()
    {
        if (_retired || _polling || _port is not { } port) return;
        _polling = true;
        _still = 0;
        _idle = 0;
        _last = null;
        var id = ++_pollId;
        port.NextFrame(() => Frame(port, id));
    }

    private void Frame(ICarouselPort port, int id)
    {
        if (_retired || id != _pollId || !ReferenceEquals(port, _port) || !_polling) return;
        if (port.Sample() is not { } now)
        {
            // Nothing laid out: keep looking for a while (a handler arriving), then rest on nothing.
            if (++_idle > IdleFrameLimit)
            {
                _polling = false;
                Rested(null);
                return;
            }
            port.NextFrame(() => Frame(port, id));
            return;
        }
        _idle = 0;
        if (IsInput(now, port)) Input();
        var moved = now.Moving || _last is not { } last || last.Shown != now.Shown || Math.Abs(last.Offset - now.Offset) > 0.5;
        _last = now;
        // Rest: two still frames, and no scroll of the port's own on its way.
        if (moved || OwnPending(port) || ++_still < 2)
        {
            if (moved) _still = 0;
            port.NextFrame(() => Frame(port, id));
            return;
        }
        _polling = false;
        Rested(now);
    }
}
