using System.Collections.Specialized;
using System.Runtime.CompilerServices;

using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Represents a hierarchical items view for <see cref="TreeView"/>, including expansion state and depth.
/// </summary>
public interface ITreeItemsView : ISelectableItemsView
{
    /// <summary>
    /// Gets the depth (indent level) for the item at <paramref name="index"/>.
    /// </summary>
    int GetDepth(int index);

    /// <summary>
    /// Gets whether the item at <paramref name="index"/> can expand and should display an expander.
    /// </summary>
    bool GetHasChildren(int index);

    /// <summary>
    /// Gets whether the item at <paramref name="index"/> is expanded.
    /// </summary>
    bool GetIsExpanded(int index);

    /// <summary>
    /// Sets the expanded state for the item at <paramref name="index"/>.
    /// </summary>
    void SetIsExpanded(int index, bool value);
}

/// <summary>
/// Helpers for creating <see cref="ITreeItemsView"/> instances.
/// </summary>
public static class TreeItemsView
{
    /// <summary>
    /// Gets an empty, non-null items view.
    /// </summary>
    public static ITreeItemsView Empty { get; } = new EmptyTreeItemsView();

    /// <summary>
    /// Creates a tree items view backed by a root list and a child selector.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="roots">Root items.</param>
    /// <param name="childrenSelector">Function that returns children for a given item.</param>
    /// <param name="textSelector">Function that returns display text for a given item.</param>
    /// <param name="keySelector">Function that returns a stable key for a given item (used for expansion/selection stability).</param>
    /// <param name="isExpandableSelector">
    /// Optional function that determines whether an item can expand independently of its currently
    /// loaded child count. When omitted, an item is expandable when its child collection is non-empty.
    /// </param>
    public static TreeItemsView<T> Create<T>(
        IReadOnlyList<T> roots,
        Func<T, IReadOnlyList<T>> childrenSelector,
        Func<T, string>? textSelector = null,
        Func<T, object?>? keySelector = null,
        Func<T, bool>? isExpandableSelector = null) =>
        new(roots, childrenSelector, textSelector, keySelector, isExpandableSelector);

    private sealed class EmptyTreeItemsView : ITreeItemsView
    {
        private int _selectedIndex = -1;

        public int Count => 0;
        public Func<object?, object?>? KeySelector => null;

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
        public int GetDepth(int index) => 0;
        public bool GetHasChildren(int index) => false;
        public bool GetIsExpanded(int index) => false;
        public void SetIsExpanded(int index, bool value) { }

        public void Invalidate() => Changed?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, 0));
    }
}

/// <summary>
/// Default <see cref="ITreeItemsView"/> implementation for a hierarchical data source.
/// </summary>
/// <typeparam name="T">Item type.</typeparam>
public sealed class TreeItemsView<T> : ITreeItemsView, IMultiSelectableItemsView
{
    private readonly Func<T, IReadOnlyList<T>> _childrenSelector;
    private readonly Func<T, string> _textSelector;
    private readonly Func<T, object?> _keySelector;
    private readonly Func<object?, object?> _keySelectorObject;
    private readonly Func<T, bool>? _isExpandableSelector;

    private readonly HashSet<object?> _expandedKeys = new();
    // Multi-selection is tracked by key (not index) so it survives expand/collapse rebuilds.
    private readonly HashSet<object?> _selectedKeys = new();
    private object? _anchorKey;
    private ItemsSelectionMode _selectionMode = ItemsSelectionMode.Single;
    private readonly List<Entry> _visible = new();
    private readonly HashSet<INotifyCollectionChanged> _subscribedChildCollections =
        new(ReferenceEqualityComparer<INotifyCollectionChanged>.Instance);
    private readonly HashSet<INotifyCollectionChanged> _requiredChildCollections =
        new(ReferenceEqualityComparer<INotifyCollectionChanged>.Instance);
    private readonly List<INotifyCollectionChanged> _removedChildCollections = new();

    private int _selectedIndex = -1;
    private object? _selectedKey;
    private bool _isRefreshing;
    private bool _refreshPending;

