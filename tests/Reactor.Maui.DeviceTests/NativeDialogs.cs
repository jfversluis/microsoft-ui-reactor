#if IOS || MACCATALYST
using Foundation;
using UIKit;
#endif

namespace Reactor.Maui.DeviceTests;

/// <summary>
/// The platform's own dialogs, found and answered in process the way the user would: through the
/// native button. Android: the dialog's window, found among the process's windows, and its button
/// clicked. iOS and Mac Catalyst: UIKit's own tap path, which dismisses the alert and runs the action
/// as a tap does (the button view's accessibility activation when an accessibility client is running,
/// else the alert's dismiss-for-action method the button calls). The action's private handler, with a
/// separate dismissal, is only a last resort. Either way the helper returns once the alert has gone.
/// </summary>
public static class NativeDialogs
{
    /// <summary>How long a dialog may take to appear or go on a slow (CI) simulator.</summary>
    public const int Timeout = 10_000;

    /// <summary>How many native dialogs are on screen (including one still dismissing on iOS).</summary>
    public static int Count(Window window)
    {
#if IOS || MACCATALYST
        return Alerts(window).Count();
#elif ANDROID
        return DialogRoots().Count();
#else
        return 0;
#endif
    }

    /// <summary>
    /// Whether a native dialog (titled <paramref name="title"/>, if given) is fully shown and can take a
    /// tap: presented, and (Android) focused.
    /// </summary>
    public static bool IsReady(Window window, string? title = null)
    {
#if IOS || MACCATALYST
        return Alerts(window).Any(a => !a.IsBeingPresented && !a.IsBeingDismissed && a.View?.Window is not null && (title is null || a.Title == title));
#elif ANDROID
        return DialogRoots().Any(r => r.HasWindowFocus && (title is null || Texts(r).Contains(title)));
#else
        return false;
#endif
    }

    /// <summary>The texts the shown dialog offers (its title, message and buttons), for diagnostics and checks.</summary>
    public static string Describe(Window window)
    {
#if IOS || MACCATALYST
        return string.Join(" | ", Alerts(window).Select(a =>
            $"{a.Title} [{a.PreferredStyle}{(a.IsBeingPresented ? ", presenting" : "")}{(a.IsBeingDismissed ? ", dismissing" : "")}]: {string.Join(",", a.Actions.Select(x => x.Title))}"));
#elif ANDROID
        return string.Join(" | ", DialogRoots().Select(r => string.Join(",", Texts(r))));
#else
        return "";
#endif
    }

#if IOS || MACCATALYST
    /// <summary>The text field of the prompt on screen, if one is.</summary>
    public static UITextField? PromptField(Window window) =>
        Alerts(window).LastOrDefault(a => !a.IsBeingDismissed)?.TextFields?.FirstOrDefault();

    /// <summary>The style of the action <paramref name="title"/> of the alert on screen.</summary>
    public static UIAlertActionStyle? ActionStyle(Window window, string title) =>
        Alerts(window).LastOrDefault(a => !a.IsBeingDismissed)?.Actions.FirstOrDefault(a => a.Title == title)?.Style;
#elif ANDROID
    /// <summary>The text field of the prompt on screen, if one is.</summary>
    public static Android.Widget.EditText? PromptField(Window window) =>
        DialogRoots().LastOrDefault() is { } root ? Find<Android.Widget.EditText>(root) : null;
#endif

    /// <summary>
    /// Answers the dialog on screen through its button <paramref name="button"/>, typing
    /// <paramref name="text"/> first for a prompt, and returns once the dialog has gone from the screen;
    /// fails with what is still shown otherwise.
    /// </summary>
    public static async Task Answer(Window window, string button, string? text = null, bool answerBeforeDismissal = false)
    {
#if IOS || MACCATALYST
        var alert = Alerts(window).LastOrDefault(a => !a.IsBeingDismissed)
            ?? throw new CheckFailedException("no alert controller on screen");
        var action = alert.Actions.FirstOrDefault(a => a.Title == button)
            ?? throw new CheckFailedException($"the alert has no '{button}' action: {Describe(window)}");
        if (text is not null) alert.TextFields![0].Text = text;
        if (answerBeforeDismissal)
        {
            // The answer arrives while the alert is still on screen (UIKit's tap runs the handler only
            // once the alert has gone): what the queue's barrier is for.
            RunHandler(action);
            alert.PresentingViewController?.DismissViewController(true, null);
        }
        else if (!Tap(alert, action))
        {
            Console.WriteLine($"#   UIKit's tap path is unavailable for '{button}'; the action's private handler runs instead");
            alert.PresentingViewController?.DismissViewController(true, null);
            RunHandler(action);
        }
        await Gone(window, alert, $"the alert answered with '{button}'");
#elif ANDROID
        var root = DialogRoots().LastOrDefault() ?? throw new CheckFailedException("no dialog window on screen");
        if (text is not null)
        {
            var edit = Find<Android.Widget.EditText>(root) ?? throw new CheckFailedException("the dialog has no text field");
            edit.Text = text;
        }
        if (!Click(root, button)) throw new CheckFailedException($"the dialog has no '{button}' button: {Describe(window)}");
        await Gone(window, root, $"the dialog answered with '{button}'");
#else
        await Task.CompletedTask;
        throw new CheckFailedException("native dialogs are not driven on this platform");
#endif
    }

#if IOS || MACCATALYST
    /// <summary>
    /// Runs the action <paramref name="button"/> of the top alert without dismissing it: an answer the
    /// app has had while UIKit still presents the alert.
    /// </summary>
    public static void AnswerWithoutDismissing(Window window, string button)
    {
        var alert = Alerts(window).LastOrDefault(a => !a.IsBeingDismissed)
            ?? throw new CheckFailedException("no alert controller on screen");
        var action = alert.Actions.FirstOrDefault(a => a.Title == button)
            ?? throw new CheckFailedException($"the alert has no '{button}' action: {Describe(window)}");
        RunHandler(action);
    }

