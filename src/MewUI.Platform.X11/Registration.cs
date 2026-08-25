using Aprillz.MewUI.Platform.Linux.X11;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the X11 platform host with <see cref="Application"/>.
/// </summary>
public static class X11Platform
{
    public static string PlatformIdentifier => X11PlatformHost.PlatformIdentifier;

    public static void Register()
    {
        Application.RegisterPlatformHost(static () => new X11PlatformHost(), Platform.PlatformSurfaceKind.X11, "X11",
            X11PlatformHost.SystemFontFamily);
        Rendering.RenderMemoryLedger.ProcessMemoryReader = Platform.Linux.LinuxProcessMemory.Read;
    }

    public static ApplicationBuilder UseX11(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        Register();

        return builder;
    }
}
