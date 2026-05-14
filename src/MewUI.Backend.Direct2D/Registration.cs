using Aprillz.MewUI.Rendering.Direct2D;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the Direct2D backend with <see cref="Application"/>.
/// </summary>
public static class Direct2DBackend
{
    public static string BackendIdentifier => Direct2DGraphicsFactory.BackendIdentifier;

    public static void Register()
        => Application.RegisterGraphicsFactory(BackendIdentifier, static () => Direct2DGraphicsFactory.Instance);

    public static ApplicationBuilder UseDirect2D(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}
