using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Tests.Tooling;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Architecture;

/// <summary>
/// Spec 067: <c>Microsoft.UI.Reactor.Core</c> is the framework-neutral engine shared by the WinUI
/// and MAUI renderers. These guard the two properties the split depends on.
/// </summary>
public class NeutralCoreBoundaryTests
{
    /// <summary>
    /// The core may reference only the .NET runtime: a UI framework reference (WinUI, Windows SDK
    /// projections, MAUI) would make it unusable by the other renderer.
    /// </summary>
    [Fact]
    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Test-only: reads the core's on-disk Location to feed PEReader. IL3000 only affects single-file publish; this host is not single-file-published.")]
    public void NeutralCore_ReferencesOnlyTheRuntime()
    {
        var path = typeof(ReactorElement).Assembly.Location;
        Assert.False(string.IsNullOrEmpty(path));

        using var stream = global::System.IO.File.OpenRead(path);
        using var pe = new global::System.Reflection.PortableExecutable.PEReader(stream);
        var reader = global::System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);

        Assert.Equal("Microsoft.UI.Reactor.Core", reader.GetString(reader.GetAssemblyDefinition().Name));

        var references = reader.AssemblyReferences
            .Select(h => reader.GetString(reader.GetAssemblyReference(h).Name))
            .ToList();

        Assert.NotEmpty(references);
        Assert.All(references, name => Assert.True(
            name.StartsWith("System.", global::System.StringComparison.Ordinal) || name == "netstandard",
            $"Microsoft.UI.Reactor.Core references '{name}'; the neutral core may reference only System.* (spec 067)."));
    }

    /// <summary>
    /// Every public type that moved from Reactor.dll to the core is type-forwarded, so a binary
    /// compiled against an earlier Reactor.dll still finds it. Types created in the core never
    /// lived in Reactor.dll and need no forward; anything else the core exports must be forwarded.
    /// </summary>
    [Fact]
    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only: enumerates the two assemblies' exported and forwarded types by reflection. Standard `dotnet test` is JIT (never trimmed). Behaviour-neutral.")]
    public void ReactorDll_ForwardsEveryPublicTypeThatMovedToTheCore()
    {
        var bornInCore = new[] { typeof(ReactorElement), typeof(ReactorComponent) };
        var moved = typeof(ReactorElement).Assembly.GetExportedTypes()
            .Where(t => !t.IsNested)
            .Except(bornInCore)
            .OrderBy(t => t.FullName, global::System.StringComparer.Ordinal)
            .ToArray();
        var forwarded = typeof(Element).Assembly.GetForwardedTypes()
            .OrderBy(t => t.FullName, global::System.StringComparer.Ordinal)
            .ToArray();

        Assert.Contains(typeof(RenderContext), moved);
        Assert.Equal(moved, forwarded);

        // A reference compiled before the move names Reactor.dll; the forward must resolve it.
        Assert.Equal("Reactor", typeof(Element).Assembly.GetName().Name);
        Assert.Same(typeof(RenderContext), global::System.Type.GetType("Microsoft.UI.Reactor.Core.RenderContext, Reactor"));
    }

    /// <summary>
    /// The renderer's element and component bases derive from the neutral ones, so the core can
    /// diff WinUI elements and run WinUI components; a regression here means the core is no longer
    /// what WinUI runs on.
    /// </summary>
    [Fact]
    public void WinUIBases_DeriveFromTheNeutralOnes()
    {
        Assert.True(typeof(ReactorElement).IsAssignableFrom(typeof(Element)));
        Assert.NotEqual(typeof(Element).Assembly, typeof(ReactorElement).Assembly);
        Assert.True(typeof(ReactorComponent).IsAssignableFrom(typeof(Component)));
        Assert.NotEqual(typeof(Component).Assembly, typeof(ReactorComponent).Assembly);
        Assert.Same(typeof(ReactorComponent).Assembly, typeof(RenderContext).Assembly);
    }

    /// <summary>
    /// Engine internals used to live in Reactor.dll, so every assembly with access to Reactor's
    /// internals (tests, hosts, companions) relied on them. The core must grant each of those
    /// access too, or moving an internal type into it breaks them, and grant nobody else.
    /// </summary>
    [Fact]
    public void NeutralCore_GrantsInternalsToEveryAssemblyReactorDoes()
    {
        static string[] Grants(string csproj) =>
            Regex.Matches(global::System.IO.File.ReadAllText(global::System.IO.Path.Join(GallerySources.RepoRoot(), csproj)),
                    "<InternalsVisibleTo Include=\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value)
                .ToArray();

        var reactor = Grants("src/Reactor/Reactor.csproj");
        var core = Grants("src/Reactor.Core/Reactor.Core.csproj");

        Assert.NotEmpty(reactor);
        // Exactly Reactor's grants plus Reactor itself: a missing one breaks an assembly that
        // used engine internals, and an extra one widens the core's internals unreviewed.
        Assert.Equal(reactor.Append("Reactor").Order(global::System.StringComparer.Ordinal), core.Order(global::System.StringComparer.Ordinal));
    }
}
