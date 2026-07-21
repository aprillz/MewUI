using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

internal sealed class MewPropertyPathBinding<TProp, TRoot, TSource> : IDisposable
    where TRoot : class
{
    private readonly MewObject _target;
    private readonly MewProperty<TProp> _targetProperty;
    private readonly BindingPathObserver<TRoot, TSource> _observer;
    private readonly Func<TSource, TProp> _convert;
    private readonly Func<TProp, TSource>? _convertBack;
    private readonly TProp _fallbackValue;
    private readonly BindingMode _mode;
    private readonly Action? _onTargetChanged;
    private bool _updating;
    private bool _disposed;

    internal MewPropertyPathBinding(
        MewObject target,
        MewProperty<TProp> targetProperty,
        TRoot root,
        BindingPath<TRoot, TSource> path,
        Func<TSource, TProp> convert,
        Func<TProp, TSource>? convertBack,
        BindingMode mode,
        TProp fallbackValue)
    {
        _target = target;
        _targetProperty = targetProperty;
        _convert = convert;
        _convertBack = convertBack;
        _fallbackValue = fallbackValue;
        _mode = mode;
        _observer = path.Attach(root);

        try
        {
            _observer.Changed += OnSourceChanged;

            if (mode == BindingMode.TwoWay)
            {
                _onTargetChanged = OnTargetChanged;
                target.AddPropertyBindingCallback(targetProperty.Id, _onTargetChanged);
            }

            OnSourceChanged();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void OnSourceChanged()
    {
        if (_updating || _disposed)
        {
            return;
        }

        _updating = true;
        try
        {
            var value = _observer.IsAvailable
                ? _convert(_observer.CurrentValue)
                : _fallbackValue;

            if (!EqualityComparer<TProp>.Default.Equals(
                    _target.GetBindingValue(_targetProperty), value))
            {
                _target.SetBindingValue(_targetProperty, value);
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private void OnTargetChanged()
    {
        if (_updating || _disposed || !_observer.IsAvailable || _convertBack == null)
        {
            return;
        }

        _updating = true;
        try
        {
            _observer.Write(_convertBack(_target.GetBindingValue(_targetProperty)));
            if (_observer.IsAvailable)
            {
                var normalized = _convert(_observer.CurrentValue);
                if (!EqualityComparer<TProp>.Default.Equals(
                        _target.GetBindingValue(_targetProperty), normalized))
                {
                    _target.SetBindingValue(_targetProperty, normalized);
                }
            }
        }
        finally
        {
            _updating = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _observer.Changed -= OnSourceChanged;
        _observer.Dispose();

        if (_mode == BindingMode.TwoWay && _onTargetChanged != null)
        {
            _target.RemovePropertyBindingCallback(_targetProperty.Id, _onTargetChanged);
        }
    }
}
