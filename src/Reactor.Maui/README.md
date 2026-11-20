# Microsoft.UI.Reactor.Maui

Reactor for .NET MAUI: declarative, component-based UI written in C#, with React-style hooks
and keyed reconciliation, rendered to native MAUI views on Android, iOS, Mac Catalyst and Windows.

```csharp
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui;
using static Microsoft.UI.Reactor.Maui.Factories;

public sealed class Counter : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        return VStack(8,
            Label($"Clicked {count} times"),
            Button("+", () => setCount(count + 1)));
    }
}
```

Host it in a page:

```csharp
// MauiProgram.cs
builder.UseMauiApp<App>().UseReactorDirectViews();

// App.cs
protected override Window CreateWindow(IActivationState? activationState) =>
    new(new ReactorPage(Factories.Component<Counter>(), DirectHandlers.CreateRegistry()));
```

`DirectHandlers.CreateRegistry()` renders primitives and layouts as lightweight views that skip
the MAUI Controls layer, and everything else as regular MAUI controls. Pass no registry to use
MAUI controls throughout.

Design notes and status:
[spec 065](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/specs/065-maui-renderer.md).
