using Aprillz.MewUI.Rendering.MewVG;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the MewVG (Win32 GL) backend with <see cref="Application"/>.
/// </summary>
public static class MewVGWin32Backend
{
    public static string BackendIdentifier => MewVGWin32GraphicsFactory.BackendIdentifier;

    public static void Register()
        => Application.RegisterGraphicsFactory(BackendIdentifier, static () => MewVGWin32GraphicsFactory.Instance);

    public static ApplicationBuilder UseMewVGWin32(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        // Runtime defaults for D2D-oriented MewVG Win32 pipeline:
        // - No MSAA
        // - No stencil buffer request
        GraphicsRuntimeOptions.PreferredMsaaSamples = 0;
        GraphicsRuntimeOptions.PreferredMewVGStencilBits = 0;
        Application.SetDefaultGraphicsFactory(BackendIdentifier);
        return builder;
    }
}
