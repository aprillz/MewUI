using System.Text;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Layout;

/// <summary>Two siblings laid out edge to edge, the second starting where the first ends.</summary>
internal readonly record struct MeetingPair(Element First, Element Second, Orientation Axis);

/// <summary>A tree swept across widths, and the siblings in it that must meet.</summary>
internal sealed class LayoutCase
{
    /// <summary>Builds a fresh tree; the returned element is the one whose width is swept.</summary>
    public required Func<FrameworkElement> Build { get; init; }

    public double FromWidth { get; init; } = 280;

    public double ToWidth { get; init; } = 330;

    /// <summary>The siblings of the built tree that must share an edge, or null when none must.</summary>
    public Func<FrameworkElement, IEnumerable<MeetingPair>>? Meeting { get; init; }
}

/// <summary>
/// Lays a case out across a range of widths at one DPI and checks after every layout that the tree settled, that every
/// edge sits on a device pixel, that wrapped text fits its box, that meeting siblings meet, and that the result does not
/// depend on the widths visited before.
/// </summary>
internal static class LayoutSweep
{
    public const double WIDTH_STEP = 0.37;

    private const int FRESH_COMPARE_EVERY = 9;
    private const int MAX_REPORTED = 12;
    private const double EDGE_TOLERANCE = 1e-6;

    public static void Run(LayoutCase layoutCase, uint dpi)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI text measurement is Windows-only.");
            return;
        }

        var violations = new List<string>();
        int violationCount = 0;
        void Report(string message)
        {
            violationCount++;
            if (violations.Count < MAX_REPORTED)
            {
                violations.Add(message);
            }
        }

        var root = layoutCase.Build();
        using var window = Host(root, dpi);
        double scale = dpi / 96.0;

        var widths = new List<double>();
        for (double width = layoutCase.FromWidth; width <= layoutCase.ToWidth; width += WIDTH_STEP)
        {
            widths.Add(Math.Round(width, 6));
        }

        // Up then down, so every width is reached once from a narrower and once from a wider layout.
        var path = widths.Concat(Enumerable.Reverse(widths)).ToList();
        for (int step = 0; step < path.Count; step++)
        {
            double width = path[step];
            root.Width = width;
            window.PerformLayout();
            string where = $"width {width:0.###}";

            Check(window, root, layoutCase, scale, where, Report);
            if (step % FRESH_COMPARE_EVERY == 0)
            {
                CompareWithFreshLayout(layoutCase, dpi, root, width, where, Report);
            }
        }

        if (violationCount > 0)
        {
            Assert.Fail($"{violationCount} layout violations at {dpi} DPI, first {violations.Count}:\n" + string.Join("\n", violations));
        }
    }

    /// <summary>Checks the settled tree of a window laid out at one width.</summary>
    public static void Check(Window window, FrameworkElement root, LayoutCase layoutCase, double scale, string where, Action<string> report)
    {
        if (!window.IsUpdatePassSettled)
        {
            report($"{where}: the window did not settle its layout");
        }

        Walk(root, element =>
        {
            if (element.IsMeasureDirty || element.IsArrangeDirty)
            {
                report($"{where}: {Describe(element)} left measure dirty={element.IsMeasureDirty} arrange dirty={element.IsArrangeDirty}");
            }

            var bounds = element.Bounds;
            if (!OnPixel(bounds.X, scale) || !OnPixel(bounds.Y, scale) || !OnPixel(bounds.Right, scale) || !OnPixel(bounds.Bottom, scale))
            {
                report($"{where}: {Describe(element)} bounds {Format(bounds)} are off the device pixel grid");
            }

            if (element is TextBlock text && text.TextWrapping != TextWrapping.NoWrap && bounds.Width > 0)
            {
                var (lineCount, contentHeight) = text.GetRenderLayoutMetrics();
                if (contentHeight > bounds.Height + 0.5 / scale)
                {
                    report($"{where}: {Describe(element)} draws {lineCount} lines {contentHeight:0.###} tall in a box {bounds.Height:0.###} tall at width {bounds.Width:0.###}");
                }
            }
        });

        if (layoutCase.Meeting != null)
        {
            foreach (var pair in layoutCase.Meeting(root))
            {
                var first = pair.First.Bounds;
                var second = pair.Second.Bounds;
                double end = pair.Axis == Orientation.Horizontal ? first.Right : first.Bottom;
                double start = pair.Axis == Orientation.Horizontal ? second.X : second.Y;
                if (Math.Abs(end - start) > EDGE_TOLERANCE)
                {
                    report($"{where}: {Describe(pair.First)} ends at {end:0.####} but {Describe(pair.Second)} starts at {start:0.####}");
                }
            }
        }
    }

    /// <summary>Visits the laid out elements under <paramref name="root"/>, skipping hidden subtrees.</summary>
    public static void Walk(Element root, Action<Element> visit)
    {
        if (root is UIElement ui && !ui.IsVisible)
        {
            return;
        }

        visit(root);
        if (root is IVisualTreeHost host)
        {
            host.VisitChildren(child =>
            {
                Walk(child, visit);
                return true;
            });
        }
    }

    public static Window Host(FrameworkElement root, uint dpi)
    {
        root.HorizontalAlignment = HorizontalAlignment.Left;
        root.VerticalAlignment = VerticalAlignment.Top;
        var window = HeadlessWindow.Create(1200, 900);
        window.SetDpi(dpi);
        window.Content = root;
        return window;
    }

    public static string Describe(Element element)
    {
        var path = new StringBuilder(element.GetType().Name);
        for (var current = element; current.Parent is Element parent && parent is not Window; current = parent)
        {
            int index = 0;
            int found = -1;
            if (parent is IVisualTreeHost host)
            {
                host.VisitChildren(child =>
                {
                    if (ReferenceEquals(child, current))
                    {
                        found = index;
                    }

                    index++;
                    return found < 0;
                });
            }

            path.Insert(0, $"{parent.GetType().Name}[{found}]/");
        }

        return path.ToString();
    }

    private static void CompareWithFreshLayout(LayoutCase layoutCase, uint dpi, FrameworkElement swept, double width, string where, Action<string> report)
    {
        var fresh = layoutCase.Build();
        using var window = Host(fresh, dpi);
        fresh.Width = width;
        window.PerformLayout();

        var sweptElements = Flatten(swept);
        var freshElements = Flatten(fresh);
        if (sweptElements.Count != freshElements.Count)
        {
            report($"{where}: the swept tree has {sweptElements.Count} laid out elements, a fresh one {freshElements.Count}");
            return;
        }

        for (int index = 0; index < sweptElements.Count; index++)
        {
            var reached = sweptElements[index];
            var built = freshElements[index];
            if (reached.Bounds != built.Bounds || reached.DesiredSize != built.DesiredSize)
            {
                report($"{where}: {Describe(reached)} has bounds {Format(reached.Bounds)} desired {reached.DesiredSize} after the sweep but {Format(built.Bounds)} desired {built.DesiredSize} laid out fresh");
                return;
            }
        }
    }

    private static List<Element> Flatten(Element root)
    {
        var elements = new List<Element>();
        Walk(root, elements.Add);
        return elements;
    }

    private static bool OnPixel(double value, double scale)
    {
        if (!double.IsFinite(value))
        {
            return true;
        }

        double pixels = value * scale;
        return Math.Abs(pixels - Math.Round(pixels)) < 1e-4;
    }

    private static string Format(Rect rect) => $"({rect.X:0.####}, {rect.Y:0.####}, {rect.Width:0.####} x {rect.Height:0.####})";
}
