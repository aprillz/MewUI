namespace Aprillz.MewUI.Controls;

/// <summary>
/// Selection queries over <see cref="ISelectableItemsView"/>, bridging the single- and
/// multi-selection interfaces. Lives with the view interfaces rather than on any control base,
/// since both scrollable (ListBox/GridView) and non-scrollable (TreeView) controls share them.
/// </summary>
internal static class ItemsSelection
{
    /// <summary>Casts to the multi-selection interface, or null when the view is single-selection only.</summary>
    public static IMultiSelectableItemsView? AsMultiSelectable(this ISelectableItemsView itemsSource)
        => itemsSource as IMultiSelectableItemsView;

    /// <summary>Gets the selection mode, or <see cref="ItemsSelectionMode.Single"/> when multi-selection is unsupported.</summary>
    public static ItemsSelectionMode GetSelectionMode(this ISelectableItemsView itemsSource)
        => itemsSource.AsMultiSelectable()?.SelectionMode ?? ItemsSelectionMode.Single;

    /// <summary>Sets the selection mode; a no-op when multi-selection is unsupported.</summary>
    public static void SetSelectionMode(this ISelectableItemsView itemsSource, ItemsSelectionMode value)
    {
        var multi = itemsSource.AsMultiSelectable();
        if (multi != null)
        {
            multi.SelectionMode = value;
        }
    }

    /// <summary>Gets the selected indices, falling back to a single-element array from SelectedIndex.</summary>
    public static IReadOnlyList<int> GetSelectedIndices(this ISelectableItemsView itemsSource)
    {
        var multi = itemsSource.AsMultiSelectable();
        if (multi != null)
        {
            return multi.SelectedIndices;
        }
        return itemsSource.SelectedIndex >= 0 ? new[] { itemsSource.SelectedIndex } : Array.Empty<int>();
    }

    /// <summary>Returns whether the item at <paramref name="index"/> is selected.</summary>
    public static bool IsItemSelected(this ISelectableItemsView itemsSource, int index)
    {
        var multi = itemsSource.AsMultiSelectable();
        return multi != null ? multi.IsSelected(index) : index == itemsSource.SelectedIndex;
    }
}
