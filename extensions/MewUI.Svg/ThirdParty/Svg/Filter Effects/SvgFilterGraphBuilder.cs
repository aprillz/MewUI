using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Filters;
using System.Globalization;

namespace Svg.FilterEffects;

/// <summary>
/// Walks an <see cref="SvgFilter"/>'s child filter primitives in document order and produces
/// an <see cref="ImageFilter"/> DAG that can be evaluated by an <see cref="IImageFilterExecutor"/>.
/// Honors the SVG <c>in</c> / <c>result</c> chain semantics: each primitive's <c>in</c>
/// resolves to either a previously named <c>result</c>, a SourceGraphic-style sentinel, or
/// (when omitted) the previous primitive's output per spec.
/// </summary>
internal static class SvgFilterGraphBuilder
{
    /// <summary>
    /// Builds an <see cref="ImageFilter"/> graph from <paramref name="primitives"/>. Returns
    /// <see langword="null"/> when the chain is empty or unbuildable. The returned filter
    /// represents the LAST primitive in document order (which is the filter's output per
    /// SVG spec).
    /// </summary>
    /// <remarks>
    /// All primitive parameters (<c>stdDeviation</c>, <c>dx/dy</c>) are kept in user-space
    /// units which equal MewUI logical (DIP) units in this rendering setup. The executor
    /// applies the input-to-pixel scale at evaluation time via
    /// <see cref="IImageFilterContext.LogicalToPixelScaleX"/>, so the same graph stays valid
    /// across zoom levels and DPIs.
    /// </remarks>
    public static ImageFilter? Build(IReadOnlyList<SvgFilterPrimitive> primitives, ISvgRenderer renderer)
    {
        if (primitives.Count == 0)
        {
            return null;
        }

        // SVG: a primitive without `in` consumes either SourceGraphic (first primitive) or
        // the previous primitive's output (subsequent ones). Track named results in a map so
        // explicit `in="..."` references can resolve.
        var resultMap = new Dictionary<string, ImageFilter>(StringComparer.Ordinal);
        ImageFilter? previousOutput = null;

        foreach (var primitive in primitives)
        {
            ImageFilter? input = ResolveInputName(primitive.Input, resultMap, previousOutput);
            ImageFilter node = BuildNode(primitive, input, resultMap, previousOutput, renderer);

            if (!string.IsNullOrEmpty(primitive.Result))
            {
                resultMap[primitive.Result] = node;
            }

            previousOutput = node;
        }

        return previousOutput;
    }

    private static ImageFilter? ResolveInputName(string? name,
        Dictionary<string, ImageFilter> resultMap, ImageFilter? previousOutput)
    {
        if (string.IsNullOrEmpty(name))
        {
            // Per SVG spec: empty `in` on the first primitive defaults to SourceGraphic;
            // on subsequent ones, defaults to the previous primitive's output. We model
            // that by returning previousOutput here (null on first call), which the
            // executor's null-input convention treats as the source.
            return previousOutput;
        }

        return name switch
        {
            SvgFilterPrimitive.SourceGraphic => SourceFilter.Instance,
            // SVG spec: SourceAlpha = the source graphic's alpha channel only, with RGB
            // forced to zero. Implemented as a ColorMatrix on top of SourceFilter — the
            // backend already supports feColorMatrix, so this composes naturally without
            // a new primitive type. Drop-shadow filters depend on this (gaussian blur of
            // SourceAlpha → dark halo behind the source graphic).
            SvgFilterPrimitive.SourceAlpha => new ColorMatrixFilter(SourceAlphaMatrix, SourceFilter.Instance),
            SvgFilterPrimitive.BackgroundImage or
            SvgFilterPrimitive.BackgroundAlpha or
            SvgFilterPrimitive.FillPaint or
            SvgFilterPrimitive.StrokePaint => SourceFilter.Instance, // TODO
            _ => resultMap.TryGetValue(name, out var named) ? named : previousOutput,
        };
    }

    // 4×5 ColorMatrix that zeroes RGB and preserves alpha — equivalent to SourceAlpha.
    private static readonly float[] SourceAlphaMatrix =
    [
        0, 0, 0, 0, 0,
        0, 0, 0, 0, 0,
        0, 0, 0, 0, 0,
        0, 0, 0, 1, 0,
    ];

    private static ImageFilter BuildNode(SvgFilterPrimitive primitive, ImageFilter? input,
        Dictionary<string, ImageFilter> resultMap, ImageFilter? previousOutput, ISvgRenderer renderer) => primitive switch
    {
        // stdDeviation / dx / dy are in user space (= logical/DIP); the executor multiplies
        // by IImageFilterContext.LogicalToPixelScale at evaluation time.
        SvgGaussianBlur b => new BlurFilter(GetSigmaX(b), GetSigmaY(b), input),

        SvgFlood f => new FloodFilter(ExtractFloodColor(f)),

        SvgOffset o => new OffsetFilter(
            o.Dx.ToDeviceValue(renderer, UnitRenderingType.Horizontal, o),
            o.Dy.ToDeviceValue(renderer, UnitRenderingType.Vertical, o),
            input),

        SvgColourMatrix cm => new ColorMatrixFilter(BuildColorMatrix(cm), input),

        SvgMerge m => BuildMerge(m, resultMap, previousOutput),

        SvgComposite c => new CompositeFilter(
            foreground: input ?? SourceFilter.Instance,
            background: ResolveInputName(c.Input2, resultMap, previousOutput) ?? SourceFilter.Instance,
            op: MapCompositeOp(c.Operator)),

        // Unknown primitive: pass through (next node sees the input as if this didn't exist).
        // Logged elsewhere in a real impl; for now silently skip.
        _ => input ?? SourceFilter.Instance,
    };

