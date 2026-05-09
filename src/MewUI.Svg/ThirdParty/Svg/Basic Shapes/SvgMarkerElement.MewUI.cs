namespace Svg;

public abstract partial class SvgMarkerElement
{
    protected internal override bool RenderStroke(ISvgRenderer renderer)
    {
        var result = base.RenderStroke(renderer);
        var path = Path(renderer);
        if (path is null || path.IsEmpty)
        {
            return result;
        }

        var points = MewSvgPathUtilities.GetMarkerPoints(path);
        if (points.Count < 2)
        {
            return result;
        }

        var markerStart = NormalizeMarkerUri(MarkerStart);
        if (markerStart is not null)
        {
            var marker = OwnerDocument.GetElementById<SvgMarker>(markerStart.ToString());
            marker?.RenderMarker(renderer, this, points[0], points[0], points[1], true);
        }

        var markerMid = NormalizeMarkerUri(MarkerMid);
        if (markerMid is not null)
        {
            var marker = OwnerDocument.GetElementById<SvgMarker>(markerMid.ToString());
            if (marker is not null)
            {
                for (var i = 1; i < points.Count - 1; i++)
                {
                    marker.RenderMarker(renderer, this, points[i], points[i - 1], points[i], points[i + 1]);
                }
            }
        }

        var markerEnd = NormalizeMarkerUri(MarkerEnd);
        if (markerEnd is not null)
        {
            var marker = OwnerDocument.GetElementById<SvgMarker>(markerEnd.ToString());
            marker?.RenderMarker(renderer, this, points[^1], points[^2], points[^1], false);
        }

        return result;
    }

    private static Uri? NormalizeMarkerUri(Uri? uri)
    {
        return uri is null || string.Equals(uri.ToString(), "none", StringComparison.OrdinalIgnoreCase)
            ? null
            : uri;
    }
}
