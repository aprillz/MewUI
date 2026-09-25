using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewvalonEdit;
using MewUI.MewvalonEdit.Test.Infrastructure;

namespace MewUI.MewvalonEdit.Test;

/// <summary>A host draws its own things over the editor, such as rows pinned above the text, through the editor's overlays.</summary>
[TestClass]
[DoNotParallelize]
public sealed class EditorOverlayTests
{
    [TestMethod]
    public void OverlaysShownBeforeTheTemplateAreAllKeptInTheOrderShown()
    {
        var editor = new TextEditor { Text = "one\ntwo" };
        var first = new Border();
        var second = new Border();

        editor.ShowOverlay(first);
        editor.ShowOverlay(second);
        var window = HeadlessWindow.Create(400, 200);
        window.Content = editor;
        window.PerformLayout();

        var host = first.Parent as Panel;
        Assert.IsNotNull(host, "The first overlay shown before the template was dropped.");
        Assert.AreSame(host, second.Parent);
        var order = host.Children.ToList();
        Assert.IsLessThan(order.IndexOf(second), order.IndexOf(first), "An overlay shown later has to lie over one shown earlier.");
    }

    [TestMethod]
    public void HidingAnOverlayTakesItOutOfTheEditor()
    {
        var editor = new TextEditor { Text = "one\ntwo" };
        var overlay = new Border();
        var window = HeadlessWindow.Create(400, 200);
        window.Content = editor;
        window.PerformLayout();
        editor.ShowOverlay(overlay);
        Assert.IsNotNull(overlay.Parent);

        editor.HideOverlay(overlay);

        Assert.IsNull(overlay.Parent);
    }
}
