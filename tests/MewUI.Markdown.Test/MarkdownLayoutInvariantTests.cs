using System.Text;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace Aprillz.MewUI.Markdown.Test;

/// <summary>
/// A Markdown document settles every width and scroll step with each paragraph's lines inside its box and every edge
/// on a device pixel, at display scales from 100% to 200%.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MarkdownLayoutInvariantTests
{
    private const int WIDTH_STEPS = 12;
    private const double WIDTH_STEP = 0.37;
    private const int SCROLL_STEPS = 30;
    private const double SCROLL_STEP = 37.3;

    [TestMethod]
    [DataRow(96u)]
    [DataRow(120u)]
    [DataRow(144u)]
    [DataRow(168u)]
    [DataRow(192u)]
    public void DocumentAcrossWidthsAndScrollSteps(uint dpi)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI text measurement is Windows-only.");
            return;
        }

        GdiBackend.Register();
        var viewer = new MarkdownViewer
        {
            Markdown = Document(),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Height = 500.3,
        };
        var window = HeadlessWindow.Create(1000, 700);
        window.SetDpi(dpi);
        window.Content = viewer;
        double scale = dpi / 96.0;
        var scroll = Find<ScrollViewer>(viewer);

        var violations = new List<string>();
        int paragraphs = 0;
        for (int widthStep = 0; widthStep < WIDTH_STEPS; widthStep++)
        {
            viewer.Width = 610 + widthStep * WIDTH_STEP;
            window.PerformLayout();
            scroll ??= Find<ScrollViewer>(viewer);
            for (int step = 0; step <= SCROLL_STEPS; step++)
            {
                scroll?.SetScrollOffsets(0, step * SCROLL_STEP * (widthStep + 1));
                window.PerformLayout();
                paragraphs += Check(window, viewer, scale, $"width {viewer.Width:0.##} offset {scroll?.VerticalOffset:0.##}", violations);
            }
        }

        Assert.IsGreaterThan(0, paragraphs, "no paragraph was laid out, so the checks above prove nothing");
        if (violations.Count > 0)
        {
            Assert.Fail($"{violations.Count} layout violations at {dpi} DPI, first:\n" + string.Join("\n", violations.Take(12)));
        }
    }

    /// <summary>Checks the settled tree and returns how many paragraphs it checked.</summary>
    private static int Check(Window window, Element root, double scale, string where, List<string> violations)
    {
        int paragraphs = 0;
        if (!window.IsUpdatePassSettled)
        {
            violations.Add($"{where}: the window did not settle its layout");
        }

        Walk(root, element =>
        {
            if (element.IsMeasureDirty || element.IsArrangeDirty)
            {
                violations.Add($"{where}: {element.GetType().Name} left measure dirty={element.IsMeasureDirty} arrange dirty={element.IsArrangeDirty}");
            }

            var bounds = element.Bounds;
            if (!OnPixel(bounds.X, scale) || !OnPixel(bounds.Y, scale) || !OnPixel(bounds.Right, scale) || !OnPixel(bounds.Bottom, scale))
            {
                violations.Add($"{where}: {element.GetType().Name} bounds {bounds} are off the device pixel grid");
            }

            if (element is MarkdownParagraph paragraph && bounds.Width > 0)
            {
                paragraphs++;
                double contentHeight = paragraph.GetLayout(bounds.Width).ContentHeight;
                if (contentHeight > bounds.Height + 0.5 / scale)
                {
                    violations.Add($"{where}: paragraph '{Preview(paragraph.Text)}' draws {contentHeight:0.###} tall in a box {bounds.Height:0.###} tall at width {bounds.Width:0.###}");
                }
            }
        });
        return paragraphs;
    }

    private static void Walk(Element element, Action<Element> visit)
    {
        if (element is UIElement ui && !ui.IsVisible)
        {
            return;
        }

        visit(element);
        if (element is IVisualTreeHost host)
        {
            host.VisitChildren(child =>
            {
                Walk(child, visit);
                return true;
            });
        }
    }

    private static T? Find<T>(Element root) where T : Element
    {
        T? found = null;
        VisualTree.Visit(root, element =>
        {
            if (found == null && element is T match)
            {
                found = match;
            }
        });
        return found;
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

    private static string Preview(string text) => text.Length <= 30 ? text : text[..30];

    private static string Document()
    {
        var builder = new StringBuilder();
        for (int index = 0; index < 60; index++)
        {
            switch (index % 6)
            {
                case 0:
                    builder.Append("## Section ").Append(index).Append("\n\n");
                    break;
                case 1:
                    builder.Append("Block ").Append(index).Append(" is a long paragraph that wraps across several lines because it keeps going on about **bold** details, [links](https://example.com/")
                        .Append(index).Append("), and `inline code` until the width runs out.\n\n");
                    break;
                case 2:
                    builder.Append("| Key | Value ").Append(index).Append(" | Notes |\n| --- | --- | --- |\n| alpha | 1 | a cell long enough to wrap inside its column of the table |\n| beta | 2 | short |\n\n");
                    break;
                case 3:
                    builder.Append("> Quote ").Append(index).Append(" with a second line that runs on and on so it wraps inside the quote block.\n\n");
                    break;
                case 4:
                    builder.Append("- item ").Append(index).Append(" with enough words to wrap when the list is narrow\n- another item\n\n");
                    break;
                default:
                    builder.Append("```csharp\nvar value").Append(index).Append(" = 1;\n```\n\n");
                    break;
            }
        }

        return builder.ToString();
    }
}
