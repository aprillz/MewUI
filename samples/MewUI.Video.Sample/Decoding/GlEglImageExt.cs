using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Video.Sample.Decoding;

/// <summary>
/// <c>GL_OES_EGL_image</c> extension entry point — binds an EGLImage to a GL texture
/// so subsequent draws sample from it directly. Looked up via <c>glXGetProcAddress</c>
/// because it's an extension function, not part of the GL ABI.
/// </summary>
internal static unsafe class GlEglImageExt
{
    private delegate void GlEglImageTargetTexture2DOesDelegate(uint target, nint image);

    private static GlEglImageTargetTexture2DOesDelegate? _entry;

    [DllImport("libGL.so.1", EntryPoint = "glXGetProcAddress")]
    private static extern nint glXGetProcAddress([MarshalAs(UnmanagedType.LPStr)] string name);

    public static bool TryLoad()
    {
        if (_entry is not null) return true;

        nint fn = glXGetProcAddress("glEGLImageTargetTexture2DOES");
        if (fn == 0) return false;

        _entry = Marshal.GetDelegateForFunctionPointer<GlEglImageTargetTexture2DOesDelegate>(fn);
        return true;
    }

    /// <summary>
    /// Bind <paramref name="image"/> as the storage of the currently-bound texture.
    /// <paramref name="target"/> is typically <c>GL_TEXTURE_2D</c> (0x0DE1) or
    /// <c>GL_TEXTURE_EXTERNAL_OES</c> (0x8D65) depending on the dma_buf format —
    /// YUV planar formats imported as a single image use the EXTERNAL_OES target,
    /// while RGB/RGBA can use TEXTURE_2D.
    /// </summary>
    public static void EglImageTargetTexture2D(uint target, nint image)
    {
        if (_entry is null)
        {
            throw new InvalidOperationException("Call TryLoad() first.");
        }

        _entry(target, image);
    }

    public const uint GL_TEXTURE_2D = 0x0DE1;
    public const uint GL_TEXTURE_EXTERNAL_OES = 0x8D65;
}
