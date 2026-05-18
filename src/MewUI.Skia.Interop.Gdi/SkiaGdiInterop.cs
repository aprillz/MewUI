using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Skia.Interop;

/// <summary>
/// Registers a GDI Skia bridge with <see cref="SkiaInterop"/>. Two paths available:
/// <list type="bullet">
///   <item><b>Software</b> (<see cref="Register"/>): Skia paints directly into a DIB section that GDI BitBlts. Zero per-frame memcpy. Best for trivial UI.</item>
///   <item><b>GL readback</b> (<see cref="RegisterGL"/>): Skia renders in a hidden GL FBO (GPU-accelerated path AA / gradients), then <c>glReadPixels</c> into the DIB. Best for complex scenes when a usable GL driver is present.</item>
/// </list>
/// Both paths produce the same <see cref="IImage"/> contract; pick by calling exactly one
/// <c>Register*</c> method at startup.
/// </summary>
public static class SkiaGdiInterop
{
    public static void Register() => SkiaInterop.Register(new Gdi.GdiSkiaInteropProvider());

    public static void RegisterGL() => SkiaInterop.Register(new Gdi.GdiGLReadbackSkiaInteropProvider());

    public static ApplicationBuilder UseSkiaGdiInterop(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }

    public static ApplicationBuilder UseSkiaGdiGLInterop(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        RegisterGL();
        return builder;
    }
}
