[assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(Microsoft.UI.Reactor.Hosting.HotReload))]

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Re-renders every live tree after a .NET Hot Reload edit, so changed <c>Render</c> methods show
/// immediately. Component state is kept: the components are the same instances.
/// </summary>
internal static class HotReload
{
    internal static void UpdateApplication(Type[]? updatedTypes) => ReactorRoot.RerenderAll();
}
