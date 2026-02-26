using Aprillz.MewUI.Platform.Win32;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the Win32 platform host with <see cref="Application"/>.
/// </summary>
public static class Win32Platform
{
    public const string PlatformId = "win32";

    public static void Register()
        => Application.RegisterPlatformHost(PlatformId, static () => new Win32PlatformHost());

    public static ApplicationBuilder UseWin32(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}
