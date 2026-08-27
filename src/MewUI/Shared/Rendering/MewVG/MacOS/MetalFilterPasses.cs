using System.Runtime.InteropServices;

using Aprillz.MewVG.Interop;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// GPU passes for the non-blur filter nodes on <see cref="MewVGMetalPixelRenderSurface"/>: a
/// placed textured quad that can optionally run a 4x5 color matrix, drawn either opaquely or
/// source-over. The MSL library and both pipeline states are built once per device and reused.
/// </summary>
internal static unsafe partial class MetalFilterPasses
{
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void SendDrawPrimitives(nint receiver, nint selector, nuint primitiveType, nuint vertexStart, nuint vertexCount);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void SendSetBytes(nint receiver, nint selector, void* bytes, nuint length, nuint index);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void SendSetTexture(nint receiver, nint selector, nint texture, nuint index);

    private const ulong MTL_PIXEL_FORMAT_BGRA8_UNORM = 80;
    private const ulong MTL_LOAD_ACTION_CLEAR = 2;
    private const ulong MTL_STORE_ACTION_STORE = 1;
    // MTLPrimitiveType: triangle = 3, triangleStrip = 4. The strip constant carried the
    // list value, so the 4-vertex composite quad drew as one triangle and lost the other.
    private const ulong MTL_PRIMITIVE_TYPE_TRIANGLE_STRIP = 4;
    private const ulong MTL_BLEND_FACTOR_ONE = 1;
    private const ulong MTL_BLEND_FACTOR_ONE_MINUS_SOURCE_ALPHA = 5;

    // A texture's row 0 is its top row here (MewVGMetalPixelRenderSurface reports YFlipped =
    // false), so image-space offsets map straight onto sample coordinates and only the NDC
    // Y axis has to be inverted when the quad is placed.
    private const string SHADER_SOURCE = @"
#include <metal_stdlib>
using namespace metal;

struct VertexOut {
    float4 position [[position]];
    float2 uv;
};

struct ColorMatrixParams {
    float4 row0;
    float4 row1;
    float4 row2;
    float4 row3;
    float4 bias;
    int useMatrix;
};

vertex VertexOut mewui_filter_vertex(uint vid [[vertex_id]],
                                     constant float4& rect [[buffer(0)]]) {
    float2 uv = float2(float(vid & 1u), float(vid >> 1u));
    VertexOut out;
    out.uv = uv;
    out.position = float4(mix(rect.x, rect.z, uv.x), mix(rect.y, rect.w, uv.y), 0.0, 1.0);
    return out;
}

fragment float4 mewui_filter_fragment(VertexOut in [[stage_in]],
                                      texture2d<float> tex [[texture(0)]],
                                      constant ColorMatrixParams& params [[buffer(0)]]) {
    constexpr sampler texSampler(filter::linear, address::clamp_to_edge);
    float4 c = tex.sample(texSampler, in.uv);
    if (params.useMatrix == 0) {
        return c;
    }
    float4 s = c.a > 0.0 ? float4(c.rgb / c.a, c.a) : float4(0.0);
    float4 r = float4(dot(params.row0, s), dot(params.row1, s), dot(params.row2, s), dot(params.row3, s)) + params.bias;
    r = clamp(r, 0.0, 1.0);
    return float4(r.rgb * r.a, r.a);
}
";

    [StructLayout(LayoutKind.Sequential)]
    private struct ColorMatrixParams
    {
        public float Row0X, Row0Y, Row0Z, Row0W;
        public float Row1X, Row1Y, Row1Z, Row1W;
        public float Row2X, Row2Y, Row2Z, Row2W;
        public float Row3X, Row3Y, Row3Z, Row3W;
        public float BiasR, BiasG, BiasB, BiasA;
        public int UseMatrix;
        private readonly int _pad0, _pad1, _pad2;
    }

    /// <summary>One input of <see cref="TryEncodeComposite"/>, placed at a pixel offset from the
    /// destination's top-left.</summary>
    internal readonly struct CompositeLayer(MewVGMetalPixelRenderSurface surface, int offsetX, int offsetY)
    {
        public MewVGMetalPixelRenderSurface Surface { get; } = surface;
        public int OffsetX { get; } = offsetX;
        public int OffsetY { get; } = offsetY;
    }

    private static readonly object _lock = new();
    private static nint _pipelineDevice;
    private static nint _opaquePipeline;
    private static nint _blendPipeline;
    private static bool _initFailed;

