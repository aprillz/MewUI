namespace Aprillz.MewUI.Rendering.Filters;

/// <summary>
/// A node in an image-filter DAG. Filters are immutable, value-like graph nodes that describe
/// a transformation pipeline applied to a source image. They are evaluated by an
/// <see cref="IImageFilterExecutor"/> which walks the graph and produces a <see cref="FilterResult"/>.
/// </summary>
/// <remarks>
/// Modeled after Skia's <c>SkImageFilter</c>: each node has zero or more <see cref="Inputs"/>
/// (a <see langword="null"/> entry means "use the source layer"), and the executor recursively
/// evaluates inputs before processing the node itself. SVG <c>&lt;filter&gt;</c> primitives map
/// to these nodes 1:1 (e.g. <c>feGaussianBlur</c> → <see cref="BlurFilter"/>, <c>feMerge</c> →
/// <see cref="MergeFilter"/>), and UI-level effects (<c>UIElement.Effect = new BlurFilter(5)</c>)
/// build the same graph.
/// </remarks>
public abstract class ImageFilter
{
    /// <summary>
    /// Input filters this node consumes. A <see langword="null"/> entry means "use the
    /// executor's current source layer" — leaves of the graph that read the rendered content.
    /// </summary>
    public abstract IReadOnlyList<ImageFilter?> Inputs { get; }
}

/// <summary>
/// Reads the executor's current source layer (the rendered content the filter applies to).
/// Equivalent to a <see langword="null"/> input slot — provided as a named leaf so SVG
/// <c>SourceGraphic</c> / <c>SourceAlpha</c> references can be expressed explicitly.
/// </summary>
public sealed class SourceFilter : ImageFilter
{
    public static readonly SourceFilter Instance = new();
    private SourceFilter() { }
    public override IReadOnlyList<ImageFilter?> Inputs => Array.Empty<ImageFilter?>();
}

/// <summary>
/// Fills the entire region with a solid color. Used inside SVG drop-shadow chains
/// (<c>feFlood</c> + <c>feComposite in</c>) and as a constant input to other filters.
/// </summary>
public sealed class FloodFilter(Color color) : ImageFilter
{
    public Color Color { get; } = color;
    public override IReadOnlyList<ImageFilter?> Inputs => Array.Empty<ImageFilter?>();
}

/// <summary>
/// Separable Gaussian blur with independent X / Y standard deviations.
/// </summary>
public sealed class BlurFilter(double sigmaX, double sigmaY, ImageFilter? input = null) : ImageFilter
{
    public BlurFilter(double sigma, ImageFilter? input = null) : this(sigma, sigma, input) { }

    public double SigmaX { get; } = Math.Max(0, sigmaX);
    public double SigmaY { get; } = Math.Max(0, sigmaY);
    public ImageFilter? Input { get; } = input;

    public override IReadOnlyList<ImageFilter?> Inputs => new[] { Input };
}

/// <summary>
/// Per-pixel color transform: out = matrix * [R, G, B, A, 1]ᵀ. The 4×5 matrix is laid out
/// row-major (first row produces R'). Maps to SVG <c>feColorMatrix type="matrix"</c>.
/// </summary>
public sealed class ColorMatrixFilter(float[] matrix4x5, ImageFilter? input = null) : ImageFilter
{
    public float[] Matrix { get; } = matrix4x5 is { Length: 20 }
        ? matrix4x5
        : throw new ArgumentException("Matrix must have exactly 20 entries (4 rows × 5 cols).", nameof(matrix4x5));
    public ImageFilter? Input { get; } = input;

    public override IReadOnlyList<ImageFilter?> Inputs => new[] { Input };
}

/// <summary>
/// Translates the input image by (<see cref="Dx"/>, <see cref="Dy"/>) DIPs. Backends can fold
/// this into adjacent passes via UV offset rather than allocating a separate scratch RT.
/// </summary>
public sealed class OffsetFilter(double dx, double dy, ImageFilter? input = null) : ImageFilter
{
    public double Dx { get; } = dx;
    public double Dy { get; } = dy;
    public ImageFilter? Input { get; } = input;

    public override IReadOnlyList<ImageFilter?> Inputs => new[] { Input };
}

