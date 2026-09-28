namespace Aprillz.MewUI.Controls;

/// <summary>
/// The realized containers a Reset left in place, each held under the key of the item it showed, until
/// the next layout pass takes back the ones whose items are still there. A container stays attached while
/// held, so what it shows, where focus sits in it and what it recorded all survive the Reset.
/// </summary>
internal sealed class ResetHeldContainers
{
    private readonly Dictionary<FrameworkElement, object> _boundKeys = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, FrameworkElement> _held = new();

    public bool IsEmpty => _held.Count == 0;

    /// <summary>The key of the item at <paramref name="index"/>: the view's key selector, or the item itself.</summary>
    public static object? KeyAt(IItemsView view, int index)
    {
        var item = view.GetItem(index);
        if (item == null)
        {
            return null;
        }

        var selector = view.KeySelector;
        return selector != null ? selector(item) : item;
    }

    /// <summary>Notes the key of the item <paramref name="container"/> was just bound to.</summary>
    public void NoteBound(FrameworkElement container, object? key)
    {
        if (key == null)
        {
            _boundKeys.Remove(container);
        }
        else
        {
            _boundKeys[container] = key;
        }
    }

    /// <summary>Forgets <paramref name="container"/>, which no longer shows an item.</summary>
    public void Forget(FrameworkElement container) => _boundKeys.Remove(container);

    /// <summary>
    /// Holds <paramref name="container"/> under its item's key. False when that key is unknown or already
    /// held by another container; the caller recycles such a container as usual.
    /// </summary>
    public bool TryHold(FrameworkElement container)
        => _boundKeys.TryGetValue(container, out var key) && _held.TryAdd(key, container);

    /// <summary>Takes back the container held under <paramref name="key"/>, if any.</summary>
    public bool TryTake(object? key, out FrameworkElement container)
    {
        if (key != null && _held.Remove(key, out var held))
        {
            container = held;
            return true;
        }

        container = null!;
        return false;
    }

    /// <summary>Hands every container still held to <paramref name="release"/> and empties the hold.</summary>
    public void Release(Action<FrameworkElement> release)
    {
        if (_held.Count == 0)
        {
            return;
        }

        var remaining = _held.Values.ToArray();
        _held.Clear();
        foreach (var container in remaining)
        {
            release(container);
        }
    }
}
