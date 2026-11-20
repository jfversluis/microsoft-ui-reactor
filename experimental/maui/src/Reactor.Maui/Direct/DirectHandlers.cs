using Microsoft.Maui.Handlers;
using Microsoft.Maui.Hosting;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Maui.Handlers;

namespace Microsoft.UI.Reactor.Maui.Direct;

/// <summary>
/// The direct renderer: Label, Button, Entry and the stacks map to <see cref="DirectView"/>s that
/// MAUI's stock handlers render, skipping the Controls layer. Everything else falls back to the
/// Controls renderer, so the two mix in one tree.
/// </summary>
public static class DirectHandlers
{
    public static ElementHandlerRegistry CreateRegistry() => MauiHandlers.CreateRegistry()
        .Register(new LabelHandler())
        .Register(new ButtonHandler())
        .Register(new EntryHandler())
        .Register(new StackHandler<VStackElement>(StackOrientation.Vertical, e => e.Children, e => e.Spacing))
        .Register(new StackHandler<HStackElement>(StackOrientation.Horizontal, e => e.Children, e => e.Spacing));

    /// <summary>Registers MAUI's stock handlers for the direct view types.</summary>
    public static MauiAppBuilder UseReactorDirectViews(this MauiAppBuilder builder) =>
        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<DirectLabel, Microsoft.Maui.Handlers.LabelHandler>();
            handlers.AddHandler<DirectButton, Microsoft.Maui.Handlers.ButtonHandler>();
            handlers.AddHandler<DirectEntry, Microsoft.Maui.Handlers.EntryHandler>();
            handlers.AddHandler<DirectStackLayout, LayoutHandler>();
        });

    private static void ApplyView(DirectView view, ViewElement next)
    {
        view.SetMargin(next.Margin);
        view.SetWidth(next.Width);
        view.SetHorizontalOptions(next.HorizontalOptions);
    }

    private sealed class LabelHandler : Reactor.Hosting.IElementHandler<LabelElement, DirectLabel>
    {
        public DirectLabel Mount(HandlerContext<LabelElement> context, LabelElement element)
        {
            var label = new DirectLabel { AutomationId = element.AutomationId ?? "" };
            Apply(label, element);
            return label;
        }

        public void Update(HandlerContext<LabelElement> context, LabelElement oldElement, LabelElement newElement, DirectLabel control)
            => Apply(control, newElement);

        private static void Apply(DirectLabel label, LabelElement element)
        {
            label.SetText(element.Text);
            label.SetFont(element.FontSize, element.FontAttributes);
            label.SetTextDecorations(element.TextDecorations);
            ApplyView(label, element);
        }
    }

    private sealed class ButtonHandler : Reactor.Hosting.IElementHandler<ButtonElement, DirectButton>
    {
        public DirectButton Mount(HandlerContext<ButtonElement> context, ButtonElement element)
        {
            var button = new DirectButton { AutomationId = element.AutomationId ?? "" };
            button.OnClicked = () => context.Current.OnClick?.Invoke();
            Update(context, element, element, button);
            return button;
        }

        public void Update(HandlerContext<ButtonElement> context, ButtonElement oldElement, ButtonElement newElement, DirectButton control)
        {
            control.SetText(newElement.Text);
            ApplyView(control, newElement);
        }
    }

    private sealed class EntryHandler : Reactor.Hosting.IElementHandler<EntryElement, DirectEntry>
    {
        public DirectEntry Mount(HandlerContext<EntryElement> context, EntryElement element)
        {
            var entry = new DirectEntry { AutomationId = element.AutomationId ?? "" };
            entry.SetText(element.Text);
            entry.SetPlaceholder(element.Placeholder);
            ApplyView(entry, element);
            entry.OnTextChanged = text =>
            {
                if (context.IsEchoing(entry)) return;
                context.Current.OnTextChanged?.Invoke(text);
            };
            return entry;
        }

        public void Update(HandlerContext<EntryElement> context, EntryElement oldElement, EntryElement newElement, DirectEntry control)
        {
            if (control.Text != newElement.Text)
                context.WriteSuppressed(control, () => control.SetText(newElement.Text));
            control.SetPlaceholder(newElement.Placeholder);
            ApplyView(control, newElement);
        }
    }

    private sealed class StackHandler<TElement>(
        StackOrientation orientation,
        Func<TElement, IReadOnlyList<Element?>> getChildren,
        Func<TElement, double> getSpacing) : Reactor.Hosting.IElementHandler<TElement, DirectStackLayout>
        where TElement : ViewElement
    {
        public DirectStackLayout Mount(HandlerContext<TElement> context, TElement element)
        {
            var stack = new DirectStackLayout(orientation) { AutomationId = element.AutomationId ?? "" };
            Update(context, element, element, stack);
            return stack;
        }

        public void Update(HandlerContext<TElement> context, TElement oldElement, TElement newElement, DirectStackLayout control)
        {
            control.SetSpacing(getSpacing(newElement));
            ApplyView(control, newElement);
        }

        public IChildrenStrategy<TElement, DirectStackLayout> Children { get; } =
            ChildrenStrategy.List<TElement, DirectStackLayout, IView>(getChildren, stack => stack);
    }
}
