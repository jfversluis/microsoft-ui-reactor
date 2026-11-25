using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>The element-type to handler map a renderer hands to a <see cref="ReactorRoot"/>.</summary>
public sealed class ElementHandlerRegistry
{
    private readonly Dictionary<Type, IHandlerAdapter> _handlers = [];

    public ElementHandlerRegistry Register<TElement, TControl>(IElementHandler<TElement, TControl> handler)
        where TElement : Element
        where TControl : class
    {
        _handlers[typeof(TElement)] = new HandlerAdapter<TElement, TControl>(handler);
        return this;
    }

    internal IHandlerAdapter Get(Type elementType) =>
        _handlers.TryGetValue(elementType, out var handler)
            ? handler
            : throw new InvalidOperationException(
                $"No handler is registered for {elementType.Name}. Register one on the renderer's ElementHandlerRegistry.");
}

/// <summary>Erases the handler's generic types so the reconciler can dispatch on element type.</summary>
internal interface IHandlerAdapter
{
    object Mount(ILiveElement live, EchoGuard echo);
    void Update(ILiveElement live, EchoGuard echo, Element oldElement, Element newElement, object control);
    void Unmount(object control);
    bool CanUpdate(Element oldElement, Element newElement);
    IReadOnlyList<Element?> GetChildren(Element element);
    void SyncChildren(object control, IReadOnlyList<object> natives);
    bool IsSlotted { get; }
    void SyncSlots(Element element, object control, IReadOnlyList<IReadOnlyList<object>> slots);
}

internal sealed class HandlerAdapter<TElement, TControl>(IElementHandler<TElement, TControl> handler) : IHandlerAdapter
    where TElement : Element
    where TControl : class
{
    private readonly IChildrenStrategy<TElement, TControl>? _children = handler.Children;

    public object Mount(ILiveElement live, EchoGuard echo) =>
        handler.Mount(new HandlerContext<TElement>(live, echo), (TElement)live.Element);

    public void Update(ILiveElement live, EchoGuard echo, Element oldElement, Element newElement, object control) =>
        handler.Update(new HandlerContext<TElement>(live, echo), (TElement)oldElement, (TElement)newElement, (TControl)control);

    public void Unmount(object control) => handler.Unmount((TControl)control);

    public bool CanUpdate(Element oldElement, Element newElement) =>
        handler.CanUpdate((TElement)oldElement, (TElement)newElement);

    public IReadOnlyList<Element?> GetChildren(Element element) =>
        _children?.GetChildren((TElement)element) ?? [];

    public void SyncChildren(object control, IReadOnlyList<object> natives) =>
        _children?.Sync((TControl)control, natives);

    public bool IsSlotted => _children is ISlottedChildrenStrategy<TElement, TControl>;

    public void SyncSlots(Element element, object control, IReadOnlyList<IReadOnlyList<object>> slots) =>
        ((ISlottedChildrenStrategy<TElement, TControl>)_children!).SyncSlots((TElement)element, (TControl)control, slots);
}
