using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.Direct2D;

namespace Aprillz.MewUI.Rendering.Direct2D;

public sealed unsafe partial class Direct2DGraphicsFactory
{
    private const int D2DERR_RECREATE_TARGET = unchecked((int)0x8899000C);
    private const int D2DERR_WRONG_RESOURCE_DOMAIN = unchecked((int)0x88990015);
    private const int DXGI_ERROR_DEVICE_REMOVED = unchecked((int)0x887A0005);
    private const int DXGI_ERROR_DEVICE_HUNG = unchecked((int)0x887A0006);
    private const int DXGI_ERROR_DEVICE_RESET = unchecked((int)0x887A0007);

    // Controls whether the factory creates its default internal D3D11 -> DXGI -> D2D
    // device chain when GPU rendering is first needed.
    private const bool ENABLE_DEFAULT_DEVICECHAIN = true;

    private enum GpuDeviceState
    {
        Uninitialized,
        Ready,
        Unavailable,
        Lost,
        Disposed,
    }

    // GPU pipeline (D3D11 + ID2D1Device + shared ID2D1DeviceContext) for filter operations
    // that want to stay on-GPU end-to-end. Lazy-initialized on first GPU bitmap creation;
    // remains 0 if init fails (e.g. headless WARP-less environment) and the factory falls
    // back to DIB-backed offscreen rendering.
    private nint _d3dDevice;
    private nint _dxgiDevice;
    private nint _d2dDevice;
    private nint _filterDeviceContext;
    private GpuDeviceState _gpuDeviceState;
    private readonly List<WeakReference<Direct2DGpuPixelRenderSurface>> _trackedGpuBitmapTargets = [];

    /// <summary>Shared <c>ID2D1DeviceContext*</c> for filter pipeline GPU bitmaps and
    /// effects. Returns 0 if the GPU pipeline isn't available (caller should fall back to
    /// DIB-backed rendering).</summary>
    /// <remarks>
    /// Internal-only: external bridges (e.g. video sample D3D11 → D2D interop) must NOT
    /// reach in here, since this DC participates in a nested-BeginDraw counter that
    /// external BeginDraw calls would corrupt. External code that needs an
    /// ID2D1DeviceContext should create its own via <see cref="NativeD2DDevice"/>; bitmaps
    /// are device-bound, not context-bound, so they remain compatible with the shared DC.
    /// </remarks>
    internal nint SharedFilterDeviceContext
    {
        get
        {
            EnsureFilterDeviceContext();
            return _filterDeviceContext;
        }
    }

    /// <summary>
    /// Native <c>ID3D11Device*</c> backing this factory's GPU pipeline. Returns 0 when the
    /// GPU pipeline isn't available (factory falls back to DIB rendering). External D3D11
    /// resource producers (video decoders, camera capture, custom interop) read this to
    /// create resources on the same device - enabling zero-copy hand-off - or to compare
    /// against their own device for shared-handle import decisions.
    /// </summary>
    public nint NativeD3D11Device
    {
        get
        {
            EnsureFilterDeviceContext();
            return _d3dDevice;
        }
    }

    /// <summary>
    /// Native <c>ID2D1Device*</c> backing this factory's GPU pipeline. Returns 0 when the
    /// GPU pipeline isn't available. External code that needs to call D2D APIs (e.g.
    /// <c>CreateBitmapFromDxgiSurface</c>) should create its own
    /// <c>ID2D1DeviceContext</c> from this device - bitmaps created on the external
    /// context are still usable by the factory's internal DC because D2D bitmaps are
    /// device-bound, not context-bound.
    /// </summary>
    public nint NativeD2DDevice
    {
        get
        {
            EnsureFilterDeviceContext();
            return _d2dDevice;
        }
    }

    // Reference count for nested BeginDraw on the shared filter DC. D2D doesn't support
    // nesting BeginDraw on the same device context - once BeginDraw is active you must
    // EndDraw before the next BeginDraw. But our pipeline naturally nests: SvgView opens
    // a frame on the shared DC (its offscreen GPU bitmap), then SvgFilter creates another
    // GPU bitmap (source layer) and opens another frame on the same shared DC. The inner
    // pass switches the DC's target via SetTarget but does NOT issue a fresh BeginDraw -
    // the outer BeginDraw is still active and covers all draws regardless of bound target.
    // EnterSharedDcDraw returns true only on the outermost entry; ExitSharedDcDraw returns
    // true only when leaving the outermost level. Both gate BeginDraw/EndDraw accordingly.
    private int _sharedDcDrawDepth;

