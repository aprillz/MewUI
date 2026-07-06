using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

/// <summary>
/// Minimal P/Invoke + vtable wrappers for DirectComposition. Used only to bind a DXGI
/// swap-chain (created via <c>CreateSwapChainForComposition</c>) to a
/// <c>WS_EX_NOREDIRECTIONBITMAP</c> HWND so DWM composes the swap-chain output with
/// per-pixel alpha.
/// </summary>
internal static unsafe partial class Dcomp
{
    internal static readonly Guid IID_IDCompositionDevice = new("c37ea93a-e7aa-450d-b16f-9746cb0407f3");
    internal static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    // DirectComposition (dcomp.dll) is Windows 8+. On Win7 the DLL is absent, so callers must
    // gate on this before touching the composition swap-chain path and fall back to the layered
    // (UpdateLayeredWindow) transparency mode. Probed once; the P/Invokes never get a chance to
    // throw DllNotFoundException when this is false.
    internal static readonly bool IsAvailable = CheckAvailable();

    private static bool CheckAvailable()
    {
        try
        {
            return NativeLibrary.TryLoad("dcomp.dll", out nint handle)
                && NativeLibrary.TryGetExport(handle, "DCompositionCreateDevice", out _);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a DirectComposition device against a Direct3D device exposed through DXGI.
    /// The first parameter must be an <c>IDXGIDevice</c> - callers QueryInterface from
    /// their <c>ID3D11Device</c> first.
    /// </summary>
    [LibraryImport("dcomp.dll")]
    internal static partial int DCompositionCreateDevice(
        nint dxgiDevice,
        in Guid iid,
        out nint dcompositionDevice);

    // IDCompositionDevice (vtbl indices, IUnknown 0-2):
    //   3 Commit
    //   6 CreateTargetForHwnd(HWND, BOOL topmost, IDCompositionTarget**)
    //   7 CreateVisual(IDCompositionVisual**)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Commit(nint device)
    {
        var vtbl = *(nint**)device;
        var fn = (delegate* unmanaged[Stdcall]<nint, int>)vtbl[3];
        return fn(device);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateTargetForHwnd(nint device, nint hwnd, bool topmost, out nint target)
    {
        nint local = 0;
        var vtbl = *(nint**)device;
        var fn = (delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)vtbl[6];
        int hr = fn(device, hwnd, topmost ? 1 : 0, &local);
        target = local;
        return hr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CreateVisual(nint device, out nint visual)
    {
        nint local = 0;
        var vtbl = *(nint**)device;
        var fn = (delegate* unmanaged[Stdcall]<nint, nint*, int>)vtbl[7];
        int hr = fn(device, &local);
        visual = local;
        return hr;
    }

    // IDCompositionTarget (IUnknown 0-2):
    //   3 SetRoot(IDCompositionVisual*)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SetRoot(nint target, nint visual)
    {
        var vtbl = *(nint**)target;
        var fn = (delegate* unmanaged[Stdcall]<nint, nint, int>)vtbl[3];
        return fn(target, visual);
    }

    // IDCompositionVisual (IUnknown 0-2). SetContent is at vtbl[15] - order:
    //   3  SetOffsetX(float)
    //   4  SetOffsetX(IDCompositionAnimation*)
    //   5  SetOffsetY(float)
    //   6  SetOffsetY(IDCompositionAnimation*)
    //   7  SetTransform(D2D_MATRIX_3X2_F&)
    //   8  SetTransform(IDCompositionTransform*)
    //   9  SetTransformParent
    //   10 SetEffect
    //   11 SetBitmapInterpolationMode
    //   12 SetBorderMode
    //   13 SetClip(D2D_RECT_F&)
    //   14 SetClip(IDCompositionClip*)
    //   15 SetContent(IUnknown*)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SetContent(nint visual, nint content)
    {
        var vtbl = *(nint**)visual;
        var fn = (delegate* unmanaged[Stdcall]<nint, nint, int>)vtbl[15];
        return fn(visual, content);
    }
}
