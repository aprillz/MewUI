using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Skia.Sample.Rendering;

/// <summary>
/// Lightweight diagnostic helpers for the GL render path: GL string introspection
/// (<c>VENDOR</c>/<c>RENDERER</c>/<c>VERSION</c>) and <c>wglGetSwapIntervalEXT</c> probing.
/// All calls require a current GL context — caller responsibility.
/// </summary>
internal static unsafe partial class GLDiagnostics
{
    private const uint GL_VENDOR = 0x1F00;
    private const uint GL_RENDERER = 0x1F01;
    private const uint GL_VERSION = 0x1F02;

    [LibraryImport("opengl32.dll", EntryPoint = "glGetString")]
    private static partial nint glGetString(uint name);

    [LibraryImport("opengl32.dll", EntryPoint = "wglGetProcAddress", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint wglGetProcAddress(string name);

    private static delegate* unmanaged<int> _pwglGetSwapIntervalEXT;
    private static bool _swapResolveAttempted;

    public static string? GetVendor() => Marshal.PtrToStringAnsi(glGetString(GL_VENDOR));
    public static string? GetRenderer() => Marshal.PtrToStringAnsi(glGetString(GL_RENDERER));
    public static string? GetVersion() => Marshal.PtrToStringAnsi(glGetString(GL_VERSION));

    /// <summary>
    /// Returns the current GL swap interval, or <see langword="null"/> when the
    /// <c>WGL_EXT_swap_control</c> extension isn't exposed on this driver / context.
    /// Resolution is per-context, but the resolved function pointer is reused once it works
    /// (extension addresses are valid for the lifetime of any context in the same share group).
    /// </summary>
    public static int? GetSwapInterval()
    {
        if (!_swapResolveAttempted)
        {
            _swapResolveAttempted = true;
            _pwglGetSwapIntervalEXT = (delegate* unmanaged<int>)wglGetProcAddress("wglGetSwapIntervalEXT");
        }
        if (_pwglGetSwapIntervalEXT == null) return null;
        return _pwglGetSwapIntervalEXT();
    }

    /// <summary>One-shot dump of the GL context identity (vendor/renderer/version) tagged
    /// with the caller-provided label. Useful to confirm we're on a hardware driver and not
    /// the Microsoft GDI Generic software fallback.</summary>
    public static void LogContextIdentity(string tag)
    {
        System.Diagnostics.Debug.WriteLine(
            $"[{tag}] GL vendor='{GetVendor()}' renderer='{GetRenderer()}' version='{GetVersion()}' swapInterval={GetSwapInterval()?.ToString() ?? "n/a"}");
    }
}
