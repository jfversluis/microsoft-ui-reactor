namespace Microsoft.UI.Reactor.Core;

/// <summary>The progress of an asynchronous fetch started by <c>UseResource</c>.</summary>
/// <param name="IsLoading">Whether a fetch is in flight. <paramref name="Value"/> keeps the previous result meanwhile.</param>
/// <param name="Value">The latest successful result, or <c>default</c> before the first one.</param>
/// <param name="Error">The exception from the latest fetch, if it failed.</param>
/// <param name="Reload">Starts the fetch again.</param>
public sealed record Resource<T>(bool IsLoading, T? Value, Exception? Error, Action Reload);
