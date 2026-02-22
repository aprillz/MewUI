namespace Aprillz.MewUI.Controls;

/// <summary>
/// Visual-tree traversal utilities shared across controls.
/// </summary>
internal static class VisualTreeHelper
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="element"/> is
    /// <paramref name="root"/> or a descendant of it in the logical/visual parent chain.
    /// </summary>
    public static bool IsInSubtreeOf(UIElement element, Element root)
    {
        for (Element? current = element; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }
        }

        return false;
    }
}