    private static readonly nint _clsRenderPipelineDescriptor = ObjCRuntime.GetClass("MTLRenderPipelineDescriptor");
    private static readonly nint _clsRenderPassDescriptor = ObjCRuntime.GetClass("MTLRenderPassDescriptor");

    private static readonly nint _selNewLibraryWithSource = ObjCRuntime.RegisterSelector("newLibraryWithSource:options:error:");
    private static readonly nint _selNewFunctionWithName = ObjCRuntime.RegisterSelector("newFunctionWithName:");
    private static readonly nint _selNewRenderPipelineState = ObjCRuntime.RegisterSelector("newRenderPipelineStateWithDescriptor:error:");
    private static readonly nint _selSetVertexFunction = ObjCRuntime.RegisterSelector("setVertexFunction:");
    private static readonly nint _selSetFragmentFunction = ObjCRuntime.RegisterSelector("setFragmentFunction:");
    private static readonly nint _selColorAttachments = ObjCRuntime.RegisterSelector("colorAttachments");
    private static readonly nint _selObjectAtIndexedSubscript = ObjCRuntime.RegisterSelector("objectAtIndexedSubscript:");
    private static readonly nint _selSetPixelFormat = ObjCRuntime.RegisterSelector("setPixelFormat:");
    private static readonly nint _selSetBlendingEnabled = ObjCRuntime.RegisterSelector("setBlendingEnabled:");
    private static readonly nint _selSetSourceRgbBlendFactor = ObjCRuntime.RegisterSelector("setSourceRGBBlendFactor:");
    private static readonly nint _selSetDestinationRgbBlendFactor = ObjCRuntime.RegisterSelector("setDestinationRGBBlendFactor:");
    private static readonly nint _selSetSourceAlphaBlendFactor = ObjCRuntime.RegisterSelector("setSourceAlphaBlendFactor:");
    private static readonly nint _selSetDestinationAlphaBlendFactor = ObjCRuntime.RegisterSelector("setDestinationAlphaBlendFactor:");
    private static readonly nint _selRenderPassDescriptor = ObjCRuntime.RegisterSelector("renderPassDescriptor");
    private static readonly nint _selSetTexture = ObjCRuntime.RegisterSelector("setTexture:");
    private static readonly nint _selSetLoadAction = ObjCRuntime.RegisterSelector("setLoadAction:");
    private static readonly nint _selSetStoreAction = ObjCRuntime.RegisterSelector("setStoreAction:");
    private static readonly nint _selSetClearColor = ObjCRuntime.RegisterSelector("setClearColor:");
    private static readonly nint _selRenderCommandEncoder = ObjCRuntime.RegisterSelector("renderCommandEncoderWithDescriptor:");
    private static readonly nint _selSetRenderPipelineState = ObjCRuntime.RegisterSelector("setRenderPipelineState:");
    private static readonly nint _selSetVertexBytes = ObjCRuntime.RegisterSelector("setVertexBytes:length:atIndex:");
    private static readonly nint _selSetFragmentBytes = ObjCRuntime.RegisterSelector("setFragmentBytes:length:atIndex:");
    private static readonly nint _selSetFragmentTexture = ObjCRuntime.RegisterSelector("setFragmentTexture:atIndex:");
    private static readonly nint _selDrawPrimitives = ObjCRuntime.RegisterSelector("drawPrimitives:vertexStart:vertexCount:");
    private static readonly nint _selEndEncoding = ObjCRuntime.RegisterSelector("endEncoding");

    /// <summary>Copies <paramref name="source"/> into <paramref name="dest"/> while applying a
    /// row-major 4x5 color matrix. Encodes into <paramref name="commandBuffer"/>; the caller
    /// commits and waits.</summary>
    public static bool TryEncodeColorMatrix(nint device, nint commandBuffer,
        MewVGMetalPixelRenderSurface source, MewVGMetalPixelRenderSurface dest, float[] matrix)
    {
        if (matrix.Length != 20)
        {
            return false;
        }

        var layers = new[] { new CompositeLayer(source, 0, 0) };
        return TryEncode(device, commandBuffer, dest, layers, blend: false, matrix);
    }

    /// <summary>Copies <paramref name="source"/> into <paramref name="dest"/> unchanged.</summary>
    public static bool TryEncodeCopy(nint device, nint commandBuffer,
        MewVGMetalPixelRenderSurface source, MewVGMetalPixelRenderSurface dest)
    {
        var layers = new[] { new CompositeLayer(source, 0, 0) };
        return TryEncode(device, commandBuffer, dest, layers, blend: false, matrix: null);
    }

