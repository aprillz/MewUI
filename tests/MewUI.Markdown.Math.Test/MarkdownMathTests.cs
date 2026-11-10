using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Syntax;

[assembly: DoNotParallelize]

namespace Aprillz.MewUI.Markdown.Test;

[TestClass]
[DoNotParallelize]
public sealed class MarkdownMathTests
{
    private static readonly MarkdownOptions _mathOptions = new() { ConfigurePipeline = static builder => builder.UseMath() };

    [TestMethod]
    public void PipelineRecognizesDollarsAndBrackets()
    {
        MarkdownPipeline pipeline = new MarkdownPipelineBuilder().UseMath().Build();
        MarkdownDocument document = Markdig.Markdown.Parse("$a$ and \\(b\\)\n\n$$\nc\n$$\n\n\\[\nd\n\\]", pipeline);

        Assert.HasCount(2, document.Descendants<MathInline>().ToList());
        Assert.HasCount(2, document.Descendants<MathBlock>().ToList());
    }

    [TestMethod]
    public void PipelineWithoutBracketsKeepsTheEscapes()
    {
        MarkdownPipeline pipeline = new MarkdownPipelineBuilder().UseMath(bracketDelimiters: false).Build();
        MarkdownDocument document = Markdig.Markdown.Parse("$a$ and \\(b\\)", pipeline);

        Assert.HasCount(1, document.Descendants<MathInline>().ToList());
    }

    [TestMethod]
    public void FormulaMeasuresAboveAndBelowTheBaseline()
    {
        MathFormula formula = new("\\frac{a}{b}", display: false);

        (double width, double ascent, double descent) = formula.Measure(16);

        Assert.IsNull(formula.ErrorMessage);
        Assert.IsGreaterThan(0, width);
        Assert.IsGreaterThan(0, ascent);
        Assert.IsGreaterThan(0, descent);
        Assert.IsGreaterThan(width, formula.Measure(32).Width);
    }

    [TestMethod]
    public void FormulaFollowsTheFontSize()
    {
        // The letter x is about half an em tall, so the ascent of a formula tracks its font size.
        MathFormula formula = new("x", display: false);

        (_, double ascent, _) = formula.Measure(100);

        Assert.IsTrue(ascent > 35 && ascent < 60, $"ascent {ascent}");
    }

    [TestMethod]
    public void BadLatexKeepsAnErrorMessage()
    {
        MathFormula formula = new("\\frac{a", display: false);

        Assert.IsNotNull(formula.ErrorMessage);
        Assert.IsGreaterThan(0, formula.Measure(16).Width);
    }

    [TestMethod]
    [DataRow("F = G\\dfrac{m_1 m_2}{r^2}", "F = G\\frac{m_1 m_2}{r^2}")]
    [DataRow("\\tfrac12", "\\frac12")]
    [DataRow("\\boldsymbol{v}\\cdot\\boldsymbol{u}", "\\mathbf{v}\\cdot\\mathbf{u}")]
    [DataRow("\\tan2\\theta=1", "\\tan2\\theta=1")]
    [DataRow("f(\\alpha')", "f(\\alpha')")]
    [DataRow("a\\\\=b \\{x\\}", "a\\\\=b \\{x\\}")]
    [DataRow("a\\\\boldsymbol", "a\\\\boldsymbol")]
    [DataRow("\\underbrace{a+b}_{n} \\not= c", "\\underbrace{a+b}_{n} \\not= c")]
    public void NormalizeRewritesWhatCSharpMathMisreads(string latex, string expected)
    {
        Assert.AreEqual(expected, MathFormula.Normalize(latex));
        Assert.IsNull(new MathFormula(latex, display: true).ErrorMessage);
    }

    [TestMethod]
    public void SafetyLimitsRejectDeepNestingAndLongSources()
    {
        Assert.IsTrue(MathFormula.IsSafe("\\frac{a}{b}"));
        Assert.IsFalse(MathFormula.IsSafe(new string('{', 51) + new string('}', 51)));
        Assert.IsFalse(MathFormula.IsSafe(new string('x', 10_001)));
    }

    [TestMethod]
    public void InlineFormulaDrawsIntoTheParagraph()
    {
        EnsureGdi();
        using var presenter = new MarkdownPresenter { Markdown = "$x^2$", Options = _mathOptions, Renderers = new MarkdownRenderers().UseMath(), FontSize = 16 };

        Assert.IsTrue(Render(presenter), "no pixel was painted");
    }

