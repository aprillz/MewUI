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
                context.SetClipPath(Require<PathGeometry>(in command, resources));
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
                ReplayLine(context, in command, arguments, paint);
                break;
            case RenderCommandKind.DrawRectangle:
                ReplayRectangle(context, in command, arguments, paint);
                break;
            case RenderCommandKind.FillRectangle:
                if (paint is Brush rectangleBrush)
                {
                    context.FillRectangle(command.Bounds, rectangleBrush);
                }
                else
                {
                    context.FillRectangle(command.Bounds, command.Color);
                }
                break;
            case RenderCommandKind.DrawRoundedRectangle:
                ReplayRoundedRectangle(context, in command, arguments, paint);
                break;
            case RenderCommandKind.FillRoundedRectangle:
                if (paint is Brush roundedBrush)
                {
                    context.FillRoundedRectangle(command.Bounds, arguments[0], arguments[1], roundedBrush);
                }
                else
                {
                    context.FillRoundedRectangle(command.Bounds, arguments[0], arguments[1], command.Color);
                }
                break;
            case RenderCommandKind.DrawEllipse:
                ReplayEllipse(context, in command, arguments, paint);
                break;
            case RenderCommandKind.FillEllipse:
                if (paint is Brush ellipseBrush)
                {
                    context.FillEllipse(command.Bounds, ellipseBrush);
                }
                else
                {
                    context.FillEllipse(command.Bounds, command.Color);
                }
                break;
            case RenderCommandKind.DrawPath:
                ReplayDrawPath(context, in command, arguments, resources, paint);
                break;
            case RenderCommandKind.FillPath:
                ReplayFillPath(context, in command, resources, paint);
                break;
            case RenderCommandKind.DrawBoxShadow:
                context.DrawBoxShadow(
                    new Rect(arguments[4], arguments[5], arguments[6], arguments[7]),
                    arguments[0],
                    arguments[1],
                    command.Color,
                    arguments[2],
                    arguments[3]);
                break;
            case RenderCommandKind.DrawImage:
                ReplayImage(context, in command, arguments, resources);
                break;
            case RenderCommandKind.DrawText:
                ReplayText(context, context.Text.Draw, in command, resources, placedOffsetX, placedOffsetY);
                break;
            case RenderCommandKind.DrawTextBackground:
                ReplayText(context, context.Text.DrawBackground, in command, resources, placedOffsetX, placedOffsetY);
                break;
            case RenderCommandKind.DrawTextForeground:
                ReplayText(context, context.Text.DrawForeground, in command, resources, placedOffsetX, placedOffsetY);
                break;
            default:
                throw new InvalidOperationException($"The recorded command {command.Kind} cannot be replayed.");
        }
    }

    private static void ReplayLine(
        IGraphicsContext context,
        in RenderCommand command,
        ReadOnlySpan<double> arguments,
        object? paint)
    {
        var start = new Point(arguments[0], arguments[1]);
        var end = new Point(arguments[2], arguments[3]);
        if (paint is Pen pen)
        {
            context.DrawLine(start, end, pen);
        }
        else if (command.UsesBooleanOverload)
        {
            context.DrawLine(start, end, command.Color, arguments[4], command.BooleanValue);
        }
        else
        {
            context.DrawLine(start, end, command.Color, arguments[4]);
        }
    }

    private static void ReplayRectangle(
        IGraphicsContext context,
        in RenderCommand command,
        ReadOnlySpan<double> arguments,
        object? paint)
    {
        if (paint is Pen pen)
        {
            context.DrawRectangle(command.Bounds, pen);
        }
        else if (command.UsesBooleanOverload)
        {
            context.DrawRectangle(command.Bounds, command.Color, arguments[0], command.BooleanValue);
        }
        else
        {
            context.DrawRectangle(command.Bounds, command.Color, arguments[0]);
        }
    }

    private static void ReplayRoundedRectangle(
        IGraphicsContext context,
        in RenderCommand command,
        ReadOnlySpan<double> arguments,
        object? paint)
    {
        if (paint is Pen pen)
        {
            context.DrawRoundedRectangle(command.Bounds, arguments[0], arguments[1], pen);
        }
        else if (command.UsesBooleanOverload)
        {
            context.DrawRoundedRectangle(
                command.Bounds,
                arguments[0],
                arguments[1],
                command.Color,
                arguments[2],
                command.BooleanValue);
        }
        else
        {
            context.DrawRoundedRectangle(command.Bounds, arguments[0], arguments[1], command.Color, arguments[2]);
        }
    }

    private static void ReplayEllipse(
        IGraphicsContext context,
        in RenderCommand command,
        ReadOnlySpan<double> arguments,
        object? paint)
    {
        if (paint is Pen pen)
        {
            context.DrawEllipse(command.Bounds, pen);
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

    private static void ReplayDrawPath(
        IGraphicsContext context,
        in RenderCommand command,
        ReadOnlySpan<double> arguments,
        object?[] resources,
        object? paint)
    {
        var path = Require<PathGeometry>(in command, resources);
        if (paint is Pen pen)
        {
            context.DrawPath(path, pen);
        }
        else
        {
            context.DrawPath(path, command.Color, arguments[0]);
        }
    }

    private static void ReplayFillPath(
        IGraphicsContext context,
        in RenderCommand command,
        object?[] resources,
        object? paint)
    {
        var path = Require<PathGeometry>(in command, resources);
        if (paint is Brush brush)
        {
            if (command.UsesFillRule)
            {
                context.FillPath(path, brush, command.FillRule);
            }
            else
            {
                context.FillPath(path, brush);
            }
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

    private static void ReplayImage(
        IGraphicsContext context,
        in RenderCommand command,
        ReadOnlySpan<double> arguments,
        object?[] resources)
    {
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
