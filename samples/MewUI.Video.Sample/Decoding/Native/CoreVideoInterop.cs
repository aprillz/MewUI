using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Video.Sample.Decoding;

/// <summary>
/// P/Invoke surface for the CoreVideo / IOSurface / Metal interop calls used by the
/// VideoToolbox zero-copy display path. CoreVideo's <c>CVMetalTextureCache</c> wraps
/// IOSurface-backed CVPixelBuffers as <c>MTLTexture</c> handles without a CPU copy —
/// the GPU samples directly from the decoder's output surface.
/// </summary>
/// <remarks>
/// All entry points return CoreFoundation / CoreVideo refcounted pointers. Lifetime
/// rules: <c>...Create...</c> APIs return retained refs the caller must release with
/// <see cref="CFRelease"/>. <c>CVMetalTextureGetTexture</c> returns a borrowed
/// <c>MTLTexture*</c> whose lifetime is tied to the parent <c>CVMetalTextureRef</c>.
/// </remarks>
internal static partial class CoreVideoInterop
{
    /// <summary>
    /// NV12 video range (BT.601/709 limited [16,235]). FFmpeg's VideoToolbox decoder
    /// emits this by default for SDR H.264/HEVC content.
    /// </summary>
    public const uint kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange = 0x34323076;

    /// <summary>NV12 full range (BT.601/709 full [0,255]).</summary>
    public const uint kCVPixelFormatType_420YpCbCr8BiPlanarFullRange = 0x34323066;

    /// <summary>kCFStringEncodingUTF8 = 0x08000100.</summary>
    public const uint kCFStringEncodingUTF8 = 0x08000100;

    /// <summary>kCFNumberIntType = 9 (CoreFoundation/CFNumber.h).</summary>
    public const nint kCFNumberIntType = 9;

    /// <summary>32-bit BGRA pixel format, four-char-code 'BGRA' = 0x42475241.</summary>
    public const uint kCVPixelFormatType_32BGRA = 0x42475241;

    public static nint CFBooleanTrue;
    public static nint CFTypeDictionaryKeyCallBacks;
    public static nint CFTypeDictionaryValueCallBacks;

    private const string CoreVideo = "/System/Library/Frameworks/CoreVideo.framework/CoreVideo";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string Metal = "/System/Library/Frameworks/Metal.framework/Metal";
    private const string VideoToolbox = "/System/Library/Frameworks/VideoToolbox.framework/VideoToolbox";

    // '420v'

    // '420f'

    private static bool _globalsLoaded;

    [LibraryImport(VideoToolbox)]
    public static partial int VTPixelTransferSessionCreate(nint allocator, out nint session);

    [LibraryImport(VideoToolbox)]
    public static partial int VTPixelTransferSessionTransferImage(nint session, nint sourceBuffer, nint destinationBuffer);

    [LibraryImport(CoreVideo)]
    public static partial int CVPixelBufferCreate(
        nint allocator,
        nuint width,
        nuint height,
        uint pixelFormatType,
        nint pixelBufferAttributes,
        out nint pixelBufferOut);

    /// <summary>
    /// Generic CFDictionary creation for the small attribute dicts we hand to
    /// CVPixelBufferCreate. Exposed via two-key helper below — keeping the raw
    /// CF API surface minimal.
    /// </summary>
    [LibraryImport(CoreFoundation)]
    public static partial nint CFDictionaryCreate(
        nint allocator,
        nint[] keys,
        nint[] values,
        nint numValues,
        nint keyCallBacks,
        nint valueCallBacks);

    [LibraryImport(CoreFoundation)]
    public static partial nint CFDictionaryCreateMutable(nint allocator, nint capacity, nint keyCallBacks, nint valueCallBacks);

    [LibraryImport(CoreFoundation)]
    public static partial void CFDictionarySetValue(nint dict, nint key, nint value);

