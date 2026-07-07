using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Input;

/// <summary>
/// Manages keyboard focus within a window.
/// </summary>
public sealed class FocusManager
{
    private readonly Window _window;

    // Reused across Tab presses/focus changes to avoid rebuilding a List/HashSet every time.
    // Each is cleared before use and never retained past the call that filled it.
    private readonly List<UIElement> _focusableScratch = new();
    private readonly HashSet<UIElement> _navigationVisitedScratch = new();
    private readonly List<UIElement> _oldFocusChainScratch = new();
    private readonly List<UIElement> _newFocusChainScratch = new();
    private readonly HashSet<UIElement> _focusChainVisitedScratch = new();
    private readonly HashSet<UIElement> _newFocusSetScratch = new();

    internal FocusManager(Window window)
    {
        _window = window;
    }

    /// <summary>
    /// Gets the currently focused element.
    /// </summary>
    public UIElement? FocusedElement { get; private set; }

    /// <summary>
    /// Sets focus to the specified element.
    /// </summary>
    public bool SetFocus(UIElement? element) => SetFocus(element, resolveDefault: true);

    /// <summary>
    /// Sets focus to the specified element.
    /// </summary>
    /// <param name="element">The element to focus.</param>
    /// <param name="resolveDefault">
    /// When true, applies <see cref="UIElement.GetDefaultFocusTarget"/> redirection (e.g. a container
    /// forwards focus to its first focusable child). Programmatic and keyboard-driven focus should use
    /// this path. Mouse-click-driven focus should pass false - the click already expresses a precise
    /// location, so silently redirecting focus elsewhere (possibly off-screen) violates user intent
    /// and can trigger unwanted scroll-into-view.
    /// </param>
    public bool SetFocus(UIElement? element, bool resolveDefault)
    {
        if (resolveDefault)
        {
            element = ResolveDefaultFocusTarget(element);
        }

        if (FocusedElement == element)
        {
            return true;
        }

        if (element != null && (!element.Focusable || !element.IsEffectivelyEnabled || !element.IsVisible))
        {
            return false;
        }

        var oldElement = FocusedElement;

        // Cancel IME composition on the losing element before changing focus.
        if (oldElement is ITextCompositionClient { IsComposing: true })
        {
            _window.CancelImeComposition();
        }

        FocusedElement = element;

        UpdateFocusWithin(oldElement, element);

        oldElement?.SetFocused(false);
        element?.SetFocused(true);

        // WPF-like policy: close non-stays-open popups when focus moves outside both the popup and its owner.
        _window.OnFocusChanged(element);

        // If focus moved into a templated control (e.g. TreeView/GridView item template),
        // let the nearest items host update selection and scroll the owning item into view.
        NotifyFocusIntoViewHosts(element);

        _window.RequerySuggested();

        return true;
    }

    private void NotifyFocusIntoViewHosts(UIElement? focusedElement)
    {
        if (focusedElement == null)
        {
            return;
        }

        // Walk up the visual tree, notifying every host along the way. Scroll-into-view must bubble so
        // that nested scrollable containers (e.g. inner ScrollViewer inside outer ScrollViewer, or a
        // TabControl nested in another TabControl) can each adjust their own viewport to make the
        // focused element visible. The return value is informational only - a host signaling `true`
        // (e.g. TreeView/GridView claiming selection) does not block outer hosts from also running
        // their scroll-into-view logic.
        for (Element? current = focusedElement; current != null; current = current.Parent)
        {
            if (current is IFocusIntoViewHost host)
            {
                host.OnDescendantFocused(focusedElement);
            }
        }
    }

    private static UIElement? ResolveDefaultFocusTarget(UIElement? element)
    {
        for (int i = 0; i < 8 && element != null; i++)
        {
            var target = element.GetDefaultFocusTarget();
            if (target == element)
            {
                break;
            }

            element = target;
        }

        return element;
    }

    internal static UIElement? FindFirstFocusable(Element? root)
    {
        if (root == null)
        {
            return null;
        }

        if (root is IFocusTraversalScope scope)
        {
            var fromContent = FindFirstFocusable(scope.ActiveTraversalRoot);
            if (fromContent != null)
            {
                return fromContent;
            }

            return root is UIElement scopeElement && IsFocusable(scopeElement) ? scopeElement : null;
        }

        if (root is UIElement uiElement && IsFocusable(uiElement))
        {
            return uiElement;
        }

        if (root is IVisualTreeHost host)
        {
            UIElement? found = null;
            host.VisitChildren(child =>
            {
                found = FindFirstFocusable(child);
                return found == null;
            });
            return found;
        }

        return null;
    }

