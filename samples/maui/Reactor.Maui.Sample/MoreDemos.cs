using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui;
using static Microsoft.UI.Reactor.Maui.Factories;

namespace Reactor.Maui.Sample;

/// <summary>Value controls. Every value is component state; the controls only report changes.</summary>
public sealed class ControlsDemo : Component
{
    private static readonly string[] Sizes = ["Small", "Medium", "Large"];

    public override Element Render()
    {
        var (volume, setVolume) = UseState(40.0);
        var (copies, setCopies) = UseState(1.0);
        var (notify, setNotify) = UseState(true);
        var (agree, setAgree) = UseState(false);
        var (size, setSize) = UseState(1);
        var (date, setDate) = UseState(new DateTime(2026, 6, 1));
        var (bio, setBio) = UseState("");
        var (plan, setPlan) = UseState("monthly");

        return VStack(16,
            Section("Slider", HStack(12,
                Slider(volume, setVolume, 0, 100).Width(220).AutomationId("VolumeSlider"),
                Label($"{volume:0}").VerticalOptions(LayoutOptions.Center).AutomationId("VolumeLabel"))),
            Section("Stepper", HStack(12,
                Stepper(copies, setCopies, 1, 10).AutomationId("CopiesStepper"),
                Label($"{copies:0} copies").VerticalOptions(LayoutOptions.Center).AutomationId("CopiesLabel"))),
            Section("Switch and CheckBox", VStack(8,
                HStack(12, Switch(notify, setNotify).AutomationId("NotifySwitch"), Label(notify ? "Notifications on" : "Notifications off").VerticalOptions(LayoutOptions.Center).AutomationId("NotifyLabel")),
                HStack(12, CheckBox(agree, setAgree).AutomationId("AgreeCheckBox"), Label("I agree").VerticalOptions(LayoutOptions.Center)),
                Button("Continue", () => { }).IsEnabled(agree).HorizontalOptions(LayoutOptions.Start).AutomationId("ContinueButton"))),
            Section("RadioButton", VStack(4,
                RadioButton("Monthly", plan == "monthly", on => { if (on) setPlan("monthly"); }, groupName: "plan").AutomationId("PlanMonthly"),
                RadioButton("Yearly", plan == "yearly", on => { if (on) setPlan("yearly"); }, groupName: "plan").AutomationId("PlanYearly"),
                Label($"Plan: {plan}").AutomationId("PlanLabel"))),
            Section("Picker and DatePicker", VStack(8,
                Picker(Sizes, size, setSize).Width(200).HorizontalOptions(LayoutOptions.Start).AutomationId("SizePicker"),
                DatePicker(date, setDate).Width(200).HorizontalOptions(LayoutOptions.Start).AutomationId("DatePicker"),
                Label($"{Sizes[size]}, {date:d MMM yyyy}").AutomationId("PickedLabel"))),
            Section("Editor", VStack(8,
                Editor(bio, setBio, placeholder: "Tell us about yourself").Height(100).AutomationId("BioEditor"),
                Label($"{bio.Length} characters").AutomationId("BioLength"))),
            Section("ProgressBar and ActivityIndicator", VStack(8,
                ProgressBar(volume / 100).AutomationId("VolumeProgress"),
                ActivityIndicator(notify))));
    }

    private static Element Section(string title, Element content) =>
        VStack(8, Label(title).FontSize(18).Bold(), content);
}

public sealed record Contact(int Id, string Name, string Role);

/// <summary>A CollectionView: keyed items, controlled selection and incremental loading.</summary>
public sealed class ListDemo : Component
{
    private static readonly string[] Roles = ["Engineer", "Designer", "PM", "Writer"];

    private static IEnumerable<Contact> Page(int start, int count) =>
        Enumerable.Range(start, count).Select(i => new Contact(i, $"Contact {i}", Roles[i % Roles.Length]));

    public override Element Render()
    {
        var (contacts, update) = UseReducer<IReadOnlyList<Contact>>([.. Page(1, 40)]);
        var (selected, setSelected) = UseState<string?>(null);

        var list = CollectionView(contacts, c => c.Id.ToString(), contact =>
                HStack(12,
                    BoxView(Colors.CornflowerBlue).Size(36, 36),
                    VStack(2, Label(contact.Name).Bold(), Label(contact.Role).Opacity(0.7))
                ).Padding(12, 8)) with
        {
            SelectionMode = SelectionMode.Single,
            SelectedKey = selected,
            OnSelectionChanged = keys => setSelected(keys.Count > 0 ? keys[0] : null),
            RemainingItemsThreshold = 5,
            OnRemainingItemsThresholdReached = () => update(list => list.Count >= 200 ? list : [.. list, .. Page(list.Count + 1, 20)]),
        };

        return Grid("auto,auto,*", "*",
            Label(selected is null ? "Nothing selected" : $"Selected contact {selected}").AutomationId("SelectedLabel").Margin(0, 0, 0, 8),
            HStack(8,
                Button("Shuffle", () => update(list => [.. list.OrderBy(_ => Random.Shared.Next())])).AutomationId("ShuffleButton"),
                Button("Clear selection", () => setSelected(null)).AutomationId("ClearSelectionButton"),
                Label($"{contacts.Count} loaded").VerticalOptions(LayoutOptions.Center).AutomationId("LoadedLabel")).Grid(1, 0),
            list.Grid(2, 0));
    }
}
