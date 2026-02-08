namespace Aprillz.MewUI;

/// <summary>
/// Provides a data source abstraction for list controls.
/// </summary>
public sealed class ItemsSource
{
    private readonly Func<int> _getCount;
    private readonly Func<int, object?> _getItem;
    private readonly Func<int, string> _getText;

    private ItemsSource(Func<int> getCount, Func<int, object?> getItem, Func<int, string> getText)
    {
        _getCount = getCount;
        _getItem = getItem;
        _getText = getText;
    }

    /// <summary>
    /// Gets an empty items source.
    /// </summary>
    public static ItemsSource Empty { get; } = new ItemsSource(() => 0, _ => null, _ => string.Empty);

    /// <summary>
    /// Gets the number of items.
    /// </summary>
    public int Count => _getCount();

    /// <summary>
    /// Gets the item object at the specified index.
    /// </summary>
    /// <param name="index">The item index.</param>
    /// <returns>The item object.</returns>
    public object? GetItem(int index) => _getItem(index);

    /// <summary>
    /// Gets the text for the item at the specified index.
    /// </summary>
    /// <param name="index">The item index.</param>
    /// <returns>The item text.</returns>
    public string GetText(int index) => _getText(index);

    /// <summary>
    /// Creates an items source from a string list.
    /// </summary>
    /// <param name="items">The string items.</param>
    /// <returns>The items source.</returns>
    public static ItemsSource Create(IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new ItemsSource(
            () => items.Count,
            i => items[i],
            i => items[i] ?? string.Empty);
    }

    /// <summary>
    /// Creates an items source from a typed list with a text selector.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The typed items.</param>
    /// <param name="textSelector">Function to extract text from each item.</param>
    /// <returns>The items source.</returns>
    public static ItemsSource Create<T>(IReadOnlyList<T> items, Func<T, string> textSelector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(textSelector);

        return new ItemsSource(
            () => items.Count,
            i => items[i],
            i => textSelector(items[i]) ?? string.Empty);
    }
}

