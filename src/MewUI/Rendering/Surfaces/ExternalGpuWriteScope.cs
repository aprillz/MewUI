namespace Aprillz.MewUI.Rendering;

/// <summary>
/// A scope handed back from <see cref="IExternalWritableGpuSurface.BeginExternalWrite"/> that
/// exposes the writable GPU texture (and any auxiliary handles) needed by external code to
/// render into the backing surface. Concrete handle semantics depend on the active rendering
/// backend; see the per-API mapping below.
/// </summary>
/// <remarks>
/// Handle slot conventions:
/// <list type="bullet">
///   <item>GL — NativeHandle = texture id, NativeAlternateHandle = framebuffer id, NativeDeviceHandle = 0</item>
///   <item>Metal — NativeHandle = MTLTexture*, NativeAlternateHandle = MTLCommandQueue*, NativeDeviceHandle = MTLDevice*</item>
///   <item>D3D11 — NativeHandle = ID3D11Texture2D*, NativeAlternateHandle = IDXGISurface*, NativeDeviceHandle = ID3D11Device*</item>
/// </list>
/// </remarks>
public interface IExternalGpuWriteScope : IDisposable
{
    int PixelWidth { get; }

    int PixelHeight { get; }

    bool YFlipped { get; }

    /// <summary>Primary writable native handle (texture).</summary>
    nint NativeHandle { get; }

    /// <summary>Auxiliary handle (FBO id / CommandQueue / IDXGISurface).</summary>
    nint NativeAlternateHandle { get; }

    /// <summary>Device handle (MTLDevice / ID3D11Device). <c>0</c> when the API has no explicit device (GL).</summary>
    nint NativeDeviceHandle { get; }

    void Flush();
}

public interface IExternalWritableGpuSurface : IRenderSurface
{
    IExternalGpuWriteScope BeginExternalWrite();

    void MarkExternalContentChanged();
}
