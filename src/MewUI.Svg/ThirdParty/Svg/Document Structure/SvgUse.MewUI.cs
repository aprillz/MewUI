using System.Numerics;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Svg;

public partial class SvgUse
{
    protected internal override bool PushTransforms(ISvgRenderer renderer)
    {
        if (!base.PushTransforms(renderer))
        {
            return false;
        }

        renderer.Transform = Matrix3x2.CreateTranslation(
            X.ToDeviceValue(renderer, UnitRenderingType.Horizontal, this),
            Y.ToDeviceValue(renderer, UnitRenderingType.Vertical, this)) * renderer.Transform;
        return true;
    }

    public override PathGeometry? Path(ISvgRenderer renderer)
    {
        var element = OwnerDocument.IdManager.GetElementById(ReferencedElement) as SvgVisualElement;
        return element is not null && !HasRecursiveReference()
            ? element.Path(renderer)
            : null;
    }

    public override Rect Bounds
    {
        get
        {
            var width = Width.ToDeviceValue(null, UnitRenderingType.Horizontal, this);
            var height = Height.ToDeviceValue(null, UnitRenderingType.Vertical, this);
            if (width > 0 && height > 0)
            {
                var location = Location.ToDeviceValue(null, this);
                return TransformedBounds(new Rect(location, new Size(width, height)));
            }

            var element = OwnerDocument.IdManager.GetElementById(ReferencedElement) as SvgVisualElement;
            return element?.Bounds ?? Rect.Empty;
        }
    }

    protected override void RenderChildren(ISvgRenderer renderer)
    {
        if (ReferencedElement is null || HasRecursiveReference())
        {
            return;
        }

        var element = OwnerDocument.IdManager.GetElementById(ReferencedElement) as SvgVisualElement;
        if (element is null)
        {
            return;
        }

        var width = Width.ToDeviceValue(renderer, UnitRenderingType.Horizontal, this);
        var height = Height.ToDeviceValue(renderer, UnitRenderingType.Vertical, this);

        renderer.Save();
        try
        {
            if (width > 0 && height > 0)
            {
                var viewBox = element.Attributes.GetAttribute<SvgViewBox>("viewBox");
                if (viewBox != SvgViewBox.Empty &&
                    viewBox.Width > 0 &&
                    viewBox.Height > 0 &&
                    (Math.Abs(width - viewBox.Width) > float.Epsilon || Math.Abs(height - viewBox.Height) > float.Epsilon))
                {
                    renderer.Transform = Matrix3x2.CreateScale(width / viewBox.Width, height / viewBox.Height) * renderer.Transform;
                }
            }

            var originalParent = element.Parent;
            element._parent = this;
            element.InvalidateChildPaths();
            element.RenderElement(renderer);
            element._parent = originalParent;
        }
        finally
        {
            renderer.Restore();
        }
    }
}
