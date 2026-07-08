using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Bridges a <see cref="MewProperty{T}"/> on a target <see cref="MewObject"/>
/// to a <see cref="MewProperty{T}"/> on a source <see cref="MewObject"/>.
/// When the source property changes, the target is updated at the local tier
/// (consistent with the converting and observable-value bindings).
/// </summary>
internal sealed class MewObjectPropertyBinding<T> : IDisposable
{
    private readonly MewObject _target;
    private readonly MewProperty<T> _targetProperty;
    private readonly MewObject _source;
    private readonly MewProperty<T> _sourceProperty;
    private readonly PropertyForwardEntry _forwardEntry;

    public MewObjectPropertyBinding(
        MewObject target,
        MewProperty<T> targetProperty,
        MewObject source,
        MewProperty<T> sourceProperty)
    {
        _target = target;
        _targetProperty = targetProperty;
        _source = source;
        _sourceProperty = sourceProperty;

        // Register forward on source: when source property changes, propagate to target.
        // Keep the returned entry so Dispose removes exactly this forward, not whatever
        // forward currently occupies the source property id (another binding may share it).
        _forwardEntry = source.AddPropertyForward(sourceProperty.Id, target, targetProperty);

        // Initial sync.
        target.PropertyStore.SetLocal(targetProperty, source.PropertyStore.GetBoxedValue(sourceProperty));
    }

    public void Dispose()
    {
        _source.RemovePropertyForward(_sourceProperty.Id, _forwardEntry);
    }
}
