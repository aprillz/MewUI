using Aprillz.MewUI;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Factory interface for creating graphics resources.
/// Allows different graphics backends to be plugged in.
/// </summary>
public interface IGraphicsFactory
{
    /// <summary>
    /// Identifies which built-in backend this factory represents.
    /// Custom factories can return <see cref="GraphicsBackend.Custom"/>.
    /// </summary>
    GraphicsBackend Backend { get; }

    /// <summary>
    /// Creates a font resource.
    /// </summary>
    IFont CreateFont(string family, double size, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false);

    /// <summary>
    /// Creates a font resource for a specific DPI.
    /// Font size is specified in DIPs (1/96 inch).
    /// </summary>
    IFont CreateFont(string family, double size, uint dpi, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false);

    /// <summary>
    /// Creates an image from a file path.
    /// </summary>
    IImage CreateImageFromFile(string path);

    /// <summary>
    /// Creates an image from a byte array.
    /// </summary>
    IImage CreateImageFromBytes(byte[] data);

    /// <summary>
    /// Creates an image backed by a versioned pixel source (e.g. <see cref="WriteableBitmap"/>).
    /// Backends should reflect updates when the source's <see cref="IPixelBufferSource.Version"/> changes.
    /// </summary>
    IImage CreateImageFromPixelSource(IPixelBufferSource source);

    /// <summary>
    /// Creates a graphics context for the specified window handle.
    /// </summary>
    IGraphicsContext CreateContext(nint hwnd, nint hdc, double dpiScale);

    /// <summary>
    /// Creates a measurement-only graphics context for text measurement.
    /// </summary>
    IGraphicsContext CreateMeasurementContext(uint dpi);
}

/// <summary>
/// Optional capability for factories that must release per-window resources when a window is destroyed.
/// </summary>
public interface IWindowResourceReleaser
{
    void ReleaseWindowResources(nint hwnd);
}
