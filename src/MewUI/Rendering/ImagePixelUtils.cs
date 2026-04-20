namespace Aprillz.MewUI.Rendering;

internal static class ImagePixelUtils
{
    public static void ConvertBgraToRgba(byte[] source, byte[] destination)
    {
        for (int i = 0; i < source.Length; i += 4)
        {
            destination[i] = source[i + 2];
            destination[i + 1] = source[i + 1];
            destination[i + 2] = source[i];
            destination[i + 3] = source[i + 3];
        }
    }

    public static void ConvertRgbaToBgra(byte[] source, byte[] destination)
    {
        // Same byte swap as BgraToRgba
        ConvertBgraToRgba(source, destination);
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
