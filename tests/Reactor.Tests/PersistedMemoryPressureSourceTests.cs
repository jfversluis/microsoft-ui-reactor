using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 067 §10: the memory-pressure signal is a seam the renderer installs as
/// <c>ApplicationPersistedScope.MemoryPressureSource</c>. These tests swap that process-wide source,
/// so they share the non-parallel <c>PersistedStateCache</c> collection and restore it afterwards.
/// </summary>
[Collection("PersistedStateCache")]
public class PersistedMemoryPressureSourceTests
{
    private sealed class FakeSource
    {
        public readonly List<Action> Subscribed = [];
        public readonly List<Action> Unsubscribed = [];

        public IDisposable? Subscribe(Action onHighPressure)
        {
            Subscribed.Add(onHighPressure);
            return new Registration(this, onHighPressure);
        }

        public Action? SubscriptionOf(ApplicationPersistedScope scope)
            => Subscribed.Find(cb => ReferenceEquals(cb.Target, scope));

        private sealed class Registration(FakeSource owner, Action callback) : IDisposable
        {
            public void Dispose() => owner.Unsubscribed.Add(callback);
        }
    }

    private static void WithSource(Action body)
    {
        // Install WinUI's source and create Default first, so Default never waits for (and then
        // subscribes to) a fake source: restoring the delegate afterwards would not undo that.
        WinUICoreServices.EnsureInstalled();
        _ = ApplicationPersistedScope.Default;
        var saved = ApplicationPersistedScope.MemoryPressureSource;
        try { body(); }
        finally { ApplicationPersistedScope.MemoryPressureSource = saved; }
    }

    [Fact]
    public void Default_InitializesWithoutFaultingTheTypeInitializer()
    {
        // Default's constructor takes the static source lock, so the lock must be initialized first.
        Assert.Equal(ApplicationPersistedScope.DefaultCapacity, ApplicationPersistedScope.Default.Capacity);
    }

    [Fact]
    public void ScopeCreatedBeforeTheSource_RegistersWhenItArrives_TrimsOnPressure_AndUnsubscribesOnDispose()
    {
        WithSource(() =>
        {
            ApplicationPersistedScope.MemoryPressureSource = null;
            var scope = new ApplicationPersistedScope(8);
            for (var i = 0; i < 8; i++) scope.Set($"k{i}", i);

            var fake = new FakeSource();
            ApplicationPersistedScope.MemoryPressureSource = fake.Subscribe;

            var onPressure = fake.SubscriptionOf(scope);
            Assert.NotNull(onPressure);
            onPressure!();
            Assert.Equal(2, scope.Count);

            scope.Dispose();
            Assert.Contains(onPressure, fake.Unsubscribed);
        });
    }

    [Fact]
    public void ScopeCreatedAfterTheSource_RegistersImmediately()
    {
        WithSource(() =>
        {
            var fake = new FakeSource();
            ApplicationPersistedScope.MemoryPressureSource = fake.Subscribe;
            using var scope = new ApplicationPersistedScope(8);
            Assert.NotNull(fake.SubscriptionOf(scope));
        });
    }

    [Fact]
    public void ScopeDisposedWhileAwaitingTheSource_NeverRegisters()
    {
        WithSource(() =>
        {
            ApplicationPersistedScope.MemoryPressureSource = null;
            var scope = new ApplicationPersistedScope(8);
            scope.Dispose();

            var fake = new FakeSource();
            ApplicationPersistedScope.MemoryPressureSource = fake.Subscribe;
            Assert.Null(fake.SubscriptionOf(scope));
        });
    }

    [Fact]
    public void ConstructingAReconciler_InstallsWinUIsMemoryPressureSource()
    {
        _ = new Reconciler();
        var source = ApplicationPersistedScope.MemoryPressureSource;
        Assert.NotNull(source);
        Assert.Equal(typeof(Reconciler).Assembly, source!.Method.DeclaringType!.Assembly);
    }
}
