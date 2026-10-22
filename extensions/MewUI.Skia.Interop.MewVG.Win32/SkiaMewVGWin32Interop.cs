namespace Aprillz.MewUI.Skia.Interop;

/// <summary>
/// Registers the MewVG (Win32) zero-copy Skia bridge with <see cref="SkiaInterop"/>.
/// Call <see cref="Register"/> at startup, or chain <see cref="UseSkiaMewVGWin32Interop"/> on an <see cref="ApplicationBuilder"/>.
/// </summary>
public static class SkiaMewVGWin32Interop
{
    /// <summary>Registers the MewVG (Win32) provider with <see cref="SkiaInterop"/>.</summary>
    public static void Register() => SkiaInterop.Register(new MewVG.Win32.MewVGWin32SkiaInteropProvider());

    /// <summary>Registers the MewVG (Win32) provider and returns <paramref name="builder"/> for chaining.</summary>
    public static ApplicationBuilder UseSkiaMewVGWin32Interop(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}
