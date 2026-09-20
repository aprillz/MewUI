using System.Numerics;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Rendering.Retained;

/// <summary>
/// Executes recorded commands against a graphics context with the same overloads and arguments the
/// visual originally called.
/// </summary>
internal static class RenderDataReplayer
{
    internal static void Replay(
        IGraphicsContext context,
        in RenderCommand command,
        double[] values,
        object?[] resources)
        => Replay(context, in command, values, resources, 0, 0);

    /// <summary>
    /// Replays one command of a recording that is being drawn moved by the given amount. The caller has
    /// already translated the context by that amount, which carries every shape. Text is the exception:
    /// the translation rides in a float matrix, and that error tips a glyph baseline resting on an exact
    /// half pixel to the other pixel. Text is therefore drawn with the move added to its own origin, in
    /// full precision, under the transform the recording was taken with.
    /// </summary>
    internal static void Replay(
        IGraphicsContext context,
        in RenderCommand command,
        double[] values,
        object?[] resources,
        double placedOffsetX,
        double placedOffsetY)
    {
        var arguments = new ReadOnlySpan<double>(values, command.ValueOffset, command.ValueCount);
        object? paint = command.PaintIndex >= 0 ? resources[command.PaintIndex] : null;
        if (paint != null && command.Kind >= RenderCommandKind.DrawLine && command.Kind <= RenderCommandKind.FillPath)
        {
            // A shape drawn with a pen or a brush, which brings gradients, patterns and dashes with it.
            ReplayOptional(_paintReplay, context, in command, values, resources, placedOffsetX, placedOffsetY);
            return;
        }

        switch (command.Kind)
        {
            case RenderCommandKind.Save:
                context.Save();
                break;
            case RenderCommandKind.Restore:
                context.Restore();
                break;
            case RenderCommandKind.SetClip:
                context.SetClip(command.Bounds);
                break;
            case RenderCommandKind.SetClipRoundedRect:
                if (command.UsesBooleanOverload)
                {
                    context.SetClipRoundedRect(command.Bounds, arguments[0], arguments[1], arguments[2]);
                }
                else
                {
                    context.SetClipRoundedRect(command.Bounds, arguments[0], arguments[1]);
                }
                break;
            case RenderCommandKind.SetClipPath:
            case RenderCommandKind.DrawPath:
            case RenderCommandKind.FillPath:
                ReplayOptional(_pathReplay, context, in command, values, resources, placedOffsetX, placedOffsetY);
                break;
            case RenderCommandKind.ResetClip:
                context.ResetClip();
                break;
            case RenderCommandKind.IntersectClip:
                context.IntersectClip(command.Bounds);
                break;
            case RenderCommandKind.Translate:
                context.Translate(arguments[0], arguments[1]);
                break;
            case RenderCommandKind.Rotate:
                context.Rotate(arguments[0]);
                break;
            case RenderCommandKind.Scale:
                context.Scale(arguments[0], arguments[1]);
                break;
            case RenderCommandKind.SetTransform:
                context.SetTransform(new Matrix3x2(
                    (float)arguments[0],
                    (float)arguments[1],
                    (float)arguments[2],
                    (float)arguments[3],
                    (float)arguments[4],
                    (float)arguments[5]));
                break;
            case RenderCommandKind.ResetTransform:
                context.ResetTransform();
                break;
            case RenderCommandKind.BeginOpaqueBackdrop:
                context.BeginOpaqueBackdrop();
                break;
            case RenderCommandKind.EndOpaqueBackdrop:
                context.EndOpaqueBackdrop();
                break;
            case RenderCommandKind.BeginOpacity:
                context.BeginOpacity(arguments[0]);
                break;
            case RenderCommandKind.EndOpacity:
                context.EndOpacity();
                break;
            case RenderCommandKind.SetGlobalAlpha:
                context.GlobalAlpha = (float)arguments[0];
                break;
            case RenderCommandKind.SetTextPixelSnap:
                context.TextPixelSnap = command.BooleanValue;
                break;
            case RenderCommandKind.SetImageScaleQuality:
                context.ImageScaleQuality = (ImageScaleQuality)(int)arguments[0];
                break;
            case RenderCommandKind.SetAlphaTextHint:
                context.EnableAlphaTextHint = command.BooleanValue;
                break;
            case RenderCommandKind.DrawLine:
                ReplayOptional(_lineReplay, context, in command, values, resources, placedOffsetX, placedOffsetY);
                break;
            case RenderCommandKind.DrawEllipse:
            case RenderCommandKind.FillEllipse:
                ReplayOptional(_ellipseReplay, context, in command, values, resources, placedOffsetX, placedOffsetY);
                break;
            case RenderCommandKind.DrawRectangle:
            case RenderCommandKind.FillRectangle:
            case RenderCommandKind.DrawRoundedRectangle:
            case RenderCommandKind.FillRoundedRectangle:
                ReplayColoredBox(context, in command, arguments);
                break;
            case RenderCommandKind.DrawBoxShadow:
                ReplayOptional(_shadowReplay, context, in command, values, resources, placedOffsetX, placedOffsetY);
                break;
            case RenderCommandKind.DrawImage:
                ReplayOptional(_imageReplay, context, in command, values, resources, placedOffsetX, placedOffsetY);
                break;
            case RenderCommandKind.DrawText:
            case RenderCommandKind.DrawTextBackground:
            case RenderCommandKind.DrawTextForeground:
                ReplayOptional(_textReplay, context, in command, values, resources, placedOffsetX, placedOffsetY);
                break;
            default:
                throw new InvalidOperationException($"The recorded command {command.Kind} cannot be replayed.");
        }
    }