    internal static UIElement? FindLastFocusable(Element? root)
    {
        if (root == null)
        {
            return null;
        }

        var result = new UIElement?[1];
        CollectLastFocusable(root, result);
        return result[0];
    }

    private static void CollectLastFocusable(Element element, UIElement?[] result)
    {
        if (element is UIElement uiElement && IsFocusable(uiElement))
        {
            result[0] = uiElement;
        }

        if (element is IVisualTreeHost host)
        {
            host.VisitChildren(child =>
            {
                CollectLastFocusable(child, result);
                return true;
            });
        }
    }

    private static bool IsFocusable(UIElement element) =>
        element.Focusable && element.IsEffectivelyEnabled && element.IsVisible;

    /// <summary>
    /// Clears focus from the current element.
    /// </summary>
    public void ClearFocus() => SetFocus(null);

    /// <summary>
    /// Moves focus to the next focusable element.
    /// </summary>
    public bool MoveFocusNext()
    {
        // Always try virtualized navigation first so that off-screen items
        // are scrolled into view and focused before falling back to the flat list.
        if (FocusedElement != null && TryMoveVirtualizedFocus(FocusedElement, moveForward: true))
        {
            return true;
        }

        var focusable = CollectFocusableElements(_window.Content);
        if (focusable.Count == 0)
        {
            return false;
        }

        var anchor = ResolveFocusNavigationAnchor(FocusedElement, focusable);
        int currentIndex = anchor != null ? focusable.IndexOf(anchor) : -1;
        int nextIndex = (currentIndex + 1) % focusable.Count;

        return SetFocus(focusable[nextIndex]);
    }

    /// <summary>
    /// Moves focus to the previous focusable element.
    /// </summary>
    public bool MoveFocusPrevious()
    {
        if (FocusedElement != null && TryMoveVirtualizedFocus(FocusedElement, moveForward: false))
        {
            return true;
        }

        var focusable = CollectFocusableElements(_window.Content);
        if (focusable.Count == 0)
        {
            return false;
        }

        var anchor = ResolveFocusNavigationAnchor(FocusedElement, focusable);
        int currentIndex = anchor != null ? focusable.IndexOf(anchor) : focusable.Count;
        int prevIndex = (currentIndex - 1 + focusable.Count) % focusable.Count;

        return SetFocus(focusable[prevIndex]);
    }

    private bool TryMoveVirtualizedFocus(UIElement focusedElement, bool moveForward)
    {
        for (Element? current = focusedElement; current != null; current = current.Parent)
        {
            if (current is IVirtualizedTabNavigationHost host)
            {
                return host.TryMoveFocusFromDescendant(focusedElement, moveForward);
            }
        }

        return false;
    }

