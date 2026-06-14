using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Model;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>
/// Faithful FlexLayout edge-dock indicators: a marker at the centre of each document-area edge, shown while a
/// document is dragged so the otherwise-invisible outer edge-dock targets are discoverable. The marker under the
/// cursor highlights. Visual only - the actual hit test stays in <see cref="RowNode"/>'s drop logic. Lives in the
/// window <see cref="OverlayLayer"/>.
/// </summary>
internal sealed class EdgeDockIndicators : FrameworkElement
{
    private const double Box = 38;
    private const double Inset = 8;

    private readonly OverlayLayer _overlay;
    private readonly List<(DockLocation Edge, Rect Rect)> _targets = new();
    private DockLocation? _active;
    private bool _visible;

    public EdgeDockIndicators(OverlayLayer overlay)
    {
        _overlay = overlay;
        IsHitTestVisible = false;
    }

    /// <summary>Recompute the four markers from the document area (the root row bounds), in window coordinates.</summary>
    public void Update(Rect documentArea)
    {
        _targets.Clear();
        AddTarget(DockLocation.Left, documentArea.X + Inset, MidY(documentArea));
        AddTarget(DockLocation.Right, documentArea.Right - Inset - Box, MidY(documentArea));
        AddTarget(DockLocation.Top, MidX(documentArea), documentArea.Y + Inset);
        AddTarget(DockLocation.Bottom, MidX(documentArea), documentArea.Bottom - Inset - Box);
        _visible = true;
        InvalidateVisual();
    }

    /// <summary>Highlight the marker for the edge currently under the cursor (null = none).</summary>
    public void SetActive(DockLocation? edge)
    {
        if (_active == edge)
        {
            return;
        }
        _active = edge;
        InvalidateVisual();
    }

    /// <summary>The edge whose marker is under the point (the drag position), or null. The hit zone is padded out
    /// beyond the drawn marker so a drop that lands just off it still snaps to the edge. Updates the highlight.</summary>
    public DockLocation? HitIndicator(Point point)
    {
        const double pad = 12;
        foreach (var target in _targets)
        {
            var hit = new Rect(target.Rect.X - pad, target.Rect.Y - pad, target.Rect.Width + 2 * pad, target.Rect.Height + 2 * pad);
            if (hit.ContainsInclusive(point.X, point.Y))
            {
                SetActive(target.Edge);
                return target.Edge;
            }
        }
        SetActive(null);
        return null;
    }

    public void HideIndicators()
    {
        if (!_visible && _active is null && _targets.Count == 0)
        {
            return;
        }
        _visible = false;
        _active = null;
        _targets.Clear();
        InvalidateVisual();
    }

    public void Dismiss() => _overlay.Remove(this);

    private static double MidY(Rect r) => r.Y + r.Height / 2 - Box / 2;

    private static double MidX(Rect r) => r.X + r.Width / 2 - Box / 2;

    private void AddTarget(DockLocation edge, double x, double y) => _targets.Add((edge, new Rect(x, y, Box, Box)));

    protected override Size MeasureOverride(Size availableSize) => Size.Empty;

    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    protected override UIElement? OnHitTest(Point point) => null;

    protected override void OnRender(IGraphicsContext context)
    {
        if (!_visible || _targets.Count == 0)
        {
            return;
        }

        var accent = Theme.Palette.Accent;
        var face = Theme.Palette.ContainerBackground;
        double dpiScale = GetDpi() / 96.0;
        foreach (var target in _targets)
        {
            var rect = GetSnappedBorderBounds(target.Rect);
            bool isActive = _active == target.Edge;
            DrawSnappedBorder(context, rect, 4,
                isActive ? accent.WithAlpha(64) : face.WithAlpha(128),
                accent.WithAlpha((byte)(isActive ? 255 : 150)), isActive ? 2 : 1, dpiScale);
            context.FillRoundedRectangle(DirectionBar(rect, target.Edge), 2, 2,
                accent.WithAlpha((byte)(isActive ? 255 : 200)));
        }
    }

    // Fill + a crisp stroke: snap the thickness to whole device pixels and inset the stroke by half so the centred
    // stroke lands on whole pixels at fractional DPI (a plain DrawRoundedRectangle on the snapped bounds is soft).
    private static void DrawSnappedBorder(IGraphicsContext context, Rect bounds, double radius,
        Color fill, Color border, double thicknessDip, double dpiScale)
    {
        context.FillRoundedRectangle(bounds, (float)radius, (float)radius, fill);
        double t = Math.Max(1, Math.Round(thicknessDip * dpiScale)) / dpiScale;
        double half = t / 2.0;
        var stroke = new Rect(bounds.X + half, bounds.Y + half,
            Math.Max(0, bounds.Width - t), Math.Max(0, bounds.Height - t));
        context.DrawRoundedRectangle(stroke, (float)Math.Max(0, radius - half), (float)Math.Max(0, radius - half), border, t);
    }

    private static Rect DirectionBar(Rect box, DockLocation edge)
    {
        const double pad = 8;
        const double thick = 7;
        var inner = new Rect(box.X + pad, box.Y + pad, box.Width - 2 * pad, box.Height - 2 * pad);
        return edge switch
        {
            DockLocation.Left => new Rect(inner.X, inner.Y, thick, inner.Height),
            DockLocation.Right => new Rect(inner.Right - thick, inner.Y, thick, inner.Height),
            DockLocation.Top => new Rect(inner.X, inner.Y, inner.Width, thick),
            _ => new Rect(inner.X, inner.Bottom - thick, inner.Width, thick),
        };
    }
}