    /// <summary>Dismisses the alert titled <paramref name="title"/> (and only it) and waits until it has gone.</summary>
    public static async Task Dismiss(Window window, string title)
    {
        var alert = Alerts(window).LastOrDefault(a => a.Title == title)
            ?? throw new CheckFailedException($"no alert titled '{title}': {Describe(window)}");
        alert.PresentingViewController?.DismissViewController(true, null);
        await Gone(window, alert, $"the alert '{title}' dismissed");
    }

    /// <summary>Presents an alert outside Reactor's queue, as the app or the system would, above what is shown.</summary>
    public static void PresentForeign(Window window, string title)
    {
        var top = (window.Handler?.PlatformView as UIWindow)?.RootViewController ?? throw new CheckFailedException("no root view controller");
        while (top.PresentedViewController is { IsBeingDismissed: false } presented) top = presented;
        var foreign = UIAlertController.Create(title, "Not Reactor's", UIAlertControllerStyle.Alert);
        foreign.AddAction(UIAlertAction.Create("Dismiss", UIAlertActionStyle.Cancel, null));
        top.PresentViewController(foreign, true, null);
    }
#elif ANDROID
    /// <summary>Shows an alert outside Reactor's queue, as the app or the system would.</summary>
    public static void PresentForeign(Window window, string title) =>
        new AndroidX.AppCompat.App.AlertDialog.Builder(Microsoft.Maui.ApplicationModel.Platform.CurrentActivity!)
            .SetTitle(title)!.SetMessage("Not Reactor's")!.SetNegativeButton("Dismiss", (_, _) => { })!.Create()!.Show();
#endif

    /// <summary>
    /// Closes every native dialog still on screen (through its cancel button, else by dismissing it),
    /// waiting until each has gone, so a failed fixture can't leave one that blocks the next fixture's
    /// dialogs. Returns whether the screen is clear.
    /// </summary>
    public static async Task<bool> CloseAll(Window window)
    {
#if IOS || MACCATALYST
        for (var round = 0; round < 4 && Alerts(window).LastOrDefault() is { } alert; round++)
        {
            if (!alert.IsBeingDismissed)
            {
                var cancel = alert.Actions.FirstOrDefault(a => a.Style == UIAlertActionStyle.Cancel) ?? alert.Actions.FirstOrDefault();
                if (cancel is null || !Tap(alert, cancel)) alert.PresentingViewController?.DismissViewController(false, null);
            }
            try
            {
                await Gone(window, alert, "a dialog left on screen");
            }
            catch (CheckFailedException)
            {
            }
        }
        return Count(window) == 0;
#elif ANDROID
        for (var round = 0; round < 4 && DialogRoots().LastOrDefault() is { } root; round++)
        {
            if (Find<Android.Widget.Button>(root) is { } any) any.PerformClick();
            try
            {
                await Gone(window, root, "a dialog left on screen");
            }
            catch (CheckFailedException)
            {
            }
        }
        return Count(window) == 0;
#else
        await Task.CompletedTask;
        return true;
#endif
    }

#if IOS || MACCATALYST
    // UIKit's own tap path, which dismisses the alert and then runs the action's handler. A button view's
    // accessibilityActivate is the public way in, but UIKit implements it only while an accessibility
    // client (VoiceOver, XCUITest) is running; otherwise the alert's dismiss-for-action method is what
    // the button calls on a tap.
    private static bool Tap(UIAlertController alert, UIAlertAction action)
    {
        if (alert.View is { } root)
        {
            foreach (var candidate in Descendants(root).Where(v => v.AccessibilityLabel == action.Title && v.IsAccessibilityElement))
                if (candidate.AccessibilityActivate()) return true;
        }
        var dismissForAction = new ObjCRuntime.Selector("_dismissAnimated:triggeringAction:triggeredByPopoverDimmingView:dismissCompletion:");
        if (!alert.RespondsToSelector(dismissForAction)) return false;
        DismissForAction(alert.Handle, dismissForAction.Handle, true, action.Handle, false, IntPtr.Zero);
        return true;

        static IEnumerable<UIView> Descendants(UIView view)
        {
            foreach (var child in view.Subviews)
            {
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }
    }

    [System.Runtime.InteropServices.DllImport(ObjCRuntime.Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void DismissForAction(IntPtr receiver, IntPtr selector, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I1)] bool animated, IntPtr action, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I1)] bool byDimmingView, IntPtr completion);

    // Waits until the alert has left the presentation chain. An animated dismissal UIKit dropped (it can
    // land as another transition finishes) is requested once more, without animation.
    private static async Task Gone(Window window, UIAlertController alert, string what)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var retried = false;
        while (alert.PresentingViewController is not null)
        {
            if (!retried && clock.ElapsedMilliseconds > 3000 && !alert.IsBeingDismissed)
            {
                retried = true;
                Console.WriteLine($"#   {what} was still presented after 3 s; dismissed again");
                alert.PresentingViewController?.DismissViewController(false, null);
            }
            if (clock.ElapsedMilliseconds > Timeout)
                throw new CheckFailedException($"{what} is still on screen after {Timeout} ms: {Describe(window)}");
            await Task.Delay(16);
        }
    }
