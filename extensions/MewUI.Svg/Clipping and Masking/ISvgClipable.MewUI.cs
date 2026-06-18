using System;

namespace Svg
{
    /// <summary>
    /// Defines the methods and properties that an <see cref="SvgElement"/> must implement to support clipping.
    /// </summary>
    public interface ISvgClipable
    {
        Uri ClipPath { get; set; }

        SvgClipRule ClipRule { get; set; }

        void SetClip(ISvgRenderer renderer);

        void ResetClip(ISvgRenderer renderer);
    }
}
