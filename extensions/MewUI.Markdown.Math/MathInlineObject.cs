using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Markdown;

/// <summary>An inline formula occupying its node's columns in a paragraph, sized and colored from the presenter's font and foreground.</summary>
internal sealed class MathInlineObject : IInlineTextObject
{
    private readonly MathFormula _formula;
    private readonly MarkdownPresenter _presenter;

    public MathInlineObject(MathFormula formula, MarkdownPresenter presenter)
    {
        _formula = formula;
        _presenter = presenter;
    }

    public InlineMetrics Measure()
    {
        (double width, double ascent, double descent) = _formula.Measure(_presenter.FontSize);
        return new InlineMetrics(width, ascent + descent, ascent);
    }

    public void Draw(ITextRenderContext context, Point origin)
    {
        (_, double ascent, _) = _formula.Measure(_presenter.FontSize);
        _formula.Draw(context.Graphics, new Point(origin.X, origin.Y + ascent), _presenter.Foreground, _presenter.FontSize);
    }
}
