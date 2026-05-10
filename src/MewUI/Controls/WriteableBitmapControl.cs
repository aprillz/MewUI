using System.Runtime.InteropServices;

using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for controls that render using a WriteableBitmap.
/// Provides infrastructure for pixel-based custom rendering with automatic
/// buffer management and DPI-aware sizing.
///
/// All rendering happens on a single CPU-writable render surface buffer:
/// - Direct pixel manipulation via LockForWrite()
/// - Vector graphics via OnRenderBitmap(IGraphicsContext)
/// </summary>
public class WriteableBitmapControl : Control
{
    private IRenderSurface? _surface;
    private ICpuPixelSurface? _pixels;
    private IImage? _image;
    private bool _needsRender;
    private bool _isRendering;

    /// <summary>
    /// Gets the pixel width of the current render target.
    /// </summary>
    protected int PixelWidth => _pixels?.PixelWidth ?? 0;

    /// <summary>
    /// Gets the pixel height of the current render target.
    /// </summary>
    protected int PixelHeight => _pixels?.PixelHeight ?? 0;

    /// <summary>
    /// Locks the render target for direct pixel manipulation.
    /// The returned context provides access to the pixel buffer.
    /// </summary>
    protected WriteContext LockForWrite()
    {
        if (_pixels == null)
            throw new InvalidOperationException("Render target not initialized. Call from OnBitmapSizeChanged or later.");

        return new WriteContext(this, _pixels);
    }

    /// <summary>
    /// Called when the bitmap size changes.
    /// Override to perform initial rendering or setup for the new size.
    /// Use LockForWrite() for direct pixel manipulation.
    /// </summary>
    protected virtual void OnBitmapSizeChanged(int pixelWidth, int pixelHeight)
    {
    }

    /// <summary>
    /// When false, skips creating a graphics context for <see cref="OnRenderBitmap"/>.
    /// Set to false when only using <see cref="OnRenderPixels"/> for direct pixel manipulation.
    /// </summary>
    protected bool UseBitmapGraphicsContext { get; set; } = true;

    /// <summary>
    /// Called when the bitmap needs to be redrawn using standard graphics operations.
    /// Override this method for drawing with IGraphicsContext (lines, rectangles, text, etc.).
    /// This is called after any direct pixel manipulation from OnBitmapSizeChanged.
    /// </summary>
    /// <param name="ctx">The graphics context for drawing operations.</param>
    protected virtual void OnRenderBitmap(IGraphicsContext ctx) { }

    /// <summary>
    /// Called after the graphics context is disposed (EndDraw complete) but before
    /// the image is created from the pixel buffer.
    /// Override this for direct pixel manipulation via <see cref="LockForWrite"/>
    /// that must not conflict with the graphics context lifecycle (e.g. D2D BeginDraw/EndDraw).
    /// </summary>
    protected virtual void OnRenderPixels() { }

