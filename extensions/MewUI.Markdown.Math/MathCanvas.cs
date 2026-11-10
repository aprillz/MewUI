using System.Numerics;
using Aprillz.MewUI.Rendering;
using CSharpMath.Rendering.FrontEnd;

namespace Aprillz.MewUI.Markdown;

/// <summary>
/// The surface CSharpMath typesets onto: glyph outlines, rules and boxes arrive in the formula's own coordinates,
/// are mapped through a transform kept here and recorded as a <see cref="MathDrawing"/> relative to the baseline origin.
/// </summary>
internal sealed class MathCanvas : ICanvas
{
    private readonly Stack<Matrix3x2> _saved = new();
    private readonly List<MathDrawCommand> _commands = new();
    private Matrix3x2 _transform = Matrix3x2.Identity;

    public MathCanvas(float width, float height)
    {
        Width = width;
        Height = height;
    }

    public float Width { get; }

    public float Height { get; }

    public System.Drawing.Color DefaultColor { get; set; }

    public System.Drawing.Color? CurrentColor { get; set; }

    public PaintStyle CurrentStyle { get; set; }

    internal static Color ToColor(System.Drawing.Color color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    internal static System.Drawing.Color FromColor(Color color) => System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>Returns the commands recorded so far as a drawing.</summary>
    internal MathDrawing ToDrawing() => new(_commands.ToArray());

    internal Point Map(float x, float y)
    {
        Vector2 mapped = Vector2.Transform(new Vector2(x, y), _transform);
        return new Point(mapped.X, mapped.Y);
    }

    public CSharpMath.Rendering.FrontEnd.Path StartNewPath() => new MathPath(this);

    public void DrawLine(float x1, float y1, float x2, float y2, float lineThickness)
    {
        if (CurrentStyle == PaintStyle.Fill)
        {
            _commands.Add(MathDrawCommand.Line(Map(x1, y1), Map(x2, y2), lineThickness * GetScale(), GetOwnColor()));
        }
        else
        {
            this.StrokeLineOutline(x1, y1, x2, y2, lineThickness);
        }
    }

    public void StrokeRect(float left, float top, float width, float height)
    {
        _commands.Add(MathDrawCommand.Rectangle(MapRect(left, top, width, height), fill: false, GetOwnColor()));
    }

    public void FillRect(float left, float top, float width, float height)
    {
        Color? color = GetOwnColor();
        if (color.HasValue && color.Value.A == 0)
        {
            // The highlight behind a formula is transparent unless a caller sets one.
            return;
        }
        _commands.Add(MathDrawCommand.Rectangle(MapRect(left, top, width, height), fill: true, color));
    }

    /// <summary>Freezes and records a finished path, in the path's own color when it has one.</summary>
    internal void AddPath(PathGeometry path, System.Drawing.Color? foreground)
    {
        if (path.IsEmpty)
        {
            return;
        }

        path.Freeze();
        Color? color = foreground.HasValue ? ToColor(foreground.Value) : GetOwnColor();
        _commands.Add(MathDrawCommand.Path(path, fill: CurrentStyle == PaintStyle.Fill, color));
    }

    public void Save() => _saved.Push(_transform);

    public void Translate(float dx, float dy) => _transform = Matrix3x2.CreateTranslation(dx, dy) * _transform;

    public void Scale(float sx, float sy) => _transform = Matrix3x2.CreateScale(sx, sy) * _transform;

    public void Restore()
    {
        if (_saved.Count > 0)
        {
            _transform = _saved.Pop();
        }
    }

    /// <summary>Gets the color CSharpMath set for the next primitive, or null when it takes the text color.</summary>
    private Color? GetOwnColor() => CurrentColor.HasValue ? ToColor(CurrentColor.Value) : null;

    private Rect MapRect(float left, float top, float width, float height)
    {
        // The formula's y axis points up, so a mapped box may come out upside down.
        Point first = Map(left, top);
        Point second = Map(left + width, top + height);
        double x = Math.Min(first.X, second.X);
        double y = Math.Min(first.Y, second.Y);
        return new Rect(x, y, Math.Abs(second.X - first.X), Math.Abs(second.Y - first.Y));
    }

    private float GetScale()
    {
        float determinant = _transform.M11 * _transform.M22 - _transform.M12 * _transform.M21;
        return MathF.Sqrt(MathF.Abs(determinant));
    }
}
