using Aprillz.MewUI.Rendering.MewVG;

namespace Aprillz.MewUI;

/// <summary>
/// Registers SkiaSharp GPU hosting services for the MewVG Win32 backend.
/// </summary>
public static class MewVGWin32SkiaBackend
{
    public static void Register()
        => MewVGSkiaGraphicsServices.RegisterFactory<MewVGGraphicsFactory>();

    public static ApplicationBuilder UseMewVGWin32Skia(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseMewVGWin32();
        Register();
        return builder;
    }
}
