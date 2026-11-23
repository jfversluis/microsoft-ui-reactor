# Microsoft.UI.Reactor.Maui.Core

> Transitional. Spec 067 replaces this package with `Microsoft.UI.Reactor.Core`, the engine extracted
> from WinUI Reactor and shared by the WinUI and MAUI renderers.

The renderer-neutral core of Reactor: immutable element records, components with React-style
hooks (`UseState`, `UseReducer`, `UseEffect`, `UseMemo`, `UseCallback`, `UseRef`), keyed
reconciliation, and batched re-rendering.

It has no UI framework dependency. A renderer supplies one handler per element type and a
scheduler for its UI thread; the core owns component state, identity, ordering and lifetime.

You normally consume this package through a renderer, such as
[`Microsoft.UI.Reactor.Maui`](https://www.nuget.org/packages/Microsoft.UI.Reactor.Maui) for .NET MAUI.
Design notes: [spec 065](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/specs/065-maui-renderer.md).
