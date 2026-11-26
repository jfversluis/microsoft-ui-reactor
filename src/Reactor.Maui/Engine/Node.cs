using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Hosting;

internal enum NodeKind
{
    Empty,
    Group,
    Component,
    Func,
    Memo,
    Provider,
    Boundary,
    Host,
}

/// <summary>
/// One mounted element. The MAUI renderer keeps this tree itself instead of attaching state to
/// native controls, and gives components no native anchor (spec 065, spec 067 §11.1). A component
/// node's state is the shared core's <see cref="ComponentNode"/>, so the core's skip gate and render
/// kernel run on it (spec 067 §13).
/// </summary>
internal sealed class Node : ComponentNode, ILiveElement
{
    public Node(Element element, Node? parent, NodeKind kind, ReactorRoot root)
    {
        base.Element = element;
        Parent = parent;
        Kind = kind;
        Root = root;
        Depth = parent is null ? 0 : parent.Depth + 1;
        // Fixed for the node's lifetime: its ancestors never change (a child root's ContextParent
        // is set before it mounts anything).
        var above = parent ?? root.ContextParent;
        NearestProvider = above is null ? null : above.Kind == NodeKind.Provider ? above : above.NearestProvider;
    }

    /// <summary>The element this node currently renders.</summary>
    public new Element Element { get => (Element)base.Element!; set => base.Element = value; }

    /// <summary>The class component instance (component nodes only).</summary>
    public new Component? Component { get => (Component?)base.Component; set => base.Component = value; }

    public Node? Parent { get; }
    public ReactorRoot Root { get; }
    public int Depth { get; }
    public NodeKind Kind { get; }
    public List<Node> Children { get; set; } = [];

    /// <summary>The child elements the last reconcile mounted <see cref="Children"/> for, nulls and empties included.</summary>
    public Element[] ChildElements { get; set; } = [];

    /// <summary><see cref="Children"/> as the core's child diff edits it.</summary>
    public IChildList<Node> ChildList => _childList ??= new NodeChildList(this);
    private NodeChildList? _childList;

    // Host nodes only.
    public IHandlerAdapter? Handler { get; set; }
    public object? Native { get; set; }

    /// <summary>The ref cell this host node bound, released when it unmounts (spec 070 §2.4).</summary>
    public ElementRefCell? BoundRef { get; set; }

    // Component, function and memo nodes: the callback their hooks' setters call. Created once.
    public Action? Rerender { get; set; }

    /// <summary>The hook storage of a component, function or memo node.</summary>
    public RenderContext? HookContext => base.Component?.Context ?? Context;

    // Provider nodes: the value as the core's context scope takes it, and the components that read it.
    public Dictionary<ContextBase, object?>? Provided { get; set; }
    public HashSet<Node>? Subscribers { get; set; }

    // Component nodes: the providers this component reads, so it can unsubscribe on unmount.
    public HashSet<Node>? Subscriptions { get; set; }

    // Boundary nodes: the error currently shown through the fallback, if any.
    public Exception? Error { get; set; }

    public bool Dirty { get; set; }
    public bool Unmounted { get; set; }

    public bool IsRenderable => Kind is NodeKind.Component or NodeKind.Func or NodeKind.Memo;

    /// <summary>
    /// The nearest provider above this node; a child root's tree continues at the element that
    /// created it. Each provider links to the next one up, so context lookups walk providers only.
    /// </summary>
    public Node? NearestProvider { get; }

    private sealed class NodeChildList(Node parent) : IChildList<Node>
    {
        public int Count => parent.Children.Count;
        public Node Get(int index) => parent.Children[index];
        public void Insert(int index, Node element) => parent.Children.Insert(index, element);
        public void RemoveAt(int index) => parent.Children.RemoveAt(index);
        public void Replace(int index, Node element) => parent.Children[index] = element;

        public void Move(int oldIndex, int newIndex)
        {
            var node = parent.Children[oldIndex];
            parent.Children.RemoveAt(oldIndex);
            parent.Children.Insert(newIndex, node);
        }
    }
}
