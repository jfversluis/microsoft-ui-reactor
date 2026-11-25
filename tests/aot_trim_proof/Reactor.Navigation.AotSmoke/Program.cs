using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Reactor.Navigation;

// Resolves a deep link to a typed route stack, snapshots it as NavigationState, and round-trips the
// snapshot through source-generated JSON, all under Native AOT (spec 069 §2.5).
var map = new DeepLinkMap<Route>()
    .Map("/items/{id:int}", args => new Route.Item(args.Get<int>("id")), () => [new Route.Home()])
    .Map("/", _ => new Route.Home());

var link = map.Resolve("myapp:///items/42?tab=reviews");
Check(link.Matched, "the deep link matched");
Check(link.Routes is [Route.Home, Route.Item { Id: 42 }], $"resolved routes: {string.Join(", ", link.Routes)}");

var state = new NavigationState<Route>(link.Routes[..^1], link.Routes[^1], [new Route.Item(7)]);
var json = JsonSerializer.Serialize(state, SmokeJson.Default.NavigationStateRoute);
var back = JsonSerializer.Deserialize(json, SmokeJson.Default.NavigationStateRoute);
Check(back is not null, "the snapshot deserialized");
Check(back!.Current == state.Current, $"current route: {back.Current}");
Check(back.BackStack.SequenceEqual(state.BackStack), "back stack");
Check(back.ForwardStack.SequenceEqual(state.ForwardStack), "forward stack");
Check(!map.Resolve("/nowhere/at/all").Matched, "an unknown link does not match");

Console.WriteLine($"PASS {json}");
return 0;

static void Check(bool condition, string what)
{
    if (condition) return;
    Console.Error.WriteLine($"FAIL {what}");
    Environment.Exit(1);
}

[JsonPolymorphic]
[JsonDerivedType(typeof(Home), "home")]
[JsonDerivedType(typeof(Item), "item")]
public abstract record Route
{
    public sealed record Home : Route;
    public sealed record Item(int Id) : Route;
}

[JsonSerializable(typeof(NavigationState<Route>))]
internal sealed partial class SmokeJson : JsonSerializerContext;
