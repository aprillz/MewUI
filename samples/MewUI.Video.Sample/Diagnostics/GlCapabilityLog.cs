using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Video.Sample.Diagnostics;

/// <summary>
/// Temporary diagnostic: logs the current OpenGL context's renderer and the interop-related GL/WGL extensions once.
/// </summary>
internal static unsafe class GlCapabilityLog
{
    private const uint GL_VENDOR = 0x1F00;
    private const uint GL_RENDERER = 0x1F01;
    private const uint GL_VERSION = 0x1F02;
    private const uint GL_EXTENSIONS = 0x1F03;
    private const uint GL_NUM_EXTENSIONS = 0x821D;

    private static readonly string[] _interestingTokens = ["memory_object", "semaphore", "interop", "NV_DX", "external_objects", "sync"];
    private static bool _logged;

    [DllImport("opengl32.dll")]
    private static extern nint glGetString(uint name);

    [DllImport("opengl32.dll")]
    private static extern void glGetIntegerv(uint name, int* value);

    [DllImport("opengl32.dll")]
    private static extern nint wglGetProcAddress(string name);

    [DllImport("opengl32.dll")]
    private static extern nint wglGetCurrentDC();

    public static void LogOnce()
    {
        if (_logged || !OperatingSystem.IsWindows())
        {
            return;
        }

        _logged = true;
        SampleLog.Write($"[gl-caps] vendor={ReadString(GL_VENDOR)} renderer={ReadString(GL_RENDERER)} version={ReadString(GL_VERSION)}");

        var extensions = new List<string>();
        nint legacyExtensions = glGetString(GL_EXTENSIONS);
        if (legacyExtensions != 0)
        {
            extensions.AddRange((Marshal.PtrToStringAnsi(legacyExtensions) ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        else
        {
            int count = 0;
            glGetIntegerv(GL_NUM_EXTENSIONS, &count);
            var getStringi = (delegate* unmanaged[Stdcall]<uint, uint, nint>)wglGetProcAddress("glGetStringi");
            if (getStringi != null)
            {
                for (uint index = 0; index < count; index++)
                {
                    string? name = Marshal.PtrToStringAnsi(getStringi(GL_EXTENSIONS, index));
                    if (name is not null)
                    {
                        extensions.Add(name);
                    }
                }
            }
        }

        var getWglExtensions = (delegate* unmanaged[Stdcall]<nint, nint>)wglGetProcAddress("wglGetExtensionsStringARB");
        if (getWglExtensions != null)
        {
            string wglText = Marshal.PtrToStringAnsi(getWglExtensions(wglGetCurrentDC())) ?? string.Empty;
            extensions.AddRange(wglText.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        var matches = extensions
            .Where(name => _interestingTokens.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal);
        SampleLog.Write($"[gl-caps] total extensions={extensions.Count}; interop-related: {string.Join(' ', matches)}");
    }

    private static string ReadString(uint name)
        => Marshal.PtrToStringAnsi(glGetString(name)) ?? "(null)";
}
