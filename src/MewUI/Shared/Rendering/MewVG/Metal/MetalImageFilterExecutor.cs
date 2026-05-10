using Debug = System.Diagnostics.Debug;
using ConditionalAttribute = System.Diagnostics.ConditionalAttribute;
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
/// the CPU executor pending dedicated MPS / shader implementations — adding one is the same
/// shape as <see cref="TryGpuBlur"/>: recurse on the input, verify it's Metal-backed, acquire
/// a Metal scratch, encode the pass.
/// </para>
/// <para>
/// The executor reaches into <see cref="FilterResult.UnderlyingSurface"/> to obtain the
/// backend's <see cref="MewVGMetalPixelRenderSurface"/> — when either input or scratch isn't
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
    private static readonly nint _selWaitUntilCompleted = ObjCRuntime.RegisterSelector("waitUntilCompleted");

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
            // ColorMatrix / Composite / Merge / Offset / DropShadow:
            // GPU shaders not yet shipped — fall back to CPU. Adding a GPU path here is
            // the same shape as TryGpuBlur: recurse on the input, verify it's a Metal-backed
            // target, acquire a Metal scratch, run the pass.
            default:
                return _fallback.Execute(filter, context);
        }
    }

    /// <summary>
    /// True when <paramref name="result"/>'s underlying target is a Metal-backed pixel surface
    /// whose color texture is realized — required precondition before sampling on the GPU.
    /// </summary>
    internal static bool LooksLikeMetalSource(FilterResult result)
        => result.UnderlyingSurface is MewVGMetalPixelRenderSurface metal
           && metal.ColorTexture != 0;

    private FilterResult? TryGpuBlur(BlurFilter b, IImageFilterContext ctx)
    {
        // Sigma is in logical/DIP units; the texture we sample is at the source layer's
        // pixel resolution, so convert via the context's input-to-pixel scale before
        // handing the value to MPS.
        double pxSigmaX = b.SigmaX * ctx.LogicalToPixelScaleX;
        double pxSigmaY = b.SigmaY * ctx.LogicalToPixelScaleY;
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

            // Lazy GPU-texture init — pool gives back a fresh RT whose MTLTexture hasn't been
            // created yet (no offscreen frame has run on it). MPS needs the destination
            // texture realised before encoding.
            metalDest.EnsureGpuTextures(device);
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

                // waitUntilCompleted is required here for correctness: MPS runs on the filter
                // command queue (offscreenProvider.TryGetFilterCommandQueue), but NVG's
                // offscreen pass that consumes metalDest.ColorTexture as a sample input
                // runs on the offscreen-surface queue — different queue. Metal only
                // guarantees ordering within a single queue; cross-queue access to the same
                // MTLTexture without explicit sync (waitUntilCompleted, MTLSharedEvent, etc.)
                // races. Without this wait, NVG samples the texture before MPS has finished
                // writing it → blank / partial / stale filter results. A future optimisation
                // could submit MPS on NVG's queue (or use MTLSharedEvent) to drop the CPU
                // stall while keeping correctness.
                ObjCRuntime.SendMessageNoReturn(commandBuffer, _selWaitUntilCompleted);

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
