namespace Aprillz.MewUI.Controls;

public sealed partial class TreeView
{
    internal ScrollViewer ScrollHost => _scrollViewer;

    /// <summary>Selects a row the way a plain click on its text does, without expanding it.</summary>
    internal void SelectFromAutomation(int index)
    {
        var multi = MultiView;
        if (multi != null && multi.SelectionMode != ItemsSelectionMode.Single)
        {
            multi.SelectSingle(index);
        }
        else
        {
            _itemsSource.SelectedIndex = index;
        }
    }

    /// <summary>Expands or collapses a row the way its glyph does, raising <see cref="Expanding"/> or <see cref="Collapsing"/>.</summary>
    internal bool SetExpandedFromAutomation(int index, bool expanded) => TrySetExpanded(index, expanded);
}
