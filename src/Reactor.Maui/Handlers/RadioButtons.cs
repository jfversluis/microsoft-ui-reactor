using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Hosting;
using MauiRadioButton = Microsoft.Maui.Controls.RadioButton;

namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>The radio button Reactor renders: its group registration and one-time warnings.</summary>
internal sealed class ReactorRadioButton : MauiRadioButton
{
    public RadioGroups? Groups;
    public bool WarnedDescription;
}

/// <summary>
/// Radio buttons with text or view content (spec 070 §3.6). Grouping is Reactor's, not MAUI's: each
/// native button gets a unique internal group name, so MAUI finds no peer, and peers come from
/// <see cref="RadioGroups"/>.
/// </summary>
internal sealed class RadioButtonHandler : ViewHandler<RadioButtonElement, ReactorRadioButton>
{
    /// <summary>A template that is only the content: the radio button looks like its content.</summary>
    internal static readonly ControlTemplate ContentOnly = new(() => new ContentPresenter());

    protected override ReactorRadioButton Create(HandlerContext<RadioButtonElement> context, RadioButtonElement element)
    {
        var view = new ReactorRadioButton { GroupName = "reactor-" + Guid.NewGuid().ToString("N") };
        // Before the handler exists: Android picks its platform view (a native button, or a content
        // group for a template) when the handler is created.
        if (element.ContentElement is not null)
            view.ControlTemplate = element.IsIndicatorVisible ? MauiRadioButton.DefaultTemplate : ContentOnly;
        view.CheckedChanged += (_, e) => RadioGroups.For(context.Node).OnCheckedChanged(context, view, e.Value);
        return view;
    }

    public override bool CanUpdate(RadioButtonElement oldElement, RadioButtonElement newElement) =>
        (oldElement.ContentElement is null) == (newElement.ContentElement is null)
        && (newElement.ContentElement is null || oldElement.IsIndicatorVisible == newElement.IsIndicatorVisible);

    protected override void Apply(HandlerContext<RadioButtonElement> context, ReactorRadioButton view, RadioButtonElement? previous, RadioButtonElement next)
    {
        if (next.ContentElement is null)
        {
            if (previous?.Content != next.Content) view.Content = next.Content;
        }
        else if (string.IsNullOrEmpty(next.Content) && next.Modifiers?.SemanticDescription is null && !view.WarnedDescription)
        {
            view.WarnedDescription = true;
            DiagnosticLog.Warning(LogCategory.Reactor, "RadioButton.Description",
                "A radio button with view content has no accessible name; set .Semantics(description: ...) on it.");
        }
        Props.ApplyText(view, previous, next);
        var groups = RadioGroups.For(context.Node);
        // A check queued while a transaction dispatched hasn't been reported yet: a render in between
        // must not overwrite it before it is (the queue restores the declaration if it drops it).
        if (view.IsChecked != next.IsChecked && !groups.IsQueued(view))
            context.WriteSuppressed(view, () => view.IsChecked = next.IsChecked);
        groups.Register(context, view, next);
    }

    // With a template, MAUI's description falls back to Content.ToString(), a type name for a view. The
    // effective description is the declared one, else the text Content; it is written after the
    // modifiers, which would otherwise clear it when a description modifier is removed.
    protected override void AfterModifiers(HandlerContext<RadioButtonElement> context, ReactorRadioButton view, RadioButtonElement element)
    {
        if (element.ContentElement is null) return;
        var effective = element.Modifiers?.SemanticDescription ?? (string.IsNullOrEmpty(element.Content) ? null : element.Content);
        if (SemanticProperties.GetDescription(view) != effective) SemanticProperties.SetDescription(view, effective);
    }

    public override void Unmount(ReactorRadioButton control) => control.Groups?.Unregister(control);

    public override IChildrenStrategy<RadioButtonElement, ReactorRadioButton> Children { get; } =
        ChildrenStrategy.Single<RadioButtonElement, ReactorRadioButton, object>(
            static e => e.ContentElement,
            static (radio, content) =>
            {
                // A text radio button's content is its text, written by Apply.
                if (content is not null) radio.Content = Props.AsView(content);
            });
}

