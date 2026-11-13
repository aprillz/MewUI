using System.Drawing;
using CSharpMath.Atom;
using CSharpMath.Display.FrontEnd;

namespace CSharpMath.Display.Displays;

/// <summary>
/// Displays stacked over one another, such as a brace over its argument with a label above the brace; the parts are
/// positioned in the parent's coordinates and move with this display.
/// </summary>
internal sealed class AnnotationStackDisplay<TFont, TGlyph> : IDisplay<TFont, TGlyph>
    where TFont : IFont<TGlyph>
{
    private readonly IDisplay<TFont, TGlyph>[] _parts;
    private PointF _position;

    public AnnotationStackDisplay(IDisplay<TFont, TGlyph>[] parts, float width, Range range, PointF position)
    {
        _parts = parts;
        Width = width;
        Range = range;
        _position = position;
    }

    public float Ascent
    {
        get
        {
            float ascent = 0;
            foreach (var part in _parts)
            {
                ascent = Math.Max(ascent, part.Ascent + part.Position.Y - _position.Y);
            }
            return ascent;
        }
    }

    public float Descent
    {
        get
        {
            float descent = 0;
            foreach (var part in _parts)
            {
                descent = Math.Max(descent, part.Descent - (part.Position.Y - _position.Y));
            }
            return descent;
        }
    }

    public float Width { get; }

    public Range Range { get; }

    public PointF Position
    {
        get => _position;
        set
        {
            float deltaX = value.X - _position.X;
            float deltaY = value.Y - _position.Y;
            foreach (var part in _parts)
            {
                part.Position = new PointF(part.Position.X + deltaX, part.Position.Y + deltaY);
            }
            _position = value;
        }
    }

    public bool HasScript { get; set; }

    public Color? TextColor { get; set; }

    public Color? BackColor { get; set; }

    public void Draw(IGraphicsContext<TFont, TGlyph> context)
    {
        this.DrawBackground(context);
        foreach (var part in _parts)
        {
            part.Draw(context);
        }
    }

    public void SetTextColorRecursive(Color? textColor)
    {
        TextColor ??= textColor;
        foreach (var part in _parts)
        {
            part.SetTextColorRecursive(textColor);
        }
    }
}
