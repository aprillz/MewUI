using Aprillz.MewUI.Platform.MacOS;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the macOS platform host with <see cref="Application"/>.
/// </summary>
public static class MacOSPlatform
{
    public const string PlatformId = "macos";

    public static void Register()
        => Application.RegisterPlatformHost(PlatformId, CreateHost);

    private static MacOSPlatformHost CreateHost()
        => new();

    public static ApplicationBuilder UseMacOS(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}
