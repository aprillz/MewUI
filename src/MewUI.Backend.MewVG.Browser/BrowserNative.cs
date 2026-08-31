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

    [LibraryImport(LibraryName, EntryPoint = "mewui_text_measure", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial double MeasureText(string text, string cssFont, out double ascent, out double descent);

    [LibraryImport(LibraryName, EntryPoint = "mewui_text_rasterize", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int RasterizeText(string text, string cssFont, int widthPx, int heightPx, double scale,
        int red, int green, int blue, int alpha, int horizontalAlignment, int verticalAlignment, int wrap,
        nint pixels);

    [LibraryImport(LibraryName)]
    internal static partial void mewui_sig_viiiiiiiii(int arg0, int arg1, int arg2, int arg3, int arg4, int arg5, int arg6, int arg7, int arg8);

    [LibraryImport(LibraryName)]
    internal static partial void mewui_sig_vif(int arg0, float arg1);

    [LibraryImport(LibraryName)]
    internal static partial void mewui_sig_vffff(float arg0, float arg1, float arg2, float arg3);

    internal static void PreserveFunctionPointerSignatures()
    {
        if (Environment.GetEnvironmentVariable("MEWUI_PIN_SIGNATURES") == "force")
        {
            mewui_sig_viiiiiiiii(0, 0, 0, 0, 0, 0, 0, 0, 0);
            mewui_sig_vif(0, 0);
            mewui_sig_vffff(0, 0, 0, 0);
        }
    }
}
