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
/// The executor reaches into <see cref="FilterResult.UnderlyingSurface"/> to obtain the
/// backend's <see cref="OpenGLPixelRenderSurface"/> and runs the shader with both source and
/// destination FBOs - so input is never mutated and we avoid the readback / re-upload that
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
            default:
                // Flood / Compose / Composite / DropShadow → CPU until we ship dedicated shaders.
                return _fallback.Execute(filter, context);
        }
    }

    private FilterResult? TryGpuColorMatrix(ColorMatrixFilter cm, IImageFilterContext ctx)
    {
        FilterResult input = cm.Input is null ? ctx.Source : Execute(cm.Input, ctx);
        ScratchFilterResult? scratch = null;
        bool ownsResult = false;
        try
        {
            if (!TryGetReadableSurface(input, out var glSource)) return null;

            scratch = ctx.AcquireScratch(input.PixelWidth, input.PixelHeight, input.Bounds);
            if (!TryGetWritableSurface(scratch, out var glDest)) return null;
            if (!OpenGLFilterPasses.TryApplyColorMatrix(glSource, glDest, cm.Matrix)) return null;

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

    private FilterResult? TryGpuOffset(OffsetFilter o, IImageFilterContext ctx)
    {
        FilterResult input = o.Input is null ? ctx.Source : Execute(o.Input, ctx);
        ScratchFilterResult? scratch = null;
        bool ownsResult = false;
        try
        {
            if (!TryGetReadableSurface(input, out var glSource)) return null;

            // Dx/Dy are in logical/DIP units; the node only moves the result, so the pixels are
            // copied unchanged and the translation lands in the bounds the consumer places by.
            var bounds = new Rect(
                input.Bounds.X + (o.Dx * ctx.LogicalToPixelScaleX),
                input.Bounds.Y + (o.Dy * ctx.LogicalToPixelScaleY),
                input.Bounds.Width,
                input.Bounds.Height);

            scratch = ctx.AcquireScratch(input.PixelWidth, input.PixelHeight, bounds);
            if (!TryGetWritableSurface(scratch, out var glDest)) return null;
            if (!OpenGLFilterPasses.TryCopy(glSource, glDest)) return null;

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

            var layers = new List<OpenGLFilterPasses.CompositeLayer>(inputs.Count);
            foreach (var result in inputs)
            {
                if (!TryGetReadableSurface(result, out var glLayer)) return null;
                layers.Add(new OpenGLFilterPasses.CompositeLayer(
                    glLayer,
                    (int)Math.Round(result.Bounds.X - bounds.X),
                    (int)Math.Round(result.Bounds.Y - bounds.Y)));
            }

            int width = Math.Max(1, (int)Math.Ceiling(bounds.Width));
            int height = Math.Max(1, (int)Math.Ceiling(bounds.Height));
            scratch = ctx.AcquireScratch(width, height, bounds);
            if (!TryGetWritableSurface(scratch, out var glDest)) return null;
            if (!OpenGLFilterPasses.TryComposite(glDest, layers)) return null;

            ownsResult = true;
            return scratch;
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

    /// <summary>True when the result is a GPU target whose texture already holds its content.
    /// Results assembled by the CPU executor are not, and need an upload we punt on.</summary>
    private static bool TryGetReadableSurface(FilterResult result, out OpenGLPixelRenderSurface surface)
    {
        surface = null!;
        if (result.UnderlyingSurface is not OpenGLPixelRenderSurface gl) return false;
        if (!gl.IsFboInitialized || gl.Fbo == 0 || gl.Texture == 0) return false;
        surface = gl;
        return true;
    }

    /// <summary>True when the scratch is a GPU target ready to be rendered into. The pool hands
    /// back render targets whose GPU resources have not been created yet.</summary>
    private static bool TryGetWritableSurface(ScratchFilterResult scratch, out OpenGLPixelRenderSurface surface)
    {
        surface = null!;
        if (scratch.UnderlyingSurface is not OpenGLPixelRenderSurface gl) return false;
        gl.InitializeFbo();
        if (!gl.IsFboInitialized || gl.Fbo == 0 || gl.Texture == 0) return false;
        surface = gl;
        return true;
    }

    private FilterResult? TryGpuBlur(BlurFilter b, IImageFilterContext ctx)
    {
        // Radius is in logical/DIP units; convert to a pixel sigma (radius / 3, then by the
        // context's input-to-pixel scale) before handing the value to the GLSL pass.
        double rawSigmaX = BlurKernel.RadiusToSigma(b.RadiusX) * ctx.LogicalToPixelScaleX;
        double rawSigmaY = BlurKernel.RadiusToSigma(b.RadiusY) * ctx.LogicalToPixelScaleY;
        // Collapse anisotropic sigma to the geometric mean - matches Metal MPS's isotropic
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
            // Need both input and scratch backed by OpenGLPixelRenderSurfaces so we can run
            // the GLSL pass directly against their FBOs. If either isn't OpenGL (e.g. a CPU
            // fallback produced a generic CPU surface), bail to the fallback.
            if (input.UnderlyingSurface is not OpenGLPixelRenderSurface glSource) return null;

            // Source must have a valid FBO with content for GPU-side filter execution
            // (rendered into the FBO and ReadbackFromFbo'd at EndFrame). Not true for results
            // assembled by the CPU executor; those need GPU upload first, which we punt on.
            if (!glSource.IsFboInitialized || glSource.Fbo == 0 || glSource.Texture == 0) return null;

            scratch = ctx.AcquireScratch(input.PixelWidth, input.PixelHeight, input.Bounds);
            if (scratch.UnderlyingSurface is not OpenGLPixelRenderSurface glDest) return null;

            // Lazy FBO init - pool gives back a fresh RT whose GPU resources haven't been
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
