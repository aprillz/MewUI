using System.IO;
using System.Reflection;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

using Svg;

namespace Aprillz.MewUI.Svg;

/// <summary>
/// An SVG image source for the <see cref="Aprillz.MewUI.Controls.Image"/> control. As an
/// <see cref="IVectorImageSource"/> it renders crisply at any size (the control re-renders on resize)
/// and can be recolored via <see cref="Tint"/> for monochrome icons. <see cref="CreateImage"/> is a
/// raster fallback for consumers that need pixels.
/// </summary>
public sealed class SvgImageSource : IVectorImageSource, INotifyImageChanged, IDisposable
{
    private readonly SvgDocument _document;
    private Color? _tint;
    private int? _rasterWidth;
    private int? _rasterHeight;

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

    /// <summary>
    /// Recolors the SVG fill, for monochrome icons whose elements inherit fill (no explicit per-element
    /// fill). Null keeps the source colors. Changing it re-renders the hosting control.
    /// </summary>
    public Color? Tint
    {
        get => _tint;
        set
        {
            if (_tint == value)
            {
                return;
            }
            _tint = value;
            InvalidateRaster();
            Changed?.Invoke();
        }
    }

    /// <summary>Pixel width for the <see cref="CreateImage"/> raster fallback. Null = intrinsic. Ignored by the vector path.</summary>
    public int? RasterWidth
    {
        get => _rasterWidth;
        set { if (_rasterWidth != value) { _rasterWidth = value; InvalidateRaster(); Changed?.Invoke(); } }
    }

    /// <summary>Pixel height for the <see cref="CreateImage"/> raster fallback. Null = intrinsic. Ignored by the vector path.</summary>
    public int? RasterHeight
    {
        get => _rasterHeight;
        set { if (_rasterHeight != value) { _rasterHeight = value; InvalidateRaster(); Changed?.Invoke(); } }
    }

    /// <inheritdoc />
    public Size IntrinsicSize => new(Math.Max(0, _document.ViewBoxWidth), Math.Max(0, _document.ViewBoxHeight));

    /// <inheritdoc />
    public void Render(IGraphicsContext context, Rect destRect) => RenderDocument(context, destRect);

    /// <inheritdoc />
    public IImage CreateImage(IGraphicsFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var intrinsic = IntrinsicSize;
        int width = Math.Max(1, _rasterWidth ?? (int)Math.Ceiling(intrinsic.Width));
        int height = Math.Max(1, _rasterHeight ?? (int)Math.Ceiling(intrinsic.Height));
        uint tintKey = _tint is Color t ? (uint)((t.A << 24) | (t.R << 16) | (t.G << 8) | t.B) : 0u;

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
                RenderDocument(context, new Rect(0, 0, width, height));
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

    private void RenderDocument(IGraphicsContext context, Rect destRect)
    {
        if (destRect.Width <= 0 || destRect.Height <= 0)
        {
            return;
        }

        if (_tint is Color tint)
        {
            // Override the root fill so inheriting (monochrome) icon paths pick up the tint, then restore.
            var previous = _document.Fill;
            _document.Fill = new SvgColourServer(System.Drawing.Color.FromArgb(tint.A, tint.R, tint.G, tint.B));
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
        InvalidateRaster();
    }
}
