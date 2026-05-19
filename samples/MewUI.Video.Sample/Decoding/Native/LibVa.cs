using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Video.Sample.Decoding;

/// <summary>
/// Minimal libva (VA-API) P/Invoke surface needed to export a decoded VA surface as
/// a DRM PRIME dma_buf for EGL/GL zero-copy import. Only the calls used by the
/// VAAPI → EGL → GL path are wrapped — extending to other VA-API features (encode,
/// post-processing) is out of scope.
/// </summary>
internal static unsafe partial class LibVa
{
    private const string LibraryName = "libva.so.2";

    public const int VA_STATUS_SUCCESS = 0x00000000;

    // mem_type for vaExportSurfaceHandle. DRM_PRIME_2 (0x00100000) is the modern
    // descriptor that exposes per-plane DRM modifiers; DRM_PRIME (0x00100000-1) is
    // the legacy single-plane variant. We always use _2.
    public const uint VA_SURFACE_ATTRIB_MEM_TYPE_DRM_PRIME_2 = 0x00100000;

    // Flags for vaExportSurfaceHandle. SEPARATE_LAYERS gives one layer per plane
    // (matches what EGL_LINUX_DMA_BUF_EXT consumes when importing NV12/YUV).
    public const uint VA_EXPORT_SURFACE_READ_ONLY = 0x0001;
    public const uint VA_EXPORT_SURFACE_SEPARATE_LAYERS = 0x0004;
    public const uint VA_EXPORT_SURFACE_COMPOSED_LAYERS = 0x0008;

    public const int VA_DRM_PRIME_SURFACE_MAX_LAYERS = 4;
    public const int VA_DRM_PRIME_SURFACE_MAX_PLANES = 4;

    /// <summary>
    /// Layout matches <c>VADRMPRIMESurfaceDescriptor</c> from <c>va/va_drmcommon.h</c>.
    /// Total size: 4*4 + 4*(4+4+8) + 4 + 4*(4+4 + 4*4 + 4*4 + 4*4) bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VADRMPRIMESurfaceDescriptor
    {
        public uint Fourcc;
        public uint Width;
        public uint Height;
        public uint NumObjects;
        public PrimeObject Object0;
        public PrimeObject Object1;
        public PrimeObject Object2;
        public PrimeObject Object3;
        public uint NumLayers;
        public PrimeLayer Layer0;
        public PrimeLayer Layer1;
        public PrimeLayer Layer2;
        public PrimeLayer Layer3;

        public PrimeObject ObjectAt(int i) => i switch
        {
            0 => Object0,
            1 => Object1,
            2 => Object2,
            3 => Object3,
            _ => throw new ArgumentOutOfRangeException(nameof(i)),
        };

        public PrimeLayer LayerAt(int i) => i switch
        {
            0 => Layer0,
            1 => Layer1,
            2 => Layer2,
            3 => Layer3,
            _ => throw new ArgumentOutOfRangeException(nameof(i)),
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PrimeObject
    {
        public int Fd;
        public uint Size;
        public ulong DrmFormatModifier;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PrimeLayer
    {
        public uint DrmFormat;
        public uint NumPlanes;
        public PlaneIndices ObjectIndex;
        public PlaneOffsets Offset;
        public PlanePitches Pitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PlaneIndices
    {
        public uint P0, P1, P2, P3;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PlaneOffsets
    {
        public uint P0, P1, P2, P3;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PlanePitches
    {
        public uint P0, P1, P2, P3;
    }

    /// <summary>
    /// Block until the GPU has finished decoding into <paramref name="surfaceId"/>.
    /// Required before exporting via <see cref="vaExportSurfaceHandle"/> — without
    /// this, the consumer can sample partially-decoded pixels.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "vaSyncSurface")]
    public static partial int vaSyncSurface(nint display, uint surfaceId);

    /// <summary>
    /// Export a VA surface as a DRM PRIME descriptor (one or more dma_buf fds plus
    /// per-plane layout). The caller owns the returned fds and must
    /// <c>close(2)</c> them when the import is no longer needed (EGL takes its own
    /// references during <c>eglCreateImage</c>).
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "vaExportSurfaceHandle")]
    public static partial int vaExportSurfaceHandle(
        nint display,
        uint surfaceId,
        uint memType,
        uint flags,
        out VADRMPRIMESurfaceDescriptor descriptor);
}
