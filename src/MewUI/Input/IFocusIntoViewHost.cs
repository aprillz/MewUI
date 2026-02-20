using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Input;

/// <summary>
/// Implemented by controls that want to react when focus moves to a descendant element,
/// typically to update selection and/or scroll the containing item into view.
/// </summary>
internal interface IFocusIntoViewHost
{
    /// <summary>
    /// Called after focus is set to <paramref name="focusedElement"/>.
    /// Return true if the host handled the focus (e.g. selected the corresponding item and scrolled it into view).
    /// </summary>
    bool OnDescendantFocused(UIElement focusedElement);
}

