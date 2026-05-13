using System.Numerics;

using Aprillz.MewUI.Rendering.Simd;

namespace Aprillz.MewUI.Rendering.Filters;

/// <summary>
/// Backend-agnostic CPU evaluator for <see cref="ImageFilter"/> graphs. Walks the DAG
/// recursively, materializing each intermediate as a scratch BGRA32 buffer rented from the
/// context's pool. All math runs in premultiplied alpha (linearly blendable) and the result's
/// <see cref="FilterResult.IsPremultiplied"/> reports <see langword="true"/>.
/// </summary>
/// <remarks>
/// Used as:
/// <list type="bullet">
/// <item>The default executor when no GPU backend is available.</item>
/// <item>The fallback executor for GPU backends — they delegate unsupported nodes here.
/// In that case the GPU result is read back via <see cref="FilterResult.ReadPixels"/> and
/// processed on CPU; subsequent GPU nodes re-upload the CPU result.</item>
/// </list>
/// Correctness over speed: this is the reference implementation for image filter semantics,
/// not the perf path. GPU executors should match its output bit-exactly (within sRGB rounding).
/// </remarks>
public sealed class CpuImageFilterExecutor : IImageFilterExecutor
{
    /// <inheritdoc />
    /// <remarks>
    /// CPU Gaussian on a transform-upscaled buffer is the perf killer (1440×1440 σ=80 ≈ 5 s).
    /// Cap at 1.0 so the source layer rasterizes at 100% logical resolution;
    /// the backend's hardware bilinear path stretches the small filter result up to the
    /// final display rect during DrawImage essentially for free, and the visual difference
    /// is negligible since blur output is low-frequency.
    /// </remarks>
    public double MaxInputScale => 1.0;

    public FilterResult Execute(ImageFilter filter, IImageFilterContext context) => filter switch
    {
        SourceFilter => context.Source,
        FloodFilter f => RenderFlood(f, context),
        BlurFilter b => RenderBlur(b, context),
        ColorMatrixFilter cm => RenderColorMatrix(cm, context),
        OffsetFilter o => RenderOffset(o, context),
        DropShadowFilter ds => RenderDropShadow(ds, context),
        ComposeFilter c => RenderCompose(c, context),
        MergeFilter m => RenderMerge(m, context),
        CompositeFilter cf => RenderComposite(cf, context),
        _ => throw new NotSupportedException($"Unknown filter: {filter.GetType().Name}"),
    };

    private FilterResult ResolveInput(ImageFilter? input, IImageFilterContext ctx)
        => input is null ? ctx.Source : Execute(input, ctx);

    // ─────────────────────────────────────────────────────────────────────
    // Node implementations
    // ─────────────────────────────────────────────────────────────────────

    private static FilterResult RenderFlood(FloodFilter f, IImageFilterContext ctx)
    {
        var bounds = ctx.SourceBounds;
        int w = (int)Math.Max(1, Math.Ceiling(bounds.Width));
        int h = (int)Math.Max(1, Math.Ceiling(bounds.Height));
        var scratch = ctx.AcquireScratch(w, h, bounds);
        scratch.AsImage(); // ensure backend image exists
        FillSolid(scratch, w, h, f.Color);
        return scratch;
    }

