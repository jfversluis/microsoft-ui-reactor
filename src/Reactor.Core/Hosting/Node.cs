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
/// One mounted element. The core keeps this tree itself instead of attaching state to
/// native controls, which is what makes it independent of any UI framework.
/// </summary>
internal sealed class Node(Element element, Node? parent, NodeKind kind, ReactorRoot root) : ILiveElement
{
    public Element Element { get; set; } = element;
    public Node? Parent { get; } = parent;
    public ReactorRoot Root { get; } = root;
    public int Depth { get; } = parent is null ? 0 : parent.Depth + 1;
    public NodeKind Kind { get; } = kind;
    public List<Node> Children { get; set; } = [];

    // Host nodes only.
    public IHandlerAdapter? Handler { get; set; }
    public object? Native { get; set; }

    // Component, function and memo nodes only.
    public Component? Component { get; set; }
    public RenderContext? Context { get; set; }

    // Provider nodes: the components that read this provider's value.
    public HashSet<Node>? Subscribers { get; set; }

    // Component nodes: the providers this component reads, so it can unsubscribe on unmount.
    public HashSet<Node>? Subscriptions { get; set; }

    // Boundary nodes: the error currently shown through the fallback, if any.
    public Exception? Error { get; set; }

    public bool Dirty { get; set; }
    public bool Unmounted { get; set; }

    public bool IsRenderable => Kind is NodeKind.Component or NodeKind.Func or NodeKind.Memo;

    /// <summary>Reads the nearest enclosing provider of <paramref name="context"/> and subscribes to it.</summary>
    public T ReadContext<T>(Context<T> context)
    {
        for (var ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.Kind != NodeKind.Provider) continue;
            var provider = (ContextProviderElement)ancestor.Element;
            if (!ReferenceEquals(provider.Context, context)) continue;

            (ancestor.Subscribers ??= []).Add(this);
            (Subscriptions ??= []).Add(ancestor);
            return (T)provider.Value!;
        }
        return context.DefaultValue;
    }
}
