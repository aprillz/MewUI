using System.Collections.Specialized;

namespace Aprillz.MewUI;

/// <summary>
/// Describes a change in an <see cref="IItemsView"/>.
/// </summary>
public enum ItemsChangeKind
{
    /// <summary>Collection contents changed in an unknown way.</summary>
    Reset = 0,
    /// <summary>Items were added.</summary>
    Add = 1,
    /// <summary>Items were removed.</summary>
    Remove = 2,
    /// <summary>Items were moved.</summary>
    Move = 3,
    /// <summary>Items were replaced in-place.</summary>
    Replace = 4,
}

/// <summary>
/// Represents a collection change notification for <see cref="IItemsView"/>.
/// </summary>
/// <param name="Kind">The kind of change.</param>
/// <param name="Index">The starting index affected by the change.</param>
/// <param name="Count">The number of items affected.</param>
/// <param name="OldIndex">For <see cref="ItemsChangeKind.Move"/>, the previous index; otherwise -1.</param>
public readonly record struct ItemsChange(
    ItemsChangeKind Kind,
    int Index,
    int Count,
    int OldIndex = -1);

/// <summary>
/// Provides a key/text-access abstraction over an item collection for list-like controls.
/// </summary>
public interface IItemsView
{
    /// <summary>
    /// Gets the number of items.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Returns the item object for a given index.
    /// </summary>
    /// <param name="index">Item index.</param>
    object? GetItem(int index);

    /// <summary>
    /// Returns the display text for a given index.
    /// </summary>
    /// <param name="index">Item index.</param>
    string GetText(int index);

    /// <summary>
    /// Optional selector that returns a stable key for a given item.
    /// When provided, selection/expansion can be preserved across collection changes by key.
    /// </summary>
    Func<object?, object?>? KeySelector { get; }

    /// <summary>
    /// Raised when the underlying collection changes.
    /// </summary>
    event Action<ItemsChange>? Changed;

    /// <summary>
    /// Forces a reset notification and re-applies selection policies.
    /// </summary>
    void Invalidate();
}

/// <summary>
/// Adds selection state to an <see cref="IItemsView"/>.
/// </summary>
public interface ISelectableItemsView : IItemsView
{
    /// <summary>
    /// Gets or sets the selected index (-1 means no selection).
    /// </summary>
    int SelectedIndex { get; set; }

    /// <summary>
    /// Gets or sets the selected item (may be <see langword="null"/>).
    /// </summary>
    object? SelectedItem { get; set; }

    /// <summary>
    /// Raised when the selection index changes.
    /// </summary>
    event Action<int>? SelectionChanged;
}

/// <summary>
/// Factory helpers for creating <see cref="IItemsView"/> instances.
/// </summary>
public static class ItemsView
{
    /// <summary>
    /// Gets an empty items view instance.
    /// </summary>
    /// <summary>
    /// Gets an empty selectable items view instance.
    /// </summary>
    public static ISelectableItemsView EmptySelectable { get; } = new EmptyItemsView();

    /// <summary>
    /// Gets an empty items view instance.
    /// </summary>
    public static IItemsView Empty { get; } = EmptySelectable;

    /// <summary>
    /// Creates an items view for a list of strings.
    /// </summary>
    /// <param name="items">Items collection.</param>
    public static ItemsView<string> Create(IReadOnlyList<string> items) => new(items, textSelector: s => s ?? string.Empty);

    /// <summary>
    /// Creates an items view for a list of items.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="items">Items collection.</param>
    /// <param name="textSelector">Optional display text selector.</param>
    /// <param name="keySelector">Optional stable key selector for selection preservation.</param>
    public static ItemsView<T> Create<T>(IReadOnlyList<T> items, Func<T, string>? textSelector = null, Func<T, object?>? keySelector = null) =>
        new(items, textSelector, keySelector);

    /// <summary>
    /// Wraps the legacy <see cref="ItemsSource"/> type into an <see cref="IItemsView"/>.
    /// </summary>
    /// <param name="source">Legacy items source.</param>
    public static ISelectableItemsView From(ItemsSource source) => source == null ? EmptySelectable : new LegacyItemsView(source);

