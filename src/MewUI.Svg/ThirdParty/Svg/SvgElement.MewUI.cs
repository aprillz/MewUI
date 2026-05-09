using System.Numerics;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Svg;

public abstract partial class SvgElement
{
    protected internal virtual bool PushTransforms(ISvgRenderer renderer)
    {
        renderer.Save();

        var transforms = Transforms;
        if (transforms is null || transforms.Count == 0)
        {
            return true;
        }

        var matrix = transforms.GetMatrix();
        if (!IsFinite(matrix))
        {
            return false;
        }

        renderer.Transform = matrix * renderer.Transform;
        return true;
    }

    protected internal virtual void PopTransforms(ISvgRenderer renderer)
    {
        renderer.Restore();
    }

    void ISvgTransformable.PushTransforms(ISvgRenderer renderer) => PushTransforms(renderer);

    void ISvgTransformable.PopTransforms(ISvgRenderer renderer) => PopTransforms(renderer);

    protected Rect TransformedBounds(Rect bounds)
    {
        if (Transforms is null || Transforms.Count == 0 || bounds.IsEmpty)
        {
            return bounds;
        }

        var matrix = Transforms.GetMatrix();
        var p1 = Vector2.Transform(new Vector2((float)bounds.Left, (float)bounds.Top), matrix);
        var p2 = Vector2.Transform(new Vector2((float)bounds.Right, (float)bounds.Top), matrix);
        var p3 = Vector2.Transform(new Vector2((float)bounds.Right, (float)bounds.Bottom), matrix);
        var p4 = Vector2.Transform(new Vector2((float)bounds.Left, (float)bounds.Bottom), matrix);

        var minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
        var minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
        var maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
        var maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    public void RenderElement(ISvgRenderer renderer)
    {
        Render(renderer);
    }

    protected virtual void Render(ISvgRenderer renderer)
    {
        try
        {
            if (PushTransforms(renderer))
            {
                RenderChildren(renderer);
            }
        }
        finally
        {
            PopTransforms(renderer);
        }
    }

    protected virtual void RenderChildren(ISvgRenderer renderer)
    {
        foreach (var element in Children)
        {
            element.Render(renderer);
        }
    }

    void ISvgElement.Render(ISvgRenderer renderer) => Render(renderer);

    protected PathGeometry GetPaths(SvgElement element, ISvgRenderer renderer)
    {
        var result = new PathGeometry();
        foreach (var child in element.Children)
        {
            if (child is SvgSymbol)
            {
                continue;
            }

            if (child is SvgVisualElement visual)
            {
                var path = visual.Path(renderer);
                if (path is not null && !path.IsEmpty)
                {
                    result.AddPath(path);
                }
            }

            if (child is not SvgPaintServer && child.Children.Count > 0)
            {
                var descendant = GetPaths(child, renderer);
                if (!descendant.IsEmpty)
                {
                    result.AddPath(descendant);
                }
            }
        }

        return result;
    }

    private static bool IsFinite(Matrix3x2 matrix)
    {
        return float.IsFinite(matrix.M11) &&
               float.IsFinite(matrix.M12) &&
               float.IsFinite(matrix.M21) &&
               float.IsFinite(matrix.M22) &&
               float.IsFinite(matrix.M31) &&
               float.IsFinite(matrix.M32);
    }
}
