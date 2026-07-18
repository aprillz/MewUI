using Aprillz.MewUI.Platform.Win32;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the Win32 platform host with <see cref="Application"/>.
/// </summary>
public static class Win32Platform
{
    public static string PlatformIdentifier => Win32PlatformHost.PlatformIdentifier;

    public static void Register()
        => Application.RegisterPlatformHost(static () => new Win32PlatformHost(), Platform.PlatformSurfaceKind.Win32, "Win32");

    public static ApplicationBuilder UseWin32(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        Register();

        return builder;
    }
}
