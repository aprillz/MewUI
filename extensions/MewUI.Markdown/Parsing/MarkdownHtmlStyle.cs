using Aprillz.MewUI;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Markdown;

internal readonly record struct MarkdownHtmlStyle(
    string? FontFamily,
    bool UsesMonospaceFont,
    double? FontSize,
    double FontSizeScale,
    FontWeight? FontWeight,
    bool Italic,
    TextDecoration Decoration,
    Color? Foreground,
    Color? Background,
    double BaselineOffset,
    double BaselineOffsetScale)
{
    public static MarkdownHtmlStyle Empty { get; } =
        new(null, false, null, 1.0, null, false, TextDecoration.None, null, null, 0, 0);
}

internal readonly record struct MarkdownHtmlFrame(string Name, MarkdownHtmlStyle PreviousStyle);
