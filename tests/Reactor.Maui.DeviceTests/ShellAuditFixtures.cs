using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Maui;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;
using static Reactor.Maui.DeviceTests.FixtureContext;
using MauiShell = Microsoft.Maui.Controls.Shell;

namespace Reactor.Maui.DeviceTests;

// Spec 070 §4.16.3 (4c-3c): the Shell's flyout presentation under each behavior, on the platform, and
// a prompt's fields through the platform's own text field.
public static partial class Fixtures
{
    private static IEnumerable<Fixture> ShellAuditFixtures() =>
    [
        .. Renderers.SelectMany(r => new Fixture[]
        {
            new($"Shell_FlyoutBehaviors_DeclaredOpen [{r.Name}]", c => ShellBehaviorSequence(c, r.Create(), declared: true)),
            new($"Shell_FlyoutBehaviors_DeclaredClosed [{r.Name}]", c => ShellBehaviorSequence(c, r.Create(), declared: false)),
        }),
        new("Dialog_PromptFields_ReachThePlatformsTextField", ClosingDialogs(PromptFields)),
        new("Dialog_ActionSheetDestruction_IsTheDestructiveButton", ClosingDialogs(ActionSheetDestruction)),
    ];

    private sealed record ShellBehaviorProps(FlyoutBehavior Behavior, bool Presented, List<bool> Reports);

    /// <summary>A Shell whose behavior a fixture changes by a render, with a callback that accepts any report.</summary>
    private sealed class ShellBehaviorApp : Component<ShellBehaviorProps>
    {
        public static Action<FlyoutBehavior>? SetBehavior;

        public override Element Render()
        {
            var (behavior, setBehavior) = UseState(Props.Behavior);
            var (open, setOpen) = UseState(Props.Presented);
            SetBehavior = setBehavior;
            return (Shell("one", null,
                    FlyoutItem("One", ShellTab("one", "One", Page("One", Label("one")))),
                    FlyoutItem("Two", ShellTab("two", "Two", Page("Two", Label("two")))))
                with { FlyoutHeader = Label("flyout header").Height(60) })
                .FlyoutBehavior(behavior)
                .FlyoutIsPresented(open, value =>
                {
                    Props.Reports.Add(value);
                    setOpen(value);
                });
        }
    }

