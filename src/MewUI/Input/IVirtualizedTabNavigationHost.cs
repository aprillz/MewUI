using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Input;

/// <summary>
/// Implemented by virtualized items controls that want Tab/Shift+Tab to continue through
/// off-screen items by scrolling and focusing the next/previous item's first focusable element.
/// </summary>
internal interface IVirtualizedTabNavigationHost
{
    bool TryMoveFocusFromDescendant(UIElement focusedElement, bool moveForward);
}

