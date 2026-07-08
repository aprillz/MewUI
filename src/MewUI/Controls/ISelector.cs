namespace Aprillz.MewUI.Controls;

/// <summary>
/// A control with a single current selection. Implemented by every selection control.
/// </summary>
public interface ISelector
{
    /// <summary>Gets or sets the selected item, or null when nothing is selected.</summary>
    object? SelectedItem { get; set; }

    /// <summary>Occurs when the selected item changes.</summary>
    event Action<object?>? SelectionChanged;
}

/// <summary>
/// A selection control whose items are addressed by a stable flat index. Implemented by the
/// list-like selectors; not by <see cref="TreeView"/>, whose selection is node-based.
/// </summary>
public interface IIndexedSelector : ISelector
{
    /// <summary>Gets or sets the selected index, or -1 when nothing is selected.</summary>
    int SelectedIndex { get; set; }
}

/// <summary>
/// A selection control that supports selecting multiple items at once.
/// </summary>
public interface IMultiSelector : ISelector
{
    /// <summary>Gets or sets the selection mode.</summary>
    ItemsSelectionMode SelectionMode { get; set; }

    /// <summary>Gets the selected indices in ascending order (read-only snapshot).</summary>
    IReadOnlyList<int> SelectedIndices { get; }

    /// <summary>Gets the selected items in ascending index order (read-only snapshot).</summary>
    IReadOnlyList<object?> SelectedItems { get; }

    /// <summary>Occurs when the set of selected items changes.</summary>
    event Action? SelectedIndicesChanged;

    /// <summary>Returns whether the item at <paramref name="index"/> is selected.</summary>
    bool IsSelected(int index);

    /// <summary>Selects every item (multi-selection modes only).</summary>
    void SelectAll();

    /// <summary>Clears the entire selection.</summary>
    void ClearSelection();

    /// <summary>Selects the inclusive range [<paramref name="start"/>, <paramref name="end"/>], replacing the selection.</summary>
    void SelectRange(int start, int end);
}
