using System.Runtime.ExceptionServices;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Per-container companion to a template: names the elements built once, and owns the bindings and
/// event subscriptions made each time an item is bound so they are undone when it is unbound.
/// </summary>
/// <remarks>
/// A container gets one context for its lifetime. Registered names survive rebinding; bindings and
/// subscriptions do not, so register them from <see cref="IDataTemplate.Bind"/> rather than
/// <see cref="IDataTemplate.Build"/>.
/// </remarks>
public sealed partial class TemplateContext : IDisposable
{
    private readonly Dictionary<string, UIElement> _namedElements = new(StringComparer.Ordinal);
    private readonly List<ICleanup> _cleanup = new();
    private IDataTemplate? _boundTemplate;
    private object? _boundItem;
    private int _boundIndex = -1;

    /// <summary>
    /// Names an element for later <see cref="Get{T}"/>. Names are unique within one context and
    /// outlive <see cref="Reset"/>, so register once from the build step.
    /// </summary>
    /// <exception cref="InvalidOperationException">An element is already registered under that name.</exception>
    public void Register<T>(string name, T element) where T : UIElement
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(element);

        if (!_namedElements.TryAdd(name, element))
        {
            throw new InvalidOperationException($"An element named '{name}' is already registered.");
        }
    }

    /// <summary>
    /// Returns the element registered under <paramref name="name"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No element is registered under that name, or it is not a <typeparamref name="T"/>.
    /// </exception>
    public T Get<T>(string name) where T : UIElement
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!_namedElements.TryGetValue(name, out var element))
        {
            throw new InvalidOperationException($"No element named '{name}' is registered.");
        }

        if (element is not T typed)
        {
            throw new InvalidOperationException($"The element named '{name}' is a {element.GetType().Name}, not a {typeof(T).Name}.");
        }

        return typed;
    }

    /// <summary>
    /// Binds a property to an observable value and clears that binding on the next
    /// <see cref="Reset"/>. Binding the same target property twice registers one cleanup.
    /// </summary>
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

    /// <summary>
    /// Binds a property to a converted observable value and clears that binding on the next
    /// <see cref="Reset"/>.
    /// </summary>
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

    /// <summary>
    /// Binds a property along a path from a source root and clears the whole path on the next
    /// <see cref="Reset"/>.
    /// </summary>
    public void Bind<TRoot, T>(
        MewObject target,
        MewProperty<T> property,
        TRoot source,
        BindingPath<TRoot, T> path,
        BindingMode? mode = null,
        T fallbackValue = default!)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(path);

        target.SetBinding(property, source, path, mode, fallbackValue);
        RegisterPropertyBinding(target, property);
    }

    /// <summary>
    /// Binds a property along a converted path from a source root and clears the whole path on the
    /// next <see cref="Reset"/>.
    /// </summary>
    public void Bind<TProp, TRoot, TSource>(
        MewObject target,
        MewProperty<TProp> property,
        TRoot source,
        BindingPath<TRoot, TSource> path,
        Func<TSource, TProp> convert,
        Func<TProp, TSource>? convertBack = null,
        BindingMode? mode = null,
        TProp fallbackValue = default!)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(convert);

        target.SetBinding(property, source, path, convert, convertBack, mode, fallbackValue);
        RegisterPropertyBinding(target, property);
    }

    /// <summary>
    /// Runs <paramref name="add"/> now and <paramref name="remove"/> on the next <see cref="Reset"/>.
    /// Subscribing from the bind step every time is correct: a rebind unsubscribes first, so
    /// handlers do not accumulate.
    /// </summary>
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

    /// <inheritdoc cref="Subscribe{TSource, THandler}(TSource, Action{TSource, THandler}, Action{TSource, THandler}, THandler)"/>
    public void Subscribe<TSource>(
        TSource source,
        Action<TSource, Action> add,
        Action<TSource, Action> remove,
        Action handler)
        where TSource : class
        => Subscribe<TSource, Action>(source, add, remove, handler);

    /// <inheritdoc cref="Subscribe{TSource, THandler}(TSource, Action{TSource, THandler}, Action{TSource, THandler}, THandler)"/>
    public void Subscribe<TSource, TArg>(
        TSource source,
        Action<TSource, Action<TArg>> add,
        Action<TSource, Action<TArg>> remove,
        Action<TArg> handler)
        where TSource : class
        => Subscribe<TSource, Action<TArg>>(source, add, remove, handler);

    /// <inheritdoc cref="Subscribe{TSource, THandler}(TSource, Action{TSource, THandler}, Action{TSource, THandler}, THandler)"/>
    public void Subscribe<TSource, TEventArgs>(
        TSource source,
        Action<TSource, EventHandler<TEventArgs>> add,
        Action<TSource, EventHandler<TEventArgs>> remove,
        EventHandler<TEventArgs> handler)
        where TSource : class
        => Subscribe<TSource, EventHandler<TEventArgs>>(source, add, remove, handler);

    /// <summary>
    /// Subscribes to an object's property changes now and unsubscribes on the next <see cref="Reset"/>.
    /// </summary>
    public void Subscribe(
        System.ComponentModel.INotifyPropertyChanged source,
        System.ComponentModel.PropertyChangedEventHandler handler)
        => Subscribe<System.ComponentModel.INotifyPropertyChanged, System.ComponentModel.PropertyChangedEventHandler>(
            source,
            static (target, value) => target.PropertyChanged += value,
            static (target, value) => target.PropertyChanged -= value,
            handler);

    /// <summary>
    /// Undoes every binding and subscription registered since the last reset, in reverse order of
    /// registration, then forgets them. Registered names are kept.
    /// </summary>
    /// <remarks>
    /// One failing cleanup does not stop the rest: the first exception is rethrown once every other
    /// cleanup has run.
    /// </remarks>
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

    /// <summary>Equivalent to <see cref="Reset"/>; the context stays usable afterwards.</summary>
    public void Dispose() => Reset();

    /// <summary>
    /// Unbinds whatever this context currently holds, then binds the template to the item. A
    /// failing bind leaves the context reset.
    /// </summary>
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

    /// <summary>
    /// Calls the bound template's unbind step and resets the context, even if that step throws.
    /// With no template bound it only resets.
    /// </summary>
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
