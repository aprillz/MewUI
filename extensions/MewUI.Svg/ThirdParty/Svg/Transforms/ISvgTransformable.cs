using Svg.Transforms;

namespace Svg
{
    /// <summary>
    /// Represents and element that may be transformed.
    /// </summary>
    public interface ISvgTransformable
    {
        SvgTransformCollection Transforms { get; set; }
        void PushTransforms(ISvgRenderer renderer);
        void PopTransforms(ISvgRenderer renderer);
    }
}
