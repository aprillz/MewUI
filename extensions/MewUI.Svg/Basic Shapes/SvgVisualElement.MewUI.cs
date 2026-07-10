using System.Globalization;
using System.Linq;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

using Svg.FilterEffects;
using Svg.ExtensionMethods;

namespace Svg;

public abstract partial class SvgVisualElement : SvgElement, ISvgBoundable, ISvgStylable, ISvgClipable
{
    public virtual PathGeometry? Path(ISvgRenderer renderer) => null;

    Point ISvgBoundable.Location => Bounds.Position;

    Size ISvgBoundable.Size => Bounds.Size;

    public virtual Rect Bounds => TransformedBounds(Path(null)?.GetBounds() ?? Rect.Empty);

    protected override void Render(ISvgRenderer renderer)
    {
        // Match SVG.NET exactly - the `||` short-circuit MATTERS: SvgGroup/SvgUse/etc
        // are non-Renderable, so Path(renderer) must NOT be called for them. Calling
        // it triggered SvgGroup.GetPaths → child SvgText.Path under the *parent's*
        // boundable, baking percent coords like x="50%" against the wrong viewport.
        if (Visible && Displayable && (!Renderable || Path(renderer) is not null))
        {
            RenderInternal(renderer, true);
        }
    }

    private void RenderInternal(ISvgRenderer renderer, bool renderFilter)
    {
        if (renderFilter && RenderFilter(renderer))
        {
            return;
        }

        try
        {
            if (PushTransforms(renderer))
            {
                var hasClip = SetClip(renderer);
                try
                {
                    float elementOpacity = FixOpacityValue(Opacity);
                    if (elementOpacity < 1f)
                    {
                        renderer.GraphicsContext.GlobalAlpha *= elementOpacity;
                    }

                    if (Renderable)
                    {
                        RenderFillAndStroke(renderer);
                    }
                    else
                    {
                        RenderChildren(renderer);
                    }
                }
                finally
                {
                    ResetClip(renderer, hasClip);
                }
            }
        }
        finally
        {
            PopTransforms(renderer);
        }
    }

    private bool RenderFilter(ISvgRenderer renderer)
    {
        var filterPath = Filter.ReplaceWithNullIfNone();
        if (filterPath is null)
        {
            return false;
        }

        var filter = OwnerDocument?.IdManager.GetElementById(filterPath) as SvgFilter;
        if (filter is null)
        {
            return false;
        }

        filter.ApplyFilter(this, renderer, r => RenderInternal(r, false));
        return true;
    }

    protected internal virtual void RenderFillAndStroke(ISvgRenderer renderer)
    {
        RenderFill(renderer);
        RenderStroke(renderer);
    }

    protected internal virtual void RenderFill(ISvgRenderer renderer)
    {
        if (Fill is null || Fill == SvgPaintServer.None)
        {
            return;
        }

        var path = Path(renderer);
        if (path is null || path.IsEmpty)
        {
            return;
        }

        var brush = Fill.GetBrush(this, renderer, FixOpacityValue(FillOpacity));
        if (brush is null)
        {
            return;
        }

        renderer.GraphicsContext.FillPath(
            path,
            brush,
            FillRule == SvgFillRule.NonZero
                ? Aprillz.MewUI.Rendering.FillRule.NonZero
                : Aprillz.MewUI.Rendering.FillRule.EvenOdd);
    }

