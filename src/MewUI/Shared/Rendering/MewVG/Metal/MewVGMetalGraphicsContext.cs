using System.Runtime.CompilerServices;

using Aprillz.MewUI.Rendering.CoreText;
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

    private readonly MewVGMetalWindowResources _resources;

    private readonly nint _drawable;
    private readonly nint _commandBuffer;
    private readonly nint _encoder;
    private readonly bool _beganFrame;

    public MewVGMetalGraphicsContext(
        nint hwnd,
        nint metalLayer,
        int pixelWidth,
        int pixelHeight,
        double dpiScale,
        MewVGMetalWindowResources resources)
    {
        _resources = resources;
        _vg = resources.Vg;

        DpiScale = dpiScale <= 0 ? 1.0 : dpiScale;
        _viewportWidthPx = Math.Max(1, pixelWidth);
        _viewportHeightPx = Math.Max(1, pixelHeight);
        _viewportWidthDip = _viewportWidthPx / DpiScale;
        _viewportHeightDip = _viewportHeightPx / DpiScale;

        using var pool = new AutoReleasePool();

        _drawable = ObjCRuntime.SendMessage(metalLayer, SelNextDrawable);
        if (_drawable == 0)
        {
            // Leave encoder/cmdBuffer null; caller will simply get a no-op frame.
            return;
        }

        RetainIfNotNull(_drawable);

        nint drawableTexture = ObjCRuntime.SendMessage(_drawable, SelTexture);
        if (drawableTexture == 0)
        {
            return;
        }

        _commandBuffer = ObjCRuntime.SendMessage(resources.CommandQueue, SelCommandBuffer);
        if (_commandBuffer == 0)
        {
            return;
        }

        RetainIfNotNull(_commandBuffer);

        nint stencilTex = resources.EnsureStencilTexture(_viewportWidthPx, _viewportHeightPx);
        nint msaaColorTex = resources.EnsureMsaaColorTexture(_viewportWidthPx, _viewportHeightPx);
        nint passDesc = CreateRenderPass(drawableTexture, stencilTex, msaaColorTex);
        if (passDesc == 0)
        {
            return;
        }

        _encoder = ObjCRuntime.SendMessage(_commandBuffer, SelRenderCommandEncoderWithDescriptor, passDesc);
        if (_encoder == 0)
        {
            return;
        }

        RetainIfNotNull(_encoder);

        _vg.SetRenderEncoder(_encoder, _commandBuffer);
        _vg.BeginFrame((float)_viewportWidthDip, (float)_viewportHeightDip, (float)DpiScale);
        _vg.ResetTransform();
        _vg.ResetScissor();
        _beganFrame = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        using var pool = new AutoReleasePool();
        bool signalFrameCompleted = _beganFrame;

        try
        {
            if (_encoder != 0 && _commandBuffer != 0 && _drawable != 0 && _beganFrame)
            {
                _vg.EndFrame();

                ObjCRuntime.SendMessageNoReturn(_encoder, SelEndEncoding);
                ObjCRuntime.SendMessageNoReturn(_commandBuffer, SelCommit);
                ObjCRuntime.SendMessageNoReturn(_commandBuffer, SelWaitUntilScheduled);
                ObjCRuntime.SendMessageNoReturn(_drawable, SelPresent);
            }
            else if (_encoder != 0)
            {
                // Safety: if we ever created an encoder but didn't reach a normal frame end,
                // ensure it's properly ended before it can be released by ARC/autorelease pools.
                ObjCRuntime.SendMessageNoReturn(_encoder, SelEndEncoding);
            }
        }
        finally
        {
            if (signalFrameCompleted)
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
        }
    }

    private static nint CreateRenderPass(nint drawableTexture, nint stencilTexture, nint msaaColorTexture)
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

    public void DrawText(ReadOnlySpan<char> text, Point location, IFont font, Color color)
    {
        if (text.IsEmpty)
        {
            return;
        }

        DrawText(text, new Rect(location.X, location.Y, 0, 0), font, color, TextAlignment.Left, TextAlignment.Top,
            text.IndexOfAny('\r', '\n') >= 0 ? TextWrapping.Wrap : TextWrapping.NoWrap);
    }

    public void DrawText(ReadOnlySpan<char> text, Rect bounds, IFont font, Color color,
        TextAlignment horizontalAlignment = TextAlignment.Left,
        TextAlignment verticalAlignment = TextAlignment.Top,
        TextWrapping wrapping = TextWrapping.NoWrap)
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
        if (wrapping != TextWrapping.NoWrap && bounds.Width > 0 && !double.IsInfinity(bounds.Width) && !double.IsNaN(bounds.Width))
        {
            targetWidthDip = bounds.Width;
        }

        int widthPx = Math.Max(1, LayoutRounding.CeilToPixelInt(targetWidthDip, DpiScale));
        int heightPx = Math.Max(1, LayoutRounding.CeilToPixelInt(measured.Height, DpiScale));

        // Early clip cull: skip text entirely outside the current scissor region.
        if (_clipBoundsWorld.HasValue)
        {
            var c = _clipBoundsWorld.Value;
            double worldLeft = bounds.X + _translateX;
            double worldTop = bounds.Y + _translateY;
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
        drawX = LayoutRounding.RoundToPixel(drawX, DpiScale);
        drawY = LayoutRounding.RoundToPixel(drawY, DpiScale);

        if (!_resources.TextCache.TryGetOrCreate(
                ct,
                text,
                widthPx,
                heightPx,
                (uint)Math.Round(DpiScale * 96.0),
                color,
                horizontalAlignment,
                TextAlignment.Top,
                wrapping,
                out int imageId))
        {
            return;
        }

        var paint = _vg.ImagePattern((float)drawX, (float)drawY, (float)widthDip, (float)heightDip, 0, imageId, 1.0f);
        _vg.BeginPath();
        _vg.Rect((float)drawX, (float)drawY, (float)widthDip, (float)heightDip);
        _vg.FillPaint(paint);
        _vg.Fill();
    }

    public Size MeasureText(ReadOnlySpan<char> text, IFont font)
    {
        if (text.IsEmpty)
        {
            return Size.Empty;
        }

        if (font is not CoreTextFont ct)
        {
            return new Size(text.Length * 8, 16);
        }

        int maxWidthPx = 0;
        var sizePx = CoreTextText.Measure(ct, text, maxWidthPx, TextWrapping.NoWrap, (uint)Math.Round(DpiScale * 96.0));
        return new Size(sizePx.Width / DpiScale, sizePx.Height / DpiScale);
    }

    public Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        if (text.IsEmpty)
        {
            return Size.Empty;
        }

        if (font is not CoreTextFont ct)
        {
            return new Size(text.Length * 8, 16);
        }

        int maxWidthPx = maxWidth <= 0 ? 0 : Math.Max(1, LayoutRounding.CeilToPixelInt(maxWidth, DpiScale));
        var sizePx = CoreTextText.Measure(ct, text, maxWidthPx, TextWrapping.Wrap, (uint)Math.Round(DpiScale * 96.0));
        return new Size(sizePx.Width / DpiScale, sizePx.Height / DpiScale);
    }

    #endregion

    #region Image Rendering

    public void DrawImage(IImage image, Point location)
        => DrawImage(image, new Rect(location.X, location.Y, image.PixelWidth / DpiScale, image.PixelHeight / DpiScale));

    public void DrawImage(IImage image, Rect destRect)
        => DrawImage(image, destRect, new Rect(0, 0, image.PixelWidth, image.PixelHeight));

    public void DrawImage(IImage image, Rect destRect, Rect sourceRect)
    {
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

    private readonly struct AutoReleasePool : IDisposable
    {
        private static readonly nint ClsNSAutoreleasePool = ObjCRuntime.GetClass("NSAutoreleasePool");
        private static readonly nint SelRelease = ObjCRuntime.Selectors.release;
        private readonly nint _pool;

        public AutoReleasePool()
        {
            if (ClsNSAutoreleasePool == 0)
            {
                _pool = 0;
                return;
            }

            nint pool = ObjCRuntime.SendMessage(ClsNSAutoreleasePool, SelAlloc);
            _pool = pool != 0 ? ObjCRuntime.SendMessage(pool, SelInit) : 0;
        }

        public void Dispose()
        {
            if (_pool != 0)
            {
                ObjCRuntime.SendMessageNoReturn(_pool, SelRelease);
            }
        }
    }
}