    /// <summary>Source-over composites <paramref name="layers"/> into <paramref name="dest"/>,
    /// the first layer at the bottom. Each layer is placed at its own pixel offset so inputs an
    /// offset node moved stay aligned.</summary>
    public static bool TryEncodeComposite(nint device, nint commandBuffer,
        MewVGMetalPixelRenderSurface dest, IReadOnlyList<CompositeLayer> layers)
        => TryEncode(device, commandBuffer, dest, layers, blend: true, matrix: null);

    private static bool TryEncode(nint device, nint commandBuffer, MewVGMetalPixelRenderSurface dest,
        IReadOnlyList<CompositeLayer> layers, bool blend, float[]? matrix)
    {
        if (device == 0 || commandBuffer == 0 || dest.ColorTexture == 0 || layers.Count == 0)
        {
            return false;
        }

        foreach (var layer in layers)
        {
            if (layer.Surface.ColorTexture == 0)
            {
                return false;
            }
        }

        if (!EnsurePipelines(device))
        {
            return false;
        }

        using var pool = new AutoreleasePool();

        nint passDescriptor = ObjCRuntime.SendMessage(_clsRenderPassDescriptor, _selRenderPassDescriptor);
        if (passDescriptor == 0) return false;

        nint attachment = ObjCRuntime.SendMessage(
            ObjCRuntime.SendMessage(passDescriptor, _selColorAttachments), _selObjectAtIndexedSubscript, 0UL);
        if (attachment == 0) return false;

        ObjCRuntime.SendMessage(attachment, _selSetTexture, dest.ColorTexture);
        ObjCRuntime.SendMessageNoReturn(attachment, _selSetLoadAction, MTL_LOAD_ACTION_CLEAR);
        ObjCRuntime.SendMessageNoReturn(attachment, _selSetStoreAction, MTL_STORE_ACTION_STORE);
        ObjCRuntime.SendMessageNoReturn(attachment, _selSetClearColor, new MTLClearColor(0, 0, 0, 0));

        nint encoder = ObjCRuntime.SendMessage(commandBuffer, _selRenderCommandEncoder, passDescriptor);
        if (encoder == 0) return false;

        ObjCRuntime.SendMessage(encoder, _selSetRenderPipelineState, blend ? _blendPipeline : _opaquePipeline);

        var parameters = BuildParams(matrix);
        SendSetBytes(encoder, _selSetFragmentBytes, &parameters, (nuint)sizeof(ColorMatrixParams), 0);

        foreach (var layer in layers)
        {
            var rect = BuildRect(layer, dest.PixelWidth, dest.PixelHeight);
            SendSetBytes(encoder, _selSetVertexBytes, &rect, (nuint)sizeof(QuadRect), 0);
            SendSetTexture(encoder, _selSetFragmentTexture, layer.Surface.ColorTexture, 0);
            SendDrawPrimitives(encoder, _selDrawPrimitives, (nuint)MTL_PRIMITIVE_TYPE_TRIANGLE_STRIP, 0, 4);
        }

        ObjCRuntime.SendMessageNoReturn(encoder, _selEndEncoding);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QuadRect
    {
        public float X0, Y0, X1, Y1;
    }

    private static QuadRect BuildRect(CompositeLayer layer, int destWidth, int destHeight)
    {
        int width = layer.Surface.PixelWidth;
        int height = layer.Surface.PixelHeight;
        return new QuadRect
        {
            X0 = (layer.OffsetX / (float)destWidth * 2f) - 1f,
            X1 = ((layer.OffsetX + width) / (float)destWidth * 2f) - 1f,
            // uv.y = 0 is the source's top row, which belongs at the top of the placed
            // rectangle, and NDC Y grows upward.
            Y0 = 1f - (layer.OffsetY / (float)destHeight * 2f),
            Y1 = 1f - ((layer.OffsetY + height) / (float)destHeight * 2f),
        };
    }

    private static ColorMatrixParams BuildParams(float[]? matrix)
    {
        if (matrix is null)
        {
            return new ColorMatrixParams { UseMatrix = 0 };
        }

        return new ColorMatrixParams
        {
            Row0X = matrix[0], Row0Y = matrix[1], Row0Z = matrix[2], Row0W = matrix[3],
            Row1X = matrix[5], Row1Y = matrix[6], Row1Z = matrix[7], Row1W = matrix[8],
            Row2X = matrix[10], Row2Y = matrix[11], Row2Z = matrix[12], Row2W = matrix[13],
            Row3X = matrix[15], Row3Y = matrix[16], Row3Z = matrix[17], Row3W = matrix[18],
            BiasR = matrix[4], BiasG = matrix[9], BiasB = matrix[14], BiasA = matrix[19],
            UseMatrix = 1,
        };
    }

    private static bool EnsurePipelines(nint device)
    {
        if (_opaquePipeline != 0 && _blendPipeline != 0 && _pipelineDevice == device)
        {
            return true;
        }

        lock (_lock)
        {
            if (_opaquePipeline != 0 && _blendPipeline != 0 && _pipelineDevice == device)
            {
                return true;
            }
            if (_initFailed && _pipelineDevice == device)
            {
                return false;
            }

            using var pool = new AutoreleasePool();
            _pipelineDevice = device;
            _initFailed = true;

            nint source = ObjCRuntime.CreateNSString(SHADER_SOURCE);
            nint error = 0;
            nint library = ObjCRuntime.SendMessage(device, _selNewLibraryWithSource, source, 0, (nint)(&error));
            ObjCRuntime.Release(source);
            if (library == 0)
            {
                return false;
            }

            nint vertexFunction = GetFunction(library, "mewui_filter_vertex");
            nint fragmentFunction = GetFunction(library, "mewui_filter_fragment");
            if (vertexFunction == 0 || fragmentFunction == 0)
            {
                ObjCRuntime.Release(library);
                return false;
            }

            nint opaque = CreatePipeline(device, vertexFunction, fragmentFunction, blend: false);
            nint blended = CreatePipeline(device, vertexFunction, fragmentFunction, blend: true);
            ObjCRuntime.Release(vertexFunction);
            ObjCRuntime.Release(fragmentFunction);
            ObjCRuntime.Release(library);

            if (opaque == 0 || blended == 0)
            {
                if (opaque != 0) ObjCRuntime.Release(opaque);
                if (blended != 0) ObjCRuntime.Release(blended);
                return false;
            }

            _opaquePipeline = opaque;
            _blendPipeline = blended;
            _initFailed = false;
            return true;
        }
    }

    private static nint GetFunction(nint library, string name)
    {
        nint nsName = ObjCRuntime.CreateNSString(name);
        try
        {
            return ObjCRuntime.SendMessage(library, _selNewFunctionWithName, nsName);
        }
        finally
        {
            ObjCRuntime.Release(nsName);
        }
    }

    private static nint CreatePipeline(nint device, nint vertexFunction, nint fragmentFunction, bool blend)
    {
        nint descriptor = ObjCRuntime.New(_clsRenderPipelineDescriptor);
        if (descriptor == 0) return 0;

        try
        {
            ObjCRuntime.SendMessage(descriptor, _selSetVertexFunction, vertexFunction);
            ObjCRuntime.SendMessage(descriptor, _selSetFragmentFunction, fragmentFunction);

            nint attachment = ObjCRuntime.SendMessage(
                ObjCRuntime.SendMessage(descriptor, _selColorAttachments), _selObjectAtIndexedSubscript, 0UL);
            if (attachment == 0) return 0;

            ObjCRuntime.SendMessageNoReturn(attachment, _selSetPixelFormat, MTL_PIXEL_FORMAT_BGRA8_UNORM);
            if (blend)
            {
                // Inputs are premultiplied, so source-over is ONE / ONE_MINUS_SOURCE_ALPHA.
                ObjCRuntime.SendMessage(attachment, _selSetBlendingEnabled, true);
                ObjCRuntime.SendMessageNoReturn(attachment, _selSetSourceRgbBlendFactor, MTL_BLEND_FACTOR_ONE);
                ObjCRuntime.SendMessageNoReturn(attachment, _selSetDestinationRgbBlendFactor, MTL_BLEND_FACTOR_ONE_MINUS_SOURCE_ALPHA);
                ObjCRuntime.SendMessageNoReturn(attachment, _selSetSourceAlphaBlendFactor, MTL_BLEND_FACTOR_ONE);
                ObjCRuntime.SendMessageNoReturn(attachment, _selSetDestinationAlphaBlendFactor, MTL_BLEND_FACTOR_ONE_MINUS_SOURCE_ALPHA);
            }

            nint error = 0;
            return ObjCRuntime.SendMessage(device, _selNewRenderPipelineState, descriptor, (nint)(&error));
        }
        finally
        {
            ObjCRuntime.Release(descriptor);
        }
    }
}
