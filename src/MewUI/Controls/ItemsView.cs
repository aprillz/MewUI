using System.Collections.Specialized;

namespace Aprillz.MewUI;

public enum ItemsChangeKind
{
    Reset = 0,
    Add = 1,
    Remove = 2,
    Move = 3,
    Replace = 4,
}

public readonly record struct ItemsChange(
    ItemsChangeKind Kind,
    int Index,
    int Count,
    int OldIndex = -1);

public interface IItemsView
{
    int Count { get; }
    object? GetItem(int index);
    string GetText(int index);

    Func<object?, object?>? KeySelector { get; }

    int SelectedIndex { get; set; }
    object? SelectedItem { get; set; }

    event Action<ItemsChange>? Changed;
    event Action<int>? SelectionChanged;

    void Invalidate();
}

public static class ItemsView
{
    public static IItemsView Empty { get; } = new EmptyItemsView();

    public static ItemsView<string> Create(IReadOnlyList<string> items) => new(items, textSelector: s => s ?? string.Empty);

    public static ItemsView<T> Create<T>(IReadOnlyList<T> items, Func<T, string>? textSelector = null, Func<T, object?>? keySelector = null) =>
        new(items, textSelector, keySelector);

    public static IItemsView From(ItemsSource source) => source == null ? Empty : new LegacyItemsView(source);

    private sealed class EmptyItemsView : IItemsView
    {
        public int Count => 0;
        public Func<object?, object?>? KeySelector => null;
        private int _selectedIndex = -1;

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                int clamped = ItemsView.ClampIndex(value, Count);
                if (_selectedIndex == clamped)
                {
                    return;
                }

                _selectedIndex = clamped;
                SelectionChanged?.Invoke(_selectedIndex);
            }
        }

        public object? SelectedItem { get => null; set => SelectedIndex = -1; }
        public event Action<ItemsChange>? Changed;
        public event Action<int>? SelectionChanged;

        public object? GetItem(int index) => null;
        public string GetText(int index) => string.Empty;

        public void Invalidate() => Changed?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, 0));
    }

    private sealed class LegacyItemsView : IItemsView
    {
        private readonly ItemsSource _source;
        private int _selectedIndex = -1;

        public LegacyItemsView(ItemsSource source)
        {
            _source = source;
        }

        public int Count => _source.Count;
        public Func<object?, object?>? KeySelector => null;

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                int clamped = ClampIndex(value, Count);
                if (_selectedIndex == clamped)
                {
                    return;
                }

                _selectedIndex = clamped;
                SelectionChanged?.Invoke(_selectedIndex);
            }
        }

        public object? SelectedItem
        {
            get => _selectedIndex >= 0 && _selectedIndex < Count ? _source.GetItem(_selectedIndex) : null;
            set
            {
                if (value == null)
                {
                    SelectedIndex = -1;
                    return;
                }

                int count = Count;
                for (int i = 0; i < count; i++)
                {
                    var item = _source.GetItem(i);
                    if (ReferenceEquals(item, value) || Equals(item, value))
                    {
                        SelectedIndex = i;
                        return;
                    }
                }
            }
        }

        public event Action<ItemsChange>? Changed;
        public event Action<int>? SelectionChanged;

        public object? GetItem(int index) => _source.GetItem(index);
        public string GetText(int index) => _source.GetText(index);

        public void Invalidate()
        {
            SelectedIndex = _selectedIndex;
            Changed?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, 0));
        }
    }

    internal static int ClampIndex(int value, int count)
    {
        if (count <= 0)
        {
            return -1;
        }

        return Math.Clamp(value, -1, count - 1);
    }
}

public sealed class ItemsView<T> : IItemsView
{
    private readonly Func<T, string>? _textSelector;
    private readonly Func<T, object?>? _keySelector;
    private readonly Func<object?, object?>? _keySelectorObject;
    private int _selectedIndex = -1;
    private object? _selectedKey;

    public ItemsView(IReadOnlyList<T> items, Func<T, string>? textSelector = null, Func<T, object?>? keySelector = null)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        _textSelector = textSelector;
        _keySelector = keySelector;
        if (keySelector != null)
        {
            _keySelectorObject = obj => obj is T t ? keySelector(t) : null;
        }

