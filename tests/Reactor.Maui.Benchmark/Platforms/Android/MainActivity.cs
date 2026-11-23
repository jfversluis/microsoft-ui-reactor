using Android.App;
using Android.Content.PM;
using Android.OS;

namespace Reactor.Maui.Benchmark;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // `adb shell am start -e REACTOR_BENCH_MODE anchor` (run.sh passes it on).
        if (Intent?.GetStringExtra("REACTOR_BENCH_MODE") is { Length: > 0 } mode)
            System.Environment.SetEnvironmentVariable("REACTOR_BENCH_MODE", mode);
        base.OnCreate(savedInstanceState);
    }
}
