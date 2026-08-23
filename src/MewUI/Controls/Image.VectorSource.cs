using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

// Vector-source (IVectorImageSource) rendering for Image: a per-control bitmap cache. The vector is
// rasterized into the top-left of a pooled offscreen surface sized to the painted region (the dest
// rect clipped to Bounds, so stretch mode and clipping both factor in); idle/unrelated repaints
// (immediate mode repaints the whole window) just blit that region. Surfaces come from the device's
// scratch pool (see ScratchSurfaceExtensions), which over-allocates so resizes repaint in place and
// releases return the surface for any control to reuse. Cache fields are UI-thread only. What
// rasterizes on a worker thread depends on VectorCacheMode: a size change always does (the previous
// bitmap is stretched in the meantime), while first show and content changes do only under
// ImageVectorCacheMode.CachedDeferred.
public sealed partial class Image
{
    private IRenderSurface? _vectorSurface;
    private IImage? _vectorImage;
    // Allocated surface size. Approx-fitted, so it is usually larger than the painted content.
    private (int Width, int Height) _vectorSize;
    // Pixels actually painted into the surface's top-left corner; the blit's source rect.
    private (int Width, int Height) _vectorContentSize;
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

            var cacheMode = VectorCacheMode;
            var factory = Application.IsRunning ? Application.Current.GraphicsFactory : Application.DefaultGraphicsFactory;
            if (factory == null || cacheMode == ImageVectorCacheMode.Direct)
            {
                // No device to cache into, or the caller asked for it: draw straight to the context.
                if (_vectorSurface != null)
                {
                    ClearVectorCache();
                }
                vector.Render(context, dest);
                return;
            }

            double effectiveScale = ComputeEffectiveScale(context);
            const int maxExtent = 4096;
            int contentWidth = Math.Clamp((int)Math.Ceiling(visible.Width * effectiveScale), 1, maxExtent);
            int contentHeight = Math.Clamp((int)Math.Ceiling(visible.Height * effectiveScale), 1, maxExtent);

            _vectorWantedSize = (contentWidth, contentHeight);

            // Approx-fitted surface: reallocate only when the content outgrows it.
            bool surfaceFits = _vectorSurface != null
                && contentWidth <= _vectorSize.Width
                && contentHeight <= _vectorSize.Height;

            bool sizeChanged = !surfaceFits || _vectorContentSize != (contentWidth, contentHeight);

            // Rasterize off the UI thread instead of stalling the frame that triggered it, a complex
            // vector takes hundreds of ms. Cached defers only a size change, where the previous bitmap
            // still shows the right content stretched; CachedDeferred defers every change, showing
            // outdated content or nothing at all meanwhile.
            bool deferSize = _vectorImage != null && _vectorContentValid;
            bool canDefer = !_vectorAsyncUnsupported
                && (deferSize || cacheMode == ImageVectorCacheMode.CachedDeferred);

            if ((sizeChanged || !_vectorContentValid) && canDefer)
            {
                MaybeStartVectorRebuild(factory, vector, dest, visible, effectiveScale, contentWidth, contentHeight);
                if (_vectorImage != null)
                {
                    context.DrawImage(_vectorImage, visible, ContentSourceRect());
                }
                return;
            }

            if (sizeChanged)
            {
                if (!surfaceFits)
                {
                    ClearVectorCache();
                    // Pool-sized allocation, at least content-sized; shared across controls.
                    _vectorSurface = factory.AcquireScratchSurface(contentWidth, contentHeight, debugName: "ImageVectorCache");
                    AccountVectorCache(_vectorSurface);
                    _vectorSize = (_vectorSurface.PixelWidth, _vectorSurface.PixelHeight);
                }
                _vectorContentValid = false;
            }

            // (Re)rasterize only when the content is stale (first show / source / tint / size change);
            // otherwise an unrelated repaint just blits the cached bitmap.
            if (!_vectorContentValid)
            {
                RenderIntoVectorSurface(factory, vector, dest, visible, effectiveScale);
                _vectorContentSize = (contentWidth, contentHeight);
                _vectorContentValid = true;
            }

