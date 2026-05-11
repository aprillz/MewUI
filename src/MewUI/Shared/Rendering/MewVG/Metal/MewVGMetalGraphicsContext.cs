using System.Runtime.CompilerServices;

using Aprillz.MewUI.Rendering.CoreText;
using Aprillz.MewVG;
using Aprillz.MewVG.Interop;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed partial class MewVGMetalGraphicsContext
{
    private static readonly nint ClsMTLRenderPassDescriptor = ObjCRuntime.GetClass("MTLRenderPassDescriptor");

    private static readonly nint SelAlloc = ObjCRuntime.Selectors.alloc;
    private static readonly nint SelInit = ObjCRuntime.Selectors.init;
    private static readonly nint SelRelease = ObjCRuntime.Selectors.release;
    private static readonly nint SelRetain = ObjCRuntime.RegisterSelector("retain");

    private static readonly nint SelNextDrawable = ObjCRuntime.RegisterSelector("nextDrawable");
    private static readonly nint SelTexture = ObjCRuntime.RegisterSelector("texture");
    private static readonly nint SelCommandBuffer = ObjCRuntime.RegisterSelector("commandBuffer");
    private static readonly nint SelRenderCommandEncoderWithDescriptor = ObjCRuntime.RegisterSelector("renderCommandEncoderWithDescriptor:");
    private static readonly nint SelEndEncoding = ObjCRuntime.RegisterSelector("endEncoding");
    private static readonly nint SelCommit = ObjCRuntime.RegisterSelector("commit");
    private static readonly nint SelWaitUntilScheduled = ObjCRuntime.RegisterSelector("waitUntilScheduled");
    private static readonly nint SelPresent = ObjCRuntime.RegisterSelector("present");
    private static readonly nint SelRenderPassDescriptor = ObjCRuntime.RegisterSelector("renderPassDescriptor");
    private static readonly nint SelColorAttachments = ObjCRuntime.RegisterSelector("colorAttachments");
    private static readonly nint SelObjectAtIndexedSubscript = ObjCRuntime.RegisterSelector("objectAtIndexedSubscript:");
    private static readonly nint SelSetTexture = ObjCRuntime.RegisterSelector("setTexture:");
    private static readonly nint SelSetLoadAction = ObjCRuntime.RegisterSelector("setLoadAction:");
    private static readonly nint SelSetStoreAction = ObjCRuntime.RegisterSelector("setStoreAction:");
    private static readonly nint SelSetClearColor = ObjCRuntime.RegisterSelector("setClearColor:");
    private static readonly nint SelStencilAttachment = ObjCRuntime.RegisterSelector("stencilAttachment");
    private static readonly nint SelSetClearStencil = ObjCRuntime.RegisterSelector("setClearStencil:");
    private static readonly nint SelDepthAttachment = ObjCRuntime.RegisterSelector("depthAttachment");
    private static readonly nint SelSetClearDepth = ObjCRuntime.RegisterSelector("setClearDepth:");
    private static readonly nint SelSetResolveTexture = Metal.Sel.SetResolveTexture;
    private static readonly nint ClsNSAutoreleasePool = ObjCRuntime.GetClass("NSAutoreleasePool");
    private static readonly nint SelWaitUntilCompleted = ObjCRuntime.RegisterSelector("waitUntilCompleted");

    private readonly IMetalFrameSession _frameSession;
    private readonly MewVGMetalTextCache _textCache;

    private nint _drawable;
    private nint _commandBuffer;
    private nint _encoder;
    private bool _beganFrame;
    private nint _framePool; // Frame-spanning autorelease pool: created in BeginFrame, drained in EndFrame.

    private MewVGMetalGraphicsContext(IMetalFrameSession frameSession)
    {
        _frameSession = frameSession;
        _vg = frameSession.Vg;
        _textCache = frameSession.TextCache;
    }

    internal static MewVGMetalGraphicsContext CreateForWindow(
        MewVGMetalWindowResources resources,
        MewVGMetalOffscreenSurfaceProvider offscreenProvider)
        => new(new MetalWindowFrameSession(resources, offscreenProvider));

    /// <summary>
    /// Builds a graphics context that renders into <paramref name="target"/>'s
    /// GPU MTLTexture instead of a window's <c>CAMetalLayer</c> drawable. The
    /// supplied <paramref name="offscreen"/> surface must have been acquired
    /// via <see cref="MewVGMetalOffscreenSurfaceProvider.AcquireSurface"/>; the
    /// context takes ownership and returns it to the pool on Dispose.
    /// </summary>
    internal static MewVGMetalGraphicsContext CreateForOffscreen(
        MewVGMetalOffscreenSurface offscreen,
        MewVGMetalPixelRenderSurface target,
        MewVGMetalOffscreenSurfaceProvider offscreenProvider)
        => new(new MetalOffscreenFrameSession(offscreen, target, offscreenProvider));

    partial void DestroyPlatform()
    {
        _frameSession.DisposeContext(this);
    }

    partial void BeginFramePlatform()
    {
        // Drain any lingering pool from a previous frame, then create a fresh
        // frame-spanning pool.  This pool lives until EndFramePlatform so that
        // *all* autoreleased ObjC objects created during rendering (by Metal
        // framework internals, NanoVG, or text rasterisation) are drained at
        // the end of each frame instead of accumulating until the outer
        // run-loop pool drains.
        DrainFramePool();
        _framePool = CreateFramePool();

        _drawable = 0;
        _commandBuffer = 0;
        _encoder = 0;
        _beganFrame = false;

        if (!_frameSession.TryBeginFrame(_viewportWidthPx, _viewportHeightPx, out var frame))
        {
            return;
        }

        _drawable = frame.Drawable;

        nint cmdBuf = ObjCRuntime.SendMessage(frame.CommandQueue, SelCommandBuffer);
        if (cmdBuf == 0)
        {
            return;
        }

        RetainIfNotNull(cmdBuf);
        _commandBuffer = cmdBuf;

        // Ensure the NanoVG coverage-AA scratch attachment matches the viewport.
        // The texture is owned by the renderer (single instance, resized on demand)
        // and is attached as color[1] of the main render pass so transparent
        // strokes/fills can write coverage and composite within one encoder.
        nint coverageTexture = _vg.EnsureCoverageTexture(_viewportWidthPx, _viewportHeightPx);

        nint passDesc = CreateRenderPass(frame.ColorTexture, frame.StencilTexture, frame.MsaaColorTexture, coverageTexture);
        if (passDesc == 0)
        {
            return;
        }

        nint encoder = ObjCRuntime.SendMessage(_commandBuffer, SelRenderCommandEncoderWithDescriptor, passDesc);
        if (encoder == 0)
        {
            return;
        }

        RetainIfNotNull(encoder);
        _encoder = encoder;

        // Pass the render-pass attachment textures so the Metal renderer can rebuild
        // an equivalent encoder after a coverage-AA detour (transparent stroke/fill
        // composite needs to switch to a coverage texture and back).
        _vg.SetRenderEncoder(_encoder, _commandBuffer,
            frame.ColorTexture, frame.StencilTexture, frame.MsaaColorTexture);
        _vg.BeginFrame((float)_viewportWidthDip, (float)_viewportHeightDip, (float)DpiScale);
        _vg.ResetTransform();
        _vg.ResetScissor();
        _beganFrame = true;
    }

    partial void EndFramePlatform()
    {
        bool didRender = false;

        try
        {
            if (_encoder != 0 && _commandBuffer != 0 && _beganFrame &&
                (_frameSession.IsOffscreen || _drawable != 0))
            {
                _vg.EndFrame();
                didRender = true;

                ObjCRuntime.SendMessageNoReturn(_encoder, SelEndEncoding);
                ObjCRuntime.SendMessageNoReturn(_commandBuffer, SelCommit);
                _frameSession.AfterCommit(_commandBuffer, _drawable);
            }
            else if (_encoder != 0)
            {
                ObjCRuntime.SendMessageNoReturn(_encoder, SelEndEncoding);
            }
        }
        finally
        {
            if (didRender)
            {
                try
                {
                    _vg.FrameCompleted();
                }
                catch
                {
                    // Best-effort: avoid deadlocking the internal frame semaphore if rendering fails mid-frame.
                }
            }

            ReleaseIfNotNull(_encoder);
            ReleaseIfNotNull(_commandBuffer);
            ReleaseIfNotNull(_drawable);
            _encoder = 0;
            _commandBuffer = 0;
            _drawable = 0;
            _beganFrame = false;

            // Main frame only: NVG.Flush has just consumed any draw calls
            // that referenced images disposed mid-frame (filter composites
            // and LRU-evicted text glyph atlases). Now it's safe to actually
            // delete their MTLTextures.
            _frameSession.ReleasePendingFrameResources();

            // Drain the frame-spanning autorelease pool.
            // This releases all autoreleased ObjC objects created since BeginFrame,
            // preventing accumulation when the run-loop pool doesn't drain between
            // high-frequency frame ticks (e.g. MaxFPS = 288).
            DrainFramePool();
        }
    }

    private readonly record struct MetalFrame(
        nint ColorTexture,
        nint StencilTexture,
        nint MsaaColorTexture,
        nint CommandQueue,
        nint Drawable);

    private interface IMetalFrameSession
    {
        NanoVGMetal Vg { get; }
        MewVGMetalTextCache TextCache { get; }
        bool IsOffscreen { get; }
        bool TryBeginFrame(int viewportWidthPx, int viewportHeightPx, out MetalFrame frame);
        void AfterCommit(nint commandBuffer, nint drawable);
        void ReleasePendingFrameResources();
        void DisposeContext(MewVGMetalGraphicsContext context);
    }

    private sealed class MetalWindowFrameSession : IMetalFrameSession
    {
        private readonly MewVGMetalWindowResources _resources;
        private readonly MewVGMetalOffscreenSurfaceProvider _offscreenProvider;

        public MetalWindowFrameSession(MewVGMetalWindowResources resources, MewVGMetalOffscreenSurfaceProvider offscreenProvider)
        {
            _resources = resources;
            _offscreenProvider = offscreenProvider;
        }

        public NanoVGMetal Vg => _resources.Vg;
        public MewVGMetalTextCache TextCache => _resources.TextCache;
        public bool IsOffscreen => false;

        public bool TryBeginFrame(int viewportWidthPx, int viewportHeightPx, out MetalFrame frame)
        {
            frame = default;

            nint drawable = ObjCRuntime.SendMessage(_resources.Layer, SelNextDrawable);
            if (drawable == 0)
            {
                return false;
            }

            RetainIfNotNull(drawable);

            nint colorTexture = ObjCRuntime.SendMessage(drawable, SelTexture);
            if (colorTexture == 0)
            {
                ReleaseIfNotNull(drawable);
                return false;
            }

            frame = new MetalFrame(
                colorTexture,
                _resources.EnsureStencilTexture(viewportWidthPx, viewportHeightPx),
                _resources.EnsureMsaaColorTexture(viewportWidthPx, viewportHeightPx),
                _resources.CommandQueue,
                drawable);
            return true;
        }

        public void AfterCommit(nint commandBuffer, nint drawable)
        {
            ObjCRuntime.SendMessageNoReturn(commandBuffer, SelWaitUntilScheduled);
            ObjCRuntime.SendMessageNoReturn(drawable, SelPresent);
        }

        public void ReleasePendingFrameResources()
        {
            // Per-NVG drain: only delete image-ids belonging to the window's NVG. Worker
            // offscreen NVG entries stay queued until the worker session's EndFrame.
            _offscreenProvider.ReleasePendingImagesForVg(_resources.Vg);
            TextCache.ReleasePendingDeletes();
        }

        public void DisposeContext(MewVGMetalGraphicsContext context)
            => _resources.InvalidateCachedContext(context);
    }

    private sealed class MetalOffscreenFrameSession : IMetalFrameSession
    {
        private readonly MewVGMetalOffscreenSurface _offscreen;
        private readonly MewVGMetalPixelRenderSurface _target;
        private readonly MewVGMetalOffscreenSurfaceProvider _offscreenProvider;

        public MetalOffscreenFrameSession(
            MewVGMetalOffscreenSurface offscreen,
            MewVGMetalPixelRenderSurface target,
            MewVGMetalOffscreenSurfaceProvider offscreenProvider)
        {
            _offscreen = offscreen;
            _target = target;
            _offscreenProvider = offscreenProvider;
        }

        public NanoVGMetal Vg => _offscreen.Vg;
        public MewVGMetalTextCache TextCache => _offscreen.TextCache;
        public bool IsOffscreen => true;

        public bool TryBeginFrame(int viewportWidthPx, int viewportHeightPx, out MetalFrame frame)
        {
            _target.EnsureGpuTextures(_offscreen.Device);
            frame = new MetalFrame(
                _target.ColorTexture,
                _target.StencilTexture,
                0,
                _offscreen.CommandQueue,
                0);
            return frame.ColorTexture != 0;
        }

        public void AfterCommit(nint commandBuffer, nint drawable)
        {
            // waitUntilCompleted is required: the rendered ColorTexture is consumed by the
            // filter executor's MPS on a DIFFERENT command queue (filter queue vs offscreen
            // surface queue). Cross-queue MTLTexture access without explicit sync races —
            // MPS reads pre-render content. NVG's outer DrawImage consumer is also a fresh
            // commandBuffer (typically window queue) — same cross-queue issue.
            //
            // What we DO defer is the much heavier MTLTexture → CPU getBytes (32 MB per
            // 4096 × 2000 RT). CPU consumers (Lock / CopyPixels / GetPixelSpan) trigger it
            // via FlushPendingReadbackIfNeeded; the pure-GPU consumer chain skips it.
            ObjCRuntime.SendMessageNoReturn(commandBuffer, SelWaitUntilCompleted);
            _target.RequestDeferredReadback(commandBuffer);
        }

        public void ReleasePendingFrameResources()
        {
            // Per-NVG drain for the offscreen NVG that just rendered. We're inside its
            // EndFrame on the thread that owns it — only safe time to call DeleteImage on
            // this NVG without racing the window NVG mid-frame on another thread.
            _offscreenProvider.ReleasePendingImagesForVg(_offscreen.Vg);
        }

        public void DisposeContext(MewVGMetalGraphicsContext context)
            => _offscreenProvider.ReturnSurface(_offscreen);
    }

    private static nint CreateRenderPass(nint drawableTexture, nint stencilTexture, nint msaaColorTexture, nint coverageTexture)
    {
        if (ClsMTLRenderPassDescriptor == 0 || SelRenderPassDescriptor == 0)
        {
            return 0;
        }

        nint passDesc = ObjCRuntime.SendMessage(ClsMTLRenderPassDescriptor, SelRenderPassDescriptor);
        if (passDesc == 0)
        {
            return 0;
        }

        bool msaa = msaaColorTexture != 0;

        // colorAttachments[0]
        nint colorAttachments = ObjCRuntime.SendMessage(passDesc, SelColorAttachments);
        nint color0 = colorAttachments != 0 ? ObjCRuntime.SendMessage(colorAttachments, SelObjectAtIndexedSubscript, (UInt64)0) : 0;
        if (color0 != 0)
        {
            if (msaa)
            {
                // Render into the MSAA texture, resolve to the drawable.
                ObjCRuntime.SendMessageNoReturn(color0, SelSetTexture, msaaColorTexture);
                ObjCRuntime.SendMessageNoReturn(color0, SelSetLoadAction, (UInt64)MTLLoadAction.Clear);
                ObjCRuntime.SendMessageNoReturn(color0, SelSetStoreAction, (UInt64)MTLStoreAction.MultisampleResolve);
                if (SelSetResolveTexture != 0)
                {
                    ObjCRuntime.SendMessageNoReturn(color0, SelSetResolveTexture, drawableTexture);
                }
            }
            else
            {
                ObjCRuntime.SendMessageNoReturn(color0, SelSetTexture, drawableTexture);
                ObjCRuntime.SendMessageNoReturn(color0, SelSetLoadAction, (UInt64)MTLLoadAction.Clear);
                ObjCRuntime.SendMessageNoReturn(color0, SelSetStoreAction, (UInt64)MTLStoreAction.Store);
            }

            ObjCRuntime.SendMessageNoReturn(color0, SelSetClearColor, new MTLClearColor(0, 0, 0, 0));
        }

        // colorAttachments[1] — coverage AA scratch (alpha8 / R8). Present so
        // transparent stroke / fill calls can build coverage and composite within
        // the same render encoder via FB fetch (no tile flush). Memoryless on
        // Apple Silicon: never lands in DRAM. Cleared per frame so subsequent
        // strokes on the same frame start from 0.
        if (coverageTexture != 0)
        {
            nint color1 = colorAttachments != 0 ? ObjCRuntime.SendMessage(colorAttachments, SelObjectAtIndexedSubscript, (UInt64)1) : 0;
            if (color1 != 0)
            {
                ObjCRuntime.SendMessageNoReturn(color1, SelSetTexture, coverageTexture);
                ObjCRuntime.SendMessageNoReturn(color1, SelSetLoadAction, (UInt64)MTLLoadAction.Clear);
                ObjCRuntime.SendMessageNoReturn(color1, SelSetStoreAction, (UInt64)MTLStoreAction.DontCare);
                ObjCRuntime.SendMessageNoReturn(color1, SelSetClearColor, new MTLClearColor(0, 0, 0, 0));
            }
        }

        if (stencilTexture != 0)
        {
            // When using a depth-stencil format (e.g. Depth32Float_Stencil8), bind the same texture to both.
            if (SelDepthAttachment != 0)
            {
                nint depth = ObjCRuntime.SendMessage(passDesc, SelDepthAttachment);
                if (depth != 0)
                {
                    ObjCRuntime.SendMessageNoReturn(depth, SelSetTexture, stencilTexture);
                    ObjCRuntime.SendMessageNoReturn(depth, SelSetLoadAction, (UInt64)MTLLoadAction.Clear);
                    ObjCRuntime.SendMessageNoReturn(depth, SelSetStoreAction, (UInt64)MTLStoreAction.DontCare);
                    if (SelSetClearDepth != 0)
                    {
                        ObjCRuntime.SendMessageNoReturn(depth, SelSetClearDepth, 1.0);
                    }
                }
            }

            if (SelStencilAttachment != 0)
            {
                nint stencil = ObjCRuntime.SendMessage(passDesc, SelStencilAttachment);
                if (stencil != 0)
                {
                    ObjCRuntime.SendMessageNoReturn(stencil, SelSetTexture, stencilTexture);
                    ObjCRuntime.SendMessageNoReturn(stencil, SelSetLoadAction, (UInt64)MTLLoadAction.Clear);
                    ObjCRuntime.SendMessageNoReturn(stencil, SelSetStoreAction, (UInt64)MTLStoreAction.DontCare);
                    // Ensure a known clear value so clip tests behave deterministically.
                    if (SelSetClearStencil != 0)
                    {
                        ObjCRuntime.SendMessageNoReturn(stencil, SelSetClearStencil, (UInt64)0);
                    }
                }
            }
        }

        return passDesc;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void RetainIfNotNull(nint obj)
    {
        if (obj == 0 || SelRetain == 0)
        {
            return;
        }

        _ = ObjCRuntime.SendMessage(obj, SelRetain);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReleaseIfNotNull(nint obj)
    {
        if (obj == 0 || SelRelease == 0)
        {
            return;
        }

        ObjCRuntime.SendMessageNoReturn(obj, SelRelease);
    }

    #region Text Rendering

    protected override void DrawTextCore(ReadOnlySpan<char> text, Rect bounds, IFont font, Color color,
        TextAlignment horizontalAlignment = TextAlignment.Left,
        TextAlignment verticalAlignment = TextAlignment.Top,
        TextWrapping wrapping = TextWrapping.NoWrap,
        TextTrimming trimming = TextTrimming.None)
    {
        if (text.IsEmpty)
        {
            return;
        }

        if (font is not CoreTextFont ct)
        {
            return;
        }

        // Compute raster extent.
        Size measured;
        if (wrapping == TextWrapping.NoWrap)
        {
            measured = MeasureText(text, font);
        }
        else
        {
            double maxWidth = bounds.Width > 0 ? bounds.Width : MeasureText(text, font).Width;
            measured = MeasureText(text, font, maxWidth);
        }

        double targetWidthDip = measured.Width;
        if (bounds.Width > 0 && !double.IsInfinity(bounds.Width) && !double.IsNaN(bounds.Width))
        {
            if (wrapping != TextWrapping.NoWrap)
            {
                // For wrapped text, use bounds width so Rasterize wraps at the same width
                // as Measure. Using the narrower measured.Width would cause different line breaks.
                targetWidthDip = bounds.Width;
            }
            else if (trimming != TextTrimming.None)
            {
                targetWidthDip = Math.Min(targetWidthDip, bounds.Width);
            }
        }

        int widthPx = Math.Max(1, LayoutRounding.CeilToPixelInt(targetWidthDip, DpiScale));

        double targetHeightDip = measured.Height;
        if (bounds.Height > 0 && !double.IsInfinity(bounds.Height) && !double.IsNaN(bounds.Height))
        {
            targetHeightDip = Math.Min(targetHeightDip, bounds.Height);
        }
        int heightPx = Math.Max(1, LayoutRounding.CeilToPixelInt(targetHeightDip, DpiScale));

        // Early clip cull: skip text entirely outside the current scissor region.
        if (_clipBoundsWorld.HasValue)
        {
            var c = _clipBoundsWorld.Value;
            double worldLeft = bounds.X + _transform.M31;
            double worldTop = bounds.Y + _transform.M32;
            double worldRight = worldLeft + widthPx / DpiScale;
            double worldBottom = worldTop + heightPx / DpiScale;
            if (worldRight <= c.X || worldLeft >= c.Right || worldBottom <= c.Y || worldTop >= c.Bottom)
            {
                return;
            }
        }

        double drawX = bounds.X;
        double drawY = bounds.Y;
        double widthDip = widthPx / DpiScale;
        double heightDip = heightPx / DpiScale;

        if (bounds.Width > 0)
        {
            drawX = horizontalAlignment switch
            {
                TextAlignment.Center => bounds.X + (bounds.Width - widthDip) / 2.0,
                TextAlignment.Right => bounds.X + (bounds.Width - widthDip),
                _ => bounds.X
            };
        }

        if (bounds.Height > 0)
        {
            drawY = verticalAlignment switch
            {
                TextAlignment.Center => bounds.Y + (bounds.Height - heightDip) / 2.0,
                TextAlignment.Bottom => bounds.Y + (bounds.Height - heightDip),
                _ => bounds.Y
            };
        }

        // Text is rasterized into a bitmap texture; snap placement to device pixels to avoid sampling blur
        // when bounds introduce fractional DIP coordinates (common during layout and live resize).
        // Skip snapping during transitions to avoid visible jumping.
        if (_textPixelSnap)
        {
            drawX = LayoutRounding.RoundToPixel(drawX, DpiScale);
            drawY = LayoutRounding.RoundToPixel(drawY, DpiScale);
        }

        if (!_textCache.TryGetOrCreate(
                ct,
                text,
                widthPx,
                heightPx,
                (uint)Math.Round(DpiScale * 96.0),
                color,
                horizontalAlignment,
                TextAlignment.Top,
                wrapping,
                trimming,
                out int imageId,
                out int bitmapWidthPx,
                out int bitmapHeightPx))
        {
            return;
        }

        // The bitmap may be wider than widthPx (Rasterize adds AA margin for right/center alignment).
        // Use the actual bitmap dimensions for the image pattern so texels map 1:1 to device pixels.
        double bitmapWidthDip = bitmapWidthPx / DpiScale;
        double bitmapHeightDip = bitmapHeightPx / DpiScale;
        var paint = _vg.ImagePattern((float)drawX, (float)drawY, (float)bitmapWidthDip, (float)bitmapHeightDip, 0, imageId, 1.0f);

        // Clip the drawn rect to bounds so text doesn't visually overflow.
        // The ImagePattern is anchored at (drawX, drawY) in absolute coords, so we can
        // clip the fill rect on any side without shifting the texture.
        double rectX = drawX;
        double rectY = drawY;
        double rectW = Math.Max(widthDip, Math.Min(bitmapWidthDip, widthDip + (bitmapWidthDip - widthDip)));
        double rectH = heightDip;
        double snapTolerance = 2.0 / DpiScale;
        if (bounds.Width > 0 && !double.IsInfinity(bounds.Width))
        {
            // Left clip: text extends left of bounds (e.g. right-aligned overflow).
            if (rectX < bounds.X)
            {
                double leftClip = bounds.X - rectX;
                rectW -= leftClip;
                rectX = bounds.X;
            }

            // Right clip: text extends right of bounds.
            double maxRight = bounds.X + bounds.Width;
            if (rectX + rectW > maxRight + snapTolerance)
            {
                rectW = maxRight - rectX;
            }
        }
        if (bounds.Height > 0 && !double.IsInfinity(bounds.Height))
        {
            double maxBottom = bounds.Y + bounds.Height;
            if (rectY + rectH > maxBottom)
            {
                rectH = maxBottom - rectY;
            }
        }

        _vg.BeginPath();
        _vg.Rect((float)rectX, (float)rectY, (float)rectW, (float)rectH);
        _vg.FillPaint(paint);
        _vg.Fill();
    }

    private Size MeasureTextCore(ReadOnlySpan<char> text, IFont font)
    {
        if (text.IsEmpty) return Size.Empty;
        if (font is not CoreTextFont ct) return new Size(text.Length * 8, 16);
        int maxWidthPx = 0;
        var sizePx = CoreTextText.Measure(ct, text, maxWidthPx, TextWrapping.NoWrap, (uint)Math.Round(DpiScale * 96.0));
        return new Size(sizePx.Width / DpiScale, sizePx.Height / DpiScale);
    }

    private Size MeasureTextCore(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        if (text.IsEmpty) return Size.Empty;
        if (font is not CoreTextFont ct) return new Size(text.Length * 8, 16);
        int maxWidthPx = maxWidth <= 0 ? 0 : Math.Max(1, LayoutRounding.CeilToPixelInt(maxWidth, DpiScale));
        var sizePx = CoreTextText.Measure(ct, text, maxWidthPx, TextWrapping.Wrap, (uint)Math.Round(DpiScale * 96.0));
        return new Size(sizePx.Width / DpiScale, sizePx.Height / DpiScale);
    }

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font)
        => MeasureTextCore(text, font);

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
        => MeasureTextCore(text, font, maxWidth);

    public override TextLayout CreateTextLayout(ReadOnlySpan<char> text,
        TextFormat format, in TextLayoutConstraints constraints)
    {
        var bounds = constraints.Bounds;
        var safeBounds = new Rect(bounds.X, bounds.Y,
            double.IsPositiveInfinity(bounds.Width) ? 0 : bounds.Width,
            double.IsPositiveInfinity(bounds.Height) ? 0 : bounds.Height);

        Size measured;
        if (format.Wrapping == TextWrapping.NoWrap)
        {
            measured = MeasureTextCore(text, format.Font);
        }
        else
        {
            double maxWidth = safeBounds.Width > 0 ? safeBounds.Width : MeasureTextCore(text, format.Font).Width;
            measured = MeasureTextCore(text, format.Font, maxWidth);
        }

        double effectiveMaxWidth = safeBounds.Width > 0 ? safeBounds.Width : measured.Width;

        return new TextLayout
        {
            MeasuredSize = measured,
            EffectiveBounds = safeBounds,
            EffectiveMaxWidth = effectiveMaxWidth,
            ContentHeight = measured.Height
        };
    }

    public override void DrawTextLayout(ReadOnlySpan<char> text,
        TextFormat format, TextLayout layout, Color color)
        => DrawTextLayoutCore(text, format, layout, color, owner: null);

    public override void DrawTextLayout(ReadOnlySpan<char> text,
        TextFormat format, TextLayout layout, Color color, object? owner)
        => DrawTextLayoutCore(text, format, layout, color, owner);

    private void DrawTextLayoutCore(ReadOnlySpan<char> text,
        TextFormat format, TextLayout layout, Color color, object? owner)
    {
        if (text.IsEmpty) return;
        if (format.Font is not CoreTextFont ct) return;

        var bounds = layout.EffectiveBounds;

        double targetWidthDip = layout.MeasuredSize.Width;
        if (bounds.Width > 0 && !double.IsInfinity(bounds.Width) && !double.IsNaN(bounds.Width))
        {
            if (format.Wrapping != TextWrapping.NoWrap)
                targetWidthDip = bounds.Width;
            else if (format.Trimming != TextTrimming.None)
                targetWidthDip = Math.Min(targetWidthDip, bounds.Width);
        }

        int widthPx = Math.Max(1, LayoutRounding.CeilToPixelInt(targetWidthDip, DpiScale));

        double targetHeightDip = layout.MeasuredSize.Height;
        if (bounds.Height > 0 && !double.IsInfinity(bounds.Height) && !double.IsNaN(bounds.Height))
            targetHeightDip = Math.Min(targetHeightDip, bounds.Height);
        int heightPx = Math.Max(1, LayoutRounding.CeilToPixelInt(targetHeightDip, DpiScale));

        if (_clipBoundsWorld.HasValue)
        {
            var c = _clipBoundsWorld.Value;
            double worldLeft = bounds.X + _transform.M31;
            double worldTop = bounds.Y + _transform.M32;
            double worldRight = worldLeft + widthPx / DpiScale;
            double worldBottom = worldTop + heightPx / DpiScale;
            if (worldRight <= c.X || worldLeft >= c.Right || worldBottom <= c.Y || worldTop >= c.Bottom)
                return;
        }

        double drawX = bounds.X;
        double drawY = bounds.Y;
        double widthDip = widthPx / DpiScale;
        double heightDip = heightPx / DpiScale;

        if (bounds.Width > 0)
        {
            drawX = format.HorizontalAlignment switch
            {
                TextAlignment.Center => bounds.X + (bounds.Width - widthDip) / 2.0,
                TextAlignment.Right => bounds.X + (bounds.Width - widthDip),
                _ => bounds.X
            };
        }

        if (bounds.Height > 0)
        {
            drawY = format.VerticalAlignment switch
            {
                TextAlignment.Center => bounds.Y + (bounds.Height - heightDip) / 2.0,
                TextAlignment.Bottom => bounds.Y + (bounds.Height - heightDip),
                _ => bounds.Y
            };
        }

        if (_textPixelSnap)
        {
            drawX = LayoutRounding.RoundToPixel(drawX, DpiScale);
            drawY = LayoutRounding.RoundToPixel(drawY, DpiScale);
        }

        int imageId;
        int bitmapWidthPx;
        int bitmapHeightPx;
        bool ok = owner != null
            ? _textCache.TryGetOrCreateOwned(
                owner,
                ct,
                text,
                widthPx,
                heightPx,
                (uint)Math.Round(DpiScale * 96.0),
                color,
                format.HorizontalAlignment,
                TextAlignment.Top,
                format.Wrapping,
                format.Trimming,
                out imageId,
                out bitmapWidthPx,
                out bitmapHeightPx)
            : _textCache.TryGetOrCreate(
                ct,
                text,
                widthPx,
                heightPx,
                (uint)Math.Round(DpiScale * 96.0),
                color,
                format.HorizontalAlignment,
                TextAlignment.Top,
                format.Wrapping,
                format.Trimming,
                out imageId,
                out bitmapWidthPx,
                out bitmapHeightPx);
        if (!ok)
        {
            return;
        }

        double bitmapWidthDip = bitmapWidthPx / DpiScale;
        double bitmapHeightDip = bitmapHeightPx / DpiScale;
        var paint = _vg.ImagePattern((float)drawX, (float)drawY, (float)bitmapWidthDip, (float)bitmapHeightDip, 0, imageId, 1.0f);

        double rectX = drawX;
        double rectY = drawY;
        double rectW = Math.Max(widthDip, Math.Min(bitmapWidthDip, widthDip + (bitmapWidthDip - widthDip)));
        double rectH = heightDip;
        double snapTolerance = 2.0 / DpiScale;
        if (bounds.Width > 0 && !double.IsInfinity(bounds.Width))
        {
            if (rectX < bounds.X)
            {
                double leftClip = bounds.X - rectX;
                rectW -= leftClip;
                rectX = bounds.X;
            }
            double maxRight = bounds.X + bounds.Width;
            if (rectX + rectW > maxRight + snapTolerance)
                rectW = maxRight - rectX;
        }
        if (bounds.Height > 0 && !double.IsInfinity(bounds.Height))
        {
            double maxBottom = bounds.Y + bounds.Height;
            if (rectY + rectH > maxBottom)
                rectH = maxBottom - rectY;
        }

        _vg.BeginPath();
        _vg.Rect((float)rectX, (float)rectY, (float)rectW, (float)rectH);
        _vg.FillPaint(paint);
        _vg.Fill();
    }

    #endregion

    #region Image Rendering

    public override void DrawImage(IImage image, Point location)
        => DrawImageCore(image, new Rect(location.X, location.Y, image.PixelWidth / DpiScale, image.PixelHeight / DpiScale));

    protected override void DrawImageCore(IImage image, Rect destRect)
        => DrawImageCore(image, destRect, new Rect(0, 0, image.PixelWidth, image.PixelHeight));

    protected override void DrawImageCore(IImage image, Rect destRect, Rect sourceRect)
    {
        if (image is MewVGExternalLockedImage extImage)
        {
            EnsureExternalAcquired(extImage.Texture);
            int extImageId = extImage.GetOrCreateImageId(_vg, GetImageFlags());
            if (extImageId == 0) return;
            DrawImagePattern(extImageId, destRect, alpha: 1f, sourceRect: sourceRect,
                image.PixelWidth, image.PixelHeight);
            return;
        }

        if (image is not MewVGImage mew)
        {
            return;
        }

        int imageId = mew.GetOrCreateImageId(_vg, GetImageFlags());
        if (imageId == 0)
        {
            return;
        }

        DrawImagePattern(imageId, destRect, alpha: 1f, sourceRect: sourceRect, mew.PixelWidth, mew.PixelHeight);
    }

    #endregion

    private static nint CreateFramePool()
    {
        if (ClsNSAutoreleasePool == 0 || SelAlloc == 0 || SelInit == 0)
        {
            return 0;
        }

        nint pool = ObjCRuntime.SendMessage(ClsNSAutoreleasePool, SelAlloc);
        return pool != 0 ? ObjCRuntime.SendMessage(pool, SelInit) : 0;
    }

    private void DrainFramePool()
    {
        nint pool = _framePool;
        if (pool != 0)
        {
            _framePool = 0;
            ObjCRuntime.SendMessageNoReturn(pool, SelRelease);
        }
    }
}
