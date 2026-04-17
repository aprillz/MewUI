using Aprillz.MewUI.Rendering.MewVG;

namespace Aprillz.MewUI;

/// <summary>
/// Registers SkiaSharp GPU hosting services for the MewVG macOS backend.
/// </summary>
public static class MewVGMacOSSkiaBackend
{
    public static void Register()
        => MewVGSkiaGraphicsServices.RegisterFactory<MewVGGraphicsFactory>();

    public static ApplicationBuilder UseMewVGMetalSkia(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseMewVGMetal();
        Register();
        return builder;
    }
}
