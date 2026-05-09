using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Video.Sample.Decoding;

/// <summary>
/// Minimal EGL P/Invoke surface for importing a DRM PRIME dma_buf as an EGLImage so
/// it can be bound to a GL texture via <c>glEGLImageTargetTexture2DOES</c>. Used
/// only on Linux for the VAAPI → EGL → GL zero-copy chain.
/// </summary>
/// <remarks>
/// EGL coexists with GLX in this build: the X11 backend creates its window/context
/// via GLX, but Mesa lets us obtain an EGL display on the same X11 Display* and
/// import dma_buf images that the GLX context can sample (the underlying DRI driver
/// state is shared across both APIs). NVIDIA proprietary doesn't share state — on
/// that driver this path will fail and the caller falls back to CPU upload.
/// </remarks>
internal static unsafe partial class LibEgl
{
    private const string LibraryName = "libEGL.so.1";

    public const nint EGL_NO_DISPLAY = 0;
    public const nint EGL_NO_CONTEXT = 0;
    public const nint EGL_NO_IMAGE = 0;

    public const int EGL_DEFAULT_DISPLAY = 0;

    // Target for eglCreateImage when the source is a Linux dma_buf (one fd per plane,
    // optionally with DRM format modifiers). Defined by EGL_EXT_image_dma_buf_import.
    public const uint EGL_LINUX_DMA_BUF_EXT = 0x3270;

    // Attribute keys for dma_buf import. Each plane has fd / offset / pitch, and
    // optionally a 64-bit DRM modifier (lo / hi halves). DRM_FOURCC of the buffer
    // itself goes in EGL_LINUX_DRM_FOURCC_EXT.
    public const int EGL_NONE = 0x3038;
    public const int EGL_WIDTH = 0x3057;
    public const int EGL_HEIGHT = 0x3056;
    public const int EGL_LINUX_DRM_FOURCC_EXT = 0x3271;

    public const int EGL_DMA_BUF_PLANE0_FD_EXT = 0x3272;
    public const int EGL_DMA_BUF_PLANE0_OFFSET_EXT = 0x3273;
    public const int EGL_DMA_BUF_PLANE0_PITCH_EXT = 0x3274;
    public const int EGL_DMA_BUF_PLANE1_FD_EXT = 0x3275;
    public const int EGL_DMA_BUF_PLANE1_OFFSET_EXT = 0x3276;
    public const int EGL_DMA_BUF_PLANE1_PITCH_EXT = 0x3277;
    public const int EGL_DMA_BUF_PLANE2_FD_EXT = 0x3278;
    public const int EGL_DMA_BUF_PLANE2_OFFSET_EXT = 0x3279;
    public const int EGL_DMA_BUF_PLANE2_PITCH_EXT = 0x327A;

    public const int EGL_DMA_BUF_PLANE0_MODIFIER_LO_EXT = 0x3443;
    public const int EGL_DMA_BUF_PLANE0_MODIFIER_HI_EXT = 0x3444;
    public const int EGL_DMA_BUF_PLANE1_MODIFIER_LO_EXT = 0x3445;
    public const int EGL_DMA_BUF_PLANE1_MODIFIER_HI_EXT = 0x3446;
    public const int EGL_DMA_BUF_PLANE2_MODIFIER_LO_EXT = 0x3447;
    public const int EGL_DMA_BUF_PLANE2_MODIFIER_HI_EXT = 0x3448;

    [LibraryImport(LibraryName, EntryPoint = "eglGetDisplay")]
    public static partial nint eglGetDisplay(nint displayId);

    [LibraryImport(LibraryName, EntryPoint = "eglInitialize")]
    [return: MarshalAs(UnmanagedType.U4)]
    public static partial uint eglInitialize(nint display, out int major, out int minor);

    /// <summary>
    /// Create an EGLImage from a list of EGL_LINUX_DMA_BUF_EXT attributes. Pass
    /// <c>EGL_NO_CONTEXT</c> as <paramref name="context"/> — dma_buf import is
    /// detached from any GL context and the returned image can be bound to textures
    /// in any context whose underlying driver shares state with EGL.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "eglCreateImage")]
    public static partial nint eglCreateImage(
        nint display,
        nint context,
        uint target,
        nint clientBuffer,
        nint attribList);

    [LibraryImport(LibraryName, EntryPoint = "eglDestroyImage")]
    [return: MarshalAs(UnmanagedType.U4)]
    public static partial uint eglDestroyImage(nint display, nint image);

    [LibraryImport(LibraryName, EntryPoint = "eglGetError")]
    public static partial int eglGetError();
}
