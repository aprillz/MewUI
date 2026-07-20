using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.Direct2D;
using Aprillz.MewUI.Rendering.Filters;
using Aprillz.MewUI.Rendering.Simd;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Direct2D;

/// <summary>
/// GPU-accelerated filter executor for the Direct2D backend. Uses the built-in D2D effects
/// (<c>CLSID_D2D1GaussianBlur</c> etc.) on the destination surface's own device context - the
/// same one <see cref="Direct2DGpuPixelRenderSurface"/> uses for general offscreen draws, so
/// filters running on a worker thread's surface use that thread's context too.
/// </summary>
/// <remarks>
/// Two execution paths, picked per call:
/// <list type="bullet">
/// <item><b>Zero-copy</b>: when src AND scratch are <see cref="Direct2DGpuPixelRenderSurface"/>
///   (GPU-resident bitmaps), the effect runs straight from src to dst with
///   no CPU touch. Mirrors MewVG's FBO→FBO blur path.</item>
/// <item><b>CPU round-trip</b>: when ends are DIB-backed
///   <see cref="Direct2DPixelRenderSurface"/>, source pixels are CPU-premultiplied,
///   uploaded to a transient <c>ID2D1Bitmap1</c>, run through the effect, and the output
///   is rendered into the scratch DC RT (which commits to its DIB). Two CPU passes per
///   filter, but the GPU still does the kernel - orders of magnitude faster than running
///   the full Gaussian on CPU.</item>
/// </list>
/// Other filter nodes (ColorMatrix / Composite / Merge / DropShadow) fall through to the
/// CPU executor until matching D2D effects are wired.
/// </remarks>
internal sealed unsafe class Direct2DImageFilterExecutor : IImageFilterExecutor
{
    private readonly IImageFilterExecutor _fallback;
    private readonly Direct2DGraphicsFactory _factory;

    private bool DebugLogs = false;

    public Direct2DImageFilterExecutor(Direct2DGraphicsFactory factory, IImageFilterExecutor? fallback = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _fallback = fallback ?? new CpuImageFilterExecutor();
    }

    /// <inheritdoc />
    /// <remarks>
    /// GPU path - raster source at the renderer's full logical-to-pixel scale (no cap).
    /// Capping at 1× was a CPU-era policy: bilinear-stretching a small filter result up
    /// to screen DPI lost text and small-feature detail (e.g. 11px Tahoma in a card with
    /// drop shadow rendered crisp inside the 1× source bitmap but sub-pixel-aliased
    /// after the stretch - visible as missing characters). On GPU the per-pixel cost is
    /// negligible relative to the kernel work, and the result cache absorbs the larger
    /// bitmap memory across pan frames.
    /// </remarks>
    public double MaxInputScale => double.PositiveInfinity;

    public FilterResult Execute(ImageFilter filter, IImageFilterContext context) => filter switch
    {
        SourceFilter => context.Source,
        BlurFilter b => TryGpuBlur(b, context) ?? _fallback.Execute(filter, context),
        ColorMatrixFilter cm => TryGpuColorMatrix(cm, context) ?? _fallback.Execute(filter, context),
        OffsetFilter o => TryGpuOffset(o, context) ?? _fallback.Execute(filter, context),
        CompositeFilter cf => TryGpuComposite(cf, context) ?? _fallback.Execute(filter, context),
        MergeFilter m => TryGpuMerge(m, context) ?? _fallback.Execute(filter, context),
        _ => _fallback.Execute(filter, context),
    };

    private static void ConfigureBlur(nint effect, float sigma)
    {
        D2D1VTable.SetEffectValueFloat(effect, (uint)D2D1_GAUSSIANBLUR_PROP.STANDARD_DEVIATION, sigma);
        // SOFT border mode matches Metal MPS's default edge mode (Clamp) and OpenGL's
        // GL_CLAMP_TO_EDGE wrap mode used by OpenGLGaussianBlur - sample-position-outside
        // -input clamps to the nearest source pixel, fading the halo smoothly off the
        // source rect. HARD treated outside-input as transparent black, so the halo
        // dropped abruptly to alpha=0 at the source bitmap edge - visible as a hard
        // rectangular boundary that shifts with zoom (because the source bitmap pixel
        // grid changes resolution). SOFT keeps the visual result invariant under zoom
        // and matches the Metal/GL backends.
        D2D1VTable.SetEffectValueEnum(effect, (uint)D2D1_GAUSSIANBLUR_PROP.BORDER_MODE, (uint)D2D1_BORDER_MODE.SOFT);
        // QUALITY uses a higher-precision kernel sampling than SPEED - smoother gradient,
        // less visible stair-stepping at the halo. Cost is moderate (single-digit ms on
        // the source sizes we hit), and the result cache amortizes it across pan frames.
        D2D1VTable.SetEffectValueEnum(effect, (uint)D2D1_GAUSSIANBLUR_PROP.OPTIMIZATION, (uint)D2D1_GAUSSIANBLUR_OPTIMIZATION.QUALITY);
    }

