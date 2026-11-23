using Microsoft.UI.Reactor.Core;
using System.Collections.Concurrent;
using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Core.Tests;

// A minimal in-memory renderer. If the core can drive this, it does not depend on any UI framework.

public sealed class FakeView(string kind)
{
    public string Kind { get; } = kind;
    public string? Text { get; private set; }
    public List<FakeView> Children { get; } = [];
    public bool Unmounted { get; set; }

    public event Action<string>? TextChanged;

    // Like a real text box, a programmatic write raises the change event too.
    public void SetText(string? text)
    {
        if (Text == text) return;
        Text = text;
        TextChanged?.Invoke(text ?? "");
    }

    public void SimulateTyping(string text) => SetText(text);
}

public sealed record BoxElement(params Element?[] Children) : Element;
/// <summary>Hosts at most one child, like a scroll view.</summary>
public sealed record FrameElement(Element? Child) : Element;
public sealed record TextElement(string Text) : Element;
public sealed record InputElement(string Value, Action<string>? OnChanged = null) : Element;
/// <summary>Its handler throws on mount, after the elements before it in a list mounted.</summary>
public sealed record ExplodingElement(string Message) : Element;

/// <summary>Hands out its handler's <c>ReportError</c>, as a control with its own async work would use it.</summary>
public sealed record ReporterElement(Action<Action<Exception>> Expose) : Element;

public sealed class FakeRenderer
{
    public List<FakeView> Mounted { get; } = [];

    public ElementHandlerRegistry CreateRegistry() => new ElementHandlerRegistry()
        .Register(new BoxHandler(this))
        .Register(new TextHandler(this))
        .Register(new InputHandler(this))
        .Register(new ExplodingHandler())
        .Register(new ReporterHandler(this))
        .Register(new FrameHandler(this));

    private FakeView Create(string kind)
    {
        var view = new FakeView(kind);
        Mounted.Add(view);
        return view;
    }

    private sealed class BoxHandler(FakeRenderer renderer) : IElementHandler<BoxElement, FakeView>
    {
        public FakeView Mount(HandlerContext<BoxElement> context, BoxElement element) => renderer.Create("box");
        public void Update(HandlerContext<BoxElement> context, BoxElement oldElement, BoxElement newElement, FakeView control) { }
        public void Unmount(FakeView control) => control.Unmounted = true;
        public IChildrenStrategy<BoxElement, FakeView> Children { get; } =
            ChildrenStrategy.List<BoxElement, FakeView, FakeView>(e => e.Children, v => v.Children);
    }

    private sealed class TextHandler(FakeRenderer renderer) : IElementHandler<TextElement, FakeView>
    {
        public FakeView Mount(HandlerContext<TextElement> context, TextElement element)
        {
            var view = renderer.Create("text");
            view.SetText(element.Text);
            return view;
        }

        public void Update(HandlerContext<TextElement> context, TextElement oldElement, TextElement newElement, FakeView control)
            => control.SetText(newElement.Text);

        public void Unmount(FakeView control) => control.Unmounted = true;
    }

    private sealed class FrameHandler(FakeRenderer renderer) : IElementHandler<FrameElement, FakeView>
    {
        public FakeView Mount(HandlerContext<FrameElement> context, FrameElement element) => renderer.Create("frame");
        public void Update(HandlerContext<FrameElement> context, FrameElement oldElement, FrameElement newElement, FakeView control) { }
        public void Unmount(FakeView control) => control.Unmounted = true;
        public IChildrenStrategy<FrameElement, FakeView> Children { get; } =
            ChildrenStrategy.Single<FrameElement, FakeView, FakeView>(e => e.Child, (frame, child) =>
            {
                frame.Children.Clear();
                if (child is not null) frame.Children.Add(child);
            });
    }

    private sealed class ExplodingHandler : IElementHandler<ExplodingElement, FakeView>
    {
        public FakeView Mount(HandlerContext<ExplodingElement> context, ExplodingElement element) =>
            throw new InvalidOperationException(element.Message);

        public void Update(HandlerContext<ExplodingElement> context, ExplodingElement oldElement, ExplodingElement newElement, FakeView control) { }
    }

    private sealed class ReporterHandler(FakeRenderer renderer) : IElementHandler<ReporterElement, FakeView>
    {
        public FakeView Mount(HandlerContext<ReporterElement> context, ReporterElement element)
        {
            element.Expose(context.ReportError);
            return renderer.Create("reporter");
        }

        public void Update(HandlerContext<ReporterElement> context, ReporterElement oldElement, ReporterElement newElement, FakeView control) { }
    }

    private sealed class InputHandler(FakeRenderer renderer) : IElementHandler<InputElement, FakeView>
    {
        public FakeView Mount(HandlerContext<InputElement> context, InputElement element)
        {
            var view = renderer.Create("input");
            view.SetText(element.Value);
            view.TextChanged += text =>
            {
                if (context.IsEchoing(view)) return;
                context.Current.OnChanged?.Invoke(text);
                context.RestoreAfterEvent();
            };
            return view;
        }

        // Controlled: the control always ends up showing the element's value.
        public void Update(HandlerContext<InputElement> context, InputElement oldElement, InputElement newElement, FakeView control)
        {
            if (control.Text != newElement.Value)
                context.WriteSuppressed(control, () => control.SetText(newElement.Value));
        }

        public void Unmount(FakeView control) => control.Unmounted = true;
    }
}

/// <summary>A render thread with an explicit queue, so tests decide when posted work runs.</summary>
public sealed class ManualScheduler : IRenderScheduler
{
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<Action> _queue = new();

    public int PendingCount => _queue.Count;

    public bool CheckAccess() => Environment.CurrentManagedThreadId == _threadId;

    public void Post(Action work) => _queue.Enqueue(work);

    public void Drain()
    {
        while (_queue.TryDequeue(out var work)) work();
    }
}

public sealed class TestHost
{
    public FakeRenderer Renderer { get; } = new();
    public ManualScheduler Scheduler { get; } = new();
    public ReactorRoot Root { get; }
    public FakeView? RootView { get; private set; }

    public TestHost()
    {
        Root = new ReactorRoot(Renderer.CreateRegistry(), Scheduler, native => RootView = (FakeView?)native);
    }

    public TestHost Render(Element element)
    {
        Root.Render(element);
        return this;
    }
}
