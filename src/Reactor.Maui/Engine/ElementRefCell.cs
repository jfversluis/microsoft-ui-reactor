namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// The engine's side of an element ref (spec 070 §2.4): the native the ref names, the node that
/// bound it, and the type the ref expects. Each node records the cell it bound
/// (<see cref="Node.BoundRef"/>) and releases it on unmount, so the binding never depends on the
/// element the node holds at the time.
/// </summary>
internal sealed class ElementRefCell(Type expectedType)
{
    public Type ExpectedType { get; } = expectedType;
    public object? Current { get; private set; }
    public Node? Owner { get; private set; }

    public void Bind(Node owner, object native)
    {
        Current = native;
        Owner = owner;
    }

    /// <summary>Clears the cell if <paramref name="owner"/> still owns it; a newer binding is kept.</summary>
    public void Release(Node owner)
    {
        if (!ReferenceEquals(Owner, owner)) return;
        Current = null;
        Owner = null;
    }
}

/// <summary>An element that can carry a ref. <c>ViewElement</c> implements it; tests' fake elements can too.</summary>
internal interface IRefTarget
{
    ElementRefCell? RefCell { get; }
}
