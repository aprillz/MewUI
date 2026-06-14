namespace Aprillz.MewUI.MewDock.Model;

/// <summary>FlexLayout-specific helpers over the core <see cref="Rect"/> struct.</summary>
internal static class RectExtensions
{
    /// <summary>
    /// Point-in-rect test inclusive on all edges. FlexLayout's Rect.contains is inclusive, whereas the core
    /// <see cref="Rect.Contains(Point)"/> is half-open, so drop hit-testing uses this for faithful behavior.
    /// </summary>
    internal static bool ContainsInclusive(this Rect r, double x, double y) =>
        r.X <= x && x <= r.Right && r.Y <= y && y <= r.Bottom;

    /// <summary>Width for a horizontal orientation, height for vertical (port of Rect._getSize).</summary>
    internal static double GetSize(this Rect r, Orientation orientation) =>
        orientation == Orientation.Vertical ? r.Height : r.Width;
}
