using Aprillz.MewUI.Rendering.Gdi;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the GDI backend with <see cref="Application"/>.
/// </summary>
public static class GdiBackend
{
    /// <summary>Gets the identifier the backend registers under.</summary>
    public static string BackendIdentifier => GdiGraphicsFactory.BackendIdentifier;

    /// <summary>Registers the GDI graphics factory for Win32 window surfaces with <see cref="Application"/>.</summary>
    public static void Register()
        => Application.RegisterGraphicsFactory(static () => new GdiGraphicsFactory(), Platform.PlatformSurfaceKind.Win32, "Gdi");

    /// <summary>Registers the GDI backend and returns <paramref name="builder"/>.</summary>
    public static ApplicationBuilder UseGdi(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}
