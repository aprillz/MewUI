using Aprillz.MewUI.Rendering.MewVG;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the MewVG (Win32 GL) backend with <see cref="Application"/>.
/// </summary>
public static class MewVGWin32Backend
{
    /// <summary>Gets the identifier the backend registers under.</summary>
    public static string BackendIdentifier => MewVGWin32GraphicsFactory.BackendIdentifier;

    /// <summary>Registers the MewVG (Win32 GL) graphics factory for Win32 window surfaces with <see cref="Application"/>.</summary>
    public static void Register()
        => Application.RegisterGraphicsFactory(static () => new MewVGWin32GraphicsFactory(), Platform.PlatformSurfaceKind.Win32, "MewVG.Win32");

    /// <summary>Registers the MewVG (Win32 GL) backend and returns <paramref name="builder"/>.</summary>
    public static ApplicationBuilder UseMewVGWin32(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}
