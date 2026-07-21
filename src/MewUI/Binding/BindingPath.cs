using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Creates reusable, reflection-free binding paths.
/// </summary>
public static class BindingPath
{
    /// <summary>
    /// Starts a path at a root object type. The returned descriptor stores no root instance.
    /// </summary>
    public static BindingPath<TRoot, TRoot> From<TRoot>()
        where TRoot : class
        => new(Array.Empty<IBindingPathSegment>());
}

/// <summary>
/// An immutable, reusable path from <typeparamref name="TRoot"/> to
/// <typeparamref name="TValue"/>.
/// </summary>
public sealed class BindingPath<TRoot, TValue>
    where TRoot : class
{
    private readonly IBindingPathSegment[] _segments;

    internal BindingPath(IBindingPathSegment[] segments)
    {
        _segments = segments;
    }

    internal IReadOnlyList<IBindingPathSegment> Segments => _segments;

    internal bool CanWrite => _segments.Length > 0 && _segments[^1].CanWrite;

    /// <summary>
    /// Appends a non-observable getter. The getter is evaluated during attachment and whenever an
    /// observed upstream segment rebuilds the downstream path.
    /// </summary>
    public BindingPath<TRoot, TNext> Then<TNext>(Func<TValue, TNext> getter)
    {
        ArgumentNullException.ThrowIfNull(getter);
        return Append<TNext>(new GetterBindingPathSegment<TValue, TNext>(getter));
    }

    /// <summary>
    /// Appends an observable segment. The observable value is read immediately and changes rebuild
    /// the downstream path.
    /// </summary>
    public BindingPath<TRoot, TNext> Then<TNext>(
        Func<TValue, ObservableValue<TNext>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return Append<TNext>(new ObservableBindingPathSegment<TValue, TNext>(selector));
    }

    internal BindingPath<TRoot, TNext> Append<TNext>(IBindingPathSegment segment)
    {
        var segments = new IBindingPathSegment[_segments.Length + 1];
        Array.Copy(_segments, segments, _segments.Length);
        segments[^1] = segment;
        return new BindingPath<TRoot, TNext>(segments);
    }

    internal BindingPathObserver<TRoot, TValue> Attach(TRoot root)
        => new(this, root);
}

/// <summary>
/// Adds <see cref="MewProperty{T}"/> segments to binding paths whose current owner is a
/// <see cref="MewObject"/>.
/// </summary>
public static class MewPropertyBindingPathExtensions
{
    /// <summary>
    /// Appends an observable MewProperty segment.
    /// </summary>
    public static BindingPath<TRoot, TNext> Then<TRoot, TOwner, TNext>(
        this BindingPath<TRoot, TOwner> path,
        MewProperty<TNext> property)
        where TRoot : class
        where TOwner : MewObject
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(property);
        return path.Append<TNext>(new MewPropertyBindingPathSegment<TOwner, TNext>(property));
    }
}
