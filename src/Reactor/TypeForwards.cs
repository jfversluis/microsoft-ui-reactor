using System.Runtime.CompilerServices;

// Types that moved from Reactor.dll into the framework-neutral Microsoft.UI.Reactor.Core.dll
// (spec 067). Same namespaces, so source compiles unchanged; the forwards keep binaries built
// against an earlier Reactor.dll (a component library, a plug-in) resolving them too.
[assembly: TypeForwardedTo(typeof(Microsoft.UI.Reactor.Core.ApplicationPersistedScope))]
[assembly: TypeForwardedTo(typeof(Microsoft.UI.Reactor.Core.Context<>))]
[assembly: TypeForwardedTo(typeof(Microsoft.UI.Reactor.Core.ContextBase))]
[assembly: TypeForwardedTo(typeof(Microsoft.UI.Reactor.Core.HookOrderException))]
[assembly: TypeForwardedTo(typeof(Microsoft.UI.Reactor.Core.IPersistedStateScope))]
[assembly: TypeForwardedTo(typeof(Microsoft.UI.Reactor.Core.PersistedScope))]
[assembly: TypeForwardedTo(typeof(Microsoft.UI.Reactor.Core.Ref<>))]
[assembly: TypeForwardedTo(typeof(Microsoft.UI.Reactor.Core.RenderContext))]
[assembly: TypeForwardedTo(typeof(Microsoft.UI.Reactor.Core.WindowPersistedScope))]
