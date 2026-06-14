namespace Aprillz.MewUI.Skia.Interop;

public static class SkiaMewVGWin32Interop
{
    public static void Register() => SkiaInterop.Register(new MewVG.Win32.MewVGWin32SkiaInteropProvider());

    public static ApplicationBuilder UseSkiaMewVGWin32Interop(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}
