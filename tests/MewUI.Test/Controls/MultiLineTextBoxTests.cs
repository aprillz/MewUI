using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text.Editing;
using MewUI.Test.Infrastructure;
using System.Diagnostics;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class MultiLineTextBoxTests
{
    [TestMethod]
    public void Control_DoesNotDependOnLegacyControlsTextTypes()
    {
        Assert.AreEqual(typeof(TextBase), typeof(MultiLineTextBox).BaseType);

        var legacyReferences = typeof(MultiLineTextBox)
            .GetFields(System.Reflection.BindingFlags.Instance |
                       System.Reflection.BindingFlags.NonPublic |
                       System.Reflection.BindingFlags.Public)
            .Select(static field => field.FieldType.FullName ?? string.Empty)
            .Where(static name => name.StartsWith("Aprillz.MewUI.Controls.Text.", StringComparison.Ordinal))
            .ToArray();

        Assert.HasCount(0, legacyReferences);
    }

    [TestMethod]
    public void InjectedDocumentRemainsTheEditableSourceOfTruth()
    {
        var document = new EditableTextDocument("seed");
        var textBox = new MultiLineTextBox(document);

        Assert.AreSame(document, textBox.Document);
        Assert.AreEqual("seed", textBox.Text);

        document.Insert(document.TextLength, " value");

        Assert.AreEqual("seed value", textBox.Text);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public void ReplacingTenMegabytesWithoutSubscriberAvoidsFullTextSnapshots()
    {
        string first = new('x', 10_000_000);
        string second = new('y', 10_000_000);
        var textBox = new MultiLineTextBox { Text = first };

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        textBox.Text = second;

        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.AreEqual(second, textBox.Text);
        Assert.IsLessThan(32L * 1024 * 1024, allocatedBytes,
            $"Replacing 10MB allocated {allocatedBytes:N0} bytes, indicating extra full document snapshots.");
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public void EditingLargeInjectedDocumentWithoutStringConsumerDefersSnapshot()
    {
        var document = new EditableTextDocument(new string('x', 10_000_000));
        var textBox = new MultiLineTextBox(document);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        document.Insert(document.TextLength, "y");

        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.AreEqual(10_000_001, document.TextLength);
        Assert.IsLessThan(1L * 1024 * 1024, allocatedBytes,
            $"A one-character edit allocated {allocatedBytes:N0} bytes, indicating a full text snapshot.");
        Assert.AreEqual(10_000_001, textBox.Text.Length,
            "The deferred snapshot was not materialized when Text was explicitly requested.");
    }

    [TestMethod]
    public void DefaultStyleDrawsEditorChromeAndNoWrapShowsHorizontalScrollBar()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var previousFactory = Application.DefaultGraphicsFactory;
        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        try
        {
            var textBox = new MultiLineTextBox
            {
                Width = 290,
                Height = 120,
                Wrap = false,
                Text = "The quick brown fox jumps over the lazy dog, then keeps running far beyond the visible editor width."
            };
            using var window = HeadlessWindow.Create(290, 120);
            window.Content = textBox;
            window.PerformLayout();

            using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(290, 120, 1));
            window.RenderFrameToSurface(surface);

            Assert.AreEqual(textBox.ThemeInternal.Palette.ControlBackground, textBox.Background);
            Assert.AreEqual(textBox.ThemeInternal.Palette.ControlBorder, textBox.BorderBrush);
            Assert.IsGreaterThan(0, textBox.BorderThickness);
            Assert.IsTrue(textBox.IsHorizontalScrollBarVisible,
                "Wrap=false did not expose a horizontal scrollbar for overflowing text.");
            Assert.IsFalse(textBox.IsVerticalScrollBarVisible);
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    [TestMethod]
    public void TextInputCompositionUndoAndRedoUseNewEditingSession()
    {
        var textBox = new MultiLineTextBox { Text = "before target after" };
        textBox.Select(7, 6);

        ((ITextCompositionClient)textBox).HandleTextCompositionStart(new TextCompositionEventArgs());
        ((ITextCompositionClient)textBox).HandleTextCompositionUpdate(
            new TextCompositionEventArgs("ㅎ", [CompositionAttr.Input]));
        ((ITextCompositionClient)textBox).HandleTextCompositionUpdate(
            new TextCompositionEventArgs("한", [CompositionAttr.Converted]));
        ((ITextCompositionClient)textBox).HandleTextCompositionEnd(new TextCompositionEventArgs());

        Assert.AreEqual("before 한 after", textBox.Text);
        Assert.IsTrue(textBox.CanUndo);

        textBox.Undo();
        Assert.AreEqual("before target after", textBox.Text);
        Assert.AreEqual(7, textBox.SelectionStart);
        Assert.AreEqual(6, textBox.SelectionLength);

        textBox.Redo();
        ((ITextInputClient)textBox).HandleTextInput(new TextInputEventArgs("!"));
        Assert.AreEqual("before 한! after", textBox.Text);
    }

    [TestMethod]
    public void CopyCutAndPasteUseClipboardServiceWithoutLegacyTextBase()
    {
        var clipboard = new TestClipboard();
        var textBox = new MultiLineTextBox
        {
            Text = "copy target",
            ClipboardService = clipboard
        };
        textBox.Select(5, 6);

        textBox.Copy();
        Assert.AreEqual("target", clipboard.Text);

        textBox.Cut();
        Assert.AreEqual("copy ", textBox.Text);
        clipboard.Text = "paste";
        textBox.Paste();
        Assert.AreEqual("copy paste", textBox.Text);

        textBox.Undo();
        Assert.AreEqual("copy ", textBox.Text);
        textBox.Undo();
        Assert.AreEqual("copy target", textBox.Text);
    }

    [TestMethod]
    public void ReadOnlyBlocksMutationAndMaxLengthPreservesTextElementBoundaries()
    {
        var clipboard = new TestClipboard { Text = "paste" };
        var textBox = new MultiLineTextBox
        {
            Text = "fixed",
            IsReadOnly = true,
            ClipboardService = clipboard
        };
        textBox.Select(0, textBox.Text.Length);

        textBox.Cut();
        textBox.Paste();
        ((ITextInputClient)textBox).HandleTextInput(new TextInputEventArgs("changed"));
        ((ITextCompositionClient)textBox).HandleTextCompositionStart(new TextCompositionEventArgs());
        ((ITextCompositionClient)textBox).HandleTextCompositionUpdate(new TextCompositionEventArgs("변경"));
        ((ITextCompositionClient)textBox).HandleTextCompositionEnd(new TextCompositionEventArgs());

        Assert.AreEqual("fixed", textBox.Text);

        textBox.IsReadOnly = false;
        textBox.Text = "A";
        textBox.CaretPosition = 1;
        textBox.MaxLength = 2;
        ((ITextInputClient)textBox).HandleTextInput(new TextInputEventArgs("😀"));
        Assert.AreEqual("A", textBox.Text, "MaxLength split a surrogate-pair text element.");

        textBox.MaxLength = 3;
        ((ITextCompositionClient)textBox).HandleTextCompositionStart(new TextCompositionEventArgs());
        ((ITextCompositionClient)textBox).HandleTextCompositionUpdate(new TextCompositionEventArgs("e\u0301x"));
        ((ITextCompositionClient)textBox).HandleTextCompositionEnd(new TextCompositionEventArgs());
        Assert.AreEqual("Ae\u0301", textBox.Text, "IME MaxLength split a combining text element.");
    }

    [TestMethod]
    public void WrappedArrowNavigationMovesExactlyOneVisualRow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var textBox = new MultiLineTextBox
        {
            Width = 140,
            Height = 120,
            Wrap = true,
            Text = new string('W', 120)
        };
        using var window = HeadlessWindow.Create(140, 120);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();
        textBox.CaretPosition = 1;

        Rect initial = textBox.GetCharRectInWindow(textBox.CaretPosition);
        window.SendKeyPress(Key.Down);
        Rect first = textBox.GetCharRectInWindow(textBox.CaretPosition);
        int firstPosition = textBox.CaretPosition;
        window.SendKeyPress(Key.Down);
        Rect second = textBox.GetCharRectInWindow(textBox.CaretPosition);

        Assert.IsGreaterThan(1, firstPosition);
        Assert.IsGreaterThan(firstPosition, textBox.CaretPosition);
        Assert.AreEqual(initial.Y + initial.Height, first.Y, 0.01);
        Assert.AreEqual(first.Y + first.Height, second.Y, 0.01);
    }

    [TestMethod]
    public void LargeDocumentMaterializesOnlyViewportLines()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var textBox = new MultiLineTextBox
        {
            Width = 320,
            Height = 160,
            Text = string.Join('\n', Enumerable.Range(0, 10_000).Select(static value => $"line {value}"))
        };
        using var window = HeadlessWindow.Create(320, 160);
        window.Content = textBox;
        window.PerformLayout();

        Assert.IsGreaterThan(0, textBox.MaterializedLineCount);
        Assert.IsLessThan(50, textBox.MaterializedLineCount,
            "The editor materialized the document instead of the viewport.");

        textBox.CaretPosition = textBox.Text.Length;
        Assert.IsGreaterThan(0, textBox.VerticalOffset);
        Assert.IsLessThan(50, textBox.MaterializedLineCount);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public void TenMegabyteSingleLogicalLine_LayoutsAsOneMaterializedLine()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var previousFactory = Application.DefaultGraphicsFactory;
        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var textBox = new MultiLineTextBox
            {
                Width = 320,
                Height = 80,
                Wrap = false,
                Text = new string('x', 10_000_000)
            };
            using var window = HeadlessWindow.Create(320, 80);
            window.Content = textBox;
            window.PerformLayout();
            long initialLayoutMilliseconds = stopwatch.ElapsedMilliseconds;

            Assert.AreEqual(10_000_000, textBox.Text.Length);
            Assert.AreEqual(1, textBox.MaterializedLineCount);
            Assert.IsLessThan(4 * 1024, textBox.MaterializedCharacterCount,
                "The no-wrap editor materialized the complete 10MB logical line.");
            Assert.AreEqual(0, textBox.HorizontalOffset);

            textBox.CaretPosition = textBox.Text.Length;
            long caretMilliseconds = stopwatch.ElapsedMilliseconds - initialLayoutMilliseconds;
            Console.WriteLine($"10MB control initial={initialLayoutMilliseconds}ms end-caret={caretMilliseconds}ms");
            Assert.IsLessThan(750L, initialLayoutMilliseconds,
                $"10MB control initialization regressed to {initialLayoutMilliseconds}ms.");
            Assert.IsLessThan(100L, caretMilliseconds,
                $"10MB end-caret navigation regressed to {caretMilliseconds}ms.");
            Assert.IsGreaterThan(0, textBox.HorizontalOffset,
                "Moving to the end of a 10MB line did not scroll the caret into view.");
            Assert.AreEqual(1, textBox.MaterializedLineCount);
            Assert.IsLessThan(4 * 1024, textBox.MaterializedCharacterCount);
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public void TenMegabyteWrappedLogicalLine_VirtualizesVisualRows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var previousFactory = Application.DefaultGraphicsFactory;
        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var textBox = new MultiLineTextBox
            {
                Width = 320,
                Height = 80,
                Wrap = true,
                Text = new string('x', 10_000_000)
            };
            using var window = HeadlessWindow.Create(320, 80);
            window.Content = textBox;
            window.PerformLayout();
            long initialMilliseconds = stopwatch.ElapsedMilliseconds;

            Assert.AreEqual(1, textBox.MaterializedLineCount);
            Assert.IsLessThan(4 * 1024, textBox.MaterializedCharacterCount,
                "The control materialized the complete wrapped logical line.");
            Assert.IsLessThan(512, textBox.MaterializedVisualLineCount,
                "The control materialized every wrapped visual row.");
            Assert.IsLessThan(750L, initialMilliseconds,
                $"10MB wrapped control initialization regressed to {initialMilliseconds}ms.");

            stopwatch.Restart();
            textBox.CaretPosition = textBox.Text.Length;
            Assert.IsLessThan(100L, stopwatch.ElapsedMilliseconds,
                "End-caret navigation scanned every wrapped visual row.");
            Assert.IsGreaterThan(0, textBox.VerticalOffset,
                "Moving to the end did not scroll the wrapped viewport.");
            Assert.IsLessThan(4 * 1024, textBox.MaterializedCharacterCount);
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    [TestMethod]
    public void HeadlessRenderProducesTextAndSelectionPixelsThroughTextEngine()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var previousFactory = Application.DefaultGraphicsFactory;
        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        try
        {
            using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(240, 120, 1));
            var textBox = new MultiLineTextBox
            {
                Width = 240,
                Height = 120,
                Text = "first line\nsecond selected line"
            };
            textBox.Select(11, 15);
            using var window = HeadlessWindow.Create(240, 120);
            window.Content = textBox;
            window.PerformLayout();
            window.RenderFrameToSurface(surface);

            Assert.IsGreaterThan(0, factory.TextEngine.ManagedCache.Count);
            var pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
            int nonTransparent = 0;
            for (int index = 3; index < pixels.Length; index += 4)
            {
                if (pixels[index] != 0) nonTransparent++;
            }
            Assert.IsGreaterThan(100, nonTransparent);
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    private sealed class TestClipboard : IClipboardService
    {
        public string Text { get; set; } = string.Empty;
        public bool TrySetText(string text)
        {
            Text = text;
            return true;
        }
        public bool TryGetText(out string text)
        {
            text = Text;
            return true;
        }
    }
}
