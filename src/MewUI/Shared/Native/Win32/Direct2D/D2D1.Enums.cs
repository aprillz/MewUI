namespace Aprillz.MewUI.Native.Direct2D;

internal enum D2D1_FACTORY_TYPE : uint
{
    SINGLE_THREADED = 0,
    MULTI_THREADED = 1
}

internal enum D3D_DRIVER_TYPE : uint
{
    UNKNOWN = 0,
    HARDWARE = 1,
    REFERENCE = 2,
    NULL = 3,
    SOFTWARE = 4,
    WARP = 5,
}

[Flags]
internal enum D3D11_CREATE_DEVICE_FLAG : uint
{
    NONE = 0,
    SINGLETHREADED = 0x1,
    DEBUG = 0x2,
    BGRA_SUPPORT = 0x20,  // Required for D2D interop
    VIDEO_SUPPORT = 0x800,
}

/// <summary>Property-type tag passed to <c>ID2D1Properties::SetValue</c>.</summary>
internal enum D2D1_PROPERTY_TYPE : uint
{
    UNKNOWN = 0,
    STRING = 1,
    BOOL = 2,
    UINT32 = 3,
    INT32 = 4,
    FLOAT = 5,
    VECTOR2 = 6,
    VECTOR3 = 7,
    VECTOR4 = 8,
    BLOB = 9,
    IUNKNOWN = 10,
    ENUM = 11,
    ARRAY = 12,
    CLSID = 13,
    MATRIX_3X2 = 14,
    MATRIX_4X3 = 15,
    MATRIX_4X4 = 16,
    MATRIX_5X4 = 17,
    COLOR_CONTEXT = 18,
}

/// <summary>Interpolation modes for <c>ID2D1DeviceContext::DrawImage</c>.</summary>
internal enum D2D1_INTERPOLATION_MODE : uint
{
    NEAREST_NEIGHBOR = 0,
    LINEAR = 1,
    CUBIC = 2,
    MULTI_SAMPLE_LINEAR = 3,
    ANISOTROPIC = 4,
    HIGH_QUALITY_CUBIC = 5,
}

/// <summary>Composite modes for <c>ID2D1DeviceContext::DrawImage</c>.</summary>
internal enum D2D1_COMPOSITE_MODE : uint
{
    SOURCE_OVER = 0,
    DESTINATION_OVER = 1,
    SOURCE_IN = 2,
    DESTINATION_IN = 3,
    SOURCE_OUT = 4,
    DESTINATION_OUT = 5,
    SOURCE_ATOP = 6,
    DESTINATION_ATOP = 7,
    XOR = 8,
    PLUS = 9,
    SOURCE_COPY = 10,
    BOUNDED_SOURCE_COPY = 11,
    MASK_INVERT = 12,
}

internal enum D2D1_BITMAP_OPTIONS : uint
{
    NONE = 0x00000000,
    TARGET = 0x00000001,
    CANNOT_DRAW = 0x00000002,
    CPU_READ = 0x00000004,
    GDI_COMPATIBLE = 0x00000008,
}

[Flags]
internal enum D2D1_MAP_OPTIONS : uint
{
    NONE = 0,
    READ = 1,
    WRITE = 2,
    DISCARD = 4,
}

internal enum D2D1_BORDER_MODE : uint
{
    SOFT = 0,
    HARD = 1,
}

internal enum D2D1_GAUSSIANBLUR_OPTIMIZATION : uint
{
    SPEED = 0,
    BALANCED = 1,
    QUALITY = 2,
}

/// <summary><c>D2D1_GAUSSIANBLUR_PROP</c> property indices for the built-in blur effect.</summary>
internal enum D2D1_GAUSSIANBLUR_PROP : uint
{
    STANDARD_DEVIATION = 0,
    OPTIMIZATION = 1,
    BORDER_MODE = 2,
}

/// <summary><c>D2D1_COLORMATRIX_PROP</c> indices for the built-in color matrix effect.</summary>
internal enum D2D1_COLORMATRIX_PROP : uint
{
    COLOR_MATRIX = 0,
    ALPHA_MODE = 1,
    CLAMP_OUTPUT = 2,
}

/// <summary><c>D2D1_COMPOSITE_PROP</c> indices for the built-in composite effect.</summary>
internal enum D2D1_COMPOSITE_PROP : uint
{
    MODE = 0,
}

/// <summary><c>D2D1_2DAFFINETRANSFORM_PROP</c> indices for the built-in 2D affine transform effect.</summary>
internal enum D2D1_2DAFFINETRANSFORM_PROP : uint
{
    INTERPOLATION_MODE = 0,
    BORDER_MODE = 1,
    TRANSFORM_MATRIX = 2,
    SHARPNESS = 3,
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

internal enum D2D1_LAYER_OPTIONS : uint
{
    NONE = 0,
    INITIALIZE_FOR_CLEARTYPE = 1
}

internal enum D2D1_LAYER_OPTIONS1 : uint
{
    NONE = 0,
    INITIALIZE_FROM_BACKGROUND = 1,
    IGNORE_ALPHA = 2
}

internal enum D2D1_FILL_MODE : uint
{
    ALTERNATE = 0,
    WINDING = 1,
}

internal enum D2D1_FIGURE_BEGIN : uint
{
    FILLED = 0,
    HOLLOW = 1,
}

internal enum D2D1_FIGURE_END : uint
{
    OPEN = 0,
    CLOSED = 1,
}

internal enum D2D1_EXTEND_MODE : uint
{
    CLAMP = 0,
    WRAP = 1,
    MIRROR = 2,
}

internal enum D2D1_GAMMA : uint
{
    GAMMA_2_2 = 0,
    GAMMA_1_0 = 1,
}

internal enum D2D1_CAP_STYLE : uint
{
    FLAT = 0,
    SQUARE = 1,
    ROUND = 2,
    TRIANGLE = 3,
}

internal enum D2D1_LINE_JOIN : uint
{
    MITER = 0,
    BEVEL = 1,
    ROUND = 2,
    MITER_OR_BEVEL = 3,
}

internal enum D2D1_DASH_STYLE : uint
{
    SOLID = 0,
    DASH = 1,
    DOT = 2,
    DASH_DOT = 3,
    DASH_DOT_DOT = 4,
    CUSTOM = 5,
}

internal enum D2D1_STROKE_TRANSFORM_TYPE : uint
{
    NORMAL = 0,
    FIXED = 1,
    HAIRLINE = 2,
}