    private static double GetSigmaX(SvgGaussianBlur blur)
        => blur.StdDeviation.Count >= 1 ? Math.Max(0, blur.StdDeviation[0]) : 0;

    private static double GetSigmaY(SvgGaussianBlur blur) => blur.StdDeviation.Count switch
    {
        >= 2 => Math.Max(0, blur.StdDeviation[1]),
        1 => Math.Max(0, blur.StdDeviation[0]),
        _ => 0,
    };

    private static Aprillz.MewUI.Color ExtractFloodColor(SvgFlood flood)
    {
        // SvgFlood.FloodColor is an SvgPaintServer. Most commonly an SvgColourServer.
        if (flood.FloodColor is SvgColourServer cs)
        {
            var c = cs.Colour;
            return Aprillz.MewUI.Color.FromArgb(c.A, c.R, c.G, c.B);
        }
        return Aprillz.MewUI.Color.Black;
    }

    private static float[] BuildColorMatrix(SvgColourMatrix matrix)
    {
        return matrix.Type switch
        {
            SvgColourMatrixType.HueRotate => BuildHueRotateMatrix(
                string.IsNullOrWhiteSpace(matrix.Values)
                    ? 0f
                    : float.Parse(matrix.Values, NumberStyles.Any, CultureInfo.InvariantCulture)),

            SvgColourMatrixType.LuminanceToAlpha =>
            [
                0, 0, 0, 0, 0,
                0, 0, 0, 0, 0,
                0, 0, 0, 0, 0,
                0.2125f, 0.7154f, 0.0721f, 0, 0,
            ],

            SvgColourMatrixType.Saturate => BuildSaturateMatrix(
                string.IsNullOrWhiteSpace(matrix.Values)
                    ? 1f
                    : float.Parse(matrix.Values, NumberStyles.Any, CultureInfo.InvariantCulture)),

            _ => ParseMatrix(matrix.Values),
        };
    }

    private static float[] BuildHueRotateMatrix(float value)
    {
        var cos = MathF.Cos(value);
        var sin = MathF.Sin(value);
        return
        [
            0.213f + cos * 0.787f + sin * -0.213f,
            0.715f + cos * -0.715f + sin * -0.715f,
            0.072f + cos * -0.072f + sin * 0.928f,
            0,
            0,

            0.213f + cos * -0.213f + sin * 0.143f,
            0.715f + cos * 0.285f + sin * 0.140f,
            0.072f + cos * -0.072f + sin * -0.283f,
            0,
            0,

            0.213f + cos * -0.213f + sin * -0.787f,
            0.715f + cos * -0.715f + sin * 0.715f,
            0.072f + cos * 0.928f + sin * 0.072f,
            0,
            0,

            0,
            0,
            0,
            1,
            0,
        ];
    }

    private static float[] BuildSaturateMatrix(float value)
    {
        return
        [
            0.213f + 0.787f * value,
            0.715f - 0.715f * value,
            0.072f - 0.072f * value,
            0,
            0,

            0.213f - 0.213f * value,
            0.715f + 0.285f * value,
            0.072f - 0.072f * value,
            0,
            0,

            0.213f - 0.213f * value,
            0.715f - 0.715f * value,
            0.072f + 0.928f * value,
            0,
            0,

            0,
            0,
            0,
            1,
            0,
        ];
    }

    private static float[] ParseMatrix(string? values)
    {
        var result = IdentityColorMatrix();
        if (string.IsNullOrWhiteSpace(values))
        {
            return result;
        }

        var parts = values.Split([' ', '\t', '\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 20)
        {
            return result;
        }

        for (int i = 0; i < 20; i++)
        {
            result[i] = float.Parse(parts[i], NumberStyles.Any, CultureInfo.InvariantCulture);
        }

        return result;
    }

    private static float[] IdentityColorMatrix() =>
    [
        1, 0, 0, 0, 0,
        0, 1, 0, 0, 0,
        0, 0, 1, 0, 0,
        0, 0, 0, 1, 0,
    ];

    private static ImageFilter BuildMerge(SvgMerge merge,
        Dictionary<string, ImageFilter> resultMap, ImageFilter? previousOutput)
    {
        var inputs = new List<ImageFilter>();
        foreach (var child in merge.Children)
        {
            if (child is SvgMergeNode node)
            {
                var inputFilter = ResolveInputName(node.Input, resultMap, previousOutput)
                    ?? SourceFilter.Instance;
                inputs.Add(inputFilter);
            }
        }
        return inputs.Count == 0
            ? SourceFilter.Instance  // empty merge → just pass source through
            : new MergeFilter(inputs.ToArray());
    }

    private static CompositeOp MapCompositeOp(SvgCompositeOperator op) => op switch
    {
        SvgCompositeOperator.Over => CompositeOp.Over,
        SvgCompositeOperator.In => CompositeOp.In,
        SvgCompositeOperator.Out => CompositeOp.Out,
        SvgCompositeOperator.Atop => CompositeOp.Atop,
        SvgCompositeOperator.Xor => CompositeOp.Xor,
        _ => CompositeOp.Over,
    };
}
