using Aprillz.MewUI.Platform.Framebuffer;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the single-screen Linux framebuffer platform host with <see cref="Application"/>.
/// </summary>
public static class FramebufferPlatform
{
    public const string PlatformId = "framebuffer";

    public static void Register()
        => Application.RegisterPlatformHost(PlatformId, static () => new FramebufferPlatformHost());

    public static ApplicationBuilder UseFramebuffer(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register();
        FramebufferBackend.Register();
        return builder;
    }
}
