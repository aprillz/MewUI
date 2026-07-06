namespace Aprillz.MewUI;

/// <summary>
/// Item selection mode for list-like controls (WPF/Avalonia standard).
/// </summary>
public enum ItemsSelectionMode
{
    /// <summary>A single item at a time.</summary>
    Single,
    /// <summary>Each plain click toggles that item; Shift+click adds a range (no modifier required).</summary>
    Multiple,
    /// <summary>Click selects one; Ctrl+click toggles; Shift+click selects a range; Ctrl+Shift+click adds a range.</summary>
    Extended,
}

/// <summary>
/// Extends <see cref="ISelectableItemsView"/> with a multi-selection set. The inherited
/// <see cref="ISelectableItemsView.SelectedIndex"/> acts as the primary (last-acted / focus) index and the
/// range anchor base; <see cref="SelectedIndices"/> carries the full set.
/// <para>Implemented opt-in (only by views that support it) so existing single-selection paths are unaffected.</para>
/// </summary>
public interface IMultiSelectableItemsView : ISelectableItemsView
{
    /// <summary>Gets or sets the selection mode. Switching to <see cref="ItemsSelectionMode.Single"/> collapses the set to the primary.</summary>
    ItemsSelectionMode SelectionMode { get; set; }

    /// <summary>Gets the selected indices in ascending order (snapshot).</summary>
    IReadOnlyList<int> SelectedIndices { get; }

    /// <summary>Gets the range anchor index (the base for Shift-range operations), or -1 when none.</summary>
    int AnchorIndex { get; }

    /// <summary>Raised when the selected set changes.</summary>
    event Action? SelectedIndicesChanged;

    /// <summary>Returns whether the item at <paramref name="index"/> is selected.</summary>
    bool IsSelected(int index);

    /// <summary>Clears the set and selects only <paramref name="index"/>, setting it as primary and anchor.</summary>
    void SelectSingle(int index);

    /// <summary>Adds or removes <paramref name="index"/> from the set, setting it as primary and anchor.</summary>
    void ToggleSelected(int index);

    /// <summary>Explicitly sets membership of <paramref name="index"/> without changing the anchor.</summary>
    void SetSelected(int index, bool selected);

    /// <summary>
    /// Selects the inclusive range from <paramref name="anchorIndex"/> to <paramref name="targetIndex"/>.
    /// When <paramref name="clearExisting"/> is true the set is replaced; otherwise the range is added.
    /// The primary becomes <paramref name="targetIndex"/>; the anchor is preserved.
    /// </summary>
    void SelectRange(int anchorIndex, int targetIndex, bool clearExisting);

    /// <summary>Clears the entire selection.</summary>
    void ClearSelection();
}

/// <summary>
/// Shared input policy that maps a click (with modifiers) or a keyboard move onto an
/// <see cref="IMultiSelectableItemsView"/>, per <see cref="ItemsSelectionMode"/>. Keeps the Single/Multiple/Extended
/// semantics in one place so GridView/ListBox/TreeView do not each reimplement it.
/// </summary>
public static class ItemsSelectionInput
{
    /// <summary>Applies a pointer click at <paramref name="index"/> with the given modifiers.</summary>
    public static void HandleClick(IMultiSelectableItemsView view, int index, ModifierKeys modifiers)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (index < 0)
        {
            return;
        }

        bool control = (modifiers & (ModifierKeys.Control | ModifierKeys.Meta)) != 0;
        bool shift = (modifiers & ModifierKeys.Shift) != 0;

        switch (view.SelectionMode)
        {
            case ItemsSelectionMode.Single:
                view.SelectSingle(index);
                break;

            case ItemsSelectionMode.Multiple:
                if (shift && view.AnchorIndex >= 0)
                {
                    view.SelectRange(view.AnchorIndex, index, clearExisting: false);
                }
                else
                {
                    view.ToggleSelected(index);
                }
                break;

            default: // Extended
                if (shift && view.AnchorIndex >= 0)
                {
                    view.SelectRange(view.AnchorIndex, index, clearExisting: !control);
                }
                else if (control)
                {
                    view.ToggleSelected(index);
                }
                else
                {
                    view.SelectSingle(index);
                }
                break;
        }
    }

    /// <summary>Applies a keyboard move to <paramref name="targetIndex"/>, optionally extending the range with Shift.</summary>
    public static void HandleKeyboardMove(IMultiSelectableItemsView view, int targetIndex, bool extend)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (targetIndex < 0)
        {
            return;
        }

        if (extend && view.SelectionMode != ItemsSelectionMode.Single && view.AnchorIndex >= 0)
        {
            view.SelectRange(view.AnchorIndex, targetIndex, clearExisting: true);
        }
        else
        {
            view.SelectSingle(targetIndex);
        }
    }
}
