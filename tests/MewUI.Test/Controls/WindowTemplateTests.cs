using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class WindowTemplateTests
{
    [TestMethod]
    public void Window_CustomChromeTemplate_ProjectsContentAndBindsChrome()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        Border? chrome = null;
        var template = new DelegateControlTemplate<Window>((owner, ctx) =>
        {
            var presenter = new ContentPresenter();
            var border = new Border { Child = presenter };
            ctx.Bind(border, Control.BackgroundProperty, Control.BackgroundProperty);
            chrome = border;
            return border;
        });

        var background = Color.FromRgb(30, 30, 46);
        var window = HeadlessWindow.Create();
        var content = new TextBlock { Text = "client area" };
        window.Content = content;
        window.Background = background;
        window.Template = template;
        window.PerformLayout();

        Assert.IsNotNull(chrome);
        Assert.AreEqual(background, chrome.Background, "chrome follows the window background through ctx.Bind");
        Assert.IsTrue(chrome.Bounds.Width > 0, "the template root is arranged by the window layout");

        Assert.AreSame(window, content.LogicalParent, "the window keeps logical ownership of its content");
        Assert.IsInstanceOfType<ContentPresenter>(content.Parent, "the chrome's presenter hosts the content visually");
        Assert.AreSame(window, content.FindVisualRoot(), "the content stays rooted in the window");

        var hit = window.HitTest(content.CenterOf());
        Assert.IsNotNull(hit);
        Assert.IsTrue(VisualTree.IsInSubtreeOf(hit, chrome), "hit testing routes through the chrome tree");
    }

    [TestMethod]
    public void Window_ClearTemplate_RestoresDirectContentLayout()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var template = new DelegateControlTemplate<Window>(
            static (owner, ctx) => new Border { Child = new ContentPresenter() });

        var window = HeadlessWindow.Create();
        var content = new Border();
        window.Content = content;
        window.Template = template;
        window.PerformLayout();
        Assert.IsInstanceOfType<ContentPresenter>(content.Parent);

        window.Template = null;
        window.PerformLayout();

        Assert.AreSame(window, content.Parent, "clearing the template rehosts the content directly");
        Assert.IsTrue(content.Bounds.Width > 0, "the direct layout path resumes");
    }
}