    /// <summary>
    /// Every directed behavior change by a render (the open sequence starts from a first Disabled, which
    /// Android's drawer never realized), with the flyout declared open or closed: nothing is reported, so
    /// the accepting callback never changes the declaration; the pane is shown when locked or declared
    /// open under Flyout, and hidden under Disabled. Under Locked and Disabled a page pushed and popped
    /// natively (MAUI forces the presentation again) reports nothing either.
    /// </summary>
    private static async Task ShellBehaviorSequence(FixtureContext c, ElementHandlerRegistry registry, bool declared)
    {
        FlyoutBehavior[] sequence = declared
            ? [FlyoutBehavior.Disabled, FlyoutBehavior.Flyout, FlyoutBehavior.Locked, FlyoutBehavior.Disabled, FlyoutBehavior.Locked, FlyoutBehavior.Flyout, FlyoutBehavior.Disabled]
            : [FlyoutBehavior.Flyout, FlyoutBehavior.Locked, FlyoutBehavior.Disabled, FlyoutBehavior.Flyout, FlyoutBehavior.Disabled, FlyoutBehavior.Locked, FlyoutBehavior.Flyout];
        var reports = new List<bool>();
        c.Show(Component<ShellBehaviorApp, ShellBehaviorProps>(new(sequence[0], declared, reports)), registry);
        await c.WaitFor(() => c.Window.Page is ReactorShell { IsLoaded: true }, "the Reactor shell to load");
        var shell = (ReactorShell)c.Window.Page!;
        var header = (View)shell.FlyoutHeader;

        async Task Expect(FlyoutBehavior behavior, string step)
        {
            var shows = behavior == FlyoutBehavior.Locked || (behavior == FlyoutBehavior.Flyout && declared);
            var presented = behavior switch { FlyoutBehavior.Locked => true, FlyoutBehavior.Disabled => false, _ => declared };
            string State() => $"{ShellFlyoutShown(shell, header)}, presented {shell.FlyoutIsPresented}, reports [{string.Join(",", reports)}]";
            await c.WaitFor(() => ShellFlyoutShown(shell, header).StartsWith(shows ? "yes" : "no", StringComparison.Ordinal) && shell.FlyoutIsPresented == presented,
                $"{step}: the pane {(shows ? "shown" : "hidden")}, presented {presented} ({State()})", 6000);
            // Still so once animations and posted work have run.
            await c.Settle(500);
            Check(ShellFlyoutShown(shell, header).StartsWith(shows ? "yes" : "no", StringComparison.Ordinal) && shell.FlyoutIsPresented == presented,
                $"{step}: settled as expected ({State()})");
            Check(reports.Count == 0, $"{step}: nothing reported ({State()})");
        }

        await Expect(sequence[0], $"mount under {sequence[0]}");
        var pushedUnder = new HashSet<FlyoutBehavior>();
        for (var i = 1; i < sequence.Length; i++)
        {
            var behavior = sequence[i];
            ShellBehaviorApp.SetBehavior!(behavior);
            await Expect(behavior, $"{sequence[i - 1]} to {behavior}");
            if (behavior != FlyoutBehavior.Flyout && pushedUnder.Add(behavior))
            {
                await shell.Navigation.PushAsync(new ContentPage { Content = new Label { Text = "pushed" } }, false);
                await Expect(behavior, $"a page pushed under {behavior}");
                await shell.Navigation.PopAsync(false);
                await Expect(behavior, $"popped under {behavior}");
            }
        }

        // Positive control, under Flyout: the user's change is reported (and accepted).
        if (sequence[^1] == FlyoutBehavior.Flyout)
        {
            shell.SetValueFromRenderer(MauiShell.FlyoutIsPresentedProperty, !declared);
            await c.WaitFor(() => reports.Count == 1, "the user's change to be reported");
            Equal(!declared, reports[0], "the report");
            await c.WaitFor(() => ShellFlyoutShown(shell, header).StartsWith(!declared ? "yes" : "no", StringComparison.Ordinal), "the pane to follow the accepted change");
        }
        Check(c.UnhandledErrors.Count == 0, $"no errors: {string.Join(" | ", c.UnhandledErrors.Select(e => e.Message))}");
    }

    // ── Dialogs ─────────────────────────────────────────────────────────────

    private sealed record PromptProps(Action<Action<bool>> Open, TaskCompletionSource<string?> Closed);

    private sealed class PromptPage : Component<PromptProps>
    {
        public override Element Render()
        {
            var (open, setOpen) = UseState(false);
            Props.Open(setOpen);
            return Page("prompt", VStack(
                Label("prompt"),
                Prompt("Rename", "A new name") with
                {
                    InitialValue = "abc",
                    Placeholder = "name",
                    MaxLength = 5,
                    Keyboard = Keyboard.Email,
                    Accept = "Save",
                    Cancel = "Back",
                    IsOpen = open,
                    OnClosed = text =>
                    {
                        Props.Closed.TrySetResult(text);
                        setOpen(false);
                    },
                }));
        }
    }

