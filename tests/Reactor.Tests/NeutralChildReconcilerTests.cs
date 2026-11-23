using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 067: the keyed child-reconciliation engine in the neutral core, driven through a fake
/// renderer with no WinUI types, so these prove the engine runs (and diffs correctly) for a
/// renderer other than WinUI. The WinUI façade is covered by the reconciler tests and selftests.
/// </summary>
public class NeutralChildReconcilerTests
{
    record Item(string Text) : ReactorElement;
    record Empty() : ReactorElement;

    sealed class Native(string? key, string text)
    {
        public string? Key { get; } = key;
        public string Text { get; set; } = text;
    }

    sealed class Log
    {
        public int Mounts, Updates, Unmounts, Moves, Skipped;
        public bool KeylessNatives;
    }

    sealed class NativeList(Log log) : IChildList<Native>
    {
        public readonly List<Native> Items = [];
        public int Count => Items.Count;
        public Native Get(int index) => Items[index];
        public void Insert(int index, Native element) => Items.Insert(index, element);
        public void RemoveAt(int index) => Items.RemoveAt(index);
        public void Move(int oldIndex, int newIndex)
        {
            if (oldIndex == newIndex) return;
            log.Moves++;
            var item = Items[oldIndex];
            Items.RemoveAt(oldIndex);
            Items.Insert(newIndex, item);
        }
        public void Replace(int index, Native element) => Items[index] = element;
    }

    readonly struct Host(Log log) : IChildReconcileHost<ReactorElement, Native>
    {
        public Native? Mount(ReactorElement element, global::System.Action requestRerender)
        {
            log.Mounts++;
            return new Native(element.Key, ((Item)element).Text);
        }

        public Native? UpdateChildTracked(ReactorElement oldElement, ReactorElement newElement, Native control, global::System.Action requestRerender, out bool unmountedByUpdate)
        {
            log.Updates++;
            control.Text = ((Item)newElement).Text;
            unmountedByUpdate = false;
            return null;
        }

        public void UnmountChild(Native control) => log.Unmounts++;
        public bool CanUpdate(ReactorElement oldElement, ReactorElement newElement) => oldElement.GetType() == newElement.GetType();
        public bool CanSkipUpdate(ReactorElement oldElement, ReactorElement newElement) => oldElement.Equals(newElement);
        public bool ForceFullRenderActive => false;
        public bool ForceRenderThroughWrapper(ReactorElement newElement) => false;
        public bool HasActiveContextValues => false;
        public bool ShouldDeclineSkip(Native control) => false;
        public bool NeedsRefreshOnSkip(ReactorElement oldElement, ReactorElement newElement) => false;
        public void RefreshSkippedChildState(Native control, ReactorElement oldElement, ReactorElement newElement) { }
        public void CountSkipped(int count) => log.Skipped += count;
        public bool TryGetChangedIndices(ReactorElement[] oldChildren, ReactorElement[] newChildren, out int[] changedIndices)
        {
            changedIndices = [];
            return false;
        }
        public DirtyChildIndices ResolveDirtyChildIndices(Native? parentControl, IChildList<Native> children) => default;
        public bool IsOnDirtyAncestorPath(Native control) => false;
        public bool HasExitingControls => false;
        public bool HoldsExitingControlIn(IChildList<Native> children) => false;
        public bool IsExiting(Native control) => false;
        public void RemoveChildWithExitTransition(IChildList<Native> children, int index)
        {
            UnmountChild(children.Get(index));
            children.RemoveAt(index);
        }
        public bool ReplaceChildWithExitTransition(IChildList<Native> children, int index, Native newControl)
        {
            UnmountChild(children.Get(index));
            children.Replace(index, newControl);
            return false;
        }
        public bool TryGetMountedKey(Native control, out string? key)
        {
            key = control.Key;
            return !log.KeylessNatives;
        }
        public void ReportDuplicateSiblingKeys(ReactorElement[] children, Native? parentControl) { }
        public bool IsEmpty(ReactorElement element) => element is Empty;
        public bool HasAmbientAnimation => false;
        public void OnInsertedUnderAmbient(Native control, ReactorElement element) { }
        public void OnMovedUnderAmbient(Native control) { }
    }

