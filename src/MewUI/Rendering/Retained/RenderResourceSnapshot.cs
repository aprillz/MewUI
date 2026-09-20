using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Rendering.Retained;

/// <summary>
/// Turns a just-drawn resource into one that stays valid after the frame: mutable geometry is
/// copied, image-backed paints take a lease, and text options are detached from the caller's spans.
/// </summary>
internal static class RenderResourceSnapshot
{
    private const int PATH_POOL_LIMIT = 4096;

    // Copies handed back by the recordings that were replaced, per thread because recordings are built on more than one.
    [ThreadStatic]
    private static Stack<PathGeometry>? _pathPool;

    /// <summary>Returns a copy of <paramref name="source"/> that later edits cannot reach.</summary>
    internal static PathGeometry SnapshotPath(PathGeometry source)
    {
        PathGeometry snapshot;
        if (_pathPool != null && _pathPool.TryPop(out var pooled))
        {
            pooled.Reset();
            snapshot = pooled;
        }
        else
        {
            snapshot = new PathGeometry();
        }

        snapshot.FillRule = source.FillRule;
        foreach (var command in source.Commands)
        {
            switch (command.Type)
            {
                case PathCommandType.MoveTo:
                    snapshot.MoveTo(command.X0, command.Y0);
                    break;
                case PathCommandType.LineTo:
                    snapshot.LineTo(command.X0, command.Y0);
                    break;
                case PathCommandType.BezierTo:
                    snapshot.BezierTo(command.X0, command.Y0, command.X1, command.Y1, command.X2, command.Y2);
                    break;
                case PathCommandType.Close:
                    snapshot.Close();
                    break;
            }
        }

        // Left unfrozen: a backend keeps a cache entry per frozen geometry, and this copy lives for one recording.
        return snapshot;
    }

    /// <summary>Takes back a copy made by <see cref="SnapshotPath"/> once the recording that held it is gone.</summary>
    internal static void ReturnPath(PathGeometry snapshot)
    {
        var pool = _pathPool ??= new Stack<PathGeometry>();
        if (pool.Count < PATH_POOL_LIMIT)
        {
            pool.Push(snapshot);
        }
    }

    /// <summary>Copies the span-backed parts of <paramref name="options"/> so the record outlives the caller's buffers.</summary>
    internal static TextDrawOptions SnapshotTextOptions(in TextDrawOptions options)
        => new(
            options.Foreground,
            options.PaintSpans.IsEmpty ? default : options.PaintSpans.ToArray(),
            options.Overlays.IsEmpty ? default : options.Overlays.ToArray(),
            options.Owner,
            options.Transient);

    /// <summary>
    /// Produces the pen or brush the replay must use, taking an image lease when the paint samples
    /// an image. Returns false when that image hands out no lease.
    /// </summary>
    internal static bool TrySnapshotPaint(object? paint, out object? snapshot, out IDisposable? ownedResource)
    {
        var imageBrush = paint as ImageBrush;
        Pen? pen = null;
        if (paint is Pen candidatePen && candidatePen.Brush is ImageBrush penImageBrush)
        {
            pen = candidatePen;
            imageBrush = penImageBrush;
        }

        if (imageBrush == null)
        {
            snapshot = paint;
            ownedResource = null;
            return true;
        }

        if (imageBrush.Image is not IRetainableImage retainableImage)
        {
            snapshot = null;
            ownedResource = null;
            return false;
        }

        try
        {
            var retainedImage = retainableImage.Retain();
            var retainedBrush = new ImageBrush(
                retainedImage,
                imageBrush.SourceRect,
                imageBrush.DestinationRect,
                imageBrush.TileMode,
                imageBrush.Opacity,
                imageBrush.Transform);
            snapshot = pen == null
                ? retainedBrush
                : new Pen(retainedBrush, pen.Thickness, pen.StrokeStyle);
            ownedResource = retainedImage;
            return true;
        }
        catch (ObjectDisposedException)
        {
        }
        catch (NotSupportedException)
        {
        }

        snapshot = null;
        ownedResource = null;
        return false;
    }
}
