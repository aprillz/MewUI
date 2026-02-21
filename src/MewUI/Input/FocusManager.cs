using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Input;

/// <summary>
/// Manages keyboard focus within a window.
/// </summary>
public sealed class FocusManager
{
    private readonly Window _window;

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
    public bool SetFocus(UIElement? element)
    {
        element = ResolveDefaultFocusTarget(element);

        if (FocusedElement == element)
        {
            return true;
        }

        if (element != null && (!element.Focusable || !element.IsEffectivelyEnabled || !element.IsVisible))
        {
            return false;
        }

        var oldElement = FocusedElement;
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

        // Walk up the visual tree: the nearest host should win.
        for (Element? current = focusedElement; current != null; current = current.Parent)
        {
            if (current is IFocusIntoViewHost host)
            {
                if (host.OnDescendantFocused(focusedElement))
                {
                    return;
                }
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

        if (root is TabControl tabControl)
        {
            var fromContent = FindFirstFocusable(tabControl.SelectedTab?.Content);
            if (fromContent != null)
            {
                return fromContent;
            }

            if (IsFocusable(tabControl))
            {
                return tabControl;
            }

            return null;
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
                if (found != null)
                {
                    return;
                }

                found = FindFirstFocusable(child);
            });
            return found;
        }

        return null;
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
        var focusable = CollectFocusableElements(_window.Content);
        if (focusable.Count == 0)
        {
            return false;
        }

        var anchor = ResolveFocusNavigationAnchor(FocusedElement, focusable);
        int currentIndex = anchor != null ? focusable.IndexOf(anchor) : -1;
        int nextIndex = (currentIndex + 1) % focusable.Count;

        if (currentIndex == focusable.Count - 1 && FocusedElement != null)
        {
            if (TryMoveVirtualizedFocus(FocusedElement, moveForward: true))
            {
                return true;
            }
        }

        return SetFocus(focusable[nextIndex]);
    }

    /// <summary>
    /// Moves focus to the previous focusable element.
    /// </summary>
    public bool MoveFocusPrevious()
    {
        var focusable = CollectFocusableElements(_window.Content);
        if (focusable.Count == 0)
        {
            return false;
        }

        var anchor = ResolveFocusNavigationAnchor(FocusedElement, focusable);
        int currentIndex = anchor != null ? focusable.IndexOf(anchor) : focusable.Count;
        int prevIndex = (currentIndex - 1 + focusable.Count) % focusable.Count;

        if (currentIndex == 0 && FocusedElement != null)
        {
            if (TryMoveVirtualizedFocus(FocusedElement, moveForward: false))
            {
                return true;
            }
        }

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
        var visited = new HashSet<UIElement>();
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
        var result = new List<UIElement>();
        CollectFocusableElementsCore(root, result);
        return result;
    }

    private void UpdateFocusWithin(UIElement? oldElement, UIElement? newElement)
    {
        if (oldElement == newElement)
        {
            return;
        }

        var oldChain = CollectFocusWithinChain(oldElement);
        var newChain = CollectFocusWithinChain(newElement);
        var newSet = new HashSet<UIElement>(newChain);

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

    private List<UIElement> CollectFocusWithinChain(UIElement? element)
    {
        var chain = new List<UIElement>();
        var visited = new HashSet<UIElement>();

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
        return chain;
    }

    private void CollectFocusableElementsCore(Element? element, List<UIElement> result)
    {
        if (element is TabControl tabControl)
        {
            int before = result.Count;
            CollectFocusableElementsCore(tabControl.SelectedTab?.Content, result);

            // WinForms-style: Tab navigation enters the selected tab page's content.
            // If there are no focusable descendants, allow the TabControl itself to be focused.
            if (result.Count == before)
            {
                AddIfFocusable(tabControl, result);
            }

            return;
        }

        if (element is UIElement uiElement)
        {
            AddIfFocusable(uiElement, result);
        }

        if (element is IVisualTreeHost host)
        {
            host.VisitChildren(child => CollectFocusableElementsCore(child, result));
        }
    }

    private static void AddIfFocusable(UIElement uiElement, List<UIElement> result)
    {
        if (uiElement.Focusable && uiElement.IsEffectivelyEnabled && uiElement.IsVisible)
        {
            result.Add(uiElement);
        }
    }
}
