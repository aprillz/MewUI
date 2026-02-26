namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Specifies the fill rule used to determine the interior of a path.
/// </summary>
public enum FillRule
{
    /// <summary>
    /// Non-zero winding number rule: a point is inside if the path winds around it at least once.
    /// This is the default fill rule (equivalent to SVG <c>nonzero</c> and WPF <c>Nonzero</c>).
    /// </summary>
    NonZero = 0,

    /// <summary>
    /// Even-odd rule: a point is inside if the path crosses it an odd number of times.
    /// Equivalent to the SVG <c>evenodd</c> fill-rule and WPF <c>EvenOdd</c>.
    /// </summary>
    EvenOdd = 1,
}
