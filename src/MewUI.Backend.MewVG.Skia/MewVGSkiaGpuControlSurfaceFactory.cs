namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// Shared Skia retained-surface factory used by the concrete MewVG backends.
/// </summary>
public static class MewVGSkiaGpuControlSurfaceFactory
{
    public static ISkiaGpuControlSurface Create(
        IDisposable resources,
        int pixelWidth,
        int pixelHeight,
        double dpiScale)
    {
        ArgumentNullException.ThrowIfNull(resources);

        return resources switch
        {
            IMewVGMetalWindowInterop metal => new MewVGMetalSkiaControlSurface(metal, pixelWidth, pixelHeight, dpiScale),
            IMewVGGlWindowInterop gl => new MewVGGlSkiaControlSurface(gl, pixelWidth, pixelHeight, dpiScale),
            _ => throw new ArgumentException(
                "Skia GPU surfaces require MewVG window resources that expose the generic external-rendering interop contracts.",
                nameof(resources))
        };
    }
}
