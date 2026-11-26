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
        else
        {
            ApplyDescription(view, previous, next);
        }
        Props.ApplyText(view, previous, next);
        if (view.IsChecked != next.IsChecked) context.WriteSuppressed(view, () => view.IsChecked = next.IsChecked);
        RadioGroups.For(context.Node).Register(context, view, next);
    }

    // With a template, MAUI's description falls back to Content.ToString(), a type name for a view: the
    // text Content is the description, and a button with neither logs a warning once.
    private static void ApplyDescription(ReactorRadioButton view, RadioButtonElement? previous, RadioButtonElement next)
    {
        if (!string.IsNullOrEmpty(next.Content))
        {
            if (previous?.Content != next.Content && next.Modifiers?.SemanticDescription is null)
                SemanticProperties.SetDescription(view, next.Content);
            return;
        }
        if (next.Modifiers?.SemanticDescription is null && !view.WarnedDescription)
        {
            view.WarnedDescription = true;
            DiagnosticLog.Warning(LogCategory.Reactor, "RadioButton.Description",
                "A radio button with view content has no accessible name; set .Semantics(description: ...) on it.");
        }
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

    private sealed record Entry(ReactorRadioButton View, HandlerContext<RadioButtonElement> Context, GroupKey Key);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ReactorRoot, RadioGroups> Tables = new();

    private readonly ReactorRoot _root;
    private readonly Dictionary<ReactorRadioButton, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<GroupKey, List<ReactorRadioButton>> _groups = [];
    private readonly Dictionary<GroupKey, int> _generations = [];
    private readonly Queue<(ReactorRadioButton View, bool Value)> _queued = new();
    private readonly HashSet<GroupKey> _dirty = [];
    private readonly HashSet<GroupKey> _warned = [];
    private bool _dispatching;
    private bool _drainPosted;
    private bool _checkPosted;

    private RadioGroups(ReactorRoot root) => _root = root;

    public static RadioGroups For(Node node) => Tables.GetValue(node.Root.TopRoot, static root => new RadioGroups(root));

    /// <summary>Unsuppressed notifications that started a transaction or were queued (a test witness).</summary>
    internal int Transactions { get; private set; }

    private static GroupKey KeyOf(Node node, string? name)
    {
        if (!string.IsNullOrEmpty(name)) return new(Microsoft.UI.Reactor.Maui.Hosting.SystemBack.PageOf(node) ?? node.Root.TopRoot, name);
        for (var up = node.Parent; up is not null; up = up.Parent)
            if (up.Kind == NodeKind.Host) return new(up, null);
        // At the top of its root: a cell, slot or swipe item tree is its own scope.
        return new(node.Root, null);
    }

    public void Register(HandlerContext<RadioButtonElement> context, ReactorRadioButton view, RadioButtonElement element)
    {
        view.Groups = this;
        var key = KeyOf(context.Node, element.GroupName);
        if (_entries.TryGetValue(view, out var existing))
        {
            if (existing.Key.Equals(key))
            {
                MarkDirty(key);
                return;
            }
            Remove(existing);
        }
        _entries[view] = new Entry(view, context, key);
        if (!_groups.TryGetValue(key, out var members)) _groups[key] = members = [];
        members.Add(view);
        MarkDirty(key);
    }

    public void Unregister(ReactorRadioButton view)
    {
        if (_entries.TryGetValue(view, out var entry)) Remove(entry);
    }

    private void Remove(Entry entry)
    {
        _entries.Remove(entry.View);
        if (_groups.TryGetValue(entry.Key, out var members))
        {
            members.Remove(entry.View);
            if (members.Count == 0) _groups.Remove(entry.Key);
        }
        MarkDirty(entry.Key);
    }

    private bool IsMember(Entry entry) => _entries.TryGetValue(entry.View, out var current) && current.Key.Equals(entry.Key);

    public void OnCheckedChanged(HandlerContext<RadioButtonElement> context, ReactorRadioButton view, bool value)
    {
        if (context.IsEchoing(view)) return;
        Transactions++;
        if (_dispatching)
        {
            // Never reentrant: a callback's own radio writes run after this transaction and MAUI's
            // setters have unwound. Queueing a check supersedes a same-group transaction.
            _queued.Enqueue((view, value));
            if (value && _entries.TryGetValue(view, out var queued)) Bump(queued.Key);
            if (!_drainPosted)
            {
                _drainPosted = true;
                _root.Post(Drain);
            }
            return;
        }
        Process(view, value);
    }

    private int Bump(GroupKey key)
    {
        var next = _generations.TryGetValue(key, out var current) ? current + 1 : 1;
        _generations[key] = next;
        return next;
    }

    private void Drain()
    {
        _drainPosted = false;
        while (_queued.Count > 0 && !_dispatching)
        {
            var (view, value) = _queued.Dequeue();
            if (!_entries.ContainsKey(view)) continue;
            // Re-read the native state: a later write superseded an entry that no longer holds.
            if (view.IsChecked == value) Process(view, value);
        }
    }

    private void Process(ReactorRadioButton view, bool value)
    {
        if (!_entries.TryGetValue(view, out var entry)) return;
        if (!value)
        {
            // An uncheck outside a write (a ref): reported and restored, as any input.
            Run([entry], [(entry, false)], entry.Key, Bump(entry.Key));
            return;
        }
        var generation = Bump(entry.Key);
        var peers = new List<Entry>();
        foreach (var member in _groups[entry.Key])
            if (!ReferenceEquals(member, view) && member.IsChecked && _entries.TryGetValue(member, out var peer)) peers.Add(peer);
        // The native group is settled before any callback, in MAUI's order: peers unchecked first.
        foreach (var peer in peers) peer.Context.WriteSuppressed(peer.View, () => peer.View.IsChecked = false);
        var reports = new List<(Entry, bool)>(peers.Count + 1);
        foreach (var peer in peers) reports.Add((peer, false));
        reports.Add((entry, true));
        Run([.. peers, entry], reports, entry.Key, generation);
    }

    private void Run(List<Entry> participants, List<(Entry Entry, bool Value)> reports, GroupKey key, int generation)
    {
        _dispatching = true;
        try
        {
            foreach (var (target, value) in reports)
            {
                if (_generations[key] != generation) break;
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
            foreach (var participant in participants)
                if (participant.Context.IsMounted) participant.Context.RestoreAfterEvent();
        }
    }

    private void MarkDirty(GroupKey key)
    {
        _dirty.Add(key);
        if (_checkPosted) return;
        _checkPosted = true;
        // After the pass: a replacement mounts before the old button unmounts.
        _root.Post(CheckConflicts);
    }

    private void CheckConflicts()
    {
        _checkPosted = false;
        foreach (var key in _dirty)
        {
            var declared = 0;
            if (_groups.TryGetValue(key, out var members))
                foreach (var member in members)
                    if (_entries.TryGetValue(member, out var entry) && entry.Context.Live is { IsChecked: true }) declared++;
            if (declared < 2)
            {
                _warned.Remove(key);
            }
            else if (_warned.Add(key))
            {
                DiagnosticLog.Warning(LogCategory.Reactor, "RadioButton.Group",
                    $"{declared} radio buttons in the group '{key.Name ?? "(unnamed)"}' are declared checked; all are shown checked.");
            }
        }
        _dirty.Clear();
    }
}
