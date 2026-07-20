using System.Runtime.CompilerServices;

using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.Direct2D;

namespace Aprillz.MewUI.Rendering.Direct2D;

public sealed unsafe partial class Direct2DGraphicsFactory : ID3D11RenderTargetDeviceProvider
{
    private const int D2DERR_RECREATE_TARGET = unchecked((int)0x8899000C);
    private const int D2DERR_WRONG_RESOURCE_DOMAIN = unchecked((int)0x88990015);
    private const int DXGI_ERROR_DEVICE_REMOVED = unchecked((int)0x887A0005);
    private const int DXGI_ERROR_DEVICE_HUNG = unchecked((int)0x887A0006);
    private const int DXGI_ERROR_DEVICE_RESET = unchecked((int)0x887A0007);
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

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

    // GPU pipeline (D3D11 -> DXGI -> ID2D1Device) shared across every device context this
    // factory hands out. Lazy-initialized on first GPU bitmap creation; remains 0 if init
    // fails (e.g. headless WARP-less environment) and the factory falls back to DIB-backed
    // offscreen rendering.
    private nint _d3dDevice;
    private nint _dxgiDevice;
    private nint _d2dDevice;
    private GpuDeviceState _gpuDeviceState;
    private readonly List<WeakReference<Direct2DGpuPixelRenderSurface>> _trackedGpuPixelSurfaces = [];

    // Every ID2D1DeviceContext this factory has handed out via GetOrCreateCurrentThreadDeviceContext,
    // for release on device-lost/Dispose. Generation bump on reset invalidates any thread's
    // cached ThreadDeviceState without needing to reach into other threads' storage.
    private readonly List<nint> _threadDeviceContexts = [];
    private int _gpuDeviceGeneration;

    /// <summary>Per (factory instance, calling thread) device context and nesting state -
    /// keyed by factory (not a plain <c>[ThreadStatic]</c> field) so two factories on the
    /// same thread never share a context.</summary>
    private sealed class ThreadDeviceState
    {
        public nint Context;
        public int DeviceGeneration = -1;
        public int DrawDepth;
        public int BackgroundScopeDepth;
    }

    [ThreadStatic] private static ConditionalWeakTable<Direct2DGraphicsFactory, ThreadDeviceState>? _threadStateByFactory;

    private ThreadDeviceState GetThreadState()
    {
        var table = _threadStateByFactory ??= new ConditionalWeakTable<Direct2DGraphicsFactory, ThreadDeviceState>();
        return table.GetValue(this, static _ => new ThreadDeviceState());
    }

    /// <summary>Lazily creates (or reuses) the calling thread's own <c>ID2D1DeviceContext*</c>,
    /// from this factory's shared <c>ID2D1Device</c>. Every GPU-resident offscreen surface and
    /// filter operation draws on the calling thread's context - there is no separate "shared"
    /// context to contend over. Returns 0 if the GPU pipeline is unavailable; callers fall
    /// back to DIB-backed rendering.</summary>
    internal nint GetOrCreateCurrentThreadDeviceContext()
    {
        var state = GetThreadState();
        if (state.Context != 0 && state.DeviceGeneration == _gpuDeviceGeneration)
        {
            return state.Context;
        }

        EnsureGpuDeviceChain();

        lock (_gpuDeviceInitLock)
        {
            if (_d2dDevice == 0)
            {
                return 0;
            }

            int hr = D2D1VTable.CreateDeviceContext(_d2dDevice, options: 0, out nint dc);
            if (hr < 0 || dc == 0)
            {
                return 0;
            }

            _threadDeviceContexts.Add(dc);
            state.Context = dc;
            state.DeviceGeneration = _gpuDeviceGeneration;
            return dc;
        }
    }

    /// <summary>True when <paramref name="dc"/> is the calling thread's own device context -
    /// the invariant a GPU pixel surface's draw/readback must satisfy, since a surface's
    /// context is only ever current on the thread that created it.</summary>
    internal bool IsCurrentThreadDeviceContext(nint dc) => dc != 0 && dc == GetThreadState().Context;

    /// <summary>Nested-BeginDraw gate for the calling thread's device context - D2D rejects a
    /// second <c>BeginDraw</c> on the same context without an intervening <c>EndDraw</c>, but
    /// offscreen/filter passes naturally nest (an outer pass draws into its own GPU surface,
    /// then an inner filter pass retargets the same context via <c>SetTarget</c> without a
    /// fresh <c>BeginDraw</c>). Returns true only for the outermost <c>Enter</c>/matching
    /// <c>Exit</c>; only the outermost call should bracket the real <c>BeginDraw</c>/<c>EndDraw</c>.
    /// <paramref name="dc"/> must be the calling thread's own context
    /// (<see cref="IsCurrentThreadDeviceContext"/>) - thread-local state, so no
    /// <c>Interlocked</c> is needed.</summary>
    internal bool EnterCurrentThreadDcDraw(nint dc)
    {
        var state = GetThreadState();
        if (state.Context != dc)
        {
            throw new InvalidOperationException(
                "Direct2D device context draw must happen on the thread that created it.");
        }

        return ++state.DrawDepth == 1;
    }

    internal bool ExitCurrentThreadDcDraw(nint dc)
    {
        var state = GetThreadState();
        if (state.Context != dc)
        {
            throw new InvalidOperationException(
                "Direct2D device context draw must happen on the thread that created it.");
        }

        return --state.DrawDepth == 0;
    }

