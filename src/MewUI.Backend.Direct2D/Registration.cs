using Aprillz.MewUI.Rendering.Direct2D;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the Direct2D backend with <see cref="Application"/>.
/// </summary>
public static class Direct2DBackend
{
    /// <summary>Gets the identifier the backend registers under.</summary>
    public static string BackendIdentifier => Direct2DGraphicsFactory.BackendIdentifier;

    /// <summary>Registers the Direct2D graphics factory for Win32 window surfaces with <see cref="Application"/>.</summary>
    public static void Register()
        => Application.RegisterGraphicsFactory(static () => new Direct2DGraphicsFactory(), Platform.PlatformSurfaceKind.Win32, "Direct2D");

    /// <summary>Registers the Direct2D backend and returns <paramref name="builder"/>.</summary>
    public static ApplicationBuilder UseDirect2D(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}
