namespace Aprillz.MewUI.Controls;

/// <summary>
/// Shared single/multi selection sync between an <see cref="ISelectableItemsView"/> model and a
/// control's bindable SelectedIndex / SelectedItem / SelectedItems properties. Centralizes the
/// reentrancy guard, the "sync properties before the owner raises events" ordering, the SelectedItems
/// projection, and the multi-selection operations that the view-backed selectors otherwise duplicate.
/// The setSelectedItems setter is null for single-selection controls that do not expose SelectedItems.
/// </summary>
internal sealed class SelectionSync
{
    private readonly Func<ISelectableItemsView> _view;
    private readonly Action<int> _setSelectedIndex;
    private readonly Action<object?> _setSelectedItem;
    private readonly Action<IReadOnlyList<object?>>? _setSelectedItems;
    private bool _syncing;

    public SelectionSync(
        Func<ISelectableItemsView> view,
        Action<int> setSelectedIndex,
        Action<object?> setSelectedItem,
        Action<IReadOnlyList<object?>>? setSelectedItems = null)
    {
        _view = view;
        _setSelectedIndex = setSelectedIndex;
        _setSelectedItem = setSelectedItem;
        _setSelectedItems = setSelectedItems;
    }

    /// <summary>True while a sync is in progress; property change callbacks should no-op.</summary>
    public bool Syncing => _syncing;

    public IReadOnlyList<int> SelectedIndices => _view().GetSelectedIndices();

    public bool IsSelected(int index) => _view().IsItemSelected(index);

    /// <summary>Pushes a selected-index set from the bindable property down to the model.</summary>
    public void PushIndex(int index)
    {
        if (_syncing) return;
        _syncing = true;
        try { _view().SelectedIndex = index; }
        finally { _syncing = false; }
        SyncFromModel();
    }

    /// <summary>Pushes a selected-item set from the bindable property down to the model.</summary>
    public void PushItem(object? item)
    {
        if (_syncing) return;
        _syncing = true;
        try { _view().SelectedItem = item; }
        finally { _syncing = false; }
        SyncFromModel();
    }

    /// <summary>
    /// Mirrors the model selection into the bindable properties. Uses save/restore (not early-return)
    /// so it also runs when nested inside a guarded push, keeping the getters fresh before the owner
    /// raises its change events. The property callbacks keep their own guard against reentry.
    /// </summary>
    public void SyncFromModel()
    {
        var view = _view();
        bool wasSyncing = _syncing;
        _syncing = true;
        try
        {
            _setSelectedIndex(view.SelectedIndex);
            _setSelectedItem(view.SelectedItem);
        }
        finally { _syncing = wasSyncing; }
        RefreshItems(view);
    }

    private void RefreshItems(ISelectableItemsView view)
    {
        if (_setSelectedItems == null)
        {
            return;
        }
        var indices = view.GetSelectedIndices();
        if (indices.Count == 0)
        {
            _setSelectedItems(Array.Empty<object?>());
            return;
        }
        // Skip indices that are transiently out of range: a collection change fires SelectionChanged
        // before the selected set is remapped, so the set can briefly reference a removed position.
        // A later SelectedIndicesChanged re-runs this with the remapped set.
        int count = view.Count;
        var items = new List<object?>(indices.Count);
        foreach (int index in indices)
        {
            if ((uint)index < (uint)count)
                items.Add(view.GetItem(index));
        }
        _setSelectedItems(items);
    }

    public void SelectAll()
    {
        var view = _view();
        var multi = view.AsMultiSelectable();
        if (multi != null && multi.SelectionMode != ItemsSelectionMode.Single && view.Count > 0)
            multi.SelectRange(0, view.Count - 1, clearExisting: true);
    }

    public void ClearSelection()
    {
        var view = _view();
        var multi = view.AsMultiSelectable();
        if (multi != null)
            multi.ClearSelection();
        else
            view.SelectedIndex = -1;
    }

    public void SelectRange(int start, int end)
        => _view().AsMultiSelectable()?.SelectRange(start, end, clearExisting: true);
}