            if (_vectorImage != null)
            {
                context.DrawImage(_vectorImage, visible, ContentSourceRect());
            }
        }
        finally
        {
            context.Restore();
        }
    }

    /// <summary>Source rect of the painted region inside the (larger) pooled surface.</summary>
    private Rect ContentSourceRect() => new(0, 0, _vectorContentSize.Width, _vectorContentSize.Height);

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
            // The in-flight build commits whatever size it was started for, and its InvalidateVisual
            // re-enters here so the next build chases the size current at that point.
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

    /// <summary>Rasterizes the vector into the rented surface on a worker thread, then commits on the UI thread.</summary>
    private async Task RebuildVectorAsync(IGraphicsFactory factory, IVectorImageSource vector, Rect destInSurface, int pixelWidth, int pixelHeight, int contentVersion)
    {
        IRenderSurface? rentedSurface = null;
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
                // Rented on this thread, not the UI thread: a backend may bind an offscreen surface
                // to the thread that created it, and the pool only hands back surfaces the calling
                // thread can render into.
                var surface = factory.AcquireScratchSurface(pixelWidth, pixelHeight, debugName: "ImageVectorCache");
                rentedSurface = surface;
                if (surface is ICpuPixelSurface pixels)
                {
                    pixels.Clear(Color.Transparent);
                }

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
            }).ConfigureAwait(false);
        }
        catch
        {
            // Build failed: drop partial state and stop using the worker path. A backend whose
            // offscreen surfaces are bound to the thread that created them fails every time, and
            // retrying would leave the control blank forever under CachedDeferred.
            newImage?.Dispose();
            newImage = null;
            newSurface = null;
            unsupported = true;
        }

        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        Action commit = () => CommitVectorRebuild(factory, rentedSurface, newSurface, newImage, pixelWidth, pixelHeight, contentVersion, unsupported);
        if (dispatcher != null && !dispatcher.IsOnUIThread)
        {
            dispatcher.BeginInvoke(commit);
        }
        else
        {
            commit();
        }
    }

    /// <summary>UI-thread commit: installs the worker-built bitmap unless the control detached or its content changed.</summary>
    private void CommitVectorRebuild(IGraphicsFactory factory, IRenderSurface? rentedSurface, IRenderSurface? newSurface, IImage? newImage, int pixelWidth, int pixelHeight, int contentVersion, bool unsupported)
    {
        try
        {
            if (unsupported)
            {
                // The worker rasterization failed; stop retrying it and rasterize on the UI thread.
                _vectorAsyncUnsupported = true;
            }

            // A size the resize already moved past is still installed: it is closer to the current one
            // than the bitmap it replaces, so the frames until the next build stretch less. Only a
            // detached control or content that changed mid-flight makes the result unusable.
            var usable = FindVisualRoot() is Window && _vectorContentVersion == contentVersion;
            if (newSurface == null || newImage == null || !usable)
            {
                newImage?.Dispose();
                if (rentedSurface != null)
                {
                    factory.ReleaseScratchSurface(rentedSurface);
                }
                if (usable && _vectorWantedSize == (pixelWidth, pixelHeight))
                {
                    // Build failed but the size is still wanted: drop the stale cache so the next
                    // paint rebuilds synchronously instead of showing the stretched bitmap forever.
                    ClearVectorCache();
                }
                return;
            }

            ClearVectorCache();
            _vectorSurface = newSurface;
            AccountVectorCache(newSurface);
            _vectorImage = newImage;
            _vectorSize = (newSurface.PixelWidth, newSurface.PixelHeight);
            _vectorContentSize = (pixelWidth, pixelHeight);
            _vectorContentValid = true;
        }
        finally
        {
            _vectorRebuildInProgress = false;
            // Repaint with the committed bitmap. When it was built for a superseded size this re-runs
            // RenderVector, which kicks the next build off the size wanted now.
            InvalidateVisual();
        }
    }

    // Marks the cached bitmap stale (content/tint changed) but keeps the surface for reuse at the same size.
    private void InvalidateVectorContent()
    {
        _vectorContentValid = false;
        _vectorContentVersion++;
    }

    /// <summary>Drops the cache on detach; the surface returns to the device scratch pool.</summary>
    internal void ParkVectorCache(Window? window) => ClearVectorCache();

    /// <summary>Releases the cached surface back to the device scratch pool.</summary>
    // Ledger bytes of the surface behind _vectorSurface, subtracted again when it is released.
    private long _vectorAccountedBytes;

    private void AccountVectorCache(IRenderSurface surface)
    {
        _vectorAccountedBytes = RenderMemoryLedger.ScratchBytes(surface.PixelWidth, surface.PixelHeight);
        RenderMemoryLedger.VectorCacheEntryAdded(_vectorAccountedBytes);
    }

    private void ClearVectorCache()
    {
        _vectorImage?.Dispose();
        if (_vectorSurface != null)
        {
            RenderMemoryLedger.VectorCacheEntryRemoved(_vectorAccountedBytes);
            _vectorAccountedBytes = 0;
            var device = Application.IsRunning ? Application.Current.GraphicsFactory : Application.DefaultGraphicsFactory;
            if (device != null)
            {
                device.ReleaseScratchSurface(_vectorSurface);
            }
            else
            {
                _vectorSurface.Dispose();
            }
        }
        _vectorImage = null;
        _vectorSurface = null;
        _vectorSize = default;
        _vectorContentSize = default;
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
