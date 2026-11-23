# Microsoft.UI.Reactor.Core

The framework-neutral core of Reactor (spec 067), shared by the WinUI renderer
(`Microsoft.UI.Reactor`) and, as spec 067 lands, the .NET MAUI renderer. It references no UI
framework. It holds element identity (`ReactorElement`, which carries `Key`) and the keyed
child-reconciliation engine; components, hooks and the rest of the engine move here in later
stages of spec 067.

You don't reference this package directly; a renderer brings it in.
