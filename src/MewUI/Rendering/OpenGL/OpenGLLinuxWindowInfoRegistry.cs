using System.Collections.Concurrent;

using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Rendering.OpenGL;

internal static class OpenGLLinuxWindowInfoRegistry
{
    private static readonly ConcurrentDictionary<nint, XVisualInfo> _visualByWindow = new();

    public static void RegisterVisual(nint window, XVisualInfo visualInfo)
    {
        if (window == 0)
            return;
        _visualByWindow[window] = visualInfo;
    }

    public static bool TryGetVisual(nint window, out XVisualInfo visualInfo)
        => _visualByWindow.TryGetValue(window, out visualInfo);

    public static void Unregister(nint window)
    {
        if (window == 0)
            return;
        _visualByWindow.TryRemove(window, out _);
    }
}

