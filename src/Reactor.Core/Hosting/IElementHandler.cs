using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Maps one element type to one native control type. This is the whole contract a
/// renderer implements per control; the core owns identity, ordering and lifetime.
/// </summary>
public interface IElementHandler<TElement, TControl>
    where TElement : Element
    where TControl : class
{
    /// <summary>Creates the native control and applies the element's initial values.</summary>
    TControl Mount(HandlerContext<TElement> context, TElement element);

    /// <summary>Applies the differences between <paramref name="oldElement"/> and <paramref name="newElement"/>.</summary>
    void Update(HandlerContext<TElement> context, TElement oldElement, TElement newElement, TControl control);

    /// <summary>Releases anything <see cref="Mount"/> acquired. Default: nothing.</summary>
    void Unmount(TControl control) { }

    /// <summary>How this control hosts child elements, or <c>null</c> for a leaf.</summary>
    IChildrenStrategy<TElement, TControl>? Children => null;
}

/// <summary>
/// Passed to a handler so it can wire native events once at mount and still reach
/// the element that is current when the event fires.
/// </summary>
public readonly struct HandlerContext<TElement> where TElement : Element
{
    private readonly ILiveElement _live;
    private readonly EchoGuard _echo;

    internal HandlerContext(ILiveElement live, EchoGuard echo)
    {
        _live = live;
        _echo = echo;
    }

    /// <summary>
    /// The element this control currently renders. Read it inside event handlers
    /// rather than capturing the element passed to <c>Mount</c>, which goes stale.
    /// </summary>
    public TElement Current => (TElement)_live.Element;

    /// <summary>
    /// Writes a value to <paramref name="control"/> without the change event that
    /// write raises reaching the element's callback. Check <see cref="IsEchoing"/>
    /// in the event handler.
    /// </summary>
    public void WriteSuppressed(object control, Action write) => _echo.WriteSuppressed(control, write);

    /// <summary>Whether <paramref name="control"/> is inside a <see cref="WriteSuppressed"/> write.</summary>
    public bool IsEchoing(object control) => _echo.IsSuppressed(control);

    /// <summary>
    /// Call after reporting user input: once state has settled, the control is updated with the
    /// element that is current then. A controlled control whose callback did not accept the input
    /// (did not change state) therefore returns to its declared value.
    /// </summary>
    public void RestoreAfterEvent()
    {
        var node = (Node)_live;
        node.Root.RequestRestore(node);
    }
}

internal interface ILiveElement
{
    Element Element { get; }
}
