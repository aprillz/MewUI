namespace Aprillz.MewUI.Rendering.OpenGL;

internal static class ImagePixelUtils
{
    public static byte[] ConvertBgraToRgba(byte[] bgra)
    {
        var rgba = new byte[bgra.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            rgba[i] = bgra[i + 2];
            rgba[i + 1] = bgra[i + 1];
            rgba[i + 2] = bgra[i];
            rgba[i + 3] = bgra[i + 3];
        }
        return rgba;
    }

    public static byte[] ConvertRgbaToBgra(byte[] rgba)
    {
        var bgra = new byte[rgba.Length];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            bgra[i] = rgba[i + 2];
            bgra[i + 1] = rgba[i + 1];
            bgra[i + 2] = rgba[i];
            bgra[i + 3] = rgba[i + 3];
        }
        return bgra;
    }

    public static void ConvertRgbaToBgraInPlace(Span<byte> rgba)
    {
        for (int i = 0; i < rgba.Length; i += 4)
        {
            byte r = rgba[i];
            byte b = rgba[i + 2];
            rgba[i] = b;
            rgba[i + 2] = r;
        }
    }
}
