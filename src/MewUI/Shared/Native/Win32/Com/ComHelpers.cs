using System.Runtime.CompilerServices;

namespace Aprillz.MewUI.Native.Com;

internal static unsafe class ComHelpers
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Release(nint ptr)
    {
        if (ptr == 0)
        {
            return 0;
        }

        var vtbl = *(nint**)ptr;
        var release = (delegate* unmanaged[Stdcall]<nint, uint>)vtbl[2];
        return release(ptr);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint AddRef(nint ptr)
    {
        if (ptr == 0)
        {
            return 0;
        }

        var vtbl = *(nint**)ptr;
        var addRef = (delegate* unmanaged[Stdcall]<nint, uint>)vtbl[1];
        return addRef(ptr);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int QueryInterface(nint ptr, in Guid riid, out nint ppvObject)
    {
        nint result = 0;
        fixed (Guid* pIid = &riid)
        {
            var vtbl = *(nint**)ptr;
            var qi = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)vtbl[0];
            int hr = qi(ptr, pIid, &result);
            ppvObject = result;
            return hr;
        }
    }
}
