using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// What a segmented control hands its strip panel to draw: the segment corner radius, the strip edges the
/// first and last segment are pinned to, and the dividers between segments.
/// </summary>
internal struct SegmentStripChrome
{
    public double SegmentRadius;

    // Null leaves a segment's own edge; set when no outer padding separates the strip from the frame.
    public double? StripLeft;
    public double? StripRight;

    // In a template the strip fills its host, so the end segments pin to the panel's own edges.
    public bool PinToOwnEdges;

    public Color DividerColor;
    public double DividerThickness;
}

/// <summary>A strip panel the segmented controls size their segments with; it draws the dividers over them.</summary>
internal interface ISegmentStrip
{
    /// <summary>Asked when the strip arranges its segments and when it draws its dividers.</summary>
    Func<SegmentStripChrome>? ChromeSource { get; set; }
}

/// <summary>Chrome drawing shared by the uniform and the auto-sized strip panel.</summary>
internal static class SegmentStripRenderer
{
    /// <summary>Hands each segment its corner radius and pinned strip edge before it draws.</summary>
    public static void ApplyChrome(Panel panel, in SegmentStripChrome chrome)
    {
        var bounds = panel.Bounds;
        for (int i = 0; i < panel.Count; i++)
        {
            if (panel[i] is not SegmentButton button)
            {
                continue;
            }

            button.Radius = chrome.SegmentRadius;
            if (chrome.PinToOwnEdges)
            {
                button.StripLeft = button.IsFirst ? bounds.Left : null;
                button.StripRight = button.IsLast ? bounds.Right : null;
            }
            else
            {
                button.StripLeft = button.IsFirst ? chrome.StripLeft : null;
                button.StripRight = button.IsLast ? chrome.StripRight : null;
            }
        }
    }

    /// <summary>Draws the seam between neighbouring segments on top of them.</summary>
    public static void DrawDividers(Panel panel, IGraphicsContext context, in SegmentStripChrome chrome, double dpiScale)
    {
        if (panel.Count < 2 || chrome.DividerThickness <= 0)
        {
            return;
        }

        // Draw each internal boundary at the RIGHT segment's left edge. Per-child layout rounding
        // rounds X and Width independently, so at fractional cell widths (3+ items) a segment's
        // snapped right edge can drift 1px from its neighbor's snapped left edge. Segments render
        // in order (later on top), so the visible seam is the right neighbor's left edge - snap it
        // the same way the segment background does so the divider lands exactly on the seam.
        for (int i = 1; i < panel.Count; i++)
        {
            if (panel[i] is not UIElement right)
            {
                continue;
            }

            var seam = LayoutRounding.SnapBoundsRectToPixels(right.Bounds, dpiScale);
            if (seam.Width <= 0 || seam.Height <= 0)
            {
                continue;
            }

            double x = seam.Left;
            context.DrawLine(new Point(x, seam.Top), new Point(x, seam.Bottom), chrome.DividerColor, chrome.DividerThickness, true);
        }
    }
}

/// <summary>The strip panel for <see cref="SegmentSizing.Uniform"/>: every segment takes the same width.</summary>
internal sealed class UniformSegmentPanel : UniformGrid, ISegmentStrip
{
    public Func<SegmentStripChrome>? ChromeSource { get; set; }

    private SegmentStripChrome Chrome => ChromeSource?.Invoke() ?? default;

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);
        SegmentStripRenderer.ApplyChrome(this, Chrome);
    }

    protected override void OnRenderOverlay(IGraphicsContext context)
        => SegmentStripRenderer.DrawDividers(this, context, Chrome, GetDpi() / 96.0);
}

/// <summary>The strip panel for <see cref="SegmentSizing.Auto"/>: each segment takes its own width.</summary>
internal sealed class StackSegmentPanel : StackPanel, ISegmentStrip
{
    public Func<SegmentStripChrome>? ChromeSource { get; set; }

    private SegmentStripChrome Chrome => ChromeSource?.Invoke() ?? default;

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);
        SegmentStripRenderer.ApplyChrome(this, Chrome);
    }

    protected override void OnRenderOverlay(IGraphicsContext context)
        => SegmentStripRenderer.DrawDividers(this, context, Chrome, GetDpi() / 96.0);
}