    public TreeItemsView(
        IReadOnlyList<T> roots,
        Func<T, IReadOnlyList<T>> childrenSelector,
        Func<T, string>? textSelector = null,
        Func<T, object?>? keySelector = null,
        Func<T, bool>? isExpandableSelector = null)
    {
        Roots = roots ?? throw new ArgumentNullException(nameof(roots));
        _childrenSelector = childrenSelector ?? throw new ArgumentNullException(nameof(childrenSelector));
        _textSelector = textSelector ?? (t => t?.ToString() ?? string.Empty);
        _keySelector = keySelector ?? (t => t);
        _keySelectorObject = obj => obj is T t ? _keySelector(t) : null;
        _isExpandableSelector = isExpandableSelector;

        RebuildVisible();

        if (roots is INotifyCollectionChanged ncc)
        {
            WeakEventManager.AddHandler<
                INotifyCollectionChanged,
                TreeItemsView<T>>(
                CollectionWeakEvents.CollectionChanged,
                ncc,
                this,
                static (view, _, _) => view.RefreshAndNotify());
        }
    }

    public IReadOnlyList<T> Roots { get; }

    /// <summary>
    /// Gets the number of currently visible rows (after applying expansion state).
    /// </summary>
    public int Count => _visible.Count;

    /// <summary>
    /// Gets a key selector for object-typed APIs (used by controls for selection stability).
    /// </summary>
    public Func<object?, object?>? KeySelector => _keySelectorObject;

    /// <summary>
    /// Gets or sets the selected visible-row index.
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
            _selectedKey = _selectedIndex >= 0 && _selectedIndex < Count ? _visible[_selectedIndex].Key : null;

            // Keep the multi-selection set consistent with single-selection usage.
            _selectedKeys.Clear();
            if (_selectedKey != null)
            {
                _selectedKeys.Add(_selectedKey);
            }
            _anchorKey = _selectedKey;

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
                CollapseSelectionToPrimary();
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<int> SelectedIndices
    {
        get
        {
            if (_selectedKeys.Count == 0)
            {
                return Array.Empty<int>();
            }

            var indices = new List<int>(_selectedKeys.Count);
            for (int i = 0; i < _visible.Count; i++)
            {
                if (_selectedKeys.Contains(_visible[i].Key))
                {
                    indices.Add(i);
                }
            }
            return indices;
        }
    }

    /// <inheritdoc />
    public int AnchorIndex => _anchorKey != null ? IndexOfKey(_anchorKey) : -1;

    /// <inheritdoc />
    public event Action? SelectedIndicesChanged;

    /// <inheritdoc />
    public bool IsSelected(int index)
        => index >= 0 && index < _visible.Count && _selectedKeys.Contains(_visible[index].Key);

    /// <inheritdoc />
    public void SelectSingle(int index)
    {
        _selectedKeys.Clear();
        if (index >= 0 && index < _visible.Count)
        {
            var key = _visible[index].Key;
            _selectedKeys.Add(key);
            _anchorKey = key;
            SetPrimaryIndex(index);
        }
        else
        {
            _anchorKey = null;
            SetPrimaryIndex(-1);
        }
        SelectedIndicesChanged?.Invoke();
    }

    /// <inheritdoc />
    public void ToggleSelected(int index)
    {
        if (index < 0 || index >= _visible.Count)
        {
            return;
        }

        var key = _visible[index].Key;
        if (!_selectedKeys.Remove(key))
        {
            _selectedKeys.Add(key);
        }
        _anchorKey = key;
        SetPrimaryIndex(ResolvePrimaryIndexAfterChange(index));
        SelectedIndicesChanged?.Invoke();
    }

    /// <inheritdoc />
    public void SetSelected(int index, bool selected)
    {
        if (index < 0 || index >= _visible.Count)
        {
            return;
        }

        var key = _visible[index].Key;
        bool changed = selected ? _selectedKeys.Add(key) : _selectedKeys.Remove(key);
        if (!changed)
        {
            return;
        }

        SetPrimaryIndex(ResolvePrimaryIndexAfterChange(index));
        SelectedIndicesChanged?.Invoke();
    }

