namespace Aprillz.MewUI.Skia.Interop;

/// <summary>
/// Registers the Direct2D zero-copy Skia bridge with <see cref="SkiaInterop"/>.
/// Call <see cref="Register"/> at startup, or chain <see cref="UseSkiaDirect2DInterop"/>
/// on an <see cref="ApplicationBuilder"/>.
/// </summary>
public static class SkiaDirect2DInterop
{
    /// <summary>Registers the Direct2D provider with <see cref="SkiaInterop"/>.</summary>
    public static void Register() => SkiaInterop.Register(new Direct2D.Direct2DSkiaInteropProvider());

    /// <summary>Registers the Direct2D provider and returns <paramref name="builder"/> for chaining.</summary>
    public static ApplicationBuilder UseSkiaDirect2DInterop(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}
