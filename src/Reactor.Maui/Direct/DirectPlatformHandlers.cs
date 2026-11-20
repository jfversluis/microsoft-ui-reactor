using Microsoft.Maui.Handlers;

namespace Microsoft.UI.Reactor.Maui.Direct;

/// <summary>
/// MAUI's label handler plus word wrapping. Core's <see cref="ILabel"/> has no line-break mode:
/// Controls maps <c>Label.LineBreakMode</c>, which defaults to word wrap, so without this a direct
/// label would keep the platform default (a single truncated line on iOS and Mac Catalyst).
/// </summary>
public sealed class DirectLabelHandler : LabelHandler
{
    public static readonly IPropertyMapper<ILabel, ILabelHandler> DirectMapper =
        new PropertyMapper<ILabel, ILabelHandler>(Mapper)
        {
            ["ReactorWordWrap"] = MapWordWrap,
        };

    public DirectLabelHandler()
        : base(DirectMapper)
    {
    }

    private static void MapWordWrap(ILabelHandler handler, ILabel label)
    {
#if IOS || MACCATALYST
        handler.PlatformView.Lines = 0;
        handler.PlatformView.LineBreakMode = UIKit.UILineBreakMode.WordWrap;
#elif ANDROID
        handler.PlatformView.SetSingleLine(false);
        handler.PlatformView.SetMaxLines(int.MaxValue);
        handler.PlatformView.Ellipsize = null;
#elif WINDOWS
        handler.PlatformView.TextWrapping = global::Microsoft.UI.Xaml.TextWrapping.WrapWholeWords;
        handler.PlatformView.TextTrimming = global::Microsoft.UI.Xaml.TextTrimming.None;
#endif
    }
}
