using System.Runtime.InteropServices;

using Aprillz.MewUI.Rendering.Filters;
using Aprillz.MewVG.Interop;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// GPU-accelerated executor for filter graphs running on the Metal backend. Mirrors
/// <c>OpenGLImageFilterExecutor</c>: walks the graph, dispatches nodes the executor knows how
/// to run on the GPU, and delegates the rest to a fallback (CPU by default).
/// </summary>
/// <remarks>
/// <para>
/// Currently handles <see cref="BlurFilter"/> via <see cref="MetalGaussianBlur"/> (Apple's
/// MPS framework). ColorMatrix / Composite / Merge / Offset / DropShadow still fall back to
/// the CPU executor pending dedicated MPS / shader implementations - adding one is the same
/// shape as <see cref="TryGpuBlur"/>: recurse on the input, verify it's Metal-backed, acquire
/// a Metal scratch, encode the pass.
/// </para>
/// <para>
/// The executor reaches into <see cref="FilterResult.UnderlyingSurface"/> to obtain the
/// backend's <see cref="MewVGMetalPixelRenderSurface"/> - when either input or scratch isn't
/// Metal-backed (e.g. a <see cref="FloodFilter"/> result built by the CPU executor), the GPU
/// path bails for that node. Cross-backend handoff goes through <see cref="FilterResult.ReadPixels"/>.
/// </para>
/// </remarks>
public sealed unsafe partial class MetalImageFilterExecutor : IImageFilterExecutor
{
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint SendMsg(nint receiver, nint selector);

    private static readonly nint _selCommandBuffer = ObjCRuntime.RegisterSelector("commandBuffer");
    private static readonly nint _selCommit = ObjCRuntime.RegisterSelector("commit");

    private readonly IImageFilterExecutor _fallback;
    private readonly MewVGMetalOffscreenSurfaceProvider _offscreenProvider;

    internal MetalImageFilterExecutor(MewVGMetalOffscreenSurfaceProvider offscreenProvider,
        IImageFilterExecutor? fallback = null)
    {
        _offscreenProvider = offscreenProvider ?? throw new ArgumentNullException(nameof(offscreenProvider));
        _fallback = fallback ?? new CpuImageFilterExecutor();
    }

    public FilterResult Execute(ImageFilter filter, IImageFilterContext context)
    {
        switch (filter)
        {
            case SourceFilter:
                return context.Source;
            case BlurFilter b:
            {
                var gpuResult = TryGpuBlur(b, context);
                return gpuResult ?? _fallback.Execute(filter, context);
            }
            case ColorMatrixFilter cm:
            {
                var gpuResult = TryGpuColorMatrix(cm, context);
                return gpuResult ?? _fallback.Execute(filter, context);
            }
            case OffsetFilter o:
            {
                var gpuResult = TryGpuOffset(o, context);
                return gpuResult ?? _fallback.Execute(filter, context);
            }
            case MergeFilter m:
            {
                var gpuResult = TryGpuMerge(m, context);
                return gpuResult ?? _fallback.Execute(filter, context);
            }
            // Flood / Compose / Composite / DropShadow: GPU shaders not yet shipped - fall back
            // to CPU. Adding a GPU path here is the same shape as TryGpuBlur: recurse on the
            // input, verify it's a Metal-backed target, acquire a Metal scratch, run the pass.
            default:
                return _fallback.Execute(filter, context);
        }
    }

    /// <summary>
    /// True when <paramref name="result"/>'s underlying target is a Metal-backed pixel surface
    /// whose color texture is realized - required precondition before sampling on the GPU.
    /// </summary>
    internal static bool LooksLikeMetalSource(FilterResult result)
        => result.UnderlyingSurface is MewVGMetalPixelRenderSurface metal
           && metal.ColorTexture != 0;

    private FilterResult? TryGpuColorMatrix(ColorMatrixFilter cm, IImageFilterContext ctx)
    {
        FilterResult input = cm.Input is null ? ctx.Source : Execute(cm.Input, ctx);
        ScratchFilterResult? scratch = null;
        bool ownsResult = false;
        try
        {
            if (input.UnderlyingSurface is not MewVGMetalPixelRenderSurface metalSource) return null;
            if (metalSource.ColorTexture == 0) return null;

            scratch = ctx.AcquireScratch(input.PixelWidth, input.PixelHeight, input.Bounds);
            if (!TryPrepareDestination(scratch, out nint device, out nint queue, out var metalDest)) return null;

            ownsResult = Submit(queue, metalDest, commandBuffer =>
                MetalFilterPasses.TryEncodeColorMatrix(device, commandBuffer, metalSource, metalDest, cm.Matrix));
            return ownsResult ? scratch : null;
        }
        finally
        {
            if (!ownsResult)
            {
                scratch?.Dispose();
            }
            if (!ReferenceEquals(input, ctx.Source))
            {
                input.Dispose();
            }
        }
    }

