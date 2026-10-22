namespace Aprillz.MewUI.Skia.Interop;

/// <summary>
/// Registers the MewVG (X11) zero-copy Skia bridge with <see cref="SkiaInterop"/>.
/// Call <see cref="Register"/> at startup, or chain <see cref="UseSkiaMewVGX11Interop"/> on an <see cref="ApplicationBuilder"/>.
/// </summary>
public static class SkiaMewVGX11Interop
{
    /// <summary>Registers the MewVG (X11) provider with <see cref="SkiaInterop"/>.</summary>
    public static void Register() => SkiaInterop.Register(new MewVG.X11.MewVGX11SkiaInteropProvider());

    /// <summary>Registers the MewVG (X11) provider and returns <paramref name="builder"/> for chaining.</summary>
    public static ApplicationBuilder UseSkiaMewVGX11Interop(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}
