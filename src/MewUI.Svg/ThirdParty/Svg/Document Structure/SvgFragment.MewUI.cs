using Aprillz.MewUI;

namespace Svg;

public partial class SvgFragment : ISvgBoundable
{
    Point ISvgBoundable.Location => default;

    Size ISvgBoundable.Size
    {
        get
        {
            if (Width.Type == SvgUnitType.Percentage || Height.Type == SvgUnitType.Percentage)
            {
                return Size.Empty;
            }

            return GetDimensions();
        }
    }

    Rect ISvgBoundable.Bounds => new(((ISvgBoundable)this).Location, ((ISvgBoundable)this).Size);

    protected internal override bool PushTransforms(ISvgRenderer renderer)
    {
        if (!base.PushTransforms(renderer))
        {
            return false;
        }

        var vb = ViewBox;
        var vbXform = vb.GetViewBoxTransform(AspectRatio, renderer, this);
        renderer.Transform = vbXform * renderer.Transform;
        return true;
    }

    protected override void Render(ISvgRenderer renderer)
    {
        switch (Overflow)
        {
            case SvgOverflow.Auto:
            case SvgOverflow.Visible:
            case SvgOverflow.Inherit:
                base.Render(renderer);
                break;
            default:
                var size = this is SvgDocument
                    ? renderer.GetBoundable().Bounds.Size
                    : GetDimensions(renderer);
                var clip = new Rect(
                    X.ToDeviceValue(renderer, UnitRenderingType.Horizontal, this),
                    Y.ToDeviceValue(renderer, UnitRenderingType.Vertical, this),
                    size.Width,
                    size.Height);
                renderer.Save();
                try
                {
                    renderer.IntersectClip(clip);
                    renderer.SetBoundable(new GenericBoundable(clip));
                    try
                    {
                        base.Render(renderer);
                    }
                    finally
                    {
                        renderer.PopBoundable();
                    }
                }
                finally
                {
                    renderer.Restore();
                }
                break;
        }
    }

    public Rect Bounds
    {
        get
        {
            var bounds = Rect.Empty;
            foreach (var child in Children)
            {
                var childBounds = Rect.Empty;
                if (child is SvgFragment fragment)
                {
                    childBounds = fragment.Bounds;
                    if (!childBounds.IsEmpty)
                    {
                        childBounds = new Rect(
                            childBounds.X + fragment.X.ToDeviceValue(null, UnitRenderingType.Horizontal, fragment),
                            childBounds.Y + fragment.Y.ToDeviceValue(null, UnitRenderingType.Vertical, fragment),
                            childBounds.Width,
                            childBounds.Height);
                    }
                }
                else if (child is SvgVisualElement visual)
                {
                    childBounds = visual.Bounds;
                }

                if (childBounds.IsEmpty)
                {
                    continue;
                }

                bounds = bounds.IsEmpty ? childBounds : Union(bounds, childBounds);
            }

            return TransformedBounds(bounds);
        }
    }

    public Size GetDimensions() => GetDimensions(null);

    internal Size GetDimensions(ISvgRenderer renderer)
    {
        var isWidthPercent = Width.Type == SvgUnitType.Percentage;
        var isHeightPercent = Height.Type == SvgUnitType.Percentage;

        Rect bounds = Rect.Empty;
        if (isWidthPercent || isHeightPercent)
        {
            if (ViewBox.Width > 0 && ViewBox.Height > 0)
            {
                bounds = new Rect(ViewBox.MinX, ViewBox.MinY, ViewBox.Width, ViewBox.Height);
            }
            else
            {
                bounds = Bounds;
            }
        }

        var width = isWidthPercent && this is SvgDocument
            ? (bounds.Width + bounds.X) * (Width.Value * 0.01)
            : Width.ToDeviceValue(renderer, UnitRenderingType.Horizontal, this);
        var height = isHeightPercent && this is SvgDocument
            ? (bounds.Height + bounds.Y) * (Height.Value * 0.01)
            : Height.ToDeviceValue(renderer, UnitRenderingType.Vertical, this);

        return new Size(width, height);
    }

    private static Rect Union(Rect a, Rect b)
    {
        var left = Math.Min(a.Left, b.Left);
        var top = Math.Min(a.Top, b.Top);
        var right = Math.Max(a.Right, b.Right);
        var bottom = Math.Max(a.Bottom, b.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }
}
