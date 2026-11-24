namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// The state of a mounted component (spec 067 §11.2): its instance or render context, the element
/// that created it, what it last rendered, and the baselines its skip gate compares against. Where
/// a renderer keeps the node (WinUI: keyed by the component's anchor) is the renderer's business.
/// </summary>
internal class ComponentNode
{
    /// <summary>The class-based component instance (null for function components).</summary>
    public ReactorComponent? Component { get; set; }
    /// <summary>The RenderContext for function components (null for class components).</summary>
    public RenderContext? Context { get; set; }
    /// <summary>The element tree output from the last Render() call.</summary>
    public ReactorElement? RenderedElement { get; set; }
    /// <summary>The component, function or memo element that created this node.</summary>
    public ReactorElement? Element { get; set; }
    /// <summary>Previous props for memo comparison (class components only).</summary>
    public object? PreviousProps { get; set; }
    /// <summary>Dependencies from the last memo render (null = render once).</summary>
    public object?[]? MemoDependencies { get; set; }
    /// <summary>Set to true when a self-triggered re-render is queued (UseState setter).
    /// Accessed from background threads (UseState callbacks) — use volatile field.</summary>
    private volatile bool _selfTriggered;
    public bool SelfTriggered { get => _selfTriggered; set => _selfTriggered = value; }
    /// <summary>
    /// <c>componentId</c> of this node in <c>ReactorEventSource.ComponentRendered</c> and the
    /// <c>Microsoft-UI-Reactor-Perf</c> events; 0 until first assigned
    /// (<see cref="EnsureDiagnosticId"/>). Lives on the node's <see cref="RenderContext"/>,
    /// so a state change the component makes reports the same id.
    /// </summary>
    public long DiagnosticId => (Component?.Context ?? Context)?.DiagnosticId ?? 0;

    /// <summary><see cref="DiagnosticId"/>, assigning it on first use.</summary>
    public long EnsureDiagnosticId()
        => (Component?.Context ?? Context)?.EnsureDiagnosticId() ?? 0;

    /// <summary>
    /// A function or memo component's name on the perf spans, resolved on its first traced
    /// render; null until then and for class components.
    /// </summary>
    public string? PerfName { get; set; }
}
