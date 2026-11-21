using System.Numerics;

using Aprillz.MewUI.Rendering.Filters;
using Aprillz.MewUI.Rendering.Retained;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// The target pixels one backdrop reads, kept between frames. A frame that repaints part of the box reads
/// only that part again while the backdrop keeps what stands behind it; otherwise the surface is reused
/// for a whole read every time.
/// </summary>
internal sealed class BackdropInputCache : IDisposable
{
    private IGraphicsFactory? _factory;
    private bool _hasAlpha;
    private double _scale;

    internal IRenderSurface? Surface { get; private set; }

    /// <summary>The target pixel rectangle the surface stands for.</summary>
    internal PixelBox Pixels { get; private set; }

    /// <summary>The surface-space box the surface was last filled for; a backdrop that moved or resized starts over.</summary>
    internal Rect Bounds { get; set; }

    /// <summary>True until the whole box has been read from the target once.</summary>
    internal bool NeedsWholeInput { get; set; } = true;

    /// <summary>Makes the surface stand for <paramref name="pixels"/>, starting over when it stood for something else.</summary>
    internal void Ensure(IGraphicsFactory factory, PixelBox pixels, double scale, bool hasAlpha)
    {
        if (Surface != null && ReferenceEquals(_factory, factory) && Pixels == pixels && _scale == scale && _hasAlpha == hasAlpha)
        {
            return;
        }

        Surface?.Dispose();
        Surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(pixels.Width, pixels.Height, scale, hasAlpha));
        _factory = factory;
        Pixels = pixels;
        _scale = scale;
        _hasAlpha = hasAlpha;
        NeedsWholeInput = true;
    }

    public void Dispose()
    {
        Surface?.Dispose();
        Surface = null;
        NeedsWholeInput = true;
    }
}

/// <summary>A rectangle of target pixels.</summary>
internal readonly record struct PixelBox(int Left, int Top, int Right, int Bottom)
{
    internal int Width => Right - Left;

    internal int Height => Bottom - Top;

    internal bool IsEmpty => Right <= Left || Bottom <= Top;

    internal PixelBox Intersect(PixelBox other)
        => new(Math.Max(Left, other.Left), Math.Max(Top, other.Top), Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));
}

/// <summary>
/// Draws a backdrop: the pixels the target holds behind a visual, inside its layout box and nowhere else,
/// through the visual's filter. The blur takes the pixels at the edge of the box for whatever lies beyond
/// it, so a change beside the box never reaches the backdrop. Where the target cannot be read, or the filter
/// cannot be run, the fallback brush fills the box instead, and without one nothing is drawn.
/// </summary>
internal static class BackdropPass
{
    [ThreadStatic]
    private static IImageFilterExecutor? _executor;

    [ThreadStatic]
    private static IGraphicsFactory? _executorFactory;

