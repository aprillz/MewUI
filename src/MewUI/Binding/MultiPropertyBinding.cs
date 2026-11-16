using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

internal sealed class MultiPropertyBinding<TProp> : IPropertyBinding
{
    private readonly MewObject _target;
    private readonly MewProperty<TProp> _targetProperty;
    private readonly MultiBindingSource<TProp> _source;
    private readonly IBindingValueObserver[] _observers;
    private readonly TProp _fallbackValue;
    private bool _updating;
    private bool _disposed;

    internal MultiPropertyBinding(
        MewObject target,
        MewProperty<TProp> targetProperty,
        MultiBindingSource<TProp> source,
        TProp fallbackValue)
    {
        _target = target;
        _targetProperty = targetProperty;
        _source = source;
        _fallbackValue = fallbackValue;
        _observers = source.Attach();

        foreach (var observer in _observers)
        {
            observer.Changed += OnSourceChanged;
        }
    }

    public BindingCapabilities Capabilities { get; } =
        BindingCapabilities.FromMode(BindingMode.OneWay);

    public void Initialize() => OnSourceChanged();

    private void OnSourceChanged()
    {
        if (_updating || _disposed)
        {
            return;
        }

        _updating = true;
        try
        {
            bool available = true;
            foreach (var observer in _observers)
            {
                if (observer.Error != null)
                {
                    _target.ReportBindingError(
                        _targetProperty,
                        null,
                        BindingStatus.BindingError,
                        BindingErrorStage.SourceReadBack,
                        observer.Error);
                    return;
                }

                available &= observer.IsAvailable;
            }

            TProp value;
            if (available)
            {
                try
                {
                    value = _source.Combine(_observers);
                }
                catch (Exception ex)
                {
                    _target.ReportBindingError(
                        _targetProperty,
                        null,
                        BindingStatus.BindingError,
                        BindingErrorStage.Convert,
                        ex);
                    return;
                }
            }
            else
            {
                value = _fallbackValue;
            }

            _target.ApplyBindingTargetValue(_targetProperty, value);
        }
        finally
        {
            _updating = false;
        }
    }

    public void UpdateTargetValue(object? value)
    {
        if (_disposed)
        {
            return;
        }

        _target.UpdateBindingTarget(_targetProperty, (TProp)value!);
    }

    public BindingCommitResult CommitTargetValue(object? value)
        => BindingCommitResult.Failure(
            BindingStatus.BindingError,
            BindingErrorStage.SourceWrite,
            "A binding computed from several inputs does not write back to them.");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var observer in _observers)
        {
            observer.Changed -= OnSourceChanged;
            observer.Dispose();
        }
    }
}