    internal bool EnterSharedDcDraw()
        => Interlocked.Increment(ref _sharedDcDrawDepth) == 1;

    internal bool ExitSharedDcDraw()
        => Interlocked.Decrement(ref _sharedDcDrawDepth) == 0;

    // Guards EnsureFilterDeviceContext against concurrent first-use from multiple threads
    // (UI thread first paint vs SvgView background rebuild). Without this, two threads can
    // both observe the GPU chain as uninitialized and each call D3D11CreateDevice,
    // leaking the loser's device.
    private readonly object _filterDeviceInitLock = new();

    private static bool IsDefaultD3D11DeviceChainEnabled()
        => ENABLE_DEFAULT_DEVICECHAIN;

    private void EnsureFilterDeviceContext()
    {
        if (_gpuDeviceState is GpuDeviceState.Ready or GpuDeviceState.Unavailable or GpuDeviceState.Disposed) return;

        lock (_filterDeviceInitLock)
        {
            if (_gpuDeviceState is GpuDeviceState.Ready or GpuDeviceState.Unavailable or GpuDeviceState.Disposed) return;

            if (_gpuDeviceState == GpuDeviceState.Lost)
            {
                _ = TryRecoverGpuDeviceChainCore();
                return;
            }

            _ = TryEnsureGpuDeviceChainCore();
        }
    }

    private bool TryEnsureGpuDeviceChain()
    {
        lock (_filterDeviceInitLock)
        {
            return TryEnsureGpuDeviceChainCore();
        }
    }

    private bool TryEnsureGpuDeviceChainCore()
    {
        EnsureInitialized();
        if (!_hasFactory1)
        {
            _gpuDeviceState = GpuDeviceState.Unavailable;
            return false;
        }

        if (!IsDefaultD3D11DeviceChainEnabled())
        {
            _gpuDeviceState = GpuDeviceState.Unavailable;
            return false;
        }

        if (TryCreateInternalD3D11Device())
        {
            _gpuDeviceState = GpuDeviceState.Ready;
            return true;
        }

        _gpuDeviceState = GpuDeviceState.Unavailable;
        return false;
    }

    private bool TryCreateInternalD3D11Device()
    {
        ResetGpuDeviceChain();

        int hr = D2D1.D3D11CreateDevice(
            pAdapter: 0,
            driverType: (uint)D3D_DRIVER_TYPE.HARDWARE,
            software: 0,
            flags: (uint)(D3D11_CREATE_DEVICE_FLAG.BGRA_SUPPORT | D3D11_CREATE_DEVICE_FLAG.VIDEO_SUPPORT),
            pFeatureLevels: 0,
            featureLevels: 0,
            sdkVersion: 7,
            ppDevice: out _d3dDevice,
            pFeatureLevel: out _,
            ppImmediateContext: out var d3dCtx);
        if (hr < 0 || _d3dDevice == 0)
        {
            hr = D2D1.D3D11CreateDevice(0, (uint)D3D_DRIVER_TYPE.WARP, 0,
                (uint)(D3D11_CREATE_DEVICE_FLAG.BGRA_SUPPORT | D3D11_CREATE_DEVICE_FLAG.VIDEO_SUPPORT), 0, 0, 7,
                out _d3dDevice, out _, out d3dCtx);
            if (hr < 0 || _d3dDevice == 0)
            {
                return false;
            }
        }

        if (d3dCtx != 0)
        {
            TryEnableD3D11MultithreadProtection(d3dCtx);
        }
        if (d3dCtx != 0) ComHelpers.Release(d3dCtx);

        if (TryCreateD2DDeviceChainFromD3D11Device(_d3dDevice))
        {
            return true;
        }

        ResetGpuDeviceChain();
        return false;
    }

    private bool TryCreateD2DDeviceChainFromD3D11Device(nint d3d11Device)
    {
        if (d3d11Device == 0)
        {
            return false;
        }

        int hr;

        if (ComHelpers.QueryInterface(d3d11Device, D2D1.IID_IDXGIDevice, out _dxgiDevice) < 0 || _dxgiDevice == 0)
        {
            return false;
        }

        hr = D2D1VTable.CreateDevice((ID2D1Factory*)_d2dFactory, _dxgiDevice, out _d2dDevice);
        if (hr < 0 || _d2dDevice == 0)
        {
            return false;
        }

        hr = D2D1VTable.CreateDeviceContext(_d2dDevice, options: 0, out _filterDeviceContext);
        if (hr < 0 || _filterDeviceContext == 0)
        {
            _filterDeviceContext = 0;
            return false;
        }

        return true;
    }

