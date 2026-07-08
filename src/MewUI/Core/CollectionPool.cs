namespace Aprillz.MewUI;

/// <summary>
/// Thread-static pool for reusable collection instances.
/// Avoids per-frame allocations of Dictionary, Stack, List, etc.
/// </summary>
internal static class CollectionPool<T> where T : class, new()
{
    [ThreadStatic] private static Stack<T>? _pool;

    private const int MaxPoolSize = 8;

    public static T Rent()
    {
        if (_pool != null && _pool.Count > 0)
        {
            return _pool.Pop();
        }
        return new T();
    }

    internal static void Return(T item)
    {
        _pool ??= new Stack<T>();
        if (_pool.Count < MaxPoolSize)
        {
            _pool.Push(item);
        }
    }
}

/// <summary>
/// Convenience Return overloads for <see cref="CollectionPool{T}"/>. Kept on this non-generic class
/// so the pooled element type is always inferred from the argument, never from an outer type
/// parameter the caller could get wrong (e.g. specifying List&lt;A&gt; while passing a List&lt;B&gt;).
/// </summary>
internal static class CollectionPool
{
    public static void Return<TKey, TValue>(Dictionary<TKey, TValue> map) where TKey : notnull
    {
        map.Clear();
        CollectionPool<Dictionary<TKey, TValue>>.Return(map);
    }

    public static void Return<TElement>(List<TElement> list)
    {
        list.Clear();
        CollectionPool<List<TElement>>.Return(list);
    }

    public static void Return<TElement>(Stack<TElement> stack)
    {
        stack.Clear();
        CollectionPool<Stack<TElement>>.Return(stack);
    }
}
