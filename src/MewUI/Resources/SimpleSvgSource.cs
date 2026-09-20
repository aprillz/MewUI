using System.Reflection;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Resources;

/// <summary>
/// Draws an SVG that reduces to plain fills, strokes and clips, without the MewUI.Svg extension.
/// Shapes, groups, transforms, inherited presentation attributes, inline <c>style</c>, solid and
/// gradient paint, and <c>clipPath</c> are read; <c>style</c> blocks, <c>class</c>, <c>filter</c>,
/// <c>mask</c>, <c>pattern</c>, <c>text</c>, <c>image</c> and <c>use</c> are not.
/// </summary>
public sealed class SimpleSvgSource : MewObject, IVectorImageSource, INotifyImageChanged
{
    /// <summary>Replaces the fill and stroke colors inherited from the root. Null keeps the source colors.</summary>
    public static readonly MewProperty<Color?> TintProperty =
        MewProperty<Color?>.Register<SimpleSvgSource>(nameof(Tint), null,
            changed: (owner, _, _) => owner.Changed?.Invoke());

    private readonly SimpleSvgDocument _document;

    private SimpleSvgSource(SimpleSvgDocument document) => _document = document;

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>
    /// Replaces the fill and stroke colors that were inherited from the root element, which is what
    /// recolors a monochrome icon. Paint declared on a shape or an intermediate group is left alone.
    /// </summary>
    public Color? Tint
    {
        get => GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    /// <inheritdoc />
    public Size IntrinsicSize => _document.IntrinsicSize;

    /// <summary>Reads SVG markup. Throws <see cref="FormatException"/> when the markup is malformed.</summary>
    public static SimpleSvgSource FromString(string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);
        return new SimpleSvgSource(SimpleSvgReader.Read(markup));
    }

    /// <summary>Reads UTF-8 SVG markup.</summary>
    public static SimpleSvgSource FromBytes(ReadOnlySpan<byte> utf8) =>
        FromString(System.Text.Encoding.UTF8.GetString(utf8));

    /// <summary>Reads an SVG file.</summary>
    public static SimpleSvgSource FromFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return FromString(File.ReadAllText(path));
    }

    /// <summary>Reads an SVG from an embedded assembly resource.</summary>
    public static SimpleSvgSource FromResource(Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new ArgumentException($"Resource '{resourceName}' not found.", nameof(resourceName));
        using var reader = new StreamReader(stream);
        return FromString(reader.ReadToEnd());
    }

    /// <inheritdoc />
    public void Render(IGraphicsContext context, Rect destRect)
    {
        ArgumentNullException.ThrowIfNull(context);
        _document.Render(context, destRect, Tint);
    }

    /// <inheritdoc />
    public IImage CreateImage(IGraphicsFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var intrinsic = IntrinsicSize;
        int width = Math.Max(1, (int)Math.Ceiling(intrinsic.Width));
        int height = Math.Max(1, (int)Math.Ceiling(intrinsic.Height));

        var surface = factory.CreateSurface(
            RenderSurfaceDescriptor.CachedImage(width, height, 1.0, nameof(SimpleSvgSource)));
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

        return factory.CreateImageView(surface);
    }
}
