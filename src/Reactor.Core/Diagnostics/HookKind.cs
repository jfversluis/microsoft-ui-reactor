namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>Which hook produced a hook cell. Internal: the public surface reports it as text.</summary>
internal enum HookKind
{
    Unknown,
    State,
    Reducer,
    Ref,
    Memo,
    Effect,
    Context,
    Persisted,
    NavigationLifecycle,
}
