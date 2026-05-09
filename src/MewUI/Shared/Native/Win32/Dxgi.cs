using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

internal static unsafe partial class Dxgi
{
    internal static readonly Guid IID_IDXGIFactory2 = new("50c83a1c-e072-4c48-87b0-3630fa36a6d0");

    public const uint DXGI_USAGE_RENDER_TARGET_OUTPUT = 0x00000020;
    public const uint DXGI_MWA_NO_ALT_ENTER = 0x00000002;

    [LibraryImport("dxgi.dll")]
    internal static partial int CreateDXGIFactory2(
        uint flags,
        in Guid riid,
        out nint ppFactory);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int MakeWindowAssociation(nint factory, nint hwnd, uint flags)
    {
        var vtbl = *(nint**)factory;
        var fn = (delegate* unmanaged[Stdcall]<nint, nint, uint, int>)vtbl[8];
        return fn(factory, hwnd, flags);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateSwapChainForHwnd(
        nint factory2,
        nint device,
        nint hwnd,
        in DXGI_SWAP_CHAIN_DESC1 desc,
        out nint swapChain)
    {
        nint localSwapChain = 0;
        fixed (DXGI_SWAP_CHAIN_DESC1* pDesc = &desc)
        {
            var vtbl = *(nint**)factory2;
            var fn = (delegate* unmanaged[Stdcall]<nint, nint, nint, DXGI_SWAP_CHAIN_DESC1*, nint, nint, nint*, int>)vtbl[15];
            int hr = fn(factory2, device, hwnd, pDesc, 0, 0, &localSwapChain);
            swapChain = localSwapChain;
            return hr;
        }
    }

    /// <summary>
    /// IDXGIFactory2::CreateSwapChainForComposition. Used for HWND-less swap-chains that
    /// will be attached to a DirectComposition visual (the only way to render into a
    /// <c>WS_EX_NOREDIRECTIONBITMAP</c> window with per-pixel alpha).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateSwapChainForComposition(
        nint factory2,
        nint device,
        in DXGI_SWAP_CHAIN_DESC1 desc,
        out nint swapChain)
    {
        nint localSwapChain = 0;
        fixed (DXGI_SWAP_CHAIN_DESC1* pDesc = &desc)
        {
            var vtbl = *(nint**)factory2;
            // IDXGIFactory2 vtbl[24] — CreateSwapChainForComposition.
            var fn = (delegate* unmanaged[Stdcall]<nint, nint, DXGI_SWAP_CHAIN_DESC1*, nint, nint*, int>)vtbl[24];
            int hr = fn(factory2, device, pDesc, 0, &localSwapChain);
            swapChain = localSwapChain;
            return hr;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Present(nint swapChain, uint syncInterval, uint flags)
    {
        var vtbl = *(nint**)swapChain;
        var fn = (delegate* unmanaged[Stdcall]<nint, uint, uint, int>)vtbl[8];
        return fn(swapChain, syncInterval, flags);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetBuffer(nint swapChain, uint index, in Guid riid, out nint surface)
    {
        nint localSurface = 0;
        fixed (Guid* pIid = &riid)
        {
            var vtbl = *(nint**)swapChain;
            var fn = (delegate* unmanaged[Stdcall]<nint, uint, Guid*, nint*, int>)vtbl[9];
            int hr = fn(swapChain, index, pIid, &localSurface);
            surface = localSurface;
            return hr;
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct DXGI_SAMPLE_DESC(uint count, uint quality)
{
    public readonly uint Count = count;
    public readonly uint Quality = quality;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct DXGI_SWAP_CHAIN_DESC1(
    uint width,
    uint height,
    uint format,
    int stereo,
    DXGI_SAMPLE_DESC sampleDesc,
    uint bufferUsage,
    uint bufferCount,
    DXGI_SCALING scaling,
    DXGI_SWAP_EFFECT swapEffect,
    DXGI_ALPHA_MODE alphaMode,
    uint flags)
{
    public readonly uint Width = width;
    public readonly uint Height = height;
    public readonly uint Format = format;
    public readonly int Stereo = stereo;
    public readonly DXGI_SAMPLE_DESC SampleDesc = sampleDesc;
    public readonly uint BufferUsage = bufferUsage;
    public readonly uint BufferCount = bufferCount;
    public readonly DXGI_SCALING Scaling = scaling;
    public readonly DXGI_SWAP_EFFECT SwapEffect = swapEffect;
    public readonly DXGI_ALPHA_MODE AlphaMode = alphaMode;
    public readonly uint Flags = flags;
}

internal enum DXGI_SCALING : uint
{
    STRETCH = 0,
}

internal enum DXGI_SWAP_EFFECT : uint
{
    DISCARD = 0,
    SEQUENTIAL = 1,
    FLIP_SEQUENTIAL = 3,
    FLIP_DISCARD = 4,
}

internal enum DXGI_ALPHA_MODE : uint
{
    UNSPECIFIED = 0,
    PREMULTIPLIED = 1,
    STRAIGHT = 2,
    IGNORE = 3,
}