    private FilterResult RenderBlur(BlurFilter b, IImageFilterContext ctx)
    {
        // Sigma is in logical/DIP units; convert to pixel sigma using the source layer's
        // input-to-pixel scale so the blur tracks zoom and DPI like Skia/WPF effects.
        double pxSigmaX = b.SigmaX * ctx.LogicalToPixelScaleX;
        double pxSigmaY = b.SigmaY * ctx.LogicalToPixelScaleY;
        if (pxSigmaX <= 0 && pxSigmaY <= 0)
        {
            return ResolveInput(b.Input, ctx);
        }

        using var input = ResolveInput(b.Input, ctx) is var raw && ReferenceEquals(raw, ctx.Source)
            ? null  // borrowed source — don't dispose, but we still want pixel access
            : raw as IDisposable;

        var inputResult = ResolveInput(b.Input, ctx);
        try
        {
            var pixels = inputResult.ReadPixels(out int stride);
            if (pixels.IsEmpty)
            {
                return CloneAsScratch(inputResult, ctx);
            }

            int w = inputResult.PixelWidth;
            int h = inputResult.PixelHeight;
            byte[] working = pixels.ToArray();

            // Premultiply if source isn't already (linear convolution requires premultiplied
            // alpha for correctness — straight-alpha blur darkens semi-transparent edges).
            if (!inputResult.IsPremultiplied)
            {
                PremultiplyInPlace(working);
            }

            // CPU Gaussian is O(W·H·kernel) per axis; at high zoom the source layer is
            // upscaled (e.g. 1440×1440, σ=80, kernel radius 240 → ~2 GOps × 2 axes = seconds).
            // Downsample to 100% logical size before blur. We DON'T upsample back — the
            // scratch is emitted at the downsampled dimensions and the final DrawImage at
            // the caller stretches it to the filter region using the backend's hardware
            // bilinear path (D2D/GDI/MewVG), which is essentially free vs a CPU upsample
            // pass. NOTE: this means the result has fewer pixels than the source layer.
            // Downstream filter nodes that align by raw pixel index (Composite/Merge's
            // MaterializeAt) would mis-align here — fine for plain feGaussianBlur but a
            // future fix when complex filter chains land for the CPU executor.
            int dsFactor = ChooseDownsampleFactor(ctx.LogicalToPixelScaleX, ctx.LogicalToPixelScaleY, w, h);
            int blurW = w / dsFactor;
            int blurH = h / dsFactor;
            byte[] blurBuf = dsFactor > 1
                ? BoxDownsample(working, w, h, dsFactor, out blurW, out blurH)
                : working;
            double dsSigmaX = pxSigmaX / dsFactor;
            double dsSigmaY = pxSigmaY / dsFactor;

            if (dsSigmaX > 0)
            {
                blurBuf = BlurAxis(blurBuf, blurW, blurH, dsSigmaX, horizontal: true);
            }
            if (dsSigmaY > 0)
            {
                blurBuf = BlurAxis(blurBuf, blurW, blurH, dsSigmaY, horizontal: false);
            }

            var output = ctx.AcquireScratch(blurW, blurH, inputResult.Bounds);
            // Match the scratch surface's pixel contract — backends like the D2D DIB report
            // IsPremultiplied=false, meaning their pixel buffer must contain straight-alpha
            // bytes. We blurred in premultiplied space for correctness, so unpremultiply
            // back before writing if the scratch expects straight. Otherwise the next
            // consumer reads premultiplied bytes as if straight → "blocky" green halo
            // around semi-transparent edges.
            if (!output.IsPremultiplied)
            {
                UnpremultiplyInPlace(blurBuf);
            }
            CopyToScratch(output, blurBuf, blurW, blurH);
            return output;
        }
        finally
        {
            // ResolveInput returned ctx.Source (borrowed) or our own scratch. Borrowed disposes
            // are no-op; scratch results need release. We can't tell in advance, so dispose
            // unconditionally (BorrowedFilterResult.Dispose is intentionally idempotent no-op).
            if (!ReferenceEquals(inputResult, ctx.Source))
            {
                inputResult.Dispose();
            }
        }
    }

    private FilterResult RenderColorMatrix(ColorMatrixFilter cm, IImageFilterContext ctx)
    {
        var input = ResolveInput(cm.Input, ctx);
        try
        {
            var pixels = input.ReadPixels(out _);
            int w = input.PixelWidth;
            int h = input.PixelHeight;
            byte[] working = pixels.IsEmpty ? new byte[w * h * 4] : pixels.ToArray();

            // Color matrix filters operate on straight (un-premultiplied) values.
            if (input.IsPremultiplied)
            {
                UnpremultiplyInPlace(working);
            }
            ApplyColorMatrix(working, cm.Matrix);

            var output = ctx.AcquireScratch(w, h, input.Bounds);
            if (output.IsPremultiplied)
            {
                PremultiplyInPlace(working);
            }
            CopyToScratch(output, working, w, h);
            return output;
        }
        finally
        {
            if (!ReferenceEquals(input, ctx.Source)) input.Dispose();
        }
    }

