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
[System.CLSCompliant(false)] // derives from MewObject (the MewProperty base), which is not CLS-compliant
public sealed class SvgImageSource : MewObject, IVectorImageSource, INotifyImageChanged, IDisposable
{
    /// <summary>Recolors the SVG fill (monochrome icons). Null keeps the source colors.</summary>
    public static readonly MewProperty<Color?> TintProperty =
        MewProperty<Color?>.Register<SvgImageSource>(nameof(Tint), null,
            changed: (owner, _, _) => owner.OnVisualChanged());

    /// <summary>Pixel width for the <see cref="CreateImage"/> raster fallback (null = intrinsic).</summary>
    public static readonly MewProperty<int?> RasterWidthProperty =
        MewProperty<int?>.Register<SvgImageSource>(nameof(RasterWidth), null,
            changed: (owner, _, _) => owner.OnVisualChanged());

    /// <summary>Pixel height for the <see cref="CreateImage"/> raster fallback (null = intrinsic).</summary>
    public static readonly MewProperty<int?> RasterHeightProperty =
        MewProperty<int?>.Register<SvgImageSource>(nameof(RasterHeight), null,
            changed: (owner, _, _) => owner.OnVisualChanged());

    private readonly SvgDocument _document;

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
        get => GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    /// <summary>Pixel width for the <see cref="CreateImage"/> raster fallback. Null = intrinsic. Ignored by the vector path.</summary>
    public int? RasterWidth
    {
        get => GetValue(RasterWidthProperty);
        set => SetValue(RasterWidthProperty, value);
    }

    /// <summary>Pixel height for the <see cref="CreateImage"/> raster fallback. Null = intrinsic. Ignored by the vector path.</summary>
    public int? RasterHeight
    {
        get => GetValue(RasterHeightProperty);
        set => SetValue(RasterHeightProperty, value);
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
        int width = Math.Max(1, RasterWidth ?? (int)Math.Ceiling(intrinsic.Width));
        int height = Math.Max(1, RasterHeight ?? (int)Math.Ceiling(intrinsic.Height));
        uint tintKey = Tint is Color t ? (uint)((t.A << 24) | (t.R << 16) | (t.G << 8) | t.B) : 0u;

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

        if (Tint is Color tint)
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

    // A Tint/Raster* property changed: drop the cached raster and notify the host control to repaint.
    private void OnVisualChanged()
    {
        InvalidateRaster();
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
        InvalidateRaster();
    }
}
