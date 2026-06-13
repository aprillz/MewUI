using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Bridges a <see cref="MewProperty{TProp}"/> to an <see cref="ObservableValue{TSource}"/>
/// when the types differ, applying convert/convertBack functions.
/// </summary>
internal sealed class MewPropertyBinding<TProp, TSource> : IDisposable
{
    private readonly MewObject _owner;
    private readonly MewProperty<TProp> _property;
    private readonly ObservableValue<TSource> _source;
    private readonly Func<TSource, TProp> _convert;
    private readonly Func<TProp, TSource>? _convertBack;
    private readonly BindingMode _mode;
    private readonly Action? _onPropertyChanged;
    private bool _updating;

    public MewPropertyBinding(
        MewObject owner,
        MewProperty<TProp> property,
        ObservableValue<TSource> source,
        Func<TSource, TProp> convert,
        Func<TProp, TSource>? convertBack,
        BindingMode mode)
    {
        _owner = owner;
        _property = property;
        _source = source;
        _convert = convert;
        _convertBack = convertBack;
        _mode = mode;

        WeakEventManager.AddHandler(
            ObservableValueWeakEvents<TSource>.Changed,
            source,
            this,
            static binding => binding.OnSourceChanged());

        if (mode == BindingMode.TwoWay && convertBack != null)
        {
            _onPropertyChanged = OnPropertyChanged;
            owner.AddPropertyBindingCallback(property.Id, _onPropertyChanged);
        }

        OnSourceChanged();
    }

    private void OnSourceChanged()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            var converted = _convert(_source.Value);
            if (!EqualityComparer<TProp>.Default.Equals(
                    _owner.PropertyStore.GetValue(_property), converted))
            {
                _owner.PropertyStore.SetLocal(_property, converted);
            }
        }
        finally { _updating = false; }
    }

    private void OnPropertyChanged()
    {
        if (_updating || _convertBack == null) return;
        _updating = true;
        try
        {
            _source.Value = _convertBack(_owner.PropertyStore.GetValue(_property));
        }
        finally { _updating = false; }
    }

    public void Dispose()
    {
        WeakEventManager.RemoveHandler(ObservableValueWeakEvents<TSource>.Changed, _source, this);
        if (_mode == BindingMode.TwoWay && _onPropertyChanged != null)
        {
            _owner.RemovePropertyBindingCallback(_property.Id, _onPropertyChanged);
        }
    }
}
