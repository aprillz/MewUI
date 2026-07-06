using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Input;

/// <summary>
/// Implemented by controls that redirect keyboard focus traversal into a single active child
/// subtree instead of all of their children (e.g. a tabbed container only exposes the
/// currently selected tab's content to Tab/Shift+Tab navigation).
/// </summary>
internal interface IFocusTraversalScope
{
    /// <summary>
    /// Gets the element that focus traversal should descend into, or null if there is none
    /// (e.g. no tab is currently selected).
    /// </summary>
    Element? ActiveTraversalRoot { get; }
}
