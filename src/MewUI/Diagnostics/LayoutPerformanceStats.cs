namespace Aprillz.MewUI.Diagnostics;

internal readonly struct LayoutPerformanceStats
{
    public LayoutPerformanceStats(double layoutMs, double measureMs, double arrangeMs, bool layoutRan, bool measureRan, bool arrangeRan)
    {
        LayoutMs = layoutMs;
        MeasureMs = measureMs;
        ArrangeMs = arrangeMs;
        LayoutRan = layoutRan;
        MeasureRan = measureRan;
        ArrangeRan = arrangeRan;
    }

    public double LayoutMs { get; }
    public double MeasureMs { get; }
    public double ArrangeMs { get; }
    public bool LayoutRan { get; }
    public bool MeasureRan { get; }
    public bool ArrangeRan { get; }
}
