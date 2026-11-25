using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// The lookup both renderers' handler registries share (spec 067 §15): an element type resolves to
/// the value registered for exactly that type, or else to the value registered for its nearest base
/// type (registrations "for derived types"). Base-type results, including misses, are cached per
/// element type, except for collectible types, whose load context the cache would otherwise keep
/// alive.
/// </summary>
/// <remarks>
/// The table has no duplicate policy of its own: <see cref="TryAdd"/> keeps the first value and
/// <see cref="Set"/> replaces it, and each renderer picks one (WinUI's process-wide registry keeps the
/// first, MAUI's per-root registries the last). Safe for concurrent use: a lookup that overlaps a
/// registration never leaves a stale entry in the cache.
/// </remarks>
/// <typeparam name="TValue">What a registration holds, such as a handler or a handler factory.</typeparam>
internal sealed class HandlerTable<TValue> where TValue : class
{
    private readonly ConcurrentDictionary<Type, TValue> _exact = new();
    private readonly ConcurrentDictionary<Type, TValue> _bases = new();
    // Each base-type result is stamped with the generation it was resolved under. Every registration
    // for derived types bumps the generation, so an entry a lookup publishes after a registration it
    // overlapped is never served: readers accept only entries from the current generation.
    private readonly ConcurrentDictionary<Type, (TValue? Value, int Generation)> _baseCache = new();
    private int _generation;

    // Whether any registration for derived types exists. ConcurrentDictionary.IsEmpty takes every
    // lock when the dictionary is empty, which is the common case this check exists for.
    private volatile bool _hasBases;

    /// <summary>Test hook: runs after a lookup's walk, before it caches the result.</summary>
    internal Action? BeforePublishForTesting;

    /// <summary>Registers <paramref name="value"/> for <paramref name="elementType"/> unless one is registered. Returns whether it was added.</summary>
    public bool TryAdd(Type elementType, TValue value, bool forDerivedTypes = false)
    {
        if (!forDerivedTypes) return _exact.TryAdd(elementType, value);
        if (!_bases.TryAdd(elementType, value)) return false;
        // Types that resolved before this registration may now resolve to it. Exact entries are
        // consulted before the cache, so an exact registration never needs this.
        Invalidate();
        return true;
    }

    /// <summary>Registers <paramref name="value"/> for <paramref name="elementType"/>, replacing any earlier registration.</summary>
    public void Set(Type elementType, TValue value, bool forDerivedTypes = false)
    {
        if (!forDerivedTypes)
        {
            _exact[elementType] = value;
            return;
        }
        _bases[elementType] = value;
        Invalidate();
    }

    private void Invalidate()
    {
        _hasBases = true;
        Interlocked.Increment(ref _generation);
        _baseCache.Clear();
    }

    /// <summary>Resolves <paramref name="elementType"/>: its exact registration, else its nearest registered base type's.</summary>
    public bool TryResolve(Type elementType, [NotNullWhen(true)] out TValue? value)
    {
        if (_exact.TryGetValue(elementType, out value)) return true;
        // Most tables have no registrations for derived types; skip the walk entirely.
        if (!_hasBases)
        {
            value = null;
            return false;
        }
        var generation = Volatile.Read(ref _generation);
        if (_baseCache.TryGetValue(elementType, out var cached) && cached.Generation == generation)
        {
            value = cached.Value;
            return value is not null;
        }

        for (var type = elementType.BaseType; type is not null && type != typeof(object); type = type.BaseType)
        {
            if (_bases.TryGetValue(type, out value))
            {
                Publish(elementType, value, generation);
                return true;
            }
        }
        // A miss is cached too, so the next lookup doesn't walk the chain again.
        Publish(elementType, null, generation);
        value = null;
        return false;
    }

    private void Publish(Type elementType, TValue? value, int generation)
    {
        if (elementType.IsCollectible) return;
        BeforePublishForTesting?.Invoke();
        // If a registration ran during the walk, this entry carries the older generation and is
        // ignored by every later lookup, which walks again and replaces it.
        _baseCache[elementType] = (value, generation);
    }

    /// <summary>Whether a registration exists for exactly <paramref name="elementType"/>.</summary>
    public bool ContainsExact(Type elementType) => _exact.ContainsKey(elementType);

    /// <summary>Whether a registration for the types derived from <paramref name="baseType"/> exists.</summary>
    public bool ContainsBase(Type baseType) => _bases.ContainsKey(baseType);

    /// <summary>The element types with an exact registration.</summary>
    public IReadOnlyCollection<Type> ExactTypes => _exact.Keys.ToArray();

    /// <summary>The base types with a registration for their derived types.</summary>
    public IReadOnlyCollection<Type> BaseTypes => _bases.Keys.ToArray();
}
