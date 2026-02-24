namespace Aprillz.MewUI;

/// <summary>
/// Describes how a window should be sized and whether it is resizable.
/// </summary>
/// <remarks>
/// Width/Height are expressed in DIPs. A value of <see cref="double.NaN"/> indicates that the dimension should be
/// derived from content, subject to the configured maximums.
/// </remarks>
public readonly struct WindowSize
{
    internal WindowSizeMode Mode { get; }

    /// <summary>
    /// Gets the requested width in DIPs (or <see cref="double.NaN"/> when content-driven).
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// Gets the requested height in DIPs (or <see cref="double.NaN"/> when content-driven).
    /// </summary>
    public double Height { get; }

    /// <summary>
    /// Gets the minimum allowed width in DIPs. Zero means no minimum constraint.
    /// </summary>
    public double MinWidth { get; }

    /// <summary>
    /// Gets the minimum allowed height in DIPs. Zero means no minimum constraint.
    /// </summary>
    public double MinHeight { get; }

    /// <summary>
    /// Gets the maximum allowed width in DIPs.
    /// </summary>
    public double MaxWidth { get; }

    /// <summary>
    /// Gets the maximum allowed height in DIPs.
    /// </summary>
    public double MaxHeight { get; }

    private WindowSize(WindowSizeMode mode, double width, double height,
        double minWidth, double minHeight, double maxWidth, double maxHeight)
    {
        Mode = mode;
        Width = width;
        Height = height;
        MinWidth = minWidth;
        MinHeight = minHeight;
        MaxWidth = maxWidth;
        MaxHeight = maxHeight;
    }

    internal bool IsResizable => Mode == WindowSizeMode.Resizable;

    /// <summary>
    /// Creates a resizable window size configuration with the specified client size and optional min/max constraints.
    /// </summary>
    public static WindowSize Resizable(double width, double height,
        double minWidth = 0, double minHeight = 0,
        double maxWidth = double.PositiveInfinity, double maxHeight = double.PositiveInfinity)
        => new(WindowSizeMode.Resizable, width, height, minWidth, minHeight, maxWidth, maxHeight);

    /// <summary>
    /// Creates a resizable window with minimum size constraints.
    /// </summary>
    public static WindowSize ResizableWithMin(double width, double height, double minWidth, double minHeight)
        => new(WindowSizeMode.Resizable, width, height, minWidth, minHeight, double.PositiveInfinity, double.PositiveInfinity);

    /// <summary>
    /// Creates a resizable window with maximum size constraints.
    /// </summary>
    public static WindowSize ResizableWithMax(double width, double height, double maxWidth, double maxHeight)
        => new(WindowSizeMode.Resizable, width, height, 0, 0, maxWidth, maxHeight);

    /// <summary>
    /// Creates a fixed-size window configuration with the specified client size.
    /// </summary>
    public static WindowSize Fixed(double width, double height)
        => new(WindowSizeMode.Fixed, width, height, width, height, width, height);

    /// <summary>
    /// Creates a configuration where the content determines width, up to <paramref name="maxWidth"/>.
    /// Height is fixed.
    /// </summary>
    public static WindowSize FitContentWidth(double maxWidth, double fixedHeight)
        => new(WindowSizeMode.FitContentWidth, double.NaN, fixedHeight, 0, 0, maxWidth, fixedHeight);

    /// <summary>
    /// Creates a configuration where the content determines height, up to <paramref name="maxHeight"/>.
    /// Width is fixed.
    /// </summary>
    public static WindowSize FitContentHeight(double fixedWidth, double maxHeight)
        => new(WindowSizeMode.FitContentHeight, fixedWidth, double.NaN, 0, 0, fixedWidth, maxHeight);

    /// <summary>
    /// Creates a configuration where the content determines both width and height, up to the specified maximums.
    /// </summary>
    public static WindowSize FitContentSize(double maxWidth, double maxHeight)
        => new(WindowSizeMode.FitContentSize, double.NaN, double.NaN, 0, 0, maxWidth, maxHeight);
}

/// <summary>
/// Internal sizing modes used by <see cref="WindowSize"/>.
/// </summary>
internal enum WindowSizeMode
{
    Resizable,
    Fixed,
    FitContentWidth,
    FitContentHeight,
    FitContentSize,
}
