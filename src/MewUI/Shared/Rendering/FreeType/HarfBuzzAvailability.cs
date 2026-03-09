using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Rendering.FreeType;

internal static class HarfBuzzAvailability
{
    private static int _probed; // 0=not probed, 1=available, -1=unavailable

    public static bool IsAvailable
    {
        get
        {
            if (_probed == 0)
            {
                Probe();
            }

            return _probed == 1;
        }
    }

    private static void Probe()
    {
        bool ok = NativeLibrary.TryLoad("libharfbuzz.so.0", out var handle);
        if (!ok)
        {
            ok = NativeLibrary.TryLoad("libharfbuzz.so", out handle);
        }

        if (ok && handle != 0)
        {
            NativeLibrary.Free(handle);
        }

        _probed = ok ? 1 : -1;
    }
}