    private sealed class EmptyItemsView : ISelectableItemsView
    {
        public int Count => 0;
        public Func<object?, object?>? KeySelector => null;
        private int _selectedIndex = -1;

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

        public object? SelectedItem { get => null; set => SelectedIndex = -1; }
        public event Action<ItemsChange>? Changed;
        public event Action<int>? SelectionChanged;

        public object? GetItem(int index) => null;
        public string GetText(int index) => string.Empty;

        public void Invalidate() => Changed?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, 0));
    }

    private sealed class LegacyItemsView : ISelectableItemsView
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

/// <summary>
/// A strongly-typed <see cref="IItemsView"/> that wraps an <see cref="IReadOnlyList{T}"/>.
/// </summary>
/// <typeparam name="T">Item type.</typeparam>
public sealed class ItemsView<T> : IMultiSelectableItemsView
{
    private readonly Func<T, string>? _textSelector;
    private readonly Func<T, object?>? _keySelector;
    private readonly Func<object?, object?>? _keySelectorObject;
    private int _selectedIndex = -1;
    private object? _selectedKey;
    private readonly SortedSet<int> _selectedSet = new();
    private int _anchorIndex = -1;
    private ItemsSelectionMode _selectionMode = ItemsSelectionMode.Single;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemsView{T}"/> class.
    /// </summary>
    /// <param name="items">Items collection.</param>
    /// <param name="textSelector">Optional display text selector.</param>
    /// <param name="keySelector">Optional stable key selector for selection preservation.</param>
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
            WeakEventManager.AddHandler<
                INotifyCollectionChanged,
                ItemsView<T>>(
                CollectionWeakEvents.CollectionChanged,
                ncc,
                this,
                static (view, _, args) => view.OnCollectionChanged(args));
        }
    }

    /// <summary>
    /// Gets the underlying items list.
    /// </summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>
    /// Gets the number of items.
    /// </summary>
    public int Count => Items.Count;

    /// <summary>
    /// Gets a boxed key selector, or <see langword="null"/> when no key selector was provided.
    /// </summary>
    public Func<object?, object?>? KeySelector => _keySelectorObject;

    /// <summary>
    /// Gets or sets the selected index (-1 means no selection).
    /// </summary>
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

            // Keep the multi-selection set consistent with single-selection usage.
            _selectedSet.Clear();
            if (clamped >= 0)
            {
                _selectedSet.Add(clamped);
            }
            _anchorIndex = clamped;

            SelectionChanged?.Invoke(_selectedIndex);
            SelectedIndicesChanged?.Invoke();
        }
    }

    /// <inheritdoc />
    public ItemsSelectionMode SelectionMode
    {
        get => _selectionMode;
        set
        {
            if (_selectionMode == value)
            {
                return;
            }

            _selectionMode = value;
            if (_selectionMode == ItemsSelectionMode.Single)
            {
                CollapseToPrimary();
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<int> SelectedIndices
    {
        get
        {
            if (_selectedSet.Count == 0)
            {
                return Array.Empty<int>();
            }

            var result = new int[_selectedSet.Count];
            _selectedSet.CopyTo(result);
            return result;
        }
    }

    /// <inheritdoc />
    public int AnchorIndex => _anchorIndex;

    /// <inheritdoc />
    public event Action? SelectedIndicesChanged;

    /// <inheritdoc />
    public bool IsSelected(int index) => _selectedSet.Contains(index);

    /// <inheritdoc />
    public void SelectSingle(int index)
    {
        index = ItemsView.ClampIndex(index, Count);
        _selectedSet.Clear();
        if (index >= 0)
        {
            _selectedSet.Add(index);
        }
        _anchorIndex = index;
        SetPrimary(index);
        SelectedIndicesChanged?.Invoke();
    }

    /// <inheritdoc />
    public void ToggleSelected(int index)
    {
        if (index < 0 || index >= Count)
        {
            return;
        }

        if (!_selectedSet.Remove(index))
        {
            _selectedSet.Add(index);
        }
        _anchorIndex = index;
        SetPrimary(ResolvePrimaryAfterChange(index));
        SelectedIndicesChanged?.Invoke();
    }

    /// <inheritdoc />
    public void SetSelected(int index, bool selected)
    {
        if (index < 0 || index >= Count)
        {
            return;
        }

        bool changed = selected ? _selectedSet.Add(index) : _selectedSet.Remove(index);
        if (!changed)
        {
            return;
        }

        SetPrimary(ResolvePrimaryAfterChange(index));
        SelectedIndicesChanged?.Invoke();
    }

    /// <inheritdoc />
    public void SelectRange(int anchorIndex, int targetIndex, bool clearExisting)
    {
        if (Count == 0)
        {
            return;
        }

        anchorIndex = ItemsView.ClampIndex(anchorIndex, Count);
        targetIndex = ItemsView.ClampIndex(targetIndex, Count);
        if (targetIndex < 0)
        {
            return;
        }

        if (clearExisting)
        {
            _selectedSet.Clear();
        }

        if (anchorIndex < 0)
        {
            anchorIndex = targetIndex;
        }

        int low = Math.Min(anchorIndex, targetIndex);
        int high = Math.Max(anchorIndex, targetIndex);
        for (int index = low; index <= high; index++)
        {
            _selectedSet.Add(index);
        }

        SetPrimary(targetIndex);
        SelectedIndicesChanged?.Invoke();
    }

    /// <inheritdoc />
    public void ClearSelection()
    {
        if (_selectedSet.Count == 0 && _selectedIndex < 0)
        {
            return;
        }

        _selectedSet.Clear();
        _anchorIndex = -1;
        SetPrimary(-1);
        SelectedIndicesChanged?.Invoke();
    }

    // Sets the primary index (and its preservation key) without touching the set, firing SelectionChanged on change.
    private void SetPrimary(int index)
    {
        if (_selectedIndex == index)
        {
            return;
        }

        _selectedIndex = index;
        _selectedKey = index >= 0 && index < Count && _keySelector != null ? _keySelector(Items[index]) : null;
        SelectionChanged?.Invoke(index);
    }

    private int ResolvePrimaryAfterChange(int preferred)
    {
        if (_selectedSet.Contains(preferred))
        {
            return preferred;
        }
        return _selectedSet.Count > 0 ? _selectedSet.Min : -1;
    }

    private void CollapseToPrimary()
    {
        _selectedSet.Clear();
        if (_selectedIndex >= 0 && _selectedIndex < Count)
        {
            _selectedSet.Add(_selectedIndex);
            _anchorIndex = _selectedIndex;
        }
        else
        {
            _anchorIndex = -1;
        }
        SelectedIndicesChanged?.Invoke();
    }

    // Remaps the multi-selection set after a collection change (v1: index-shift Add/Remove; clear otherwise).
    private void RemapSelectedSet(NotifyCollectionChangedAction action, int start, int count)
    {
        if (_selectedSet.Count == 0)
        {
            return;
        }

        var snapshot = SelectedIndices;
        _selectedSet.Clear();

        switch (action)
        {
            case NotifyCollectionChangedAction.Add:
                foreach (int index in snapshot)
                {
                    _selectedSet.Add(index >= start ? index + count : index);
                }
                if (_anchorIndex >= start)
                {
                    _anchorIndex += count;
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                int end = start + count;
                foreach (int index in snapshot)
                {
                    if (index < start)
                    {
                        _selectedSet.Add(index);
                    }
                    else if (index >= end)
                    {
                        _selectedSet.Add(index - count);
                    }
                }
                if (_anchorIndex >= end)
                {
                    _anchorIndex -= count;
                }
                else if (_anchorIndex >= start)
                {
                    _anchorIndex = Math.Max(0, start - 1);
                }
                break;

            default:
                // Move/Replace/Reset: best-effort preserve by index, dropping any now out of range.
                foreach (int index in snapshot)
                {
                    if (index < Count)
                    {
                        _selectedSet.Add(index);
                    }
                }
                _anchorIndex = ItemsView.ClampIndex(_anchorIndex, Count);
                break;
        }

        SelectedIndicesChanged?.Invoke();
    }

    private void RemapSelectedSetForChange(NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewStartingIndex >= 0:
                RemapSelectedSet(NotifyCollectionChangedAction.Add, e.NewStartingIndex, e.NewItems?.Count ?? 1);
                break;
            case NotifyCollectionChangedAction.Remove when e.OldStartingIndex >= 0:
                RemapSelectedSet(NotifyCollectionChangedAction.Remove, e.OldStartingIndex, e.OldItems?.Count ?? 1);
                break;
            case NotifyCollectionChangedAction.Replace:
                break; // indices and membership unchanged
            default:
                RemapSelectedSet(NotifyCollectionChangedAction.Reset, 0, 0);
                break;
        }
    }

    /// <summary>
    /// Gets or sets the selected item.
    /// </summary>
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

    object? ISelectableItemsView.SelectedItem
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

    /// <summary>
    /// Raised when the underlying collection changes.
    /// </summary>
    public event Action<ItemsChange>? Changed;

    /// <summary>
    /// Raised when the selection index changes.
    /// </summary>
    public event Action<int>? SelectionChanged;

    /// <summary>
    /// Gets the item object at the specified index.
    /// </summary>
    /// <param name="index">Item index.</param>
    public object? GetItem(int index) => Items[index];

    /// <summary>
    /// Gets the display text for the item at the specified index.
    /// </summary>
    /// <param name="index">Item index.</param>
    public string GetText(int index)
    {
        var item = Items[index];
        if (_textSelector != null)
        {
            return _textSelector(item) ?? string.Empty;
        }

        return item?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Forces a reset notification and re-applies selection policies.
    /// </summary>
    public void Invalidate()
    {
        ApplyResetSelectionPolicy();
        RemapSelectedSet(NotifyCollectionChangedAction.Reset, 0, 0);
        ReconcilePrimaryAndSet();
        Changed?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, 0));
    }

    private void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        ApplySelectionChange(e);
        RemapSelectedSetForChange(e);
        ReconcilePrimaryAndSet();
        Changed?.Invoke(ToItemsChange(e));
    }

    // Keeps the primary and the selection set consistent after a collection change:
    // the primary (when valid) is always a member of the set, and an empty set means no primary.
    private void ReconcilePrimaryAndSet()
    {
        if (_selectedSet.Count == 0)
        {
            if (_selectedIndex >= 0 && _selectedIndex < Count)
            {
                _selectedSet.Add(_selectedIndex);
                SelectedIndicesChanged?.Invoke();
            }
            return;
        }

        if (_selectedIndex < 0 || _selectedIndex >= Count || !_selectedSet.Contains(_selectedIndex))
        {
            SetPrimary(_selectedSet.Min);
        }
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
            SetPrimary(found >= 0 ? found : ItemsView.ClampIndex(_selectedIndex, Count));
            return;
        }

        int count;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                count = e.NewItems?.Count ?? 1;
                if (_selectedIndex >= e.NewStartingIndex && e.NewStartingIndex >= 0)
                {
                    SetPrimary(_selectedIndex + count);
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                count = e.OldItems?.Count ?? 1;
                if (e.OldStartingIndex >= 0)
                {
                    if (_selectedIndex >= e.OldStartingIndex && _selectedIndex < e.OldStartingIndex + count)
                    {
                        SetPrimary(ItemsView.ClampIndex(e.OldStartingIndex, Count));
                    }
                    else if (_selectedIndex >= e.OldStartingIndex + count)
                    {
                        SetPrimary(_selectedIndex - count);
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
                        SetPrimary(newIndex);
                    }
                    else if (oldIndex < _selectedIndex && _selectedIndex <= newIndex)
                    {
                        SetPrimary(_selectedIndex - 1);
                    }
                    else if (newIndex <= _selectedIndex && _selectedIndex < oldIndex)
                    {
                        SetPrimary(_selectedIndex + 1);
                    }
                }
                else
                {
                    ApplyResetSelectionPolicy();
                }
                break;

            case NotifyCollectionChangedAction.Replace:
                SetPrimary(ItemsView.ClampIndex(_selectedIndex, Count));
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
            SetPrimary(found >= 0 ? found : ItemsView.ClampIndex(_selectedIndex, Count));
            return;
        }

        SetPrimary(ItemsView.ClampIndex(_selectedIndex, Count));
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