    private FilterResult? TryGpuOffset(OffsetFilter o, IImageFilterContext ctx)
    {
        FilterResult input = o.Input is null ? ctx.Source : Execute(o.Input, ctx);
        ScratchFilterResult? scratch = null;
        bool ownsResult = false;
        try
        {
            if (input.UnderlyingSurface is not MewVGMetalPixelRenderSurface metalSource) return null;
            if (metalSource.ColorTexture == 0) return null;

            // Dx/Dy are in logical/DIP units; the node only moves the result, so the pixels are
            // copied unchanged and the translation lands in the bounds the consumer places by.
            var bounds = new Rect(
                input.Bounds.X + (o.Dx * ctx.LogicalToPixelScaleX),
                input.Bounds.Y + (o.Dy * ctx.LogicalToPixelScaleY),
                input.Bounds.Width,
                input.Bounds.Height);

            scratch = ctx.AcquireScratch(input.PixelWidth, input.PixelHeight, bounds);
            if (!TryPrepareDestination(scratch, out nint device, out nint queue, out var metalDest)) return null;

            ownsResult = Submit(queue, metalDest, commandBuffer =>
                MetalFilterPasses.TryEncodeCopy(device, commandBuffer, metalSource, metalDest));
            return ownsResult ? scratch : null;
        }
        finally
        {
            if (!ownsResult)
            {
                scratch?.Dispose();
            }
            if (!ReferenceEquals(input, ctx.Source))
            {
                input.Dispose();
            }
        }
    }

    private FilterResult? TryGpuMerge(MergeFilter m, IImageFilterContext ctx)
    {
        if (m.InputList.Count == 0)
        {
            // Empty merge produces a transparent layer at source bounds; leave that to the CPU.
            return null;
        }

        var inputs = new List<FilterResult>(m.InputList.Count);
        ScratchFilterResult? scratch = null;
        bool ownsResult = false;
        try
        {
            foreach (var node in m.InputList)
            {
                inputs.Add(Execute(node, ctx));
            }

            // Composite over the union so inputs an offset node moved stay spatially aligned,
            // matching the CPU executor's Porter-Duff path.
            var bounds = inputs[0].Bounds;
            for (int i = 1; i < inputs.Count; i++)
            {
                bounds = Union(bounds, inputs[i].Bounds);
            }

            var layers = new List<MetalFilterPasses.CompositeLayer>(inputs.Count);
            foreach (var result in inputs)
            {
                if (result.UnderlyingSurface is not MewVGMetalPixelRenderSurface metalLayer) return null;
                if (metalLayer.ColorTexture == 0) return null;
                layers.Add(new MetalFilterPasses.CompositeLayer(
                    metalLayer,
                    (int)Math.Round(result.Bounds.X - bounds.X),
                    (int)Math.Round(result.Bounds.Y - bounds.Y)));
            }

            int width = Math.Max(1, (int)Math.Ceiling(bounds.Width));
            int height = Math.Max(1, (int)Math.Ceiling(bounds.Height));
            scratch = ctx.AcquireScratch(width, height, bounds);
            if (!TryPrepareDestination(scratch, out nint device, out nint queue, out var metalDest)) return null;

            ownsResult = Submit(queue, metalDest, commandBuffer =>
                MetalFilterPasses.TryEncodeComposite(device, commandBuffer, metalDest, layers));
            return ownsResult ? scratch : null;
        }
        finally
        {
            if (!ownsResult)
            {
                scratch?.Dispose();
            }
            foreach (var result in inputs)
            {
                if (!ReferenceEquals(result, ctx.Source))
                {
                    result.Dispose();
                }
            }
        }
    }

