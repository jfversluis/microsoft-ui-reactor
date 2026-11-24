using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Spec 067 moved the shared hook shortcuts (<c>UseState</c>, <c>UseEffect</c>, <c>UsePersisted</c>,
/// …) from <c>Component</c> to its framework-neutral base <c>ReactorComponent</c>, so an unqualified
/// hook call in a class component now binds to a method declared on <c>ReactorComponent</c>. The other
/// analyzer suites stub the hooks directly on <c>Component</c>; these stubs mirror the real hierarchy,
/// so an analyzer that anchors only on <c>Component</c> as the declaring type goes silent here.
/// </summary>
public class ReactorComponentHookAnchorTests
{
    private const string Stubs = @"
using System;
using System.Threading.Tasks;

namespace Microsoft.UI.Reactor.Core
{
    public enum PersistedScope { Window, Application }

    public class RenderContext
    {
        public void UseEffect(Action effect, params object[] dependencies) { }
        public void UseEffect(Func<Action> effectWithCleanup, params object[] dependencies) { }
        public void UsePersisted<T>(string key, T initialValue) { }
        public void UsePersisted<T>(string key, T initialValue, PersistedScope scope) { }
    }

    public abstract class ReactorComponent
    {
        internal RenderContext Context { get; } = new RenderContext();
        public abstract string Render();
        protected (int, Action<int>) UseState(int initial) => (0, _ => { });
        protected void UseEffect(Action effect, params object[] dependencies) { }
        protected void UseEffect(Func<Action> effectWithCleanup, params object[] dependencies) { }
        protected void UsePersisted<T>(string key, T initialValue) { }
        protected void UsePersisted<T>(string key, T initialValue, PersistedScope scope) { }
    }

    public abstract class Component : ReactorComponent { }
}

namespace App
{
    public static class Data
    {
        public static Task<int> FetchAsync() => Task.FromResult(1);
    }
}
";

    private static Task Verify<TAnalyzer>(string body) where TAnalyzer : DiagnosticAnalyzer, new()
        => new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> { TestCode = Stubs + body }
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task HookRules_FlagsAConditionalHookDeclaredOnReactorComponent()
        => Verify<HookRulesAnalyzer>(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override string Render()
        {
            if (DateTime.Now.Ticks > 0)
            {
                var (count, setCount) = {|REACTOR_HOOKS_001:UseState(0)|};
            }
            return """";
        }
    }
}");

    [Fact]
    public Task EffectCleanup_FlagsATimerInAnEffectDeclaredOnReactorComponent()
        => Verify<EffectCleanupAnalyzer>(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override string Render()
        {
            UseEffect(() =>
            {
                var t = {|REACTOR_LIFECYCLE_002:new System.Threading.Timer(_ => { })|};
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    [Fact]
    public Task BlockingTask_FlagsAResultInAnEffectDeclaredOnReactorComponent()
        => Verify<BlockingTaskAnalyzer>(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override string Render()
        {
            UseEffect(() =>
            {
                var data = {|REACTOR_THREAD_002:Data.FetchAsync().Result|};
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    [Fact]
    public Task UsePersistedScope_FlagsTheDefaultScopeOverloadDeclaredOnReactorComponent()
        => Verify<UsePersistedScopeAnalyzer>(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override string Render()
        {
            {|REACTOR_PERSIST_001:UsePersisted(""filter"", 0)|};
            return """";
        }
    }
}");
}
