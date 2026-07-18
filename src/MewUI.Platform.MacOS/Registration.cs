using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.MacOS;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the macOS platform host with <see cref="Application"/>.
/// </summary>
public static class MacOSPlatform
{
    public static string PlatformIdentifier => MacOSPlatformHost.PlatformIdentifier;

    public static void Register()
    {
        Application.RegisterPlatformHost(CreateHost, Platform.PlatformSurfaceKind.MacOS, "MacOS");
        PlatformConventions.Current = new MacOSConventions();
    }

    private static MacOSPlatformHost CreateHost()
        => new();

    public static ApplicationBuilder UseMacOS(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        Register();

        return builder;
    }
}
