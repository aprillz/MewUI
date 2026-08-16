using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Folding;
using Aprillz.MewUI.Rendering;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// What a folding strategy sees when it re-runs on every edit, which is how the samples wire it.
/// The document moves its anchored segments before it raises the editor's text notification, so a
/// strategy that recomputes offsets from the new text must not have its result shifted again.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class FoldingUpdateTests
{
    private static TextEditor CreateEditor(string text)
    {
        var editor = new TextEditor
        {
            Text = text,
            FontFamily = "Consolas",
            FontSize = 13,
            ShowLineNumbers = false,
            SkipViewportCull = true
        };
        editor.Measure(new Size(600, 400));
        editor.Arrange(new Rect(0, 0, 600, 400));
        return editor;
    }

    [TestMethod]
    public void TypingInsideAFoldingLeavesItsRangeOnTheDocument()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor("{\n\n\n}\n");
        var manager = FoldingManager.Install(editor.TextArea);
        var strategy = new BraceFoldingStrategy();
        editor.TextChanged += (_, _) => strategy.UpdateFoldings(manager, editor.Document);
        strategy.UpdateFoldings(manager, editor.Document);

        var folding = manager.AllFoldings.Single();
        Assert.AreEqual(0, folding.StartOffset);
        Assert.AreEqual(5, folding.EndOffset);

        editor.Document.Insert(1, "   test");

        folding = manager.AllFoldings.Single();
        Assert.AreEqual(0, folding.StartOffset);
        Assert.AreEqual(12, folding.EndOffset,
            "The strategy recomputed the end from the new text and the segment shift moved it again.");
        Assert.IsLessThanOrEqualTo(editor.Document.TextLength, folding.EndOffset);
    }

    [TestMethod]
    public void ACollapsedFoldingKeepsTheLinesAfterItVisible()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor("{\n\n\n}\n");
        var manager = FoldingManager.Install(editor.TextArea);
        var strategy = new BraceFoldingStrategy();
        editor.TextChanged += (_, _) => strategy.UpdateFoldings(manager, editor.Document);
        strategy.UpdateFoldings(manager, editor.Document);
        editor.Document.Insert(1, "   test");
        editor.Measure(new Size(600, 400));
        editor.Arrange(new Rect(0, 0, 600, 400));

        manager.AllFoldings.Single().IsFolded = true;
        editor.Measure(new Size(600, 400));
        editor.Arrange(new Rect(0, 0, 600, 400));

        var view = editor.TextArea.TextView;
        var collapsed = view.GetOrConstructVisualLine(editor.Document.GetLineByNumber(1));
        var trailing = view.GetOrConstructVisualLine(editor.Document.GetLineByNumber(5));

        Assert.IsNotNull(collapsed);
        Assert.IsNotNull(trailing);
        Assert.AreEqual(12, collapsed.DocumentLength, "The collapsed line must stop at the closing brace.");
        Assert.AreEqual(13, trailing.StartOffset, "The line after the folding must stay on its own.");
    }

    /// <summary>
    /// The placeholder takes its color from the theme on every paint. An element that draws itself
    /// was skipped, so the text fell back to black and vanished on a dark background.
    /// </summary>
    [TestMethod]
    public void TheCollapsedPlaceholderTakesTheThemeColor()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor("{\n\n\n}\n");
        var manager = FoldingManager.Install(editor.TextArea);
        new BraceFoldingStrategy().UpdateFoldings(manager, editor.Document);
        manager.AllFoldings.Single().IsFolded = true;
        editor.Measure(new Size(600, 400));
        editor.Arrange(new Rect(0, 0, 600, 400));

        var factory = Application.DefaultGraphicsFactory;
        using (var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(600, 400, 1)))
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            editor.Render(context);
            context.EndFrame();
        }

        var visualLine = editor.TextArea.TextView.GetOrConstructVisualLine(
            editor.Document.GetLineByNumber(1));
        var placeholder = visualLine!.Elements.Single();

        Assert.AreEqual(editor.FoldingMarkerColor, placeholder.Foreground,
            "The placeholder kept no color, so it drew in the fallback black.");
    }
}