/// <summary>
/// Composition operators for <see cref="CompositeFilter"/> matching SVG <c>feComposite</c>
/// operator values. The "fg" / "bg" naming follows Porter–Duff: foreground over background, etc.
/// </summary>
public enum CompositeOp
{
    /// <summary>fg over bg (standard alpha blend).</summary>
    Over,
    /// <summary>fg only where bg is opaque.</summary>
    In,
    /// <summary>fg only where bg is transparent.</summary>
    Out,
    /// <summary>fg over bg, but only inside bg's opaque region.</summary>
    Atop,
    /// <summary>Symmetric difference: fg + bg minus their intersection.</summary>
    Xor,
}

/// <summary>
/// Combines two inputs using a Porter–Duff <see cref="CompositeOp"/>. Maps to SVG
/// <c>feComposite</c>. <see cref="MergeFilter"/> is preferred for N-way source-over.
/// </summary>
public sealed class CompositeFilter(ImageFilter foreground, ImageFilter background, CompositeOp op) : ImageFilter
{
    public ImageFilter Foreground { get; } = foreground ?? throw new ArgumentNullException(nameof(foreground));
    public ImageFilter Background { get; } = background ?? throw new ArgumentNullException(nameof(background));
    public CompositeOp Op { get; } = op;

    public override IReadOnlyList<ImageFilter?> Inputs => new ImageFilter?[] { Foreground, Background };
}

/// <summary>
/// Sequential composition: <c>Outer(Inner(source))</c>. Equivalent to plugging
/// <see cref="Inner"/> into the <c>Input</c> slot of <see cref="Outer"/>; provided as a
/// distinct node so layered effect builders ("blur, then drop shadow") read naturally.
/// </summary>
public sealed class ComposeFilter(ImageFilter outer, ImageFilter inner) : ImageFilter
{
    public ImageFilter Outer { get; } = outer ?? throw new ArgumentNullException(nameof(outer));
    public ImageFilter Inner { get; } = inner ?? throw new ArgumentNullException(nameof(inner));

    public override IReadOnlyList<ImageFilter?> Inputs => new ImageFilter?[] { Outer, Inner };
}

/// <summary>
/// Combines N inputs by source-over composition in order: each subsequent input is drawn
/// over the previous accumulated result. Maps to SVG <c>feMerge</c> (each <c>feMergeNode</c>
/// becomes one input). NOT additive — overlapping semi-transparent inputs blend, they don't
/// brighten beyond their individual contribution.
/// </summary>
/// <remarks>
/// Concretely: <c>result_0 = inputs[0]</c>, <c>result_i = source_over(inputs[i], result_{i-1})</c>.
/// </remarks>
public sealed class MergeFilter(params ImageFilter[] inputs) : ImageFilter
{
    public IReadOnlyList<ImageFilter> InputList { get; } = inputs ?? throw new ArgumentNullException(nameof(inputs));

    public override IReadOnlyList<ImageFilter?> Inputs => InputList!;
}

/// <summary>
/// Convenience: blurred, colored, offset copy of the input drawn behind it. Equivalent to
/// <c>Merge(Source, Composite(Flood(Color), Offset(dx, dy, Blur(σ, SourceAlpha))))</c> but
/// backends may implement as a single fused shader. Maps to SVG drop-shadow filter idioms.
/// </summary>
public enum DropShadowMode
{
    /// <summary>Output contains both the original input and the shadow underneath.</summary>
    DrawShadowAndForeground,
    /// <summary>Output contains the shadow only (input alpha used as the mask source).</summary>
    DrawShadowOnly,
}

public sealed class DropShadowFilter(double dx, double dy, double sigma, Color color,
    DropShadowMode mode = DropShadowMode.DrawShadowAndForeground, ImageFilter? input = null) : ImageFilter
{
    public double Dx { get; } = dx;
    public double Dy { get; } = dy;
    public double Sigma { get; } = Math.Max(0, sigma);
    public Color Color { get; } = color;
    public DropShadowMode Mode { get; } = mode;
    public ImageFilter? Input { get; } = input;

    public override IReadOnlyList<ImageFilter?> Inputs => new[] { Input };
}
