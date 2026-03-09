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
        // Runtime defaults for D2D-oriented MewVG Win32 pipeline:
        // - No MSAA
        // - No stencil buffer request
        GraphicsRuntimeOptions.PreferredMsaaSamples = 0;
        GraphicsRuntimeOptions.PreferredMewVGStencilBits = 0;
        Application.SetDefaultGraphicsFactory(BackendId);
        return builder;
    }
}
