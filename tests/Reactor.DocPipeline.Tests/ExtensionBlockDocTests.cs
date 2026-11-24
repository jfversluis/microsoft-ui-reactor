using Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Spec 067 moved WinUI's hooks into C# 14 extension blocks on the core's RenderContext. The
/// compiler documents those members on a generated grouping type; the reference pages must keep
/// them at their RenderContext crefs.
/// </summary>
public class ExtensionBlockDocTests
{
    // The shape the C# 14 compiler writes for `extension(RenderContext ctx) { … }` in WinUIHooks.
    private const string CompilerOutput = """
        <?xml version="1.0"?>
        <doc>
          <assembly><name>Reactor</name></assembly>
          <members>
            <member name="M:Microsoft.UI.Reactor.Core.WinUIHooks.UseWindow(Microsoft.UI.Reactor.Core.RenderContext)">
              <inheritdoc cref="M:Microsoft.UI.Reactor.Core.WinUIHooks.&lt;G&gt;$4F0A.UseWindow"/>
            </member>
            <member name="M:Microsoft.UI.Reactor.Core.WinUIHooks.UseDpi(Microsoft.UI.Reactor.Core.RenderContext)">
              <inheritdoc cref="M:Microsoft.UI.Reactor.Core.WinUIHooks.&lt;G&gt;$4F0A.UseDpi"/>
            </member>
            <member name="M:Microsoft.UI.Reactor.Core.WinUIHooks.&lt;G&gt;$4F0A.UseWindow">
              <summary>The owning window. See <see cref="M:Microsoft.UI.Reactor.Core.WinUIHooks.UseDpi(Microsoft.UI.Reactor.Core.RenderContext)"/>.</summary>
            </member>
            <member name="M:Microsoft.UI.Reactor.Core.WinUIHooks.&lt;G&gt;$4F0A.UseDpi">
              <summary>The window's DPI.</summary>
            </member>
            <member name="M:Microsoft.UI.Reactor.Hooks.Plain.UseThing(Microsoft.UI.Reactor.Core.RenderContext)">
              <summary>A classic extension method.</summary>
            </member>
          </members>
        </doc>
        """;

    [Fact]
    public void ExtensionBlockMembers_AreDocumentedOnTheirReceiver()
    {
        var members = XmlDocReader.ReadString(CompilerOutput);

        Assert.Equal(
            new[]
            {
                "M:Microsoft.UI.Reactor.Core.RenderContext.UseDpi",
                "M:Microsoft.UI.Reactor.Core.RenderContext.UseWindow",
                "M:Microsoft.UI.Reactor.Hooks.Plain.UseThing(Microsoft.UI.Reactor.Core.RenderContext)",
            },
            members.Select(m => m.Cref).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void CrefToASiblingStub_PointsAtTheReceiverMember()
    {
        var useWindow = XmlDocReader.ReadString(CompilerOutput).Single(m => m.Cref.EndsWith(".UseWindow", StringComparison.Ordinal));

        Assert.Contains("M:Microsoft.UI.Reactor.Core.RenderContext.UseDpi", useWindow.Summary);
        Assert.DoesNotContain("WinUIHooks", useWindow.Summary);
    }
}
