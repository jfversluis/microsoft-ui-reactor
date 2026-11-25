using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>Runs posted work only when the test drains it, on the test's own thread.</summary>
public sealed class TestScheduler : IRenderScheduler
{
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly Queue<Action> _queue = new();

    public bool CheckAccess() => Environment.CurrentManagedThreadId == _threadId;

    public void Post(Action work) => _queue.Enqueue(work);

    /// <summary>Runs only the work posted first, if any.</summary>
    public void RunOne()
    {
        if (_queue.TryDequeue(out var work)) work();
    }

    public void Drain()
    {
        while (_queue.TryDequeue(out var work)) work();
    }
}

/// <summary>Mounts elements headlessly through a renderer registry and exposes the root native view.</summary>
public sealed class TestHost
{
    public TestScheduler Scheduler { get; } = new();
    public ReactorRoot Root { get; }
    public object? RootView { get; private set; }

    public TestHost(ElementHandlerRegistry registry)
    {
        Root = new ReactorRoot(registry, Scheduler, native =>
        {
            RootView = native;
            RootNativeChanged?.Invoke(native);
        });
    }

    /// <summary>Called when the root's native view changes, as a window's page setter would be.</summary>
    public Action<object?>? RootNativeChanged { get; set; }

    public TestHost Render(Element element)
    {
        Root.Render(element);
        return this;
    }

    public T View<T>() => Assert.IsType<T>(RootView, exactMatch: false);
}
