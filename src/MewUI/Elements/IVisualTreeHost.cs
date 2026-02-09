using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Provides child traversal for the visual tree.
/// </summary>
internal interface IVisualTreeHost
{
    /// <summary>
    /// Visits visual children of the current element.
    /// </summary>
    void VisitChildren(Action<Element> visitor);
}
