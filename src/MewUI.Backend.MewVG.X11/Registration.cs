using Aprillz.MewUI.Platform.Linux.X11;
using Aprillz.MewUI.Rendering.MewVG;
using Aprillz.MewUI.Rendering.OpenGL;

namespace Aprillz.MewUI;

/// <summary>
/// Registers the MewVG (X11 GL) backend with <see cref="Application"/>.
/// </summary>
public static class MewVGX11Backend
{
    public static string BackendIdentifier => MewVGX11GraphicsFactory.BackendIdentifier;

    public static void Register()
    {
        // Pick the GLX backend once. Everything GL-API-specific (window/worker context, current
        // context) goes through it; the platform layer's visual chooser is derived from it so the
        // path is chosen in exactly one place. The chooser keeps glX/egl out of the platform layer.
        X11GLBackendRegistry.Current ??= new GlxBackend();
        X11GLVisualChooserRegistry.Current ??= X11GLBackendRegistry.Current.VisualChooser;
        Application.RegisterGraphicsFactory(BackendIdentifier, static () => MewVGX11GraphicsFactory.Instance);
    }

    /// <summary>
    /// Registers the backend in EGL mode instead of the default GLX. The whole app then renders
    /// through an EGL context (desktop GL via <c>EGL_OPENGL_API</c>, so NanoVG/MewVG is unchanged),
    /// which is required for v4l2m2m DRM_PRIME dma_buf/EGLImage zero-copy video on the Pi - that
    /// import segfaults in a GLX context. Call this INSTEAD of <see cref="Register"/>.
    /// </summary>
    public static void RegisterEgl()
    {
        X11GLBackendRegistry.Current = new EglBackend();
        X11GLVisualChooserRegistry.Current = X11GLBackendRegistry.Current.VisualChooser;
        Application.RegisterGraphicsFactory(BackendIdentifier, static () => MewVGX11GraphicsFactory.Instance);
    }

    public static ApplicationBuilder UseMewVGX11(this ApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        Register();

        return builder;
    }
}
