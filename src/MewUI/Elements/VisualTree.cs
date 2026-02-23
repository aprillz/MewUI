using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Helper for traversing the visual tree.
/// </summary>
public static class VisualTree
{
    public static Element? FindVisualChild<T>(this Element element) where T : Element
    {
        return Find(element, x => x is T);
    }

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
            host.VisitChildren(child =>
            {
                Visit(child, visitor);
                return true;
            });
        }
    }

    /// <summary>
    /// Returns the first element (depth-first) matching <paramref name="predicate"/>,
    /// or <see langword="null"/> if none is found.
    /// </summary>
    public static Element? Find(Element? element, Func<Element, bool> predicate)
    {
        if (element == null)
        {
            return null;
        }

        if (predicate(element))
        {
            return element;
        }

        if (element is IVisualTreeHost host)
        {
            Element? found = null;

            host.VisitChildren(child =>
            {
                found = Find(child, predicate);
                return found == null;
            });

            return found;
        }

        return null;
    }

    /// <summary>
    /// Returns all elements (depth-first) matching <paramref name="predicate"/>.
    /// </summary>
    public static IReadOnlyList<Element> FindAll(Element? root, Func<Element, bool> predicate)
    {
        var result = new List<Element>();
        Visit(root, element =>
        {
            if (predicate(element))
            {
                result.Add(element);
            }
        });
        return result;
    }
}
