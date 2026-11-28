using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Maui.Factories;
using MauiCarouselView = Microsoft.Maui.Controls.CarouselView;
using MauiIndicatorView = Microsoft.Maui.Controls.IndicatorView;

namespace Microsoft.UI.Reactor.Maui.Tests;

// Spec 070 §5.7 (4c-4): the carousel's and the indicator's claims. The pending list is empty after them.
public static partial class MatrixCases
{
    private static readonly string[] CarouselItems = ["a", "b", "c"];

    private static CarouselViewElement Carousel() => CarouselView(CarouselItems, static k => k, static k => Label(k), 0);

    private static IndicatorViewElement Indicator() => IndicatorView(5, 1);

    private static IEnumerable<PropertyCase> CarouselCases()
    {
        // Layout values are published to MAUI as a transaction, at rest (headless: at once, no platform).
        // MAUI's Loop defaults to true, Reactor's to false: the mount writes false, unsetting shows false.
        yield return Case(Carousel, nameof(CarouselViewElement.Loop), static (e, v) => e with { Loop = v }, static () => false, static () => true,
            static v => ((MauiCarouselView)v).Loop, MauiCarouselView.LoopProperty, clears: false) with { Cleared = static () => false };
        yield return Case(Carousel, nameof(CarouselViewElement.PeekAreaInsets), static (e, v) => e with { PeekAreaInsets = v },
            static () => (Thickness?)new Thickness(10, 0), static () => (Thickness?)new Thickness(20, 0),
            static v => ((MauiCarouselView)v).PeekAreaInsets, MauiCarouselView.PeekAreaInsetsProperty, expect: static v => v!.Value);
        // A layout of its own (the default is a shared static instance), rewritten when the spacing changes.
        yield return Case(Carousel, nameof(CarouselViewElement.ItemSpacing), static (e, v) => e with { ItemSpacing = v }, static () => 8d, static () => 16d,
            static v => ((LinearItemsLayout)((MauiCarouselView)v).ItemsLayout).ItemSpacing, MauiCarouselView.ItemsLayoutProperty, clears: false);

        yield return Case(Indicator, nameof(IndicatorViewElement.Count), static (e, v) => e with { Count = v }, static () => 3, static () => 7,
            static v => ((MauiIndicatorView)v).Count, MauiIndicatorView.CountProperty, clears: false) with { Cleared = static () => 5 };
        yield return Case(Indicator, nameof(IndicatorViewElement.IndicatorColor), static (e, v) => e with { IndicatorColor = v }, static () => Colors.Red, static () => Colors.Blue,
            static v => ((MauiIndicatorView)v).IndicatorColor, MauiIndicatorView.IndicatorColorProperty);
        yield return Case(Indicator, nameof(IndicatorViewElement.SelectedIndicatorColor), static (e, v) => e with { SelectedIndicatorColor = v }, static () => Colors.Red, static () => Colors.Blue,
            static v => ((MauiIndicatorView)v).SelectedIndicatorColor, MauiIndicatorView.SelectedIndicatorColorProperty);
        yield return Case(Indicator, nameof(IndicatorViewElement.IndicatorSize), static (e, v) => e with { IndicatorSize = v }, static () => (double?)10, static () => (double?)14,
            static v => ((MauiIndicatorView)v).IndicatorSize, MauiIndicatorView.IndicatorSizeProperty, expect: static v => v!.Value);
        yield return Case(Indicator, nameof(IndicatorViewElement.IndicatorsShape), static (e, v) => e with { IndicatorsShape = v },
            static () => (IndicatorShape?)IndicatorShape.Square, static () => (IndicatorShape?)IndicatorShape.Circle,
            static v => ((MauiIndicatorView)v).IndicatorsShape, MauiIndicatorView.IndicatorsShapeProperty, expect: static v => v!.Value);
        yield return Case(Indicator, nameof(IndicatorViewElement.MaximumVisible), static (e, v) => e with { MaximumVisible = v }, static () => (int?)3, static () => (int?)4,
            static v => ((MauiIndicatorView)v).MaximumVisible, MauiIndicatorView.MaximumVisibleProperty, expect: static v => v!.Value);
        yield return Case(Indicator, nameof(IndicatorViewElement.HideSingle), static (e, v) => e with { HideSingle = v }, static () => false, static () => true,
            static v => ((MauiIndicatorView)v).HideSingle, MauiIndicatorView.HideSingleProperty, clears: false);
    }

    private static IEnumerable<ControlledCase> CarouselControlled() =>
    [
        // A tap on an indicator: the platform sets the position (iOS UIPageControl, Android a click, §5.1).
        ControlledValue(typeof(IndicatorViewElement), nameof(IndicatorViewElement.Position), nameof(IndicatorViewElement.OnPositionChanged),
            static (int v, Action<string> r) => IndicatorView(5, v, x => r($"{x}")),
            static v => ((MauiIndicatorView)v).Position, static (v, x) => ((MauiIndicatorView)v).Position = x, 1, 2, 3, nameof(MauiIndicatorView.Position)),
    ];