    [TestMethod]
    public void BlockFormulaBecomesACenteredElement()
    {
        EnsureGdi();
        using var presenter = new MarkdownPresenter { Markdown = "before\n\n$$\n\\sum_{k=1}^{n} k\n$$\n\nafter", Options = _mathOptions, Renderers = new MarkdownRenderers().UseMath(), FontSize = 16 };
        presenter.Measure(new Size(240, 200));
        presenter.Arrange(new Rect(0, 0, 240, 200));

        MathBlockElement block = Descendants(presenter).OfType<MathBlockElement>().Single();
        Assert.AreEqual("\\sum_{k=1}^{n} k", block.Source);
        Assert.IsGreaterThan(0, block.Bounds.Width);
        Assert.IsGreaterThan(0, block.Bounds.Height);
        Assert.IsTrue(Render(presenter), "no pixel was painted");
    }

    [TestMethod]
    public void UnsafeFormulaFallsBackToSourceText()
    {
        EnsureGdi();
        string deep = new string('{', 51) + "x" + new string('}', 51);
        using var presenter = new MarkdownPresenter { Markdown = $"$$\n{deep}\n$$", Options = _mathOptions, Renderers = new MarkdownRenderers().UseMath() };
        presenter.Measure(new Size(240, 100));

        Assert.IsFalse(Descendants(presenter).OfType<MathBlockElement>().Any());
    }

    [TestMethod]
    public void FormulaRecordsOncePerFontSize()
    {
        MathFormula formula = new("\\frac{a}{b}", display: true);

        MathDrawing first = formula.GetDrawing(16);
        Assert.AreSame(first, formula.GetDrawing(16));
        formula.GetDrawing(24);
        formula.Measure(24);
        Assert.AreSame(first, formula.GetDrawing(16), "a second font size dropped the first");
        Assert.AreEqual(2, formula.RecordCount);
    }

    [TestMethod]
    public void RecordedPathsAreFrozenAndTakeTheForegroundUnlessColored()
    {
        MathFormula plain = new("x + \\sqrt{y}", display: false);
        MathFormula colored = new("\\color{red}{x} + y", display: false);

        MathDrawCommand[] plainCommands = plain.GetDrawing(16).Commands.ToArray();
        MathDrawCommand[] coloredCommands = colored.GetDrawing(16).Commands.ToArray();

        Assert.IsNotEmpty(plainCommands);
        Assert.IsTrue(plainCommands.All(command => command.Color == null), "a plain formula fixed a color");
        Assert.IsTrue(plainCommands.Where(command => command.Geometry != null).All(command => command.Geometry!.IsFrozen));
        Assert.IsTrue(coloredCommands.Any(command => command.Color == Color.FromArgb(255, 255, 0, 0)), "the red part lost its color");
        Assert.IsTrue(coloredCommands.Any(command => command.Color == null), "the uncolored part fixed a color");
    }

    [TestMethod]
    public void RebuiltDocumentReusesTypesetFormulas()
    {
        EnsureGdi();
        using var presenter = new MarkdownPresenter { Markdown = "a\n\n$$\n\\sum_{k=1}^{n} k\n$$", Options = _mathOptions, Renderers = new MarkdownRenderers().UseMath(), FontSize = 16 };
        Assert.IsTrue(Render(presenter), "no pixel was painted");
        MathFormula before = Descendants(presenter).OfType<MathBlockElement>().Single().Formula;

        presenter.Markdown = "b\n\n$$\n\\sum_{k=1}^{n} k\n$$";
        Assert.IsTrue(Render(presenter), "no pixel was painted");
        MathBlockElement after = Descendants(presenter).OfType<MathBlockElement>().Single();

        Assert.AreSame(before, after.Formula);
        Assert.AreEqual(1, after.Formula.RecordCount);
    }

    [TestMethod]
    public void FormulaCacheDropsTheLeastRecentlyUsed()
    {
        MathFormulaCache cache = new(capacity: 2);
        MathFormula first = cache.Get("a", display: false);
        MathFormula second = cache.Get("b", display: false);

        Assert.AreSame(first, cache.Get("a", display: false));
        Assert.AreNotSame(first, cache.Get("a", display: true));
        Assert.AreSame(first, cache.Get("a", display: false));
        Assert.AreNotSame(second, cache.Get("b", display: false));
        Assert.AreEqual(2, cache.Count);
    }

    private static bool Render(MarkdownPresenter presenter)
    {
        presenter.Measure(new Size(240, 200));
        presenter.Arrange(new Rect(0, 0, 240, 200));
        var factory = (GdiGraphicsFactory)Application.DefaultGraphicsFactory;
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(240, 200, 1));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            presenter.Render(context);
            context.EndFrame();
        }
        var pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        return pixels.ToArray().Chunk(4).Any(pixel => pixel[3] > 16);
    }

    private static IEnumerable<Element> Descendants(Element root)
    {
        yield return root;
        if (root is IVisualTreeHost host)
        {
            var children = new List<Element>();
            host.VisitChildren(child => { children.Add(child); return true; });
            foreach (var child in children)
            {
                foreach (var descendant in Descendants(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static void EnsureGdi()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
        }
        GdiBackend.Register();
    }
}
