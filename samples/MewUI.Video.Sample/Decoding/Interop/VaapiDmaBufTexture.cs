using System.Runtime.InteropServices;

using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Video.Sample.Diagnostics;

namespace Aprillz.MewUI.Video.Sample.Decoding;

/// <summary>
/// Wraps a VA-API decoded surface as an <see cref="IExternalRasterSource"/> by
/// exporting the surface as a DRM PRIME dma_buf and importing it as an EGLImage
/// bound to a GL texture. Enables zero-copy sampling of hardware-decoded video on
/// Linux when running against a Mesa driver (Intel iHD, AMD radeonsi, Nouveau).
/// </summary>
/// <remarks>
/// <para>
/// <b>Format note</b>: VAAPI typically decodes to NV12 (two planes: Y as R8, UV as
/// GR88). The export below requests <c>VA_EXPORT_SURFACE_COMPOSED_LAYERS</c> so a
/// single EGLImage represents the whole surface — but the resulting texture is a
/// YUV image that NVG's <c>sampler2D</c> shader can't render directly. To make the
/// pixels usable, the upstream pipeline must either:
/// <list type="bullet">
///   <item>Configure VAAPI to decode straight to BGRA via VPP (preferred), or</item>
///   <item>Render the YUV texture into an RGB FBO with a YUV→RGB shader before
///         handing it to NVG.</item>
/// </list>
/// This file does the GPU memory plumbing only; the YUV→RGB step is left to the
/// caller. As a first integration point it's still useful — it replaces the
/// per-frame CPU readback + texture upload with a one-time dma_buf import.
/// </para>
/// <para>
/// <b>Driver support</b>: requires Mesa's GLX/EGL state sharing. NVIDIA proprietary
/// driver doesn't share state between GLX and EGL — the EGLImage created here will
/// fail to bind in the GLX context. Caller should fall back to the CPU path on
/// failure.
/// </para>
/// </remarks>
internal sealed unsafe class VaapiDmaBufTexture : IExternalRasterSource
{
    private readonly nint _vaDisplay;
    private readonly uint _vaSurfaceId;
    private readonly nint _eglDisplay;
    private nint _eglImage;
    private uint _glTextureId;
    private readonly int[] _ownedFds;
    private bool _disposed;

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public int Version => 0;
    public RenderPixelFormat Format => RenderPixelFormat.Bgra8888;
    public BitmapAlphaMode AlphaMode => BitmapAlphaMode.Ignore; // video frames are opaque
    public bool YFlipped => false;                              // dma_buf row 0 = top
    public SurfaceCapabilities Capabilities =>
        SurfaceCapabilities.ExternalHandle |
        SurfaceCapabilities.ExternallySynchronized |
        SurfaceCapabilities.GpuSampleable;
    public IReadOnlyList<ExternalRasterPlane> Planes =>
    [
        new ExternalRasterPlane(0, (nint)_glTextureId, PixelWidth, PixelHeight, 0, Format)
    ];

    /// <summary>
    /// Imports <paramref name="vaSurfaceId"/> from <paramref name="vaDisplay"/> as a
    /// GL texture via dma_buf + EGLImage. Throws if any step fails — callers wanting
    /// graceful fallback should catch and use the CPU upload path.
    /// </summary>
    public VaapiDmaBufTexture(nint vaDisplay, uint vaSurfaceId, int pixelWidth, int pixelHeight)
    {
        if (vaDisplay == 0) throw new ArgumentException("VA display is null.", nameof(vaDisplay));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelHeight, 0);

        if (!GlEglImageExt.TryLoad())
        {
            throw new InvalidOperationException("glEGLImageTargetTexture2DOES not available — GL_OES_EGL_image extension missing.");
        }

        _vaDisplay = vaDisplay;
        _vaSurfaceId = vaSurfaceId;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;

        // Sync first so the export reflects fully-decoded pixels.
        int syncStatus = LibVa.vaSyncSurface(_vaDisplay, _vaSurfaceId);
        if (syncStatus != LibVa.VA_STATUS_SUCCESS)
        {
            throw new InvalidOperationException($"vaSyncSurface failed: 0x{syncStatus:X}.");
        }

        // COMPOSED_LAYERS asks libva to give us a single layer that covers all
        // planes — easier to import as one EGLImage. The downside is the importer
        // gets an opaque YUV blob; see class doc for the YUV→RGB story.
        int exportStatus = LibVa.vaExportSurfaceHandle(
            _vaDisplay,
            _vaSurfaceId,
            LibVa.VA_SURFACE_ATTRIB_MEM_TYPE_DRM_PRIME_2,
            LibVa.VA_EXPORT_SURFACE_READ_ONLY | LibVa.VA_EXPORT_SURFACE_COMPOSED_LAYERS,
            out var descriptor);