    /// <summary>
    /// Reads and filters the backdrop of a visual whose layout box is <paramref name="box"/> in the context's
    /// current coordinates. <paramref name="node"/> is the visual's scene node, whose input it may keep, or
    /// null for a visual drawn directly; <paramref name="dirtyRect"/> is the surface area the frame repaints,
    /// null for a whole frame.
    /// </summary>
    internal static Prepared Prepare(
        IGraphicsContext context,
        IGraphicsFactory? factory,
        in CompositionState state,
        Rect box,
        VisualNode? node,
        Rect? dirtyRect,
        RenderSceneStatistics? statistics)
    {
        var filter = state.BackdropFilter;
        if (filter == null || box.Width <= 0 || box.Height <= 0)
        {
            return default;
        }

        var surfaceBox = RetainedGeometry.TransformRect(box, context.GetTransform());
        var prepared = new Prepared(state, box, surfaceBox);
        if (dirtyRect is Rect repainted && !repainted.IntersectsWith(surfaceBox))
        {
            // The frame repaints nothing of the box, so what the target holds there still stands.
            return default;
        }

        if (factory == null || context is not ITargetSnapshotContext snapshot || context is not GraphicsContextBase contextBase ||
            !snapshot.TryGetSnapshotTarget(out int targetWidth, out int targetHeight, out bool hasAlpha))
        {
            return Fallback(prepared, node, statistics);
        }

        double scale = context.DpiScale;
        var root = contextBase.RootTransform;
        var pixels = ToPixels(surfaceBox, root, scale, targetWidth, targetHeight);
        if (pixels.IsEmpty)
        {
            return default;
        }

        // The node keeps what it read only where its box on this target is the box the dirty region was
        // closed over; a target that stands inside the surface (a group) is read whole every time.
        bool keepsInput = node != null && node.BackdropRetained && root.IsIdentity && IsNear(surfaceBox, node.ShiftedBackdropBounds);
        IRenderSurface? input;
        IRenderSurface? ownedInput = null;
        if (node != null)
        {
            var cache = node.BackdropCache ??= new BackdropInputCache();
            cache.Ensure(factory, pixels, scale, hasAlpha);
            var read = pixels;
            if (keepsInput && !cache.NeedsWholeInput && cache.Bounds == surfaceBox && dirtyRect is Rect dirty)
            {
                // Only what the frame has drawn again is new; the rest of the box still holds what stands behind it.
                read = pixels.Intersect(ToPixels(dirty, root, scale, targetWidth, targetHeight));
            }

            if (!read.IsEmpty &&
                !snapshot.TryCopyTargetPixels(read.Left, read.Top, read.Width, read.Height, cache.Surface!, read.Left - pixels.Left, read.Top - pixels.Top))
            {
                cache.Dispose();
                return Fallback(prepared, node, statistics);
            }

            if (statistics != null && !read.IsEmpty)
            {
                statistics.BackdropInputPixelCount += (long)read.Width * read.Height;
            }

            cache.Bounds = surfaceBox;
            cache.NeedsWholeInput = !keepsInput;
            input = cache.Surface;
        }
        else
        {
            ownedInput = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(pixels.Width, pixels.Height, scale, hasAlpha));
            if (!snapshot.TryCopyTargetPixels(pixels.Left, pixels.Top, pixels.Width, pixels.Height, ownedInput, 0, 0))
            {
                ownedInput.Dispose();
                return Fallback(prepared, node, statistics);
            }

            input = ownedInput;
        }

        var inputSurface = RenderSurfaceResource.ResolveBackendSurface(input!);
        IImage? sourceImage = null;
        DefaultFilterContext? filterContext = null;
        FilterResult? result = null;
        try
        {
            if (_executor == null || !ReferenceEquals(_executorFactory, factory))
            {
                _executor = factory.CreateImageFilterExecutor();
                _executorFactory = factory;
            }

            sourceImage = factory.CreateImageView(inputSurface);
            // A node after a blur may read any of its pixels at their own positions, so only a lone blur may be partial or reduced.
            bool isLoneBlur = filter is BlurFilter { Input: null };
            filterContext = new DefaultFilterContext(inputSurface, sourceImage, new Rect(0, 0, pixels.Width, pixels.Height), factory, scale, scale)
            {
                ExtendsSourceEdges = true,
                ReducesBlur = isLoneBlur,
                OutputPixels = isLoneBlur ? NeededOutput(dirtyRect, root, scale, targetWidth, targetHeight, pixels) : null,
            };
            result = _executor.Execute(filter, filterContext);
        }
        catch (NotSupportedException)
        {
            // A filter graph the executor does not handle is drawn as if the target could not be read.
            result = null;
        }
        finally
        {
            // The context may not have drawn with them yet, so nothing goes back for reuse before its frame ends.
            var heldResult = result;
            var heldContext = filterContext;
            var heldImage = sourceImage;
            var heldInput = ownedInput;
            contextBase.ReleaseWhenFrameEnds(() =>
            {
                heldResult?.Dispose();
                heldContext?.Dispose();
                heldImage?.Dispose();
                heldInput?.Dispose();
            });
        }

        if (result == null)
        {
            return Fallback(prepared, node, statistics);
        }

        if (statistics != null)
        {
            statistics.BackdropPassCount++;
        }