    /// <summary>
    /// Invalidates the bitmap content, causing a full redraw on next render.
    /// </summary>
    protected void InvalidateBitmap()
    {
        _needsRender = true;
        InvalidateVisual();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var dpi = GetDpi();
        var scale = dpi / 96.0;

        // Calculate pixel dimensions
        int pixelWidth = (int)Math.Ceiling(bounds.Width * scale);
        int pixelHeight = (int)Math.Ceiling(bounds.Height * scale);

        if (pixelWidth <= 0 || pixelHeight <= 0)
            return;

        // Check if we need to recreate the render target
        bool sizeChanged = _surface == null ||
                          _surface.PixelWidth != pixelWidth ||
                          _surface.PixelHeight != pixelHeight;

        _isRendering = true;
        try
        {
            if (sizeChanged)
            {
                _image?.Dispose();
                _surface?.Dispose();
                _pixels = null;

                var factory = GetGraphicsFactory();
                var renderDevice = factory;
                _surface = renderDevice.CreateSurface(RenderSurfaceDescriptor.CpuPixels(
                    pixelWidth,
                    pixelHeight,
                    scale,
                    debugName: nameof(WriteableBitmapControl)));
                if (_surface is not ICpuPixelSurface pixels)
                {
                    _surface.Dispose();
                    _surface = null;
                    throw new NotSupportedException($"{nameof(WriteableBitmapControl)} requires a CPU-writable render surface.");
                }

                _pixels = pixels;
                _image = null;

                // Let derived class initialize the bitmap
                OnBitmapSizeChanged(pixelWidth, pixelHeight);
                _needsRender = true;
            }

            if (_pixels == null || _surface == null)
                return;

            // Call OnRenderBitmap if needed
            if (_needsRender)
            {
                _needsRender = false;

                // Vector graphics phase (context active — BeginDraw/EndDraw scope)
                if (UseBitmapGraphicsContext)
                {
                    using var renderCtx = GetGraphicsFactory().CreateContext(_surface);
                    OnRenderBitmap(renderCtx);
                }
                // Context disposed: EndDraw complete — safe for direct pixel access

                // Pixel manipulation phase (no context, LockForWrite safe)
                OnRenderPixels();

                // Increment version after all rendering
                IncrementRenderTargetVersion();
            }
        }
        finally
        {
            _isRendering = false;
        }

        // Create IImage once — it holds a reference to the render target (IPixelBufferSource)
        // and updates internally via version tracking.
        if (_image == null)
        {
            _image = GetGraphicsFactory().CreateImageView(_surface);
        }

        // Draw the bitmap to the screen
        if (_image != null)
        {
            context.DrawImage(_image, bounds);
        }
    }

    private void IncrementRenderTargetVersion()
    {
        _pixels?.IncrementVersion();
    }

    protected override void OnDispose()
    {
        base.OnDispose();

        _image?.Dispose();
        _image = null;

        _surface?.Dispose();
        _surface = null;
        _pixels = null;
    }

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        // Force render target recreation on DPI change
        InvalidateBitmap();
    }

    /// <summary>
    /// Context for direct pixel manipulation of the render target.
    /// </summary>
    public readonly ref struct WriteContext
    {
        private readonly WriteableBitmapControl _control;
        private readonly ICpuPixelSurface _target;

        public int Width => _target.PixelWidth;
        public int Height => _target.PixelHeight;
        public int PixelWidth => _target.PixelWidth;
        public int PixelHeight => _target.PixelHeight;
        public int Stride => _target.PixelWidth * 4;
        public int StrideBytes => _target.PixelWidth * 4;

        public Span<byte> PixelsBgra32 => _target.GetWritablePixelSpan();
        public Span<uint> PixelsUInt32 => MemoryMarshal.Cast<byte, uint>(_target.GetWritablePixelSpan());

        /// <summary>
        /// Whether the underlying buffer expects pre-multiplied BGRA. Backends differ (GDI's
        /// HBITMAP-backed RT is premultiplied because <c>AlphaBlend</c> demands it; D2D's
        /// software RT is straight). Direct-pixel writers must respect this — writing straight
        /// RGBA into a premultiplied RT shows up as bright halos on alpha-soft edges (the
        /// downstream blit then assumes RGB is already alpha-scaled and skips the divide).
        /// </summary>
        public bool IsPremultiplied => _target.Capabilities.HasFlag(SurfaceCapabilities.Premultiplied);

        internal WriteContext(WriteableBitmapControl control, ICpuPixelSurface target)
        {
            _control = control;
            _target = target;
        }

        public void Clear(Color color)
        {
            _target.Clear(color);
        }

        public void Dispose()
        {
            // Increment version after pixel manipulation
            _target.IncrementVersion();
            // Trigger repaint (skip if already inside OnRender)
            if (!_control._isRendering)
            {
                _control.InvalidateVisual();
            }
        }
    }
}