    static ReactorElement[] Keyed(params string[] keys) => [.. keys.Select(k => new Item(k) { Key = k })];

    static (NativeList List, Log Log) Render(ReactorElement[] first, bool keylessNatives = false)
    {
        var log = new Log { KeylessNatives = keylessNatives };
        var list = new NativeList(log);
        ChildReconciler<ReactorElement, Native, Host>.Reconcile([], first, list, new Host(log), static () => { });
        log.Mounts = 0;
        return (list, log);
    }

    static void Rerender(NativeList list, Log log, ReactorElement[] oldChildren, ReactorElement[] newChildren) =>
        ChildReconciler<ReactorElement, Native, Host>.Reconcile(oldChildren, newChildren, list, new Host(log), static () => { });

    [Fact]
    public void FirstRender_MountsEveryChildInOrder()
    {
        var log = new Log();
        var list = new NativeList(log);

        ChildReconciler<ReactorElement, Native, Host>.Reconcile([], Keyed("a", "b", "c"), list, new Host(log), static () => { });

        Assert.Equal(["a", "b", "c"], list.Items.Select(n => n.Text));
        Assert.Equal(3, log.Mounts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeyedRotation_MovesOneNativeAndRemountsNothing(bool keylessNatives)
    {
        var old = Keyed("a", "b", "c", "d", "e");
        var (list, log) = Render(old, keylessNatives);
        var before = list.Items.ToDictionary(n => n.Text);

        Rerender(list, log, old, Keyed("e", "a", "b", "c", "d"));

        Assert.Equal(["e", "a", "b", "c", "d"], list.Items.Select(n => n.Text));
        Assert.All(list.Items, n => Assert.Same(before[n.Text], n));
        Assert.Equal(0, log.Mounts);
        Assert.Equal(0, log.Unmounts);
        Assert.Equal(1, log.Moves); // the LIS keeps a..d in place and moves only e
    }

    [Fact]
    public void KeyedMiddleReplacement_UnmountsTheRemovedAndMountsTheAdded()
    {
        var old = Keyed("a", "b", "c", "d");
        var (list, log) = Render(old);
        var kept = new[] { list.Items[0], list.Items[2], list.Items[3] };

        Rerender(list, log, old, Keyed("a", "x", "c", "d"));

        Assert.Equal(["a", "x", "c", "d"], list.Items.Select(n => n.Text));
        Assert.Same(kept[0], list.Items[0]);
        Assert.Same(kept[1], list.Items[2]);
        Assert.Same(kept[2], list.Items[3]);
        Assert.Equal(1, log.Mounts);
        Assert.Equal(1, log.Unmounts);
    }

    [Fact]
    public void UnchangedChildren_AreSkippedWithoutAnUpdate()
    {
        var old = Keyed("a", "b", "c");
        var (list, log) = Render(old);

        Rerender(list, log, old, Keyed("a", "b", "c"));

        Assert.Equal(0, log.Updates);
        Assert.Equal(3, log.Skipped);
    }

    [Fact]
    public void UnkeyedConditionalChild_KeepsItsSiblingsInTheirSlots()
    {
        ReactorElement[] old = [new Empty(), new Item("p"), new Item("q")];
        var (list, log) = Render(old);
        var p = list.Items[0];
        var q = list.Items[1];

        Rerender(list, log, old, [new Item("t"), new Item("p"), new Item("q")]);

        Assert.Equal(["t", "p", "q"], list.Items.Select(n => n.Text));
        Assert.Same(p, list.Items[1]);
        Assert.Same(q, list.Items[2]);
        Assert.Equal(1, log.Mounts);
        Assert.Equal(0, log.Unmounts);
    }
}
