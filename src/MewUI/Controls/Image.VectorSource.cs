using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

// Vector-source (IVectorImageSource) rendering for Image: a per-control bitmap cache. The vector is
// rasterized into an offscreen surface sized to the painted region (the dest rect clipped to Bounds, so
// stretch mode and clipping both factor in); idle/unrelated repaints (immediate mode repaints the whole
// window) just blit it. The surface is reused across content changes at the same painted size (e.g. a
// virtualized tile rebinding to a same-aspect icon); a size/DPI change reallocates. Detached controls
// hand their surface to the window's reclaimer pool for same-size reuse. UI-thread only.
public sealed partial class Image
{
    private IRenderSurface? _vectorSurface;
    private IImage? _vectorImage;
    private (int Width, int Height) _vectorSize;
    private bool _vectorContentValid;

    private void RenderVector(IGraphicsContext context, IVectorImageSource vector)
    {
        var intrinsic = vector.IntrinsicSize;
        if (intrinsic.Width <= 0 || intrinsic.Height <= 0)
        {
            return;
        }

        context.Save();
        var dpiScale = GetDpi() / 96.0;
        context.SetClip(LayoutRounding.SnapViewportRectToPixels(Bounds, dpiScale));
        try
        {
            var dest = ComputeVectorDest(intrinsic, Bounds, StretchMode, AlignmentX, AlignmentY);
            // Cache only the region actually painted: dest clipped to Bounds. This is the minimum that
            // accounts for both the stretch mode (which sizes and positions dest) and the Bounds clip, so
            // the surface holds neither invisible overflow (UniformToFill / large None) nor empty padding
            // (Uniform / small None). Same-size painted regions share a pooled surface across rebinds.
            var visible = dest.Intersect(Bounds);
            if (visible.Width <= 0 || visible.Height <= 0)
            {
                return;
            }

            var factory = Application.IsRunning ? Application.Current.GraphicsFactory : Application.DefaultGraphicsFactory;
            if (factory == null)
            {
                vector.Render(context, dest); // No device to cache into: draw straight to the context.
                return;
            }

            double effectiveScale = ComputeEffectiveScale(context);
            const int maxExtent = 4096;
            int surfaceWidth = Math.Clamp((int)Math.Ceiling(visible.Width * effectiveScale), 1, maxExtent);
            int surfaceHeight = Math.Clamp((int)Math.Ceiling(visible.Height * effectiveScale), 1, maxExtent);

            if (_vectorSurface == null || _vectorSize != (surfaceWidth, surfaceHeight))
            {
                ClearVectorCache();
                // Reuse a surface this control parked on a recent detach/recycle if one of the exact
                // size survived; otherwise allocate. Reusing it keeps the offscreen surface (and its
                // device resources) intact, so only the content is repainted.
                if (!TryReclaimVectorSurface(surfaceWidth, surfaceHeight))
                {
                    _vectorSurface = factory.CreateSurface(
                        RenderSurfaceDescriptor.CachedImage(surfaceWidth, surfaceHeight, 1.0, "ImageVectorCache"));
                    _vectorSize = (surfaceWidth, surfaceHeight);
                }
                _vectorContentValid = false;
            }

            // (Re)rasterize only when the content is stale (first show / source / tint change); otherwise
            // an unrelated repaint just blits the cached bitmap.
            if (!_vectorContentValid)
            {
                RenderIntoVectorSurface(factory, vector, dest, visible, effectiveScale);
                _vectorContentValid = true;
            }

            if (_vectorImage != null)
            {
                context.DrawImage(_vectorImage, visible);
            }
        }
        finally
        {
            context.Restore();
        }
    }

