using System.Collections.Specialized;
using System.Runtime.CompilerServices;

using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Represents a hierarchical items view for <see cref="TreeView"/>, including expansion state and depth.
/// </summary>
public interface ITreeItemsView : IItemsView
{
    /// <summary>
    /// Gets the depth (indent level) for the item at <paramref name="index"/>.
    /// </summary>
    int GetDepth(int index);

    /// <summary>
    /// Gets whether the item at <paramref name="index"/> has children.
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
    public static TreeItemsView<T> Create<T>(
        IReadOnlyList<T> roots,
        Func<T, IReadOnlyList<T>> childrenSelector,
        Func<T, string>? textSelector = null,
        Func<T, object?>? keySelector = null) =>
        new(roots, childrenSelector, textSelector, keySelector);

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
public sealed class TreeItemsView<T> : ITreeItemsView
{
    private readonly Func<T, IReadOnlyList<T>> _childrenSelector;
    private readonly Func<T, string> _textSelector;
    private readonly Func<T, object?> _keySelector;
    private readonly Func<object?, object?> _keySelectorObject;

    private readonly HashSet<object?> _expandedKeys = new();
    private readonly List<Entry> _visible = new();

    private int _selectedIndex = -1;
    private object? _selectedKey;

    public TreeItemsView(
        IReadOnlyList<T> roots,
        Func<T, IReadOnlyList<T>> childrenSelector,
        Func<T, string>? textSelector = null,
        Func<T, object?>? keySelector = null)
    {
        Roots = roots ?? throw new ArgumentNullException(nameof(roots));
        _childrenSelector = childrenSelector ?? throw new ArgumentNullException(nameof(childrenSelector));
        _textSelector = textSelector ?? (t => t?.ToString() ?? string.Empty);
        _keySelector = keySelector ?? (t => t);
        _keySelectorObject = obj => obj is T t ? _keySelector(t) : null;

        RebuildVisible();

        if (roots is INotifyCollectionChanged ncc)
        {
            NotifyCollectionChangedEventHandler? handler = null;
            var weak = new WeakReference<TreeItemsView<T>>(this);
            handler = (_, e) =>
            {
                if (!weak.TryGetTarget(out var self))
                {
                    ncc.CollectionChanged -= handler!;
                    return;
                }

                // For now, treat any structural change as reset.
                self.RebuildVisible();
                self.Changed?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, 0));
            };
            ncc.CollectionChanged += handler;
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
            SelectionChanged?.Invoke(_selectedIndex);
        }
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

    object? IItemsView.SelectedItem
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

        var children = _childrenSelector(_visible[index].Item);
        return children != null && children.Count > 0;
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
            if (_expandedKeys.Add(key))
            {
                RebuildVisible();
                Changed?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, 0));
            }
        }
        else
        {
            if (_expandedKeys.Remove(key))
            {
                RebuildVisible();
                Changed?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, 0));
            }
        }
    }

    public void Invalidate()
    {
        // Selection might need to be re-resolved when items move.
        if (_selectedKey != null)
        {
            int idx = IndexOfKey(_selectedKey);
            _selectedIndex = idx;
        }
        else
        {
            _selectedIndex = ItemsView.ClampIndex(_selectedIndex, Count);
        }

        Changed?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, 0));
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
        for (int i = 0; i < Roots.Count; i++)
        {
            AddVisible(Roots[i], depth: 0);
        }

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
        _visible.Add(new Entry(item, key, depth));

        if (!_expandedKeys.Contains(key))
        {
            return;
        }

        var children = _childrenSelector(item);
        if (children == null || children.Count == 0)
        {
            return;
        }

        for (int i = 0; i < children.Count; i++)
        {
            AddVisible(children[i], depth + 1);
        }
    }

    private readonly record struct Entry(T Item, object? Key, int Depth);
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
