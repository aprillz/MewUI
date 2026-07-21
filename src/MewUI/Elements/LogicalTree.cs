using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Helper for traversing the logical tree. Only follows <see cref="ILogicalTreeHost"/>;
/// never falls back to the visual <see cref="Element.Parent"/> chain.
/// Uses iterative traversal with a ThreadStatic stack to avoid per-node closure allocations.
/// </summary>
public static class LogicalTree
{
    [ThreadStatic]
    private static List<Element>? _stack;

    // Single static delegate - no per-call allocation.
    private static readonly Func<Element, bool> _collector = static child =>
    {
        _stack!.Add(child);
        return true;
    };

    /// <summary>
    /// Returns the first element (depth-first, including <paramref name="element"/> itself)
    /// of type <typeparamref name="T"/>, or <see langword="null"/> if none is found.
    /// </summary>
    public static T? FindLogicalChild<T>(this Element element) where T : Element
    {
        return (T?)Find(element, static candidate => candidate is T);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="element"/> is
    /// <paramref name="root"/> or a descendant of it in the logical parent chain.
    /// </summary>
    public static bool IsInSubtreeOf(Element element, Element root)
    {
        for (Element? current = element; current != null; current = current.LogicalParent)
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Visits <paramref name="element"/> and all of its logical descendants in depth-first pre-order.
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

        try
        {
            while (stack.Count > baseIndex)
            {
                int lastIdx = stack.Count - 1;
                var el = stack[lastIdx];
                stack.RemoveAt(lastIdx);

                visitor(el);

                if (el is ILogicalTreeHost host)
                {
                    int childStart = stack.Count;
                    host.VisitLogicalChildren(_collector);
                    // Reverse newly added children so first child is popped first (DFS pre-order)
                    ReverseRange(stack, childStart, stack.Count - 1);
                }
            }
        }
        finally
        {
            // A throwing visitor must not strand element references above baseIndex on the reused stack.
            if (stack.Count > baseIndex)
            {
                stack.RemoveRange(baseIndex, stack.Count - baseIndex);
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

        try
        {
            if (element is ILogicalTreeHost rootHost)
            {
                int childStart = stack.Count;
                rootHost.VisitLogicalChildren(_collector);
                ReverseRange(stack, childStart, stack.Count - 1);
            }

            while (stack.Count > baseIndex)
            {
                int lastIdx = stack.Count - 1;
                var el = stack[lastIdx];
                stack.RemoveAt(lastIdx);

                if (predicate(el))
                {
                    return el;
                }

                if (el is ILogicalTreeHost host)
                {
                    int childStart = stack.Count;
                    host.VisitLogicalChildren(_collector);
                    ReverseRange(stack, childStart, stack.Count - 1);
                }
            }

            return null;
        }
        finally
        {
            // Trim work items back to baseIndex whether we matched early, finished, or a predicate threw.
            if (stack.Count > baseIndex)
            {
                stack.RemoveRange(baseIndex, stack.Count - baseIndex);
            }
        }
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
