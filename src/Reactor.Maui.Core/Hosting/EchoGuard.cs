namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Tracks controls that are being written by the reconciler, so the change event a
/// programmatic write raises is not mistaken for user input.
/// </summary>
public sealed class EchoGuard
{
    private readonly HashSet<object> _writing = new(ReferenceEqualityComparer.Instance);

    public void WriteSuppressed(object control, Action write)
    {
        // A nested write to the same control stays suppressed until the outer one ends.
        var added = _writing.Add(control);
        try
        {
            write();
        }
        finally
        {
            if (added) _writing.Remove(control);
        }
    }

    public bool IsSuppressed(object control) => _writing.Contains(control);
}