/// <summary>
/// The radio groups of one top-level root and the child roots it created (spec 070 §3.6). A named
/// group is scoped to its page; an unnamed one to the nearest host ancestor within the button's own
/// root, a child root being a boundary. A user's check is one transaction that settles the native
/// group before any callback; checks that arrive while a transaction dispatches are queued and run on
/// a posted turn, because MAUI delays writes to a radio button whose setter is still active.
/// </summary>
internal sealed class RadioGroups
{
    private readonly record struct GroupKey(object Scope, string? Name);

    /// <summary>One group's members and transaction generation. It goes with its last member, and its key
    /// with it, so a removed page or layout (a key's scope) isn't kept alive.</summary>
    private sealed class Group(GroupKey key)
    {
        public readonly GroupKey Key = key;
        public readonly List<ReactorRadioButton> Members = [];
        public int Generation;
    }

    private sealed record Entry(ReactorRadioButton View, HandlerContext<RadioButtonElement> Context, Group Group);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ReactorRoot, RadioGroups> Tables = new();

    private readonly ReactorRoot _root;
    private readonly Dictionary<ReactorRadioButton, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<GroupKey, Group> _groups = [];
    private readonly Queue<(ReactorRadioButton View, bool Value)> _queued = new();
    private readonly HashSet<Group> _dirty = [];
    private readonly HashSet<Group> _warned = [];
    private bool _dispatching;
    private bool _drainPosted;
    private bool _checkPosted;

    private RadioGroups(ReactorRoot root) => _root = root;

    public static RadioGroups For(Node node) => Tables.GetValue(node.Root.TopRoot, static root => new RadioGroups(root));

    /// <summary>Unsuppressed notifications that started a transaction or were queued (a test witness).</summary>
    internal int Transactions { get; private set; }

    /// <summary>The number of live groups (a test witness).</summary>
    internal int GroupCount => _groups.Count;

    private static GroupKey KeyOf(Node node, string? name)
    {
        if (!string.IsNullOrEmpty(name)) return new(Microsoft.UI.Reactor.Maui.Hosting.SystemBack.PageOf(node) ?? node.Root.TopRoot, name);
        for (var up = node.Parent; up is not null; up = up.Parent)
            if (up.Kind == NodeKind.Host) return new(up, null);
        // At the top of its root: a cell, slot or swipe item tree is its own scope.
        return new(node.Root, null);
    }

    public bool IsQueued(ReactorRadioButton view)
    {
        foreach (var (queued, _) in _queued)
            if (ReferenceEquals(queued, view)) return true;
        return false;
    }

    public void Register(HandlerContext<RadioButtonElement> context, ReactorRadioButton view, RadioButtonElement element)
    {
        view.Groups = this;
        var key = KeyOf(context.Node, element.GroupName);
        if (_entries.TryGetValue(view, out var existing))
        {
            if (existing.Group.Key.Equals(key))
            {
                MarkDirty(existing.Group);
                return;
            }
            Remove(existing);
        }
        if (!_groups.TryGetValue(key, out var group)) _groups[key] = group = new Group(key);
        group.Members.Add(view);
        _entries[view] = new Entry(view, context, group);
        MarkDirty(group);
    }

    public void Unregister(ReactorRadioButton view)
    {
        if (_entries.TryGetValue(view, out var entry)) Remove(entry);
    }

    private void Remove(Entry entry)
    {
        _entries.Remove(entry.View);
        var group = entry.Group;
        group.Members.Remove(entry.View);
        if (group.Members.Count == 0)
        {
            _groups.Remove(group.Key);
            _warned.Remove(group);
            _dirty.Remove(group);
            // A transaction or queued check on it stops: nothing is current in a removed group.
            group.Generation++;
        }
        else
        {
            MarkDirty(group);
        }
    }

    private bool IsMember(Entry entry) => _entries.TryGetValue(entry.View, out var current) && ReferenceEquals(current.Group, entry.Group);

