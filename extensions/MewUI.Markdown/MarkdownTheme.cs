namespace Aprillz.MewUI.Markdown;

/// <summary>Immutable document metrics and optional colors; null colors follow the current UI theme.</summary>
public sealed record MarkdownTheme
{
    /// <summary>Gets the gap between document blocks in DIPs.</summary>
    public double BlockSpacing { get; init; } = 8;
    /// <summary>Gets the font family for inline and block code.</summary>
    public string CodeFontFamily { get; init; } = "Consolas";
    /// <summary>Gets the optional link foreground.</summary>
    public Color? LinkForeground { get; init; }
    /// <summary>Gets the optional code background.</summary>
    public Color? CodeBackground { get; init; }
    /// <summary>Gets the optional marked-text background.</summary>
    public Color? MarkedBackground { get; init; }
}