        if (exportStatus != LibVa.VA_STATUS_SUCCESS)
        {
            throw new InvalidOperationException($"vaExportSurfaceHandle failed: 0x{exportStatus:X}.");
        }

        // Snapshot the fds we own so Dispose can close them even if subsequent
        // construction steps throw.
        int objectCount = (int)descriptor.NumObjects;
        _ownedFds = new int[objectCount];
        for (int i = 0; i < objectCount; i++)
        {
            var obj = descriptor.ObjectAt(i);
            _ownedFds[i] = obj.Fd;
        }

        try
        {
            _eglDisplay = LibEgl.eglGetDisplay(LibEgl.EGL_DEFAULT_DISPLAY);
            if (_eglDisplay == LibEgl.EGL_NO_DISPLAY)
            {
                throw new InvalidOperationException("eglGetDisplay returned EGL_NO_DISPLAY.");
            }

            if (LibEgl.eglInitialize(_eglDisplay, out _, out _) == 0)
            {
                throw new InvalidOperationException($"eglInitialize failed: 0x{LibEgl.eglGetError():X}.");
            }

            var layer0 = descriptor.LayerAt(0);
            _eglImage = CreateImageForLayer(_eglDisplay, descriptor, layer0);

            // Allocate a GL texture name and bind the EGLImage to it. The texture
            // target is GL_TEXTURE_2D — for COMPOSED YUV layers the driver
            // typically still requires GL_TEXTURE_EXTERNAL_OES, but desktop NVG
            // doesn't bind that target. This call may fail at runtime on YUV
            // surfaces; callers can catch and fall back.
            uint textureId;
            unsafe
            {
                LinuxGl.glGenTextures(1, &textureId);
                LinuxGl.glBindTexture(GlEglImageExt.GL_TEXTURE_2D, textureId);
            }

            _glTextureId = textureId;
            GlEglImageExt.EglImageTargetTexture2D(GlEglImageExt.GL_TEXTURE_2D, _eglImage);
        }
        catch
        {
            CleanupResources();
            throw;
        }
    }

    private static nint CreateImageForLayer(
        nint eglDisplay,
        in LibVa.VADRMPRIMESurfaceDescriptor descriptor,
        in LibVa.PrimeLayer layer)
    {
        // Build the EGL_LINUX_DMA_BUF_EXT attribute list. Each plane needs (fd,
        // offset, pitch) and optionally a 64-bit DRM modifier (lo, hi). Here we
        // pass per-plane modifier from the corresponding object's
        // drm_format_modifier — required for tiled formats (Intel/AMD drivers
        // produce tiled surfaces by default).
        var attrs = new List<long>(64);
        attrs.Add(LibEgl.EGL_WIDTH); attrs.Add(descriptor.Width);
        attrs.Add(LibEgl.EGL_HEIGHT); attrs.Add(descriptor.Height);
        attrs.Add(LibEgl.EGL_LINUX_DRM_FOURCC_EXT); attrs.Add(layer.DrmFormat);

        AddPlaneAttrs(attrs, 0, layer, descriptor,
            LibEgl.EGL_DMA_BUF_PLANE0_FD_EXT, LibEgl.EGL_DMA_BUF_PLANE0_OFFSET_EXT, LibEgl.EGL_DMA_BUF_PLANE0_PITCH_EXT,
            LibEgl.EGL_DMA_BUF_PLANE0_MODIFIER_LO_EXT, LibEgl.EGL_DMA_BUF_PLANE0_MODIFIER_HI_EXT);

        if (layer.NumPlanes >= 2)
        {
            AddPlaneAttrs(attrs, 1, layer, descriptor,
                LibEgl.EGL_DMA_BUF_PLANE1_FD_EXT, LibEgl.EGL_DMA_BUF_PLANE1_OFFSET_EXT, LibEgl.EGL_DMA_BUF_PLANE1_PITCH_EXT,
                LibEgl.EGL_DMA_BUF_PLANE1_MODIFIER_LO_EXT, LibEgl.EGL_DMA_BUF_PLANE1_MODIFIER_HI_EXT);
        }

        if (layer.NumPlanes >= 3)
        {
            AddPlaneAttrs(attrs, 2, layer, descriptor,
                LibEgl.EGL_DMA_BUF_PLANE2_FD_EXT, LibEgl.EGL_DMA_BUF_PLANE2_OFFSET_EXT, LibEgl.EGL_DMA_BUF_PLANE2_PITCH_EXT,
                LibEgl.EGL_DMA_BUF_PLANE2_MODIFIER_LO_EXT, LibEgl.EGL_DMA_BUF_PLANE2_MODIFIER_HI_EXT);
        }

        attrs.Add(LibEgl.EGL_NONE);

        nint image;
        fixed (long* attrPtr = CollectionsMarshal.AsSpan(attrs))
        {
            image = LibEgl.eglCreateImage(eglDisplay, LibEgl.EGL_NO_CONTEXT,
                LibEgl.EGL_LINUX_DMA_BUF_EXT, 0, (nint)attrPtr);
        }

        if (image == LibEgl.EGL_NO_IMAGE)
        {
            throw new InvalidOperationException($"eglCreateImage failed: 0x{LibEgl.eglGetError():X}.");
        }

        return image;
    }

    private static void AddPlaneAttrs(List<long> attrs, int planeIndex,
        in LibVa.PrimeLayer layer, in LibVa.VADRMPRIMESurfaceDescriptor descriptor,
        int fdKey, int offsetKey, int pitchKey, int modLoKey, int modHiKey)
    {
        uint objectIdx = planeIndex switch
        {
            0 => layer.ObjectIndex.P0,
            1 => layer.ObjectIndex.P1,
            2 => layer.ObjectIndex.P2,
            _ => layer.ObjectIndex.P3,
        };
        uint offset = planeIndex switch
        {
            0 => layer.Offset.P0,
            1 => layer.Offset.P1,
            2 => layer.Offset.P2,
            _ => layer.Offset.P3,
        };
        uint pitch = planeIndex switch
        {
            0 => layer.Pitch.P0,
            1 => layer.Pitch.P1,
            2 => layer.Pitch.P2,
            _ => layer.Pitch.P3,
        };

        var obj = descriptor.ObjectAt((int)objectIdx);
        attrs.Add(fdKey); attrs.Add(obj.Fd);
        attrs.Add(offsetKey); attrs.Add(offset);
        attrs.Add(pitchKey); attrs.Add(pitch);
        attrs.Add(modLoKey); attrs.Add((long)(obj.DrmFormatModifier & 0xFFFFFFFF));
        attrs.Add(modHiKey); attrs.Add((long)(obj.DrmFormatModifier >> 32));
    }

    /// <summary>
    /// Sync the VA surface so the GPU has finished decoding before the consumer
    /// samples. Cheap on most drivers (no-op when decode is already complete).
    /// </summary>
    public IExternalRasterLease Acquire()
    {
        SyncSurface();
        return new GlLease(this);
    }

    private void SyncSurface()
    {
        if (_disposed) return;
        int status = LibVa.vaSyncSurface(_vaDisplay, _vaSurfaceId);
        if (status != LibVa.VA_STATUS_SUCCESS)
        {
            SampleLog.Write($"vaSyncSurface during Acquire returned 0x{status:X}.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CleanupResources();
    }

    private sealed class GlLease : IExternalRasterLease
    {
        private readonly VaapiDmaBufTexture _source;

        public GlLease(VaapiDmaBufTexture source)
        {
            _source = source;
        }

        public nint NativeHandle => (nint)_source._glTextureId;
        public nint NativeAlternateHandle => 0;
        public int PixelWidth => _source.PixelWidth;
        public int PixelHeight => _source.PixelHeight;
        public bool YFlipped => _source.YFlipped;
        public void Dispose()
        {
            // dma_buf is always-readable shared memory; nothing to unlock per frame.
        }
    }

    private void CleanupResources()
    {
        if (_glTextureId != 0)
        {
            unsafe
            {
                uint id = _glTextureId;
                LinuxGl.glDeleteTextures(1, &id);
            }
            _glTextureId = 0;
        }

        if (_eglImage != 0 && _eglDisplay != 0)
        {
            LibEgl.eglDestroyImage(_eglDisplay, _eglImage);
            _eglImage = 0;
        }

        if (_ownedFds != null)
        {
            foreach (int fd in _ownedFds)
            {
                if (fd >= 0) LinuxLibc.close(fd);
            }
        }
    }
}

internal static partial class LinuxGl
{
    [LibraryImport("libGL.so.1", EntryPoint = "glGenTextures")]
    public static unsafe partial void glGenTextures(int n, uint* textures);

    [LibraryImport("libGL.so.1", EntryPoint = "glDeleteTextures")]
    public static unsafe partial void glDeleteTextures(int n, uint* textures);

    [LibraryImport("libGL.so.1", EntryPoint = "glBindTexture")]
    public static partial void glBindTexture(uint target, uint texture);
}

internal static partial class LinuxLibc
{
    [LibraryImport("libc.so.6", EntryPoint = "close")]
    public static partial int close(int fd);
}