    private UIElement? ResolveFocusNavigationAnchor(UIElement? focusedElement, List<UIElement> focusableInWindow)
    {
        if (focusedElement == null)
        {
            return null;
        }

        if (focusableInWindow.Contains(focusedElement))
        {
            return focusedElement;
        }

        // Focus may be inside a popup. For tab navigation, anchor to the popup owner
        // so we move to the next element after the owning control (WPF-like behavior).
        var visited = _navigationVisitedScratch;
        visited.Clear();

        Element? current = focusedElement;
        for (int i = 0; i < 32 && current != null; i++)
        {
            if (current is UIElement ui && !visited.Add(ui))
            {
                break;
            }

            if (current is UIElement currentUi && _window.TryGetPopupOwner(currentUi, out var owner) && !ReferenceEquals(owner, currentUi))
            {
                current = owner;
            }
            else
            {
                current = current.Parent;
            }

            if (current is UIElement candidate && focusableInWindow.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private List<UIElement> CollectFocusableElements(Element? root)
    {
        var result = _focusableScratch;
        result.Clear();
        CollectScopeOrdered(root, result);
        return result;
    }

    private void UpdateFocusWithin(UIElement? oldElement, UIElement? newElement)
    {
        if (oldElement == newElement)
        {
            return;
        }

        var oldChain = _oldFocusChainScratch;
        var newChain = _newFocusChainScratch;
        oldChain.Clear();
        newChain.Clear();
        CollectFocusWithinChain(oldElement, oldChain);
        CollectFocusWithinChain(newElement, newChain);

        var newSet = _newFocusSetScratch;
        newSet.Clear();
        foreach (var element in newChain)
        {
            newSet.Add(element);
        }

        for (int i = 0; i < oldChain.Count; i++)
        {
            var e = oldChain[i];
            if (!newSet.Contains(e))
            {
                e.SetFocusWithin(false);
            }
        }

        for (int i = 0; i < newChain.Count; i++)
        {
            newChain[i].SetFocusWithin(true);
        }
    }

    private void CollectFocusWithinChain(UIElement? element, List<UIElement> chain)
    {
        var visited = _focusChainVisitedScratch;
        visited.Clear();

        Element? current = element;
        while (current != null)
        {
            if (current is UIElement ui && visited.Add(ui))
            {
                chain.Add(ui);
            }

            if (current is UIElement currentUi && _window.TryGetPopupOwner(currentUi, out var popupOwner))
            {
                if (popupOwner == currentUi)
                {
                    current = current.Parent;
                }
                else
                {
                    current = popupOwner;
                }
            }
            else
            {
                current = current.Parent;
            }
        }
    }

    private void CollectScopeOrdered(Element? root, List<UIElement> output)
    {
        // Order is resolved per traversal scope: direct tab stops and nested scopes (as atomic
        // blocks) sort by TabIndex - explicit non-negative finite values ascending first, then
        // tree order (stable). With no explicit TabIndex anywhere this equals plain tree order.
        var entries = new List<TabOrderEntry>();
        CollectScopeEntries(root, entries);

        entries.Sort(static (left, right) =>
        {
            int byKey = left.Key.CompareTo(right.Key);
            return byKey != 0 ? byKey : left.Sequence.CompareTo(right.Sequence);
        });

        foreach (var entry in entries)
        {
            if (entry.Block != null)
            {
                output.AddRange(entry.Block);
            }
            else
            {
                output.Add(entry.Element!);
            }
        }
    }

    private void CollectScopeEntries(Element? element, List<TabOrderEntry> entries)
    {
        if (element is UIElement guarded && (!guarded.IsVisible || !guarded.IsEffectivelyEnabled))
        {
            return;
        }

        if (element is IFocusTraversalScope scope)
        {
            // A nested scope joins this scope's order as one block keyed by the scope element's
            // own TabIndex; its inner order is resolved recursively and never interleaves out.
            var block = new List<UIElement>();
            CollectScopeOrdered(scope.ActiveTraversalRoot, block);

            // WinForms-style: Tab navigation enters the selected tab's content.
            // If there are no focusable descendants, allow the scope element itself to be focused.
            if (block.Count == 0 && element is UIElement scopeElement && IsTabStopTarget(scopeElement))
            {
                block.Add(scopeElement);
            }

            if (block.Count > 0 && element is UIElement keyElement)
            {
                entries.Add(new TabOrderEntry(TabSortKey(keyElement), entries.Count, null, block));
            }

            return;
        }

        if (element is UIElement uiElement && IsTabStopTarget(uiElement))
        {
            entries.Add(new TabOrderEntry(TabSortKey(uiElement), entries.Count, uiElement, null));
        }

        if (element is IVisualTreeHost host)
        {
            host.VisitChildren(child =>
            {
                CollectScopeEntries(child, entries);
                return true;
            });
        }
    }

    private readonly record struct TabOrderEntry(double Key, int Sequence, UIElement? Element, List<UIElement>? Block);

    private static double TabSortKey(UIElement element)
    {
        // NaN (the default) fails the comparison and lands in the tree-order group, so the
        // sort key can never itself be NaN.
        double tabIndex = element.TabIndex;
        return tabIndex >= 0 && double.IsFinite(tabIndex) ? tabIndex : double.PositiveInfinity;
    }

    private static bool IsTabStopTarget(UIElement uiElement)
        => uiElement.Focusable && uiElement.IsTabStop && uiElement.IsEffectivelyEnabled && uiElement.IsVisible;
}
