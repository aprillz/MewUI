namespace Aprillz.MewUI.Rendering;

/// <summary>
/// A scope handed back from <see cref="IExternalWritableGpuSurface.BeginExternalWrite"/> that
/// exposes the writable GPU texture (and any auxiliary handles) needed by external code to
/// render into the backing surface. Each handle carries the role documented on its property;
/// the concrete native type behind it is defined by the backend that produced the scope.
/// </summary>
public interface IExternalGpuWriteScope : IDisposable
{
    int PixelWidth { get; }

    int PixelHeight { get; }

    bool YFlipped { get; }

    /// <summary>Primary writable native handle (texture).</summary>
    nint NativeHandle { get; }

    /// <summary>Auxiliary handle: the render target or command queue the primary texture is written through.</summary>
    nint NativeAlternateHandle { get; }

    /// <summary>Device handle. <c>0</c> when the backend has no explicit device object.</summary>
    nint NativeDeviceHandle { get; }

    void Flush();
}

public interface IExternalWritableGpuSurface : IRenderSurface
{
    IExternalGpuWriteScope BeginExternalWrite();

    void MarkExternalContentChanged();
}
