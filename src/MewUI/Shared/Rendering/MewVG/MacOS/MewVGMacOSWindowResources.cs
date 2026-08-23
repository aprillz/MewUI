using Aprillz.MewVG;
using Aprillz.MewVG.Interop;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed class MewVGMetalWindowResources : IDisposable, IMewVGWindowCacheMaintenance
{
    private static readonly nint ClsNSAutoreleasePool = ObjCRuntime.GetClass("NSAutoreleasePool");
    private static readonly nint SelAlloc = ObjCRuntime.Selectors.alloc;
    private static readonly nint SelInit = ObjCRuntime.Selectors.init;
    private static readonly nint SelRelease = ObjCRuntime.Selectors.release;
    private static readonly nint SelNewCommandQueue = ObjCRuntime.RegisterSelector("newCommandQueue");
    private static readonly nint SelSetDevice = ObjCRuntime.RegisterSelector("setDevice:");
    private static readonly nint SelSetPixelFormat = ObjCRuntime.RegisterSelector("setPixelFormat:");
    private static readonly nint SelSetFramebufferOnly = ObjCRuntime.RegisterSelector("setFramebufferOnly:");
    private static readonly nint SelSetPresentsWithTransaction = ObjCRuntime.RegisterSelector("setPresentsWithTransaction:");
    private static readonly nint SelSetAllowsNextDrawableTimeout = ObjCRuntime.RegisterSelector("setAllowsNextDrawableTimeout:");

    private bool _disposed;

    public nint Hwnd { get; }

    public nint Layer { get; }

    public nint Device { get; }

    public nint CommandQueue { get; }

    public NanoVGMetal Vg { get; }

    public MewVGMetalTextCache TextCache { get; }

    private MewVGMacOSGraphicsContext? _cachedContext;

    internal MewVGMacOSGraphicsContext GetOrCreateContext(
        MewVGMetalOffscreenSurfaceProvider offscreenProvider,
        Action<GpuInteropInvalidatedEventArgs>? gpuInteropInvalidated)
        => _cachedContext ??= MewVGMacOSGraphicsContext.CreateForWindow(this, offscreenProvider, gpuInteropInvalidated);

    /// <summary>
    /// Drops the cached graphics context reference when the context is
    /// disposed externally (e.g. on window resize). Without this, the next
    /// <see cref="GetOrCreateContext"/> hands out the dead context whose
    /// pooled <c>_saveStack</c> has already been returned to
    /// <c>CollectionPool</c> ??a subsequent Rent then aliases the same
    /// Stack between two contexts and they corrupt each other's state.
    /// (Same root cause as the Win32 fix in <c>MewVGWindowResources</c>.)
    /// </summary>
    internal void InvalidateCachedContext(MewVGMacOSGraphicsContext ctx)
    {
        if (ReferenceEquals(_cachedContext, ctx))
        {
            _cachedContext = null;
        }
    }

    private MewVGMetalWindowResources(nint hwnd, nint layer, nint device, nint commandQueue, NanoVGMetal vg)
    {
        Hwnd = hwnd;
        Layer = layer;
        Device = device;
        CommandQueue = commandQueue;
        Vg = vg;
        TextCache = new MewVGMetalTextCache(vg);
    }

    public static MewVGMetalWindowResources Create(nint hwnd, nint metalLayer, nint device)
    {
        if (hwnd == 0 || metalLayer == 0)
        {
            throw new ArgumentException("Invalid window handle or CAMetalLayer pointer.");
        }

        if (device == 0)
        {
            throw new ArgumentException("Metal device handle must be non-zero (factory-provided).", nameof(device));
        }

        using var pool = new AutoReleasePool();

        var vg = new NanoVGMetal(device)
        {
            PixelFormat = MTLPixelFormat.BGRA8Unorm
        };

        // Configure layer to match the device and pixel format used by the renderer.
        if (SelSetDevice != 0)
        {
            ObjCRuntime.SendMessageNoReturn(metalLayer, SelSetDevice, device);
        }

        if (SelSetPixelFormat != 0)
        {
            ObjCRuntime.SendMessageNoReturn(metalLayer, SelSetPixelFormat, (UInt64)MTLPixelFormat.BGRA8Unorm);
        }

        if (SelSetFramebufferOnly != 0)
        {
            ObjCRuntime.SendMessageNoReturn(metalLayer, SelSetFramebufferOnly, (UInt64)1);
        }

        if (SelSetAllowsNextDrawableTimeout != 0)
        {
            ObjCRuntime.SendMessageNoReturn(metalLayer, SelSetAllowsNextDrawableTimeout, (UInt64)0);
        }

        nint commandQueue = ObjCRuntime.SendMessage(device, SelNewCommandQueue);
        if (commandQueue == 0)
        {
            if (vg is IDisposable disposable)
            {
                disposable.Dispose();
            }

            ObjCRuntime.Release(device);
            throw new InvalidOperationException("Failed to create MTLCommandQueue.");
        }

        return new MewVGMetalWindowResources(hwnd, metalLayer, device, commandQueue, vg);
    }

    public void TrimCaches()
    {
        if (!_disposed)
        {
            TextCache.Trim();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _cachedContext?.Dispose();
        _cachedContext = null;

        TextCache.Dispose();

        if (Vg is IDisposable disposable)
        {
            disposable.Dispose();
        }

        nint queue = CommandQueue;
        if (queue != 0)
        {
            ObjCRuntime.Release(queue);
        }

        nint device = Device;
        if (device != 0)
        {
            ObjCRuntime.Release(device);
        }
    }

    private readonly struct AutoReleasePool : IDisposable
    {
        private readonly nint _pool;

        public AutoReleasePool()
        {
            if (ClsNSAutoreleasePool == 0 || SelAlloc == 0 || SelInit == 0)
            {
                _pool = 0;
                return;
            }

            nint pool = ObjCRuntime.SendMessage(ClsNSAutoreleasePool, SelAlloc);
            _pool = pool != 0 ? ObjCRuntime.SendMessage(pool, SelInit) : 0;
        }

        public void Dispose()
        {
            if (_pool != 0 && SelRelease != 0)
            {
                ObjCRuntime.SendMessageNoReturn(_pool, SelRelease);
            }
        }
    }
}
