using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Fluent API extension methods for styled text runs.
/// </summary>
public static class TextExtensions
{
    /// <summary>Replaces the block's inline runs.</summary>
    public static T Inlines<T>(this T textBlock, params Run[] runs) where T : TextBlock
    {
        ArgumentNullException.ThrowIfNull(textBlock);
        ArgumentNullException.ThrowIfNull(runs);

        textBlock.Inlines.Clear();
        foreach (var run in runs)
        {
            textBlock.Inlines.Add(run);
        }
        return textBlock;
    }

    /// <summary>Sets the run text.</summary>
    public static Run Text(this Run run, string text)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.Text = text;
        return run;
    }

    /// <summary>Sets the font family; null inherits from the owning text element.</summary>
    public static Run FontFamily(this Run run, string? family)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.FontFamily = family;
        return run;
    }

    /// <summary>Sets the font size in points; null inherits from the owning text element.</summary>
    public static Run FontSize(this Run run, double? size)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.FontSize = size;
        return run;
    }

    /// <summary>Sets the font weight; null inherits from the owning text element.</summary>
    public static Run FontWeight(this Run run, FontWeight? weight)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.FontWeight = weight;
        return run;
    }

    /// <summary>Sets the baseline shift in DIPs; positive raises the run above the line's baseline.</summary>
    public static Run BaselineOffset(this Run run, double offset)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.BaselineOffset = offset;
        return run;
    }

    /// <summary>Sets the text color; null inherits from the owning text element.</summary>
    public static Run Foreground(this Run run, Color? color)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.Foreground = color;
        return run;
    }

    /// <summary>Sets the highlight color painted behind the run; null paints nothing.</summary>
    public static Run Background(this Run run, Color? color)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.Background = color;
        return run;
    }

    /// <summary>Sets underline and strikethrough, replacing any current decoration.</summary>
    public static Run Decoration(this Run run, TextDecoration decoration)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.Decoration = decoration;
        return run;
    }

    /// <summary>Renders the run bold.</summary>
    public static Run Bold(this Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.FontWeight = Aprillz.MewUI.FontWeight.Bold;
        return run;
    }

    /// <summary>Renders the run italic.</summary>
    public static Run Italic(this Run run, bool italic = true)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.Italic = italic;
        return run;
    }

    /// <summary>Adds an underline, keeping any other decoration.</summary>
    public static Run Underline(this Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.Decoration |= TextDecoration.Underline;
        return run;
    }

    /// <summary>Adds a strikethrough, keeping any other decoration.</summary>
    public static Run Strikethrough(this Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.Decoration |= TextDecoration.Strikethrough;
        return run;
    }
}
