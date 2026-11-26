using System.Diagnostics;
using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Input;

/// <summary>
/// A handle to the native view an element renders, for calls made outside render: focusing,
/// scrolling, a web view's commands. Get one from <c>UseElementRef&lt;T&gt;()</c>, attach it with
/// <c>.Ref(...)</c>, and read <see cref="Current"/> in an event handler or an effect.
/// </summary>
/// <typeparam name="T">
/// The view type to expose: a Controls type such as <c>Entry</c> or <c>ScrollView</c>, or a Core
/// interface such as <c>IView</c> or <c>IEntry</c>. With the direct renderer, an element whose ref
/// asks for a type its direct view isn't renders as a Controls view, so <c>ElementRef&lt;Entry&gt;</c>
/// is always an <c>Entry</c>; <c>ElementRef&lt;IView&gt;</c> keeps the direct view.
/// </typeparam>
/// <remarks>
/// <para><see cref="Current"/> is set when the element mounts, before its effects run, and is
/// <see langword="null"/> once it unmounts (children unmount first, so it is already
/// <see langword="null"/> in a parent component's cleanup). A ref names one element: attached to
/// several, the last one mounted or updated wins.</para>
/// <para>A ref holds its view strongly. A ref kept outside its tree (from
/// <see cref="TypedElementRef.Create{T}"/>) keeps the view alive until the tree unmounts.</para>
/// <para>Read it and call the view on the UI thread.</para>
/// </remarks>
[DebuggerDisplay("ElementRef<{typeof(T).Name,nq}> Current={Current}")]
public sealed class ElementRef<T> where T : class, IView
{
    internal ElementRef() => Cell = new ElementRefCell(typeof(T));

    internal ElementRefCell Cell { get; }

    /// <summary>The mounted view, or <see langword="null"/> before the element mounts and after it unmounts.</summary>
    public T? Current => Cell.Current as T;

    /// <inheritdoc />
    public override string ToString() => $"ElementRef<{typeof(T).Name}>";
}

/// <summary>Creates refs outside a render.</summary>
public static class TypedElementRef
{
    /// <summary>
    /// Creates an unbound ref, for a test or a field set up before the first render. In a component,
    /// prefer <c>UseElementRef&lt;T&gt;()</c>, which returns the same ref every render.
    /// </summary>
    public static ElementRef<T> Create<T>() where T : class, IView => new();
}
