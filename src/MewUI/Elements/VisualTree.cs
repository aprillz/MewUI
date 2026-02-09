using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Helper for traversing the visual tree.
/// </summary>
internal static class VisualTree
{
    /// <summary>
    /// Visits <paramref name="element"/> and all of its descendants in depth-first order.
    /// </summary>
    public static void Visit(Element? element, Action<Element> visitor)
    {
        if (element == null)
        {
            return;
        }

        visitor(element);

        if (element is IVisualTreeHost host)
        {
            host.VisitChildren(child => Visit(child, visitor));
        }
    }
}
