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
    private static readonly nint SelSetTextureType = Metal.Sel.SetTextureType;
    private static readonly nint SelSetSampleCount = Metal.Sel.SetSampleCount;

    // MTLTextureUsageRenderTarget = 1 << 2
    private const ulong MTLTextureUsageRenderTarget = 1ul << 2;
    // MTLStorageModePrivate = 2
    private const ulong MTLStorageModePrivate = 2;

    /// <summary>
    /// MSAA sample count. 1 = no MSAA, 4 or 8 for hardware multisampling.
    /// </summary>
    internal const int MsaaSampleCount = 4;

    private bool _disposed;
    private nint _stencilTexture;
    private int _stencilWidthPx;
    private int _stencilHeightPx;
    private nint _msaaColorTexture;
    private int _msaaColorWidthPx;
    private int _msaaColorHeightPx;

    public nint Hwnd { get; }
    public nint Layer { get; }
    public nint Device { get; }
    public nint CommandQueue { get; }
    public NanoVGMetal Vg { get; }
    public MewVGMetalTextCache TextCache { get; }

    private MewVGMetalWindowResources(nint hwnd, nint layer, nint device, nint commandQueue, NanoVGMetal vg)
    {
        Hwnd = hwnd;
        Layer = layer;
        Device = device;
        CommandQueue = commandQueue;
        Vg = vg;
        TextCache = new MewVGMetalTextCache(vg);
    }

    public static MewVGMetalWindowResources Create(nint hwnd, nint metalLayer)
    {
        if (hwnd == 0 || metalLayer == 0)
        {
            throw new ArgumentException("Invalid window handle or CAMetalLayer pointer.");
        }

        using var pool = new AutoReleasePool();

        nint device = MetalDevice.CreateSystemDefaultDevice();
        if (device == 0)
        {
            throw new PlatformNotSupportedException("MetalDevice.CreateSystemDefaultDevice() returned null.");
        }

        // With MSAA enabled, disable geometry-based AA (fringe triangles are not needed).
        var flags = MsaaSampleCount > 1
            ? NVGcreateFlags.None
            : NVGcreateFlags.Antialias;

        var vg = new NanoVGMetal(device, flags)
        {
            PixelFormat = MTLPixelFormat.BGRA8Unorm,
            // Use a depth-stencil format for reliable stencil rendering on macOS.
            // Stencil8 alone is not consistently renderable across devices/drivers.
            StencilFormat = MTLPixelFormat.Depth32Float_Stencil8,
            SampleCount = MsaaSampleCount
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

        // Live-resize jitter fix: ensure presentation is synchronized with the resize transaction.
        if (SelSetPresentsWithTransaction != 0)
        {
            ObjCRuntime.SendMessageNoReturn(metalLayer, SelSetPresentsWithTransaction, (UInt64)1);
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

    public nint EnsureStencilTexture(int widthPx, int heightPx)
    {
        if (_disposed)
        {
            return 0;
        }

        widthPx = Math.Max(1, widthPx);
        heightPx = Math.Max(1, heightPx);

        if (_stencilTexture != 0 &&
            _stencilWidthPx == widthPx &&
            _stencilHeightPx == heightPx)
        {
            return _stencilTexture;
        }

        using var pool = new AutoReleasePool();

        ReleaseIfNotNull(ref _stencilTexture);
        _stencilWidthPx = widthPx;
        _stencilHeightPx = heightPx;

        _stencilTexture = CreateTexture(
            MTLPixelFormat.Depth32Float_Stencil8, widthPx, heightPx, MsaaSampleCount);
        return _stencilTexture;
    }

    public nint EnsureMsaaColorTexture(int widthPx, int heightPx)
    {
        if (_disposed || MsaaSampleCount <= 1)
        {
            return 0;
        }

        widthPx = Math.Max(1, widthPx);
        heightPx = Math.Max(1, heightPx);

        if (_msaaColorTexture != 0 &&
            _msaaColorWidthPx == widthPx &&
            _msaaColorHeightPx == heightPx)
        {
            return _msaaColorTexture;
        }

        using var pool = new AutoReleasePool();

        ReleaseIfNotNull(ref _msaaColorTexture);
        _msaaColorWidthPx = widthPx;
        _msaaColorHeightPx = heightPx;

        _msaaColorTexture = CreateTexture(
            MTLPixelFormat.BGRA8Unorm, widthPx, heightPx, MsaaSampleCount);
        return _msaaColorTexture;
    }

    private nint CreateTexture(MTLPixelFormat format, int widthPx, int heightPx, int sampleCount)
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

        // Configure MSAA.
        if (sampleCount > 1)
        {
            if (SelSetTextureType != 0)
            {
                ObjCRuntime.SendMessageNoReturn(desc, SelSetTextureType, (UInt64)MTLTextureType.Type2DMultisample);
            }

            if (SelSetSampleCount != 0)
            {
                ObjCRuntime.SendMessageNoReturn(desc, SelSetSampleCount, (UInt64)sampleCount);
            }
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

        ReleaseIfNotNull(ref _stencilTexture);
        ReleaseIfNotNull(ref _msaaColorTexture);

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
