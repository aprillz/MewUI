namespace Aprillz.MewUI.Controls;

/// <summary>
/// Provides a lightweight, window-managed overlay layer for elements that need to render and receive input
/// above the normal content (similar in spirit to WPF's AdornerLayer).
/// </summary>
public sealed class AdornerLayer
{
    private readonly Window _window;

    internal AdornerLayer(Window window)
    {
        _window = window;
    }

    /// <summary>
    /// Finds the adorner layer for the specified element.
    /// </summary>
    public static AdornerLayer? GetAdornerLayer(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var root = element.FindVisualRoot();
        return root is Window window ? window.AdornerLayer : null;
    }

    /// <summary>
    /// Adds a WPF-style <see cref="Adorner"/> wrapper.
    /// </summary>
    public void Add(Adorner adorner)
    {
        ArgumentNullException.ThrowIfNull(adorner);
        _window.AddAdornerInternal(adorner.AdornedElement, adorner);
    }

    /// <summary>
    /// Removes a previously added <see cref="Adorner"/>.
    /// </summary>
    public bool Remove(Adorner adorner)
    {
        ArgumentNullException.ThrowIfNull(adorner);
        return _window.RemoveAdornerInternal(adorner);
    }
}
