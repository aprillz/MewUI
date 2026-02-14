using System.Runtime.InteropServices;

using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Platform.Win32;

internal sealed class Win32ClipboardService : IClipboardService
{
    public bool TrySetText(string text)
    {
        if (!User32.OpenClipboard(0))
        {
            return false;
        }

        try
        {
            User32.EmptyClipboard();

            var bytes = System.Text.Encoding.Unicode.GetBytes((text ?? string.Empty) + "\0");
            var hGlobal = Kernel32.GlobalAlloc(0x0042, (nuint)bytes.Length); // GMEM_MOVEABLE | GMEM_ZEROINIT
            if (hGlobal == 0)
            {
                return false;
            }

            var ptr = Kernel32.GlobalLock(hGlobal);
            if (ptr == 0)
            {
                return false;
            }

            try
            {
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
            }
            finally
            {
                Kernel32.GlobalUnlock(hGlobal);
            }

            // CF_UNICODETEXT = 13
            User32.SetClipboardData(13, hGlobal);
            return true;
        }
        finally
        {
            User32.CloseClipboard();
        }
    }

    public bool TryGetText(out string text)
    {
        text = string.Empty;

        // CF_UNICODETEXT = 13
        if (!User32.IsClipboardFormatAvailable(13))
        {
            return false;
        }

        if (!User32.OpenClipboard(0))
        {
            return false;
        }

        try
        {
            var hGlobal = User32.GetClipboardData(13);
            if (hGlobal == 0)
            {
                return false;
            }

            var ptr = Kernel32.GlobalLock(hGlobal);
            if (ptr == 0)
            {
                return false;
            }

            try
            {
                text = Marshal.PtrToStringUni(ptr) ?? string.Empty;
                return true;
            }
            finally
            {
                Kernel32.GlobalUnlock(hGlobal);
            }
        }
        finally
        {
            User32.CloseClipboard();
        }
    }
}

