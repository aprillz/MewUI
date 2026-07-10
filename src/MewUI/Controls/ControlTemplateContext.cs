namespace Aprillz.MewUI.Controls;

/// <summary>
/// The named-part registry for one template application. Template builders register parts
/// during <see cref="ControlTemplate.Build"/>; the owning control looks them up afterwards.
/// </summary>
public sealed class ControlTemplateContext
{
    private readonly Dictionary<string, Element> _parts = new();
    private List<(int SourcePropertyId, PropertyForwardEntry Entry)>? _bindings;

    /// <summary>Gets the control this template application belongs to.</summary>
    public Control Owner { get; }

    internal ControlTemplateContext(Control owner)
    {
        Owner = owner;
    }

    /// <summary>
    /// Keeps a target part property in sync with a property of <see cref="Owner"/>:
    /// applies the current value now and forwards subsequent changes. Released when
    /// the template instance is discarded.
    /// </summary>
    /// <param name="target">The template part to update.</param>
    /// <param name="targetProperty">The part property to write.</param>
    /// <param name="sourceProperty">The owner property to follow.</param>
    public void Bind<T>(Element target, MewProperty<T> targetProperty, MewProperty<T> sourceProperty)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(targetProperty);
        ArgumentNullException.ThrowIfNull(sourceProperty);

        target.PropertyStore.SetLocal(targetProperty, Owner.PropertyStore.GetValue(sourceProperty));
        var entry = Owner.AddPropertyForward(sourceProperty.Id, target, targetProperty);
        (_bindings ??= new()).Add((sourceProperty.Id, entry));
    }

    internal void ReleaseBindings()
    {
        if (_bindings == null)
        {
            return;
        }

        for (int i = 0; i < _bindings.Count; i++)
        {
            Owner.RemovePropertyForward(_bindings[i].SourcePropertyId, _bindings[i].Entry);
        }

        _bindings = null;
    }

    /// <summary>
    /// Registers a named part. Names are unique within one template application.
    /// </summary>
    /// <param name="name">The part name.</param>
    /// <param name="element">The part element.</param>
    public void Register(string name, Element element)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(element);

        if (!_parts.TryAdd(name, element))
        {
            throw new InvalidOperationException($"A part named '{name}' is already registered.");
        }
    }

    /// <summary>
    /// Returns the registered part with the given name and type; throws when missing or mismatched.
    /// </summary>
    /// <param name="name">The part name.</param>
    public T Get<T>(string name) where T : Element
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (_parts.TryGetValue(name, out var element) && element is T typed)
        {
            return typed;
        }

        throw new InvalidOperationException($"No part named '{name}' of type {typeof(T).Name} is registered.");
    }

    internal Element? Find(string name) => _parts.GetValueOrDefault(name);
}
