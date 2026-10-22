using Aprillz.MewUI.Rendering;

using LiveChartsCore.Drawing;
using LiveChartsCore.Drawing.Segments;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>Base class for path-based geometries (lines and areas) built from the <see cref="BaseVectorGeometry.Commands"/> segments.</summary>
public abstract class VectorGeometry : BaseVectorGeometry, IDrawnElement<MewDrawingContext>
{
    /// <summary>Starts the path at the first segment according to the closing method.</summary>
    protected abstract void OnOpen(MewDrawingContext context, PathGeometry path, Segment segment);

    /// <summary>Finishes the path after the last segment according to the closing method.</summary>
    protected abstract void OnClose(MewDrawingContext context, PathGeometry path, Segment segment);

    /// <summary>Appends one segment to the path.</summary>
    protected abstract void OnDrawSegment(MewDrawingContext context, PathGeometry path, Segment segment);

    /// <summary>Draws the path built from the segments with the active paint and removes segments flagged for removal once they complete; no segments draws nothing.</summary>
    public void Draw(MewDrawingContext context)
    {
        if (Commands.Count == 0) return;

        var path = new PathGeometry();
        var isValid = true;
        var isFirst = true;
        Segment? last = null;

        var toRemove = new List<Segment>();

        foreach (var segment in Commands)
        {
            segment.IsValid = true;

            if (isFirst)
            {
                isFirst = false;
                OnOpen(context, path, segment);
            }

            OnDrawSegment(context, path, segment);
            isValid = isValid && segment.IsValid;

            if (segment.IsValid && segment.RemoveOnCompleted) toRemove.Add(segment);
            last = segment;
        }

        foreach (var segment in toRemove)
        {
            _ = Commands.Remove(segment);
            isValid = false;
        }

        if (last is not null) OnClose(context, path, last);

        context.DrawPath(path);

        if (!isValid) IsValid = false;
    }
}
