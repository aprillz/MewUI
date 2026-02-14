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
}
