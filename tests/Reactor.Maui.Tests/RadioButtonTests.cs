using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiRadioButton = Microsoft.Maui.Controls.RadioButton;
using System.Runtime.CompilerServices;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>Spec 070 §3.6: radio buttons with view content.</summary>
[Collection("RadioDescriptionWarnings")]
public class RadioContentTests
{
    private sealed class Card : Component<(string Name, bool Checked)>
    {
        public static readonly Dictionary<string, Action<int>> Set = [];

        public override Element Render()
        {
            var (count, setCount) = UseState(0);
            Set[Props.Name] = setCount;
            return Label($"{Props.Name} {(Props.Checked ? "on" : "off")} {count}");
        }
    }

    private static Element CardOf(string name, bool on) => Component<Card, (string, bool)>((name, on));

    private static string ContentText(MauiRadioButton radio) => radio.Content switch
    {
        MauiLabel label => label.Text,
        DirectViewHost host => ((DirectLabel)host.Children[0]).Text,
        string text => text,
        var other => other?.GetType().Name ?? "<none>",
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ViewContent_IsTheContent_WithTheTemplateItAsksFor(bool direct)
    {
        var host = new TestHost(direct ? DirectHandlers.CreateRegistry() : MauiHandlers.CreateRegistry())
            .Render(RadioButton(CardOf("light", true), true));
        var radio = host.View<MauiRadioButton>();
        Assert.Same(MauiRadioButton.DefaultTemplate, radio.ControlTemplate);
        Assert.Equal("light on 0", ContentText(radio));
        if (direct) Assert.IsType<DirectViewHost>(radio.Content);
        Card.Set["light"](2);
        host.Scheduler.Drain();

        host.Render(RadioButton(CardOf("light", false), false));
        Assert.Same(radio, host.RootView);
        Assert.Equal("light off 2", ContentText(radio));

        host.Render(RadioButton(CardOf("light", false), false) with { IsIndicatorVisible = false });
        var cardOnly = host.View<MauiRadioButton>();
        Assert.NotSame(radio, cardOnly);
        Assert.Same(RadioButtonHandler.ContentOnly, cardOnly.ControlTemplate);

        host.Render(RadioButton("text", false));
        Assert.NotSame(cardOnly, host.RootView);
        Assert.Null(host.View<MauiRadioButton>().ControlTemplate);
        Assert.Equal("text", ContentText(host.View<MauiRadioButton>()));
    }

    [Fact]
    public void TheAccessibleName_IsTheTextOrTheDescription_AndItsAbsenceWarnsOnce()
    {
        using var warnings = new WarningListener("RadioButton.Description");
        var named = new TestHost(MauiHandlers.CreateRegistry()).Render(RadioButton(CardOf("a", false), false) with { Content = "Light theme" });
        Assert.Equal("Light theme", SemanticProperties.GetDescription(named.View<MauiRadioButton>()));
        var described = new TestHost(MauiHandlers.CreateRegistry()).Render(RadioButton(CardOf("b", false), false).Semantics(description: "Dark"));
        Assert.Equal("Dark", SemanticProperties.GetDescription(described.View<MauiRadioButton>()));
        Assert.Empty(warnings.Warnings);

        var anonymous = new TestHost(MauiHandlers.CreateRegistry()).Render(RadioButton(CardOf("c", false), false));
        anonymous.Render(RadioButton(CardOf("c", true), true));
        Assert.Single(warnings.Warnings);
    }
}

[Collection("RadioDescriptionWarnings")]
public class RadioDescriptionTests
{
    private static Element Card() => Label("card");

    [Fact]
    public void RemovingTheDescriptionModifier_KeepsTheTextAsTheName_AndAnEmptyTextClearsIt()
    {
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render((RadioButton(Card(), false) with { Content = "A" }).Semantics(description: "Declared"));
        var radio = host.View<MauiRadioButton>();
        Assert.Equal("Declared", SemanticProperties.GetDescription(radio));

        host.Render(RadioButton(Card(), false) with { Content = "A" });
        Assert.Equal("A", SemanticProperties.GetDescription(radio));

        host.Render(RadioButton(Card(), false) with { Content = "" });
        Assert.Null(SemanticProperties.GetDescription(radio));
    }
}

/// <summary>Spec 070 §3.6: Reactor-owned radio groups, their scopes and transactions.</summary>
public class RadioGroupTests
{
    private static ElementRef<MauiRadioButton>[] Refs(int count) => [.. Enumerable.Range(0, count).Select(_ => TypedElementRef.Create<MauiRadioButton>())];

