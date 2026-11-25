using System.Runtime.CompilerServices;
using Microsoft.Maui.Layouts;
using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>
/// Base of the Controls handlers: creates the view, then applies the control's own properties
/// and the shared <see cref="ViewModifiers"/>. Subclasses only describe their control.
/// </summary>
internal abstract class ViewHandler<TElement, TView> : Reactor.Hosting.IElementHandler<TElement, TView>
    where TElement : ViewElement
    where TView : View
{
    public TView Mount(HandlerContext<TElement> context, TElement element)
    {
        var view = Create(context, element);
        Apply(context, view, null, element);
        ViewApplier.Apply(context, view, null, element.Modifiers);
        return view;
    }

    public void Update(HandlerContext<TElement> context, TElement oldElement, TElement newElement, TView control)
    {
        Apply(context, control, oldElement, newElement);
        ViewApplier.Apply(context, control, oldElement.Modifiers, newElement.Modifiers);
    }

    public virtual void Unmount(TView control) { }

    public virtual IChildrenStrategy<TElement, TView>? Children => null;

    /// <summary>Creates the view and subscribes its events. Events invoke callbacks through <c>context.Live</c>.</summary>
    protected abstract TView Create(HandlerContext<TElement> context, TElement element);

    /// <summary>Applies the control's own properties. <paramref name="previous"/> is <c>null</c> on mount.</summary>
    protected abstract void Apply(HandlerContext<TElement> context, TView view, TElement? previous, TElement next);
}

/// <summary>Property-writing helpers shared by the handlers.</summary>
internal static class Props
{
    /// <summary>Writes a changed value, or clears the property back to its default when it was unset.</summary>
    public static void Set<T>(BindableObject target, BindableProperty property, T? previous, T? next) where T : struct
    {
        if (Nullable.Equals(previous, next)) return;
        if (next is { } value) target.SetValue(property, value);
        else target.ClearValue(property);
    }

    /// <summary>Reference-type variant of <see cref="Set{T}(BindableObject, BindableProperty, T?, T?)"/>, compared with <see cref="object.Equals(object?, object?)"/>.</summary>
    public static void SetRef<T>(BindableObject target, BindableProperty property, T? previous, T? next) where T : class
    {
        if (Equals(previous, next)) return;
        if (next is not null) target.SetValue(property, next);
        else target.ClearValue(property);
    }

    /// <summary>Applies the shared text settings of a <see cref="TextViewElement"/>.</summary>
    /// <remarks>Every Controls text control shares the same font and text-color bindable properties.</remarks>
    public static void ApplyText(BindableObject view, TextViewElement? previous, TextViewElement next)
    {
        Set(view, Label.FontSizeProperty, previous?.FontSize, next.FontSize);
        Set(view, Label.FontAttributesProperty, previous?.FontAttributes, next.FontAttributes);
        SetRef(view, Label.FontFamilyProperty, previous?.FontFamily, next.FontFamily);
        SetRef(view, Label.TextColorProperty, previous?.TextColor, next.TextColor);
        Set(view, Label.CharacterSpacingProperty, previous?.CharacterSpacing, next.CharacterSpacing);
    }

    /// <summary>
    /// Whether two image sources show the same image. Factories create a new source on every
    /// render, so reference equality would reload the image each time.
    /// </summary>
    public static bool SameImage(ImageSource? a, ImageSource? b) => (a, b) switch
    {
        (null, null) => true,
        (FileImageSource fa, FileImageSource fb) => fa.File == fb.File,
        (UriImageSource ua, UriImageSource ub) => ua.Uri == ub.Uri,
        _ => ReferenceEquals(a, b),
    };

    public static void SetImage(BindableObject target, BindableProperty property, ImageSource? previous, ImageSource? next, bool mounting)
    {
        if (!mounting && SameImage(previous, next)) return;
        if (next is not null) target.SetValue(property, next);
        else if (!mounting) target.ClearValue(property);
    }

    private static readonly ConditionalWeakTable<IView, DirectViewHost> Wrappers = new();

    /// <summary>
    /// Controls-only slots (page, scroll view or border content) take a <see cref="View"/>. A view
    /// from another renderer, such as a direct view, is hosted in a Grid that is reused for it.
    /// </summary>
    public static View AsView(object native) =>
        native as View ?? Wrappers.GetValue((IView)native, static view =>
        {
            var wrapper = new DirectViewHost { view };
            if (view is Direct.DirectView direct) direct.AttachToControlsParent(wrapper);
            return wrapper;
        });
}

/// <summary>Applies <see cref="ViewModifiers"/> to a Controls view.</summary>
internal static class ViewApplier
{

