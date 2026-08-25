using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class TextTrimmingTests
{
    private const string LongLine = "The quick brown fox jumps over the lazy dog";

    [TestMethod]
    public void NoWrapTrimsEveryOverflowingLine()
    {
        using var scope = GdiScope.Create();
        if (scope is null)
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var layout = (ManagedTextLayout)CreateLayout(scope.Factory, LongLine + "\n" + LongLine,
            TextWrapping.NoWrap, maxWidth: 80, maxHeight: double.PositiveInfinity);

        Assert.HasCount(2, layout.ManagedLines);
        foreach (var line in layout.ManagedLines)
        {
            Assert.IsTrue(line.IsTrimmed, "An overflowing line was left untrimmed.");
            Assert.IsLessThanOrEqualTo(80.5, line.Metrics.Bounds.Width);
            Assert.IsLessThan(LongLine.Length, line.Metrics.TextLength);
        }
    }

    [TestMethod]
    public void NoWrapKeepsFittingLineUntrimmed()
    {
        using var scope = GdiScope.Create();
        if (scope is null)
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var layout = (ManagedTextLayout)CreateLayout(scope.Factory, "ab",
            TextWrapping.NoWrap, maxWidth: 400, maxHeight: double.PositiveInfinity);

        Assert.IsFalse(layout.ManagedLines[0].IsTrimmed);
        Assert.AreEqual(2, layout.ManagedLines[0].Metrics.TextLength);
    }

    [TestMethod]
    public void WrapDropsLinesPastHeightAndAlwaysTrimsTheLast()
    {
        using var scope = GdiScope.Create();
        if (scope is null)
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var untrimmed = (ManagedTextLayout)CreateLayout(scope.Factory, LongLine,
            TextWrapping.Wrap, maxWidth: 80, maxHeight: double.PositiveInfinity);
        Assert.IsGreaterThan(2, untrimmed.ManagedLines.Count, "Test text should wrap to several lines.");

        double twoLines = untrimmed.ManagedLines[1].Metrics.Bounds.Bottom;
        var layout = (ManagedTextLayout)CreateLayout(scope.Factory, LongLine,
            TextWrapping.Wrap, maxWidth: 80, maxHeight: twoLines);

        Assert.HasCount(2, layout.ManagedLines);
        Assert.IsFalse(layout.ManagedLines[0].IsTrimmed);
        Assert.IsTrue(layout.ManagedLines[1].IsTrimmed, "Wrap overflow must mark the last visible line.");
    }

    [TestMethod]
    public void TrimmingIsPartOfTheLayoutCacheKey()
    {
        using var scope = GdiScope.Create();
        if (scope is null)
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var plain = CreateLayout(scope.Factory, LongLine, TextWrapping.NoWrap, 80, double.PositiveInfinity,
            TextTrimming.None);
        var trimmed = CreateLayout(scope.Factory, LongLine, TextWrapping.NoWrap, 80, double.PositiveInfinity,
            TextTrimming.CharacterEllipsis);

        Assert.AreNotEqual(plain.MeasuredSize.Width, trimmed.MeasuredSize.Width);
    }

    private static ITextLayout CreateLayout(
        IGraphicsFactory factory,
        string text,
        TextWrapping wrapping,
        double maxWidth,
        double maxHeight,
        TextTrimming trimming = TextTrimming.CharacterEllipsis)
        => factory.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Text = text.AsMemory(),
            Dpi = 96,
            DefaultStyle = new TextRunStyle("Consolas", 12),
            Paragraph = new TextParagraphStyle
            {
                Wrapping = wrapping,
                MaxWidth = maxWidth,
                MaxHeight = maxHeight,
                Trimming = trimming
            }
        });

    private sealed class GdiScope : IDisposable
    {
        private readonly IGraphicsFactory _previous;

        private GdiScope(GdiGraphicsFactory factory, IGraphicsFactory previous)
        {
            Factory = factory;
            _previous = previous;
        }

        public GdiGraphicsFactory Factory { get; }

        public static GdiScope? Create()
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            var previous = Application.DefaultGraphicsFactory;
            var factory = new GdiGraphicsFactory();
            Application.DefaultGraphicsFactory = factory;
            return new GdiScope(factory, previous);
        }

        public void Dispose()
        {
            Application.DefaultGraphicsFactory = _previous;
            Factory.Dispose();
        }
    }
}
