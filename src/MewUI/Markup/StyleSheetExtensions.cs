namespace Aprillz.MewUI.Controls;

/// <summary>
/// Fluent API extension methods for <see cref="StyleSheet"/>.
/// </summary>
public static class StyleSheetExtensions
{
    /// <summary>
    /// Defines a named style through a factory invoked on first lookup
    /// (<see cref="StyleSheet.Define(string, Func{Style})"/>) and returns the sheet for chaining.
    /// </summary>
    /// <param name="sheet">Target style sheet.</param>
    /// <param name="name">Style name.</param>
    /// <param name="factory">Factory invoked on first lookup.</param>
    /// <returns>The style sheet for chaining.</returns>
    public static StyleSheet With(this StyleSheet sheet, string name, Func<Style> factory)
    {
        sheet.Define(name, factory);
        return sheet;
    }

    /// <summary>
    /// Defines a type-based style rule (<see cref="StyleSheet.Define{T}(Style)"/>) and returns the sheet for chaining.
    /// </summary>
    /// <typeparam name="T">Target control type.</typeparam>
    /// <param name="sheet">Target style sheet.</param>
    /// <param name="style">Style definition.</param>
    /// <returns>The style sheet for chaining.</returns>
    public static StyleSheet With<T>(this StyleSheet sheet, Style style) where T : Control
    {
        sheet.Define<T>(style);
        return sheet;
    }

    /// <summary>
    /// Defines a type-based style rule by style name (<see cref="StyleSheet.Define{T}(string)"/>) and returns
    /// the sheet for chaining.
    /// </summary>
    /// <remarks>
    /// The name is resolved where a control's own <see cref="Control.StyleName"/> is, so a rule can point at a
    /// style defined further out.
    /// </remarks>
    /// <typeparam name="T">Target control type.</typeparam>
    /// <param name="sheet">Target style sheet.</param>
    /// <param name="styleName">Style name to resolve.</param>
    /// <returns>The style sheet for chaining.</returns>
    public static StyleSheet WithName<T>(this StyleSheet sheet, string styleName) where T : Control
    {
        sheet.Define<T>(styleName);
        return sheet;
    }
}
