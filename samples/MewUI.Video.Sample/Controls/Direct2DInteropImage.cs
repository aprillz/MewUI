using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Video.Sample.Controls;

internal static unsafe class Direct2DInteropImage
{
    private static readonly Guid IID_IDXGISurface = new("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");

    public static IImage? TryCreate(Direct2DGraphicsFactory factory, nint d3d11Texture, int pixelWidth, int pixelHeight, out string? failureReason)
    {
        failureReason = null;

        if (!OperatingSystem.IsWindows() || d3d11Texture == 0)
        {
            failureReason = $"precondition failed (isWindows={OperatingSystem.IsWindows()}, texture=0x{d3d11Texture:X})";
            return null;
        }

        nint dxgiSurface = 0;

        try
        {
            int querySurfaceHr = Marshal.QueryInterface(d3d11Texture, in IID_IDXGISurface, out dxgiSurface);
            if (querySurfaceHr < 0 || dxgiSurface == 0)
            {
                failureReason = $"IDXGISurface QI failed: hr=0x{querySurfaceHr:X8}, surface=0x{dxgiSurface:X}";
                return null;
            }

            return factory.CreateImageFromDxgiSurface(dxgiSurface, pixelWidth, pixelHeight, BitmapAlphaMode.Ignore);
        }
        finally
        {
            Release(dxgiSurface);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Release(nint unknown)
    {
        if (unknown == 0)
        {
            return 0;
        }

        var vtbl = *(void***)unknown;
        var release = (delegate* unmanaged[Stdcall]<nint, uint>)vtbl[2];
        return release(unknown);
    }
}