using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>Provides fluent configuration for <see cref="MarkupTextBlock"/>.</summary>
public static class MarkupTextExtensions
{
    /// <summary>Sets the source markup.</summary>
    public static MarkupTextBlock Markup(this MarkupTextBlock textBlock, string markup)
    {
        ArgumentNullException.ThrowIfNull(textBlock);
        textBlock.Markup = markup;
        return textBlock;
    }

    /// <summary>Sets markup presentation options.</summary>
    public static MarkupTextBlock Options(this MarkupTextBlock textBlock, TextMarkupOptions options)
    {
        ArgumentNullException.ThrowIfNull(textBlock);
        ArgumentNullException.ThrowIfNull(options);
        textBlock.Options = options;
        return textBlock;
    }
}
