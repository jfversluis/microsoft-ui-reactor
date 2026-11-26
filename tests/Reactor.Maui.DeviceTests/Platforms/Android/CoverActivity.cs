using Android.App;
using Android.OS;
using Android.Widget;

namespace Reactor.Maui.DeviceTests;

/// <summary>An activity of the test app that covers the main one, so its window stops (spec 069 §5.5).</summary>
[Activity(Exported = false)]
public class CoverActivity : Activity
{
    public static CoverActivity? Current { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Current = this;
        SetContentView(new TextView(this) { Text = "cover" });
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(Current, this)) Current = null;
        base.OnDestroy();
    }
}
