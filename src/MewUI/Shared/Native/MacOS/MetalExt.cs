namespace Aprillz.MewUI.Native;

/// <summary>
/// Metal enumeration values used by the macOS rendering paths. Declared once here so a call
/// site cannot silently pick a neighbouring value: MTLPrimitiveType.triangle and
/// .triangleStrip differ by one, and a strip drawn as a list loses every second triangle.
/// </summary>
internal static class MetalExt
{
    // MTLPixelFormat
    public const ulong MTLPixelFormatBGRA8Unorm = 80;

    // MTLLoadAction / MTLStoreAction
    public const ulong MTLLoadActionDontCare = 0;
    public const ulong MTLLoadActionLoad = 1;
    public const ulong MTLLoadActionClear = 2;
    public const ulong MTLStoreActionDontCare = 0;
    public const ulong MTLStoreActionStore = 1;

    // MTLPrimitiveType
    public const ulong MTLPrimitiveTypePoint = 0;
    public const ulong MTLPrimitiveTypeLine = 1;
    public const ulong MTLPrimitiveTypeLineStrip = 2;
    public const ulong MTLPrimitiveTypeTriangle = 3;
    public const ulong MTLPrimitiveTypeTriangleStrip = 4;

    // MTLBlendFactor
    public const ulong MTLBlendFactorZero = 0;
    public const ulong MTLBlendFactorOne = 1;
    public const ulong MTLBlendFactorSourceAlpha = 4;
    public const ulong MTLBlendFactorOneMinusSourceAlpha = 5;
}
