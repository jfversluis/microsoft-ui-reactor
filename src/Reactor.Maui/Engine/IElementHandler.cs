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

    /// <summary>
    /// Whether <paramref name="newElement"/> can update the control mounted for <paramref name="oldElement"/>.
    /// Return <c>false</c> when the change needs a different kind of control; the reconciler then
    /// unmounts it and mounts a new one. Default: always <c>true</c>.
    /// </summary>
    bool CanUpdate(TElement oldElement, TElement newElement) => true;
}

/// <summary>
/// Passed to a handler so it can wire native events once at mount and still reach the element
/// that is current when the event fires. In an event handler, invoke callbacks through
/// <see cref="Live"/>; for a change event, return first when <see cref="IsEchoing"/> is true, and
/// call <see cref="RestoreAfterEvent"/> after reporting.
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
    /// The element this control last rendered, even after it has been unmounted. To invoke a
    /// callback from an event handler use <see cref="Live"/>, which is <see langword="null"/> once the
    /// element is unmounted. Read <see cref="Current"/> in <c>Update</c>-time work, not to invoke callbacks.
    /// </summary>
    public TElement Current => (TElement)_live.Element;

    /// <summary>
    /// <see cref="Current"/> while the element is mounted, and <see langword="null"/> once it has
    /// been unmounted. Invoke callbacks through it in event handlers
    /// (<c>context.Live?.OnClick?.Invoke()</c>): a control can still raise events after Reactor
    /// unmounts it (the platform tears it down, or something else holds it), and an unmounted
    /// component's callbacks must not run.
    /// </summary>
    public TElement? Live => IsMounted ? Current : null;

    /// <summary>Whether the element is still mounted. False once Reactor has unmounted it.</summary>
    public bool IsMounted => !((Node)_live).Unmounted;

    /// <summary>
    /// Writes a value to <paramref name="control"/> without the change event that write raises
    /// reaching the element's callback. Check <see cref="IsEchoing"/> in the event handler. The
    /// control must raise that event inside the write: an event raised later is reported.
    /// </summary>
    public void WriteSuppressed(object control, Action write) => _echo.WriteSuppressed(control, write);

    /// <summary>
    /// Whether a change event on <paramref name="control"/> must not be reported: it is raised
    /// inside a <see cref="WriteSuppressed"/> write, or the element has been unmounted. The second
    /// case also covers a handler that then reads <see cref="Current"/> instead of <see cref="Live"/>.
    /// </summary>
    public bool IsEchoing(object control) => !IsMounted || _echo.IsSuppressed(control);

    /// <summary>
    /// Creates a separate tree that renders through the same renderer and inherits this element's
    /// context. Errors it does not catch go to the error boundaries above this element. Use it for
    /// content a control creates on its own schedule, such as the cells of a virtualized list.
    /// Unmount it when the control no longer shows it.
    /// </summary>
    public ReactorRoot CreateChildRoot(Action<object?> setRootNative)
    {
        var node = (Node)_live;
        return node.Root.CreateChildRoot(node, setRootNative);
    }

    /// <summary>
    /// Reports a failure in work the control runs on its own schedule, such as an asynchronous
    /// platform operation it started. The error goes to the error boundaries above this element, or
    /// to the root's <c>UnhandledException</c>, as a render error would. Callable from any thread;
    /// ignored once the element has been unmounted.
    /// </summary>
    public void ReportError(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var node = (Node)_live;
        node.Root.ReportHandlerError(node, error);
    }

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
