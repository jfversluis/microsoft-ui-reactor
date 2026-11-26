#if IOS || MACCATALYST
using Foundation;
using UIKit;
#endif

namespace Reactor.Maui.DeviceTests;

/// <summary>
/// The platform's own dialogs, found and answered in process the way the user would: through the
/// native button. Android: the dialog's window, found among the process's windows, and its button
/// clicked. iOS and Mac Catalyst: the alert controller in the window's presentation chain, dismissed,
/// and the chosen action's own handler run (UIKit exposes no public way to tap an action).
/// </summary>
public static class NativeDialogs
{
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

    /// <summary>Whether a native dialog is fully shown and can take a tap: presented, and (Android) focused.</summary>
    public static bool IsReady(Window window)
    {
#if IOS || MACCATALYST
        return Alerts(window).Any(a => !a.IsBeingPresented && !a.IsBeingDismissed && a.View?.Window is not null);
#elif ANDROID
        return DialogRoots().Any(r => r.HasWindowFocus);
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

    /// <summary>Answers the dialog on screen through its button <paramref name="button"/>, typing <paramref name="text"/> first for a prompt.</summary>
    public static void Answer(Window window, string button, string? text = null)
    {
#if IOS || MACCATALYST
        var alert = Alerts(window).LastOrDefault(a => !a.IsBeingDismissed)
            ?? throw new CheckFailedException("no alert controller on screen");
        var action = alert.Actions.FirstOrDefault(a => a.Title == button)
            ?? throw new CheckFailedException($"the alert has no '{button}' action: {Describe(window)}");
        if (text is not null) alert.TextFields![0].Text = text;
        // As a tap does: the alert dismisses (animated, so it stays in the chain for a while) and the
        // action's handler, MAUI's, sets the answer.
        alert.DismissViewController(true, null);
        RunHandler(action);
#elif ANDROID
        var root = DialogRoots().LastOrDefault() ?? throw new CheckFailedException("no dialog window on screen");
        if (text is not null)
        {
            var edit = Find<Android.Widget.EditText>(root) ?? throw new CheckFailedException("the dialog has no text field");
            edit.Text = text;
        }
        if (Find<Android.Widget.Button>(root, b => b.Text == button) is { } native)
        {
            native.PerformClick();
            return;
        }
        // An action sheet's buttons are the rows of its list.
        if (Find<Android.Widget.ListView>(root) is { } list)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list.GetItemAtPosition(i)?.ToString() != button) continue;
                list.PerformItemClick(list.GetChildAt(i - list.FirstVisiblePosition), i, list.GetItemIdAtPosition(i));
                return;
            }
        }
        throw new CheckFailedException($"the dialog has no '{button}' button: {Describe(window)}");
#else
        throw new CheckFailedException("native dialogs are not driven on this platform");
#endif
    }

    /// <summary>
    /// Dismisses again an alert whose handler already ran but which is still on screen and not
    /// dismissing: UIKit ignores a dismissal requested while another transition is finishing, which a
    /// tap never meets but the in-process answer can. Returns whether it asked.
    /// </summary>
    public static bool DismissAgain(Window window)
    {
#if IOS || MACCATALYST
        var stuck = Alerts(window).LastOrDefault(a => !a.IsBeingDismissed && !a.IsBeingPresented);
        if (stuck is null) return false;
        (stuck.PresentingViewController ?? stuck).DismissViewController(true, null);
        return true;
#else
        return false;
#endif
    }

    /// <summary>Closes whatever native dialog a failed fixture left on screen, so it can't block the next one.</summary>
    public static void DismissAll(Window window)
    {
#if IOS || MACCATALYST
        foreach (var alert in Alerts(window).Reverse())
        {
            var cancel = alert.Actions.FirstOrDefault(a => a.Style == UIAlertActionStyle.Cancel) ?? alert.Actions.FirstOrDefault();
            alert.DismissViewController(false, null);
            if (cancel is not null) RunHandler(cancel);
        }
#elif ANDROID
        foreach (var root in DialogRoots())
        {
            if (Find<Android.Widget.Button>(root) is { } button) button.PerformClick();
        }
#endif
    }

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
