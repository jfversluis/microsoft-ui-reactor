namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// The renderer's UI thread. The core never touches a dispatcher directly: state
/// written off this thread is posted here, and re-renders are batched into one
/// posted flush.
/// </summary>
public interface IRenderScheduler
{
    /// <summary>Whether the caller is on the render thread.</summary>
    bool CheckAccess();

    /// <summary>Queues <paramref name="work"/> to run on the render thread.</summary>
    void Post(Action work);
}
