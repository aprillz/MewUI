using Aprillz.MewUI.Rendering.Framebuffer;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the Linux framebuffer rendering backend with <see cref="Application"/>.
/// </summary>
public static class FramebufferBackend
{
    public const string BackendIdentifier = FramebufferGraphicsFactory.BackendIdentifier;

    public static void Register()
        => Application.RegisterGraphicsFactory(BackendIdentifier, static () => FramebufferGraphicsFactory.Instance);

    public static ApplicationBuilder UseFramebufferBackend(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        return builder;
    }
}
