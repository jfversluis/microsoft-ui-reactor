using System.Collections.Immutable;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui;
using static Microsoft.UI.Reactor.Maui.Factories;

namespace Reactor.Maui.Sample;

/// <summary>UseState plus a click handler.</summary>
public sealed class CounterCard : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        return VStack(8,
            Label("Counter").FontSize(20).Bold(),
            Label($"Clicked {count} times").AutomationId("CounterLabel"),
            HStack(8,
                Button("−", () => setCount(count - 1)).AutomationId("DecrementButton"),
                Button("+", () => setCount(count + 1)).AutomationId("IncrementButton")));
    }
}

/// <summary>UseEffect with cleanup, and state written from a background thread.</summary>
public sealed class ClockCard : Component
{
    public override Element Render()
    {
        var (seconds, update) = UseReducer(0);
        UseEffect(() =>
        {
            // The timer fires on a thread-pool thread; the core posts the update to the UI thread.
            var timer = new Timer(_ => update(s => s + 1), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            return timer.Dispose;
        });

        return VStack(8,
            Label("Effect").FontSize(20).Bold(),
            Label($"Mounted for {seconds}s (updated from a background timer)").AutomationId("ClockLabel"));
    }
}

public sealed record Todo(int Id, string Title, bool Done);

public sealed record TodoRowProps(Todo Todo, Action<int> Toggle, Action<int> Remove);

/// <summary>A controlled Entry, keyed rows and stable callbacks.</summary>
public sealed class TodoCard : Component
{
    public override Element Render()
    {
        var (draft, setDraft) = UseState("");
        var (todos, update) = UseReducer(ImmutableList.Create(
            new Todo(1, "Mount real MAUI controls", true),
            new Todo(2, "Keep row state across a reorder", false),
            new Todo(3, "Decide where the core lives", false)));
        var nextId = UseRef(4);

        // Stable across renders, so rows whose todo did not change skip re-rendering.
        var toggle = UseCallback<Action<int>>(id => update(list => list.Select(t => t.Id == id ? t with { Done = !t.Done } : t).ToImmutableList()));
        var remove = UseCallback<Action<int>>(id => update(list => list.RemoveAll(t => t.Id == id)));

        void Add()
        {
            if (string.IsNullOrWhiteSpace(draft)) return;
            var id = nextId.Current++;
            var title = draft.Trim();
            update(list => list.Add(new Todo(id, title, false)));
            setDraft("");
        }

        return VStack(8,
            Label("Todos").FontSize(20).Bold(),
            Label("Type a note in a row, then press Reverse: the note follows its row because rows are keyed."),
            Grid("auto", "*,auto,auto",
                Entry(draft, setDraft, placeholder: "What needs doing?").AutomationId("DraftEntry"),
                Button("Add", Add).AutomationId("AddButton").Grid(0, 1),
                Button("Reverse", () => update(list => list.Reverse())).AutomationId("ReverseButton").Grid(0, 2)),
            Label($"{todos.Count(t => !t.Done)} of {todos.Count} left").AutomationId("RemainingLabel"),
            VStack(4, [.. todos.Select(todo =>
                Component<TodoRow, TodoRowProps>(new(todo, toggle, remove)).WithKey(todo.Id.ToString()))]));
    }
}

public sealed class TodoRow : Component<TodoRowProps>
{
    public override Element Render()
    {
        var (todo, toggle, remove) = (Props.Todo, Props.Toggle, Props.Remove);
        // Local state owned by the row, not the list.
        var (note, setNote) = UseState("");
        // Check box, then the title over the row's own note, then Remove; the middle column takes the rest.
        return Grid("auto", "auto,*,auto",
            CheckBox(todo.Done, _ => toggle(todo.Id)).VerticalOptions(LayoutOptions.Center).AutomationId($"Done{todo.Id}"),
            VStack(2,
                Label(todo.Title).Strikethrough(todo.Done),
                Entry(note, setNote, placeholder: "note").AutomationId($"Note{todo.Id}")).Grid(0, 1),
            Button("Remove", () => remove(todo.Id)).VerticalOptions(LayoutOptions.Center).AutomationId($"Remove{todo.Id}").Grid(0, 2));
    }
}
