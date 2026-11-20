using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Aprillz.MewUI.Rendering.MewVG;

[SupportedOSPlatform("browser")]
internal static partial class BrowserNative
{
    private const string LibraryName = "mewui_webgl_shim";

    [LibraryImport(LibraryName, EntryPoint = "mewui_webgl_init", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int InitializeContext(string selector);

    [LibraryImport(LibraryName, EntryPoint = "mewui_webgl_get_proc", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GetProcAddress(string name);

    [LibraryImport(LibraryName, EntryPoint = "mewui_webgl_make_current")]
    internal static partial void MakeContextCurrent();

    // Same shape as mewui_sig_vif; the first argument carries nothing.
    [LibraryImport(LibraryName, EntryPoint = "mewui_text_set_letter_spacing")]
    internal static partial void SetTextLetterSpacing(int reserved, float spacingPx);

    [LibraryImport(LibraryName, EntryPoint = "mewui_text_measure", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial double MeasureText(string text, string cssFont, out double ascent, out double descent);

    [LibraryImport(LibraryName, EntryPoint = "mewui_text_ink_box", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial double MeasureInkBox(string text, string cssFont, out double ascent, out double descent);

    [LibraryImport(LibraryName, EntryPoint = "mewui_text_ink_extent", StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial double MeasureInkExtent(string text, string cssFont, double* horizontal, double* vertical);

    [LibraryImport(LibraryName, EntryPoint = "mewui_text_prefix_advances", StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial double MeasurePrefixAdvances(string text, string cssFont, double* advances, double* capacity);

    [LibraryImport(LibraryName, EntryPoint = "mewui_text_rasterize", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int RasterizeText(string text, string cssFont, int widthPx, int heightPx, double scale,
        int red, int green, int blue, int alpha, int insetLeftPx, int insetTopPx, int wrap,
        nint pixels);

    [LibraryImport(LibraryName, EntryPoint = "mewui_text_draw_to_texture", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int DrawTextToTexture(string text, string cssFont, int widthPx, int heightPx, double scale,
        int red, int green, int blue, int alpha, int insetLeftPx, int insetTopPx, int wrap,
        nint texture);

    [LibraryImport(LibraryName)]
    internal static partial void mewui_sig_viiiiiiiii(int arg0, int arg1, int arg2, int arg3, int arg4, int arg5, int arg6, int arg7, int arg8);

    [LibraryImport(LibraryName)]
    internal static partial void mewui_sig_vif(int arg0, float arg1);

    [LibraryImport(LibraryName)]
    internal static partial void mewui_sig_vffff(float arg0, float arg1, float arg2, float arg3);

    // glDrawElementsInstanced's shape.
    [LibraryImport(LibraryName)]
    internal static partial void mewui_sig_viiiii(int arg0, int arg1, int arg2, int arg3, int arg4);

    // glVertexAttrib4f's shape.
    [LibraryImport(LibraryName)]
    internal static partial void mewui_sig_viffff(int arg0, float arg1, float arg2, float arg3, float arg4);

    // glLineWidth's shape.
    [LibraryImport(LibraryName)]
    internal static partial void mewui_sig_vf(float arg0);

    // glUniform2f's shape.
    [LibraryImport(LibraryName)]
    internal static partial void mewui_sig_viff(int arg0, float arg1, float arg2);

    // glReadPixels' shape.
    [LibraryImport(LibraryName)]
    internal static partial void mewui_sig_viiiiiii(int arg0, int arg1, int arg2, int arg3, int arg4, int arg5, int arg6);

    // glBlitFramebuffer's shape.
    [LibraryImport(LibraryName)]
    internal static partial void mewui_sig_viiiiiiiiii(int arg0, int arg1, int arg2, int arg3, int arg4, int arg5, int arg6, int arg7, int arg8, int arg9);

    internal static void PreserveFunctionPointerSignatures()
    {
        if (Environment.GetEnvironmentVariable("MEWUI_PIN_SIGNATURES") == "force")
        {
            mewui_sig_viiiiiiiii(0, 0, 0, 0, 0, 0, 0, 0, 0);
            mewui_sig_vif(0, 0);
            mewui_sig_vffff(0, 0, 0, 0);
            mewui_sig_viiiii(0, 0, 0, 0, 0);
            mewui_sig_viffff(0, 0, 0, 0, 0);
            mewui_sig_vf(0);
            mewui_sig_viff(0, 0, 0);
            mewui_sig_viiiiiii(0, 0, 0, 0, 0, 0, 0);
            mewui_sig_viiiiiiiiii(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }
    }
}
