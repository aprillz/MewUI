namespace Aprillz.MewUI;

/// <summary>
/// Registers the MewVG (Win32 GL) backend with <see cref="Application"/>.
/// </summary>
public static class MewVGWin32Backend
{
    public const string BackendId = "mewvg-win32-gl";

    public static void Register()
        => Application.RegisterGraphicsFactory(BackendId, static () => Rendering.MewVG.MewVGGraphicsFactory.Instance);

    public static ApplicationBuilder UseMewVGWin32(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}
