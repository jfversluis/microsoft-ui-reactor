using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Direct;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiButton = Microsoft.Maui.Controls.Button;
using MauiEntry = Microsoft.Maui.Controls.Entry;
using MauiLabel = Microsoft.Maui.Controls.Label;

namespace Microsoft.UI.Reactor.Maui.Tests;

public sealed record TodoListProps(string[] Items);

public sealed class TodoList : Component<TodoListProps>
{
    public override Element Render() =>
        VStack([.. Props.Items.Select(item => Component<Row, string>(item).WithKey(item))]);
}

public sealed class Row : Component<string>
{
    public static readonly Dictionary<string, Action<string>> SetNote = [];

    public override Element Render()
    {
        var (note, setNote) = UseState("");
        SetNote[Props] = setNote;
        return HStack(Label(Props), Entry(note, setNote));
    }
}

public sealed class UppercaseForm : Component<List<string>>
{
    public override Element Render()
    {
        var (text, setText) = UseState("");
        return VStack(
            Entry(text, value =>
            {
                Props.Add(value);
                setText(value.ToUpperInvariant());
            }),
            Label(text));
    }
}

public class ControlsRendererTests
{
    private static TestHost Host() => new(MauiHandlers.CreateRegistry());

    [Fact]
    public void Mount_CreatesControls_WithTheElementsValues()
    {
        var clicks = 0;
        var host = Host().Render(VStack(12,
            Label("hello").FontSize(20).Bold(),
            Button("go", () => clicks++).AutomationId("Go")));

        var stack = host.View<VerticalStackLayout>();
        Assert.Equal(12, stack.Spacing);
        var label = Assert.IsType<MauiLabel>(stack[0]);
        Assert.Equal("hello", label.Text);
        Assert.Equal(20, label.FontSize);
        Assert.Equal(FontAttributes.Bold, label.FontAttributes);
        var button = Assert.IsType<MauiButton>(stack[1]);
        Assert.Equal("Go", button.AutomationId);

        button.SendClicked();
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Update_PatchesTheSameControls_AndClearsRemovedModifiers()
    {
        var host = Host().Render(VStack(Label("a").Margin(8).Width(100)));
        var label = (MauiLabel)host.View<VerticalStackLayout>()[0];

        host.Render(VStack(Label("b")));

        Assert.Same(label, host.View<VerticalStackLayout>()[0]);
        Assert.Equal("b", label.Text);
        Assert.Equal(default, label.Margin);
        Assert.Equal(-1, label.WidthRequest);
    }

    [Fact]
    public void KeyedReorder_MovesExistingControls_AndRowStateFollows()
    {
        Row.SetNote.Clear();
        var host = Host().Render(Component<TodoList, TodoListProps>(new(["a", "b", "c"])));
        Row.SetNote["b"]("note on b");
        host.Scheduler.Drain();
        var rows = host.View<VerticalStackLayout>().ToArray();

        host.Render(Component<TodoList, TodoListProps>(new(["c", "b", "a"])));

        var reordered = host.View<VerticalStackLayout>().ToArray();
        Assert.Equal([rows[2], rows[1], rows[0]], reordered);
        var middle = (HorizontalStackLayout)reordered[1];
        Assert.Equal("note on b", ((MauiEntry)middle[1]).Text);
    }

    [Fact]
    public void ControlledEntry_ReportsUserInputOnce_AndReconcilerWritesDoNotEcho()
    {
        var changes = new List<string>();
        var host = Host().Render(Component<UppercaseForm, List<string>>(changes));
        var stack = host.View<VerticalStackLayout>();
        var entry = (MauiEntry)stack[0];

        entry.Text = "abc";
        host.Scheduler.Drain();

        Assert.Equal("ABC", entry.Text);
        Assert.Equal("ABC", ((MauiLabel)stack[1]).Text);
        Assert.Equal(["abc"], changes);
    }
}

public class DirectRendererTests
{
    private static TestHost Host() => new(DirectHandlers.CreateRegistry());

    [Fact]
    public void Mount_CreatesDirectViews_NotControls()
    {
        var host = Host().Render(VStack(4, Label("hi"), Button("go"), Entry("text")));

        var stack = host.View<DirectStackLayout>();
        Assert.Equal(4, stack.Spacing);
        Assert.Equal("hi", Assert.IsType<DirectLabel>(stack[0]).Text);
        Assert.Equal("go", Assert.IsType<DirectButton>(stack[1]).Text);
        Assert.Equal("text", Assert.IsType<DirectEntry>(stack[2]).Text);
        Assert.All(stack, child => Assert.Same(stack, ((DirectView)child).Parent));
    }

    [Fact]
    public void ControlledEntry_ReportsUserInputOnce_AndReconcilerWritesDoNotEcho()
    {
        var changes = new List<string>();
        var host = Host().Render(Component<UppercaseForm, List<string>>(changes));
        var stack = host.View<DirectStackLayout>();
        var entry = (DirectEntry)stack[0];

        // What MAUI's EntryHandler does when the user types.
        ((ITextInput)entry).Text = "abc";
        host.Scheduler.Drain();

        Assert.Equal("ABC", entry.Text);
        Assert.Equal("ABC", ((DirectLabel)stack[1]).Text);
        Assert.Equal(["abc"], changes);
    }

    [Fact]
    public void KeyedReorder_MovesExistingViews_AndRowStateFollows()
    {
        Row.SetNote.Clear();
        var host = Host().Render(Component<TodoList, TodoListProps>(new(["a", "b", "c"])));
        Row.SetNote["b"]("note on b");
        host.Scheduler.Drain();
        var rows = host.View<DirectStackLayout>().ToArray();

        host.Render(Component<TodoList, TodoListProps>(new(["c", "b", "a"])));

        var reordered = host.View<DirectStackLayout>().ToArray();
        Assert.Equal([rows[2], rows[1], rows[0]], reordered);
        Assert.Equal("note on b", ((DirectEntry)((DirectStackLayout)reordered[1])[1]).Text);
    }

    [Fact]
    public void MixedTree_HostsControlsInsideDirectStacks_AndDirectContentInControlsSlots()
    {
        var host = Host().Render(ScrollView(VStack(Stepper(1), Label("x"))));

        var scrollView = host.View<ScrollView>();
        var wrapper = Assert.IsType<Grid>(scrollView.Content, exactMatch: false);
        var stack = Assert.IsType<DirectStackLayout>(Assert.Single(wrapper));
        var stepper = Assert.IsType<Stepper>(stack[0]);
        // A Controls view inside a direct tree gets the wrapper as its logical parent, so styles reach it.
        Assert.Same(wrapper, stepper.Parent);
        // Visual-tree tools see through the wrapper and the direct stack down to the Controls view.
        Assert.Same(stack, Assert.Single(((IVisualTreeElement)wrapper).GetVisualChildren()));
        Assert.Equal<IVisualTreeElement>([stepper, (IVisualTreeElement)stack[1]], ((IVisualTreeElement)stack).GetVisualChildren());

        host.Render(ScrollView(VStack(Stepper(2), Label("y"))));

        Assert.Same(wrapper, scrollView.Content);
        Assert.Equal(2, stepper.Value);
    }
}
