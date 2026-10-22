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

    /// <summary>Renders the run bold.</summary>
    [FluentSetsConstant(nameof(Run.FontWeight), Aprillz.MewUI.FontWeight.Bold)]
    public static Run Bold(this Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.FontWeight = Aprillz.MewUI.FontWeight.Bold;
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
