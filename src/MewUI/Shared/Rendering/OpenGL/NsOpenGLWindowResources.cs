using System.Runtime.InteropServices;

using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Rendering.OpenGL;

internal sealed unsafe class NsOpenGLWindowResources : IOpenGLWindowResources
{
    public OpenGLTextCache TextCache { get; } = new();

    public bool SupportsBgra { get; }
    public bool SupportsNpotTextures { get; }

    private nint _nsContext;
    private bool _disposed;

    private NsOpenGLWindowResources(nint nsContext, bool supportsBgra, bool supportsNpot)
    {
        _nsContext = nsContext;
        SupportsBgra = supportsBgra;
        SupportsNpotTextures = supportsNpot;
    }

    public static NsOpenGLWindowResources Create(nint nsContext)
    {
        if (nsContext == 0)
        {
            throw new ArgumentException("Invalid NSOpenGLContext.", nameof(nsContext));
        }

        // Make current temporarily to query extensions.
        MakeCurrentStatic(nsContext);

        var ext = GL.GetExtensions() ?? string.Empty;
        bool supportsBgra = ext.Contains("GL_EXT_bgra", StringComparison.OrdinalIgnoreCase) ||
                            ext.Contains("GL_APPLE_packed_pixels", StringComparison.OrdinalIgnoreCase);
        bool supportsNpot = true; // Core in 2.0+; treat as available.

        ClearCurrentStatic();

        return new NsOpenGLWindowResources(nsContext, supportsBgra, supportsNpot);
    }

    public void TrackTexture(uint textureId)
    {
        // Texture lifetime tracking is handled by OpenGLTextCache; no extra tracking for now.
    }

    public void MakeCurrent(nint deviceOrDisplay)
    {
        if (_disposed)
        {
            return;
        }

        // deviceOrDisplay is the NSOpenGLContext.
        _nsContext = deviceOrDisplay != 0 ? deviceOrDisplay : _nsContext;
        MakeCurrentStatic(_nsContext);
    }

    public void ReleaseCurrent()
        => ClearCurrentStatic();

