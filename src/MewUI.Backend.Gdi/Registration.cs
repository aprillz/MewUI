using Aprillz.MewUI.Rendering.Gdi;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the GDI backend with <see cref="Application"/>.
/// </summary>
public static class GdiBackend
{
    public static string BackendIdentifier => GdiGraphicsFactory.BackendIdentifier;

    public static void Register()
        => Application.RegisterGraphicsFactory(static () => new GdiGraphicsFactory());

    public static ApplicationBuilder UseGdi(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}