    /// <inheritdoc />
    public void SelectRange(int anchorIndex, int targetIndex, bool clearExisting)
    {
        if (_visible.Count == 0)
        {
            return;
        }

        anchorIndex = ItemsView.ClampIndex(anchorIndex, _visible.Count);
        targetIndex = ItemsView.ClampIndex(targetIndex, _visible.Count);
        if (targetIndex < 0)
        {
            return;
        }

        if (clearExisting)
        {
            _selectedKeys.Clear();
        }

        if (anchorIndex < 0)
        {
            anchorIndex = targetIndex;
        }

        int low = Math.Min(anchorIndex, targetIndex);
        int high = Math.Max(anchorIndex, targetIndex);
        for (int i = low; i <= high; i++)
        {
            _selectedKeys.Add(_visible[i].Key);
        }

        SetPrimaryIndex(targetIndex);
        SelectedIndicesChanged?.Invoke();
    }

    /// <inheritdoc />
    public void ClearSelection()
    {
        if (_selectedKeys.Count == 0 && _selectedIndex < 0)
        {
            return;
        }

        _selectedKeys.Clear();
        _anchorKey = null;
        SetPrimaryIndex(-1);
        SelectedIndicesChanged?.Invoke();
    }

    // Sets the primary index and its key without resetting the multi-selection set.
    private void SetPrimaryIndex(int index)
    {
        if (_selectedIndex == index)
        {
            // Key may need refresh if the visible row at this index changed.
            _selectedKey = index >= 0 && index < _visible.Count ? _visible[index].Key : _selectedKey;
            return;
        }

        _selectedIndex = index;
        _selectedKey = index >= 0 && index < _visible.Count ? _visible[index].Key : null;
        SelectionChanged?.Invoke(_selectedIndex);
    }

    private int ResolvePrimaryIndexAfterChange(int preferred)
    {
        if (preferred >= 0 && preferred < _visible.Count && _selectedKeys.Contains(_visible[preferred].Key))
        {
            return preferred;
        }

        for (int i = 0; i < _visible.Count; i++)
        {
            if (_selectedKeys.Contains(_visible[i].Key))
            {
                return i;
            }
        }
        return -1;
    }

    private void CollapseSelectionToPrimary()
    {
        _selectedKeys.Clear();
        if (_selectedKey != null)
        {
            _selectedKeys.Add(_selectedKey);
            _anchorKey = _selectedKey;
        }
        else
        {
            _anchorKey = null;
        }
        SelectedIndicesChanged?.Invoke();
    }

    /// <summary>
    /// Gets or sets the selected item (typed).
    /// </summary>
    public T? SelectedItem
    {
        get => _selectedIndex >= 0 && _selectedIndex < Count ? _visible[_selectedIndex].Item : default;
        set
        {
            if (value is null)
            {
                SelectedIndex = -1;
                return;
            }

            var key = _keySelector(value);
            if (key == null)
            {
                SelectedIndex = -1;
                return;
            }

            int idx = IndexOfKey(key);
            if (idx >= 0)
            {
                SelectedIndex = idx;
            }
        }
    }

    object? ISelectableItemsView.SelectedItem
    {
        get => SelectedItem;
        set
        {
            if (value is null)
            {
                SelectedItem = default;
                return;
            }

            if (value is T t)
            {
                SelectedItem = t;
                return;
            }

            SelectedIndex = -1;
        }
    }

    public event Action<ItemsChange>? Changed;
    public event Action<int>? SelectionChanged;

    /// <summary>
    /// Gets the item at the given visible-row index.
    /// </summary>
    public object? GetItem(int index) => index >= 0 && index < _visible.Count ? _visible[index].Item : null;

    /// <summary>
    /// Gets the display text for the item at the given visible-row index.
    /// </summary>
    public string GetText(int index)
    {
        if (index < 0 || index >= _visible.Count)
        {
            return string.Empty;
        }

        return _textSelector(_visible[index].Item);
    }

    /// <summary>
    /// Gets the depth (indent level) for the given visible-row index.
    /// </summary>
    public int GetDepth(int index) => index >= 0 && index < _visible.Count ? _visible[index].Depth : 0;

    /// <summary>
    /// Gets whether the item at the given visible-row index has children.
    /// </summary>
    public bool GetHasChildren(int index)
    {
        if (index < 0 || index >= _visible.Count)
        {
            return false;
        }

        var entry = _visible[index];
        return _isExpandableSelector?.Invoke(entry.Item) ?? entry.Children.Count > 0;
    }

