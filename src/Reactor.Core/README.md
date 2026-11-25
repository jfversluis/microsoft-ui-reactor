# Microsoft.UI.Reactor.Core

The framework-neutral core of Reactor (spec 067), shared by the WinUI renderer
(`Microsoft.UI.Reactor`) and the .NET MAUI renderer (`Microsoft.UI.Reactor.Maui`). It references no
UI framework. It holds:

- element identity (`ReactorElement`, which carries `Key`) and the keyed child-reconciliation engine;
- components (`ReactorComponent`) and the hook table (`RenderContext`) with the framework-neutral
  hooks (`UseState`, `UseEffect`, `UseMemo`, `UseContext`, `UsePersisted`, …), plus `Context<T>`,
  `Ref<T>` and the persisted-state scopes;
- the component runtime every renderer shares: whether a component re-renders when its parent does
  (`ShouldUpdate`, memo dependencies, consumed context), and how one render runs and fails (effects,
  the hot-reload hook-order retry, error boundaries versus the in-tree fallback);
- the `Microsoft-UI-Reactor` ETW event source.

Each renderer keeps its own tree walk (spec 067 §11.1); the handler protocol moves here as both
renderers adopt it (spec 067 §12).

You don't reference this package directly; a renderer brings it in.