    private static double ComputeEffectiveScale(IGraphicsContext context)
    {
        double dpiScale = context.DpiScale > 0 ? context.DpiScale : 1.0;
        var transform = context.GetTransform();
        double scaleX = Math.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12);
        double scaleY = Math.Sqrt(transform.M21 * transform.M21 + transform.M22 * transform.M22);
        double transformScale = Math.Max(scaleX, scaleY);
        if (!double.IsFinite(transformScale) || transformScale <= 0)
        {
            transformScale = 1.0;
        }
        return dpiScale * transformScale;
    }

    // Rasterizes the vector into the (reused) offscreen surface, whose origin is the visible region's
    // top-left. dest is mapped relative to that origin (scaled by effectiveScale); any part of dest
    // outside the surface (overflow that Bounds clips away) falls off the surface and is clipped by it.
    private void RenderIntoVectorSurface(IGraphicsFactory factory, IVectorImageSource vector, Rect dest, Rect visible, double effectiveScale)
    {
        var surface = _vectorSurface!;
        using (var offscreen = factory.CreateContext(surface))
        {
            offscreen.BeginFrame(surface);
            try
            {
                if (surface is ICpuPixelSurface cpu)
                {
                    cpu.Clear(Color.Transparent);
                }

                var destInSurface = new Rect(
                    (dest.X - visible.X) * effectiveScale,
                    (dest.Y - visible.Y) * effectiveScale,
                    dest.Width * effectiveScale,
                    dest.Height * effectiveScale);
                vector.Render(offscreen, destInSurface);
            }
            finally
            {
                offscreen.EndFrame();
            }
        }

        // Refresh the view so it reflects the newly rendered surface content. Cheap relative to creating
        // the surface (the expensive allocation), which is reused.
        _vectorImage?.Dispose();
        _vectorImage = factory.CreateImageView(surface);
    }

    // Marks the cached bitmap stale (content/tint changed) but keeps the surface for reuse at the same size.
    private void InvalidateVectorContent() => _vectorContentValid = false;

    // Hands the live cache surface to the window's size-keyed reclaimer on detach (e.g. a virtualized
    // tile recycled) so any same-size control can reuse it instead of rebuilding the offscreen
    // surface. The image view is recreated on the next paint, so only the surface is parked. With no
    // window to park with, releases it outright so the surface is never leaked.
    internal void ParkVectorCache(Window? window)
    {
        if (_vectorSurface == null)
        {
            return;
        }

        if (window != null)
        {
            _vectorImage?.Dispose();
            window.VectorSurfaceReclaimer.Park(_vectorSurface, _vectorSize.Width, _vectorSize.Height);
            _vectorSurface = null;
            _vectorImage = null;
            _vectorSize = default;
            _vectorContentValid = false;
        }
        else
        {
            ClearVectorCache();
        }
    }

    // Rents a parked surface of the exact pixel size from the window's reclaimer, if one is retained.
    // The image view is left null; RenderIntoVectorSurface creates it on the imminent repaint.
    private bool TryReclaimVectorSurface(int pixelWidth, int pixelHeight)
    {
        if (FindVisualRoot() is not Window window)
        {
            return false;
        }

        var surface = window.VectorSurfaceReclaimer.Rent(pixelWidth, pixelHeight);
        if (surface == null)
        {
            return false;
        }

        _vectorSurface = surface;
        _vectorSize = (pixelWidth, pixelHeight);
        return true;
    }

    // Releases the cached surface entirely (detach/dispose or size change).
    private void ClearVectorCache()
    {
        _vectorImage?.Dispose();
        _vectorSurface?.Dispose();
        _vectorImage = null;
        _vectorSurface = null;
        _vectorSize = default;
        _vectorContentValid = false;
    }

    // Destination rect for a vector source. Unlike the raster path (which crops the source rect for
    // UniformToFill), vectors are scaled into the returned rect and clipped to Bounds by the caller.
    private static Rect ComputeVectorDest(Size intrinsic, Rect bounds, Stretch stretch, ImageAlignmentX alignX, ImageAlignmentY alignY)
    {
        double iw = Math.Max(0, intrinsic.Width);
        double ih = Math.Max(0, intrinsic.Height);
        if (iw <= 0 || ih <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return new Rect(bounds.X, bounds.Y, 0, 0);
        }

        if (stretch == Stretch.Fill)
        {
            return bounds;
        }

        double dw, dh;
        if (stretch == Stretch.None)
        {
            dw = iw;
            dh = ih;
        }
        else
        {
            double scale = stretch == Stretch.UniformToFill
                ? Math.Max(bounds.Width / iw, bounds.Height / ih)
                : Math.Min(bounds.Width / iw, bounds.Height / ih);
            dw = iw * scale;
            dh = ih * scale;
        }

        double ax = alignX == ImageAlignmentX.Left ? 0 : alignX == ImageAlignmentX.Right ? 1 : 0.5;
        double ay = alignY == ImageAlignmentY.Top ? 0 : alignY == ImageAlignmentY.Bottom ? 1 : 0.5;
        double dx = bounds.X + (bounds.Width - dw) * ax;
        double dy = bounds.Y + (bounds.Height - dh) * ay;
        return new Rect(dx, dy, dw, dh);
    }
}
