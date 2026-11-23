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
    /// The renderer's element base derives from the neutral one, so the engine can diff WinUI
    /// elements; a regression here means the engine is no longer what WinUI runs on.
    /// </summary>
    [Fact]
    public void WinUIElement_DerivesFromTheNeutralElement()
    {
        Assert.True(typeof(ReactorElement).IsAssignableFrom(typeof(Element)));
        Assert.NotEqual(typeof(Element).Assembly, typeof(ReactorElement).Assembly);
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
