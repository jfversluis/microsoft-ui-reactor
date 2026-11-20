namespace Reactor.Maui.DeviceTests;

/// <summary>Reads state back from the native views, so fixtures check what the platform shows.</summary>
public static class Platform
{
    public static object? View(IView view) => view.Handler?.PlatformView;

    public static string? Text(IView view) => View(view) switch
    {
#if IOS || MACCATALYST
        UIKit.UILabel label => label.Text,
        UIKit.UIButton button => button.CurrentTitle,
        UIKit.UITextField field => field.Text,
        UIKit.UITextView textView => textView.Text,
#elif ANDROID
        Android.Widget.TextView textView => textView.Text,
#endif
        null => throw new CheckFailedException($"{view.GetType().Name} has no platform view"),
        var other => throw new CheckFailedException($"cannot read text from a {other.GetType().Name}"),
    };

    /// <summary>Types into a text field the way the keyboard does, so the handler's change path runs.</summary>
    public static void Type(IView view, string text)
    {
        switch (View(view))
        {
#if IOS || MACCATALYST
            case UIKit.UITextField field:
                field.Text = text;
                field.SendActionForControlEvents(UIKit.UIControlEvent.EditingChanged);
                break;
#elif ANDROID
            case Android.Widget.EditText edit:
                edit.Text = text;
                break;
#endif
            default:
                throw new CheckFailedException($"cannot type into a {view.GetType().Name}");
        }
    }

    /// <summary>The platform's own navigation depth, where the platform exposes it.</summary>
    public static int? NavigationDepth(NavigationPage page)
    {
#if IOS || MACCATALYST
        if (page.Handler is IPlatformViewHandler { ViewController: UIKit.UINavigationController controller })
            return controller.ViewControllers?.Length;
#endif
        return null;
    }

    /// <summary>The native view's size in device-independent units.</summary>
    public static Size Size(IView view)
    {
        switch (View(view))
        {
#if IOS || MACCATALYST
            case UIKit.UIView native:
                return new Size(native.Frame.Width, native.Frame.Height);
#elif ANDROID
            case Android.Views.View native:
                var density = native.Context?.Resources?.DisplayMetrics?.Density ?? 1;
                return new Size(native.Width / density, native.Height / density);
#endif
            default:
                throw new CheckFailedException($"{view.GetType().Name} has no platform view");
        }
    }

    /// <summary>The native view's frame in window coordinates, in device-independent units.</summary>
    public static Rect WindowFrame(IView view)
    {
        switch (View(view))
        {
#if IOS || MACCATALYST
            case UIKit.UIView native:
                var frame = native.ConvertRectToView(native.Bounds, null);
                return new Rect(frame.X, frame.Y, frame.Width, frame.Height);
#elif ANDROID
            case Android.Views.View native:
                var density = native.Context?.Resources?.DisplayMetrics?.Density ?? 1;
                var location = new int[2];
                native.GetLocationInWindow(location);
                return new Rect(location[0] / density, location[1] / density, native.Width / density, native.Height / density);
#endif
            default:
                throw new CheckFailedException($"{view.GetType().Name} has no platform view");
        }
    }

    /// <summary>
    /// The window's top safe-area inset on phones and tablets. Not on Mac Catalyst: there MAUI lays
    /// page content under the title bar, whatever renders it.
    /// </summary>
    public static double? SafeAreaTop(IView view)
    {
#if IOS && !MACCATALYST
        if (View(view) is UIKit.UIView { Window: { } window }) return window.SafeAreaInsets.Top;
#elif ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(30) && View(view) is Android.Views.View { RootWindowInsets: { } insets } native)
        {
            var bars = insets.GetInsets(Android.Views.WindowInsets.Type.SystemBars() | Android.Views.WindowInsets.Type.DisplayCutout());
            return bars.Top / (native.Context?.Resources?.DisplayMetrics?.Density ?? 1);
        }
#endif
        return null;
    }

    /// <summary>Clicks a button through its virtual view, the path the platform's tap handler takes.</summary>
    public static void Tap(IView view)
    {
        if (view is not IButton button) throw new CheckFailedException($"{view.GetType().Name} is not a button");
        button.Pressed();
        button.Released();
        button.Clicked();
    }
}
