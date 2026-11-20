using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Hosting;

internal enum NodeKind
{
    Empty,
    Group,
    Component,
    Func,
    Host,
}

/// <summary>
/// One mounted element. The core keeps this tree itself instead of attaching state to
/// native controls, which is what makes it independent of any UI framework.
/// </summary>
internal sealed class Node(Element element, Node? parent, NodeKind kind) : ILiveElement
{
    public Element Element { get; set; } = element;
    public Node? Parent { get; } = parent;
    public int Depth { get; } = parent is null ? 0 : parent.Depth + 1;
    public NodeKind Kind { get; } = kind;
    public List<Node> Children { get; set; } = [];

    // Host nodes only.
    public IHandlerAdapter? Handler { get; set; }
    public object? Native { get; set; }

    // Component and function nodes only.
    public Component? Component { get; set; }
    public RenderContext? Context { get; set; }

    public bool Dirty { get; set; }
    public bool Unmounted { get; set; }
}
