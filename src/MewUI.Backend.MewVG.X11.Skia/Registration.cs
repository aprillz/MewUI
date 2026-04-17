using Aprillz.MewUI.Rendering.MewVG;

namespace Aprillz.MewUI;

/// <summary>
/// Registers SkiaSharp GPU hosting services for the MewVG X11 backend.
/// </summary>
public static class MewVGX11SkiaBackend
{
    public static void Register()
        => MewVGSkiaGraphicsServices.RegisterFactory<MewVGGraphicsFactory>();

    public static ApplicationBuilder UseMewVGX11Skia(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseMewVGX11();
        Register();
        return builder;
    }
}