    // Replaying a kind of drawing refers to everything a backend needs to draw it. A kind is therefore
    // replayed through a handler that only recording that kind puts in place, so an application that
    // never draws text carries no text renderer on account of the replayer.
    private delegate void OptionalReplay(IGraphicsContext context, in RenderCommand command, double[] values, object?[] resources, double placedOffsetX, double placedOffsetY);

    private static OptionalReplay? _textReplay;
    private static OptionalReplay? _imageReplay;
    private static OptionalReplay? _pathReplay;
    private static OptionalReplay? _shadowReplay;
    private static OptionalReplay? _lineReplay;
    private static OptionalReplay? _ellipseReplay;
    private static OptionalReplay? _paintReplay;

    internal static void UseText() => _textReplay ??= ReplayAnyText;

    internal static void UseImage() => _imageReplay ??= ReplayAnyImage;

    internal static void UsePath() => _pathReplay ??= ReplayAnyPath;

    internal static void UseBoxShadow() => _shadowReplay ??= ReplayBoxShadow;

    internal static void UseLine() => _lineReplay ??= ReplayAnyLine;

    internal static void UseEllipse() => _ellipseReplay ??= ReplayAnyEllipse;

    /// <summary>Pens and brushes, as opposed to plain colours, for the shapes that are always replayed.</summary>
    internal static void UsePaint() => _paintReplay ??= ReplayPainted;

    private static void ReplayOptional(OptionalReplay? replay, IGraphicsContext context, in RenderCommand command, double[] values, object?[] resources, double placedOffsetX, double placedOffsetY)
    {
        if (replay == null)
        {
            throw new InvalidOperationException($"The recorded command {command.Kind} has no replay in place.");
        }

        replay(context, in command, values, resources, placedOffsetX, placedOffsetY);
    }

    private static void ReplayAnyText(IGraphicsContext context, in RenderCommand command, double[] values, object?[] resources, double placedOffsetX, double placedOffsetY)
    {
        var text = context.Text;
        if (command.Kind == RenderCommandKind.DrawTextBackground)
        {
            ReplayText(context, text.DrawBackground, in command, resources, placedOffsetX, placedOffsetY);
        }
        else if (command.Kind == RenderCommandKind.DrawTextForeground)
        {
            ReplayText(context, text.DrawForeground, in command, resources, placedOffsetX, placedOffsetY);
        }
        else
        {
            ReplayText(context, text.Draw, in command, resources, placedOffsetX, placedOffsetY);
        }
    }

