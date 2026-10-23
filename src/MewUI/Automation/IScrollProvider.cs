namespace Aprillz.MewUI.Automation;

/// <summary>How far a scroll request moves.</summary>
public enum ScrollAmount
{
    LargeDecrement,
    SmallDecrement,
    NoAmount,
    LargeIncrement,
    SmallIncrement,
}

/// <summary>A control that scrolls its content.</summary>
public interface IScrollProvider
{
    /// <summary>Use for a percent that does not change, in <see cref="SetScrollPercent"/>.</summary>
    static double NoScroll => -1;

    bool HorizontallyScrollable { get; }

    bool VerticallyScrollable { get; }

    /// <summary>The horizontal offset as a percent (0 to 100) of the scrollable range.</summary>
    double HorizontalScrollPercent { get; }

    /// <summary>The vertical offset as a percent (0 to 100) of the scrollable range.</summary>
    double VerticalScrollPercent { get; }

    /// <summary>The visible width as a percent of the content width.</summary>
    double HorizontalViewSize { get; }

    /// <summary>The visible height as a percent of the content height.</summary>
    double VerticalViewSize { get; }

    void Scroll(ScrollAmount horizontalAmount, ScrollAmount verticalAmount);

    void SetScrollPercent(double horizontalPercent, double verticalPercent);
}

/// <summary>A child that its scrolling container can bring into view.</summary>
public interface IScrollItemProvider
{
    void ScrollIntoView();
}
