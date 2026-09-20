using System.Numerics;

using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Resources;

/// <summary>What one entry of a read SVG does when the document is drawn.</summary>
internal enum SimpleSvgCommandKind
{
    /// <summary>Fills and/or strokes one shape.</summary>
    Draw,

    /// <summary>Opens a scope that carries a clip and/or a group opacity.</summary>
    BeginGroup,

    /// <summary>Closes the innermost scope.</summary>
    EndGroup,
}

/// <summary>One entry of a read SVG, in the document's own user coordinates.</summary>
internal readonly struct SimpleSvgCommand
{
    public required SimpleSvgCommandKind Kind { get; init; }

    /// <summary>The shape to fill and stroke, for <see cref="SimpleSvgCommandKind.Draw"/>.</summary>
    public PathGeometry? Geometry { get; init; }

    public Brush? Fill { get; init; }

    public FillRule FillRule { get; init; }

    public Pen? Stroke { get; init; }

    /// <summary>Set by <c>paint-order</c> when the stroke belongs under the fill.</summary>
    public bool StrokeUnderFill { get; init; }

    /// <summary>The fill color came from the root element, so <c>Tint</c> replaces it.</summary>
    public bool FillFromRoot { get; init; }

    /// <summary>The stroke color came from the root element, so <c>Tint</c> replaces it.</summary>
    public bool StrokeFromRoot { get; init; }

    /// <summary>Clip for the scope, or null. Both group commands of a pair carry the same value.</summary>
    public PathGeometry? Clip { get; init; }

    /// <summary>Group opacity. Both group commands of a pair carry the same value.</summary>
    public double Opacity { get; init; }

    /// <summary>
    /// Coordinate system the scope establishes, or null. Pushed rather than baked into the geometry so
    /// that stroke width and user-space gradients scale with it, as SVG requires.
    /// </summary>
    public Matrix3x2? Transform { get; init; }
}

/// <summary>
/// A read SVG: the coordinate system it was drawn in and the entries that draw it. Immutable once
/// built, so one document can back several controls and is never re-read per frame.
/// </summary>
internal sealed class SimpleSvgDocument
{
    private readonly SimpleSvgCommand[] _commands;
    private readonly Rect _viewBox;

    // One-entry cache for the tinted pens, so a redraw at a steady tint allocates nothing. A document
    // shared by controls with different tints rebuilds per draw, which is correct but not free.
    private Color _tintKey;
    private Brush? _tintBrush;
    private Pen?[]? _tintPens;

    public SimpleSvgDocument(Rect viewBox, Size intrinsicSize, SimpleSvgCommand[] commands)
    {
        _viewBox = viewBox;
        _commands = commands;
        IntrinsicSize = intrinsicSize;
    }

    public Size IntrinsicSize { get; }

    /// <summary>Entry count, for tests that assert on what was read.</summary>
    public int CommandCount => _commands.Length;

    public void Render(IGraphicsContext context, Rect destRect, Color? tint)
    {
        if (destRect.Width <= 0 || destRect.Height <= 0 ||
            _viewBox.Width <= 0 || _viewBox.Height <= 0 ||
            _commands.Length == 0)
        {
            return;
        }

        // The entries keep the document's own coordinates, so the whole drawing maps to destRect with
        // one transform. That also carries the gradients, which are stated in those same coordinates.
        var map =
            Matrix3x2.CreateTranslation((float)-_viewBox.X, (float)-_viewBox.Y) *
            Matrix3x2.CreateScale(
                (float)(destRect.Width / _viewBox.Width),
                (float)(destRect.Height / _viewBox.Height)) *
            Matrix3x2.CreateTranslation((float)destRect.X, (float)destRect.Y);

        PrepareTint(tint);

        var outer = context.GetTransform();
        context.Save();
        context.SetTransform(map * outer);
        try
        {
            Execute(context);
        }
        finally
        {
            context.Restore();
        }
    }

    private void Execute(IGraphicsContext context)
    {
        for (int i = 0; i < _commands.Length; i++)
        {
            var command = _commands[i];
            switch (command.Kind)
            {
                case SimpleSvgCommandKind.BeginGroup:
                    context.Save();
                    if (command.Transform is Matrix3x2 transform)
                    {
                        context.SetTransform(transform * context.GetTransform());
                    }
                    // After the transform, so the clip is read in the space the scope establishes.
                    if (command.Clip != null)
                    {
                        context.SetClipPath(command.Clip);
                    }
                    if (command.Opacity < 1.0)
                    {
                        context.BeginOpacity(command.Opacity);
                    }
                    break;

                case SimpleSvgCommandKind.EndGroup:
                    if (command.Opacity < 1.0)
                    {
                        context.EndOpacity();
                    }
                    context.Restore();
                    break;

                default:
                    Draw(context, command, i);
                    break;
            }
        }
    }

    private void Draw(IGraphicsContext context, in SimpleSvgCommand command, int index)
    {
        var geometry = command.Geometry;
        if (geometry == null)
        {
            return;
        }

        var fill = command.FillFromRoot && _tintBrush != null ? _tintBrush : command.Fill;
        var stroke = command.StrokeFromRoot && _tintPens != null ? _tintPens[index] : command.Stroke;

        if (command.StrokeUnderFill && stroke != null)
        {
            context.DrawPath(geometry, stroke);
        }

        if (fill != null)
        {
            context.FillPath(geometry, fill, command.FillRule);
        }

        if (!command.StrokeUnderFill && stroke != null)
        {
            context.DrawPath(geometry, stroke);
        }
    }

    // Builds the substitute brush and pens for the current tint, reusing the previous ones when the
    // color has not moved.
    private void PrepareTint(Color? tint)
    {
        if (tint is not Color color)
        {
            _tintBrush = null;
            _tintPens = null;
            _tintKey = default;
            return;
        }

        if (_tintBrush != null && _tintKey == color)
        {
            return;
        }

        _tintKey = color;
        _tintBrush = new SolidColorBrush(color);
        _tintPens = null;

        for (int i = 0; i < _commands.Length; i++)
        {
            var source = _commands[i].Stroke;
            if (!_commands[i].StrokeFromRoot || source == null)
            {
                continue;
            }

            _tintPens ??= new Pen?[_commands.Length];
            _tintPens[i] = new Pen(_tintBrush, source.Thickness, source.StrokeStyle);
        }
    }
}
