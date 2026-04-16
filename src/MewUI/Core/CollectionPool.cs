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
            return _pool.Pop();
        return new T();
    }

    public static void Return(T item)
    {
        _pool ??= new Stack<T>();
        if (_pool.Count < MaxPoolSize)
            _pool.Push(item);
    }

    public static void ReturnList<TElement>(List<TElement> list)
    {
        list.Clear();
        CollectionPool<List<TElement>>.Return(list);
    }
}
