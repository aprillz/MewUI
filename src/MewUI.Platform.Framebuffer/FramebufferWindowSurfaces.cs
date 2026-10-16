namespace Aprillz.MewUI.Platform.Linux.Framebuffer;

/// <summary>
/// Window surface backed by a GBM surface on a DRM/KMS output. A graphics backend renders into
/// <see cref="GbmSurface"/> through EGL and calls <see cref="OnBuffersSwapped"/> after each swap.
/// </summary>
public interface IDrmKmsWindowSurface : IWindowSurface
{
    /// <summary>The gbm_device the EGL display is created on.</summary>
    nint GbmDevice { get; }

    /// <summary>The gbm_surface the EGL window surface is created on.</summary>
    nint GbmSurface { get; }

    /// <summary>The GBM pixel format of <see cref="GbmSurface"/>; the EGL config must report it as its native visual.</summary>
    uint GbmFormat { get; }

    /// <summary>Schedules the buffer the last eglSwapBuffers produced for scanout.</summary>
    void OnBuffersSwapped();
}

/// <summary>
/// Window surface backed by memory the CPU writes, such as a mapped fbdev device. A graphics backend renders
/// elsewhere and copies the changed rows in with <see cref="CopyRows"/>.
/// </summary>
public interface ICpuFramebufferWindowSurface : IWindowSurface
{
    /// <summary>Copies BGRA rows (top-down, premultiplied, alpha ignored) to the screen rectangle at <paramref name="x"/>, <paramref name="y"/>.</summary>
    void CopyRows(ReadOnlySpan<byte> bgra, int strideBytes, int x, int y, int width, int height);

    /// <summary>Waits for the next vertical blank where the device reports it; returns at once otherwise.</summary>
    void WaitForVerticalBlank();
}