    private static byte[] ReadAndPremultiply(Direct2DPixelRenderSurface rt)
    {
        var span = rt.GetPixelSpan();
        var copy = new byte[span.Length];
        SimdDispatcher.PremultiplyBgra(span, copy);
        return copy;
    }

    private static void UnpremultiplyDib(Direct2DPixelRenderSurface rt)
    {
        var span = rt.GetPixelSpan();
        SimdDispatcher.UnpremultiplyBgra(span, span);
    }

    private FilterResult? TryGpuBlur(BlurFilter b, IImageFilterContext ctx)
    {
        if (b.RadiusX <= 0 && b.RadiusY <= 0)
        {
            return b.Input is null ? ctx.Source : Execute(b.Input, ctx);
        }

        FilterResult input = b.Input is null ? ctx.Source : Execute(b.Input, ctx);

        // D2D1GaussianBlur StandardDeviation is in DIPs - D2D scales by the source bitmap's
        // own DpiScale internally to land in pixel space. The bitmap's DpiScale is set by
        // filter source layer (clamped to >= 1.0); when LogicalToPixelScale < 1.0 (zoom < 1x), the
        // bitmap's DpiScale is clamped to 1.0 while the actual pixel raster is sub-DIP, so
        // passing raw σ_DIP makes D2D over-blur. Pre-divide by the bitmap's reported DPI and
        // pre-multiply by ctx.LogicalToPixelScale so D2D's internal × DpiScale lands at the
        // true pixel σ - matching Metal's `σ × LogicalToPixelScale` formulation regardless
        // of the clamp.
        double bitmapDpiScaleX = input.UnderlyingSurface?.DpiScale ?? 1.0;
        double bitmapDpiScaleY = bitmapDpiScaleX;
        double sigmaXDip = BlurKernel.RadiusToSigma(b.RadiusX) * (ctx.LogicalToPixelScaleX / Math.Max(1e-9, bitmapDpiScaleX));
        double sigmaYDip = BlurKernel.RadiusToSigma(b.RadiusY) * (ctx.LogicalToPixelScaleY / Math.Max(1e-9, bitmapDpiScaleY));

        // D2D1GaussianBlur is isotropic - collapse anisotropic σ via geometric mean to
        // match Metal MPS (which does sqrt(σx·σy) inside MetalGaussianBlur.TryEncode).
        // The previous Math.Max picked the stronger axis and consistently over-blurred
        // when σx ≠ σy; geometric mean gives the same effective area as a true anisotropic
        // pass without an extra render trip.
        double sigmaCombined = (sigmaXDip > 0 && sigmaYDip > 0)
            ? Math.Sqrt(sigmaXDip * sigmaYDip)
            : Math.Max(sigmaXDip, sigmaYDip);
        float sigma = (float)sigmaCombined;
        ScratchFilterResult? scratch = null;
        bool ownsResult = false;
        try
        {
            scratch = ctx.AcquireScratch(input.PixelWidth, input.PixelHeight, input.Bounds);

            // Fast path: both ends GPU-resident. Run effect directly src.Bitmap → dst.Bitmap.
            if (input.UnderlyingSurface is Direct2DGpuPixelRenderSurface srcGpu &&
                scratch.UnderlyingSurface is Direct2DGpuPixelRenderSurface dstGpu &&
                dstGpu.DeviceContext is var dc and not 0)
            {
                if (!RunGpuOnlyBlur(dc, srcGpu, dstGpu, sigma))
                {
                    return null;
                }
                dstGpu.IncrementVersion();
                ownsResult = true;
                return scratch;
            }

            // Slow path: DIB-backed ends. Upload source pixels, run effect, render into
            // scratch's DC RT (which commits to its DIB).
            if (input.UnderlyingSurface is Direct2DPixelRenderSurface srcDib &&
                scratch.UnderlyingSurface is Direct2DPixelRenderSurface dstDib)
            {
                if (!RunDibRoundtripBlur(srcDib, dstDib, sigma))
                {
                    return null;
                }
                dstDib.IncrementVersion();
                ownsResult = true;
                return scratch;
            }

            // Mixed / unknown - bail to fallback.
            return null;
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
    private bool RunGpuOnlyBlur(nint dc, Direct2DGpuPixelRenderSurface srcGpu, Direct2DGpuPixelRenderSurface dstGpu, float sigma)
    {
        if (DebugLogs)
            System.Diagnostics.Debug.WriteLine($"[D2DBlur] GPU path src={srcGpu.PixelWidth}x{srcGpu.PixelHeight} σ={sigma:F2}");
        int hr = D2D1VTable.CreateEffect((ID2D1DeviceContext*)dc, D2D1.CLSID_D2D1GaussianBlur, out nint effect);
        if (hr < 0 || effect == 0)
        {
            if (DebugLogs)
                System.Diagnostics.Debug.WriteLine($"[D2DBlur] CreateEffect failed: 0x{hr:X8}");
            return false;
        }
        try
        {
            ConfigureBlur(effect, sigma);
            D2D1VTable.GetEffectOutput(effect, out nint effectImage);
            if (effectImage == 0) return false;
            try
            {
                D2D1VTable.SetEffectInput(effect, 0, srcGpu.Bitmap);

                // EnterCurrentThreadDcDraw bumps the nested-depth counter and returns true on
                // the outermost entry - used below to gate BeginDraw/EndDraw (D2D rejects
                // nested BeginDraw on the same DC).
                bool issuedBeginDraw = _factory.EnterCurrentThreadDcDraw(dc);
                try
                {
                    // Flush before the effect samples its input. The source bitmap was drawn
                    // in a prior nested pass whose inner OnEndFrame skipped EndDraw (outer
                    // pass owns the BeginDraw cycle), so the source's DrawImage commands
                    // are still queued in the DC command buffer - uncommitted to GPU. The
                    // upcoming DrawImage(effect) triggers the effect's GPU sampling of that
                    // bitmap; without an explicit submit, the effect samples the source's
                    // pre-write state (post-Clear empty) and produces empty output. Flush
                    // pushes those queued commands to the GPU without ending the BeginDraw
                    // pass. EndDraw later subsumes a Flush so this is no-op for outermost.
                    D2D1VTable.Flush((ID2D1RenderTarget*)dc);

                    D2D1VTable.GetTarget((ID2D1DeviceContext*)dc, out nint prevTarget);
                    var prevTransform = D2D1VTable.GetTransform((ID2D1RenderTarget*)dc);
                    D2D1VTable.GetDpi((ID2D1RenderTarget*)dc, out float prevDpiX, out float prevDpiY);

                    D2D1VTable.SetTarget((ID2D1DeviceContext*)dc, dstGpu.Bitmap);
                    // Match DPI to the dst bitmap so DrawImage's coordinate space lines up
                    // with its full pixel extent (effect output covers the entire dst
                    // bitmap pixel-for-pixel). Mismatched DPI here causes the effect to
                    // write to a sub-rect.
                    float dstDpi = (float)(96.0 * dstGpu.DpiScale);
                    D2D1VTable.SetDpi((ID2D1RenderTarget*)dc, dstDpi, dstDpi);

                    if (issuedBeginDraw)
                    {
                        D2D1VTable.BeginDraw((ID2D1RenderTarget*)dc);
                    }
                    // Reset transform (the parent left a Scale+Translate set) so DrawImage
                    // lands at the dst bitmap's (0,0) pixel-for-pixel.
                    D2D1VTable.SetTransform((ID2D1RenderTarget*)dc, D2D1_MATRIX_3X2_F.Identity);
                    D2D1VTable.Clear((ID2D1RenderTarget*)dc, new D2D1_COLOR_F(0, 0, 0, 0));
                    D2D1VTable.DrawImage((ID2D1DeviceContext*)dc, effectImage,
                        D2D1_INTERPOLATION_MODE.LINEAR, D2D1_COMPOSITE_MODE.SOURCE_COPY);
                    if (issuedBeginDraw)
                    {
                        int endHr = D2D1VTable.EndDraw((ID2D1RenderTarget*)dc);
                        if (endHr < 0)
                        {
                            _ = _factory.NotifyGpuDeviceLost(endHr);
                            // Release prevTarget if we acquired it but are bailing.
                            if (prevTarget != 0) ComHelpers.Release(prevTarget);
                            return false;
                        }
                    }

                    // Restore inside the lock so the next thread that enters sees the
                    // parent's state, not our intermediate dst settings.
                    D2D1VTable.SetTarget((ID2D1DeviceContext*)dc, prevTarget);
                    if (prevTarget != 0) ComHelpers.Release(prevTarget);
                    D2D1VTable.SetTransform((ID2D1RenderTarget*)dc, prevTransform);
                    D2D1VTable.SetDpi((ID2D1RenderTarget*)dc, prevDpiX, prevDpiY);
                }
                finally
                {
                    _factory.ExitCurrentThreadDcDraw(dc);
                }
            }
            finally
            {
                ComHelpers.Release(effectImage);
            }
        }
        finally
        {
            ComHelpers.Release(effect);
        }
        return true;
    }

    private bool RunDibRoundtripBlur(Direct2DPixelRenderSurface srcDib, Direct2DPixelRenderSurface dstDib, float sigma)
    {
        if (DebugLogs)
            System.Diagnostics.Debug.WriteLine($"[D2DBlur] DIB path src={srcDib.PixelWidth}x{srcDib.PixelHeight} σ={sigma:F2}");

        // CPU-side: read src DIB pixels and premultiply into a transient buffer.
        byte[] sourcePremultiplied = ReadAndPremultiply(srcDib);

        // Borrow a Direct2DGraphicsContext for the dst (binds its DC RT, BeginDraw); we
        // use the QI'd DeviceContext to upload source pixels as an effect input bitmap and
        // render the effect output into the dst DC RT, which commits to its DIB.
        using var dstCtx = (Direct2DGraphicsContext)_factory.CreateContext((IRenderTarget)dstDib);
        dstCtx.BeginFrame(dstDib);
        bool committed = false;
        try
        {
            nint dcRt = dstCtx.RenderTargetHandle;
            if (dcRt == 0) return false;
            if (ComHelpers.QueryInterface(dcRt, D2D1.IID_ID2D1DeviceContext, out var dcHandle) < 0 || dcHandle == 0)
            {
                return false;
            }
            try
            {
                var dc = (ID2D1DeviceContext*)dcHandle;
                var pixelFormat = new D2D1_PIXEL_FORMAT(D2D1.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.PREMULTIPLIED);
                float dpi = 96f;
                var bitmapProps = new D2D1_BITMAP_PROPERTIES1(pixelFormat, dpi, dpi, D2D1_BITMAP_OPTIONS.NONE, 0);
                var size = new D2D1_SIZE_U((uint)srcDib.PixelWidth, (uint)srcDib.PixelHeight);
                uint pitch = (uint)(srcDib.PixelWidth * 4);
                int hr = D2D1VTable.CreateBitmap1(dc, size, sourcePremultiplied, pitch, bitmapProps, out nint sourceBitmap);
                if (hr < 0 || sourceBitmap == 0) return false;

                nint effect = 0;
                try
                {
                    hr = D2D1VTable.CreateEffect(dc, D2D1.CLSID_D2D1GaussianBlur, out effect);
                    if (hr < 0 || effect == 0) return false;

                    D2D1VTable.SetEffectInput(effect, 0, sourceBitmap);
                    ConfigureBlur(effect, sigma);

                    var rt = (ID2D1RenderTarget*)dcRt;
                    // Reset DC transform - Direct2DGraphicsContext.BeginFrame doesn't push
                    // identity to the native DC at frame start (only resets the C# field),
                    // so any transform left over from previous use of this DC RT (e.g.,
                    // source content rendered before our filter pass) would shift the effect
                    // output's drawn position on dst.
                    D2D1VTable.SetTransform(rt, D2D1_MATRIX_3X2_F.Identity);
                    D2D1VTable.Clear(rt, new D2D1_COLOR_F(0, 0, 0, 0));

                    D2D1VTable.GetEffectOutput(effect, out nint effectImage);
                    if (effectImage == 0) return false;
                    try
                    {
                        D2D1VTable.DrawImage(dc, effectImage,
                            D2D1_INTERPOLATION_MODE.LINEAR, D2D1_COMPOSITE_MODE.SOURCE_COPY);
                    }
                    finally
                    {
                        ComHelpers.Release(effectImage);
                    }
                    committed = true;
                }
                finally
                {
                    if (effect != 0) ComHelpers.Release(effect);
                    ComHelpers.Release(sourceBitmap);
                }
            }
            finally
            {
                ComHelpers.Release(dcHandle);
            }
        }
        finally
        {
            dstCtx.EndFrame();
        }

        if (committed)
        {
            // D2D commits premultiplied bytes; the DIB contract is straight-alpha. Convert
            // in place so downstream readers (Direct2DImage upload, CPU executor input) see
            // the correct format.
            UnpremultiplyDib(dstDib);
        }
        return committed;
    }

    // ColorMatrix/Offset/Composite/Merge follow TryGpuBlur's GPU-only pattern; DIB fallback
    // is left to CpuImageFilterExecutor.
    private FilterResult? TryGpuColorMatrix(ColorMatrixFilter cm, IImageFilterContext ctx)
    {
        FilterResult input = cm.Input is null ? ctx.Source : Execute(cm.Input, ctx);
        ScratchFilterResult? scratch = null;
        bool ownsResult = false;
        try
        {
            scratch = ctx.AcquireScratch(input.PixelWidth, input.PixelHeight, input.Bounds);
            if (input.UnderlyingSurface is Direct2DGpuPixelRenderSurface srcGpu &&
                srcGpu.IsDeviceCurrent &&
                scratch.UnderlyingSurface is Direct2DGpuPixelRenderSurface dstGpu &&
                dstGpu.IsDeviceCurrent &&
                dstGpu.DeviceContext is var dc and not 0)
            {
                // D2D D2D1_MATRIX_5X4_F is 5 rows × 4 cols (output channel = column,
                // input source/offset = row), but ColorMatrixFilter.Matrix is the source
                // convention: 4 rows × 5 cols (output channel = row, input source = col).
                // Transpose into a stackalloc'd Span - 80 bytes, no per-call heap alloc.
                float[] svgMatrix = cm.Matrix;
                if (!RunGpuSingleInputEffect(dc, D2D1.CLSID_D2D1ColorMatrix, srcGpu, dstGpu, effect =>
                    {
                        Span<float> d2d = stackalloc float[20];
                        for (int outCh = 0; outCh < 4; outCh++)
                            for (int inSrc = 0; inSrc < 5; inSrc++)
                                d2d[inSrc * 4 + outCh] = svgMatrix[outCh * 5 + inSrc];
                        D2D1VTable.SetEffectValueMatrix5x4(effect, (uint)D2D1_COLORMATRIX_PROP.COLOR_MATRIX, d2d);
                    }))
                {
                    return null;
                }
                dstGpu.IncrementVersion();
                ownsResult = true;
                return scratch;
            }
            return null;
        }
        finally
        {
            if (!ownsResult) scratch?.Dispose();
            if (!ReferenceEquals(input, ctx.Source)) input.Dispose();
        }
    }

    private FilterResult? TryGpuOffset(OffsetFilter o, IImageFilterContext ctx)
    {
        FilterResult input = o.Input is null ? ctx.Source : Execute(o.Input, ctx);
        ScratchFilterResult? scratch = null;
        bool ownsResult = false;
        try
        {
            scratch = ctx.AcquireScratch(input.PixelWidth, input.PixelHeight, input.Bounds);
            if (input.UnderlyingSurface is Direct2DGpuPixelRenderSurface srcGpu &&
                srcGpu.IsDeviceCurrent &&
                scratch.UnderlyingSurface is Direct2DGpuPixelRenderSurface dstGpu &&
                dstGpu.IsDeviceCurrent &&
                dstGpu.DeviceContext is var dc and not 0)
            {
                // D2D AffineTransform2D translation is expressed in the destination
                // context's DIPs, while OffsetFilter dx/dy are SVG logical units. Convert
                // the desired logical-to-pixel offset back to DIPs using the target bitmap's
                // DPI. These scales can differ when the filtered element has its own
                // transform (issue-084-02: 24.1 * 0.06945, not a raw 24.1-DIP shift).
                double targetDpiScale = Math.Max(1e-9, dstGpu.DpiScale);
                float dxDip = (float)(o.Dx * ctx.LogicalToPixelScaleX / targetDpiScale);
                float dyDip = (float)(o.Dy * ctx.LogicalToPixelScaleY / targetDpiScale);
                if (!RunGpuSingleInputEffect(dc, D2D1.CLSID_D2D12DAffineTransform, srcGpu, dstGpu, effect =>
                    {
                        var matrix = new D2D1_MATRIX_3X2_F(1f, 0f, 0f, 1f, dxDip, dyDip);
                        D2D1VTable.SetEffectValueMatrix3x2(effect, (uint)D2D1_2DAFFINETRANSFORM_PROP.TRANSFORM_MATRIX, matrix);
                    }))
                {
                    return null;
                }
                dstGpu.IncrementVersion();
                ownsResult = true;
                return scratch;
            }
            return null;
        }
        finally
        {
            if (!ownsResult) scratch?.Dispose();
            if (!ReferenceEquals(input, ctx.Source)) input.Dispose();
        }
    }

    private FilterResult? TryGpuComposite(CompositeFilter cf, IImageFilterContext ctx)
    {
        // Two inputs (fg + bg). Each may itself be a sub-graph requiring recursive Execute.
        // Hold both results until composite is done so neither scratch is recycled mid-draw.
        FilterResult fg = Execute(cf.Foreground, ctx);
        FilterResult bg = ReferenceEquals(cf.Background, cf.Foreground) ? fg : Execute(cf.Background, ctx);
        ScratchFilterResult? scratch = null;
        bool ownsResult = false;
        try
        {
            // Choose the larger envelope; both inputs share the source coordinate frame.
            int pw = Math.Max(fg.PixelWidth, bg.PixelWidth);
            int ph = Math.Max(fg.PixelHeight, bg.PixelHeight);
            scratch = ctx.AcquireScratch(pw, ph, fg.Bounds);
            if (fg.UnderlyingSurface is Direct2DGpuPixelRenderSurface fgGpu &&
                fgGpu.IsDeviceCurrent &&
                bg.UnderlyingSurface is Direct2DGpuPixelRenderSurface bgGpu &&
                bgGpu.IsDeviceCurrent &&
                scratch.UnderlyingSurface is Direct2DGpuPixelRenderSurface dstGpu &&
                dstGpu.IsDeviceCurrent &&
                dstGpu.DeviceContext is var dc and not 0)
            {
                if (!RunGpuTwoInputEffect(dc, D2D1.CLSID_D2D1Composite, bgGpu, fgGpu, dstGpu, effect =>
                    {
                        D2D1VTable.SetEffectValueEnum(effect, (uint)D2D1_COMPOSITE_PROP.MODE, (uint)MapCompositeOp(cf.Op));
                    }))
                {
                    return null;
                }
                dstGpu.IncrementVersion();
                ownsResult = true;
                return scratch;
            }
            return null;
        }
        finally
        {
            if (!ownsResult) scratch?.Dispose();
            if (!ReferenceEquals(bg, fg) && !ReferenceEquals(bg, ctx.Source)) bg.Dispose();
            if (!ReferenceEquals(fg, ctx.Source)) fg.Dispose();
        }
    }

    private FilterResult? TryGpuMerge(MergeFilter m, IImageFilterContext ctx)
    {
        // Merge filter: render each input bitmap into the output, bottom-to-top, with
        // SOURCE_OVER. Implement directly via DrawImage(SOURCE_OVER) chained per input
        // - this matches ordered source-over semantics and avoids D2D1Composite's input-rect
        // output-bounds quirks (where fg pixels outside input0's bbox can get cropped).
        System.Diagnostics.Debug.WriteLine($"[D2DMerge] inputs={m.InputList.Count}");
        if (m.InputList.Count == 0) return ctx.Source;

        var heldInputs = new List<FilterResult>(m.InputList.Count);
        try
        {
            foreach (var input in m.InputList)
            {
                heldInputs.Add(Execute(input, ctx));
            }

            // Compute the output envelope: max of all inputs' pixel sizes. All inputs share
            // the same source coordinate frame (filterRegion at SourceGraphic raster time),
            // so taking the max is sufficient.
            int pw = 0, ph = 0;
            foreach (var input in heldInputs)
            {
                if (input.PixelWidth > pw) pw = input.PixelWidth;
                if (input.PixelHeight > ph) ph = input.PixelHeight;
            }
            if (pw <= 0 || ph <= 0) return null;

            ScratchFilterResult? scratch = ctx.AcquireScratch(pw, ph, heldInputs[0].Bounds);
            if (scratch is null || scratch.UnderlyingSurface is not Direct2DGpuPixelRenderSurface dstGpu)
            {
                scratch?.Dispose();
                return null;
            }

            if (!dstGpu.IsDeviceCurrent)
            {
                scratch.Dispose();
                return null;
            }

            nint dc = dstGpu.DeviceContext;
            if (dc == 0)
            {
                scratch.Dispose();
                return null;
            }

            if (!CompositeBitmapsIntoTarget(dc, heldInputs, dstGpu))
            {
                scratch.Dispose();
                return null;
            }
            dstGpu.IncrementVersion();
            return scratch;
        }
        finally
        {
            foreach (var inp in heldInputs)
            {
                if (!ReferenceEquals(inp, ctx.Source))
                {
                    inp.Dispose();
                }
            }
        }
    }

    /// <summary>Renders <paramref name="inputs"/> into <paramref name="dstGpu"/> bottom-to-top
    /// with SOURCE_OVER. All inputs assumed to share the source coordinate frame.</summary>
    private bool CompositeBitmapsIntoTarget(nint dc, IReadOnlyList<FilterResult> inputs, Direct2DGpuPixelRenderSurface dstGpu)
    {
        // EnterCurrentThreadDcDraw - see RunGpuOnlyBlur. Nested-depth gate for BeginDraw/EndDraw.
        bool issuedBeginDraw = _factory.EnterCurrentThreadDcDraw(dc);
        bool ok = true;
        try
        {
            // Flush before sampling inputs - see RunGpuOnlyBlur for rationale.
            D2D1VTable.Flush((ID2D1RenderTarget*)dc);

            D2D1VTable.GetTarget((ID2D1DeviceContext*)dc, out nint prevTarget);
            var prevTransform = D2D1VTable.GetTransform((ID2D1RenderTarget*)dc);
            D2D1VTable.GetDpi((ID2D1RenderTarget*)dc, out float prevDpiX, out float prevDpiY);

            D2D1VTable.SetTarget((ID2D1DeviceContext*)dc, dstGpu.Bitmap);
            float dstDpi = (float)(96.0 * dstGpu.DpiScale);
            D2D1VTable.SetDpi((ID2D1RenderTarget*)dc, dstDpi, dstDpi);

            if (issuedBeginDraw)
            {
                D2D1VTable.BeginDraw((ID2D1RenderTarget*)dc);
            }
            D2D1VTable.SetTransform((ID2D1RenderTarget*)dc, D2D1_MATRIX_3X2_F.Identity);
            D2D1VTable.Clear((ID2D1RenderTarget*)dc, new D2D1_COLOR_F(0, 0, 0, 0));
            for (int i = 0; i < inputs.Count; i++)
            {
                if (inputs[i].UnderlyingSurface is not Direct2DGpuPixelRenderSurface srcGpu || !srcGpu.IsDeviceCurrent) { ok = false; break; }
                D2D1VTable.DrawImage((ID2D1DeviceContext*)dc, srcGpu.Bitmap,
                    D2D1_INTERPOLATION_MODE.LINEAR, D2D1_COMPOSITE_MODE.SOURCE_OVER);
            }
            if (issuedBeginDraw)
            {
                int endHr = D2D1VTable.EndDraw((ID2D1RenderTarget*)dc);
                if (endHr < 0)
                {
                    _ = _factory.NotifyGpuDeviceLost(endHr);
                    ok = false;
                }
            }

            D2D1VTable.SetTarget((ID2D1DeviceContext*)dc, prevTarget);
            if (prevTarget != 0) ComHelpers.Release(prevTarget);
            D2D1VTable.SetTransform((ID2D1RenderTarget*)dc, prevTransform);
            D2D1VTable.SetDpi((ID2D1RenderTarget*)dc, prevDpiX, prevDpiY);
        }
        finally
        {
            _factory.ExitCurrentThreadDcDraw(dc);
        }
        return ok;
    }

    /// <summary>Applies a single-input D2D effect (input bitmap → effect → dst bitmap).
    /// Mirrors the DC state save/restore + BeginDraw bracketing of <see cref="RunGpuOnlyBlur"/>
    /// so multiple effects compose cleanly without leaking transform/dpi/target between them.</summary>
    private bool RunGpuSingleInputEffect(nint dc, Guid effectClsid,
        Direct2DGpuPixelRenderSurface srcGpu, Direct2DGpuPixelRenderSurface dstGpu,
        Action<nint> configureEffect)
    {
        int hr = D2D1VTable.CreateEffect((ID2D1DeviceContext*)dc, effectClsid, out nint effect);
        if (hr < 0 || effect == 0) return false;
        try
        {
            D2D1VTable.SetEffectInput(effect, 0, srcGpu.Bitmap);
            configureEffect(effect);
            return DrawEffectIntoBitmap(dc, effect, dstGpu);
        }
        finally
        {
            ComHelpers.Release(effect);
        }
    }

    /// <summary>Applies a 2-input D2D effect (e.g. Composite). Input slots: 0=destination/bg, 1=source/fg.</summary>
    private bool RunGpuTwoInputEffect(nint dc, Guid effectClsid,
        Direct2DGpuPixelRenderSurface input0, Direct2DGpuPixelRenderSurface input1,
        Direct2DGpuPixelRenderSurface dstGpu, Action<nint> configureEffect)
    {
        int hr = D2D1VTable.CreateEffect((ID2D1DeviceContext*)dc, effectClsid, out nint effect);
        if (hr < 0 || effect == 0) return false;
        try
        {
            D2D1VTable.SetEffectInputCount(effect, 2);
            D2D1VTable.SetEffectInput(effect, 0, input0.Bitmap);
            D2D1VTable.SetEffectInput(effect, 1, input1.Bitmap);
            configureEffect(effect);
            return DrawEffectIntoBitmap(dc, effect, dstGpu);
        }
        finally
        {
            ComHelpers.Release(effect);
        }
    }

    /// <summary>DC-state-aware DrawImage(effectOutput → dstGpu.Bitmap). Matches RunGpuOnlyBlur's
    /// state handling: save/swap target, identity-transform + clear + DrawImage, restore.</summary>
    private bool DrawEffectIntoBitmap(nint dc, nint effect, Direct2DGpuPixelRenderSurface dstGpu)
    {
        D2D1VTable.GetEffectOutput(effect, out nint effectImage);
        if (effectImage == 0) return false;
        try
        {
            // EnterCurrentThreadDcDraw - see RunGpuOnlyBlur. Nested-depth gate for BeginDraw/EndDraw.
            bool issuedBeginDraw = _factory.EnterCurrentThreadDcDraw(dc);
            bool ok = true;
            try
            {
                // Flush before sampling inputs - see RunGpuOnlyBlur for rationale.
                D2D1VTable.Flush((ID2D1RenderTarget*)dc);

                D2D1VTable.GetTarget((ID2D1DeviceContext*)dc, out nint prevTarget);
                var prevTransform = D2D1VTable.GetTransform((ID2D1RenderTarget*)dc);
                D2D1VTable.GetDpi((ID2D1RenderTarget*)dc, out float prevDpiX, out float prevDpiY);

                D2D1VTable.SetTarget((ID2D1DeviceContext*)dc, dstGpu.Bitmap);
                float dstDpi = (float)(96.0 * dstGpu.DpiScale);
                D2D1VTable.SetDpi((ID2D1RenderTarget*)dc, dstDpi, dstDpi);

                if (issuedBeginDraw)
                {
                    D2D1VTable.BeginDraw((ID2D1RenderTarget*)dc);
                }
                D2D1VTable.SetTransform((ID2D1RenderTarget*)dc, D2D1_MATRIX_3X2_F.Identity);
                D2D1VTable.Clear((ID2D1RenderTarget*)dc, new D2D1_COLOR_F(0, 0, 0, 0));
                D2D1VTable.DrawImage((ID2D1DeviceContext*)dc, effectImage,
                    D2D1_INTERPOLATION_MODE.LINEAR, D2D1_COMPOSITE_MODE.SOURCE_COPY);
                if (issuedBeginDraw)
                {
                    int endHr = D2D1VTable.EndDraw((ID2D1RenderTarget*)dc);
                    if (endHr < 0)
                    {
                        _ = _factory.NotifyGpuDeviceLost(endHr);
                        ok = false;
                    }
                }

                D2D1VTable.SetTarget((ID2D1DeviceContext*)dc, prevTarget);
                if (prevTarget != 0) ComHelpers.Release(prevTarget);
                D2D1VTable.SetTransform((ID2D1RenderTarget*)dc, prevTransform);
                D2D1VTable.SetDpi((ID2D1RenderTarget*)dc, prevDpiX, prevDpiY);
            }
            finally
            {
                _factory.ExitCurrentThreadDcDraw(dc);
            }
            return ok;
        }
        finally
        {
            ComHelpers.Release(effectImage);
        }
    }

    private static D2D1_COMPOSITE_MODE MapCompositeOp(CompositeOp op) => op switch
    {
        CompositeOp.Over => D2D1_COMPOSITE_MODE.SOURCE_OVER,
        CompositeOp.In => D2D1_COMPOSITE_MODE.SOURCE_IN,
        CompositeOp.Out => D2D1_COMPOSITE_MODE.SOURCE_OUT,
        CompositeOp.Atop => D2D1_COMPOSITE_MODE.SOURCE_ATOP,
        CompositeOp.Xor => D2D1_COMPOSITE_MODE.XOR,
        _ => D2D1_COMPOSITE_MODE.SOURCE_OVER,
    };
}
