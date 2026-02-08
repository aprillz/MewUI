namespace Aprillz.MewUI.Controls;

/// <summary>
/// Represents a tab item with header and content.
/// </summary>
public sealed class TabItem
{
    /// <summary>
    /// Gets or sets the tab header element.
    /// </summary>
    public Element? Header { get; set; }

    /// <summary>
    /// Gets or sets the tab content element.
    /// </summary>
    public Element? Content { get; set; }

    /// <summary>
    /// Gets or sets whether the tab is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
