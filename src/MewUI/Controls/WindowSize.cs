namespace Aprillz.MewUI.Controls;

public readonly struct WindowSize
{
    internal WindowSizeMode Mode { get; }

    public double Width { get; }
    public double Height { get; }

    public double MaxWidth { get; }
    public double MaxHeight { get; }

    private WindowSize(WindowSizeMode mode, double width, double height, double maxWidth, double maxHeight)
    {
        Mode = mode;
        Width = width;
        Height = height;
        MaxWidth = maxWidth;
        MaxHeight = maxHeight;
    }

    internal bool IsResizable => Mode == WindowSizeMode.Resizable;

    public static WindowSize Resizable(double width, double height)
        => new(WindowSizeMode.Resizable, width, height, double.PositiveInfinity, double.PositiveInfinity);

    public static WindowSize Fixed(double width, double height)
        => new(WindowSizeMode.Fixed, width, height, width, height);

    public static WindowSize FitContentWidth(double maxWidth, double fixedHeight)
        => new(WindowSizeMode.FitContentWidth, double.NaN, fixedHeight, maxWidth, fixedHeight);

    public static WindowSize FitContentHeight(double fixedWidth, double maxHeight)
        => new(WindowSizeMode.FitContentHeight, fixedWidth, double.NaN, fixedWidth, maxHeight);

    public static WindowSize FitContentSize(double maxWidth, double maxHeight)
        => new(WindowSizeMode.FitContentSize, double.NaN, double.NaN, maxWidth, maxHeight);
}

internal enum WindowSizeMode
{
    Resizable,
    Fixed,
    FitContentWidth,
    FitContentHeight,
    FitContentSize,
}

