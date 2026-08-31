namespace Aprillz.MewUI.Rendering.Filters;

/// <summary>
/// Maps an output rectangle back to the source rectangle a filter graph needs to produce it.
/// Rasterizing only that rectangle keeps a filtered element's source layer bounded by the
/// viewport instead of the element's full extent, which at high zoom is the difference between
/// a viewport-sized surface and a multi-hundred-megabyte one.
/// </summary>
public static class ImageFilterBounds
{
    /// <summary>
    /// The source rectangle <paramref name="filter"/> reads to produce <paramref name="output"/>,
    /// in the same coordinate space as <paramref name="output"/>. Returns null when the graph
    /// contains a node whose input requirement cannot be bounded, meaning the caller must
    /// rasterize its whole filter region.
    /// </summary>
    public static Rect? TryGetSourceRect(ImageFilter? filter, Rect output)
    {
        if (output.Width <= 0 || output.Height <= 0)
        {
            return output;
        }

        // A null graph draws the source as-is.
        return filter is null ? output : Accumulate(filter, output, depth: 0);
    }

    // Guards against a graph that cycles through a shared node; SVG builders produce DAGs, but
    // this runs on documents the application did not author.
    private const int MAX_DEPTH = 64;

    private static Rect? Accumulate(ImageFilter filter, Rect output, int depth)
    {
        if (depth > MAX_DEPTH)
        {
            return null;
        }

        switch (filter)
        {
            // The source layer itself: what this node needs is exactly what was asked of it.
            case SourceFilter:
                return output;

            // Generates its own pixels, so it reads nothing from the source.
            case FloodFilter:
                return Rect.Empty;

            case BlurFilter blur:
                return AccumulateInput(
                    blur.Input,
                    Expand(output, blur.RadiusX, blur.RadiusY),
                    depth);

            case OffsetFilter offset:
                // The output at (x, y) comes from the input at (x - dx, y - dy).
                return AccumulateInput(
                    offset.Input,
                    new Rect(output.X - offset.Dx, output.Y - offset.Dy, output.Width, output.Height),
                    depth);

            // Per-pixel: reads exactly the pixel it writes.
            case ColorMatrixFilter colorMatrix:
                return AccumulateInput(colorMatrix.Input, output, depth);

            case DropShadowFilter dropShadow:
                // The shadow branch blurs then offsets; the foreground branch reads the output
                // directly, and the union of the two is what the input has to cover.
                {
                    var shadow = Expand(
                        new Rect(output.X - dropShadow.Dx, output.Y - dropShadow.Dy, output.Width, output.Height),
                        dropShadow.Radius,
                        dropShadow.Radius);
                    var needed = dropShadow.Mode == DropShadowMode.DrawShadowOnly
                        ? shadow
                        : Union(shadow, output);
                    return AccumulateInput(dropShadow.Input, needed, depth);
                }

            case CompositeFilter composite:
                return UnionOf(
                    Accumulate(composite.Foreground, output, depth + 1),
                    Accumulate(composite.Background, output, depth + 1));

            case ComposeFilter compose:
            {
                // Outer(Inner(source)): what Outer needs of its input is what Inner must produce.
                var innerOutput = Accumulate(compose.Outer, output, depth + 1);
                return innerOutput is { } needed ? Accumulate(compose.Inner, needed, depth + 1) : null;
            }

            case MergeFilter merge:
            {
                Rect? total = Rect.Empty;
                foreach (var input in merge.InputList)
                {
                    total = UnionOf(total, Accumulate(input, output, depth + 1));
                    if (total is null)
                    {
                        return null;
                    }
                }
                return total;
            }

            // An unrecognized node may read anywhere, so the caller keeps its whole region.
            default:
                return null;
        }
    }

    /// <summary>Follows an optional input slot; a null slot means the source layer.</summary>
    private static Rect? AccumulateInput(ImageFilter? input, Rect output, int depth)
        => input is null ? output : Accumulate(input, output, depth + 1);

    private static Rect Expand(Rect rect, double padX, double padY)
    {
        padX = Math.Max(0, padX);
        padY = Math.Max(0, padY);
        return new Rect(rect.X - padX, rect.Y - padY, rect.Width + 2 * padX, rect.Height + 2 * padY);
    }

    private static Rect? UnionOf(Rect? first, Rect? second)
    {
        if (first is not { } a || second is not { } b)
        {
            return null;
        }
        return Union(a, b);
    }

    private static Rect Union(Rect first, Rect second)
    {
        if (first.Width <= 0 || first.Height <= 0) return second;
        if (second.Width <= 0 || second.Height <= 0) return first;

        double left = Math.Min(first.X, second.X);
        double top = Math.Min(first.Y, second.Y);
        double right = Math.Max(first.Right, second.Right);
        double bottom = Math.Max(first.Bottom, second.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }
}
