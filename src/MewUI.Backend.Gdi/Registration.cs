using Aprillz.MewUI.Rendering.Gdi;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the GDI backend with <see cref="Application"/>.
/// </summary>
public static class GdiBackend
{
    public const string BackendId = "gdi";

    public static void Register()
        => Application.RegisterGraphicsFactory(BackendId, static () => GdiGraphicsFactory.Instance);

    public static ApplicationBuilder UseGdi(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}
