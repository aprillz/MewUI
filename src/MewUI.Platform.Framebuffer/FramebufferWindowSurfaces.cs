namespace Aprillz.MewUI.Platform.Linux.Framebuffer;

/// <summary>
/// Window surface backed by a ring of GBM buffers on a DRM/KMS output. A graphics backend takes a buffer with
/// <see cref="AcquireBuffer"/>, renders into it through EGL and hands it back with <see cref="Present"/>; the
/// platform owns the buffers and the page flips.
/// </summary>
public interface IDrmKmsWindowSurface : IWindowSurface
{
    /// <summary>The gbm_device the EGL display is created on.</summary>
    nint GbmDevice { get; }

    /// <summary>The GBM pixel format of every buffer; the EGL config must report it as its native visual.</summary>
    uint GbmFormat { get; }

    /// <summary>
    /// The gbm_bo the next frame draws into: one no flip holds, or the one a frame that never presented left
    /// behind. 0 when every buffer is on its way to the screen.
    /// </summary>
    nint AcquireBuffer();

    /// <summary>Describes <paramref name="buffer"/> for import as a dma-buf; the caller closes the descriptor.</summary>
    DrmKmsBufferExport ExportBuffer(nint buffer);

    /// <summary>
    /// How many presents ago <paramref name="buffer"/> was last presented (1 would be the newest frame), or 0
    /// when it has never been presented and holds nothing known.
    /// </summary>
    int GetBufferAge(nint buffer);

    /// <summary>
    /// Schedules <paramref name="buffer"/> for scanout. Its GL commands must have been flushed; the flip waits
    /// for them to finish through the kernel's fence on the buffer.
    /// </summary>
    void Present(nint buffer);
}

/// <summary>A scan-out buffer as a dma-buf: a new descriptor the receiver closes, and the layout of its single plane.</summary>
public readonly record struct DrmKmsBufferExport(int DmaBufFd, uint Offset, uint Pitch, ulong Modifier)
{
    private const ulong DRM_FORMAT_MOD_INVALID = 0x00FFFFFFFFFFFFFF;

    /// <summary>True when the allocator reported a layout modifier that an import must carry along.</summary>
    public bool HasModifier => Modifier != DRM_FORMAT_MOD_INVALID;
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
