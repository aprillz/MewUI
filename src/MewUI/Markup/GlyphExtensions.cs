using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Fluent API extensions for <see cref="GlyphElement"/>.
/// </summary>
public static class GlyphExtensions
{
    public static GlyphElement Kind(this GlyphElement element, GlyphKind kind)
    {
        element.Kind = kind;
        return element;
    }

    public static GlyphElement GlyphSize(this GlyphElement element, double size)
    {
        element.GlyphSize = size;
        return element;
    }

    public static GlyphElement StrokeThickness(this GlyphElement element, double thickness)
    {
        element.StrokeThickness = thickness;
        return element;
    }

    public static GlyphElement Foreground(this GlyphElement element, Color color)
    {
        element.Foreground = color;
        return element;
    }
}
