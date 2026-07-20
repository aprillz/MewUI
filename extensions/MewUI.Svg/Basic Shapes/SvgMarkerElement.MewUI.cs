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

        var markerStart = NormalizeMarkerUri(MarkerStart);
        if (markerStart is not null &&
            MewSvgPathUtilities.TryGetStartMarkerSegment(path, out var startPoint, out var startTangentPoint))
        {
            var marker = OwnerDocument.GetElementById<SvgMarker>(markerStart.ToString());
            marker?.RenderMarker(renderer, this, startPoint, startPoint, startTangentPoint, true);
        }

        var markerMid = NormalizeMarkerUri(MarkerMid);
        if (markerMid is not null)
        {
            var points = MewSvgPathUtilities.GetMarkerPoints(path);
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
        if (markerEnd is not null &&
            MewSvgPathUtilities.TryGetEndMarkerSegment(path, out var endTangentPoint, out var endPoint))
        {
            var marker = OwnerDocument.GetElementById<SvgMarker>(markerEnd.ToString());
            marker?.RenderMarker(renderer, this, endPoint, endTangentPoint, endPoint, false);
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
