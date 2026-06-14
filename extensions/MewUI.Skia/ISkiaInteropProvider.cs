using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Skia;

/// <summary>
/// Provider registered with <see cref="SkiaInterop"/> that knows how to bridge a Skia render
/// into a specific MewUI backend (e.g. Direct2D, MewVG.Win32). Each Interop NuGet package
/// ships exactly one provider and exposes a <c>Use()</c> entry point that registers it.
/// </summary>
public interface ISkiaInteropProvider
{
    /// <summary>Backend identifier this provider bridges to (matches <see cref="IGraphicsFactory.Backend"/>).</summary>
    string BackendIdentifier { get; }

    /// <summary>Creates a surface host bound to the given factory. May return <see langword="null"/> when the factory is not compatible at runtime (e.g. driver missing an extension).</summary>
    ISkiaSurfaceHost? TryCreateSurfaceHost(IGraphicsFactory factory);
}
