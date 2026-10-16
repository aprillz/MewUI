using Aprillz.MewUI.Rendering.MewVG;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the MewVG backend for the Linux framebuffer platform with <see cref="Application"/>: EGL on the
/// DRM/KMS output's GBM surface, or EGL without a window system read back to an fbdev device.
/// </summary>
public static class MewVGFramebufferBackend
{
    public static string BackendIdentifier => MewVGFramebufferGraphicsFactory.BackendIdentifier;

    public static void Register()
        => Application.RegisterGraphicsFactory(static () => new MewVGFramebufferGraphicsFactory(),
            Platform.PlatformSurfaceKind.Framebuffer, "MewVG.Framebuffer");

    public static ApplicationBuilder UseMewVGFramebuffer(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        Register();

        return builder;
    }
}
