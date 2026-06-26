using Aprillz.MewUI.Rendering.MewVG;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the MewVG (Win32 GL) backend with <see cref="Application"/>.
/// </summary>
public static class MewVGWin32Backend
{
    public static string BackendIdentifier => MewVGWin32GraphicsFactory.BackendIdentifier;

    public static void Register()
        => Application.RegisterGraphicsFactory(static () => new MewVGWin32GraphicsFactory());

    public static ApplicationBuilder UseMewVGWin32(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}