    private static ReadOnlySpan<double> Arguments(in RenderCommand command, double[] values)
        => new(values, command.ValueOffset, command.ValueCount);

    private static object? Paint(in RenderCommand command, object?[] resources)
        => command.PaintIndex >= 0 ? resources[command.PaintIndex] : null;

    /// <summary>Rectangles and rounded rectangles in a plain colour, which the chrome of every control draws.</summary>
    private static void ReplayColoredBox(IGraphicsContext context, in RenderCommand command, ReadOnlySpan<double> arguments)
    {
        switch (command.Kind)
        {
            case RenderCommandKind.FillRectangle:
                context.FillRectangle(command.Bounds, command.Color);
                break;
            case RenderCommandKind.FillRoundedRectangle:
                context.FillRoundedRectangle(command.Bounds, arguments[0], arguments[1], command.Color);
                break;
            case RenderCommandKind.DrawRectangle:
                if (command.UsesBooleanOverload)
                {
                    context.DrawRectangle(command.Bounds, command.Color, arguments[0], command.BooleanValue);
                }
                else
                {
                    context.DrawRectangle(command.Bounds, command.Color, arguments[0]);
                }

                break;
            default:
                if (command.UsesBooleanOverload)
                {
                    context.DrawRoundedRectangle(command.Bounds, arguments[0], arguments[1], command.Color, arguments[2], command.BooleanValue);
                }
                else
                {
                    context.DrawRoundedRectangle(command.Bounds, arguments[0], arguments[1], command.Color, arguments[2]);
                }

                break;
        }
    }

    private static void ReplayPainted(IGraphicsContext context, in RenderCommand command, double[] values, object?[] resources, double placedOffsetX, double placedOffsetY)
    {
        var arguments = Arguments(in command, values);
        object? paint = Paint(in command, resources);
        switch (command.Kind)
        {
            case RenderCommandKind.DrawLine:
                context.DrawLine(new Point(arguments[0], arguments[1]), new Point(arguments[2], arguments[3]), (Pen)paint!);
                break;
            case RenderCommandKind.DrawRectangle:
                context.DrawRectangle(command.Bounds, (Pen)paint!);
                break;
            case RenderCommandKind.FillRectangle:
                context.FillRectangle(command.Bounds, (Brush)paint!);
                break;
            case RenderCommandKind.DrawRoundedRectangle:
                context.DrawRoundedRectangle(command.Bounds, arguments[0], arguments[1], (Pen)paint!);
                break;
            case RenderCommandKind.FillRoundedRectangle:
                context.FillRoundedRectangle(command.Bounds, arguments[0], arguments[1], (Brush)paint!);
                break;
            case RenderCommandKind.DrawEllipse:
                context.DrawEllipse(command.Bounds, (Pen)paint!);
                break;
            case RenderCommandKind.FillEllipse:
                context.FillEllipse(command.Bounds, (Brush)paint!);
                break;
            case RenderCommandKind.DrawPath:
                context.DrawPath(Require<PathGeometry>(in command, resources), (Pen)paint!);
                break;
            default:
                if (command.UsesFillRule)
                {
                    context.FillPath(Require<PathGeometry>(in command, resources), (Brush)paint!, command.FillRule);
                }
                else
                {
                    context.FillPath(Require<PathGeometry>(in command, resources), (Brush)paint!);
                }

                break;
        }
    }

    private static void ReplayAnyLine(IGraphicsContext context, in RenderCommand command, double[] values, object?[] resources, double placedOffsetX, double placedOffsetY)
    {
        var arguments = Arguments(in command, values);
        var start = new Point(arguments[0], arguments[1]);
        var end = new Point(arguments[2], arguments[3]);
        if (command.UsesBooleanOverload)
        {
            context.DrawLine(start, end, command.Color, arguments[4], command.BooleanValue);
        }
        else
        {
            context.DrawLine(start, end, command.Color, arguments[4]);
        }
    }

