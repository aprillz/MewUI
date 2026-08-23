using System.Runtime.InteropServices;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// BGRA pixels of a window's client area as they reach the screen. Read through the screen DC
/// rather than the window, so the capture reflects the real pixel format and present path rather
/// than a backend-side readback.
/// </summary>
public sealed class ScreenCapture
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Bgra { get; }

    private ScreenCapture(int width, int height, byte[] bgra)
    {
        Width = width;
        Height = height;
        Bgra = bgra;
    }

    public (byte B, byte G, byte R, byte A) At(int x, int y)
    {
        int offset = (y * Width + x) * 4;
        return (Bgra[offset], Bgra[offset + 1], Bgra[offset + 2], Bgra[offset + 3]);
    }

    /// <summary>
    /// Captures the client area of <paramref name="hwnd"/> in device pixels, through DWM's composed
    /// image of the window. A plain screen-DC BitBlt sees only the window's GDI surface on some
    /// drivers, where a GL or DX present never lands, and reports the erased background instead.
    /// </summary>
    public static ScreenCapture OfClientArea(nint hwnd)
    {
        GetWindowRect(hwnd, out var window);
        GetClientRect(hwnd, out var client);
        var clientOrigin = new POINT { X = 0, Y = 0 };
        ClientToScreen(hwnd, ref clientOrigin);
        int windowWidth = window.Right - window.Left;
        int windowHeight = window.Bottom - window.Top;
        int width = client.Right - client.Left;
        int height = client.Bottom - client.Top;

        nint screen = GetDC(0);
        nint memory = CreateCompatibleDC(screen);
        nint bitmap = CreateCompatibleBitmap(screen, windowWidth, windowHeight);
        nint previous = SelectObject(memory, bitmap);
        try
        {
            const uint PW_RENDERFULLCONTENT = 0x00000002;
            PrintWindow(hwnd, memory, PW_RENDERFULLCONTENT);

            var info = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = windowWidth,
                biHeight = -windowHeight,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            };
            var whole = new byte[windowWidth * windowHeight * 4];
            const uint DIB_RGB_COLORS = 0;
            GetDIBits(memory, bitmap, 0, (uint)windowHeight, whole, ref info, DIB_RGB_COLORS);

            // The client area sits inside the window image at the client-to-window offset.
            int offsetX = clientOrigin.X - window.Left;
            int offsetY = clientOrigin.Y - window.Top;
            var pixels = new byte[width * height * 4];
            for (int row = 0; row < height; row++)
            {
                Buffer.BlockCopy(whole, ((offsetY + row) * windowWidth + offsetX) * 4, pixels, row * width * 4, width * 4);
            }

            return new ScreenCapture(width, height, pixels);
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    public static ScreenCapture OfScreen(int x, int y, int width, int height)
    {
        nint screen = GetDC(0);
        nint memory = CreateCompatibleDC(screen);
        nint bitmap = CreateCompatibleBitmap(screen, width, height);
        nint previous = SelectObject(memory, bitmap);
        try
        {
            const uint SRCCOPY = 0x00CC0020;
            BitBlt(memory, 0, 0, width, height, screen, x, y, SRCCOPY);

            var info = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                // Negative height: top-down rows, so the buffer reads like the screen.
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            };
            var pixels = new byte[width * height * 4];
            const uint DIB_RGB_COLORS = 0;
            GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, DIB_RGB_COLORS);
            return new ScreenCapture(width, height, pixels);
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint hwnd, nint hdc, uint flags);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref POINT point);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint hdc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint hdc, int width, int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint hdc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint dest, int x, int y, int width, int height, nint src, int srcX, int srcY, uint rop);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(nint hdc, nint bitmap, uint startScan, uint scanLines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint hdc);
}
