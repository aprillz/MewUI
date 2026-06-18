using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

// Vector-source (IVectorImageSource) rendering for Image: a per-control bitmap cache. The vector is
// rasterized into an offscreen surface sized to the control's pixel bounds; idle/unrelated repaints
// (immediate mode repaints the whole window) just blit it. The surface is reused across content changes
// at the same size (e.g. a virtualized tile rebinding to a new icon) — only a size/DPI change reallocates.
// All fields here are touched on the UI thread only.
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
            if (dest.Width <= 0 || dest.Height <= 0)
            {
                return;
            }

            var factory = Application.IsRunning ? Application.Current.GraphicsFactory : Application.DefaultGraphicsFactory;
            if (factory == null)
            {
                vector.Render(context, dest); // No device to cache into — draw straight to the context.
                return;
            }

            // Cache bitmap sized to the control's pixel bounds. Keying on Bounds (not dest) means a tile
            // keeps the same surface across content changes (a virtualized rebind to a different-aspect
            // icon) — only a size/DPI change reallocates.
            double effectiveScale = ComputeEffectiveScale(context);
            const int maxExtent = 4096;
            int surfaceWidth = Math.Clamp((int)Math.Ceiling(Bounds.Width * effectiveScale), 1, maxExtent);
            int surfaceHeight = Math.Clamp((int)Math.Ceiling(Bounds.Height * effectiveScale), 1, maxExtent);

            if (_vectorSurface == null || _vectorSize != (surfaceWidth, surfaceHeight))
            {
                ClearVectorCache();
                _vectorSurface = factory.CreateSurface(
                    RenderSurfaceDescriptor.CachedImage(surfaceWidth, surfaceHeight, 1.0, "ImageVectorCache"));
                _vectorSize = (surfaceWidth, surfaceHeight);
                _vectorContentValid = false;
            }

            // (Re)rasterize only when the content is stale (first show / source / tint change); otherwise
            // an unrelated repaint just blits the cached bitmap.
            if (!_vectorContentValid)
            {
                RenderIntoVectorSurface(factory, vector, dest, effectiveScale);
                _vectorContentValid = true;
            }

            if (_vectorImage != null)
            {
                context.DrawImage(_vectorImage, Bounds);
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

    // Rasterizes the vector into the (reused) offscreen surface. The vector is drawn at its dest rect
    // mapped into the surface's pixel space (Bounds origin -> surface origin, scaled by effectiveScale).
    private void RenderIntoVectorSurface(IGraphicsFactory factory, IVectorImageSource vector, Rect dest, double effectiveScale)
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
                    (dest.X - Bounds.X) * effectiveScale,
                    (dest.Y - Bounds.Y) * effectiveScale,
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
