using Aprillz.MewUI.Rendering;

using LiveChartsCore.Drawing;
using LiveChartsCore.Drawing.Segments;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>
/// Base for path-based geometries (lines, areas). Builds a MewUI <see cref="PathGeometry"/>
/// from the <see cref="BaseVectorGeometry.Commands"/> segments and paints it.
/// </summary>
public abstract class VectorGeometry : BaseVectorGeometry, IDrawnElement<MewDrawingContext>
{
    protected abstract void OnOpen(MewDrawingContext context, PathGeometry path, Segment segment);

    protected abstract void OnClose(MewDrawingContext context, PathGeometry path, Segment segment);

    protected abstract void OnDrawSegment(MewDrawingContext context, PathGeometry path, Segment segment);

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
