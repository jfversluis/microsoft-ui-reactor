using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>
/// Describes how an element renders to a Controls view, as a list of entries, and implements its
/// handler (spec 067 §16). The verbs follow WinUI's <c>ControlDescriptor</c>: <see cref="Property"/>
/// and <see cref="OneWay"/> for values Reactor owns, <see cref="Initial"/> for values set once,
/// <see cref="Controlled"/> for a value the user can change and the element declares, and
/// <see cref="Event"/> for anything else the view reports. The element's shared modifiers
/// (<see cref="ViewModifiers"/>) are applied too, as on every built-in control.
/// </summary>
/// <remarks>
/// On mount, the view is created, every entry writes its initial value, the modifiers are applied,
/// and only then are events wired, so no initial write is reported as input. On update, each entry
/// writes only what changed. Register the descriptor like any handler:
/// <c>registry.Register(descriptor)</c>.
/// </remarks>
public sealed class ControlDescriptor<TElement, TView> : Reactor.Hosting.IElementHandler<TElement, TView>
    where TElement : ViewElement
    where TView : View
{
    private readonly Func<TElement, TView> _create;
    private readonly List<Entry> _entries = [];
    private Action<TView>? _unmount;
    private Func<TElement, TElement, bool>? _canUpdate;

    /// <param name="create">Creates the view for an element. Don't write the element's values here; entries do.</param>
    public ControlDescriptor(Func<TElement, TView> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        _create = create;
    }

    /// <summary>How the view hosts child elements, or <see langword="null"/> for a leaf.</summary>
    public IChildrenStrategy<TElement, TView>? Children { get; private set; }

    /// <summary>
    /// Binds a bindable property to a value of the element. A changed value is written; a value that
    /// becomes <see langword="null"/> clears the property, so the control's default or its style applies.
    /// </summary>
    public ControlDescriptor<TElement, TView> Property<TValue>(
        BindableProperty property, Func<TElement, TValue> get, IEqualityComparer<TValue>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(get);
        // A value of the wrong type compiles, and MAUI would drop the write at run time.
        var valueType = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);
        if (!property.ReturnType.IsAssignableFrom(valueType))
            throw new ArgumentException(
                $"{property.PropertyName} holds {property.ReturnType.Name} values, but the element gives {valueType.Name}.", nameof(get));
        _entries.Add(new PropertyEntry<TValue>(property, get, comparer ?? EqualityComparer<TValue>.Default));
        return this;
    }

    /// <summary>Writes a value of the element through <paramref name="set"/> on mount, and again whenever it changes.</summary>
    public ControlDescriptor<TElement, TView> OneWay<TValue>(
        Func<TElement, TValue> get, Action<TView, TValue> set, IEqualityComparer<TValue>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        _entries.Add(new OneWayEntry<TValue>(get, set, comparer ?? EqualityComparer<TValue>.Default));
        return this;
    }

    /// <summary>Writes a value of the element once, on mount. Later changes are ignored.</summary>
    public ControlDescriptor<TElement, TView> Initial<TValue>(Func<TElement, TValue> get, Action<TView, TValue> set)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        _entries.Add(new InitialEntry<TValue>(get, set));
        return this;
    }

    /// <summary>
    /// A value the user can change and the element declares, such as a switch's state. The view always
    /// shows the declared value: Reactor's own writes are not reported, the user's changes are reported
    /// through <paramref name="callback"/>, and if the app doesn't accept one (its state doesn't change),
    /// the view returns to the declared value. The parameters follow WinUI's <c>Controlled</c>; MAUI
    /// needs no <c>unsubscribe</c>, because a view is never reused.
    /// </summary>
    /// <param name="get">The value the element declares.</param>
    /// <param name="set">Shows a value. Its change event must be raised inside the write.</param>
    /// <param name="subscribe">Subscribes to the view's change event, reporting the new value.</param>
    /// <param name="callback">The element's callback for a change, if it has one.</param>
    /// <param name="readBack">The value the view shows now. A write happens only when it differs from <paramref name="get"/>.</param>
    /// <param name="comparer">Compares the view's value with the declared one.</param>
    public ControlDescriptor<TElement, TView> Controlled<TValue>(
        Func<TElement, TValue> get,
        Action<TView, TValue> set,
        Action<TView, Action<TValue>> subscribe,
        Func<TElement, Action<TValue>?> callback,
        Func<TView, TValue> readBack,
        IEqualityComparer<TValue>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(readBack);
        _entries.Add(new ControlledEntry<TValue>(get, readBack, set, subscribe, callback, comparer ?? EqualityComparer<TValue>.Default));
        return this;
    }

    /// <summary>
    /// Wires an event of the view, once, after the initial values are written. Invoke the element's
    /// callbacks through <see cref="HandlerContext{TElement}.Live"/>, which is <see langword="null"/>
    /// once the element is unmounted: <c>(context, view) =&gt; view.Completed += (_, _) =&gt; context.Live?.OnCompleted?.Invoke()</c>.
    /// </summary>
    public ControlDescriptor<TElement, TView> Event(Action<HandlerContext<TElement>, TView> wire)
    {
        ArgumentNullException.ThrowIfNull(wire);
        _entries.Add(new EventEntry(wire));
        return this;
    }

    /// <summary>Sets how the view hosts child elements. Children are reconciled after the view's own values are written.</summary>
    public ControlDescriptor<TElement, TView> WithChildren(IChildrenStrategy<TElement, TView> children)
    {
        Children = children ?? throw new ArgumentNullException(nameof(children));
        return this;
    }

    /// <summary>Releases what the view acquired outside the entries, when its element is unmounted.</summary>
    public ControlDescriptor<TElement, TView> WithUnmount(Action<TView> unmount)
    {
        _unmount = unmount ?? throw new ArgumentNullException(nameof(unmount));
        return this;
    }

    /// <summary>
    /// Decides whether a new element can update the view mounted for the old one. Return
    /// <see langword="false"/> when the change needs a new view; Reactor then remounts it.
    /// </summary>
    public ControlDescriptor<TElement, TView> WithCanUpdate(Func<TElement, TElement, bool> canUpdate)
    {
        _canUpdate = canUpdate ?? throw new ArgumentNullException(nameof(canUpdate));
        return this;
    }

    TView Reactor.Hosting.IElementHandler<TElement, TView>.Mount(HandlerContext<TElement> context, TElement element)
    {
        var view = _create(element);
        foreach (var entry in _entries) entry.Mount(view, element);
        ViewApplier.Apply(context, view, null, element.Modifiers);
        foreach (var entry in _entries) entry.Wire(context, view);
        return view;
    }

    void Reactor.Hosting.IElementHandler<TElement, TView>.Update(HandlerContext<TElement> context, TElement oldElement, TElement newElement, TView control)
    {
        foreach (var entry in _entries) entry.Update(context, control, oldElement, newElement);
        ViewApplier.Apply(context, control, oldElement.Modifiers, newElement.Modifiers);
    }

    void Reactor.Hosting.IElementHandler<TElement, TView>.Unmount(TView control) => _unmount?.Invoke(control);

    bool Reactor.Hosting.IElementHandler<TElement, TView>.CanUpdate(TElement oldElement, TElement newElement) =>
        _canUpdate?.Invoke(oldElement, newElement) ?? true;

    private abstract class Entry
    {
        public abstract void Mount(TView view, TElement element);
        public virtual void Wire(HandlerContext<TElement> context, TView view) { }
        public virtual void Update(HandlerContext<TElement> context, TView view, TElement oldElement, TElement newElement) { }
    }

    private sealed class PropertyEntry<TValue>(BindableProperty property, Func<TElement, TValue> get, IEqualityComparer<TValue> comparer) : Entry
    {
        public override void Mount(TView view, TElement element)
        {
            // Nothing to clear on a new view.
            if (get(element) is { } value) view.SetValue(property, value);
        }

        public override void Update(HandlerContext<TElement> context, TView view, TElement oldElement, TElement newElement)
        {
            var next = get(newElement);
            if (comparer.Equals(get(oldElement), next)) return;
            if (next is null) view.ClearValue(property);
            else view.SetValue(property, next);
        }
    }

    private sealed class OneWayEntry<TValue>(Func<TElement, TValue> get, Action<TView, TValue> set, IEqualityComparer<TValue> comparer) : Entry
    {
        public override void Mount(TView view, TElement element) => set(view, get(element));

        public override void Update(HandlerContext<TElement> context, TView view, TElement oldElement, TElement newElement)
        {
            var next = get(newElement);
            if (!comparer.Equals(get(oldElement), next)) set(view, next);
        }
    }

    private sealed class InitialEntry<TValue>(Func<TElement, TValue> get, Action<TView, TValue> set) : Entry
    {
        public override void Mount(TView view, TElement element) => set(view, get(element));
    }

    private sealed class ControlledEntry<TValue>(
        Func<TElement, TValue> get,
        Func<TView, TValue> read,
        Action<TView, TValue> write,
        Action<TView, Action<TValue>> subscribe,
        Func<TElement, Action<TValue>?> callback,
        IEqualityComparer<TValue> comparer) : Entry
    {
        // Events aren't wired yet, so the initial write reports nothing.
        public override void Mount(TView view, TElement element)
        {
            var value = get(element);
            if (!comparer.Equals(read(view), value)) write(view, value);
        }

        public override void Wire(HandlerContext<TElement> context, TView view) =>
            subscribe(view, value =>
            {
                if (context.IsEchoing(view) || context.Live is not { } live) return;
                callback(live)?.Invoke(value);
                // Once state settles, the view shows whatever the element then declares.
                context.RestoreAfterEvent();
            });

        // Compared with the view, not the old element: the user may have changed it since the last
        // render. A restore after an input the app refused calls this with the same element twice.
        public override void Update(HandlerContext<TElement> context, TView view, TElement oldElement, TElement newElement)
        {
            var value = get(newElement);
            if (!comparer.Equals(read(view), value)) context.WriteSuppressed(view, () => write(view, value));
        }
    }

    private sealed class EventEntry(Action<HandlerContext<TElement>, TView> wire) : Entry
    {
        public override void Mount(TView view, TElement element) { }
        public override void Wire(HandlerContext<TElement> context, TView view) => wire(context, view);
    }
}

