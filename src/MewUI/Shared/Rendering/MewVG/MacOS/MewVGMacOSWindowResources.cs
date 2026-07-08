using System.Runtime.CompilerServices;

using Aprillz.MewVG;
using Aprillz.MewVG.Interop;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed class MewVGMetalWindowResources : IDisposable
{
    private static readonly nint ClsNSAutoreleasePool = ObjCRuntime.GetClass("NSAutoreleasePool");
    private static readonly nint ClsMTLTextureDescriptor = ObjCRuntime.GetClass("MTLTextureDescriptor");
    private static readonly nint SelAlloc = ObjCRuntime.Selectors.alloc;
    private static readonly nint SelInit = ObjCRuntime.Selectors.init;
    private static readonly nint SelRelease = ObjCRuntime.Selectors.release;
    private static readonly nint SelNewCommandQueue = ObjCRuntime.RegisterSelector("newCommandQueue");
    private static readonly nint SelSetDevice = ObjCRuntime.RegisterSelector("setDevice:");
    private static readonly nint SelSetPixelFormat = ObjCRuntime.RegisterSelector("setPixelFormat:");
    private static readonly nint SelSetFramebufferOnly = ObjCRuntime.RegisterSelector("setFramebufferOnly:");
    private static readonly nint SelSetPresentsWithTransaction = ObjCRuntime.RegisterSelector("setPresentsWithTransaction:");
    private static readonly nint SelSetAllowsNextDrawableTimeout = ObjCRuntime.RegisterSelector("setAllowsNextDrawableTimeout:");
    private static readonly nint SelNewTextureWithDescriptor = ObjCRuntime.RegisterSelector("newTextureWithDescriptor:");
    private static readonly nint SelTexture2DDescriptorWithPixelFormat = ObjCRuntime.RegisterSelector("texture2DDescriptorWithPixelFormat:width:height:mipmapped:");
    private static readonly nint SelSetUsage = ObjCRuntime.RegisterSelector("setUsage:");
    private static readonly nint SelSetStorageMode = ObjCRuntime.RegisterSelector("setStorageMode:");
    private const ulong MTLTextureUsageRenderTarget = 1ul << 2;
    private const ulong MTLStorageModePrivate = 2;

    // Depth/stencil pool depth matches the color drawable's triple buffering (CAMetalLayer
    // default maximumDrawableCount = 3), so the stencil attachment pipelines the same way the
    // color texture does. A single shared stencil texture forces Metal's hazard tracker to
    // serialize frame N+1 behind frame N's stencil access; N independent slots let up to N
    // frames stay in flight. Kept <= MNVG_INIT_BUFFER_COUNT (4, the vertex/uniform ring depth)
    // since there is no benefit pooling stencil deeper than the color drawable pool.
    private const int STENCIL_POOL_SIZE = 3;

    private bool _disposed;
    private readonly nint[] _stencilTextures = new nint[STENCIL_POOL_SIZE];
    private int _stencilSlotIndex = -1;
    private int _stencilWidthPx;
    private int _stencilHeightPx;

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

        var vg = new NanoVGMetal(device, NVGcreateFlags.Antialias)
        {
            PixelFormat = MTLPixelFormat.BGRA8Unorm,
            // Use a depth-stencil format for reliable stencil rendering on macOS.
            // Stencil8 alone is not consistently renderable across devices/drivers.
            StencilFormat = MTLPixelFormat.Depth32Float_Stencil8
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

    /// <summary>
    /// Advances to the next pooled stencil slot and returns its texture, creating or resizing
    /// it as needed. Must be called exactly once per frame, at the frame boundary (from
    /// <c>TryBeginFrame</c>) - the render pass bound in that same frame keeps this slot for its
    /// single render encoder, so slot rotation here never splits a frame's clip stack across
    /// two textures.
    /// </summary>
    public nint EnsureStencilTexture(int widthPx, int heightPx)
    {
        if (_disposed)
        {
            return 0;
        }

        widthPx = Math.Max(1, widthPx);
        heightPx = Math.Max(1, heightPx);

        if (_stencilWidthPx != widthPx || _stencilHeightPx != heightPx)
        {
            using var pool = new AutoReleasePool();

            for (int i = 0; i < _stencilTextures.Length; i++)
            {
                ReleaseIfNotNull(ref _stencilTextures[i]);
            }

            _stencilWidthPx = widthPx;
            _stencilHeightPx = heightPx;
        }

        _stencilSlotIndex = (_stencilSlotIndex + 1) % STENCIL_POOL_SIZE;

        if (_stencilTextures[_stencilSlotIndex] == 0)
        {
            using var pool = new AutoReleasePool();
            _stencilTextures[_stencilSlotIndex] = CreateTexture(MTLPixelFormat.Depth32Float_Stencil8, widthPx, heightPx);
        }

        return _stencilTextures[_stencilSlotIndex];
    }

    private nint CreateTexture(MTLPixelFormat format, int widthPx, int heightPx)
    {
        if (ClsMTLTextureDescriptor == 0 || SelTexture2DDescriptorWithPixelFormat == 0)
        {
            return 0;
        }

        // +[MTLTextureDescriptor texture2DDescriptorWithPixelFormat:width:height:mipmapped:]
        nint desc = ObjCRuntime.SendMessage(
            ClsMTLTextureDescriptor,
            SelTexture2DDescriptorWithPixelFormat,
            (uint)format,
            (UIntPtr)(uint)widthPx,
            (UIntPtr)(uint)heightPx,
            false);

        if (desc == 0)
        {
            return 0;
        }

        // Configure usage/storage for render target.
        if (SelSetUsage != 0)
        {
            ObjCRuntime.SendMessageNoReturn(desc, SelSetUsage, (UInt64)MTLTextureUsageRenderTarget);
        }

        if (SelSetStorageMode != 0)
        {
            ObjCRuntime.SendMessageNoReturn(desc, SelSetStorageMode, (UInt64)MTLStorageModePrivate);
        }

        if (Device == 0 || SelNewTextureWithDescriptor == 0)
        {
            return 0;
        }

        return ObjCRuntime.SendMessage(Device, SelNewTextureWithDescriptor, desc);
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

        for (int i = 0; i < _stencilTextures.Length; i++)
        {
            ReleaseIfNotNull(ref _stencilTextures[i]);
        }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReleaseIfNotNull(ref nint obj)
    {
        if (obj == 0)
        {
            return;
        }

        ObjCRuntime.Release(obj);
        obj = 0;
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