    /// <summary>
    /// The prompt's fields on the platform's text field: the initial value and placeholder as shown; the
    /// keyboard as configured on the field; the maximum length through input, one character at a time
    /// through the platform's input path (UIKit's text input, which asks the field's delegate; Android's
    /// input connection, through the field's filters): the first two fit, the third is refused.
    /// </summary>
    private static async Task PromptFields(FixtureContext c)
    {
        Action<bool>? setOpen = null;
        var closed = new TaskCompletionSource<string?>();
        c.Show(Component<PromptPage, PromptProps>(new(s => setOpen = s, closed)));
        await LoadedPage<ContentPage>(c);
        await c.Settle();
        setOpen!(true);
        await c.WaitFor(() => NativeDialogs.IsReady(c.Window, "Rename"), "the prompt to be shown", NativeDialogs.Timeout);
        var field = NativeDialogs.PromptField(c.Window) ?? throw new CheckFailedException("the prompt has no text field");
#if IOS || MACCATALYST
        Equal("abc", field.Text, "the initial value");
        Equal("name", field.Placeholder, "the placeholder");
        var reference = new UIKit.UITextField();
        Microsoft.Maui.Platform.KeyboardExtensions.ApplyKeyboard(reference, Keyboard.Email);
        Check(reference.KeyboardType != UIKit.UIKeyboardType.Default, "positive control: the email keyboard isn't the default");
        Equal(reference.KeyboardType, field.KeyboardType, "the keyboard");
        field.BecomeFirstResponder();
        await c.Settle(300);
        // The caret at the end (UIKit selects the initial text), then each key as UIKit handles it: the
        // field's delegate is asked (what MAUI configures from the maximum length), and the key inserted
        // only if it allows it. (UITextField.InsertText from code doesn't ask the delegate.)
        field.SelectedTextRange = field.GetTextRange(field.EndOfDocument, field.EndOfDocument);
        var allowed = new List<bool>();
        foreach (var ch in new[] { "1", "2", "3" })
        {
            var ok = KeyAllowed(field, ch);
            allowed.Add(ok);
            if (ok) field.InsertText(ch);
        }
        Equal("True,True,False", string.Join(",", allowed), "the delegate's answers");
        Equal("abc12", field.Text, "typed past the maximum length");
#elif ANDROID
        Equal("abc", field.Text, "the initial value");
        Equal("name", field.Hint, "the placeholder");
        Equal(Microsoft.Maui.Platform.KeyboardExtensions.ToInputType(Keyboard.Email), field.InputType, "the keyboard");
        field.RequestFocus();
        var input = field.OnCreateInputConnection(new Android.Views.InputMethods.EditorInfo())
            ?? throw new CheckFailedException("the field has no input connection");
        foreach (var ch in new[] { "1", "2", "3" }) input.CommitText(new Java.Lang.String(ch), 1);
        Equal("abc12", field.Text, "typed past the maximum length");
#endif
        await NativeDialogs.Answer(c.Window, "Save");
        Equal("abc12", await closed.Task.WaitAsync(TimeSpan.FromSeconds(5)), "OnClosed's text");
    }

#if IOS || MACCATALYST
    /// <summary>What UIKit asks the field's delegate before inserting a typed key at the end.</summary>
    private static bool KeyAllowed(UIKit.UITextField field, string key)
    {
        var range = new Foundation.NSRange(field.Text?.Length ?? 0, 0);
        // The handlers UIKit's delegate call reaches (MAUI sets one of them, by OS version).
        if ((OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26)) && field.ShouldChangeCharactersInRanges is { } ranges)
            return ranges(field, [Foundation.NSValue.FromRange(range)], key);
        return field.ShouldChangeCharacters?.Invoke(field, range, key) ?? true;
    }
#endif

    private sealed record SheetProps(Action<Action<bool>> Open, TaskCompletionSource<string?> Closed);

    private sealed class SheetPage : Component<SheetProps>
    {
        public override Element Render()
        {
            var (open, setOpen) = UseState(false);
            Props.Open(setOpen);
            return Page("sheet", VStack(
                Label("sheet"),
                ActionSheet("Share", "Cancel", "Delete", "Mail") with
                {
                    IsOpen = open,
                    OnClosed = choice =>
                    {
                        Props.Closed.TrySetResult(choice);
                        setOpen(false);
                    },
                }));
        }
    }

    /// <summary>The action sheet's destruction is the platform's destructive button, and answers with its text.</summary>
    private static async Task ActionSheetDestruction(FixtureContext c)
    {
        Action<bool>? setOpen = null;
        var closed = new TaskCompletionSource<string?>();
        c.Show(Component<SheetPage, SheetProps>(new(s => setOpen = s, closed)));
        await LoadedPage<ContentPage>(c);
        await c.Settle();
        setOpen!(true);
        await c.WaitFor(() => NativeDialogs.IsReady(c.Window), "the action sheet to be shown", NativeDialogs.Timeout);
        Check(NativeDialogs.Describe(c.Window).Contains("Delete", StringComparison.Ordinal), $"the sheet offers Delete: {NativeDialogs.Describe(c.Window)}");
#if IOS || MACCATALYST
        Equal(UIKit.UIAlertActionStyle.Destructive, NativeDialogs.ActionStyle(c.Window, "Delete"), "the destruction's style");
        Equal(UIKit.UIAlertActionStyle.Default, NativeDialogs.ActionStyle(c.Window, "Mail"), "positive control: a button's style");
#endif
        await NativeDialogs.Answer(c.Window, "Delete");
        Equal("Delete", await closed.Task.WaitAsync(TimeSpan.FromSeconds(5)), "OnClosed's choice");
    }
}
