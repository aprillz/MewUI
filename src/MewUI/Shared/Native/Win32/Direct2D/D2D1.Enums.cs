namespace Aprillz.MewUI.Native.Direct2D;

internal enum D2D1_FACTORY_TYPE : uint
{
    SINGLE_THREADED = 0,
    MULTI_THREADED = 1
}

internal enum D2D1_RENDER_TARGET_TYPE : uint
{
    DEFAULT = 0,
    SOFTWARE = 1,
    HARDWARE = 2
}

internal enum D2D1_ALPHA_MODE : uint
{
    UNKNOWN = 0,
    PREMULTIPLIED = 1,
    STRAIGHT = 2,
    IGNORE = 3
}

internal enum D2D1_PRESENT_OPTIONS : uint
{
    NONE = 0x00000000,
    RETAIN_CONTENTS = 0x00000001,
    IMMEDIATELY = 0x00000002
}

internal enum D2D1_ANTIALIAS_MODE : uint
{
    PER_PRIMITIVE = 0,
    ALIASED = 1
}

internal enum D2D1_TEXT_ANTIALIAS_MODE : uint
{
    DEFAULT = 0,
    CLEARTYPE = 1,
    GRAYSCALE = 2,
    ALIASED = 3
}

internal enum D2D1_DRAW_TEXT_OPTIONS : uint
{
    NONE = 0,
    NO_SNAP = 0x00000001,
    CLIP = 0x00000002,
    ENABLE_COLOR_FONT = 0x00000004,
    DISABLE_COLOR_BITMAP_SNAPPING = 0x00000008
}

internal enum D2D1_BITMAP_INTERPOLATION_MODE : uint
{
    NEAREST_NEIGHBOR = 0,
    LINEAR = 1
}