    private static void Tap(ElementRef<MauiRadioButton> radio) => ((IRadioButton)radio.Current!).IsChecked = true;

    private static RadioButtonElement Radio(string name, bool on, List<string> log, ElementRef<MauiRadioButton> r, string? group = null, Action<bool>? also = null) =>
        RadioButton(name, on, v => { log.Add($"{name} {v}"); also?.Invoke(v); }, group).Ref(r);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANativeCheck_UnchecksThePeers_AndReportsThem_BeforeAnyRender(bool direct)
    {
        var log = new List<string>();
        var r = Refs(4);
        var host = new TestHost(direct ? DirectHandlers.CreateRegistry() : MauiHandlers.CreateRegistry()).Render(
            HStack(
                VStack(Radio("a", true, log, r[0], "g1")),
                Grid("*", "*", VStack(Radio("b", false, log, r[1], "g1"))),
                VStack(Radio("c", true, log, r[2]), Radio("d", false, log, r[3]))));

        Tap(r[1]);
        Tap(r[3]);

        Assert.Equal(["a False", "b True", "c False", "d True"], log);
        Assert.Equal((false, true, false, true), (r[0].Current!.IsChecked, r[1].Current!.IsChecked, r[2].Current!.IsChecked, r[3].Current!.IsChecked));
        Assert.All(r, x => Assert.StartsWith("reactor-", x.Current!.GroupName, StringComparison.Ordinal));
        Assert.NotEqual(r[0].Current!.GroupName, r[1].Current!.GroupName);

        // Refused: every button returns to its declaration, and nothing more is reported.
        host.Scheduler.Drain();
        Assert.Equal((true, false, true, false), (r[0].Current!.IsChecked, r[1].Current!.IsChecked, r[2].Current!.IsChecked, r[3].Current!.IsChecked));
        Assert.Equal(4, log.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MauisOwnGrouping_MissesPeersInDirectStacks_ButNotInControlsLayouts(bool direct)
    {
        // The positive witness for Reactor's groups: without them, peers in direct stacks stay checked,
        // while MAUI's own search does find them in a Controls layout.
        var log = new List<string>();
        var r = Refs(2);
        new TestHost(direct ? DirectHandlers.CreateRegistry() : MauiHandlers.CreateRegistry())
            .Render(VStack(Radio("a", true, log, r[0], "g"), Radio("b", false, log, r[1], "g")));
        foreach (var x in r)
        {
            var radio = (ReactorRadioButton)x.Current!;
            radio.Groups!.Unregister(radio);
            radio.GroupName = "shared";
        }

        Tap(r[1]);

        Assert.Equal(direct, r[0].Current!.IsChecked);
    }

    [Fact]
    public void UnnamedGroupsInSeparateDirectStacks_AreIndependent()
    {
        var log = new List<string>();
        var r = Refs(4);
        new TestHost(DirectHandlers.CreateRegistry()).Render(VStack(
            VStack(Radio("a", true, log, r[0]), Radio("b", false, log, r[1])),
            VStack(Radio("c", true, log, r[2]), Radio("d", false, log, r[3]))));

        Tap(r[1]);

        Assert.Equal(["a False", "b True"], log);
        Assert.True(r[2].Current!.IsChecked);
    }

    [Fact]
    public void ADeclaredChange_InEitherTreeOrder_ReportsNothing()
    {
        var log = new List<string>();
        var r = Refs(2);
        Element Pair(string selected) => VStack(Radio("a", selected == "a", log, r[0], "g"), Radio("b", selected == "b", log, r[1], "g"));
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Pair("a"));

        host.Render(Pair("b"));
        Assert.Equal((false, true), (r[0].Current!.IsChecked, r[1].Current!.IsChecked));
        host.Render(Pair("a"));
        host.Scheduler.Drain();

        Assert.Empty(log);
        Assert.Equal((true, false), (r[0].Current!.IsChecked, r[1].Current!.IsChecked));
    }

    private sealed class Wrapped : Component<(List<string> Log, ElementRef<MauiRadioButton> Ref)>
    {
        public override Element Render() => Radio("wrapped", true, Props.Log, Props.Ref);
    }

    [Fact]
    public void AnUnnamedGroup_IncludesAButtonInsideAComponent()
    {
        var log = new List<string>();
        var r = Refs(2);
        new TestHost(DirectHandlers.CreateRegistry()).Render(VStack(
            Component<Wrapped, (List<string>, ElementRef<MauiRadioButton>)>((log, r[0])),
            Radio("plain", false, log, r[1])));

        Tap(r[1]);

        Assert.Equal(["wrapped False", "plain True"], log);
        Assert.False(r[0].Current!.IsChecked);
    }

    private sealed class Probe : Component<Action<Action<bool>>>
    {
        public override Element Render()
        {
            var (show, setShow) = UseState(true);
            Props(setShow);
            return show ? VStack(RadioButton("a", true, null), RadioButton("b", false, null)) : Label("gone");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (TestHost Host, WeakReference Layout, Action<bool> Show, RadioGroups Groups) MountAndCheck()
    {
        Action<bool>? show = null;
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Component<Probe, Action<Action<bool>>>(s => show = s));
        var layout = host.View<VerticalStackLayout>();
        var b = (ReactorRadioButton)layout[1];
        ((IRadioButton)b).IsChecked = true; // a transaction: the unnamed group (scoped to the layout's node) has a generation
        host.Scheduler.Drain();
        return (host, new WeakReference(layout), show!, b.Groups!);
    }

    [Fact]
    public void ARemovedGroup_DoesNotKeepItsScopeAlive()
    {
        var (host, layout, show, groups) = MountAndCheck();
        Assert.Equal(1, groups.GroupCount);

        show(false);
        host.Scheduler.Drain();
        for (var i = 0; i < 3 && layout.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.Equal(0, groups.GroupCount);
        Assert.False(layout.IsAlive, "the removed layout (the group's scope) was collected");
        GC.KeepAlive(host);
    }

    [Fact]
    public void NamedGroups_AreScopedToTheirPage_AndUnnamedOnesToTheirCell()
    {
        var log = new List<string>();
        var r = Refs(4);
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(NavigationStack([
            Page("one", VStack(Radio("a", true, log, r[0], "size"))).WithKey("one"),
            Page("two", VStack(Radio("b", true, log, r[1], "size"), Radio("c", false, log, r[2], "size"))).WithKey("two"),
        ]));

        Tap(r[2]);

        Assert.Equal(["b False", "c True"], log);
        Assert.True(r[0].Current!.IsChecked);
    }

    [Fact]
    public void TabsScopeNamedGroups_ToTheirPage()
    {
        var log = new List<string>();
        var r = Refs(2);
        new TestHost(MauiHandlers.CreateRegistry()).Render(Tabs(0, null,
            Page("one", VStack(Radio("a", true, log, r[0], "size"))),
            Page("two", VStack(Radio("b", false, log, r[1], "size")))));

        Tap(r[1]);

        Assert.Equal(["b True"], log);
        Assert.True(r[0].Current!.IsChecked);
    }

    [Fact]
    public void CellsAndSlots_BoundUnnamedGroups_WhileANamedGroupSpansCells()
    {
        var log = new List<string>();
        var refs = new Dictionary<string, ElementRef<MauiRadioButton>>();
        ElementRef<MauiRadioButton> R(string key) => refs.TryGetValue(key, out var found) ? found : refs[key] = TypedElementRef.Create<MauiRadioButton>();

        // Unnamed radio buttons at the top of two cells, of the header slot and of the footer slot.
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(
            CollectionView(new[] { "x", "y" }, k => k, k => Radio($"u{k}", k == "x", log, R($"u{k}"))) with
            {
                Header = Radio("h", false, log, R("h")),
                Footer = Radio("f", true, log, R("f")),
            });
        var view = host.View<CollectionView>();
        Cells.Realize(view, 0);
        Cells.Realize(view, 1);
        Tap(R("uy"));
        Tap(R("h"));
        Assert.Equal(["uy True", "h True"], log);
        Assert.True(R("ux").Current!.IsChecked && R("f").Current!.IsChecked);

        // A named group spans the cells of one page (the positive control).
        log.Clear();
        var named = new TestHost(MauiHandlers.CreateRegistry()).Render(
            CollectionView(new[] { "x", "y" }, k => k, k => Radio($"n{k}", k == "x", log, R($"n{k}"), "named")));
        Cells.Realize(named.View<CollectionView>(), 0);
        Cells.Realize(named.View<CollectionView>(), 1);
        Tap(R("ny"));
        Assert.Equal(["nx False", "ny True"], log);
    }
}

/// <summary>Spec 070 §3.6: a user's check is one transaction, never reentrant.</summary>
public class RadioTransactionTests
{
    private static void Tap(ElementRef<MauiRadioButton> radio) => ((IRadioButton)radio.Current!).IsChecked = true;

    [Fact]
    public void TheCheckedCallback_SeesItsPeersUnchecked_AfterThePeersReports()
    {
        var log = new List<string>();
        var a = TypedElementRef.Create<MauiRadioButton>();
        var b = TypedElementRef.Create<MauiRadioButton>();
        new TestHost(MauiHandlers.CreateRegistry()).Render(VStack(
            RadioButton("a", true, v => log.Add($"a {v}"), "g").Ref(a),
            RadioButton("b", false, v => log.Add($"b {v} sees a {a.Current!.IsChecked}"), "g").Ref(b)));

        Tap(b);

        Assert.Equal(["a False", "b True sees a False"], log);
    }

    [Fact]
    public void AThrowingCallback_ReachesTheBoundary_AndTheNextStillRuns()
    {
        var log = new List<string>();
        var a = TypedElementRef.Create<MauiRadioButton>();
        var b = TypedElementRef.Create<MauiRadioButton>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(ErrorBoundary(VStack(
            RadioButton("a", true, _ => throw new InvalidOperationException("boom"), "g").Ref(a),
            RadioButton("b", false, v => log.Add($"b {v}"), "g").Ref(b)),
            error => Label($"fallback {error.Message}")));

        Tap(b);
        Assert.Equal(["b True"], log);
        host.Scheduler.Drain();

        Assert.Equal("fallback boom", host.View<MauiLabel>().Text);
    }

    [Fact]
    public void ACallbackThatUnmountsLaterParticipants_SkipsTheirReports()
    {
        var log = new List<string>();
        var c = TypedElementRef.Create<MauiRadioButton>();
        TestHost? host = null;
        host = new TestHost(MauiHandlers.CreateRegistry()).Render(VStack(
            RadioButton("a", true, v => { log.Add($"a {v}"); host!.Root.Unmount(); }, "g"),
            RadioButton("b", true, v => log.Add($"b {v}"), "g"),
            RadioButton("c", false, v => log.Add($"c {v}"), "g").Ref(c)));
        var radio = (IRadioButton)c.Current!;

        radio.IsChecked = true;

        Assert.Equal(["a False"], log);
    }

    [Fact]
    public void ACheckFromACallback_IsQueued_AndRunsAfterTheTransaction()
    {
        var log = new List<string>();
        var a = TypedElementRef.Create<MauiRadioButton>();
        var b = TypedElementRef.Create<MauiRadioButton>();
        var c = TypedElementRef.Create<MauiRadioButton>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(VStack(
            RadioButton("a", true, v =>
            {
                log.Add($"a {v}");
                // A's callback checks C through a ref, while B's transaction dispatches.
                if (!v) ((IRadioButton)c.Current!).IsChecked = true;
            }, "g").Ref(a),
            RadioButton("b", false, v => log.Add($"b {v}"), "g").Ref(b),
            RadioButton("c", false, v => log.Add($"c {v}"), "g").Ref(c)));
        host.Scheduler.Drain();
        var groups = ((ReactorRadioButton)a.Current!).Groups!;
        var before = groups.Transactions;

        Tap(b);
        // The queued same-group check superseded B's transaction: B's own report wasn't dispatched.
        Assert.Equal(["a False"], log);
        Assert.True(c.Current!.IsChecked && b.Current!.IsChecked);

        host.Scheduler.RunOne(); // the queued transaction
        Assert.Equal(["a False", "b False", "c True"], log);
        Assert.Equal((false, false, true), (a.Current!.IsChecked, b.Current!.IsChecked, c.Current!.IsChecked));
        // Unsuppressed notifications: the user's tap and the ref's check, nothing else.
        Assert.Equal(before + 2, groups.Transactions);
    }

    [Fact]
    public void ARefUncheck_OutsideATransaction_IsReportedOnce_AndRestored()
    {
        var log = new List<string>();
        var a = TypedElementRef.Create<MauiRadioButton>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(VStack(
            RadioButton("a", true, v => log.Add($"a {v}"), "g").Ref(a),
            RadioButton("b", false, v => log.Add($"b {v}"), "g")));
        host.Scheduler.Drain();

        ((IRadioButton)a.Current!).IsChecked = false;
        Assert.Equal(["a False"], log);
        host.Scheduler.Drain();

        Assert.True(a.Current!.IsChecked);
        Assert.Equal(["a False"], log);
    }

    private sealed record QueueProps(List<string> Log, ElementRef<MauiRadioButton> A, ElementRef<MauiRadioButton> B, ElementRef<MauiRadioButton> C, string Mode);

    // A's uncheck callback changes state, then checks C through a ref (queued, since B's transaction is
    // dispatching). Mode "state": only a state change; "supersede": C is unchecked again through the ref;
    // "unmount": the state change removes C.
    private sealed class Queueing : Component<QueueProps>
    {
        public override Element Render()
        {
            var (count, setCount) = UseState(0);
            var (showC, setShowC) = UseState(true);
            var p = Props;
            return VStack(
                Label($"count {count}"),
                RadioButton("a", true, v =>
                {
                    p.Log.Add($"a {v}");
                    if (v) return;
                    setCount(count + 1);
                    if (p.Mode == "unmount") setShowC(false);
                    ((IRadioButton)p.C.Current!).IsChecked = true;
                    if (p.Mode == "supersede") ((IRadioButton)p.C.Current!).IsChecked = false;
                }, "g").Ref(p.A),
                RadioButton("b", false, v => p.Log.Add($"b {v}"), "g").Ref(p.B),
                showC ? RadioButton("c", false, v => p.Log.Add($"c {v}"), "g").Ref(p.C) : null);
        }
    }

    private static (TestHost Host, QueueProps Props) MountQueueing(string mode)
    {
        var props = new QueueProps([], TypedElementRef.Create<MauiRadioButton>(), TypedElementRef.Create<MauiRadioButton>(), TypedElementRef.Create<MauiRadioButton>(), mode);
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Component<Queueing, QueueProps>(props));
        host.Scheduler.Drain();
        return (host, props);
    }

    [Fact]
    public void AQueuedCheck_DroppedBecauseAnotherCheckUncheckedIt_IsRestoredToItsDeclaration()
    {
        var log = new List<string>();
        var c = TypedElementRef.Create<MauiRadioButton>();
        var d = TypedElementRef.Create<MauiRadioButton>();
        var b = TypedElementRef.Create<MauiRadioButton>();
        var once = true;
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(VStack(
            RadioButton("b", false, v => log.Add($"b {v}"), "g").Ref(b),
            RadioButton("c", false, v => log.Add($"c {v}"), "g").Ref(c),
            RadioButton("d", true, v =>
            {
                log.Add($"d {v}");
                if (v || !once) return;
                once = false;
                // Two checks from a callback: both queue. C's runs first and unchecks D natively, so D's
                // entry is dropped; D, declared checked, must still get its declaration back.
                ((IRadioButton)c.Current!).IsChecked = true;
                ((IRadioButton)d.Current!).IsChecked = true;
            }, "g").Ref(d)));
        host.Scheduler.Drain();

        Tap(b);
        host.Scheduler.Drain();

        // C's queued check supersedes B's transaction (same group), and unchecks B and D as its peers.
        Assert.Equal(["d False", "b False", "d False", "c True"], log);
        Assert.Equal((false, false, true), (b.Current!.IsChecked, c.Current!.IsChecked, d.Current!.IsChecked));
    }

    [Fact]
    public void AThrowInTheDrain_LeavesTheRestQueuedForAnotherTurn()
    {
        var log = new List<string>();
        var c = TypedElementRef.Create<MauiRadioButton>();
        var d = TypedElementRef.Create<MauiRadioButton>();
        var e = TypedElementRef.Create<MauiRadioButton>();
        var b = TypedElementRef.Create<MauiRadioButton>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(VStack(
            VStack(
                RadioButton("b", false, v =>
                {
                    log.Add($"b {v}");
                    if (!v) return;
                    // Both queue: C's (group g) throws while unchecking its peer D; E's (group h) must run.
                    ((IRadioButton)c.Current!).IsChecked = true;
                    ((IRadioButton)e.Current!).IsChecked = true;
                }, "h").Ref(b),
                RadioButton("c", false, v => log.Add($"c {v}"), "g").Ref(c),
                RadioButton("d", true, v => log.Add($"d {v}"), "g").Ref(d)),
            RadioButton("e", false, v => log.Add($"e {v}"), "i").Ref(e)));
        host.Scheduler.Drain();
        d.Current!.CheckedChanged += (_, args) => { if (!args.Value) throw new InvalidOperationException("app handler"); };

        Tap(b);
        Assert.Throws<InvalidOperationException>(() => host.Scheduler.Drain());
        host.Scheduler.Drain();

        Assert.Contains("e True", log);
        Assert.DoesNotContain("c True", log);
        // E isn't left queued: its declaration (unchecked) is written back again.
        Assert.False(e.Current!.IsChecked);
    }

    [Fact]
    public void AQueuedCheck_IsNotOverwrittenByARenderBeforeItRuns()
    {
        var (host, p) = MountQueueing("state");

        Tap(p.B);
        host.Scheduler.Drain(); // the state change's render is posted before the queued check

        // The render restored a (still declared checked); C's transaction then unchecks it again.
        Assert.Equal(["a False", "a False", "c True"], p.Log);
    }

    [Fact]
    public void AQueuedCheck_SupersededBeforeItRuns_ReportsNothing()
    {
        var (host, p) = MountQueueing("supersede");

        Tap(p.B);
        host.Scheduler.Drain();

        Assert.DoesNotContain("c True", p.Log);
        Assert.Equal("a False", p.Log[0]);
    }

    [Fact]
    public void AQueuedCheck_OnAButtonUnmountedBeforeItRuns_ReportsNothing()
    {
        var (host, p) = MountQueueing("unmount");

        Tap(p.B);
        host.Scheduler.Drain();

        Assert.Equal(["a False"], p.Log);
        Assert.Null(p.C.Current);
    }

    [Fact]
    public void NestedChecksInIndependentGroups_BothDeliverTheirReports()
    {
        var log = new List<string>();
        var a = TypedElementRef.Create<MauiRadioButton>();
        var b = TypedElementRef.Create<MauiRadioButton>();
        var x = TypedElementRef.Create<MauiRadioButton>();
        var y = TypedElementRef.Create<MauiRadioButton>();
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(VStack(
            RadioButton("a", true, v =>
            {
                log.Add($"a {v}");
                if (!v) ((IRadioButton)y.Current!).IsChecked = true;
            }, "g").Ref(a),
            RadioButton("b", false, v => log.Add($"b {v}"), "g").Ref(b),
            RadioButton("x", true, v => log.Add($"x {v}"), "h").Ref(x),
            RadioButton("y", false, v => log.Add($"y {v}"), "h").Ref(y)));
        host.Scheduler.Drain();

        Tap(b);
        Assert.Equal(["a False", "b True"], log);
        host.Scheduler.RunOne();

        Assert.Equal(["a False", "b True", "x False", "y True"], log);
    }
}

/// <summary>Spec 070 §3.6: conflicting declarations are applied verbatim, with a warning after the pass.</summary>
public class RadioConflictTests
{
    [Fact]
    public void TwoCheckedInOneGroup_AreShown_AndWarnOnce()
    {
        using var warnings = new WarningListener("RadioButton.Group");
        Element Pair() => VStack(RadioButton("a", true, null, "conflict-1"), RadioButton("b", true, null, "conflict-1"));
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Pair());
        var stack = host.View<VerticalStackLayout>();
        Assert.True(((MauiRadioButton)stack[0]).IsChecked && ((MauiRadioButton)stack[1]).IsChecked);
        Assert.DoesNotContain(warnings.Warnings, w => w.Contains("conflict-1", StringComparison.Ordinal));

        host.Scheduler.Drain();
        Assert.Single(warnings.Warnings, w => w.Contains("conflict-1", StringComparison.Ordinal));
        host.Render(Pair());
        host.Scheduler.Drain();
        Assert.Single(warnings.Warnings, w => w.Contains("conflict-1", StringComparison.Ordinal));
    }

    [Fact]
    public void AGroupNameChangeThatMergesTwoChecked_Warns()
    {
        using var warnings = new WarningListener("RadioButton.Group");
        Element Pair(string second) => VStack(RadioButton("a", true, null, "conflict-2"), RadioButton("b", true, null, second));
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Pair("conflict-2b"));
        host.Scheduler.Drain();
        Assert.DoesNotContain(warnings.Warnings, w => w.Contains("conflict-2", StringComparison.Ordinal));

        host.Render(Pair("conflict-2"));
        host.Scheduler.Drain();

        Assert.Single(warnings.Warnings, w => w.Contains("'conflict-2'", StringComparison.Ordinal));
    }

    [Fact]
    public void ReplacingACheckedButton_InOneRender_WarnsNothing()
    {
        using var warnings = new WarningListener("RadioButton.Group");
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(VStack(RadioButton("a", true, null, "conflict-3").WithKey("a")));
        host.Scheduler.Drain();

        host.Render(VStack(RadioButton("b", true, null, "conflict-3").WithKey("b")));
        host.Scheduler.Drain();

        Assert.DoesNotContain(warnings.Warnings, w => w.Contains("conflict-3", StringComparison.Ordinal));
    }
}
