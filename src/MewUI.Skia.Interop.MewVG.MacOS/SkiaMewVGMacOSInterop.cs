namespace Aprillz.MewUI.Skia.Interop;

public static class SkiaMewVGMacOSInterop
{
    public static void Register() => SkiaInterop.Register(new MewVG.MacOS.MewVGMacOSSkiaInteropProvider());

    public static ApplicationBuilder UseSkiaMewVGMacOSInterop(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}
