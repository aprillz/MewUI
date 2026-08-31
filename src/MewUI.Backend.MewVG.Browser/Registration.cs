using Aprillz.MewUI.Rendering.MewVG;

namespace Aprillz.MewUI;

public static class MewVGBrowserBackend
{
    public static string BackendIdentifier => MewVGWin32GraphicsFactory.BackendIdentifier;

    public static void Register()
        => Application.RegisterGraphicsFactory(
            static () => new MewVGWin32GraphicsFactory(),
            Platform.PlatformSurfaceKind.Browser,
            "MewVG.Browser");

    public static ApplicationBuilder UseMewVGWebGL(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}
