using Aprillz.MewUI.Rendering.MewVG;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the MewVG (Metal) backend with <see cref="Application"/> on macOS.
/// </summary>
public static class MewVGMacOSBackend
{
        public static string BackendIdentifier => MewVGMacOSGraphicsFactory.BackendIdentifier;

    public static void Register()
        => Application.RegisterGraphicsFactory(static () => new MewVGMacOSGraphicsFactory());

    public static ApplicationBuilder UseMewVGMetal(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}