    private static Rect Union(Rect first, Rect second)
    {
        double left = Math.Min(first.X, second.X);
        double top = Math.Min(first.Y, second.Y);
        double right = Math.Max(first.Right, second.Right);
        double bottom = Math.Max(first.Bottom, second.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>Resolves the device and filter queue and realizes the scratch target's texture.
    /// The pool hands back render targets whose MTLTexture has not been created yet.</summary>
    private bool TryPrepareDestination(ScratchFilterResult scratch, out nint device, out nint queue,
        out MewVGMetalPixelRenderSurface dest)
    {
        device = 0;
        queue = 0;
        dest = null!;
        if (scratch.UnderlyingSurface is not MewVGMetalPixelRenderSurface metalDest) return false;

        device = _offscreenProvider.TryGetDefaultDevice();
        if (device == 0) return false;

        queue = _offscreenProvider.TryGetFilterCommandQueue();
        if (queue == 0) return false;

        metalDest.EnsureGpuTextures(device, queue);
        if (metalDest.ColorTexture == 0) return false;

        dest = metalDest;
        return true;
    }

    /// <summary>Runs <paramref name="encode"/> on a one-shot command buffer and commits it without
    /// waiting: the passes that consume this result - later filter nodes and the offscreen NVG frame
    /// that draws the graph's output - submit to the same queue, which executes in commit order. A
    /// CPU consumer still gets correct pixels through the deferred readback recorded here.</summary>
    private static bool Submit(nint queue, MewVGMetalPixelRenderSurface dest, Func<nint, bool> encode)
    {
        nint commandBuffer = SendMsg(queue, _selCommandBuffer);
        if (commandBuffer == 0) return false;
        ObjCRuntime.Retain(commandBuffer);
        try
        {
            if (!encode(commandBuffer))
            {
                return false;
            }

            ObjCRuntime.SendMessageNoReturn(commandBuffer, _selCommit);
            dest.RequestDeferredReadback(commandBuffer);
            return true;
        }
        finally
        {
            ObjCRuntime.Release(commandBuffer);
        }
    }

    private FilterResult? TryGpuBlur(BlurFilter b, IImageFilterContext ctx)
    {
        // Radius is in logical/DIP units; convert to a pixel sigma (radius / 3, then by the
        // context's input-to-pixel scale) before handing the value to MPS.
        double pxSigmaX = BlurKernel.RadiusToSigma(b.RadiusX) * ctx.LogicalToPixelScaleX;
        double pxSigmaY = BlurKernel.RadiusToSigma(b.RadiusY) * ctx.LogicalToPixelScaleY;
        if (pxSigmaX <= 0 && pxSigmaY <= 0)
        {
            return b.Input is null ? ctx.Source : Execute(b.Input, ctx);
        }

        FilterResult input = b.Input is null ? ctx.Source : Execute(b.Input, ctx);
        ScratchFilterResult? scratch = null;
        bool ownsResult = false;
        try
        {
            if (input.UnderlyingSurface is not MewVGMetalPixelRenderSurface metalSource) return null;
            if (metalSource.ColorTexture == 0) return null;

            nint device = _offscreenProvider.TryGetDefaultDevice();
            if (device == 0) return null;

            nint queue = _offscreenProvider.TryGetFilterCommandQueue();
            if (queue == 0) return null;

            scratch = ctx.AcquireScratch(input.PixelWidth, input.PixelHeight, input.Bounds);
            if (scratch.UnderlyingSurface is not MewVGMetalPixelRenderSurface metalDest) return null;

            // Lazy GPU-texture init - pool gives back a fresh RT whose MTLTexture hasn't been
            // created yet (no offscreen frame has run on it). MPS needs the destination
            // texture realised before encoding.
            metalDest.EnsureGpuTextures(device, queue);
            if (metalDest.ColorTexture == 0) return null;

            // Build a one-shot command buffer for this blur pass. MPS encodes both the
            // horizontal and vertical separable passes inside its kernel; the host only
            // sees a single encode call.
            nint commandBuffer = SendMsg(queue, _selCommandBuffer);
            if (commandBuffer == 0) return null;
            ObjCRuntime.Retain(commandBuffer);

            try
            {
                if (!MetalGaussianBlur.TryEncode(device, commandBuffer,
                    metalSource.ColorTexture, metalDest.ColorTexture, pxSigmaX, pxSigmaY))
                {
                    return null;
                }

                ObjCRuntime.SendMessageNoReturn(commandBuffer, _selCommit);

                // No completion wait: MPS and the offscreen NVG pass that samples
                // metalDest.ColorTexture now submit to the same queue (the provider's shared
                // offscreen/filter queue), and Metal executes a queue's command buffers in
                // commit order. Sampling before the write lands is what a separate filter
                // queue used to allow.

                // Defer the MTLTexture → CPU readback (the much heavier 32 MB getBytes).
                // CPU consumers (FilterResult.ReadPixels, CPU MergeFilter) trigger it
                // transparently via metalDest.GetPixelSpan / Lock / CopyPixels.
                metalDest.RequestDeferredReadback(commandBuffer);
            }
            finally
            {
                ObjCRuntime.Release(commandBuffer);
            }

            ownsResult = true;
            return scratch;
        }
        finally
        {
            if (!ownsResult)
            {
                scratch?.Dispose();
            }
            if (!ReferenceEquals(input, ctx.Source))
            {
                input.Dispose();
            }
        }
    }

}
