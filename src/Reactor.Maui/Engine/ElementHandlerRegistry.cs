using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// The element-type to handler map a renderer hands to a <see cref="ReactorRoot"/>. A later
/// registration for the same type replaces the earlier one, so an app can override a built-in
/// handler. An element type with no registration of its own uses the handler registered for its
/// nearest base type with <see cref="RegisterForDerivedTypes"/>. The lookup is the shared core's
/// (spec 067 §15), the one WinUI's <c>ControlRegistry</c> uses.
/// </summary>
public sealed class ElementHandlerRegistry
{
    private readonly HandlerTable<IHandlerAdapter> _handlers = new();

    /// <summary>Registers <paramref name="handler"/> for exactly <typeparamref name="TElement"/>, replacing any earlier one.</summary>
    public ElementHandlerRegistry Register<TElement, TControl>(IElementHandler<TElement, TControl> handler)
        where TElement : Element
        where TControl : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers.Set(typeof(TElement), new HandlerAdapter<TElement, TControl>(handler));
        return this;
    }

    /// <summary>
    /// Registers <paramref name="handler"/> for every element type derived from
    /// <typeparamref name="TBase"/> that has no registration of its own, replacing any earlier one
    /// for <typeparamref name="TBase"/>'s derived types. Use it for a family of element records that
    /// one handler renders, such as the closed types <c>Chip&lt;int&gt;</c> and
    /// <c>Chip&lt;string&gt;</c> of a generic record that derives from a non-generic base
    /// (<c>abstract record ChipBase : Element</c>): register the handler for that base.
    /// </summary>
    /// <remarks>
    /// <typeparamref name="TBase"/> itself is not covered, only the types derived from it. An exact
    /// <see cref="Register{TElement, TControl}"/> wins over this, and the nearest registered base type
    /// wins over a more distant one.
    /// </remarks>
    public ElementHandlerRegistry RegisterForDerivedTypes<TBase, TControl>(IElementHandler<TBase, TControl> handler)
        where TBase : Element
        where TControl : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers.Set(typeof(TBase), new HandlerAdapter<TBase, TControl>(handler), forDerivedTypes: true);
        return this;
    }

    internal IHandlerAdapter Get(Type elementType) =>
        _handlers.TryResolve(elementType, out var handler)
            ? handler
            : throw new InvalidOperationException(
                $"No handler is registered for {elementType}. Register one on the renderer's ElementHandlerRegistry"
                + (elementType.BaseType is { } baseType && baseType != typeof(Element)
                    ? $", or one for the types derived from {baseType} with RegisterForDerivedTypes."
                    : "."));
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
