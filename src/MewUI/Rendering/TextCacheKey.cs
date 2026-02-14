namespace Aprillz.MewUI.Rendering;

internal readonly record struct TextCacheKey(
    int TextHash,
    nint FontHandle,
    string FontId,
    int FontSizePx,
    uint ColorArgb,
    int WidthPx,
    int HeightPx,
    int HAlign,
    int VAlign,
    int Wrapping
);
