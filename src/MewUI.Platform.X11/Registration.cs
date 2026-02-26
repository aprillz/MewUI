using Aprillz.MewUI.Platform.Linux.X11;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the X11 platform host with <see cref="Application"/>.
/// </summary>
public static class X11Platform
{
    public const string PlatformId = "x11";

    public static void Register()
        => Application.RegisterPlatformHost(PlatformId, static () => new X11PlatformHost());

    public static ApplicationBuilder UseX11(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}
