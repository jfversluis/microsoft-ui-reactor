# Microsoft.UI.Reactor.Core

The framework-neutral core of Reactor (spec 067), shared by the WinUI renderer
(`Microsoft.UI.Reactor`) and, as spec 067 lands, the .NET MAUI renderer. It references no UI
framework. It holds:

- element identity (`ReactorElement`, which carries `Key`) and the keyed child-reconciliation engine;
- components (`ReactorComponent`) and the hook table (`RenderContext`) with the framework-neutral
  hooks (`UseState`, `UseEffect`, `UseMemo`, `UseContext`, `UsePersisted`, …), plus `Context<T>`,
  `Ref<T>` and the persisted-state scopes;
- the `Microsoft-UI-Reactor` ETW event source.

The rest of the engine moves here in later stages of spec 067.

You don't reference this package directly; a renderer brings it in.
