using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// How a control hosts children. The core reconciles child elements (keys, state,
/// mount and unmount) and then hands the strategy the ordered native children the
/// control should end up with.
/// </summary>
public interface IChildrenStrategy<TElement, TControl>
    where TElement : Element
    where TControl : class
{
    IReadOnlyList<Element?> GetChildren(TElement element);

    /// <summary>Makes <paramref name="control"/>'s children exactly <paramref name="natives"/>, in order.</summary>
    void Sync(TControl control, IReadOnlyList<object> natives);
}

/// <summary>
/// A children strategy for controls whose children fill distinct slots (a navigation stack's pages
/// and modals, a flyout and its detail). It receives the native views grouped per child element,
/// in order, instead of one flattened list; a child that rendered nothing has an empty group.
/// </summary>
public interface ISlottedChildrenStrategy<TElement, TControl> : IChildrenStrategy<TElement, TControl>
    where TElement : Element
    where TControl : class
{
    void SyncSlots(TElement element, TControl control, IReadOnlyList<IReadOnlyList<object>> slots);

    void IChildrenStrategy<TElement, TControl>.Sync(TControl control, IReadOnlyList<object> natives) =>
        throw new NotSupportedException("A slotted strategy is synchronized through SyncSlots.");
}

public static class ChildrenStrategy
{
    /// <summary>A control whose children live in an ordered list (a stack or grid panel).</summary>
    public static IChildrenStrategy<TElement, TControl> List<TElement, TControl, TChild>(
        Func<TElement, IReadOnlyList<Element?>> getChildren,
        Func<TControl, IList<TChild>> getList)
        where TElement : Element
        where TControl : class
        where TChild : class
        => new ListStrategy<TElement, TControl, TChild>(getChildren, getList);

    /// <summary>A control with at most one child (a content or scroll host).</summary>
    public static IChildrenStrategy<TElement, TControl> Single<TElement, TControl, TChild>(
        Func<TElement, Element?> getChild,
        Action<TControl, TChild?> setChild)
        where TElement : Element
        where TControl : class
        where TChild : class
        => new SingleStrategy<TElement, TControl, TChild>(getChild, setChild);

    /// <summary>
    /// Reorders <paramref name="actual"/> into <paramref name="desired"/> by reference,
    /// moving only what changed so retained native children are never recreated.
    /// </summary>
    internal static void SyncList<TChild>(IList<TChild> actual, IReadOnlyList<object> desired) where TChild : class
    {
        if (actual.Count > 0)
        {
            var keep = new HashSet<object>(desired, ReferenceEqualityComparer.Instance);
            for (var i = actual.Count - 1; i >= 0; i--)
                if (!keep.Contains(actual[i])) actual.RemoveAt(i);
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var wanted = (TChild)desired[i];
            if (i < actual.Count && ReferenceEquals(actual[i], wanted)) continue;

            for (var j = i + 1; j < actual.Count; j++)
            {
                if (ReferenceEquals(actual[j], wanted))
                {
                    actual.RemoveAt(j);
                    break;
                }
            }
            actual.Insert(i, wanted);
        }
    }

    private sealed class ListStrategy<TElement, TControl, TChild>(
        Func<TElement, IReadOnlyList<Element?>> getChildren,
        Func<TControl, IList<TChild>> getList) : IChildrenStrategy<TElement, TControl>
        where TElement : Element
        where TControl : class
        where TChild : class
    {
        public IReadOnlyList<Element?> GetChildren(TElement element) => getChildren(element);

        public void Sync(TControl control, IReadOnlyList<object> natives) => SyncList(getList(control), natives);
    }

    private sealed class SingleStrategy<TElement, TControl, TChild>(
        Func<TElement, Element?> getChild,
        Action<TControl, TChild?> setChild) : IChildrenStrategy<TElement, TControl>
        where TElement : Element
        where TControl : class
        where TChild : class
    {
        public IReadOnlyList<Element?> GetChildren(TElement element) => [getChild(element)];

        public void Sync(TControl control, IReadOnlyList<object> natives)
        {
            if (natives.Count > 1)
                throw new InvalidOperationException(
                    $"{typeof(TElement).Name} hosts a single child but its content rendered {natives.Count} controls. " +
                    "Wrap them in a stack.");
            setChild(control, natives.Count == 0 ? null : (TChild)natives[0]);
        }
    }
}
