using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Document;
using MewUI.MewvalonEdit.Test.Infrastructure;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// An editor a host builds, gives its font and then asks for a metric before the editor is in a window, as a host does
/// that wires services at construction. The surface reads its font before it is in the editor's template, and the
/// lines on screen must still use the editor's font.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class FontBeforeAttachTests
{
    private const string TEXT = "VAR\n    nCount : INT;\nEND_VAR";

    // Equal to the DPI a detached element falls back to in tests, so an attach does not also run a DPI change,
    // which rebuilds the lines on its own and would hide a missing font notice.
    private const double SCALE = 1.0;

    [TestMethod]
    public void TheEditorFontIsTheOneOnScreenWhenAMetricWasReadBeforeAttach()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var expected = Shown(readMetricBeforeAttach: false);
        var actual = Shown(readMetricBeforeAttach: true);

        Assert.AreEqual(expected, actual, 0.001, "The editor kept the font its surface read before it was in the template.");
    }

    private static double Shown(bool readMetricBeforeAttach)
    {
        var editor = new TextEditor { Document = new TextDocument(TEXT) };
        editor.FontFamily = "Consolas";
        editor.FontSize = 30;
        if (readMetricBeforeAttach)
        {
            _ = editor.TextArea.TextView.DefaultLineHeight;
        }

        var window = ScaledWindow.Create(SCALE, 800, 300);
        window.Content = editor;
        window.PerformLayout();
        return editor.TextArea.TextView.VisualLines[0].Height;
    }
}