    internal bool NotifyGpuDeviceLost(int hr)
    {
        if (!IsRecoverableGpuDeviceChainFailure(hr))
        {
            return false;
        }

        bool shouldInvalidateFactoryTargets = false;

        lock (_filterDeviceInitLock)
        {
            if (_gpuDeviceState == GpuDeviceState.Disposed)
            {
                return false;
            }

            InvalidateTrackedGpuBitmapTargets();
            _gpuDeviceState = GpuDeviceState.Lost;
            shouldInvalidateFactoryTargets = true;
        }

        if (shouldInvalidateFactoryTargets)
        {
            InvalidateAllFactoryRenderTargetsForDeviceLost();
        }

        return true;
    }

    internal bool TryRecoverGpuDeviceChain()
    {
        lock (_filterDeviceInitLock)
        {
            return TryRecoverGpuDeviceChainCore();
        }
    }

    private bool TryRecoverGpuDeviceChainCore()
    {
        if (_gpuDeviceState == GpuDeviceState.Disposed)
        {
            return false;
        }

        ResetGpuDeviceChain();
        return TryEnsureGpuDeviceChainCore();
    }

    internal static bool IsRecoverableGpuDeviceChainFailure(int hr)
        => hr == D2DERR_RECREATE_TARGET
        || hr == D2DERR_WRONG_RESOURCE_DOMAIN
        || hr == DXGI_ERROR_DEVICE_REMOVED
        || hr == DXGI_ERROR_DEVICE_HUNG
        || hr == DXGI_ERROR_DEVICE_RESET;

    internal void RegisterGpuBitmapTarget(Direct2DGpuPixelRenderSurface target)
    {
        lock (_filterDeviceInitLock)
        {
            for (int i = _trackedGpuBitmapTargets.Count - 1; i >= 0; i--)
            {
                if (!_trackedGpuBitmapTargets[i].TryGetTarget(out _))
                {
                    _trackedGpuBitmapTargets.RemoveAt(i);
                }
            }

            _trackedGpuBitmapTargets.Add(new WeakReference<Direct2DGpuPixelRenderSurface>(target));
        }
    }

    private void InvalidateTrackedGpuBitmapTargets()
    {
        for (int i = _trackedGpuBitmapTargets.Count - 1; i >= 0; i--)
        {
            if (_trackedGpuBitmapTargets[i].TryGetTarget(out var target))
            {
                target.NotifyDeviceLost();
                continue;
            }

            _trackedGpuBitmapTargets.RemoveAt(i);
        }
    }

    private void ResetGpuDeviceChain()
    {
        if (_filterDeviceContext != 0) { ComHelpers.Release(_filterDeviceContext); _filterDeviceContext = 0; }
        if (_d2dDevice != 0) { ComHelpers.Release(_d2dDevice); _d2dDevice = 0; }
        if (_dxgiDevice != 0) { ComHelpers.Release(_dxgiDevice); _dxgiDevice = 0; }
        if (_d3dDevice != 0) { ComHelpers.Release(_d3dDevice); _d3dDevice = 0; }

        if (_gpuDeviceState != GpuDeviceState.Disposed)
        {
            _gpuDeviceState = GpuDeviceState.Uninitialized;
        }
    }

    private static void TryEnableD3D11MultithreadProtection(nint d3dDeviceContext)
    {
        if (d3dDeviceContext == 0)
        {
            return;
        }

        Guid iid = new("9B7E4E00-342C-4106-A19F-4F2704F689F0");
        if (ComHelpers.QueryInterface(d3dDeviceContext, iid, out var multithread) < 0 || multithread == 0)
        {
            return;
        }

        try
        {
            var vtbl = *(nint**)multithread;
            var setMultithreadProtected = (delegate* unmanaged[Stdcall]<nint, int, int>)vtbl[5];
            _ = setMultithreadProtected(multithread, 1);
        }
        finally
        {
            ComHelpers.Release(multithread);
        }
    }
}