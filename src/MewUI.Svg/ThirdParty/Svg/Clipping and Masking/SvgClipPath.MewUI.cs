using System.Numerics;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Svg;

public partial class SvgClipPath
{
    private PathGeometry? _path;

    public PathGeometry? GetClipPath(SvgVisualElement owner, ISvgRenderer renderer)
    {
        if (_path is null || IsPathDirty)
        {
            var combined = new PathGeometry();
            foreach (var element in Children)
            {
                CombinePaths(combined, element, renderer);
            }

            _path = combined.IsEmpty ? null : combined;
            IsPathDirty = false;
        }

        if (_path is null)
        {
            return null;
        }

        if (ClipPathUnits == SvgCoordinateUnits.ObjectBoundingBox)
        {
            var bounds = owner.Bounds;
            var matrix =
                Matrix3x2.CreateScale((float)bounds.Width, (float)bounds.Height) *
                Matrix3x2.CreateTranslation((float)bounds.Left, (float)bounds.Top);
            return MewSvgPathUtilities.TransformPath(_path, matrix);
        }

        return _path;
    }

    public Rect GetClipBounds(SvgVisualElement owner, ISvgRenderer renderer)
    {
        return GetClipPath(owner, renderer)?.GetBounds() ?? Rect.Empty;
    }

    private void CombinePaths(PathGeometry path, SvgElement element, ISvgRenderer renderer)
    {
        if (element is SvgVisualElement visual)
        {
            var childPath = visual.Path(renderer);
            if (childPath is not null && !childPath.IsEmpty)
            {
                path.FillRule = visual.ClipRule == SvgClipRule.NonZero
                    ? Aprillz.MewUI.Rendering.FillRule.NonZero
                    : Aprillz.MewUI.Rendering.FillRule.EvenOdd;

                var candidate = childPath;
                if (visual.Transforms is { Count: > 0 })
                {
                    candidate = MewSvgPathUtilities.TransformPath(childPath, visual.Transforms.GetMatrix());
                }

                path.AddPath(candidate);
            }
        }

        foreach (var child in element.Children)
        {
            CombinePaths(path, child, renderer);
        }
    }

    protected override void Render(ISvgRenderer renderer)
    {
    }
}
