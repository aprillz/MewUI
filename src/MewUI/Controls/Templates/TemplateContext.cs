using System.Runtime.ExceptionServices;

namespace Aprillz.MewUI.Controls;

public sealed class TemplateContext : IDisposable
{
    private readonly Dictionary<string, UIElement> _namedElements = new(StringComparer.Ordinal);
    private readonly List<ICleanup> _cleanup = new();
    private IDataTemplate? _boundTemplate;
    private object? _boundItem;
    private int _boundIndex = -1;

    public void Register<T>(string name, T element) where T : UIElement
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(element);

        _namedElements[name] = element;
    }

    public T Get<T>(string name) where T : UIElement
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!_namedElements.TryGetValue(name, out var element))
        {
            throw new KeyNotFoundException($"TemplateContext has no element named '{name}'.");
        }

        if (element is not T typed)
        {
            throw new InvalidCastException($"TemplateContext element '{name}' is '{element.GetType().Name}', not '{typeof(T).Name}'.");
        }

        return typed;
    }

    public void Bind<T>(
        MewObject target,
        MewProperty<T> property,
        ObservableValue<T> source,
        BindingMode? mode = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(source);

        target.SetBinding(property, source, mode);
        RegisterPropertyBinding(target, property);
    }

    public void Bind<TProp, TSource>(
        MewObject target,
        MewProperty<TProp> property,
        ObservableValue<TSource> source,
        Func<TSource, TProp> convert,
        Func<TProp, TSource>? convertBack = null,
        BindingMode? mode = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        target.SetBinding(property, source, convert, convertBack, mode);
        RegisterPropertyBinding(target, property);
    }

    public void Subscribe<TSource, THandler>(
        TSource source,
        Action<TSource, THandler> add,
        Action<TSource, THandler> remove,
        THandler handler)
        where TSource : class
        where THandler : Delegate
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(remove);
        ArgumentNullException.ThrowIfNull(handler);

        add(source, handler);
        _cleanup.Add(new EventCleanup<TSource, THandler>(source, remove, handler));
    }

    public void Reset()
    {
        ExceptionDispatchInfo? firstError = null;

        for (int i = _cleanup.Count - 1; i >= 0; i--)
        {
            try
            {
                _cleanup[i].Run();
            }
            catch (Exception ex)
            {
                firstError ??= ExceptionDispatchInfo.Capture(ex);
            }
        }

        _cleanup.Clear();
        firstError?.Throw();
    }

    public void Dispose() => Reset();

    internal void BindTemplate(FrameworkElement view, IDataTemplate template, object? item, int index)
    {
        UnbindTemplate(view);

        try
        {
            template.Bind(view, item, index, this);
            _boundTemplate = template;
            _boundItem = item;
            _boundIndex = index;
        }
        catch
        {
            Reset();
            throw;
        }
    }

    internal void UnbindTemplate(FrameworkElement view)
    {
        var template = _boundTemplate;
        var item = _boundItem;
        var index = _boundIndex;

        _boundTemplate = null;
        _boundItem = null;
        _boundIndex = -1;

        try
        {
            template?.Unbind(view, item, index, this);
        }
        finally
        {
            Reset();
        }
    }

    private void RegisterPropertyBinding<T>(MewObject target, MewProperty<T> property)
    {
        for (int i = 0; i < _cleanup.Count; i++)
        {
            if (_cleanup[i] is PropertyBindingCleanup<T> existing &&
                ReferenceEquals(existing.Target, target) &&
                ReferenceEquals(existing.Property, property))
            {
                return;
            }
        }

        _cleanup.Add(new PropertyBindingCleanup<T>(target, property));
    }

    private interface ICleanup
    {
        void Run();
    }

    private sealed class PropertyBindingCleanup<T>(
        MewObject target,
        MewProperty<T> property) : ICleanup
    {
        public MewObject Target { get; } = target;

        public MewProperty<T> Property { get; } = property;

        public void Run() => Target.ClearBinding(Property);
    }

    private sealed class EventCleanup<TSource, THandler>(
        TSource source,
        Action<TSource, THandler> remove,
        THandler handler) : ICleanup
        where TSource : class
        where THandler : Delegate
    {
        public void Run() => remove(source, handler);
    }
}