    private static IEnumerable<CoveredClaim> CarouselCovered() =>
    [
        new(typeof(CarouselViewElement), nameof(CarouselViewElement.Position), typeof(CarouselPositionTests), nameof(CarouselPositionTests.Mount_WritesThePositionClamped_AndLaterDeclarationsNeverWritePosition),
            "written once at mount, clamped; later declarations projected through ScrollTo at rest, never written to Position; clamped beyond the count; nothing for an empty list "
            + "(with ADeclaredChange_IsProjected_AndItsOwnSettleIsNotReported, ALongHold_WithUnrelatedRenders_WritesNothing_AndTheReleaseReportsOnce, ASupersededProjection_SettlesUnreported_AndTheNewestIsProjected)",
            CarouselMutants.Position),
        new(typeof(CarouselViewElement), nameof(CarouselViewElement.OnPositionChanged), typeof(CarouselPositionTests), nameof(CarouselPositionTests.Input_IsReportedOnceAtRest_NeverWhileMoving),
            "input reported once at rest, never while moving; refused restored at rest; accepted writes nothing; a motionless jump reported; the current callback, nothing after unmount "
            + "(with ARefusal_ScrollsBackAtRest_AndTheLandingIsNotReported, AnAcceptance_WritesNothing_AndMovesIsCurrentWithoutRemounting, AJumpWithoutMotion_IsReported, TheReport_ReachesTheCurrentCallback_AndNothingAfterUnmount)",
            CarouselMutants.OnPositionChanged),
        new(typeof(CarouselViewElement), nameof(CarouselViewElement.Items), typeof(CarouselPositionTests), nameof(CarouselPositionTests.ItemsAndADeclarationDuringAHold_WaitForRest),
            "published at rest as a transaction (the native list unchanged while moving), then the declaration jumped to behind the barrier; epochs own their barriers "
            + "(with Barriers_BelongToTheirEpoch, AMoveDuringAHoldWithAnItemsChange_IsReportedFirst_MappedByKey)",
            CarouselMutants.Items),
        new(typeof(CarouselViewElement), nameof(CarouselViewElement.ItemKey), typeof(CarouselPositionTests), nameof(CarouselPositionTests.ItemsAndKeys_SyncByKey_KeepingBoxes),
            "reorder keeps each item's box (the cell's binding), removal drops boxes, the order follows the keys",
            CarouselMutants.ItemKey),
        new(typeof(CarouselViewElement), nameof(CarouselViewElement.ItemTemplate), typeof(CarouselPositionTests), nameof(CarouselPositionTests.AnAcceptance_WritesNothing_AndMovesIsCurrentWithoutRemounting),
            "IsCurrent from the declaration, re-rendered in place (the cell's native content kept) "
            + "(with ARecycledCell_RendersItsNewItemsState: a rebound cell renders its new item's state)",
            CarouselMutants.ItemTemplate),
        new(typeof(CarouselViewElement), nameof(CarouselViewElement.IsScrollAnimated), typeof(CarouselPositionTests), nameof(CarouselPositionTests.IsScrollAnimated_DecidesADeclaredChange_ItemsChangesAlwaysJump),
            "a declared change animates by the flag (false, then true); an items change's projection jumps",
            CarouselMutants.IsScrollAnimated),
    ];
}

/// <summary>The mutation evidence of the carousel's covered-by claims (spec 070 §5.13).</summary>
internal static class CarouselMutants
{
    public const string Position =
        "Position written on every update (H1) and not at mount (H6), a projection while moving (C4), the restore not projecting (H5), "
        + "an off-center landing accepted (C18), no retry cap (C17): red; on device the iOS port without its readiness gate (DV1) "
        + "and through MAUI's ScrollTo instead of the page offset (DV2): red (spec 070 §5.13)";
    public const string OnPositionChanged =
        "reports from the poll while moving (C1), no wait for the answer (C2), every other rest taken for input (C3), the report "
        + "not mapped by key (H3), a ref ScrollTo ignored (C10), a drag in the barrier window not input (C15) or projected over (C16), "
        + "an own settle reported (C11): red; on device reports while moving (DV5): red (spec 070 §5.13)";
    public const string Items =
        "items published while moving (C5), synced at once without a transaction (H7), an obsolete barrier completion acting (C6) "
        + "or not reissuing (C7): red; on device the Android barrier ignoring the item animator (DV3): red (spec 070 §5.13)";
    public const string ItemKey = "new boxes on every publish instead of keeping them by key (H4): red (spec 070 §5.13)";
    public const string ItemTemplate = "IsCurrent from the previous declaration (H2): red (spec 070 §5.13)";
    public const string IsScrollAnimated =
        "a declared change always animated (C13) and an items change's deferred projection animated (C14): red (spec 070 §5.13)";
}
