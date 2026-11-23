using Android.App;
using Android.Content.PM;
using Android.OS;

namespace Reactor.Maui.DeviceTests;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // `adb shell am start -e REACTOR_DEVICE_TEST_FILTER <substring>` (run.sh passes it on).
        if (Intent?.GetStringExtra("REACTOR_DEVICE_TEST_FILTER") is { Length: > 0 } filter)
            System.Environment.SetEnvironmentVariable("REACTOR_DEVICE_TEST_FILTER", filter);
        base.OnCreate(savedInstanceState);
    }
}
