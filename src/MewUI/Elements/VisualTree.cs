using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Helper for traversing the visual tree.
/// Uses iterative traversal with a ThreadStatic stack to avoid
/// per-node closure allocations.
/// </summary>
public static class VisualTree
{
    [ThreadStatic]
    private static List<Element>? _stack;

    // Single static delegate - no per-call allocation.
    // Pushes children onto the ThreadStatic stack for iterative processing.
    private static readonly Func<Element, bool> _collector = static child =>
    {
        _stack!.Add(child);
        return true;
    };

    public static Element? FindVisualChild<T>(this Element element) where T : Element
    {
        return Find(element, static x => x is T);
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
    /// Visits <paramref name="element"/> and all of its descendants in depth-first pre-order.
    /// </summary>
    public static void Visit(Element? element, Action<Element> visitor)
    {
        if (element == null)
        {
            return;
        }

        var stack = _stack ??= new List<Element>();
        int baseIndex = stack.Count; // support reentrance

        stack.Add(element);

        while (stack.Count > baseIndex)
        {
            int lastIdx = stack.Count - 1;
            var el = stack[lastIdx];
            stack.RemoveAt(lastIdx);

            visitor(el);

            if (el is IVisualTreeHost host)
            {
                int childStart = stack.Count;
                host.VisitChildren(_collector);
                // Reverse newly added children so first child is popped first (DFS pre-order)
                ReverseRange(stack, childStart, stack.Count - 1);
            }
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

        var stack = _stack ??= new List<Element>();
        int baseIndex = stack.Count;

        if (element is IVisualTreeHost rootHost)
        {
            int childStart = stack.Count;
            rootHost.VisitChildren(_collector);
            ReverseRange(stack, childStart, stack.Count - 1);
        }

        while (stack.Count > baseIndex)
        {
            int lastIdx = stack.Count - 1;
            var el = stack[lastIdx];
            stack.RemoveAt(lastIdx);

            if (predicate(el))
            {
                // Clear remaining work items back to base
                if (stack.Count > baseIndex)
                    stack.RemoveRange(baseIndex, stack.Count - baseIndex);
                return el;
            }

            if (el is IVisualTreeHost host)
            {
                int childStart = stack.Count;
                host.VisitChildren(_collector);
                ReverseRange(stack, childStart, stack.Count - 1);
            }
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

    private static void ReverseRange(List<Element> list, int start, int end)
    {
        while (start < end)
        {
            (list[start], list[end]) = (list[end], list[start]);
            start++;
            end--;
        }
    }
}