    /// <summary>See <see cref="IGraphicsFactory.AcquireBackgroundRenderScope"/>. For Direct2D
    /// this eagerly creates the calling thread's device context (avoiding a stall on the first
    /// offscreen surface inside the scope); it no longer picks between two different contexts -
    /// every thread's context is created the same way whether or not a scope is active.</summary>
    public IDisposable AcquireBackgroundRenderScope()
    {
        var state = GetThreadState();
        if (state.BackgroundScopeDepth > 0)
        {
            state.BackgroundScopeDepth++;
            return new BackgroundRenderScope(this);
        }

        if (GetOrCreateCurrentThreadDeviceContext() == 0)
        {
            return Direct2DNoOpRenderScope.Instance;
        }

        state.BackgroundScopeDepth = 1;
        return new BackgroundRenderScope(this);
    }

    private sealed class BackgroundRenderScope : IDisposable
    {
        private readonly Direct2DGraphicsFactory _factory;
        private bool _disposed;

        public BackgroundRenderScope(Direct2DGraphicsFactory factory) => _factory = factory;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var state = _factory.GetThreadState();
            if (state.BackgroundScopeDepth > 0)
            {
                state.BackgroundScopeDepth--;
            }
            // Device context stays cached for reuse by the next scope on this thread.
        }
    }

    private sealed class Direct2DNoOpRenderScope : IDisposable
    {
        public static readonly Direct2DNoOpRenderScope Instance = new();
        public void Dispose() { }
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
            EnsureGpuDeviceChain();
            return _d3dDevice;
        }
    }

    /// <summary>
    /// Returns an AddRef'ed <c>ID3D11Device*</c> compatible with the current cached target
    /// for <paramref name="renderTargetHandle"/>. The caller must release the returned
    /// pointer. If the target has not been created yet, this falls back to the factory device.
    /// </summary>
    public nint RetainD3D11DeviceForRenderTarget(nint renderTargetHandle)
    {
        EnsureGpuDeviceChain();

        nint device = 0;
        lock (_rtLock)
        {
            device = _d3dDevice;

            if (device != 0)
            {
                ComHelpers.AddRef(device);
            }
        }

        return device;
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
            EnsureGpuDeviceChain();
            return _d2dDevice;
        }
    }

    // Guards EnsureGpuDeviceChain against concurrent first-use from multiple threads
    // (UI thread first paint vs background offscreen rebuild). Without this, two threads can
    // both observe the GPU chain as uninitialized and each call D3D11CreateDevice,
    // leaking the loser's device.
    private readonly object _gpuDeviceInitLock = new();

    private static bool IsDefaultD3D11DeviceChainEnabled()
        => ENABLE_DEFAULT_DEVICECHAIN;

    private void EnsureGpuDeviceChain()
    {
        if (_gpuDeviceState is GpuDeviceState.Ready or GpuDeviceState.Unavailable or GpuDeviceState.Disposed) return;

        lock (_gpuDeviceInitLock)
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
        if (TryCreateD2DDeviceChainFromD3D11Device(d3d11Device, out var dxgiDevice, out var d2dDevice))
        {
            _dxgiDevice = dxgiDevice;
            _d2dDevice = d2dDevice;
            return true;
        }

        return false;
    }

    private bool TryCreateD2DDeviceChainFromD3D11Device(nint d3d11Device, out nint dxgiDevice, out nint d2dDevice)
    {
        dxgiDevice = 0;
        d2dDevice = 0;
        if (d3d11Device == 0)
        {
            return false;
        }

        if (ComHelpers.QueryInterface(d3d11Device, D2D1.IID_IDXGIDevice, out dxgiDevice) < 0 || dxgiDevice == 0)
        {
            return false;
        }

        int hr = D2D1VTable.CreateDevice((ID2D1Factory*)_d2dFactory, dxgiDevice, out d2dDevice);
        if (hr < 0 || d2dDevice == 0)
        {
            ComHelpers.Release(dxgiDevice);
            dxgiDevice = 0;
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

        lock (_gpuDeviceInitLock)
        {
            if (_gpuDeviceState == GpuDeviceState.Disposed)
            {
                return false;
            }

            InvalidateTrackedGpuPixelSurfaces();
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
        lock (_gpuDeviceInitLock)
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

    internal void RegisterGpuPixelSurface(Direct2DGpuPixelRenderSurface target)
    {
        lock (_gpuDeviceInitLock)
        {
            for (int i = _trackedGpuPixelSurfaces.Count - 1; i >= 0; i--)
            {
                if (!_trackedGpuPixelSurfaces[i].TryGetTarget(out _))
                {
                    _trackedGpuPixelSurfaces.RemoveAt(i);
                }
            }

            _trackedGpuPixelSurfaces.Add(new WeakReference<Direct2DGpuPixelRenderSurface>(target));
        }
    }

    private void InvalidateTrackedGpuPixelSurfaces()
    {
        for (int i = _trackedGpuPixelSurfaces.Count - 1; i >= 0; i--)
        {
            if (_trackedGpuPixelSurfaces[i].TryGetTarget(out var target))
            {
                target.NotifyDeviceLost();
                continue;
            }

            _trackedGpuPixelSurfaces.RemoveAt(i);
        }
    }

    private void ResetGpuDeviceChain()
    {
        // Release before the owning device goes away; generation bump invalidates thread caches.
        foreach (nint dc in _threadDeviceContexts)
        {
            if (dc != 0) ComHelpers.Release(dc);
        }
        _threadDeviceContexts.Clear();
        unchecked { _gpuDeviceGeneration++; }

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
