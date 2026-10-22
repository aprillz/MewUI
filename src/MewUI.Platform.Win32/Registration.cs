using Aprillz.MewUI.Platform.Win32;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the Win32 platform host with <see cref="Application"/>.
/// </summary>
public static class Win32Platform
{
    /// <summary>Gets the identifier the platform registers under.</summary>
    public static string PlatformIdentifier => Win32PlatformHost.PlatformIdentifier;

    /// <summary>Registers the Win32 platform host and its process memory reader with <see cref="Application"/>.</summary>
    public static void Register()
    {
        Application.RegisterPlatformHost(static () => new Win32PlatformHost(), Platform.PlatformSurfaceKind.Win32, "Win32",
            Win32PlatformHost.SystemFontFamily);
        Rendering.RenderResourceMetrics.ProcessMemoryReader = Win32ProcessMemory.Read;
    }

    /// <summary>Registers the Win32 platform host and returns <paramref name="builder"/>.</summary>
    public static ApplicationBuilder UseWin32(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        Register();

        return builder;
    }
}
