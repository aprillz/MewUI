using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Rendering;
using MewUI.MewvalonEdit.Test.Infrastructure;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// The selection outline on a scaled display. Snapping rounds against the pixel grid the window
/// reports, so a case that is right at 100% can still be wrong at 125% or 150%.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class SelectionGeometryScaleTests
{
    // The original's Custom Highlighting document: tabs, lines of very different lengths, and short
    // lines between long ones, which is the shape the selection outline goes wrong on.
    private const string TEXT = """
        <?xml version="1.0"?>
        <SyntaxDefinition name="Custom Highlighting" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
        	<Color name="Comment" foreground="Green" />
        	<Color name="String" foreground="Blue" />

        	<!-- This is the main ruleset. -->
        	<RuleSet>
        		<Span color="Comment" begin="//" />
        		<Span color="Comment" multiline="true" begin="/\*" end="\*/" />

        		<Span color="String">
        			<Begin>"</Begin>
        			<End>"</End>
        			<RuleSet>
        				<!-- nested span for escape sequences -->
        				<Span begin="\\" end="." />
        			</RuleSet>
        		</Span>
        	</RuleSet>
        </SyntaxDefinition>
        """;

    [TestMethod]
    [DataRow(1.0)]
    [DataRow(1.25)]
    [DataRow(1.5)]
    [DataRow(1.75)]
    public void OneSelectionIsOneOutline(double dpiScale)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var geometry = BuildSelectionGeometry(dpiScale, out _, out var view);

        Assert.IsNotNull(geometry);
        Assert.AreEqual(dpiScale, view.DpiScale, 1e-9, "The window did not report the scale asked for.");
        Assert.AreEqual(1, CountFigures(geometry),
            $"At {dpiScale:P0} the rows of one selection were drawn as separate outlines.");
    }

    /// <summary>
    /// The outline has to stay inside the text viewport, or the top of the first row is cut off by
    /// the clip and the selection looks open at the top.
    /// </summary>
    [TestMethod]
    [DataRow(1.0)]
    [DataRow(1.25)]
    [DataRow(1.5)]
    [DataRow(1.75)]
    public void TheOutlineStaysInsideTheTextViewport(double dpiScale)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var geometry = BuildSelectionGeometry(dpiScale, out double borderThickness, out var view);
        var viewport = view.Surface.TextViewportBounds;
        var bounds = geometry!.GetBounds();

        // Compared in device pixels: the two edges reach this point through different arithmetic, so
        // in DIP an edge that sits exactly on the viewport can read one ulp below it.
        double outerPx = (bounds.Y - (borderThickness / 2)) * dpiScale;
        double viewportPx = viewport.Y * dpiScale;
        Assert.IsGreaterThanOrEqualTo(viewportPx - 1e-6, outerPx,
            $"At {dpiScale:P0} the top of the outline is {viewportPx - outerPx:F2}px above the viewport.");
    }

    private static PathGeometry? BuildSelectionGeometry(
        double dpiScale, out double borderThickness, out TextView view)
    {
        var editor = new TextEditor
        {
            Text = TEXT,
            FontFamily = "Consolas",
            FontSize = 13,
            ShowLineNumbers = false,
            SkipViewportCull = true
        };
        var window = ScaledWindow.Create(dpiScale, 500, 300);
        window.Content = editor;
        window.PerformLayout();
        editor.Select(0, editor.Text.Length);

        view = editor.TextArea.TextView;
        // The hairline the selection layer uses.
        borderThickness = new ColorPen(Color.FromRgb(0, 0, 0), 1 / view.DpiScale)
            .SnapThickness(view.DpiScale).Thickness;
        var builder = new BackgroundGeometryBuilder
        {
            AlignToWholePixels = true,
            BorderThickness = borderThickness
        };
        foreach (var segment in editor.TextArea.Selection.Segments)
        {
            builder.AddSegment(view, segment);
        }
        return builder.CreateGeometry();
    }

    private static int CountFigures(PathGeometry? geometry)
    {
        if (geometry is null)
        {
            return 0;
        }
        int figures = 0;
        foreach (var command in geometry.Commands)
        {
            if (command.Type == PathCommandType.MoveTo)
            {
                figures++;
            }
        }
        return figures;
    }
}
