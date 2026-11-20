using System.Runtime.CompilerServices;
using Microsoft.Maui.Dispatching;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>
/// A synchronous MAUI dispatcher for headless tests. A few Controls types (TabbedPage) require a
/// dispatcher when they are constructed; nothing in these tests depends on dispatch timing.
/// </summary>
internal sealed class TestDispatcherProvider : IDispatcherProvider
{
    private static readonly IDispatcher Instance = new SynchronousDispatcher();

    public IDispatcher GetForCurrentThread() => Instance;

    [ModuleInitializer]
    internal static void Install() => DispatcherProvider.SetCurrent(new TestDispatcherProvider());

    private sealed class SynchronousDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;

        public bool Dispatch(Action action)
        {
            action();
            return true;
        }

        public bool DispatchDelayed(TimeSpan delay, Action action)
        {
            action();
            return true;
        }

        public IDispatcherTimer CreateTimer() => throw new NotSupportedException("Timers are not available in headless tests.");
    }
}
