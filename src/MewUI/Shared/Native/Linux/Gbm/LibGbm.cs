using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

// Mesa GBM: scanout-capable buffers that EGL renders into and KMS displays.
internal static unsafe partial class LibGbm
{
    private const string LIBRARY_NAME = "libgbm.so.1";

    public const uint GBM_FORMAT_XRGB8888 = 0x34325258;
    public const uint GBM_FORMAT_ARGB8888 = 0x34325241;
    public const uint GBM_BO_USE_SCANOUT = 1 << 0;
    public const uint GBM_BO_USE_CURSOR = 1 << 1;
    public const uint GBM_BO_USE_RENDERING = 1 << 2;
    public const uint GBM_BO_USE_WRITE = 1 << 3;

    [LibraryImport(LIBRARY_NAME)]
    public static partial nint gbm_create_device(int fd);

    [LibraryImport(LIBRARY_NAME)]
    public static partial void gbm_device_destroy(nint device);

    [LibraryImport(LIBRARY_NAME)]
    public static partial ulong gbm_bo_get_handle(nint bo);

    [LibraryImport(LIBRARY_NAME)]
    public static partial uint gbm_bo_get_stride(nint bo);

    [LibraryImport(LIBRARY_NAME)]
    public static partial uint gbm_bo_get_offset(nint bo, int plane);

    [LibraryImport(LIBRARY_NAME)]
    public static partial uint gbm_bo_get_width(nint bo);

    [LibraryImport(LIBRARY_NAME)]
    public static partial uint gbm_bo_get_height(nint bo);

    [LibraryImport(LIBRARY_NAME)]
    public static partial ulong gbm_bo_get_modifier(nint bo);

    /// <summary>A new dma-buf file descriptor for the buffer; the caller closes it.</summary>
    [LibraryImport(LIBRARY_NAME)]
    public static partial int gbm_bo_get_fd(nint bo);

    [LibraryImport(LIBRARY_NAME)]
    public static partial nint gbm_bo_create(nint device, uint width, uint height, uint format, uint flags);

    [LibraryImport(LIBRARY_NAME)]
    public static partial int gbm_bo_write(nint bo, void* buffer, nuint count);

    [LibraryImport(LIBRARY_NAME)]
    public static partial void gbm_bo_destroy(nint bo);
}
