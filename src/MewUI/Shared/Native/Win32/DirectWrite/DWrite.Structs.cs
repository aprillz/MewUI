using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.DirectWrite;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct DWRITE_TEXT_METRICS(
    float left,
    float top,
    float width,
    float widthIncludingTrailingWhitespace,
    float height,
    float layoutWidth,
    float layoutHeight,
    uint maxBidiReorderingDepth,
    uint lineCount)
{
    public readonly float left = left;
    public readonly float top = top;
    public readonly float width = width;
    public readonly float widthIncludingTrailingWhitespace = widthIncludingTrailingWhitespace;
    public readonly float height = height;
    public readonly float layoutWidth = layoutWidth;
    public readonly float layoutHeight = layoutHeight;
    public readonly uint maxBidiReorderingDepth = maxBidiReorderingDepth;
    public readonly uint lineCount = lineCount;
}
