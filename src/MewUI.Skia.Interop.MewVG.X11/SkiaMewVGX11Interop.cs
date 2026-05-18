namespace Aprillz.MewUI.Skia.Interop;

public static class SkiaMewVGX11Interop
{
    public static void Register() => SkiaInterop.Register(new MewVG.X11.MewVGX11SkiaInteropProvider());

    public static ApplicationBuilder UseSkiaMewVGX11Interop(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}
