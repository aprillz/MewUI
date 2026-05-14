using Aprillz.MewUI.Rendering.MewVG;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the MewVG (X11 GL) backend with <see cref="Application"/>.
/// </summary>
public static class MewVGX11Backend
{
    public static string BackendIdentifier => MewVGX11GraphicsFactory.BackendIdentifier;

    public static void Register()
        => Application.RegisterGraphicsFactory(BackendIdentifier, static () => MewVGX11GraphicsFactory.Instance);

    public static ApplicationBuilder UseMewVGX11(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        Register();

        return builder;
    }
}
