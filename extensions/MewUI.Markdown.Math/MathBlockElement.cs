using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Markdown;

/// <summary>A display formula on lines of its own, centered in the document width and drawn in the inherited font size and foreground.</summary>
internal sealed class MathBlockElement : TextElement
{
    private readonly MathFormula _formula;

    public MathBlockElement(MathFormula formula)
    {
        _formula = formula;
        IsHitTestVisible = false;
        HorizontalAlignment = HorizontalAlignment.Center;
    }

    internal MathFormula Formula => _formula;

    /// <summary>Gets the formula's LaTeX source.</summary>
    public string Source { get; init; } = string.Empty;

    protected override Size MeasureContent(Size availableSize)
    {
        (double width, double ascent, double descent) = _formula.Measure(FontSize);
        return new Size(Math.Ceiling(width), Math.Ceiling(ascent + descent));
    }

    protected override void OnRender(IGraphicsContext context)
    {
        (double width, double ascent, _) = _formula.Measure(FontSize);
        // A formula wider than the document starts at the left edge instead of running past both sides.
        double x = Bounds.X + Math.Max(0, (Bounds.Width - width) / 2);
        _formula.Draw(context, new Point(x, Bounds.Y + ascent), Foreground, FontSize);
    }
}