        // The result covers its bounds, in pixels of the input, whatever its own resolution.
        var resultBounds = result.Bounds;
        var placement = new Rect(
            (pixels.Left + resultBounds.X) / scale - root.M31,
            (pixels.Top + resultBounds.Y) / scale - root.M32,
            resultBounds.Width / scale,
            resultBounds.Height / scale);
        var drawn = new Rect(pixels.Left / scale - root.M31, pixels.Top / scale - root.M32, pixels.Width / scale, pixels.Height / scale);
        var erased = drawn;
        if (dirtyRect is Rect repaintedArea)
        {
            erased = erased.Intersect(repaintedArea);
        }

        return prepared with
        {
            Image = result.AsImage(),
            ImageSource = new Rect(0, 0, result.PixelWidth, result.PixelHeight),
            ImagePlacement = placement,
            // A target placed inside the surface (a group) is erased in its own pixels, which these are not.
            Erased = hasAlpha && root.IsIdentity ? erased : default,
        };
    }

    /// <summary>
    /// Draws what <see cref="Prepare"/> made under the visual's current coordinates. <paramref name="erase"/>
    /// clears the box on a target with alpha first, which is right only where the backdrop replaces what was
    /// there at full strength: a faded backdrop is blended over it instead.
    /// </summary>
    internal static void Draw(IGraphicsContext context, in Prepared prepared, bool erase)
    {
        if (!prepared.HasDrawing)
        {
            return;
        }

        context.Save();
        try
        {
            ClipToCorners(context, prepared.Box, prepared.State.BackdropCornerRadius);
            if (prepared.Image == null)
            {
                context.FillRectangle(prepared.Box, prepared.State.BackdropFallback!);
                return;
            }

            context.SetTransform(Matrix3x2.Identity);
            if (erase && !prepared.Erased.IsEmpty && context is ITransparentDirtyRectContext eraser)
            {
                // Blended over a target that carries alpha, the sharp pixels would show through wherever the
                // filtered ones are not opaque. The erase ignores the clip, so it stays inside the corners.
                EraseInsideCorners(eraser, prepared.Erased, prepared.SurfaceBox, prepared.State.BackdropCornerRadius, prepared.Box);
            }

            context.DrawImage(prepared.Image, prepared.ImagePlacement, prepared.ImageSource);
        }
        finally
        {
            context.Restore();
        }
    }

    private static Prepared Fallback(Prepared prepared, VisualNode? node, RenderSceneStatistics? statistics)
    {
        node?.ReleaseBackdropCache();
        if (statistics != null)
        {
            statistics.BackdropFallbackCount++;
        }

        return prepared.State.BackdropFallback == null ? default : prepared with { IsFallback = true };
    }

    private static void ClipToCorners(IGraphicsContext context, Rect box, CornerRadius corners)
    {
        if (corners.IsZero)
        {
            context.IntersectClip(box);
        }
        else if (corners.IsUniform)
        {
            context.SetClipRoundedRect(box, corners.TopLeft, corners.TopLeft);
        }
        else
        {
            context.SetClipPath(PathGeometry.FromRoundedRect(box, corners.TopLeft, corners.TopRight, corners.BottomRight, corners.BottomLeft));
        }
    }

    /// <summary>
    /// Erases <paramref name="area"/> where it lies inside the rounded box: the two bands that clear every
    /// corner. The corner squares are left as they are, since the erase has no curve to stop at.
    /// </summary>
    private static void EraseInsideCorners(ITransparentDirtyRectContext eraser, Rect area, Rect surfaceBox, CornerRadius corners, Rect localBox)
    {
        if (corners.IsZero)
        {
            eraser.ClearRectangleToTransparent(area);
            return;
        }

        // The radii are in the visual's own units; the box on the surface tells how far they stretch.
        double scaleX = localBox.Width > 0 ? surfaceBox.Width / localBox.Width : 1;
        double scaleY = localBox.Height > 0 ? surfaceBox.Height / localBox.Height : 1;
        double left = Math.Max(corners.TopLeft, corners.BottomLeft) * scaleX;
        double right = Math.Max(corners.TopRight, corners.BottomRight) * scaleX;
        double top = Math.Max(corners.TopLeft, corners.TopRight) * scaleY;
        double bottom = Math.Max(corners.BottomLeft, corners.BottomRight) * scaleY;
        var tall = new Rect(surfaceBox.X + left, surfaceBox.Y, Math.Max(0, surfaceBox.Width - left - right), surfaceBox.Height);
        var wide = new Rect(surfaceBox.X, surfaceBox.Y + top, surfaceBox.Width, Math.Max(0, surfaceBox.Height - top - bottom));
        EraseIfAny(eraser, tall.Intersect(area));
        EraseIfAny(eraser, wide.Intersect(area));
    }

    private static void EraseIfAny(ITransparentDirtyRectContext eraser, Rect area)
    {
        if (area.Width > 0 && area.Height > 0)
        {
            eraser.ClearRectangleToTransparent(area);
        }
    }

    /// <summary>
    /// The input pixels whose filtered result this frame draws: the part of the box the frame repaints, since the
    /// target keeps the rest of the last result. Null for a whole frame or a repaint that covers the box.
    /// </summary>
    private static Rect? NeededOutput(Rect? dirtyRect, Matrix3x2 root, double scale, int targetWidth, int targetHeight, PixelBox pixels)
    {
        if (dirtyRect is not Rect dirty)
        {
            return null;
        }

        var repainted = pixels.Intersect(ToPixels(dirty, root, scale, targetWidth, targetHeight));
        if (repainted == pixels)
        {
            return null;
        }

        // A pixel more on each side keeps the stretch of a reduced result from sampling past what was worked out.
        const int MARGIN = 2;
        int left = Math.Max(0, repainted.Left - pixels.Left - MARGIN);
        int top = Math.Max(0, repainted.Top - pixels.Top - MARGIN);
        int right = Math.Min(pixels.Width, repainted.Right - pixels.Left + MARGIN);
        int bottom = Math.Min(pixels.Height, repainted.Bottom - pixels.Top + MARGIN);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>The target pixels a surface-space rectangle covers, outward to whole pixels and inside the target.</summary>
    private static PixelBox ToPixels(Rect surfaceRect, Matrix3x2 root, double scale, int targetWidth, int targetHeight)
        => new(
            Math.Clamp((int)Math.Floor((surfaceRect.X + root.M31) * scale), 0, targetWidth),
            Math.Clamp((int)Math.Floor((surfaceRect.Y + root.M32) * scale), 0, targetHeight),
            Math.Clamp((int)Math.Ceiling((surfaceRect.Right + root.M31) * scale), 0, targetWidth),
            Math.Clamp((int)Math.Ceiling((surfaceRect.Bottom + root.M32) * scale), 0, targetHeight));

    // How far apart two boxes may be and still be the same box, for the way through the transform and back.
    private const double SAME_BOX_TOLERANCE = 1.0 / 256;

    private static bool IsNear(Rect first, Rect second)
        => Math.Abs(first.X - second.X) <= SAME_BOX_TOLERANCE && Math.Abs(first.Y - second.Y) <= SAME_BOX_TOLERANCE &&
           Math.Abs(first.Width - second.Width) <= SAME_BOX_TOLERANCE && Math.Abs(first.Height - second.Height) <= SAME_BOX_TOLERANCE;

    /// <summary>
    /// A backdrop read and filtered, ready to be drawn: the filtered image placed in surface coordinates, or
    /// the fallback brush. Default when there is nothing to draw.
    /// </summary>
    internal readonly record struct Prepared(CompositionState State, Rect Box, Rect SurfaceBox)
    {
        internal IImage? Image { get; init; }

        internal Rect ImageSource { get; init; }

        internal Rect ImagePlacement { get; init; }

        /// <summary>The surface area cleared before the image is drawn; empty on a target without alpha.</summary>
        internal Rect Erased { get; init; }

        internal bool IsFallback { get; init; }

        internal bool HasDrawing => Image != null || IsFallback;
    }
}
