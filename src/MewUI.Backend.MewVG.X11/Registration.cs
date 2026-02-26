namespace Aprillz.MewUI;

/// <summary>
/// Registers the MewVG (X11 GL) backend with <see cref="Application"/>.
/// </summary>
public static class MewVGX11Backend
{
    public const string BackendId = "mewvg-x11-gl";

    public static void Register()
        => Application.RegisterGraphicsFactory(BackendId, static () => Rendering.MewVG.MewVGGraphicsFactory.Instance);

    public static ApplicationBuilder UseMewVGX11(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}
