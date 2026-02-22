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
}
