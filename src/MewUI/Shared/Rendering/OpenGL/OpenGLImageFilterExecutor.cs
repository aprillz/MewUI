using Debug = System.Diagnostics.Debug;
using ConditionalAttribute = System.Diagnostics.ConditionalAttribute;

using Aprillz.MewUI.Rendering.Filters;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// GPU-accelerated executor for filter graphs running on the OpenGL backend (used by MewVG
/// and the standalone OpenGL backend). Currently handles <see cref="BlurFilter"/> via the
/// <see cref="OpenGLGaussianBlur"/> shader; everything else delegates to the CPU fallback.
/// </summary>
/// <remarks>
/// The executor reaches into <see cref="FilterResult.UnderlyingTarget"/> to obtain the
/// backend's <see cref="OpenGLBitmapRenderTarget"/> and runs the shader with both source and
/// destination FBOs — so input is never mutated and we avoid the readback / re-upload that
/// plagued the older capability-based approach. When the input or scratch isn't an OpenGL
/// target (e.g. a <see cref="FloodFilter"/> result built by the CPU executor), we fall back
/// to CPU for that node only.
/// </remarks>
public sealed class OpenGLImageFilterExecutor : IImageFilterExecutor
{
    private readonly IImageFilterExecutor _fallback;

    public OpenGLImageFilterExecutor(IImageFilterExecutor? fallback = null)
    {
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
                // TryGpuBlur logs the specific reason on each null-return path itself.
                var gpuResult = TryGpuBlur(b, context);
                return gpuResult ?? _fallback.Execute(filter, context);
            }
            default:
                // ColorMatrix / Composite / Merge / DropShadow → CPU until we ship dedicated shaders.
                return _fallback.Execute(filter, context);
        }
    }

    private FilterResult? TryGpuBlur(BlurFilter b, IImageFilterContext ctx)
    {
        // Sigma is in logical/DIP units; the FBO we sample is at the source layer's pixel
        // resolution, so convert via the context's input-to-pixel scale before handing the
        // value to the GLSL pass.
        double rawSigmaX = b.SigmaX * ctx.LogicalToPixelScaleX;
        double rawSigmaY = b.SigmaY * ctx.LogicalToPixelScaleY;
        // Collapse anisotropic sigma to the geometric mean — matches Metal MPS's isotropic
        // Gaussian (which can't do separable per-axis without a custom compute shader).
        // Both backends now produce the same shape for σx ≠ σy / non-uniform-zoom inputs.
        double pxSigma = (rawSigmaX > 0 && rawSigmaY > 0)
            ? Math.Sqrt(rawSigmaX * rawSigmaY)
            : Math.Max(rawSigmaX, rawSigmaY);
        double pxSigmaX = pxSigma;
        double pxSigmaY = pxSigma;
        if (pxSigmaX <= 0 && pxSigmaY <= 0)
        {
            return b.Input is null ? ctx.Source : Execute(b.Input, ctx);
        }

        FilterResult input = b.Input is null ? ctx.Source : Execute(b.Input, ctx);
        ScratchFilterResult? scratch = null;
        bool ownsResult = false;
        try
        {
            // Need both input and scratch backed by OpenGLBitmapRenderTargets so we can run
            // the GLSL pass directly against their FBOs. If either isn't OpenGL (e.g. a CPU
            // fallback produced a generic IBitmapRenderTarget), bail to the fallback.
            if (input.UnderlyingTarget is not OpenGLBitmapRenderTarget glSource) return null;

            // Source must have a valid FBO with content — true for the SvgFilter source layer
            // (rendered into the FBO and ReadbackFromFbo'd at EndFrame). Not true for results
            // assembled by the CPU executor; those need GPU upload first, which we punt on.
            if (!glSource.IsFboInitialized || glSource.Fbo == 0 || glSource.Texture == 0) return null;

            scratch = ctx.AcquireScratch(input.PixelWidth, input.PixelHeight, input.Bounds);
            if (scratch.UnderlyingTarget is not OpenGLBitmapRenderTarget glDest) return null;

            // Lazy FBO init — pool gives back a fresh RT whose GPU resources haven't been
            // created yet (no BeginFrame has run on it). We're inside the main render path
            // so the GL context is current.
            glDest.InitializeFbo();
            if (!glDest.IsFboInitialized || glDest.Fbo == 0 || glDest.Texture == 0) return null;

            if (!OpenGLGaussianBlur.TryApply(glSource, glDest, pxSigmaX, pxSigmaY)) return null;

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
