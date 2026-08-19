using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

// Vector-source (IVectorImageSource) rendering for Image: a per-control bitmap cache. The vector is
// rasterized into an offscreen surface sized to the painted region (the dest rect clipped to Bounds, so
// stretch mode and clipping both factor in); idle/unrelated repaints (immediate mode repaints the whole
// window) just blit it. The surface is reused across content changes at the same painted size (e.g. a
// virtualized tile rebinding to a same-aspect icon); a size/DPI change reallocates. Detached controls
// hand their surface to the window's reclaimer pool for same-size reuse. Cache fields are UI-thread
// only; a size change with a valid stale bitmap re-rasterizes on a worker thread (the stale bitmap is
// stretched in the meantime), first show and content changes stay synchronous.
public sealed partial class Image
{
    private IRenderSurface? _vectorSurface;
    private IImage? _vectorImage;
    private (int Width, int Height) _vectorSize;
    private bool _vectorContentValid;

    // Background-rebuild state. The volatile flag is the only cross-thread field (the worker never
    // touches cache fields); everything else is read/written on the UI thread.
    private volatile bool _vectorRebuildInProgress;
    private (int Width, int Height) _vectorWantedSize;
    private int _vectorContentVersion;
    private bool _vectorAsyncUnsupported;

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

            _vectorWantedSize = (surfaceWidth, surfaceHeight);

            if (_vectorSurface == null || _vectorSize != (surfaceWidth, surfaceHeight))
            {
                // Size change with a still-correct stale bitmap: show it stretched into the new region
                // and re-rasterize in the background (a complex vector can take hundreds of ms, which
                // would stall every resize frame). First show and content changes fall through to the
                // synchronous path so no wrong/empty frame is ever displayed.
                if (_vectorImage != null && _vectorContentValid && !_vectorAsyncUnsupported)
                {
                    MaybeStartVectorRebuild(factory, vector, dest, visible, effectiveScale, surfaceWidth, surfaceHeight);
                    context.DrawImage(_vectorImage, visible);
                    return;
                }

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

    /// <summary>Starts a background re-rasterization for the wanted pixel size unless one is already in flight.</summary>
    private void MaybeStartVectorRebuild(IGraphicsFactory factory, IVectorImageSource vector, Rect dest, Rect visible, double effectiveScale, int pixelWidth, int pixelHeight)
    {
        if (_vectorRebuildInProgress)
        {
            // Latest-wins: the in-flight build commits or is discarded against the wanted size, and its
            // InvalidateVisual re-enters here with the current size.
            return;
        }

        _vectorRebuildInProgress = true;
        var destInSurface = new Rect(
            (dest.X - visible.X) * effectiveScale,
            (dest.Y - visible.Y) * effectiveScale,
            dest.Width * effectiveScale,
            dest.Height * effectiveScale);
        _ = RebuildVectorAsync(factory, vector, destInSurface, pixelWidth, pixelHeight, _vectorContentVersion);
    }

    /// <summary>Rasterizes the vector on a worker thread, then commits the result on the UI thread.</summary>
    private async Task RebuildVectorAsync(IGraphicsFactory factory, IVectorImageSource vector, Rect destInSurface, int pixelWidth, int pixelHeight, int contentVersion)
    {
        IRenderSurface? newSurface = null;
        IImage? newImage = null;
        var unsupported = false;
        try
        {
            // The lambda captures locals only; instance cache fields stay UI-thread exclusive.
            await Task.Run(() =>
            {
                // Backend worker-thread setup: GL activates a share-listed worker context, D2D
                // (multi-threaded factory), Metal and GDI return a no-op scope.
                using var workerScope = factory.AcquireBackgroundRenderScope();
                var surface = factory.CreateSurface(
                    RenderSurfaceDescriptor.CachedImage(pixelWidth, pixelHeight, 1.0, "ImageVectorCache"));
                if (surface is not ICpuPixelSurface cpu)
                {
                    surface.Dispose();
                    unsupported = true;
                    return;
                }
                try
                {
                    cpu.Clear(Color.Transparent);
                    using (var offscreen = factory.CreateContext(surface))
                    {
                        offscreen.BeginFrame(surface);
                        try
                        {
                            vector.Render(offscreen, destInSurface);
                        }
                        finally
                        {
                            offscreen.EndFrame();
                        }
                    }
                    newImage = factory.CreateImageView(surface);
                    newSurface = surface;
                    surface = null!;
                }
                finally
                {
                    surface?.Dispose();
                }
            }).ConfigureAwait(false);
        }
        catch
        {
            // Build failed: drop partial state; the commit below falls back to a synchronous rebuild.
            newImage?.Dispose();
            newSurface?.Dispose();
            newImage = null;
            newSurface = null;
        }

        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        Action commit = () => CommitVectorRebuild(newSurface, newImage, pixelWidth, pixelHeight, contentVersion, unsupported);
        if (dispatcher != null && !dispatcher.IsOnUIThread)
        {
            dispatcher.BeginInvoke(commit);
        }
        else
        {
            commit();
        }
    }

    /// <summary>UI-thread commit: installs the worker-built bitmap if the request still matches, discards it otherwise.</summary>
    private void CommitVectorRebuild(IRenderSurface? newSurface, IImage? newImage, int pixelWidth, int pixelHeight, int contentVersion, bool unsupported)
    {
        try
        {
            if (unsupported)
            {
                // The factory produced a non-CPU-writable cache surface; stop retrying the worker path.
                _vectorAsyncUnsupported = true;
            }

            // Discard when the control detached, the wanted size moved on (a newer resize supersedes
            // this build) or the content changed mid-flight (the bitmap shows outdated content).
            var stillWanted = FindVisualRoot() is Window
                && _vectorWantedSize == (pixelWidth, pixelHeight)
                && _vectorContentVersion == contentVersion;
            if (newSurface == null || newImage == null || !stillWanted)
            {
                newImage?.Dispose();
                newSurface?.Dispose();
                if (stillWanted)
                {
                    // Build failed but the size is still wanted: drop the stale cache so the next
                    // paint rebuilds synchronously instead of showing the stretched bitmap forever.
                    ClearVectorCache();
                }
                return;
            }

            ClearVectorCache();
            _vectorSurface = newSurface;
            _vectorImage = newImage;
            _vectorSize = (pixelWidth, pixelHeight);
            _vectorContentValid = true;
        }
        finally
        {
            _vectorRebuildInProgress = false;
            // Repaint with the committed bitmap; on discard this re-runs RenderVector, which re-kicks
            // a build for the currently wanted size.
            InvalidateVisual();
        }
    }

    // Marks the cached bitmap stale (content/tint changed) but keeps the surface for reuse at the same size.
    private void InvalidateVectorContent()
    {
        _vectorContentValid = false;
        _vectorContentVersion++;
    }

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
            _vectorContentVersion++;
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
        _vectorContentVersion++;
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