/// <summary>Creates <see cref="ControlDescriptor{TElement, TView}"/>s.</summary>
public static class ControlDescriptor
{
    /// <summary>A descriptor whose views are created with <typeparamref name="TView"/>'s parameterless constructor.</summary>
    public static ControlDescriptor<TElement, TView> Create<TElement, TView>()
        where TElement : ViewElement
        where TView : View, new() => new(static _ => new TView());
}

/// <summary>Entries for the settings every text element shares.</summary>
public static class ControlDescriptorExtensions
{
    /// <summary>
    /// Binds a text element's font and text color (<see cref="TextViewElement"/>) to the bindable
    /// properties every Controls text view shares.
    /// </summary>
    public static ControlDescriptor<TElement, TView> TextProperties<TElement, TView>(this ControlDescriptor<TElement, TView> descriptor)
        where TElement : TextViewElement
        where TView : View, Microsoft.Maui.Controls.Internals.IFontElement, Microsoft.Maui.Controls.ITextElement =>
        descriptor
            .Property(Label.FontSizeProperty, static e => e.FontSize)
            .Property(Label.FontAttributesProperty, static e => e.FontAttributes)
            .Property(Label.FontFamilyProperty, static e => e.FontFamily)
            .Property(Label.TextColorProperty, static e => e.TextColor)
            .Property(Label.CharacterSpacingProperty, static e => e.CharacterSpacing);
}
