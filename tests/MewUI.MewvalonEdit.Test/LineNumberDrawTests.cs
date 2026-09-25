using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;
using MewUI.MewvalonEdit.Test.Infrastructure;
using MewUI.Test.Infrastructure;

namespace MewUI.MewvalonEdit.Test;

/// <summary>A host shows a line number away from its row, such as on a row pinned above the text, the way the margin draws it.</summary>
[TestClass]
[DoNotParallelize]
public sealed class LineNumberDrawTests
{
    [TestMethod]
    public void ALineNumberDrawnAwayFromItsRowStartsWhereTheMarginDrawsIt()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var editor = new TextEditor { Text = string.Join("\n", Enumerable.Range(1, 20).Select(index => $"line {index}")), ShowLineNumbers = true };
        var window = HeadlessWindow.Create(400, 300);
        window.Content = editor;
        window.PerformLayout();
        var margin = editor.TextArea.LeftMargins.OfType<LineNumberMargin>().Single();
        var line = editor.TextArea.TextView.Host.VisibleTextLines[8];
        int lineNumber = line.LogicalLine.LineNumber + 1;
        double rowTop = editor.TextArea.TextView.Host.TextViewportBounds.Y + line.DocumentY;

        var besideItsRow = new RecordingContext();
        margin.Render(besideItsRow);
        var drawnBeside = besideItsRow.Origins.Single(origin => Math.Abs(origin.Y - rowTop) < 0.01);

        var awayFromItsRow = new RecordingContext();
        margin.DrawLineNumber(awayFromItsRow, lineNumber, 7);

        Assert.AreEqual(new Point(drawnBeside.X, 7), awayFromItsRow.Origins.Single());
    }

    private sealed class RecordingContext : NoOpGraphicsContext, IGraphicsContext
    {
        private readonly RecordingText _text;

        public RecordingContext() => _text = new RecordingText(this);

        public List<Point> Origins => _text.Origins;

        ITextRenderContext IGraphicsContext.Text => _text;

        private sealed class RecordingText(IGraphicsContext graphics) : ITextRenderContext
        {
            public List<Point> Origins { get; } = new();

            public IGraphicsContext Graphics => graphics;

            public void Draw(ITextLayout layout, Point origin, in TextDrawOptions options) => Origins.Add(origin);

            public void DrawBackground(ITextLayout layout, Point origin, in TextDrawOptions options) { }

            public void DrawForeground(ITextLayout layout, Point origin, in TextDrawOptions options) => Origins.Add(origin);
        }
    }
}
