using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Highlighting;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace MewUI.MewvalonEdit.Test;

[TestClass]
[DoNotParallelize]
public sealed class WhitespaceAndThemeTests
{
    /// <summary>
    /// A space is stood in for by a marker glyph in its own color, so it reads as whitespace rather
    /// than as content. The color is taken from the view on every paint, so a theme change reaches a
    /// marker whose element was built under the previous one.
    /// </summary>
    [TestMethod]
    public void SpaceMarkersArePaintedInTheMarkerColor()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = new TextEditor { Text = "a b", ShowLineNumbers = false, SkipViewportCull = true };
        editor.Options.ShowSpaces = true;
        editor.Measure(new Size(320, 80));
        editor.Arrange(new Rect(0, 0, 320, 80));

        var factory = Application.DefaultGraphicsFactory;
        using (var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(320, 80, 1)))
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            editor.Render(context);
            context.EndFrame();
        }

        var element = editor.TextArea.TextView
            .GetOrConstructVisualLine(editor.Document.GetLineByNumber(1))!.Elements.SingleOrDefault();

        Assert.IsNotNull(element, "No element stood in for the space.");
        Assert.AreEqual(editor.WhitespaceMarkerColor, element.Foreground);
        Assert.AreNotEqual(editor.Foreground, element.Foreground);
    }

    [TestMethod]
    public void SpaceMarkersAreNotPaintedWhenHidden()
    {
        var editor = new TextEditor { Text = "a b" };
        editor.Options.ShowSpaces = false;

        Assert.IsNull(ConstructSingleCharacterElement(editor, offset: 1));
    }

    /// <summary>
    /// A control character is stood in for by a box naming it. A tab is a control character too, and
    /// must not be boxed: the original settles it before the box is reached.
    /// </summary>
    [TestMethod]
    public void ControlCharactersAreBoxedButTabsAreNot()
    {
        var editor = new TextEditor { Text = "ab\tc" };

        var boxed = ConstructSingleCharacterElement(editor, offset: 1);
        Assert.IsInstanceOfType<ControlCharacterBoxElement>(boxed, "The bell character was not boxed.");
        Assert.AreEqual("BEL", TextUtilities.GetControlCharacterName((char)7));
        Assert.IsNull(ConstructSingleCharacterElement(editor, offset: 3), "The tab was boxed.");
    }

    /// <summary>
    /// A marked tab occupies two visual columns for its one document character: the marker, which
    /// paints and reports no width, and the tab itself, which is laid out as text and finds the stop.
    /// </summary>
    [TestMethod]
    public void MarkedTabsKeepTheTabBesideTheMarker()
    {
        var editor = new TextEditor { Text = "a\tb" };
        editor.Options.ShowTabs = true;

        var element = ConstructSingleCharacterElement(editor, offset: 1) as TabMarkerElement;

        Assert.IsNotNull(element, "The tab was not marked.");
        Assert.AreEqual(2, element.VisualLength);
        Assert.AreEqual(1, element.DocumentLength);
        Assert.AreEqual(1, element.PaintedVisualLength);
        Assert.AreEqual("￼\t", element.GetVisualText());
        Assert.AreEqual(0, element.Measure(96).Width, "The marker must report no width of its own.");
    }

    private static VisualLineElement? ConstructSingleCharacterElement(TextEditor editor, int offset)
    {
        var generator = editor.TextArea.TextView.ElementGenerators
            .OfType<SingleCharacterElementGenerator>()
            .SingleOrDefault();
        if (generator is null)
        {
            return null;
        }
        generator.StartGeneration(new GenerationContext(editor));
        try
        {
            return generator.GetFirstInterestedOffset(offset) == offset
                ? generator.ConstructElement(offset)
                : null;
        }
        finally
        {
            generator.FinishGeneration();
        }
    }

    private sealed class GenerationContext(TextEditor editor) : ITextRunConstructionContext
    {
        public TextDocument Document => editor.Document;
        public TextView TextView => editor.TextArea.TextView;
        public DocumentLine CurrentDocumentLine => editor.Document.GetLineByNumber(1);
        public TextRunStyle DefaultStyle => new(editor.FontFamily, editor.FontSize, editor.FontWeight);
    }

    /// <summary>
    /// The shipped definitions carry one colour per scope. A palette entry for that scope decides
    /// the colour instead, per theme, without the definition being edited.
    /// </summary>
    [TestMethod]
    public void ThePaletteOverridesADefinitionsOwnColorPerTheme()
    {
        var definition = HighlightingManager.Instance.GetDefinition("C#");
        Assert.IsNotNull(definition);
        const string SOURCE = "public int Value = 3;";

        using (WithPalette())
        {
            var dark = HighlightingTestHost.Colorize(definition, SOURCE, isDarkTheme: true);
            var light = HighlightingTestHost.Colorize(definition, SOURCE, isDarkTheme: false);

            var darkKeyword = dark.First(element => element.RelativeTextOffset == 0);
            var lightKeyword = light.First(element => element.RelativeTextOffset == 0);
            Assert.AreEqual(Color.FromRgb(86, 156, 214), darkKeyword.TextRunProperties.ForegroundBrush);
            Assert.AreEqual(Color.FromRgb(220, 220, 170), lightKeyword.TextRunProperties.ForegroundBrush);
        }
    }

    [TestMethod]
    public void OneColorizerFollowsAThemeSwitchWithoutRebuilding()
    {
        var definition = HighlightingManager.Instance.GetDefinition("C#");
        Assert.IsNotNull(definition);
        var colorizer = new HighlightingColorizer(definition);

        using (WithPalette())
        {
            // The same instance across the switch: the colorizer reads the theme per line, so
            // nothing has to be rebuilt when it changes.
            var dark = HighlightingTestHost.Colorize(colorizer, "public", isDarkTheme: true);
            var light = HighlightingTestHost.Colorize(colorizer, "public", isDarkTheme: false);

            Assert.AreEqual(Color.FromRgb(86, 156, 214), dark[0].TextRunProperties.ForegroundBrush);
            Assert.AreEqual(Color.FromRgb(220, 220, 170), light[0].TextRunProperties.ForegroundBrush);
        }
    }

    /// <summary>Installs a palette that colours the scope 'public' carries, and restores it after.</summary>
    private static IDisposable WithPalette()
    {
        var previous = HighlightingPalette.Current;
        var palette = new HighlightingPalette();
        palette.Set("Visibility", new PaletteEntry(
            Color.FromRgb(86, 156, 214), Color.FromRgb(220, 220, 170)));
        HighlightingPalette.Current = palette;
        return new PaletteScope(previous);
    }

    private sealed class PaletteScope(HighlightingPalette previous) : IDisposable
    {
        public void Dispose() => HighlightingPalette.Current = previous;
    }
}
