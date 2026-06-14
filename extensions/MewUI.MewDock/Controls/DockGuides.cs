using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Model;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>
/// VS-style dock guide targets shown over the content while a tool is dragged. Each target is an explicit square the
/// user drops on - no cursor-position guessing. Inner targets sit on the document area edges (dock between existing
/// docks and the document); outer targets sit on the dock-area edges (stack outside existing docks) and appear only
/// where a dock already occupies that edge. Lives in the window <see cref="OverlayLayer"/>; hit-tested geometrically
/// by <see cref="FlexLayoutView"/> against the drag position.
/// </summary>
internal sealed class DockGuides : FrameworkElement
{
    private const double Box = 38;
    private const double Inset = 12;

    private readonly OverlayLayer _overlay;
    private readonly List<(DockLocation Edge, bool Outer, Rect Rect)> _targets = new();
    private (DockLocation Edge, bool Outer)? _active;
    private bool _visible;

    public DockGuides(OverlayLayer overlay)
    {
        _overlay = overlay;
        IsHitTestVisible = false;
    }

    /// <summary>Recompute the targets from the current dock area (outer) and document area (inner), both in window
    /// coordinates.</summary>
    public void Update(Rect dockArea, Rect documentArea)
    {
        _targets.Clear();

        // Inner diamond, clustered at the document centre: dock at the document's edge (between existing docks and the
        // document, document-sized).
        double cx = documentArea.X + documentArea.Width / 2 - Box / 2;
        double cy = documentArea.Y + documentArea.Height / 2 - Box / 2;
        const double spread = Box + 10;
        AddTarget(DockLocation.Left, outer: false, cx - spread, cy);
        AddTarget(DockLocation.Right, outer: false, cx + spread, cy);
        AddTarget(DockLocation.Top, outer: false, cx, cy - spread);
        AddTarget(DockLocation.Bottom, outer: false, cx, cy + spread);

        // Outer arrows on the dock-area edges: dock to the OUTERMOST edge (full extent, past any perpendicular docks).
        AddTarget(DockLocation.Left, outer: true, dockArea.X + Inset, MidY(dockArea));
        AddTarget(DockLocation.Right, outer: true, dockArea.Right - Inset - Box, MidY(dockArea));
        AddTarget(DockLocation.Top, outer: true, MidX(dockArea), dockArea.Y + Inset);
        AddTarget(DockLocation.Bottom, outer: true, MidX(dockArea), dockArea.Bottom - Inset - Box);

        _visible = true;
        InvalidateVisual();
    }

    /// <summary>The target under the point (the drag position), or null. The hit zone is padded out beyond the drawn
    /// square so a drop that lands just off it still snaps to the target instead of falling through to a join.</summary>
    public (DockLocation Edge, bool Outer)? HitGuide(Point point)
    {
        const double pad = 12;
        foreach (var target in _targets)
        {
            var hit = new Rect(target.Rect.X - pad, target.Rect.Y - pad, target.Rect.Width + 2 * pad, target.Rect.Height + 2 * pad);
            if (hit.ContainsInclusive(point.X, point.Y))
            {
                if (!_active.HasValue || _active.Value.Edge != target.Edge || _active.Value.Outer != target.Outer)
                {
                    _active = (target.Edge, target.Outer);
                    InvalidateVisual();
                }
                return _active;
            }
        }
        if (_active is not null)
        {
            _active = null;
            InvalidateVisual();
        }
        return null;
    }

    public void HideGuides()
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

    private void AddTarget(DockLocation edge, bool outer, double x, double y)
        => _targets.Add((edge, outer, new Rect(x, y, Box, Box)));

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
            var isActive = _active.HasValue && _active.Value.Edge == target.Edge && _active.Value.Outer == target.Outer;
            var snapped = GetSnappedBorderBounds(target.Rect);

            // Snap bounds + a crisp stroke (thickness to whole device pixels, half-inset so the centred stroke lands
            // on whole pixels) - the bounds snap alone leaves the stroke soft at fractional DPI.
            context.FillRoundedRectangle(snapped, 4, 4, isActive ? accent.WithAlpha(64) : face.WithAlpha(128));
            double t = Math.Max(1, Math.Round((isActive ? 2 : 1) * dpiScale)) / dpiScale;
            double half = t / 2.0;
            var stroke = new Rect(snapped.X + half, snapped.Y + half,
                Math.Max(0, snapped.Width - t), Math.Max(0, snapped.Height - t));
            context.DrawRoundedRectangle(stroke, (float)Math.Max(0, 4 - half), (float)Math.Max(0, 4 - half),
                accent.WithAlpha((byte)(isActive ? 255 : 150)), t);

            // A bar hugging the dock-edge side shows where the panel lands.
            context.FillRoundedRectangle(DirectionBar(snapped, target.Edge), 2, 2,
                accent.WithAlpha((byte)(isActive ? 255 : 200)));
        }
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
            DockLocation.Bottom => new Rect(inner.X, inner.Bottom - thick, inner.Width, thick),
            _ => throw new ArgumentOutOfRangeException(nameof(edge)),
        };
    }
}
