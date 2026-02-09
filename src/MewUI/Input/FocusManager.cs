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

        _window.RequerySuggested();

        return true;
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

        if (root is Controls.TabControl tabControl)
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

        int currentIndex = FocusedElement != null ? focusable.IndexOf(FocusedElement) : -1;
        int nextIndex = (currentIndex + 1) % focusable.Count;

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

        int currentIndex = FocusedElement != null ? focusable.IndexOf(FocusedElement) : focusable.Count;
        int prevIndex = (currentIndex - 1 + focusable.Count) % focusable.Count;

        return SetFocus(focusable[prevIndex]);
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
        if (element is Controls.TabControl tabControl)
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
