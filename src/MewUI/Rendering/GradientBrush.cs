using System.Numerics;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Base class for gradient brushes.
/// <para>
/// The color stops are copied into a private array at construction, so a gradient
/// brush is fully immutable regardless of the list the caller supplies.
/// </para>
/// </summary>
public abstract class GradientBrush : Brush
{
    private readonly GradientStop[] _stops;

    /// <summary>Gets the color stops that define the gradient.</summary>
    public IReadOnlyList<GradientStop> Stops => _stops;

    /// <summary>Gets how the gradient extends beyond its defined bounds.</summary>
    public SpreadMethod SpreadMethod { get; }

    /// <summary>Gets the coordinate space used for gradient geometry.</summary>
    public GradientUnits GradientUnits { get; }

    /// <summary>
    /// Gets an optional additional transform applied to the gradient geometry,
    /// or <see langword="null"/> for no additional transform.
    /// </summary>
    public Matrix3x2? GradientTransform { get; }

    private protected GradientBrush(
        IReadOnlyList<GradientStop> stops,
        SpreadMethod spreadMethod,
        GradientUnits gradientUnits,
        Matrix3x2? gradientTransform)
    {
        _stops = stops is null ? [] : [.. stops];
        SpreadMethod = spreadMethod;
        GradientUnits = gradientUnits;
        GradientTransform = gradientTransform;
    }

    // Compares the stop sequence and the shared gradient parameters. Subclasses
    // fold their own geometry into Equals on top of this.
    private protected bool CommonEquals(GradientBrush other) =>
        SpreadMethod == other.SpreadMethod &&
        GradientUnits == other.GradientUnits &&
        Nullable.Equals(GradientTransform, other.GradientTransform) &&
        StopsEqual(_stops, other._stops);

    // Bounded hash: stop count plus the first and last stop plus the shared parameters.
    // Equal brushes always yield this same value; the full stop sequence is only walked
    // in CommonEquals to resolve collisions.
    private protected int CommonHashCode()
    {
        var hash = new HashCode();
        hash.Add(_stops.Length);
        if (_stops.Length > 0)
        {
            AddStop(ref hash, _stops[0]);
            AddStop(ref hash, _stops[^1]);
        }
        hash.Add(SpreadMethod);
        hash.Add(GradientUnits);
        hash.Add(GradientTransform);
        return hash.ToHashCode();
    }

    private static void AddStop(ref HashCode hash, GradientStop stop)
    {
        hash.Add(stop.Offset);
        hash.Add(stop.Color);
    }

    private static bool StopsEqual(GradientStop[] left, GradientStop[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i].Offset != right[i].Offset || !left[i].Color.Equals(right[i].Color))
                return false;
        }
        return true;
    }
}
