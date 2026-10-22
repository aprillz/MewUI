namespace Aprillz.MewUI.Skia.Interop;

/// <summary>
/// Registers the MewVG (macOS) zero-copy Skia bridge with <see cref="SkiaInterop"/>.
/// Call <see cref="Register"/> at startup, or chain <see cref="UseSkiaMewVGMacOSInterop"/> on an <see cref="ApplicationBuilder"/>.
/// </summary>
public static class SkiaMewVGMacOSInterop
{
    /// <summary>Registers the MewVG (macOS) provider with <see cref="SkiaInterop"/>.</summary>
    public static void Register() => SkiaInterop.Register(new MewVG.MacOS.MewVGMacOSSkiaInteropProvider());

    /// <summary>Registers the MewVG (macOS) provider and returns <paramref name="builder"/> for chaining.</summary>
    public static ApplicationBuilder UseSkiaMewVGMacOSInterop(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}
