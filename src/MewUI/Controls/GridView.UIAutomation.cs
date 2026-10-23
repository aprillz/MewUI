namespace Aprillz.MewUI.Controls;

public sealed partial class GridView
{
    internal UIElement HeaderElement => _header;

    /// <summary>The rows in display order (sorted), which row indices refer to.</summary>
    internal ISelectableItemsView RowItems => _core.ItemsSource;

    /// <summary>Selects a row the way a plain click does.</summary>
    internal void SelectRowFromAutomation(int rowIndex)
    {
        var multi = _core.MultiView;
        if (multi != null && multi.SelectionMode != ItemsSelectionMode.Single)
        {
            multi.SelectSingle(rowIndex);
        }
        else
        {
            CommitTargetValue(SelectedIndexProperty, rowIndex);
        }
    }

    internal IMultiSelectableItemsView? RowMultiView => _core.MultiView;
}
