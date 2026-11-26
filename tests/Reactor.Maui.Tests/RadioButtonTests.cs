using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiRadioButton = Microsoft.Maui.Controls.RadioButton;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>Spec 070 §3.6: radio buttons with view content.</summary>
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

    [Fact]
    public void MauisOwnGrouping_MissesPeersInDirectStacks()
    {
        // The positive witness for Reactor's groups: without them, peers in direct stacks stay checked.
        var log = new List<string>();
        var r = Refs(2);
        new TestHost(DirectHandlers.CreateRegistry()).Render(VStack(Radio("a", true, log, r[0], "g"), Radio("b", false, log, r[1], "g")));
        foreach (var x in r)
        {
            var radio = (ReactorRadioButton)x.Current!;
            radio.Groups!.Unregister(radio);
            radio.GroupName = "shared";
        }

        Tap(r[1]);

        Assert.True(r[0].Current!.IsChecked);
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
        host.Render(Pair("a"));
        host.Scheduler.Drain();

        Assert.Empty(log);
        Assert.Equal((true, false), (r[0].Current!.IsChecked, r[1].Current!.IsChecked));
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
