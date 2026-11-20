using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using MauiButton = Microsoft.Maui.Controls.Button;
using MauiCheckBox = Microsoft.Maui.Controls.CheckBox;
using MauiEntry = Microsoft.Maui.Controls.Entry;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiScrollView = Microsoft.Maui.Controls.ScrollView;

namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>The MAUI handler set: one class per control, registered by <see cref="CreateRegistry"/>.</summary>
public static class MauiHandlers
{
    public static ElementHandlerRegistry CreateRegistry() => new ElementHandlerRegistry()
        .Register(new LabelHandler())
        .Register(new ButtonHandler())
        .Register(new EntryHandler())
        .Register(new CheckBoxHandler())
        .Register(new VStackHandler())
        .Register(new HStackHandler())
        .Register(new ScrollViewHandler());

    /// <summary>Applies <see cref="ViewElement"/>'s shared properties. <paramref name="previous"/> is <c>null</c> on mount.</summary>
    internal static void ApplyView(View view, ViewElement? previous, ViewElement next)
    {
        if (previous is null && next.AutomationId is { } automationId) view.AutomationId = automationId;
        Set(view, View.MarginProperty, previous?.Margin, next.Margin);
        Set(view, VisualElement.WidthRequestProperty, previous?.Width, next.Width);
        Set(view, View.HorizontalOptionsProperty, previous?.HorizontalOptions, next.HorizontalOptions);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IView, Grid> Wrappers = new();

    /// <summary>
    /// Controls-only slots (page or scroll view content) take a <see cref="View"/>. A view from
    /// another renderer, such as a direct view, is hosted in a Grid that is reused for it.
    /// </summary>
    internal static View AsView(object native) =>
        native as View ?? Wrappers.GetValue((IView)native, static view => new Grid { view });

    /// <summary>Writes a changed value, or clears the property back to its default when it was unset.</summary>
    internal static void Set<T>(BindableObject target, BindableProperty property, T? previous, T? next) where T : struct
    {
        if (Nullable.Equals(previous, next)) return;
        if (next is { } value) target.SetValue(property, value);
        else target.ClearValue(property);
    }

    private sealed class LabelHandler : Reactor.Hosting.IElementHandler<LabelElement, MauiLabel>
    {
        public MauiLabel Mount(HandlerContext<LabelElement> context, LabelElement element)
        {
            var label = new MauiLabel { Text = element.Text };
            Apply(label, null, element);
            return label;
        }

        public void Update(HandlerContext<LabelElement> context, LabelElement oldElement, LabelElement newElement, MauiLabel control)
        {
            if (oldElement.Text != newElement.Text) control.Text = newElement.Text;
            Apply(control, oldElement, newElement);
        }

        private static void Apply(MauiLabel label, LabelElement? previous, LabelElement next)
        {
            ApplyView(label, previous, next);
            Set(label, MauiLabel.FontSizeProperty, previous?.FontSize, next.FontSize);
            Set(label, MauiLabel.FontAttributesProperty, previous?.FontAttributes, next.FontAttributes);
            Set(label, MauiLabel.TextDecorationsProperty, previous?.TextDecorations, next.TextDecorations);
        }
    }

    private sealed class ButtonHandler : Reactor.Hosting.IElementHandler<ButtonElement, MauiButton>
    {
        public MauiButton Mount(HandlerContext<ButtonElement> context, ButtonElement element)
        {
            var button = new MauiButton { Text = element.Text };
            ApplyView(button, null, element);
            // Subscribed once; reads the current element so a re-render never needs to re-subscribe.
            button.Clicked += (_, _) => context.Current.OnClick?.Invoke();
            return button;
        }

        public void Update(HandlerContext<ButtonElement> context, ButtonElement oldElement, ButtonElement newElement, MauiButton control)
        {
            if (oldElement.Text != newElement.Text) control.Text = newElement.Text;
            ApplyView(control, oldElement, newElement);
        }
    }

    private sealed class EntryHandler : Reactor.Hosting.IElementHandler<EntryElement, MauiEntry>
    {
        public MauiEntry Mount(HandlerContext<EntryElement> context, EntryElement element)
        {
            var entry = new MauiEntry { Text = element.Text, Placeholder = element.Placeholder };
            ApplyView(entry, null, element);
            entry.TextChanged += (_, e) =>
            {
                if (context.IsEchoing(entry)) return;
                context.Current.OnTextChanged?.Invoke(e.NewTextValue ?? "");
                context.RestoreAfterEvent();
            };
            return entry;
        }

        public void Update(HandlerContext<EntryElement> context, EntryElement oldElement, EntryElement newElement, MauiEntry control)
        {
            // Compare with the control rather than the old element: the user may have typed since the last render.
            if ((control.Text ?? "") != newElement.Text)
                context.WriteSuppressed(control, () => control.Text = newElement.Text);
            if (oldElement.Placeholder != newElement.Placeholder) control.Placeholder = newElement.Placeholder;
            ApplyView(control, oldElement, newElement);
        }
    }

    private sealed class CheckBoxHandler : Reactor.Hosting.IElementHandler<CheckBoxElement, MauiCheckBox>
    {
        public MauiCheckBox Mount(HandlerContext<CheckBoxElement> context, CheckBoxElement element)
        {
            var checkBox = new MauiCheckBox { IsChecked = element.IsChecked };
            ApplyView(checkBox, null, element);
            checkBox.CheckedChanged += (_, e) =>
            {
                if (context.IsEchoing(checkBox)) return;
                context.Current.OnCheckedChanged?.Invoke(e.Value);
                context.RestoreAfterEvent();
            };
            return checkBox;
        }

        public void Update(HandlerContext<CheckBoxElement> context, CheckBoxElement oldElement, CheckBoxElement newElement, MauiCheckBox control)
        {
            if (control.IsChecked != newElement.IsChecked)
                context.WriteSuppressed(control, () => control.IsChecked = newElement.IsChecked);
            ApplyView(control, oldElement, newElement);
        }
    }

    private sealed class VStackHandler : Reactor.Hosting.IElementHandler<VStackElement, VerticalStackLayout>
    {
        public VerticalStackLayout Mount(HandlerContext<VStackElement> context, VStackElement element)
        {
            var stack = new VerticalStackLayout { Spacing = element.Spacing };
            ApplyView(stack, null, element);
            return stack;
        }

        public void Update(HandlerContext<VStackElement> context, VStackElement oldElement, VStackElement newElement, VerticalStackLayout control)
        {
            if (oldElement.Spacing != newElement.Spacing) control.Spacing = newElement.Spacing;
            ApplyView(control, oldElement, newElement);
        }

        public IChildrenStrategy<VStackElement, VerticalStackLayout> Children { get; } =
            ChildrenStrategy.List<VStackElement, VerticalStackLayout, IView>(e => e.Children, stack => stack.Children);
    }

    private sealed class HStackHandler : Reactor.Hosting.IElementHandler<HStackElement, HorizontalStackLayout>
    {
        public HorizontalStackLayout Mount(HandlerContext<HStackElement> context, HStackElement element)
        {
            var stack = new HorizontalStackLayout { Spacing = element.Spacing };
            ApplyView(stack, null, element);
            return stack;
        }

        public void Update(HandlerContext<HStackElement> context, HStackElement oldElement, HStackElement newElement, HorizontalStackLayout control)
        {
            if (oldElement.Spacing != newElement.Spacing) control.Spacing = newElement.Spacing;
            ApplyView(control, oldElement, newElement);
        }

        public IChildrenStrategy<HStackElement, HorizontalStackLayout> Children { get; } =
            ChildrenStrategy.List<HStackElement, HorizontalStackLayout, IView>(e => e.Children, stack => stack.Children);
    }

    private sealed class ScrollViewHandler : Reactor.Hosting.IElementHandler<ScrollViewElement, MauiScrollView>
    {
        public MauiScrollView Mount(HandlerContext<ScrollViewElement> context, ScrollViewElement element)
        {
            var scrollView = new MauiScrollView();
            ApplyView(scrollView, null, element);
            return scrollView;
        }

        public void Update(HandlerContext<ScrollViewElement> context, ScrollViewElement oldElement, ScrollViewElement newElement, MauiScrollView control)
            => ApplyView(control, oldElement, newElement);

        public IChildrenStrategy<ScrollViewElement, MauiScrollView> Children { get; } =
            ChildrenStrategy.Single<ScrollViewElement, MauiScrollView, object>(
                e => e.Content, (scrollView, content) => scrollView.Content = content is null ? null : AsView(content));
    }
}
