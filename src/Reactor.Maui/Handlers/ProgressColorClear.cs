#if ANDROID
using System.Runtime.CompilerServices;
using Microsoft.Maui.Handlers;

namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>
/// Completes MAUI's clear of a progress bar's color on Android (spec 070 §4.15). MAUI 11 sets a color as
/// the bar's progress tint list but clears it by clearing a color filter, so a cleared color stays drawn.
/// Reactor wraps the mapping once, acting only on its own bars (<see cref="ReactorProgressBar"/> and
/// <c>DirectProgressBar</c>): when a color MAUI applied is cleared, the bar's own tint list (or the
/// theme's activated color) comes back. Other progress bars in the app keep MAUI's behavior.
/// </summary>
internal static class ProgressColorClear
{
    // The tint list each bar had before MAUI first tinted it (the theme's; a box, since it may be null).
    private static readonly ConditionalWeakTable<global::Android.Widget.ProgressBar, StrongBox<global::Android.Content.Res.ColorStateList?>> Original = new();
    private static int _registered;

    // A bar without a tint list draws its progress layer with the theme's activated color (the
    // drawable's own tint), which a null tint list would override.
    private static global::Android.Content.Res.ColorStateList? ThemeTint(global::Android.Widget.ProgressBar bar)
    {
        var value = new global::Android.Util.TypedValue();
        if (bar.Context?.Theme?.ResolveAttribute(global::Android.Resource.Attribute.ColorControlActivated, value, true) != true) return null;
        return value.ResourceId != 0
            ? AndroidX.Core.Content.ContextCompat.GetColorStateList(bar.Context!, value.ResourceId)
            : global::Android.Content.Res.ColorStateList.ValueOf(new global::Android.Graphics.Color(value.Data));
    }

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;
        ProgressBarHandler.Mapper.ModifyMapping<IProgress, IProgressBarHandler>(nameof(IProgress.ProgressColor), static (handler, view, previous) =>
        {
            // Not Reactor's, or an indeterminate bar or Material's indicator (they clear what they set).
            if (view is not (ReactorProgressBar or Direct.DirectProgressBar)
                || handler.PlatformView is not { Indeterminate: false } bar || bar is Google.Android.Material.ProgressIndicator.LinearProgressIndicator)
            {
                previous?.Invoke(handler, view);
                return;
            }
            if (view.ProgressColor is not null && !Original.TryGetValue(bar, out _)) Original.Add(bar, new(bar.ProgressTintList));
            previous?.Invoke(handler, view);
            if (view.ProgressColor is null && Original.TryGetValue(bar, out var original))
            {
                Original.Remove(bar);
                bar.ProgressTintList = original.Value ?? ThemeTint(bar);
            }
        });
    }
}
#endif
