namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Abstract interface for pen resources used to stroke outlines.
/// <para>
/// Pens are created by <see cref="IGraphicsFactory"/> and are backend-specific.
/// They must be disposed when no longer needed.
/// </para>
/// </summary>
public interface IPen : IDisposable
{
    /// <summary>Gets the brush used to paint the stroke.</summary>
    IBrush Brush { get; }

    /// <summary>Gets the stroke thickness in device-independent pixels (DIPs).</summary>
    double Thickness { get; }

    /// <summary>Gets the stroke style (line cap, line join, miter limit).</summary>
    StrokeStyle StrokeStyle { get; }
}