#endif

#if ANDROID
    private static bool Click(Android.Views.View root, string button)
    {
        if (Find<Android.Widget.Button>(root, b => b.Text == button) is { } native)
        {
            native.PerformClick();
            return true;
        }
        // An action sheet's buttons are the rows of its list.
        if (Find<Android.Widget.ListView>(root) is { } list)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list.GetItemAtPosition(i)?.ToString() != button) continue;
                list.PerformItemClick(list.GetChildAt(i - list.FirstVisiblePosition), i, list.GetItemIdAtPosition(i));
                return true;
            }
        }
        return false;
    }

    private static async Task Gone(Window window, Android.Views.View root, string what)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (root.IsAttachedToWindow)
        {
            if (clock.ElapsedMilliseconds > Timeout)
                throw new CheckFailedException($"{what} is still on screen after {Timeout} ms: {Describe(window)}");
            await Task.Delay(16);
        }
    }
#endif

#if IOS || MACCATALYST
    private static IEnumerable<UIAlertController> Alerts(Window window)
    {
        if (window.Handler?.PlatformView is not UIWindow { RootViewController: { } root }) yield break;
        for (var controller = root; controller is not null; controller = controller.PresentedViewController)
            if (controller is UIAlertController alert) yield return alert;
    }

    // UIAlertAction keeps its handler block in a private property; a block's invoke function is the
    // fourth field of its layout (after isa, flags and reserved).
    private static unsafe void RunHandler(UIAlertAction action)
    {
        var block = action.ValueForKey(new NSString("handler")) ?? throw new CheckFailedException("the action has no handler");
        var invoke = *(delegate* unmanaged<IntPtr, IntPtr, void>*)(block.Handle + 16);
        invoke(block.Handle, action.Handle);
    }
#endif

#if ANDROID
    // The process's alert dialogs: windows other than the activity's own with an alert's buttons
    // (a MAUI modal is a dialog window too, without them).
    private static IEnumerable<Android.Views.View> DialogRoots()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var decor = activity?.Window?.DecorView;
        if (!OperatingSystem.IsAndroidVersionAtLeast(29)) throw new CheckFailedException("WindowInspector needs Android 10");
        return Android.Views.Inspectors.WindowInspector.GlobalWindowViews
            .Where(v => !ReferenceEquals(v, decor) && v.IsAttachedToWindow && v.Visibility == Android.Views.ViewStates.Visible
                && (v.FindViewById(Android.Resource.Id.Button1) is not null || v.FindViewById(Android.Resource.Id.Button2) is not null))
            .ToList();
    }

    private static IEnumerable<string> Texts(Android.Views.View view)
    {
        if (view is Android.Widget.TextView text && !string.IsNullOrEmpty(text.Text)) yield return text.Text;
        if (view is Android.Views.ViewGroup group)
            for (var i = 0; i < group.ChildCount; i++)
                foreach (var t in Texts(group.GetChildAt(i)!)) yield return t;
    }

    private static T? Find<T>(Android.Views.View? view, Func<T, bool>? predicate = null) where T : Android.Views.View
    {
        if (view is T match && view.IsShown && (predicate is null || predicate(match))) return match;
        if (view is Android.Views.ViewGroup group)
        {
            for (var i = 0; i < group.ChildCount; i++)
                if (Find(group.GetChildAt(i), predicate) is { } found) return found;
        }
        return null;
    }
#endif
}
