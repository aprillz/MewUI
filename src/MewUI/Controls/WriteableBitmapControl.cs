using System.Runtime.InteropServices;

using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for controls that render using a WriteableBitmap.
/// Provides infrastructure for pixel-based custom rendering with automatic
/// buffer management and DPI-aware sizing.
///
/// All rendering happens on a single IBitmapRenderTarget buffer:
/// - Direct pixel manipulation via LockForWrite()
/// - Vector graphics via OnRenderBitmap(IGraphicsContext)
/// </summary>
public class WriteableBitmapControl : Control
{
    private IBitmapRenderTarget? _renderTarget;
    private IImage? _image;
    private bool _needsRender;
    private int _lastVersion;
    private bool _isRendering;

    /// <summary>
    /// Gets the pixel width of the current render target.
    /// </summary>
    protected int PixelWidth => _renderTarget?.PixelWidth ?? 0;

    /// <summary>
    /// Gets the pixel height of the current render target.
    /// </summary>
    protected int PixelHeight => _renderTarget?.PixelHeight ?? 0;

    /// <summary>
    /// Locks the render target for direct pixel manipulation.
    /// The returned context provides access to the pixel buffer.
    /// </summary>
    protected WriteContext LockForWrite()
    {
        if (_renderTarget == null)
            throw new InvalidOperationException("Render target not initialized. Call from OnBitmapSizeChanged or later.");

        return new WriteContext(this, _renderTarget);
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
    /// Called when the bitmap needs to be redrawn using standard graphics operations.
    /// Override this method for drawing with IGraphicsContext (lines, rectangles, text, etc.).
    /// This is called after any direct pixel manipulation from OnBitmapSizeChanged.
    /// </summary>
    /// <param name="ctx">The graphics context for drawing operations.</param>
    protected virtual void OnRenderBitmap(IGraphicsContext ctx) { }

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
        bool sizeChanged = _renderTarget == null ||
                          _renderTarget.PixelWidth != pixelWidth ||
                          _renderTarget.PixelHeight != pixelHeight;

        _isRendering = true;
        try
        {
            if (sizeChanged)
            {
                _renderTarget?.Dispose();
                _image?.Dispose();

                var factory = GetGraphicsFactory();
                _renderTarget = factory.CreateBitmapRenderTarget(pixelWidth, pixelHeight, scale);
                _image = null;
                _lastVersion = -1;

                // Let derived class initialize the bitmap
                OnBitmapSizeChanged(pixelWidth, pixelHeight);
                _needsRender = true;
            }

            if (_renderTarget == null)
                return;

            // Call OnRenderBitmap if needed
            if (_needsRender)
            {
                _needsRender = false;

                using var renderCtx = GetGraphicsFactory().CreateContext(_renderTarget);
                OnRenderBitmap(renderCtx);

                // Increment version after IGraphicsContext rendering
                IncrementRenderTargetVersion();
            }
        }
        finally
        {
            _isRendering = false;
        }

        // Create or update IImage from render target (which implements IPixelBufferSource)
        int currentVersion = _renderTarget.Version;
        if (_image == null || _lastVersion != currentVersion)
        {
            _image?.Dispose();
            var factory = GetGraphicsFactory();
            _image = factory.CreateImageFromPixelSource(_renderTarget);
            _lastVersion = currentVersion;
        }

        // Draw the bitmap to the screen
        if (_image != null)
        {
            context.DrawImage(_image, bounds);
        }
    }

    private void IncrementRenderTargetVersion()
    {
        _renderTarget?.IncrementVersion();
    }

    protected override void OnDispose()
    {
        base.OnDispose();

        _image?.Dispose();
        _image = null;

        _renderTarget?.Dispose();
        _renderTarget = null;
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
        private readonly IBitmapRenderTarget _target;

        public int Width => _target.PixelWidth;
        public int Height => _target.PixelHeight;
        public int PixelWidth => _target.PixelWidth;
        public int PixelHeight => _target.PixelHeight;
        public int Stride => _target.PixelWidth * 4;
        public int StrideBytes => _target.PixelWidth * 4;

        public Span<byte> PixelsBgra32 => _target.GetPixelSpan();
        public Span<uint> PixelsUInt32 => MemoryMarshal.Cast<byte, uint>(_target.GetPixelSpan());

        internal WriteContext(WriteableBitmapControl control, IBitmapRenderTarget target)
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