    private static void ReplayAnyEllipse(IGraphicsContext context, in RenderCommand command, double[] values, object?[] resources, double placedOffsetX, double placedOffsetY)
    {
        var arguments = Arguments(in command, values);
        if (command.Kind == RenderCommandKind.FillEllipse)
        {
            context.FillEllipse(command.Bounds, command.Color);
        }
        else if (command.UsesBooleanOverload)
        {
            context.DrawEllipse(command.Bounds, command.Color, arguments[0], command.BooleanValue);
        }
        else
        {
            context.DrawEllipse(command.Bounds, command.Color, arguments[0]);
        }
    }

    private static void ReplayAnyPath(IGraphicsContext context, in RenderCommand command, double[] values, object?[] resources, double placedOffsetX, double placedOffsetY)
    {
        var path = Require<PathGeometry>(in command, resources);
        if (command.Kind == RenderCommandKind.SetClipPath)
        {
            context.SetClipPath(path);
        }
        else if (command.Kind == RenderCommandKind.DrawPath)
        {
            context.DrawPath(path, command.Color, Arguments(in command, values)[0]);
        }
        else if (command.UsesFillRule)
        {
            context.FillPath(path, command.Color, command.FillRule);
        }
        else
        {
            context.FillPath(path, command.Color);
        }
    }

    private static void ReplayBoxShadow(IGraphicsContext context, in RenderCommand command, double[] values, object?[] resources, double placedOffsetX, double placedOffsetY)
    {
        var arguments = Arguments(in command, values);
        context.DrawBoxShadow(
            new Rect(arguments[4], arguments[5], arguments[6], arguments[7]),
            arguments[0],
            arguments[1],
            command.Color,
            arguments[2],
            arguments[3]);
    }

    private static void ReplayAnyImage(IGraphicsContext context, in RenderCommand command, double[] values, object?[] resources, double placedOffsetX, double placedOffsetY)
    {
        var arguments = Arguments(in command, values);
        var image = Require<IImage>(in command, resources);
        switch (command.ImageVariant)
        {
            case RenderImageVariant.Location:
                context.DrawImage(image, new Point(command.Bounds.X, command.Bounds.Y));
                break;
            case RenderImageVariant.Destination:
                context.DrawImage(image, command.Bounds);
                break;
            case RenderImageVariant.DestinationAndSource:
                context.DrawImage(
                    image,
                    command.Bounds,
                    new Rect(arguments[0], arguments[1], arguments[2], arguments[3]));
                break;
            default:
                throw new InvalidOperationException("The recorded image command has an invalid overload.");
        }
    }

    private static void ReplayText(
        IGraphicsContext context,
        TextReplay replay,
        in RenderCommand command,
        object?[] resources,
        double placedOffsetX,
        double placedOffsetY)
    {
        var layout = Require<ITextLayout>(in command, resources);
        var options = (TextDrawOptions)resources[command.PaintIndex]!;
        if (placedOffsetX == 0 && placedOffsetY == 0)
        {
            replay(layout, new Point(command.Bounds.X, command.Bounds.Y), in options);
            return;
        }

        context.Save();
        try
        {
            // Negating the same float the caller translated by cancels it exactly.
            context.Translate(-placedOffsetX, -placedOffsetY);
            replay(layout, new Point(command.Bounds.X + placedOffsetX, command.Bounds.Y + placedOffsetY), in options);
        }
        finally
        {
            context.Restore();
        }
    }

    private static T Require<T>(in RenderCommand command, object?[] resources) where T : class
        => (command.ResourceIndex >= 0 ? resources[command.ResourceIndex] : null) as T
            ?? throw new InvalidOperationException(
                $"The recorded command {command.Kind} has no {typeof(T).Name} resource.");

    private delegate void TextReplay(ITextLayout layout, Point origin, in TextDrawOptions options);
}