    public void SwapBuffers(nint deviceOrDisplay, nint nativeWindow)
    {
        if (_disposed)
        {
            return;
        }

        var ctx = deviceOrDisplay != 0 ? deviceOrDisplay : _nsContext;
        if (ctx == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint(ctx, SelFlushBuffer);
    }

    public void SetSwapInterval(int interval)
    {
        if (_disposed || _nsContext == 0)
        {
            return;
        }

        // NSOpenGLContextParameterSwapInterval = 222
        const int NSOpenGLContextParameterSwapInterval = 222;
        int value = Math.Clamp(interval, 0, 1);

        // During live-resize, allowing swap interval 0 can produce visible tearing/jitter on macOS.
        // Force vsync while AppKit is actively resizing the view.
        if (value == 0)
        {
            var view = ObjC.MsgSend_nint(_nsContext, SelView);
            if (view != 0 && ObjC.MsgSend_bool(view, SelInLiveResize))
            {
                value = 1;
            }
        }

        ObjC.MsgSend_void_nint_intPtr(_nsContext, SelSetValuesForParameter, &value, NSOpenGLContextParameterSwapInterval);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        TextCache.Dispose();
        _nsContext = 0;
    }

    private static readonly nint SelMakeCurrentContext = ObjC.Sel("makeCurrentContext");
    private static readonly nint SelClearCurrentContext = ObjC.Sel("clearCurrentContext");
    private static readonly nint SelFlushBuffer = ObjC.Sel("flushBuffer");
    private static readonly nint SelView = ObjC.Sel("view");
    private static readonly nint SelInLiveResize = ObjC.Sel("inLiveResize");
    private static readonly nint SelSetValuesForParameter = ObjC.Sel("setValues:forParameter:");

    private static void MakeCurrentStatic(nint nsContext)
        => ObjC.MsgSend_void(nsContext, SelMakeCurrentContext);

    private static void ClearCurrentStatic()
        => ObjC.MsgSend_void_nint(ObjC.GetClass("NSOpenGLContext"), SelClearCurrentContext);

    internal static (int WidthPx, int HeightPx) GetViewSizePx(nint nsView, double dpiScale)
    {
        // frame -> NSRect (points)
        // Prefer frame over bounds for live-resize correctness (see MacOSInterop.GetViewBounds).
        var rect = ObjC.MsgSend_rect(nsView, SelFrame);
        // Use convertRectToBacking: to get the actual backing pixel size that AppKit will use.
        // This avoids off-by-1 rounding differences (and occasional stretched frames) during live resize.
        var pointsRect = new NSRect(new NSPoint(0, 0), rect.size);
        var backingRect = ObjC.MsgSend_rect_rect(nsView, SelConvertRectToBacking, pointsRect);
        int w = (int)Math.Round(backingRect.size.width);
        int h = (int)Math.Round(backingRect.size.height);
        if (w <= 0 || h <= 0)
        {
            w = (int)Math.Round(rect.size.width * dpiScale);
            h = (int)Math.Round(rect.size.height * dpiScale);
        }

        w = Math.Max(1, w);
        h = Math.Max(1, h);
        return (w, h);
    }

    private static readonly nint SelFrame = ObjC.Sel("frame");
    private static readonly nint SelConvertRectToBacking = ObjC.Sel("convertRectToBacking:");
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NSPoint
{
    public readonly double x;
    public readonly double y;
    public NSPoint(double x, double y) { this.x = x; this.y = y; }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NSSize
{
    public readonly double width;
    public readonly double height;
    public NSSize(double width, double height) { this.width = width; this.height = height; }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NSRect
{
    public readonly NSPoint origin;
    public readonly NSSize size;
    public NSRect(NSPoint origin, NSSize size) { this.origin = origin; this.size = size; }
}

internal static unsafe partial class ObjC
{
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
    private static partial nint objc_getClass(byte* name);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static partial nint sel_registerName(byte* name);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void(nint receiver, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void_nint(nint receiver, nint selector, nint a0);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_void_intPtr_int(nint receiver, nint selector, int* value, int parameter);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_nint(nint receiver, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial byte objc_msgSend_bool(nint receiver, nint selector);

    // On Apple Silicon (arm64), objc_msgSend_stret is not exported and struct returns follow the normal ABI.
    // On Intel (x64), objc_msgSend_stret is used for large struct returns like NSRect.
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial NSRect objc_msgSend_NSRect(nint receiver, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend_stret")]
    private static partial void objc_msgSend_stret_NSRect(out NSRect ret, nint receiver, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial NSRect objc_msgSend_NSRect_rect(nint receiver, nint selector, NSRect a0);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend_stret")]
    private static partial void objc_msgSend_stret_NSRect_rect(out NSRect ret, nint receiver, nint selector, NSRect a0);

    public static nint GetClass(string name)
    {
        var utf8 = System.Text.Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = utf8)
        {
            return objc_getClass(p);
        }
    }

    public static nint Sel(string name)
    {
        var utf8 = System.Text.Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = utf8)
        {
            return sel_registerName(p);
        }
    }

    public static void MsgSend_void(nint receiver, nint selector)
    {
        objc_msgSend_void(receiver, selector);
    }

    public static nint MsgSend_nint(nint receiver, nint selector)
        => objc_msgSend_nint(receiver, selector);

    public static bool MsgSend_bool(nint receiver, nint selector)
        => objc_msgSend_bool(receiver, selector) != 0;

    public static void MsgSend_void_nint(nint receiver, nint selector)
    {
        objc_msgSend_void(receiver, selector);
    }

    public static void MsgSend_void_nint(nint receiver, nint selector, nint a0)
    {
        objc_msgSend_void_nint(receiver, selector, a0);
    }

    public static NSRect MsgSend_rect(nint receiver, nint selector)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            return objc_msgSend_NSRect(receiver, selector);
        }

        objc_msgSend_stret_NSRect(out var rect, receiver, selector);
        return rect;        
    }

    public static NSRect MsgSend_rect_rect(nint receiver, nint selector, NSRect a0)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            return objc_msgSend_NSRect_rect(receiver, selector, a0);
        }

        objc_msgSend_stret_NSRect_rect(out var rect, receiver, selector, a0);
        return rect;
    }

    public static void MsgSend_void_nint_intPtr(nint receiver, nint selector, int* value, int parameter)
    {
        objc_msgSend_void_intPtr_int(receiver, selector, value, parameter);
    }
}
