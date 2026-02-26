using Aprillz.MewUI.Rendering.MewVG;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the MewVG (Metal) backend with <see cref="Application"/> on macOS.
/// </summary>
public static class MewVGMacOSBackend
{
    public const string BackendId = "mewvg-metal";

    public static void Register()
        => Application.RegisterGraphicsFactory(BackendId, static () => MewVGGraphicsFactory.Instance);

    public static ApplicationBuilder UseMewVGMetal(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}

