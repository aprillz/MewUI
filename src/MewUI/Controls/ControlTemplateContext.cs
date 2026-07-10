namespace Aprillz.MewUI.Controls;

/// <summary>
/// The named-part registry for one template application. Template builders register parts
/// during <see cref="ControlTemplate.Build"/>; the owning control looks them up afterwards.
/// </summary>
public sealed class ControlTemplateContext
{
    private readonly Dictionary<string, Element> _parts = new();

    /// <summary>Gets the control this template application belongs to.</summary>
    public Control Owner { get; }

    internal ControlTemplateContext(Control owner)
    {
        Owner = owner;
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
