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

    /// <summary>Flips a switch the way a tap does, so the handler's change path runs.</summary>
    public static void Toggle(ISwitch view, bool on)
    {
        switch (View(view))
        {
#if IOS || MACCATALYST
            case UIKit.UISwitch native:
                native.SetState(on, false);
                native.SendActionForControlEvents(UIKit.UIControlEvent.ValueChanged);
                break;
#elif ANDROID
            case Android.Widget.CompoundButton native:
                native.Checked = on;
                break;
#endif
            default:
                throw new CheckFailedException($"cannot toggle a {view.GetType().Name}");
        }
    }

    /// <summary>Whether the platform switch shows on.</summary>
    public static bool IsOn(ISwitch view) => View(view) switch
    {
#if IOS || MACCATALYST
        UIKit.UISwitch native => native.On,
#elif ANDROID
        Android.Widget.CompoundButton native => native.Checked,
#endif
        var other => throw new CheckFailedException($"cannot read a {other?.GetType().Name ?? "missing"} switch"),
    };

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

    /// <summary>The window's left and right safe-area insets on phones and tablets (0 elsewhere).</summary>
    public static (double Left, double Right) SafeAreaHorizontal(IView view)
    {
#if IOS && !MACCATALYST
        if (View(view) is UIKit.UIView { Window: { } window }) return (window.SafeAreaInsets.Left, window.SafeAreaInsets.Right);
#elif ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(30) && View(view) is Android.Views.View { RootWindowInsets: { } insets } native)
        {
            var bars = insets.GetInsets(Android.Views.WindowInsets.Type.SystemBars() | Android.Views.WindowInsets.Type.DisplayCutout());
            var density = native.Context?.Resources?.DisplayMetrics?.Density ?? 1;
            return (bars.Left / density, bars.Right / density);
        }
#endif
        return (0, 0);
    }

    /// <summary>
    /// Goes back the way the platform does for the user. iOS and Mac Catalyst: the navigation
    /// controller pops its top view controller, as its back button does, and MAUI follows. Android: the
    /// activity's back dispatcher, the back key's path through MAUI's window. Returns whether the
    /// platform popped the page itself (iOS) rather than asking the app first (Android).
    /// </summary>
    public static bool NativeBack(NavigationPage page)
    {
#if IOS || MACCATALYST
        if (page.Handler is IPlatformViewHandler { ViewController: UIKit.UINavigationController controller })
        {
            controller.PopViewController(true);
            return true;
        }
        throw new CheckFailedException("the navigation page has no UINavigationController");
#elif ANDROID
        ((AndroidX.Activity.ComponentActivity)Microsoft.Maui.ApplicationModel.Platform.CurrentActivity!).OnBackPressedDispatcher.OnBackPressed();
        return false;
#else
        page.SendBackButtonPressed();
        return false;
#endif
    }

    /// <summary>Whether the platform shows <paramref name="page"/>: its native view is in the window and visible.</summary>
    public static bool IsDisplayed(Page page)
    {
#if IOS || MACCATALYST
        return page.Handler is IPlatformViewHandler { ViewController.View: { Window: not null, Hidden: false } };
#elif ANDROID
        // A hidden tab's or covered page's view can stay attached; only one on screen has a visible rectangle.
        return page.Handler?.PlatformView is Android.Views.View { IsAttachedToWindow: true, IsShown: true, Width: > 0 } view
            && view.GetGlobalVisibleRect(new Android.Graphics.Rect());
#else
        return page.IsLoaded;
#endif
    }

    /// <summary>
    /// The depth of the native navigation stack <paramref name="top"/> is in (iOS and Mac Catalyst: the
    /// section's UINavigationController, root included), where the platform exposes it.
    /// </summary>
    public static int? SectionDepth(Page top)
    {
#if IOS || MACCATALYST
        if (top.Handler is IPlatformViewHandler { ViewController: { } controller })
            return (controller.NavigationController ?? controller.ParentViewController?.NavigationController)?.ViewControllers?.Length;
#endif
        return null;
    }

    /// <summary>
    /// The user's back in a Shell section, the way the platform delivers it. iOS and Mac Catalyst: the
    /// navigation bar asks the section's navigation controller whether to pop its top item, as the back
    /// button does. Android: the activity's back dispatcher, the back key's path through MAUI's window.
    /// </summary>
    public static void ShellBack(ShellSection section)
    {
#if IOS || MACCATALYST
        var top = section.Stack[^1];
        if (top.Handler is IPlatformViewHandler { ViewController.NavigationController: Microsoft.Maui.Controls.Platform.Compatibility.ShellSectionRenderer renderer }
            && renderer.NavigationBar.TopItem is { } item)
        {
            renderer.ShouldPopItem(renderer.NavigationBar, item);
            return;
        }
        throw new CheckFailedException("the section's top page has no Shell navigation controller");
#elif ANDROID
        ((AndroidX.Activity.ComponentActivity)Microsoft.Maui.ApplicationModel.Platform.CurrentActivity!).OnBackPressedDispatcher.OnBackPressed();
#else
        ((Shell)section.Parent.Parent).SendBackButtonPressed();
#endif
    }

    /// <summary>Whether the platform shows the Shell's navigation bar above <paramref name="page"/>, where it can be read.</summary>
    public static bool? NavBarShown(Page page)
    {
#if IOS || MACCATALYST
        if (page.Handler is IPlatformViewHandler { ViewController: { } controller }
            && (controller.NavigationController ?? controller.ParentViewController?.NavigationController) is { } navigation)
            return !navigation.NavigationBarHidden;
#elif ANDROID
        // MAUI hides the toolbar by collapsing its app bar to no height.
        if (Find<AndroidX.AppCompat.Widget.Toolbar>(Root()) is { } toolbar) return toolbar is { IsShown: true, Height: > 0 };
#endif
        return null;
    }

    /// <summary>Whether the platform shows the Shell's tab bar, where it can be read.</summary>
    public static bool? TabBarShown(Page page)
    {
#if IOS || MACCATALYST
        if (page.Handler is IPlatformViewHandler { ViewController.TabBarController: { } tabs }) return !tabs.TabBar.Hidden;
        return null;
#elif ANDROID
        // MAUI removes the bottom navigation view to hide it.
        return Find<Google.Android.Material.BottomNavigation.BottomNavigationView>(Root()) is { IsShown: true, Height: > 0 };
#else
        return null;
#endif
    }

    /// <summary>The bars the platform has, for a failure message.</summary>
    public static string Describe()
    {
#if ANDROID
        var parts = new List<string>();
        void Walk(Android.Views.View? view, int depth)
        {
            if (view is null) return;
            if (view is AndroidX.AppCompat.Widget.Toolbar or Google.Android.Material.BottomNavigation.BottomNavigationView or Google.Android.Material.AppBar.AppBarLayout)
                parts.Add($"{view.GetType().Name}(shown={view.IsShown}, vis={view.Visibility}, h={view.Height}, depth={depth})");
            if (view is Android.Views.ViewGroup group)
                for (var i = 0; i < group.ChildCount; i++) Walk(group.GetChildAt(i), depth + 1);
        }
        Walk(Root(), 0);
        return string.Join("; ", parts);
#else
        return "";
#endif
    }

#if ANDROID
    private static Android.Views.View? Root() => Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window?.DecorView;

    private static T? Find<T>(Android.Views.View? view) where T : Android.Views.View
    {
        if (view is T match) return match;
        if (view is Android.Views.ViewGroup group)
        {
            for (var i = 0; i < group.ChildCount; i++)
                if (Find<T>(group.GetChildAt(i)) is { } found) return found;
        }
        return null;
    }
#endif

    /// <summary>Clicks a button through its virtual view, the path the platform's tap handler takes.</summary>
    public static void Tap(IView view)
    {
        if (view is not IButton button) throw new CheckFailedException($"{view.GetType().Name} is not a button");
        button.Pressed();
        button.Released();
        button.Clicked();
    }
}
