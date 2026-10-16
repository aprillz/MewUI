using Aprillz.MewUI.Native;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>Loads MewVG's GL entry points once, through EGL, for the API the framebuffer device chose.</summary>
internal static class MewVGGLBootstrapFramebuffer
{
    private static int _initialized;

    public static void EnsureInitialized(bool gles)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        MewVGGL.Initialize(EglGLLoader.Resolve, gles ? MewVGGLProfile.Gles3 : MewVGGLProfile.Gl3Core);
    }
}
