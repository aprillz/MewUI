namespace Aprillz.MewUI.Platform.Linux.X11;

/// <summary>
/// Minimal GLX surface information for an X11 window.
/// This is produced by the X11 platform backend and consumed by OpenGL (GLX) backends.
/// </summary>
public interface IX11GlxWindowSurface : IWindowSurface
{
    nint Display { get; }

    nint Window { get; }

    X11GlxVisualInfo VisualInfo { get; }
}

/// <summary>
/// Portable representation of XVisualInfo used for GLX context creation.
/// </summary>
public readonly struct X11GlxVisualInfo
{
    public nint Visual { get; }
    public nint VisualId { get; }
    public int Screen { get; }
    public int Depth { get; }
    public int Class { get; }
    public ulong RedMask { get; }
    public ulong GreenMask { get; }
    public ulong BlueMask { get; }
    public int ColormapSize { get; }
    public int BitsPerRgb { get; }

    public X11GlxVisualInfo(
        nint visual,
        nint visualId,
        int screen,
        int depth,
        int @class,
        ulong redMask,
        ulong greenMask,
        ulong blueMask,
        int colormapSize,
        int bitsPerRgb)
    {
        Visual = visual;
        VisualId = visualId;
        Screen = screen;
        Depth = depth;
        Class = @class;
        RedMask = redMask;
        GreenMask = greenMask;
        BlueMask = blueMask;
        ColormapSize = colormapSize;
        BitsPerRgb = bitsPerRgb;
    }
}