    public void OnCheckedChanged(HandlerContext<RadioButtonElement> context, ReactorRadioButton view, bool value)
    {
        if (context.IsEchoing(view)) return;
        Transactions++;
        if (_dispatching)
        {
            // Never reentrant: a callback's own radio writes run after this transaction and MAUI's
            // setters have unwound. Queueing a check supersedes a same-group transaction.
            _queued.Enqueue((view, value));
            if (value && _entries.TryGetValue(view, out var queued)) queued.Group.Generation++;
            if (!_drainPosted)
            {
                _drainPosted = true;
                _root.Post(Drain);
            }
            return;
        }
        Process(view, value);
    }

    private void Drain()
    {
        _drainPosted = false;
        try
        {
            while (_queued.Count > 0 && !_dispatching)
            {
                var (view, value) = _queued.Dequeue();
                if (!_entries.TryGetValue(view, out var entry)) continue;
                // Re-read the native state: an entry a later write superseded is dropped, and the
                // declaration the render skipped while it was queued is restored.
                if (view.IsChecked == value) Process(view, value);
                else if (entry.Context.IsMounted) entry.Context.RestoreAfterEvent();
            }
        }
        finally
        {
            // A throw (a platform write, an app's own CheckedChanged handler) leaves the rest queued,
            // and a queued button's controlled writes are skipped: the rest runs on another turn.
            if (_queued.Count > 0 && !_drainPosted)
            {
                _drainPosted = true;
                _root.Post(Drain);
            }
        }
    }

    private void Process(ReactorRadioButton view, bool value)
    {
        if (!_entries.TryGetValue(view, out var entry)) return;
        var group = entry.Group;
        var generation = ++group.Generation;
        var participants = new List<Entry> { entry };
        // One transaction: the native settling and the dispatch. Whatever throws (a platform write, an
        // app's own CheckedChanged handler on a peer) goes to the error boundary, and every participant
        // gets its declaration back, so the group is never left half-settled.
        try
        {
            if (!value)
            {
                // An uncheck outside a write (a ref): reported and restored, as any input.
                Run([(entry, false)], group, generation);
                return;
            }
            var peers = new List<Entry>();
            foreach (var member in group.Members)
                if (!ReferenceEquals(member, view) && member.IsChecked && _entries.TryGetValue(member, out var peer)) peers.Add(peer);
            participants.AddRange(peers);
            // The native group is settled before any callback, in MAUI's order: peers unchecked first.
            foreach (var peer in peers) peer.Context.WriteSuppressed(peer.View, () => peer.View.IsChecked = false);
            var reports = new List<(Entry, bool)>(peers.Count + 1);
            foreach (var peer in peers) reports.Add((peer, false));
            reports.Add((entry, true));
            Run(reports, group, generation);
        }
        catch (Exception ex)
        {
            if (entry.Context.IsMounted) entry.Context.ReportError(ex);
        }
        finally
        {
            foreach (var participant in participants)
                if (participant.Context.IsMounted) participant.Context.RestoreAfterEvent();
        }
    }

    private void Run(List<(Entry Entry, bool Value)> reports, Group group, int generation)
    {
        _dispatching = true;
        try
        {
            foreach (var (target, value) in reports)
            {
                if (group.Generation != generation) break;
                if (target.Context.Live is not { } live || !IsMember(target)) continue;
                try
                {
                    live.OnCheckedChanged?.Invoke(value);
                }
                catch (Exception ex)
                {
                    target.Context.ReportError(ex);
                }
            }
        }
        finally
        {
            _dispatching = false;
        }
    }

    private void MarkDirty(Group group)
    {
        _dirty.Add(group);
        if (_checkPosted) return;
        _checkPosted = true;
        // After the pass: a replacement mounts before the old button unmounts.
        _root.Post(CheckConflicts);
    }

    private void CheckConflicts()
    {
        _checkPosted = false;
        foreach (var group in _dirty)
        {
            var declared = 0;
            foreach (var member in group.Members)
                if (_entries.TryGetValue(member, out var entry) && entry.Context.Live is { IsChecked: true }) declared++;
            if (declared < 2)
            {
                _warned.Remove(group);
            }
            else if (_warned.Add(group))
            {
                DiagnosticLog.Warning(LogCategory.Reactor, "RadioButton.Group",
                    $"{declared} radio buttons in the group '{group.Key.Name ?? "(unnamed)"}' are declared checked; all are shown checked.");
            }
        }
        _dirty.Clear();
    }
}
