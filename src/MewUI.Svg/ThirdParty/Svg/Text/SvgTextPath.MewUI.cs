using Aprillz.MewUI.Rendering;

namespace Svg;

public partial class SvgTextPath
{
    protected override PathGeometry? GetBaselinePath(ISvgRenderer renderer)
    {
        var path = OwnerDocument?.IdManager.GetElementById(ReferencedPath) as SvgVisualElement;
        if (path is null)
        {
            return null;
        }

        var pathData = path.Path(renderer);
        if (pathData is null || pathData.IsEmpty)
        {
            return null;
        }

        if (path.Transforms is not { Count: > 0 })
        {
            return pathData;
        }

        return MewSvgPathUtilities.TransformPath(pathData, path.Transforms.GetMatrix());
    }

    protected override double GetAuthorPathLength()
    {
        return OwnerDocument?.IdManager.GetElementById(ReferencedPath) is SvgPath path ? path.PathLength : 0;
    }
}
