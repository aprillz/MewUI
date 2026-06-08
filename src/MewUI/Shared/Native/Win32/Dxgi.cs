using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Aprillz.MewUI.Native.Structs;

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
    public static int EnumAdapters(nint factory, uint adapterIndex, out nint adapter)
    {
        nint localAdapter = 0;
        var vtbl = *(nint**)factory;
        var fn = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)vtbl[7];
        int hr = fn(factory, adapterIndex, &localAdapter);
        adapter = localAdapter;
        return hr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int EnumOutputs(nint adapter, uint outputIndex, out nint output)
    {
        nint localOutput = 0;
        var vtbl = *(nint**)adapter;
        var fn = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)vtbl[7];
        int hr = fn(adapter, outputIndex, &localOutput);
        output = localOutput;
        return hr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetAdapterDesc(nint adapter, out DXGI_ADAPTER_DESC desc)
    {
        desc = default;
        fixed (DXGI_ADAPTER_DESC* pDesc = &desc)
        {
            var vtbl = *(nint**)adapter;
            var fn = (delegate* unmanaged[Stdcall]<nint, DXGI_ADAPTER_DESC*, int>)vtbl[8];
            return fn(adapter, pDesc);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetAdapterFromDevice(nint dxgiDevice, out nint adapter)
    {
        nint localAdapter = 0;
        var vtbl = *(nint**)dxgiDevice;
        var fn = (delegate* unmanaged[Stdcall]<nint, nint*, int>)vtbl[7];
        int hr = fn(dxgiDevice, &localAdapter);
        adapter = localAdapter;
        return hr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetOutputDesc(nint output, out DXGI_OUTPUT_DESC desc)
    {
        desc = default;
        fixed (DXGI_OUTPUT_DESC* pDesc = &desc)
        {
            var vtbl = *(nint**)output;
            var fn = (delegate* unmanaged[Stdcall]<nint, DXGI_OUTPUT_DESC*, int>)vtbl[7];
            return fn(output, pDesc);
        }
    }

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
    public static int ResizeBuffers(nint swapChain, uint bufferCount, uint width, uint height, uint newFormat, uint flags)
    {
        var vtbl = *(nint**)swapChain;
        var fn = (delegate* unmanaged[Stdcall]<nint, uint, uint, uint, uint, uint, int>)vtbl[13];
        return fn(swapChain, bufferCount, width, height, newFormat, flags);
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

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct DXGI_OUTPUT_DESC
{
    public fixed char DeviceName[32];
    public RECT DesktopCoordinates;
    public int AttachedToDesktop;
    public DXGI_MODE_ROTATION Rotation;
    public nint Monitor;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct DXGI_ADAPTER_DESC
{
    public fixed char Description[128];
    public uint VendorId;
    public uint DeviceId;
    public uint SubSysId;
    public uint Revision;
    public nuint DedicatedVideoMemory;
    public nuint DedicatedSystemMemory;
    public nuint SharedSystemMemory;
    public LUID AdapterLuid;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct LUID(uint lowPart, int highPart) : IEquatable<LUID>
{
    public readonly uint LowPart = lowPart;
    public readonly int HighPart = highPart;

    public bool Equals(LUID other) => LowPart == other.LowPart && HighPart == other.HighPart;

    public override bool Equals(object? obj) => obj is LUID other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(LowPart, HighPart);
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

internal enum DXGI_MODE_ROTATION : uint
{
    UNSPECIFIED = 0,
    IDENTITY = 1,
    ROTATE90 = 2,
    ROTATE180 = 3,
    ROTATE270 = 4,
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
