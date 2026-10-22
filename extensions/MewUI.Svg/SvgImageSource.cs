using System.Reflection;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

using Svg;

namespace Aprillz.MewUI.Svg;

/// <summary>
/// An SVG image source for the <see cref="Aprillz.MewUI.Controls.Image"/> control. As an
/// <see cref="IVectorImageSource"/> it renders crisply at any size (the control re-renders on resize)
/// and can be recolored via <see cref="Tint"/> for monochrome icons. <see cref="CreateImage"/> is a
/// raster fallback for consumers that need pixels.
/// </summary>
public sealed class SvgImageSource : MewObject, IVectorImageSource, INotifyImageChanged, IDisposable
{
    /// <summary>The color the SVG fill is recolored with, for monochrome icons; null keeps the source colors.</summary>
    public static readonly MewProperty<Color?> TintProperty =
        MewProperty<Color?>.Register<SvgImageSource>(nameof(Tint), null,
            changed: (owner, _, _) => owner.OnVisualChanged());

    /// <summary>The pixel width of the <see cref="CreateImage"/> raster fallback; null uses the intrinsic width.</summary>
    public static readonly MewProperty<int?> RasterWidthProperty =
        MewProperty<int?>.Register<SvgImageSource>(nameof(RasterWidth), null,
            changed: (owner, _, _) => owner.OnVisualChanged());

    /// <summary>The pixel height of the <see cref="CreateImage"/> raster fallback; null uses the intrinsic height.</summary>
    public static readonly MewProperty<int?> RasterHeightProperty =
        MewProperty<int?>.Register<SvgImageSource>(nameof(RasterHeight), null,
            changed: (owner, _, _) => owner.OnVisualChanged());

    private readonly SvgDocument _document;

    // Serializes the document mutation in Render: a host may rasterize on a background thread, and a
    // shared source could be drawn by more than one host at once.
    private readonly object _renderLock = new();

    // CreateImage raster-fallback cache (the vector Render path does not use these).
    private IRenderSurface? _surface;
    private IImage? _image;
    private (int Width, int Height, uint Tint) _cacheKey;
    private bool _disposed;

    private SvgImageSource(SvgDocument document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    /// <summary>Loads an SVG from a file path.</summary>
    public static SvgImageSource FromFile(string path) => new(SvgDocument.Open(path));

    /// <summary>Parses an SVG from markup.</summary>
    public static SvgImageSource FromString(string svg) => new(SvgDocument.Parse(svg));

    /// <summary>Parses an SVG from a stream.</summary>
    public static SvgImageSource FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream);
        return new SvgImageSource(SvgDocument.Parse(reader.ReadToEnd()));
    }

    /// <summary>Loads an SVG from an embedded assembly resource.</summary>
    public static SvgImageSource FromResource(Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new ArgumentException($"Resource '{resourceName}' not found.", nameof(resourceName));
        return FromStream(stream);
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>Gets or sets the color the SVG fill is recolored with, for monochrome icons whose elements inherit fill
    /// (no explicit per-element fill); null keeps the source colors.</summary>
    /// <remarks>Changing it re-renders the hosting control.</remarks>
    public Color? Tint
    {
        get => GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    /// <summary>Gets or sets the pixel width of the <see cref="CreateImage"/> raster fallback; null uses the intrinsic width.</summary>
    /// <remarks>The vector path ignores it.</remarks>
    public int? RasterWidth
    {
        get => GetValue(RasterWidthProperty);
        set => SetValue(RasterWidthProperty, value);
    }

    /// <summary>Gets or sets the pixel height of the <see cref="CreateImage"/> raster fallback; null uses the intrinsic height.</summary>
    /// <remarks>The vector path ignores it.</remarks>
    public int? RasterHeight
    {
        get => GetValue(RasterHeightProperty);
        set => SetValue(RasterHeightProperty, value);
    }

    /// <inheritdoc />
    public Size IntrinsicSize => new(Math.Max(0, _document.ViewBoxWidth), Math.Max(0, _document.ViewBoxHeight));

    /// <summary>Gets the number of SVG rasterizations across all instances, for diagnostics such as sampling rasters per second.</summary>
    /// <remarks>
    /// The count grows on each <see cref="Render"/>; the host <see cref="Aprillz.MewUI.Controls.Image"/> caches the rasterized
    /// bitmap, so a render happens only on the host's cache miss (size, tint or DPI change).
    /// </remarks>
    public static long TotalRasterCount => System.Threading.Interlocked.Read(ref _rasterCount);

    private static long _rasterCount;

    /// <inheritdoc />
    /// <remarks>
    /// The SVG is drawn into <paramref name="context"/> at <paramref name="destRect"/> on every call; rasterized-bitmap
    /// caching is the host's concern (the <see cref="Aprillz.MewUI.Controls.Image"/> vector path caches per control).
    /// </remarks>
    public void Render(IGraphicsContext context, Rect destRect)
    {
        if (destRect.Width <= 0 || destRect.Height <= 0)
        {
            return;
        }

        System.Threading.Interlocked.Increment(ref _rasterCount);

        // Snapshot the tint (set on the UI thread) and serialize the document mutation, since Render may
        // run on a background thread (Image's async raster) and the source may be shared.
        var tint = Tint;
        lock (_renderLock)
        {
            if (tint is Color color)
            {
                // Override the root fill so inheriting (monochrome) icon paths pick up the tint, then restore.
                var previous = _document.Fill;
                _document.Fill = new SvgColourServer(System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B));
                try
                {
                    _document.Render(context, destRect);
                }
                finally
                {
                    _document.Fill = previous;
                }
            }
            else
            {
                _document.Render(context, destRect);
            }
        }
    }

    /// <inheritdoc />
    public IImage CreateImage(IGraphicsFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var intrinsic = IntrinsicSize;
        int width = Math.Max(1, RasterWidth ?? (int)Math.Ceiling(intrinsic.Width));
        int height = Math.Max(1, RasterHeight ?? (int)Math.Ceiling(intrinsic.Height));
        uint tintKey = Tint is Color t ? (uint)((t.A << 24) | (t.R << 16) | (t.G << 8) | t.B) : 0u;

        // CreateImage rasterizes at the intrinsic/RasterWidth size, which is the same for every consumer of
        // this source, so a single self-managed entry suffices here (unlike the per-control display size).
        if (_image is not null && _cacheKey == (width, height, tintKey))
        {
            return _image;
        }

        InvalidateRaster();

        var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(width, height, 1.0, "SvgImageSource"));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            try
            {
                if (surface is ICpuPixelSurface cpu)
                {
                    cpu.Clear(Color.Transparent);
                }
                Render(context, new Rect(0, 0, width, height));
            }
            finally
            {
                context.EndFrame();
            }
        }

        _image = factory.CreateImageView(surface);
        _surface = surface;
        _cacheKey = (width, height, tintKey);
        return _image;
    }

    // A Tint/Raster* property changed: drop the cached raster and notify the host control to repaint.
    private void OnVisualChanged()
    {
        lock (_renderLock)
        {
            _document.InvalidateRenderCaches();
            InvalidateRaster();
        }
        Changed?.Invoke();
    }

    private void InvalidateRaster()
    {
        _image?.Dispose();
        _surface?.Dispose();
        _image = null;
        _surface = null;
        _cacheKey = default;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        DisposePropertyBindings();
        lock (_renderLock)
        {
            _document.InvalidateRenderCaches();
            InvalidateRaster();
        }
    }
}