    private sealed class GestureState
    {
        public TapGestureRecognizer? Tap;
        public SwipeGestureRecognizer[]? Swipes;
        public PanGestureRecognizer? Pan;
    }

    private static readonly ConditionalWeakTable<View, GestureState> Gestures = new();

    public static void Apply<TElement>(HandlerContext<TElement> context, View view, ViewModifiers? previous, ViewModifiers? next)
        where TElement : ViewElement
    {
        var p = previous ?? ViewModifiers.Empty;
        var n = next ?? ViewModifiers.Empty;
        if (!ReferenceEquals(p, n))
        {
            if (previous is null && n.AutomationId is { } automationId) view.AutomationId = automationId;

            Props.Set(view, View.MarginProperty, p.Margin, n.Margin);
            Props.Set(view, VisualElement.WidthRequestProperty, p.Width, n.Width);
            Props.Set(view, VisualElement.HeightRequestProperty, p.Height, n.Height);
            Props.Set(view, VisualElement.MinimumWidthRequestProperty, p.MinWidth, n.MinWidth);
            Props.Set(view, VisualElement.MinimumHeightRequestProperty, p.MinHeight, n.MinHeight);
            Props.Set(view, VisualElement.MaximumWidthRequestProperty, p.MaxWidth, n.MaxWidth);
            Props.Set(view, VisualElement.MaximumHeightRequestProperty, p.MaxHeight, n.MaxHeight);
            Props.Set(view, View.HorizontalOptionsProperty, p.HorizontalOptions, n.HorizontalOptions);
            Props.Set(view, View.VerticalOptionsProperty, p.VerticalOptions, n.VerticalOptions);

            Props.Set(view, Microsoft.Maui.Controls.Grid.RowProperty, p.GridRow, n.GridRow);
            Props.Set(view, Microsoft.Maui.Controls.Grid.ColumnProperty, p.GridColumn, n.GridColumn);
            Props.Set(view, Microsoft.Maui.Controls.Grid.RowSpanProperty, p.GridRowSpan, n.GridRowSpan);
            Props.Set(view, Microsoft.Maui.Controls.Grid.ColumnSpanProperty, p.GridColumnSpan, n.GridColumnSpan);
            Props.Set(view, Microsoft.Maui.Controls.FlexLayout.GrowProperty, p.FlexGrow, n.FlexGrow);
            Props.Set(view, Microsoft.Maui.Controls.FlexLayout.ShrinkProperty, p.FlexShrink, n.FlexShrink);
            Props.Set(view, Microsoft.Maui.Controls.FlexLayout.BasisProperty, p.FlexBasis, n.FlexBasis);
            Props.Set(view, Microsoft.Maui.Controls.FlexLayout.AlignSelfProperty, p.FlexAlignSelf, n.FlexAlignSelf);
            Props.Set(view, Microsoft.Maui.Controls.AbsoluteLayout.LayoutBoundsProperty, p.AbsoluteBounds, n.AbsoluteBounds);
            Props.Set(view, Microsoft.Maui.Controls.AbsoluteLayout.LayoutFlagsProperty, p.AbsoluteFlags, n.AbsoluteFlags);

            Props.SetRef(view, VisualElement.BackgroundColorProperty, p.BackgroundColor, n.BackgroundColor);
            Props.SetRef(view, VisualElement.BackgroundProperty, p.Background, n.Background);
            Props.Set(view, VisualElement.OpacityProperty, p.Opacity, n.Opacity);
            Props.Set(view, VisualElement.IsVisibleProperty, p.IsVisible, n.IsVisible);
            Props.Set(view, VisualElement.IsEnabledProperty, p.IsEnabled, n.IsEnabled);
            Props.Set(view, VisualElement.RotationProperty, p.Rotation, n.Rotation);
            Props.Set(view, VisualElement.ScaleProperty, p.Scale, n.Scale);
            Props.Set(view, VisualElement.TranslationXProperty, p.TranslationX, n.TranslationX);
            Props.Set(view, VisualElement.TranslationYProperty, p.TranslationY, n.TranslationY);
            Props.Set(view, VisualElement.ZIndexProperty, p.ZIndex, n.ZIndex);
            Props.SetRef(view, VisualElement.ShadowProperty, p.Shadow, n.Shadow);
            Props.SetRef(view, VisualElement.ClipProperty, p.Clip, n.Clip);
            Props.Set(view, VisualElement.InputTransparentProperty, p.InputTransparent, n.InputTransparent);
            Props.SetRef(view, NavigableElement.StyleProperty, p.Style, n.Style);
            if (!SameClasses(p.StyleClass, n.StyleClass)) view.StyleClass = n.StyleClass ?? [];

            if (p.SemanticDescription != n.SemanticDescription) SemanticProperties.SetDescription(view, n.SemanticDescription);
            if (p.SemanticHint != n.SemanticHint) SemanticProperties.SetHint(view, n.SemanticHint);
            Props.Set(view, SemanticProperties.HeadingLevelProperty, p.SemanticHeadingLevel, n.SemanticHeadingLevel);

            ApplyGestures(context, view, p, n);
        }

        n.Configure?.Invoke(view);
    }

