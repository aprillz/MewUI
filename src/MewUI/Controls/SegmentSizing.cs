namespace Aprillz.MewUI.Controls;

/// <summary>
/// Controls how a <see cref="SegmentedBase"/> (<see cref="SegmentedControl"/> / <see cref="ButtonGroup"/>)
/// sizes its segments along the horizontal axis.
/// </summary>
public enum SegmentSizing
{
    /// <summary>Each segment takes its own content width (toolbar-like).</summary>
    Auto,

    /// <summary>All segments share an equal width.</summary>
    Uniform,
}
