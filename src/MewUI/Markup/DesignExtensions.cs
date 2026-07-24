namespace Aprillz.MewUI.Controls;

/// <summary>
/// Fluent design-time hints consumed by editor preview sessions.
/// </summary>
public static class DesignExtensions
{
    /// <summary>
    /// Sets the preferred preview size for this element. A no-op outside preview sessions.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="width">Preview width in DIPs.</param>
    /// <param name="height">Preview height in DIPs.</param>
    /// <returns>The element for chaining.</returns>
    public static T DesignSize<T>(this T element, double width, double height) where T : FrameworkElement
    {
        Design.SetDesignSize(element, width, height);
        return element;
    }

    /// <summary>
    /// Sets the preferred preview width for this element. A no-op outside preview sessions.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="width">Preview width in DIPs.</param>
    /// <returns>The element for chaining.</returns>
    public static T DesignWidth<T>(this T element, double width) where T : FrameworkElement
    {
        Design.SetDesignSize(element, width, null);
        return element;
    }

    /// <summary>
    /// Sets the preferred preview height for this element. A no-op outside preview sessions.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="height">Preview height in DIPs.</param>
    /// <returns>The element for chaining.</returns>
    public static T DesignHeight<T>(this T element, double height) where T : FrameworkElement
    {
        Design.SetDesignSize(element, null, height);
        return element;
    }
}