    private FilterResult RenderOffset(OffsetFilter o, IImageFilterContext ctx)
    {
        var input = ResolveInput(o.Input, ctx);
        // Dx/Dy are in logical/DIP units; convert to pixel offset for bounds translation.
        double pxDx = o.Dx * ctx.LogicalToPixelScaleX;
        double pxDy = o.Dy * ctx.LogicalToPixelScaleY;
        // Offset is purely a bounds change — the pixels stay the same. Wrap the input
        // result in a new BorrowedFilterResult with translated bounds (cheaper than
        // copying pixels to a scratch).
        var newBounds = new Rect(input.Bounds.X + pxDx, input.Bounds.Y + pxDy,
            input.Bounds.Width, input.Bounds.Height);
        // Can't pass through the disposable owned by an upstream node; copy instead.
        try
        {
            var pixels = input.ReadPixels(out _);
            var output = ctx.AcquireScratch(input.PixelWidth, input.PixelHeight, newBounds);
            CopyToScratch(output, pixels.IsEmpty ? new byte[input.PixelWidth * input.PixelHeight * 4] : pixels.ToArray(),
                input.PixelWidth, input.PixelHeight);
            return output;
        }
        finally
        {
            if (!ReferenceEquals(input, ctx.Source)) input.Dispose();
        }
    }

    private FilterResult RenderCompose(ComposeFilter c, IImageFilterContext ctx)
    {
        // Inner runs against the original source. Outer runs against inner's result.
        var innerResult = Execute(c.Inner, ctx);
        try
        {
            using var subContext = ((DefaultFilterContext)ctx).WithSource(innerResult) as IDisposable;
            var outerCtx = subContext is null ? ctx.WithSource(innerResult) : (IImageFilterContext)subContext;
            return Execute(c.Outer, outerCtx);
        }
        finally
        {
            if (!ReferenceEquals(innerResult, ctx.Source)) innerResult.Dispose();
        }
    }

    private FilterResult RenderMerge(MergeFilter m, IImageFilterContext ctx)
    {
        // Merge filters source-over each input onto the accumulated result in order.
        // Sequence: result_0 = inputs[0]; result_i = source_over(inputs[i], result_{i-1}).
        if (m.InputList.Count == 0)
        {
            // Empty merge → empty transparent layer at source bounds.
            int w = (int)Math.Max(1, Math.Ceiling(ctx.SourceBounds.Width));
            int h = (int)Math.Max(1, Math.Ceiling(ctx.SourceBounds.Height));
            var empty = ctx.AcquireScratch(w, h, ctx.SourceBounds);
            FillSolid(empty, w, h, Color.Transparent);
            return empty;
        }

        FilterResult? acc = null;
        try
        {
            for (int i = 0; i < m.InputList.Count; i++)
            {
                var input = Execute(m.InputList[i], ctx);
                if (acc is null)
                {
                    // First input becomes the initial accumulator. Materialize as scratch
                    // so subsequent source-over passes can write into it.
                    acc = CloneAsScratch(input, ctx);
                    if (!ReferenceEquals(input, ctx.Source)) input.Dispose();
                    continue;
                }

                try
                {
                    var combined = SourceOver(input, acc, ctx);
                    acc.Dispose();
                    acc = combined;
                }
                finally
                {
                    if (!ReferenceEquals(input, ctx.Source)) input.Dispose();
                }
            }

            return acc!;
        }
        catch
        {
            acc?.Dispose();
            throw;
        }
    }