    private static bool SameClasses(string[]? a, string[]? b) =>
        ReferenceEquals(a, b) || (a is not null && b is not null && a.AsSpan().SequenceEqual(b));

    // Recognizers are added once and read the current element when they fire, so a re-render
    // that only swaps the callback does not touch the view.
    private static void ApplyGestures<TElement>(HandlerContext<TElement> context, View view, ViewModifiers p, ViewModifiers n)
        where TElement : ViewElement
    {
        if (p.OnTap is null && n.OnTap is null && p.OnSwipe is null && n.OnSwipe is null && p.OnPan is null && n.OnPan is null)
            return;

        var state = Gestures.GetOrCreateValue(view);

        if (n.OnTap is not null && state.Tap is null)
        {
            state.Tap = new TapGestureRecognizer();
            state.Tap.Tapped += (_, _) => context.Live?.Modifiers?.OnTap?.Invoke();
            view.GestureRecognizers.Add(state.Tap);
        }
        else if (n.OnTap is null && state.Tap is not null)
        {
            view.GestureRecognizers.Remove(state.Tap);
            state.Tap = null;
        }
        if (state.Tap is not null) state.Tap.NumberOfTapsRequired = n.TapCount ?? 1;

        if (n.OnSwipe is not null && state.Swipes is null)
        {
            state.Swipes = [.. new[] { SwipeDirection.Left, SwipeDirection.Right, SwipeDirection.Up, SwipeDirection.Down }.Select(direction =>
            {
                var swipe = new SwipeGestureRecognizer { Direction = direction };
                swipe.Swiped += (_, e) => context.Live?.Modifiers?.OnSwipe?.Invoke(e.Direction);
                view.GestureRecognizers.Add(swipe);
                return swipe;
            })];
        }
        else if (n.OnSwipe is null && state.Swipes is not null)
        {
            foreach (var swipe in state.Swipes) view.GestureRecognizers.Remove(swipe);
            state.Swipes = null;
        }

        if (n.OnPan is not null && state.Pan is null)
        {
            state.Pan = new PanGestureRecognizer();
            state.Pan.PanUpdated += (_, e) => context.Live?.Modifiers?.OnPan?.Invoke(e);
            view.GestureRecognizers.Add(state.Pan);
        }
        else if (n.OnPan is null && state.Pan is not null)
        {
            view.GestureRecognizers.Remove(state.Pan);
            state.Pan = null;
        }
    }
}

/// <summary>
/// The Grid that hosts a direct view in a Controls slot. Controls layouts list only Controls
/// elements as visual children; this one lists the direct view too, so visual-tree tools see it.
/// </summary>
internal sealed class DirectViewHost : Grid, IVisualTreeElement
{
    IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => [.. Children.OfType<IVisualTreeElement>()];
}

/// <summary>What happens to a view once Reactor has removed it.</summary>
internal static class RemovedViews
{
    /// <summary>
    /// Disconnects a removed view's handlers, with its subtree. MAUI keeps them otherwise, and some
    /// views stay subscribed to app-wide events: on Android a theme change then reached a removed
    /// <c>SearchBar</c>'s released native views and crashed (SIGSEGV in
    /// <c>SearchBarHandler.MapPlaceholderColor</c>). As MAUI does for a popped page, a view that is
    /// still loaded (still animating out, for example) is disconnected when it unloads.
    /// <c>HandlerDisconnectPolicy.Manual</c> opts a view out. Pages are left to MAUI, which
    /// disconnects a page, and its content, once it has been popped.
    /// </summary>
    public static void Release(object native)
    {
        if (native is Page || native is not IView { Handler: not null } view) return;
        if (view is VisualElement { IsLoaded: true } loaded)
        {
            void OnUnloaded(object? sender, EventArgs e)
            {
                loaded.Unloaded -= OnUnloaded;
                try
                {
                    if (loaded.Handler is not null) loaded.DisconnectHandlers();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError($"Reactor: disconnecting a removed view failed: {ex}");
                }
            }
            loaded.Unloaded += OnUnloaded;
            return;
        }
        view.DisconnectHandlers();
    }
}