    /// <summary>
    /// Gets whether the item at the given visible-row index is expanded.
    /// </summary>
    public bool GetIsExpanded(int index)
    {
        if (index < 0 || index >= _visible.Count)
        {
            return false;
        }

        return _expandedKeys.Contains(_visible[index].Key);
    }

    /// <summary>
    /// Sets the expanded state for the item at the given visible-row index.
    /// </summary>
    public void SetIsExpanded(int index, bool value)
    {
        if (index < 0 || index >= _visible.Count)
        {
            return;
        }

        var key = _visible[index].Key;
        if (value)
        {
            if (!GetHasChildren(index))
            {
                return;
            }

            if (_expandedKeys.Add(key))
            {
                RefreshAndNotify();
            }
        }
        else
        {
            if (_expandedKeys.Remove(key))
            {
                RefreshAndNotify();
            }
        }
    }

    public void Invalidate() => RefreshAndNotify();

    private void RefreshAndNotify()
    {
        if (_isRefreshing)
        {
            _refreshPending = true;
            return;
        }

        do
        {
            _refreshPending = false;
            _isRefreshing = true;
            try
            {
                RebuildVisible();
                Changed?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, 0));
            }
            finally
            {
                _isRefreshing = false;
            }
        }
        while (_refreshPending);
    }

    private int IndexOfKey(object key)
    {
        for (int i = 0; i < _visible.Count; i++)
        {
            if (Equals(_visible[i].Key, key))
            {
                return i;
            }
        }

        return -1;
    }

    private void RebuildVisible()
    {
        _visible.Clear();
        _requiredChildCollections.Clear();

        for (int i = 0; i < Roots.Count; i++)
        {
            AddVisible(Roots[i], depth: 0);
        }

        UpdateChildCollectionSubscriptions();

        // Clamp selection after rebuild (best-effort key match).
        if (_selectedKey != null)
        {
            _selectedIndex = IndexOfKey(_selectedKey);
        }
        else
        {
            _selectedIndex = ItemsView.ClampIndex(_selectedIndex, _visible.Count);
        }
    }

    private void AddVisible(T item, int depth)
    {
        var key = _keySelector(item);
        var children = _childrenSelector(item) ?? Array.Empty<T>();
        _visible.Add(new Entry(item, key, depth, children));

        if (children is INotifyCollectionChanged observable)
        {
            _requiredChildCollections.Add(observable);
        }

        if (!_expandedKeys.Contains(key))
        {
            return;
        }

        if (children.Count == 0)
        {
            return;
        }

        for (int i = 0; i < children.Count; i++)
        {
            AddVisible(children[i], depth + 1);
        }
    }

    private void UpdateChildCollectionSubscriptions()
    {
        if (_subscribedChildCollections.Count > 0)
        {
            _removedChildCollections.Clear();
            foreach (var collection in _subscribedChildCollections)
            {
                if (!_requiredChildCollections.Contains(collection))
                {
                    _removedChildCollections.Add(collection);
                }
            }

            foreach (var collection in _removedChildCollections)
            {
                WeakEventManager.RemoveHandler(CollectionWeakEvents.CollectionChanged, collection, this);
                _subscribedChildCollections.Remove(collection);
            }
        }

        foreach (var collection in _requiredChildCollections)
        {
            if (_subscribedChildCollections.Contains(collection))
            {
                continue;
            }

            WeakEventManager.AddHandler<
                INotifyCollectionChanged,
                TreeItemsView<T>>(
                CollectionWeakEvents.CollectionChanged,
                collection,
                this,
                static (view, _, _) => view.RefreshAndNotify());
            _subscribedChildCollections.Add(collection);
        }
    }

    private readonly record struct Entry(
        T Item,
        object? Key,
        int Depth,
        IReadOnlyList<T> Children);
}

internal sealed class TreeViewNodeItemsView : ITreeItemsView
{
    private readonly IItemsView _roots;
    private readonly HashSet<TreeViewNode> _expanded = new(ReferenceEqualityComparer<TreeViewNode>.Instance);
    private readonly List<(TreeViewNode Node, int Depth)> _visible = new();
    private int _selectedIndex = -1;
    private TreeViewNode? _selectedNode;