    protected internal virtual bool RenderStroke(ISvgRenderer renderer)
    {
        if (Stroke is null || Stroke == SvgPaintServer.None || StrokeWidth <= 0f)
        {
            return false;
        }

        var path = Path(renderer);
        if (path is null || path.IsEmpty)
        {
            return false;
        }

        var brush = Stroke.GetBrush(this, renderer, FixOpacityValue(StrokeOpacity), true);
        if (brush is null)
        {
            return false;
        }

        var strokeWidth = StrokeWidth.ToDeviceValue(renderer, UnitRenderingType.Other, this);
        if (strokeWidth <= 0f)
        {
            return false;
        }

        var bounds = path.GetBounds();
        if (bounds.Width <= 0d && bounds.Height <= 0d)
        {
            if (!TryGetFirstPoint(path, out var firstPoint))
            {
                return false;
            }

            var capBounds = new Rect(
                firstPoint.X - (strokeWidth / 2d),
                firstPoint.Y - (strokeWidth / 2d),
                strokeWidth,
                strokeWidth);

            switch (StrokeLineCap)
            {
                case SvgStrokeLineCap.Round:
                    renderer.GraphicsContext.FillEllipse(capBounds, brush);
                    return true;

                case SvgStrokeLineCap.Square:
                    renderer.GraphicsContext.FillRectangle(capBounds, brush);
                    return true;
            }
        }

        IReadOnlyList<double>? dashArray = null;
        if (StrokeDashArray is { Count: > 0 })
        {
            var strokeDashArray = StrokeDashArray;
            if (strokeDashArray.Count % 2 != 0)
            {
                var duplicatedDashArray = new SvgUnitCollection();
                duplicatedDashArray.AddRange(strokeDashArray);
                duplicatedDashArray.AddRange(StrokeDashArray);
                strokeDashArray = duplicatedDashArray;
            }

            dashArray = strokeDashArray
                .Select(x => (double)(Math.Max(1f, x.ToDeviceValue(renderer, UnitRenderingType.Other, this)) / Math.Max(strokeWidth, 1f)))
                .ToArray();
        }

        var strokeStyle = new StrokeStyle
        {
            LineCap = StrokeLineCap switch
            {
                SvgStrokeLineCap.Round => Aprillz.MewUI.Rendering.StrokeLineCap.Round,
                SvgStrokeLineCap.Square => Aprillz.MewUI.Rendering.StrokeLineCap.Square,
                _ => Aprillz.MewUI.Rendering.StrokeLineCap.Flat,
            },
            LineJoin = StrokeLineJoin switch
            {
                SvgStrokeLineJoin.Bevel => Aprillz.MewUI.Rendering.StrokeLineJoin.Bevel,
                SvgStrokeLineJoin.Round => Aprillz.MewUI.Rendering.StrokeLineJoin.Round,
                _ => Aprillz.MewUI.Rendering.StrokeLineJoin.Miter,
            },
            MiterLimit = StrokeMiterLimit,
            DashArray = dashArray,
            DashOffset = StrokeDashOffset.ToDeviceValue(renderer, UnitRenderingType.Other, this) / Math.Max(strokeWidth, 1f),
        };

        // SVG stroke-width is in user space, which maps 1:1 to MewUI logical (DIP) units
        // here. MewUI backends now scale stroke with transform by default (Skia/WPF/SVG
        // semantics), so we pass the user-space width through unchanged.
        var pen = new Pen(brush, strokeWidth, strokeStyle);
        renderer.GraphicsContext.DrawPath(path, pen);
        return true;
    }

    private static bool TryGetFirstPoint(PathGeometry path, out Point point)
    {
        foreach (var command in path.Commands)
        {
            switch (command.Type)
            {
                case PathCommandType.MoveTo:
                case PathCommandType.LineTo:
                    point = new Point(command.X0, command.Y0);
                    return true;

                case PathCommandType.BezierTo:
                    point = new Point(command.X0, command.Y0);
                    return true;
            }
        }

        point = default;
        return false;
    }

    private bool _lastSetClipPushed;

    /// <summary>Pushes a clip Save only when a clip-path/clip attribute actually resolves
    /// to something drawable. Returns whether a Save was pushed - callers must pass this
    /// back into <see cref="ResetClip(ISvgRenderer, bool)"/> so Restore only fires when
    /// Save did, instead of re-deriving the same condition (which can drift out of sync,
    /// e.g. when ClipPath references a missing/empty clip-path and Save is skipped but a
    /// Restore-on-presence check would still fire).</summary>
    protected internal virtual bool SetClip(ISvgRenderer renderer)
    {
        var hasClip = false;

        if (ClipPath is not null)
        {
            var clipPath = OwnerDocument?.IdManager.GetElementById(ClipPath) as SvgClipPath;
            var clipGeometry = clipPath?.GetClipPath(this, renderer);
            if (clipGeometry is { IsEmpty: false })
            {
                renderer.GraphicsContext.Save();
                renderer.GraphicsContext.SetClipPath(clipGeometry);
                hasClip = true;
            }
        }

        var clip = Clip;
        if (!string.IsNullOrEmpty(clip) && clip.StartsWith("rect(", StringComparison.Ordinal))
        {
            var offsets = clip.Substring(5, clip.Length - 6)
                .Split(',')
                .Select(x => double.Parse(x.Trim(), CultureInfo.InvariantCulture))
                .ToArray();
            if (offsets.Length == 4)
            {
                var bounds = Bounds;
                if (!hasClip)
                {
                    renderer.GraphicsContext.Save();
                    hasClip = true;
                }

                renderer.IntersectClip(new Rect(
                    bounds.Left + offsets[3],
                    bounds.Top + offsets[0],
                    Math.Max(0, bounds.Width - (offsets[3] + offsets[1])),
                    Math.Max(0, bounds.Height - (offsets[2] + offsets[0]))));
            }
        }

        _lastSetClipPushed = hasClip;
        return hasClip;
    }

    /// <summary>Restores the clip Save pushed by <see cref="SetClip"/>, if any. Takes the
    /// exact boolean SetClip returned rather than re-checking ClipPath/Clip, so this can
    /// never Restore without a matching Save.</summary>
    protected internal virtual void ResetClip(ISvgRenderer renderer, bool hasClip)
    {
        if (hasClip)
        {
            renderer.GraphicsContext.Restore();
        }
    }

    void ISvgClipable.SetClip(ISvgRenderer renderer) => SetClip(renderer);

    void ISvgClipable.ResetClip(ISvgRenderer renderer) => ResetClip(renderer, _lastSetClipPushed);
}
