namespace Aprillz.MewUI.Controls;

/// <summary>
/// Selects the virtualization strategy used by an items control.
/// </summary>
public enum ItemsPresenterMode
{
    /// <summary>
    /// Items are treated as fixed-size rows. <see cref="ItemsControl.ItemHeight"/> is used as the actual row height.
    /// </summary>
    Fixed = 0,

    /// <summary>
    /// Items are measured individually and cached. <see cref="ItemsControl.ItemHeight"/> is used as an estimated height hint.
    /// </summary>
    Variable = 1,

    /// <summary>
    /// All items are realized and stacked vertically without virtualization.
    /// Each item is measured individually. Suitable for short lists where all items should be visible.
    /// </summary>
    Stack = 2,
}