    public TreeViewNodeItemsView(IItemsView roots)
    {
        _roots = roots ?? throw new ArgumentNullException(nameof(roots));
        _roots.Changed += OnRootsChanged;
        Rebuild();
    }

    public int Count => _visible.Count;

    public Func<object?, object?>? KeySelector => obj => obj is TreeViewNode n ? n : null;

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
            _selectedNode = _selectedIndex >= 0 && _selectedIndex < _visible.Count ? _visible[_selectedIndex].Node : null;
            SelectionChanged?.Invoke(_selectedIndex);
        }
    }

    public object? SelectedItem
    {
        get => _selectedNode;
        set
        {
            if (value == null)
            {
                SelectedIndex = -1;
                return;
            }

            if (value is not TreeViewNode node)
            {
                SelectedIndex = -1;
                return;
            }

            int idx = IndexOfNode(node);
            SelectedIndex = idx;
        }
    }

    public event Action<ItemsChange>? Changed;
    public event Action<int>? SelectionChanged;

    public object? GetItem(int index) => index >= 0 && index < _visible.Count ? _visible[index].Node : null;
    public string GetText(int index) => index >= 0 && index < _visible.Count ? _visible[index].Node.Text ?? string.Empty : string.Empty;
    public int GetDepth(int index) => index >= 0 && index < _visible.Count ? _visible[index].Depth : 0;
    public bool GetHasChildren(int index) => index >= 0 && index < _visible.Count && _visible[index].Node.HasChildren;
    public bool GetIsExpanded(int index) => index >= 0 && index < _visible.Count && _expanded.Contains(_visible[index].Node);

    public void SetIsExpanded(int index, bool value)
    {
        if (index < 0 || index >= _visible.Count)
        {
            return;
        }

        var node = _visible[index].Node;
        if (value)
        {
            if (_expanded.Add(node))
            {
                Rebuild();
                Changed?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, 0));
            }
        }
        else
        {
            if (_expanded.Remove(node))
            {
                Rebuild();
                Changed?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, 0));
            }
        }
    }

    public void Invalidate()
    {
        _roots.Invalidate();
        Rebuild();
        Changed?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, 0));
    }

    public bool IsExpanded(TreeViewNode node) => _expanded.Contains(node);

    public void Expand(TreeViewNode node)
    {
        if (_expanded.Add(node))
        {
            Rebuild();
            Changed?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, 0));
        }
    }

    public void Collapse(TreeViewNode node)
    {
        if (_expanded.Remove(node))
        {
            Rebuild();
            Changed?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, 0));
        }
    }

    private void OnRootsChanged(ItemsChange change)
    {
        Rebuild();
        Changed?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, 0));
    }

    private void Rebuild()
    {
        var selectedBefore = _selectedNode;

        _visible.Clear();
        int count = _roots.Count;
        for (int i = 0; i < count; i++)
        {
            if (_roots.GetItem(i) is TreeViewNode node)
            {
                AddVisible(node, 0);
            }
        }

        int oldIndex = _selectedIndex;
        if (selectedBefore != null)
        {
            _selectedIndex = IndexOfNode(selectedBefore);
            _selectedNode = _selectedIndex >= 0 ? selectedBefore : null;
        }
        else
        {
            _selectedIndex = ItemsView.ClampIndex(_selectedIndex, _visible.Count);
            _selectedNode = _selectedIndex >= 0 ? _visible[_selectedIndex].Node : null;
        }

        if (oldIndex != _selectedIndex)
        {
            SelectionChanged?.Invoke(_selectedIndex);
        }
    }

    private void AddVisible(TreeViewNode node, int depth)
    {
        _visible.Add((node, depth));
        if (!_expanded.Contains(node) || !node.HasChildren)
        {
            return;
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            AddVisible(node.Children[i], depth + 1);
        }
    }

    private int IndexOfNode(TreeViewNode node)
    {
        for (int i = 0; i < _visible.Count; i++)
        {
            if (ReferenceEquals(_visible[i].Node, node))
            {
                return i;
            }
        }

        return -1;
    }
}

internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
    where T : class
{
    public static ReferenceEqualityComparer<T> Instance { get; } = new();

    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

    public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
}
