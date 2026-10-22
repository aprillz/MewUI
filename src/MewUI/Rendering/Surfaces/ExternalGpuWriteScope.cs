namespace Aprillz.MewUI.Rendering;

/// <summary>
/// A scope handed back from <see cref="IExternalWritableGpuSurface.BeginExternalWrite"/> that
/// exposes the writable GPU texture (and any auxiliary handles) needed by external code to
/// render into the backing surface. Each handle carries the role documented on its property;
/// the concrete native type behind it is defined by the backend that produced the scope.
/// </summary>
public interface IExternalGpuWriteScope : IDisposable
{
    /// <summary>Gets the width in device pixels.</summary>
    int PixelWidth { get; }

    /// <summary>Gets the height in device pixels.</summary>
    int PixelHeight { get; }

    /// <summary>Gets whether row 0 is the bottom of the texture.</summary>
    bool YFlipped { get; }

    /// <summary>Primary writable native handle (texture).</summary>
    nint NativeHandle { get; }

    /// <summary>Auxiliary handle: the render target or command queue the primary texture is written through.</summary>
    nint NativeAlternateHandle { get; }

    /// <summary>Device handle. <c>0</c> when the backend has no explicit device object.</summary>
    nint NativeDeviceHandle { get; }

    /// <summary>Makes the external writes visible to the backend before the scope is disposed.</summary>
    void Flush();
}

/// <summary>A surface that external GPU code renders into through a write scope.</summary>
public interface IExternalWritableGpuSurface : IRenderSurface
{
    /// <summary>Begins an external write; dispose the returned scope to end it.</summary>
    IExternalGpuWriteScope BeginExternalWrite();

    /// <summary>Advances the surface version after external code changed the contents outside a write scope.</summary>
    void MarkExternalContentChanged();
}
