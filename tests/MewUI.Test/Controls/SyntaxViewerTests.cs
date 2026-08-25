using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class SyntaxViewerTests
{
    [TestMethod]
    public void ViewerDoesNotLinkEditingModule()
    {
        string[] editingReferences = typeof(SyntaxViewer)
            .GetFields(System.Reflection.BindingFlags.Instance |
                       System.Reflection.BindingFlags.NonPublic |
                       System.Reflection.BindingFlags.Public)
            .Select(static field => field.FieldType.FullName ?? string.Empty)
            .Where(static name => name.StartsWith("Aprillz.MewUI.Text.Editing.", StringComparison.Ordinal))
            .ToArray();

        Assert.HasCount(0, editingReferences);
    }

    [TestMethod]
    public void DefaultStyleAndOverflowScrollBarsMatchReadOnlyTextSurface()
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
            string line = "public static readonly string Value = \"This line extends beyond the visible syntax viewport.\";";
            var viewer = new SyntaxViewer
            {
                Width = 320,
                Height = 120,
                Wrap = false,
                Text = string.Join('\n', Enumerable.Repeat(line, 100))
            };
            using var window = HeadlessWindow.Create(320, 120);
            window.Content = viewer;
            window.PerformLayout();
            using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(320, 120, 1));
            window.RenderFrameToSurface(surface);

            Assert.AreEqual(viewer.ThemeInternal.Palette.ControlBackground, viewer.Background);
            Assert.AreEqual(viewer.ThemeInternal.Palette.ControlBorder, viewer.BorderBrush);
            Assert.IsGreaterThan(0, viewer.BorderThickness);
            Assert.IsTrue(viewer.IsVerticalScrollBarVisible);
            Assert.IsTrue(viewer.IsHorizontalScrollBarVisible);
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    [TestMethod]
    public void ViewerRunsTheClassifierOnlyForMaterializedLines()
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
            var classifier = new CountingClassifier();
            var viewer = new SyntaxViewer
            {
                Width = 320,
                Height = 160,
                Text = string.Join('\n', Enumerable.Range(0, 10_000).Select(static value => $"keyword value{value}"))
            };
            viewer.Extensions.Classifiers.Add(classifier);
            viewer.InvalidateTextView();
            viewer.Select(0, 7);

            using var window = HeadlessWindow.Create(320, 160);
            window.Content = viewer;
            window.PerformLayout();
            using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(320, 160, 1));
            window.RenderFrameToSurface(surface);

            Assert.IsGreaterThan(0, viewer.MaterializedLineCount);
            Assert.IsLessThan(50, viewer.MaterializedLineCount);
            Assert.AreEqual(viewer.MaterializedLineCount, classifier.InvocationCount);
            Assert.AreEqual("keyword", viewer.SelectedText);
            Assert.IsGreaterThan(0, factory.TextEngine.ManagedCache.Count);
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    private sealed class CountingClassifier : ITextClassifier
    {
        public int InvocationCount { get; private set; }

        public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
        {
            InvocationCount++;
            int length = Math.Min(7, context.Text.Length);
            if (length > 0)
            {
                output.Add(new TextPaintSpan(new TextRange(0, length), Foreground: Color.Blue));
            }
        }
    }
}