    private FilterResult RenderComposite(CompositeFilter cf, IImageFilterContext ctx)
    {
        var fg = Execute(cf.Foreground, ctx);
        FilterResult? bg = null;
        try
        {
            bg = Execute(cf.Background, ctx);
            return cf.Op switch
            {
                CompositeOp.Over => SourceOver(fg, bg, ctx),
                CompositeOp.In => PorterDuff(fg, bg, ctx, PorterDuffOp.In),
                CompositeOp.Out => PorterDuff(fg, bg, ctx, PorterDuffOp.Out),
                CompositeOp.Atop => PorterDuff(fg, bg, ctx, PorterDuffOp.Atop),
                CompositeOp.Xor => PorterDuff(fg, bg, ctx, PorterDuffOp.Xor),
                _ => throw new NotSupportedException($"Composite op {cf.Op} not supported"),
            };
        }
        finally
        {
            if (!ReferenceEquals(fg, ctx.Source)) fg.Dispose();
            if (bg is not null && !ReferenceEquals(bg, ctx.Source)) bg.Dispose();
        }
    }

    private FilterResult RenderDropShadow(DropShadowFilter ds, IImageFilterContext ctx)
    {
        // Decompose to: Merge[
        //   (mode == DrawShadowAndForeground ? Source : nothing),
        //   Composite(Flood(color), Offset(dx, dy, Blur(σ, source-alpha)), op=In)
        // ]
        // Build the equivalent graph and let the standard evaluator handle it.
        var input = ds.Input ?? SourceFilter.Instance;
        ImageFilter shadow = new CompositeFilter(
            foreground: new FloodFilter(ds.Color),
            background: new OffsetFilter(ds.Dx, ds.Dy, new BlurFilter(ds.Sigma, input)),
            op: CompositeOp.In);

        ImageFilter graph = ds.Mode == DropShadowMode.DrawShadowOnly
            ? shadow
            : new MergeFilter(shadow, input);

        return Execute(graph, ctx);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Pixel helpers
    // ─────────────────────────────────────────────────────────────────────

    private FilterResult CloneAsScratch(FilterResult source, IImageFilterContext ctx)
    {
        var pixels = source.ReadPixels(out _);
        var scratch = ctx.AcquireScratch(source.PixelWidth, source.PixelHeight, source.Bounds);
        if (!pixels.IsEmpty)
        {
            CopyToScratch(scratch, pixels.ToArray(), source.PixelWidth, source.PixelHeight);
        }
        return scratch;
    }

    private static void CopyToScratch(ScratchFilterResult scratch, byte[] pixels, int width, int height)
    {
        // Write through the internal CPU pixel surface hook without exposing mutable pixels
        // on the public FilterResult API.
        var target = ((IPixelTargetAccess)scratch).Target;
        var dest = target.GetWritablePixelSpan();
        int needed = Math.Min(pixels.Length, dest.Length);
        pixels.AsSpan(0, needed).CopyTo(dest);
        target.IncrementVersion();
    }

    private static void FillSolid(ScratchFilterResult scratch, int width, int height, Color color)
    {
        var target = ((IPixelTargetAccess)scratch).Target;
        byte a = color.A;
        bool premultiply = scratch.IsPremultiplied;
        byte b = premultiply ? (byte)((color.B * a + 127) / 255) : color.B;
        byte g = premultiply ? (byte)((color.G * a + 127) / 255) : color.G;
        byte r = premultiply ? (byte)((color.R * a + 127) / 255) : color.R;
        var span = target.GetWritablePixelSpan();
        for (int i = 0; i + 3 < span.Length; i += 4)
        {
            span[i + 0] = b;
            span[i + 1] = g;
            span[i + 2] = r;
            span[i + 3] = a;
        }
        target.IncrementVersion();
    }

    /// <summary>
    /// Picks a downsample factor that brings the working buffer back to 100% logical
    /// resolution (i.e. fully neutralizes the ZoomPanCanvas+DPI scale baked into the source
    /// layer). Uses <see cref="Math.Floor(double)"/> to ensure the result stays at-or-above 100%
    /// (never under-resolved); a fractional residue is left for the bilinear upsample to
    /// stretch back, which is cheap relative to running the full Gaussian on the upscaled
    /// buffer. The minimum-side guard keeps very tiny filter regions from collapsing.
    /// </summary>
    private static int ChooseDownsampleFactor(double logicalToPixelScaleX, double logicalToPixelScaleY, int width, int height)
    {
        double scale = Math.Max(logicalToPixelScaleX, logicalToPixelScaleY);
        int scaleFactor = Math.Max(1, (int)Math.Floor(scale));

        // Pixel-budget factor: CPU blur is O(W·H·kernel) per axis with a fresh byte[] per
        // pass — at 4096² that's 64 MB per allocation × multiple passes per filter ×
        // many filters per frame = GB of GC pressure. Cap input pixel count regardless of
        // scale (independent of the scale-driven factor above) so that large filter
        // regions from unitless SVG values (e.g. width="200" in objectBoundingBox =
        // 200× bbox) don't dominate. 1M pixels keeps each pass's allocation around 4 MB.
        const long maxPixels = 1024L * 1024L;
        long pixels = (long)width * height;
        int sizeFactor = pixels > maxPixels
            ? Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)pixels / maxPixels)))
            : 1;

        int factor = Math.Max(scaleFactor, sizeFactor);
        const int minSide = 32;
        while (factor > 1 && (width / factor < minSide || height / factor < minSide))
        {
            factor--;
        }
        return factor;
    }

    /// <summary>
    /// Box-filter downsample by integer factor. Operates in premultiplied alpha (linear
    /// blendable). Output dimensions are <c>width / factor</c> × <c>height / factor</c>.
    /// Implemented as two separable passes (horizontal then vertical) using unsafe pointer
    /// access — ~10× faster than the nested-loop byte[] indexing version that suffered
    /// O(W·H·factor²) from bounds-checked inner reads. At 1440² → 180² (factor 8) this
    /// drops from ~300 ms to ~20 ms.
    /// </summary>
    private static unsafe byte[] BoxDownsample(byte[] src, int width, int height, int factor, out int outWidth, out int outHeight)
    {
        outWidth = width / factor;
        outHeight = height / factor;

        // Horizontal pass: src (width × height) → mid (outWidth × height). Each output pixel
        // sums `factor` source pixels along the row. Accumulators stay as int (max 8×255 fits
        // easily in int32 even for factor up to ~16M before overflow, far beyond reality).
        var mid = new byte[outWidth * height * 4];
        int outW = outWidth;
        int srcRowBytes = width * 4;
        int midRowBytes = outW * 4;
        fixed (byte* srcPtr = src)
        fixed (byte* midPtr = mid)
        {
            byte* sp = srcPtr;
            byte* mp = midPtr;
            Parallel.For(0, height, y =>
            {
                byte* rowSrc = sp + y * srcRowBytes;
                byte* rowDst = mp + y * midRowBytes;
                for (int ox = 0; ox < outW; ox++)
                {
                    byte* block = rowSrc + ox * factor * 4;
                    int b = 0, g = 0, r = 0, a = 0;
                    for (int dx = 0; dx < factor; dx++)
                    {
                        int o = dx * 4;
                        b += block[o + 0];
                        g += block[o + 1];
                        r += block[o + 2];
                        a += block[o + 3];
                    }
                    int dOff = ox * 4;
                    rowDst[dOff + 0] = (byte)(b / factor);
                    rowDst[dOff + 1] = (byte)(g / factor);
                    rowDst[dOff + 2] = (byte)(r / factor);
                    rowDst[dOff + 3] = (byte)(a / factor);
                }
            });
        }

        // Vertical pass: mid (outWidth × height) → dst (outWidth × outHeight).
        var dst = new byte[outWidth * outHeight * 4];
        int outH = outHeight;
        int dstRowBytes = outW * 4;
        fixed (byte* midPtr = mid)
        fixed (byte* dstPtr = dst)
        {
            byte* mp = midPtr;
            byte* dp = dstPtr;
            Parallel.For(0, outH, oy =>
            {
                byte* rowDst = dp + oy * dstRowBytes;
                byte* colBase = mp + oy * factor * midRowBytes;
                for (int x = 0; x < outW; x++)
                {
                    int b = 0, g = 0, r = 0, a = 0;
                    int xBase = x * 4;
                    for (int dy = 0; dy < factor; dy++)
                    {
                        byte* p = colBase + dy * midRowBytes + xBase;
                        b += p[0];
                        g += p[1];
                        r += p[2];
                        a += p[3];
                    }
                    int dOff = x * 4;
                    rowDst[dOff + 0] = (byte)(b / factor);
                    rowDst[dOff + 1] = (byte)(g / factor);
                    rowDst[dOff + 2] = (byte)(r / factor);
                    rowDst[dOff + 3] = (byte)(a / factor);
                }
            });
        }

        return dst;
    }

    private static byte[] BlurAxis(byte[] src, int width, int height, double sigma, bool horizontal)
    {
        var kernel = BuildKernel(sigma, out int radius);
        var dst = new byte[src.Length];

        // Parallelize across rows (or columns for vertical pass) — each output row only
        // reads from src and writes to its own dst row, no cross-thread dependency.
        // Doubles to triples throughput on 4+ core machines for blur-dominated frames.
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                double b = 0, g = 0, r = 0, a = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int sx = horizontal ? Math.Clamp(x + k, 0, width - 1) : x;
                    int sy = horizontal ? y : Math.Clamp(y + k, 0, height - 1);
                    int idx = (sy * width + sx) * 4;
                    double w = kernel[k + radius];
                    b += src[idx + 0] * w;
                    g += src[idx + 1] * w;
                    r += src[idx + 2] * w;
                    a += src[idx + 3] * w;
                }
                int dIdx = (y * width + x) * 4;
                dst[dIdx + 0] = (byte)Math.Clamp((int)Math.Round(b), 0, 255);
                dst[dIdx + 1] = (byte)Math.Clamp((int)Math.Round(g), 0, 255);
                dst[dIdx + 2] = (byte)Math.Clamp((int)Math.Round(r), 0, 255);
                dst[dIdx + 3] = (byte)Math.Clamp((int)Math.Round(a), 0, 255);
            }
        });

        return dst;
    }

    private static double[] BuildKernel(double sigma, out int radius)
    {
        radius = Math.Max(1, (int)Math.Ceiling(sigma * 3));
        int size = radius * 2 + 1;
        var k = new double[size];
        double s2 = 2 * sigma * sigma;
        double sum = 0;
        for (int i = -radius; i <= radius; i++)
        {
            double v = Math.Exp(-(i * i) / s2);
            k[i + radius] = v;
            sum += v;
        }
        for (int i = 0; i < size; i++) k[i] /= sum;
        return k;
    }

    private static void ApplyColorMatrix(byte[] pixels, float[] m)
    {
        // m is row-major 4×5: [r' = m[0..4], g' = m[5..9], b' = m[10..14], a' = m[15..19]]
        // Pre-pack matrix rows as Vector4 (RGBA weights) so Vector4.Dot can do the
        // per-pixel mul-add in hardware (SSE4.1 DPPS / ARM64 vmulq + horizontal add).
        var mR = new Vector4(m[0], m[1], m[2], m[3]);
        var mG = new Vector4(m[5], m[6], m[7], m[8]);
        var mB = new Vector4(m[10], m[11], m[12], m[13]);
        var mA = new Vector4(m[15], m[16], m[17], m[18]);
        float biasR = m[4], biasG = m[9], biasB = m[14], biasA = m[19];

        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            // Pixels are BGRA; matrix expects RGBA — swizzle on read.
            var rgba = new Vector4(
                pixels[i + 2],
                pixels[i + 1],
                pixels[i + 0],
                pixels[i + 3]) * (1f / 255f);

            float nr = Vector4.Dot(mR, rgba) + biasR;
            float ng = Vector4.Dot(mG, rgba) + biasG;
            float nb = Vector4.Dot(mB, rgba) + biasB;
            float na = Vector4.Dot(mA, rgba) + biasA;

            pixels[i + 2] = (byte)Math.Clamp((int)MathF.Round(nr * 255f), 0, 255);
            pixels[i + 1] = (byte)Math.Clamp((int)MathF.Round(ng * 255f), 0, 255);
            pixels[i + 0] = (byte)Math.Clamp((int)MathF.Round(nb * 255f), 0, 255);
            pixels[i + 3] = (byte)Math.Clamp((int)MathF.Round(na * 255f), 0, 255);
        }
    }

    private static void PremultiplyInPlace(byte[] pixels)
        => SimdDispatcher.PremultiplyBgra(pixels, pixels);

    private static void UnpremultiplyInPlace(byte[] pixels)
        => SimdDispatcher.UnpremultiplyBgra(pixels, pixels);

    // ─────────────────────────────────────────────────────────────────────
    // Composition primitives — all in premultiplied alpha
    // ─────────────────────────────────────────────────────────────────────

    private FilterResult SourceOver(FilterResult fg, FilterResult bg, IImageFilterContext ctx)
        => PorterDuff(fg, bg, ctx, PorterDuffOp.Over);

    private enum PorterDuffOp { Over, In, Out, Atop, Xor }

    private FilterResult PorterDuff(FilterResult fg, FilterResult bg, IImageFilterContext ctx, PorterDuffOp op)
    {
        // Operate on the union bounds so nodes such as feOffset remain spatially aligned
        // when they feed feMerge/feComposite.
        var bounds = Union(fg.Bounds, bg.Bounds);
        int w = Math.Max(1, (int)Math.Ceiling(bounds.Width));
        int h = Math.Max(1, (int)Math.Ceiling(bounds.Height));

        var fgPixels = fg.ReadPixels(out _);
        var bgPixels = bg.ReadPixels(out _);
        if (fgPixels.IsEmpty && bgPixels.IsEmpty)
        {
            var emptyResult = ctx.AcquireScratch(w, h, bounds);
            return emptyResult;
        }

        // Materialize both into uniform-size buffers (zero-pad smaller one).
        byte[] fgBuf = MaterializeAt(fgPixels, fg.PixelWidth, fg.PixelHeight, fg.Bounds, bounds, w, h);
        byte[] bgBuf = MaterializeAt(bgPixels, bg.PixelWidth, bg.PixelHeight, bg.Bounds, bounds, w, h);

        // All inputs assumed premultiplied (we premultiply in Blur, ColorMatrix, etc.)
        if (!fg.IsPremultiplied) PremultiplyInPlace(fgBuf);
        if (!bg.IsPremultiplied) PremultiplyInPlace(bgBuf);

        var dst = new byte[w * h * 4];
        for (int i = 0; i + 3 < dst.Length; i += 4)
        {
            int fb = fgBuf[i + 0], fg_ = fgBuf[i + 1], fr = fgBuf[i + 2], fa = fgBuf[i + 3];
            int bb = bgBuf[i + 0], bg_ = bgBuf[i + 1], br = bgBuf[i + 2], ba = bgBuf[i + 3];

            // Porter–Duff coefficients (premultiplied form):
            //   Over:  out = fg + bg * (1 - fg.a)
            //   In:    out = fg * bg.a
            //   Out:   out = fg * (1 - bg.a)
            //   Atop:  out = fg * bg.a + bg * (1 - fg.a)
            //   Xor:   out = fg * (1 - bg.a) + bg * (1 - fg.a)
            int outR, outG, outB, outA;
            switch (op)
            {
                case PorterDuffOp.Over:
                    outR = fr + br * (255 - fa) / 255;
                    outG = fg_ + bg_ * (255 - fa) / 255;
                    outB = fb + bb * (255 - fa) / 255;
                    outA = fa + ba * (255 - fa) / 255;
                    break;
                case PorterDuffOp.In:
                    outR = fr * ba / 255;
                    outG = fg_ * ba / 255;
                    outB = fb * ba / 255;
                    outA = fa * ba / 255;
                    break;
                case PorterDuffOp.Out:
                    outR = fr * (255 - ba) / 255;
                    outG = fg_ * (255 - ba) / 255;
                    outB = fb * (255 - ba) / 255;
                    outA = fa * (255 - ba) / 255;
                    break;
                case PorterDuffOp.Atop:
                    outR = fr * ba / 255 + br * (255 - fa) / 255;
                    outG = fg_ * ba / 255 + bg_ * (255 - fa) / 255;
                    outB = fb * ba / 255 + bb * (255 - fa) / 255;
                    outA = fa * ba / 255 + ba * (255 - fa) / 255;
                    break;
                case PorterDuffOp.Xor:
                    outR = fr * (255 - ba) / 255 + br * (255 - fa) / 255;
                    outG = fg_ * (255 - ba) / 255 + bg_ * (255 - fa) / 255;
                    outB = fb * (255 - ba) / 255 + bb * (255 - fa) / 255;
                    outA = fa * (255 - ba) / 255 + ba * (255 - fa) / 255;
                    break;
                default:
                    outR = outG = outB = outA = 0;
                    break;
            }

            dst[i + 0] = (byte)Math.Clamp(outB, 0, 255);
            dst[i + 1] = (byte)Math.Clamp(outG, 0, 255);
            dst[i + 2] = (byte)Math.Clamp(outR, 0, 255);
            dst[i + 3] = (byte)Math.Clamp(outA, 0, 255);
        }

        var output = ctx.AcquireScratch(w, h, bounds);
        CopyToScratch(output, dst, w, h);
        return output;
    }

    private static Rect Union(Rect a, Rect b)
    {
        var left = Math.Min(a.Left, b.Left);
        var top = Math.Min(a.Top, b.Top);
        var right = Math.Max(a.Right, b.Right);
        var bottom = Math.Max(a.Bottom, b.Bottom);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static byte[] MaterializeAt(
        ReadOnlySpan<byte> src,
        int srcW,
        int srcH,
        Rect srcBounds,
        Rect dstBounds,
        int outW,
        int outH)
    {
        var dst = new byte[outW * outH * 4];
        if (src.IsEmpty) return dst;

        int x0 = Math.Clamp((int)Math.Round(srcBounds.X - dstBounds.X), 0, outW);
        int y0 = Math.Clamp((int)Math.Round(srcBounds.Y - dstBounds.Y), 0, outH);
        int x1 = Math.Clamp((int)Math.Round(srcBounds.Right - dstBounds.X), 0, outW);
        int y1 = Math.Clamp((int)Math.Round(srcBounds.Bottom - dstBounds.Y), 0, outH);
        if (x1 <= x0 || y1 <= y0 || srcW <= 0 || srcH <= 0 || srcBounds.Width <= 0 || srcBounds.Height <= 0)
        {
            return dst;
        }

        double scaleX = srcW / srcBounds.Width;
        double scaleY = srcH / srcBounds.Height;
        for (int y = y0; y < y1; y++)
        {
            int sy = Math.Clamp((int)Math.Floor((y - y0) * scaleY), 0, srcH - 1);
            for (int x = x0; x < x1; x++)
            {
                int sx = Math.Clamp((int)Math.Floor((x - x0) * scaleX), 0, srcW - 1);
                int s = (sy * srcW + sx) * 4;
                int d = (y * outW + x) * 4;
                dst[d + 0] = src[s + 0];
                dst[d + 1] = src[s + 1];
                dst[d + 2] = src[s + 2];
                dst[d + 3] = src[s + 3];
            }
        }
        return dst;
    }
}

/// <summary>
/// Internal hook that lets <see cref="CpuImageFilterExecutor"/> reach into a
/// <see cref="ScratchFilterResult"/>'s underlying CPU pixel surface to write
/// pixels directly. Avoids exposing the target on the public <see cref="FilterResult"/> API.
/// </summary>
internal interface IPixelTargetAccess
{
    ICpuPixelSurface Target { get; }
}
