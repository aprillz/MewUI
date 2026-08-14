using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public sealed partial class ToolBar
{
    /// <summary>
    /// Where a drop would put the dragged group: which band, and how many groups precede it there.
    /// A band index past the last one asks for a new band. The commit and the mark both read this, so
    /// neither carries its own idea of where the drop is.
    /// </summary>
    internal readonly record struct DropTarget(int Band, int Index)
    {
        internal static DropTarget None => new(-1, -1);

        internal bool IsSet => Band >= 0;
    }

    private (GroupVisual? Group, DropTarget Target, Point Press, bool Armed) _drag;

    /// <summary>The drop the current drag would commit, for tests and for the mark.</summary>
    internal DropTarget DropTargetInternal => _drag.Target;

    private void OnGripPressed(GroupVisual group, MouseEventArgs e)
    {
        if (!CanReorderGroups)
        {
            return;
        }

        // Captured on the press, not on the threshold: the grip is narrower than the gesture, so the
        // pointer has left it long before a drag would start and its own moves would stop arriving.
        _drag = (null, DropTarget.None, e.GetPosition(this), Armed: true);
        _pressedGroup = group;
        (FindVisualRoot() as Window)?.CaptureMouse(this);
    }

    private GroupVisual? _pressedGroup;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_pressedGroup == null)
        {
            return;
        }

        var local = e.GetPosition(this);
        var pointer = new Point(Bounds.X + local.X, Bounds.Y + local.Y);

        if (_drag.Group == null)
        {
            if (Math.Abs(local.X - _drag.Press.X) < DRAG_THRESHOLD &&
                Math.Abs(local.Y - _drag.Press.Y) < DRAG_THRESHOLD)
            {
                return;
            }

            _drag = (_pressedGroup, DropTarget.None, _drag.Press, true);
        }

        var next = ResolveDropTarget(pointer);
        if (next != _drag.Target)
        {
            bool hadPendingBand = HasPendingBand;
            _drag = (_drag.Group, next, _drag.Press, true);

            // Opening or closing the pending band changes the toolbar's height, which its parent has to see.
            if (HasPendingBand != hadPendingBand)
            {
                InvalidateMeasure();
            }

            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (_pressedGroup == null)
        {
            return;
        }

        var dragged = _drag.Group;
        var target = _drag.Target;
        bool hadPendingBand = HasPendingBand;
        _pressedGroup = null;
        _drag = default;
        (FindVisualRoot() as Window)?.ReleaseMouseCapture();

        if (dragged != null && target.IsSet)
        {
            Commit(dragged, target);
        }
        else
        {
            if (hadPendingBand)
            {
                InvalidateMeasure();
            }

            InvalidateVisual();
        }
    }

    /// <summary>
    /// The band comes from how far down the pointer is, the index from how far right it is among that
    /// band's plates. Two coordinates, one for each dimension of the layout.
    /// </summary>
    private DropTarget ResolveDropTarget(Point pointer)
    {
        if (_visuals.Count == 0)
        {
            return DropTarget.None;
        }

        // Below every band asks for a new one. Measured against the bands as laid out, the dragged
        // group's own band included: it is still on screen while it is being dragged.
        if (pointer.Y >= _visuals[^1].Bounds.Bottom)
        {
            return new DropTarget(_visuals.Count, 0);
        }

        int band = 0;
        for (int i = 0; i < _visuals.Count; i++)
        {
            if (pointer.Y < _visuals[i].Bounds.Bottom)
            {
                band = i;
                break;
            }

            band = i + 1;
        }

        band = Math.Min(band, _visuals.Count - 1);

        int index = 0;
        foreach (var group in _visuals[band].Groups)
        {
            if (ReferenceEquals(group, _drag.Group) || group.IsHidden)
            {
                continue;
            }

            if (pointer.X < group.Bounds.X + (group.Bounds.Width / 2))
            {
                break;
            }

            index++;
        }

        return new DropTarget(band, index);
    }

    private void Commit(GroupVisual dragged, DropTarget target)
    {
        var source = dragged.Group.Owner;
        if (source == null)
        {
            return;
        }

        int sourceBand = _bands.IndexOf(source);
        int sourceIndex = source.Groups.IndexOf(dragged.Group);
        if (sourceBand < 0 || sourceIndex < 0)
        {
            return;
        }

        // Removing first, so the target index counts the groups that stay.
        int index = target.Index;
        if (target.Band == sourceBand && index > sourceIndex)
        {
            index--;
        }

        if (target.Band == sourceBand && index == sourceIndex)
        {
            InvalidateVisual();
            return;
        }

        source.Groups.RemoveAt(sourceIndex);

        var band = target.Band < _bands.Count ? _bands[target.Band] : null;
        if (band == null)
        {
            band = new ToolBarBand();
            Bands.Add(band);
            index = 0;
        }

        band.Groups.Insert(Math.Clamp(index, 0, band.Groups.Count), dragged.Group);

        // A band the move emptied is gone: a row exists because groups are on it.
        for (int i = _bands.Count - 1; i >= 0; i--)
        {
            if (_bands[i].GroupsInternal.Count == 0)
            {
                Bands.RemoveAt(i);
            }
        }

        GroupsReordered?.Invoke();
    }

    /// <summary>The mark's rectangle, derived from the drop target rather than decided again.</summary>
    internal bool TryGetDropLine(out Rect line)
    {
        line = Rect.Empty;
        var target = _drag.Target;
        if (_drag.Group == null || !target.IsSet)
        {
            return false;
        }

        double dpiScale = GetDpi() / 96.0;
        double thickness = LayoutRounding.SnapThicknessToPixels(2, dpiScale, 1);
        var content = Bounds.Deflate(Padding);

        // A new band: the mark stands at the start of the row the toolbar has made room for.
        if (target.Band >= _visuals.Count)
        {
            double top = _visuals.Count > 0 ? _visuals[^1].Bounds.Bottom : content.Y;
            line = new Rect(
                LayoutRounding.RoundToPixel(content.X + GROUP_MARGIN, dpiScale),
                LayoutRounding.RoundToPixel(top + GROUP_MARGIN, dpiScale),
                thickness,
                GroupHeight);
            return true;
        }

        var visual = _visuals[target.Band];
        var visible = visual.Groups.Where(static group => !group.IsHidden).ToList();
        double x;
        if (target.Index < visible.Count)
        {
            x = visible[target.Index].Bounds.X - GROUP_MARGIN;
        }
        else if (visible.Count > 0)
        {
            x = visible[^1].Bounds.Right + GROUP_MARGIN;
        }
        else
        {
            x = visual.Bounds.X + GROUP_MARGIN;
        }

        x = LayoutRounding.RoundToPixel(
            Math.Clamp(x - (thickness / 2), content.X, content.Right - thickness), dpiScale);
        line = new Rect(x, visual.Bounds.Y + GROUP_MARGIN, thickness, Math.Max(0, visual.Bounds.Height - (GROUP_MARGIN * 2)));
        return true;
    }

    private void RenderDropIndicator(IGraphicsContext context)
    {
        if (TryGetDropLine(out var line))
        {
            context.FillRectangle(line, Theme.Palette.Accent);
        }
    }
}