        if (items is INotifyCollectionChanged ncc)
        {
            NotifyCollectionChangedEventHandler? handler = null;
            var weak = new WeakReference<ItemsView<T>>(this);
            handler = (s, e) =>
            {
                if (!weak.TryGetTarget(out var self))
                {
                    ncc.CollectionChanged -= handler!;
                    return;
                }

                self.OnCollectionChanged(e);
            };
            ncc.CollectionChanged += handler;
        }
    }

    public IReadOnlyList<T> Items { get; }

    public int Count => Items.Count;

    public Func<object?, object?>? KeySelector => _keySelectorObject;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int clamped = ItemsView.ClampIndex(value, Count);
            if (_selectedIndex == clamped)
            {
                return;
            }

            _selectedIndex = clamped;
            _selectedKey = _selectedIndex >= 0 && _selectedIndex < Count && _keySelector != null
                ? _keySelector(Items[_selectedIndex])
                : null;
            SelectionChanged?.Invoke(_selectedIndex);
        }
    }

    public T? SelectedItem
    {
        get => _selectedIndex >= 0 && _selectedIndex < Count ? Items[_selectedIndex] : default;
        set
        {
            if (value == null)
            {
                SelectedIndex = -1;
                return;
            }

            int idx = IndexOf(value);
            if (idx >= 0)
            {
                SelectedIndex = idx;
            }
        }
    }

    object? IItemsView.SelectedItem
    {
        get => _selectedIndex >= 0 && _selectedIndex < Count ? Items[_selectedIndex] : null;
        set
        {
            if (value == null)
            {
                SelectedIndex = -1;
                return;
            }

            if (value is T t)
            {
                SelectedItem = t;
                return;
            }

            int count = Count;
            for (int i = 0; i < count; i++)
            {
                var item = Items[i];
                if (item != null && Equals(item, value))
                {
                    SelectedIndex = i;
                    return;
                }
            }
        }
    }

    public event Action<ItemsChange>? Changed;
    public event Action<int>? SelectionChanged;

    public object? GetItem(int index) => Items[index];

    public string GetText(int index)
    {
        var item = Items[index];
        if (_textSelector != null)
        {
            return _textSelector(item) ?? string.Empty;
        }

        return item?.ToString() ?? string.Empty;
    }

    public void Invalidate()
    {
        ApplyResetSelectionPolicy();
        Changed?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, 0));
    }

    private void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        ApplySelectionChange(e);
        Changed?.Invoke(ToItemsChange(e));
    }

    private ItemsChange ToItemsChange(NotifyCollectionChangedEventArgs e)
    {
        return e.Action switch
        {
            NotifyCollectionChangedAction.Add => new ItemsChange(ItemsChangeKind.Add, e.NewStartingIndex, e.NewItems?.Count ?? 1),
            NotifyCollectionChangedAction.Remove => new ItemsChange(ItemsChangeKind.Remove, e.OldStartingIndex, e.OldItems?.Count ?? 1),
            NotifyCollectionChangedAction.Move => new ItemsChange(ItemsChangeKind.Move, e.NewStartingIndex, e.NewItems?.Count ?? 1, e.OldStartingIndex),
            NotifyCollectionChangedAction.Replace => new ItemsChange(ItemsChangeKind.Replace, e.NewStartingIndex, e.NewItems?.Count ?? 1),
            _ => new ItemsChange(ItemsChangeKind.Reset, 0, 0),
        };
    }

    private void ApplySelectionChange(NotifyCollectionChangedEventArgs e)
    {
        if (_selectedIndex < 0)
        {
            return;
        }

        if (_keySelector != null && _selectedKey != null)
        {
            int found = FindIndexByKey(_selectedKey);
            SelectedIndex = found >= 0 ? found : ItemsView.ClampIndex(_selectedIndex, Count);
            return;
        }

        int count;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                count = e.NewItems?.Count ?? 1;
                if (_selectedIndex >= e.NewStartingIndex && e.NewStartingIndex >= 0)
                {
                    SelectedIndex = _selectedIndex + count;
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                count = e.OldItems?.Count ?? 1;
                if (e.OldStartingIndex >= 0)
                {
                    if (_selectedIndex >= e.OldStartingIndex && _selectedIndex < e.OldStartingIndex + count)
                    {
                        SelectedIndex = ItemsView.ClampIndex(e.OldStartingIndex, Count);
                    }
                    else if (_selectedIndex >= e.OldStartingIndex + count)
                    {
                        SelectedIndex = _selectedIndex - count;
                    }
                }
                break;

            case NotifyCollectionChangedAction.Move:
                count = e.NewItems?.Count ?? 1;
                if (count == 1 && e.OldStartingIndex >= 0 && e.NewStartingIndex >= 0)
                {
                    int oldIndex = e.OldStartingIndex;
                    int newIndex = e.NewStartingIndex;
                    if (_selectedIndex == oldIndex)
                    {
                        SelectedIndex = newIndex;
                    }
                    else if (oldIndex < _selectedIndex && _selectedIndex <= newIndex)
                    {
                        SelectedIndex = _selectedIndex - 1;
                    }
                    else if (newIndex <= _selectedIndex && _selectedIndex < oldIndex)
                    {
                        SelectedIndex = _selectedIndex + 1;
                    }
                }
                else
                {
                    ApplyResetSelectionPolicy();
                }
                break;

            case NotifyCollectionChangedAction.Replace:
                SelectedIndex = ItemsView.ClampIndex(_selectedIndex, Count);
                break;

            default:
                ApplyResetSelectionPolicy();
                break;
        }
    }

    private void ApplyResetSelectionPolicy()
    {
        if (_selectedIndex < 0)
        {
            return;
        }

        if (_keySelector != null && _selectedKey != null)
        {
            int found = FindIndexByKey(_selectedKey);
            SelectedIndex = found >= 0 ? found : ItemsView.ClampIndex(_selectedIndex, Count);
            return;
        }

        SelectedIndex = ItemsView.ClampIndex(_selectedIndex, Count);
    }

    private int FindIndexByKey(object key)
    {
        int count = Count;
        for (int i = 0; i < count; i++)
        {
            var item = Items[i];
            var itemKey = _keySelector!(item);
            if (Equals(itemKey, key))
            {
                return i;
            }
        }

        return -1;
    }

    private int IndexOf(T item)
    {
        int count = Count;
        var cmp = EqualityComparer<T>.Default;
        for (int i = 0; i < count; i++)
        {
            if (cmp.Equals(Items[i], item))
            {
                return i;
            }
        }

        if (_keySelector != null)
        {
            var key = _keySelector(item);
            if (key != null)
            {
                return FindIndexByKey(key);
            }
        }

        return -1;
    }
}
