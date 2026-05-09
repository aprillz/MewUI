using System.Xml.Linq;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Svg.Internal;

namespace Aprillz.MewUI.Svg;

/// <summary>
/// Represents a parsed SVG document.
/// <para>
/// Load an SVG from a string, stream, or file, then call
/// <see cref="Render"/> to draw it into any <see cref="IGraphicsContext"/>.
/// The implementation is pure managed code (System.Xml.Linq only), NativeAOT-compatible,
/// and has no dependency on System.Drawing.
/// </para>
/// </summary>
public sealed class SvgDocument
{
    private readonly SvgDocumentNode _root;

    private SvgDocument(SvgDocumentNode root)
    {
        _root = root;
    }

    /// <summary>Gets the intrinsic width in user units, or <c>null</c> if not specified.</summary>
    public double? IntrinsicWidth  => _root.Width;

    /// <summary>Gets the intrinsic height in user units, or <c>null</c> if not specified.</summary>
    public double? IntrinsicHeight => _root.Height;

    /// <summary>Gets the viewBox width, or the intrinsic width, falling back to 100.</summary>
    public double ViewBoxWidth  => _root.VbW ?? _root.Width  ?? 100;

    /// <summary>Gets the viewBox height, or the intrinsic height, falling back to 100.</summary>
    public double ViewBoxHeight => _root.VbH ?? _root.Height ?? 100;

    // ──────────────────────────────────────────────
    // Parsing
    // ──────────────────────────────────────────────

    /// <summary>Parses SVG from an XML string.</summary>
    public static SvgDocument Parse(string xml)
    {
        var doc = XDocument.Parse(xml, LoadOptions.None);
        return new SvgDocument(SvgXmlParser.Parse(doc));
    }

    /// <summary>Loads SVG from a stream.</summary>
    public static SvgDocument Load(Stream stream)
    {
        var doc = XDocument.Load(stream, LoadOptions.None);
        return new SvgDocument(SvgXmlParser.Parse(doc));
    }

    /// <summary>Loads SVG from a file path.</summary>
    public static SvgDocument LoadFromFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    // ──────────────────────────────────────────────
    // Rendering
    // ──────────────────────────────────────────────

    /// <summary>
    /// Renders the SVG into the given <see cref="IGraphicsContext"/>,
    /// scaled to fill <paramref name="destRect"/>.
    /// </summary>
    public void Render(IGraphicsContext ctx, Rect destRect)
    {
        new SvgRenderer(ctx, _root).Render(destRect);
    }

    // ──────────────────────────────────────────────
    // Rasterisation
    // ──────────────────────────────────────────────

    /// <summary>
    /// Rasterises the SVG at its intrinsic size and returns a <see cref="WriteableBitmap"/>
    /// (BGRA32, straight alpha). The caller is responsible for disposing the bitmap.
    /// </summary>
    public WriteableBitmap Rasterize(IGraphicsFactory factory)
    {
        int w = Math.Max(1, (int)Math.Ceiling(ViewBoxWidth));
        int h = Math.Max(1, (int)Math.Ceiling(ViewBoxHeight));
        return Rasterize(factory, w, h);
    }

    /// <summary>
    /// Rasterises the SVG at the specified pixel size and returns a <see cref="WriteableBitmap"/>
    /// (BGRA32, straight alpha). The caller is responsible for disposing the bitmap.
    /// </summary>
    public WriteableBitmap Rasterize(IGraphicsFactory factory, int pixelWidth, int pixelHeight)
    {
        var renderDevice = factory;
        using var surface = renderDevice.CreateSurface(RenderSurfaceDescriptor.CpuBitmap(
            pixelWidth,
            pixelHeight,
            debugName: "SvgDocumentRasterize"));
        if (surface is not BitmapRenderTargetSurfaceAdapter bitmapSurface)
        {
            throw new NotSupportedException($"{nameof(SvgDocument)} rasterization requires a bitmap-backed render surface.");
        }

        var target = bitmapSurface.Target;
        target.Clear(Color.Transparent);
        using (var ctx = renderDevice.CreateContext(surface))
            Render(ctx, new Rect(0, 0, pixelWidth, pixelHeight));

        var bitmap = new WriteableBitmap(pixelWidth, pixelHeight, clear: false);
        bitmap.WritePixels(0, 0, pixelWidth, pixelHeight, target.CopyPixels(), pixelWidth * 4);
        return bitmap;
    }

    /// <summary>
    /// Rasterises the SVG and returns an <see cref="IImage"/> backed by the graphics factory.
    /// The returned image is backed by a <see cref="WriteableBitmap"/>; the factory keeps a
    /// reference to it for the lifetime of the image.
    /// </summary>
    public IImage CreateImage(IGraphicsFactory factory, int pixelWidth, int pixelHeight)
    {
        var bitmap = Rasterize(factory, pixelWidth, pixelHeight);
        return factory.CreateImageView(bitmap);
    }
}
