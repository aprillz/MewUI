using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Highlighting;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// Advancing the highlighting state up to the viewport raises one notification per scanned line.
/// The colorizer suppresses those for the ongoing pass - without the guard, a jump to the end of a
/// large document issues one full viewport rebuild per line (20,000 rebuilds for 20,000 lines).
/// </summary>
[TestClass]
public sealed class HighlightingStateScanTests
{
    [TestMethod]
    public void TheStateScanToTheViewportDoesNotRepaintPerLine()
    {
        var definition = HighlightingManager.Instance.GetDefinition("C#");
        Assert.IsNotNull(definition);
        var document = new TextDocument(
            string.Join("\n", Enumerable.Range(1, 500).Select(n => $"public static int Value{n} => {n}; // line {n}")));
        var colorizer = new HighlightingColorizer(definition);
        int repaints = 0;
        colorizer.HighlightingStateChanged += (_, _) => repaints++;

        colorizer.OnVisualLineConstructionStarting(document, 500);
        colorizer.OnVisualLinesChanged();

        // Only a change crossing the first line in view may repaint; the scan itself must not.
        Assert.IsLessThanOrEqualTo(1, repaints, $"The scan to the viewport issued {repaints} repaints.");
    }

    [TestMethod]
    public void StateNotificationsArriveAfterTheHighlighterIsIdle()
    {
        var definition = HighlightingManager.Instance.GetDefinition("C#");
        Assert.IsNotNull(definition);
        var document = new TextDocument(
            string.Join("\n", Enumerable.Range(1, 40).Select(n => $"int value{n} = {n};")));
        var colorizer = new HighlightingColorizer(definition);
        var highlighter = colorizer.GetHighlighter(document);
        int forwarded = 0;
        // A consumer's repaint rebuilds lines synchronously, which re-enters the highlighter;
        // that call throws unless the notification waits for the highlighter to go idle.
        colorizer.HighlightingStateChanged += (_, toLine) =>
        {
            highlighter.HighlightLine(Math.Min(toLine, document.LineCount));
            forwarded++;
        };

        colorizer.OnVisualLineConstructionStarting(document, 30);
        colorizer.OnVisualLinesChanged();

        Assert.IsGreaterThanOrEqualTo(1, forwarded,
            "The boundary notification must still reach the consumer.");
    }

    [TestMethod]
    public void AStateChangeBelowTheColorizedLineStillRepaints()
    {
        var definition = HighlightingManager.Instance.GetDefinition("C#");
        Assert.IsNotNull(definition);
        var document = new TextDocument("int before;\nint after;");
        var colorizer = new HighlightingColorizer(definition);
        int repaints = 0;
        colorizer.HighlightingStateChanged += (_, _) => repaints++;

        // Scanning to line 0 keeps the whole document ahead of the pass, so opening a comment on
        // line 1 must still reach the host: the state at line 2 changed below the scanned range.
        colorizer.OnVisualLineConstructionStarting(document, 1);
        colorizer.OnVisualLinesChanged();
        int baseline = repaints;
        document.Insert(0, "/*");
        colorizer.GetHighlighter(document).HighlightLine(1);

        Assert.IsGreaterThan(baseline,
repaints, "A state change crossing below the highlighted line was suppressed.");
    }
}
