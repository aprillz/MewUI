using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Markdown;

/// <summary>
/// A formula typeset once at one font size and kept as frozen paths, rules and boxes relative to the baseline origin,
/// so every draw replays the same geometry objects and a backend can reuse what it built for them.
/// </summary>
internal sealed class MathDrawing
{
    private readonly MathDrawCommand[] _commands;

    public MathDrawing(MathDrawCommand[] commands)
    {
        _commands = commands;
    }

    /// <summary>Gets the recorded commands.</summary>
    public ReadOnlySpan<MathDrawCommand> Commands => _commands;

    /// <summary>Draws the recording with its origin at <paramref name="baseline"/>; commands without their own color take <paramref name="foreground"/>.</summary>
    public void Draw(IGraphicsContext graphics, Point baseline, Color foreground)
    {
        graphics.Save();
        try
        {
            graphics.Translate(baseline.X, baseline.Y);
            foreach (MathDrawCommand command in _commands)
            {
                Color color = command.Color ?? foreground;
                switch (command.Kind)
                {
                    case MathDrawKind.FillPath:
                        graphics.FillPath(command.Geometry!, color);
                        break;
                    case MathDrawKind.StrokePath:
                        graphics.DrawPath(command.Geometry!, color, 1);
                        break;
                    case MathDrawKind.Line:
                        graphics.DrawLine(command.Start, command.End, color, command.Thickness);
                        break;
                    case MathDrawKind.FillRectangle:
                        graphics.FillRectangle(command.Bounds, color);
                        break;
                    case MathDrawKind.StrokeRectangle:
                        graphics.DrawRectangle(command.Bounds, color, 1);
                        break;
                }
            }
        }
        finally
        {
            graphics.Restore();
        }
    }
}

internal enum MathDrawKind
{
    FillPath,
    StrokePath,
    Line,
    FillRectangle,
    StrokeRectangle,
}

/// <summary>One recorded primitive; a null <see cref="Color"/> means the foreground at draw time.</summary>
internal readonly struct MathDrawCommand
{
    private MathDrawCommand(MathDrawKind kind, Color? color, PathGeometry? geometry, Point start, Point end, double thickness, Rect bounds)
    {
        Kind = kind;
        Color = color;
        Geometry = geometry;
        Start = start;
        End = end;
        Thickness = thickness;
        Bounds = bounds;
    }

    public MathDrawKind Kind { get; }

    public Color? Color { get; }

    public PathGeometry? Geometry { get; }

    public Point Start { get; }

    public Point End { get; }

    public double Thickness { get; }

    public Rect Bounds { get; }

    public static MathDrawCommand Path(PathGeometry geometry, bool fill, Color? color)
        => new(fill ? MathDrawKind.FillPath : MathDrawKind.StrokePath, color, geometry, default, default, 0, default);

    public static MathDrawCommand Line(Point start, Point end, double thickness, Color? color)
        => new(MathDrawKind.Line, color, null, start, end, thickness, default);

    public static MathDrawCommand Rectangle(Rect bounds, bool fill, Color? color)
        => new(fill ? MathDrawKind.FillRectangle : MathDrawKind.StrokeRectangle, color, null, default, default, 0, bounds);
}