    [LibraryImport(CoreFoundation, EntryPoint = "CFStringCreateWithCString", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint CFStringCreateWithCString(nint allocator, string cStr, uint encoding);

    /// <summary>
    /// Loads global CoreFoundation symbols we need but can't import as functions:
    /// <c>kCFBooleanTrue</c> (the True singleton, dereferenced because it's a
    /// <c>const CFBooleanRef *</c>) and the type-aware dictionary callbacks structs
    /// <c>kCFTypeDictionaryKeyCallBacks</c> / <c>kCFTypeDictionaryValueCallBacks</c>
    /// (passed by address to CFDictionaryCreate so the resulting dict actually retains
    /// the keys/values it stores — passing NULL there silently creates a non-retaining
    /// dict whose values dangle the moment we release them, which CV reads back as
    /// nonsense and produces buffers that fail Metal-compat checks later with -6660).
    /// </summary>
    public static void EnsureGlobalsLoaded()
    {
        if (_globalsLoaded) return;

        nint lib = NativeLibrary.Load(CoreFoundation);

        nint booleanTrueSymbol = NativeLibrary.GetExport(lib, "kCFBooleanTrue");
        unsafe
        {
            CFBooleanTrue = booleanTrueSymbol == 0 ? 0 : *(nint*)booleanTrueSymbol;
        }

        // These are struct-typed exports — we want the address of the struct, not its
        // dereferenced contents. Pass the address straight to CFDictionaryCreate.
        CFTypeDictionaryKeyCallBacks = NativeLibrary.GetExport(lib, "kCFTypeDictionaryKeyCallBacks");
        CFTypeDictionaryValueCallBacks = NativeLibrary.GetExport(lib, "kCFTypeDictionaryValueCallBacks");

        // We intentionally don't NativeLibrary.Free — the addresses we just captured
        // must stay valid for the life of the process, and CoreFoundation is always
        // resident in any macOS app anyway.

        _globalsLoaded = true;
    }

    /// <summary>
    /// CFNumberCreate. Used to encode the Boolean / Int values CV attribute dictionaries
    /// expect (e.g. kCVPixelBufferMetalCompatibilityKey = 1). CV accepts either CFBoolean
    /// or non-zero CFNumber for boolean-typed attributes — CFNumber is easier to construct
    /// without loading the global kCFBooleanTrue symbol.
    /// </summary>
    [LibraryImport(CoreFoundation)]
    public static partial nint CFNumberCreate(nint allocator, nint theType, in int valuePtr);

    /// <summary>
    /// Returns the system-default Metal device. CoreFoundation refcounted — the device is
    /// shared across the process; identical handle on every call (so safe to compare with
    /// the backend's device for sanity checks). Caller releases via <see cref="CFRelease"/>.
    /// </summary>
    [LibraryImport(Metal, EntryPoint = "MTLCreateSystemDefaultDevice")]
    public static partial nint MTLCreateSystemDefaultDevice();

    [LibraryImport(CoreVideo)]
    public static partial int CVMetalTextureCacheCreate(
        nint allocator,                    // CFAllocatorRef (NULL = default)
        nint cacheAttributes,              // CFDictionaryRef (NULL = default)
        nint metalDevice,                  // id<MTLDevice>
        nint textureAttributes,            // CFDictionaryRef (NULL = default)
        out nint cacheOut);                // CVMetalTextureCacheRef*

    [LibraryImport(CoreVideo)]
    public static partial int CVMetalTextureCacheCreateTextureFromImage(
        nint allocator,
        nint textureCache,                 // CVMetalTextureCacheRef
        nint sourceImage,                  // CVImageBufferRef (CVPixelBufferRef alias)
        nint textureAttributes,            // CFDictionaryRef (NULL = default)
        nuint pixelFormat,                 // MTLPixelFormat
        nuint width,
        nuint height,
        nuint planeIndex,
        out nint textureOut);              // CVMetalTextureRef*

    [LibraryImport(CoreVideo)]
    public static partial nint CVMetalTextureGetTexture(nint metalTexture);

    [LibraryImport(CoreVideo)]
    public static partial void CVMetalTextureCacheFlush(nint cache, ulong options);

    [LibraryImport(CoreVideo)]
    public static partial nint CVPixelBufferRetain(nint pixelBuffer);

    [LibraryImport(CoreVideo)]
    public static partial void CVPixelBufferRelease(nint pixelBuffer);

    [LibraryImport(CoreVideo)]
    public static partial nuint CVPixelBufferGetWidth(nint pixelBuffer);

    [LibraryImport(CoreVideo)]
    public static partial nuint CVPixelBufferGetHeight(nint pixelBuffer);

    [LibraryImport(CoreVideo)]
    public static partial uint CVPixelBufferGetPixelFormatType(nint pixelBuffer);

    [LibraryImport(CoreFoundation)]
    public static partial void CFRelease(nint cf);

    /// <summary>
    /// MTLPixelFormat values needed by the video display path. Keeping the subset here
    /// avoids dragging in an enum from MewVG's interop assembly (which already defines
    /// the full set internally).
    /// </summary>
    public static class MTLPixelFormat
    {
        public const nuint BGRA8Unorm = 80;
    }
}
