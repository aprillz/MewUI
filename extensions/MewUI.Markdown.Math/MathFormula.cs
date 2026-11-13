using System.Text;
using Aprillz.MewUI.Rendering;
using CSharpMath.Atom;
using CSharpMath.Rendering.FrontEnd;

namespace Aprillz.MewUI.Markdown;

/// <summary>
/// One LaTeX formula typeset by CSharpMath: its size at a font size, and its drawing recorded once per font size
/// through <see cref="MathCanvas"/> and replayed on every draw. The most recent font sizes are kept, so a formula
/// shared by a heading and a paragraph does not typeset again on every pass.
/// </summary>
internal sealed class MathFormula
{
    // Past these, CSharpMath's recursive typesetter overflows the stack, which no handler can catch.
    private const int MAX_SOURCE_LENGTH = 10_000;
    private const int MAX_BRACE_DEPTH = 50;
    private const int MAX_KEPT_SIZES = 4;

    private readonly Painter _painter;
    private readonly List<SizedLayout> _layouts = new();
    private readonly object _gate = new();

    /// <summary>Parses <paramref name="latex"/>; bad LaTeX is kept and draws as CSharpMath's own error text.</summary>
    public MathFormula(string latex, bool display)
    {
        MathCommands.EnsureRegistered();
        _painter = new Painter
        {
            LineStyle = display ? LineStyle.Display : LineStyle.Text,
            LaTeX = Normalize(latex),
        };
    }

    /// <summary>
    /// Rewrites the commands CSharpMath does not know into ones it draws the same way: <c>\dfrac</c> and
    /// <c>\tfrac</c> become <c>\frac</c>, and <c>\boldsymbol</c> becomes <c>\mathbf</c>.
    /// </summary>
    internal static string Normalize(string latex)
    {
        StringBuilder builder = new(latex.Length);
        int index = 0;
        while (index < latex.Length)
        {
            if (latex[index] != '\\' || index + 1 >= latex.Length)
            {
                builder.Append(latex[index]);
                index++;
                continue;
            }

            if (!char.IsAsciiLetter(latex[index + 1]))
            {
                // A control symbol such as "\\" or "\{" ends at its second character, so "\\boldsymbol" stays a line break and text.
                builder.Append(latex, index, 2);
                index += 2;
                continue;
            }

            int end = index + 1;
            while (end < latex.Length && char.IsAsciiLetter(latex[end]))
            {
                end++;
            }

            ReadOnlySpan<char> name = latex.AsSpan(index + 1, end - index - 1);
            if (name is "dfrac" or "tfrac")
            {
                builder.Append("\\frac");
            }
            else if (name is "boldsymbol")
            {
                builder.Append("\\mathbf");
            }
            else
            {
                builder.Append(latex, index, end - index);
            }
            index = end;
        }
        return builder.ToString();
    }

    /// <summary>Gets CSharpMath's message when the LaTeX did not parse.</summary>
    public string? ErrorMessage => _painter.ErrorMessage;

    /// <summary>Returns whether a source is within the limits that keep the typesetter from overflowing the stack.</summary>
    public static bool IsSafe(string latex)
    {
        if (latex.Length > MAX_SOURCE_LENGTH)
        {
            return false;
        }

        int depth = 0;
        foreach (char character in latex)
        {
            if (character == '{')
            {
                depth++;
                if (depth > MAX_BRACE_DEPTH)
                {
                    return false;
                }
            }
            else if (character == '}')
            {
                depth--;
            }
        }
        return true;
    }

    /// <summary>Gets how many times the formula was recorded, for tests.</summary>
    internal int RecordCount { get; private set; }

    /// <summary>Typesets at <paramref name="fontSize"/> DIPs and returns the width, the ascent above the baseline and the descent below it.</summary>
    public (double Width, double Ascent, double Descent) Measure(double fontSize)
    {
        lock (_gate)
        {
            SizedLayout layout = GetLayout(fontSize);
            return (layout.Width, layout.Ascent, layout.Descent);
        }
    }

    /// <summary>Draws the formula with its baseline starting at <paramref name="baseline"/>, in <paramref name="color"/>.</summary>
    public void Draw(IGraphicsContext graphics, Point baseline, Color color, double fontSize)
        => GetDrawing(fontSize).Draw(graphics, baseline, color);

    /// <summary>Returns the recorded drawing at <paramref name="fontSize"/>, recording it the first time.</summary>
    internal MathDrawing GetDrawing(double fontSize)
    {
        lock (_gate)
        {
            SizedLayout layout = GetLayout(fontSize);
            return layout.Drawing ??= Record(layout);
        }
    }

    /// <summary>Returns the layout for a font size, typesetting it when it is not among the kept ones.</summary>
    private SizedLayout GetLayout(double fontSize)
    {
        float points = (float)Math.Max(1, fontSize);
        for (int index = _layouts.Count - 1; index >= 0; index--)
        {
            SizedLayout kept = _layouts[index];
            if (kept.Points == points)
            {
                if (index != _layouts.Count - 1)
                {
                    _layouts.RemoveAt(index);
                    _layouts.Add(kept);
                }
                return kept;
            }
        }

        SetFontSize(points);
        System.Drawing.RectangleF bounds = _painter.Measure();
        SizedLayout layout = new(points, bounds.Width, -bounds.Y, bounds.Height + bounds.Y);
        if (_layouts.Count == MAX_KEPT_SIZES)
        {
            _layouts.RemoveAt(0);
        }
        _layouts.Add(layout);
        return layout;
    }

    private MathDrawing Record(SizedLayout layout)
    {
        SetFontSize(layout.Points);
        MathCanvas canvas = new((float)layout.Width, (float)(layout.Ascent + layout.Descent));
        _painter.Draw(canvas, 0f, 0f);
        RecordCount++;
        return canvas.ToDrawing();
    }

    private void SetFontSize(float points)
    {
        // Setting the size typesets again even when it is unchanged.
        if (_painter.FontSize != points)
        {
            _painter.FontSize = points;
        }
    }

    /// <summary>The metrics and the recorded drawing at one font size.</summary>
    private sealed class SizedLayout
    {
        public SizedLayout(float points, double width, double ascent, double descent)
        {
            Points = points;
            Width = width;
            Ascent = ascent;
            Descent = descent;
        }

        public float Points { get; }

        public double Width { get; }

        public double Ascent { get; }

        public double Descent { get; }

        public MathDrawing? Drawing { get; set; }
    }

    /// <summary>CSharpMath's painter bound to <see cref="MathCanvas"/> and MewUI colors.</summary>
    private sealed class Painter : MathPainter<MathCanvas, Color>
    {
        public override System.Drawing.Color WrapColor(Color color) => MathCanvas.FromColor(color);

        public override Color UnwrapColor(System.Drawing.Color color) => MathCanvas.ToColor(color);

        public override ICanvas WrapCanvas(MathCanvas canvas) => canvas;
    }
}
