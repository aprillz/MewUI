using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class GroupBoxTemplateTests
{
    private static DelegateControlTemplate<GroupBox> PresenterTemplate()
        => new(static (owner, ctx) =>
        {
            var headerPresenter = new ContentPresenter { ContentSource = HeaderedContentControl.HeaderProperty };
            var contentPresenter = new ContentPresenter();
            return new StackPanel().Children(headerPresenter, contentPresenter);
        });

    [TestMethod]
    public void TemplatedGroupBox_ProjectsHeaderAndContentThroughPresenters()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var header = new TextBlock { Text = "title" };
        var content = new Border();

        var window = HeadlessWindow.Create();
        var box = new GroupBox { Header = header, Content = content, Template = PresenterTemplate() };
        window.Content = box;
        window.PerformLayout();

        Assert.IsInstanceOfType<ContentPresenter>(header.Parent, "the header projects through its own presenter");
        Assert.IsInstanceOfType<ContentPresenter>(content.Parent, "the content projects through its own presenter");
        Assert.AreNotSame(header.Parent, content.Parent, "each slot lands in its own presenter");
        Assert.AreSame(box, header.LogicalParent, "logical ownership of the header stays with the GroupBox");
        Assert.AreSame(box, content.LogicalParent, "logical ownership of the content stays with the GroupBox");
    }

    [TestMethod]
    public void TemplatedGroupBox_MeasuresThroughTemplateRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var template = new DelegateControlTemplate<GroupBox>(
            static (owner, ctx) => new Border { Width = 120, Height = 40 });

        var window = HeadlessWindow.Create();
        var box = new GroupBox
        {
            Header = new TextBlock { Text = "title" },
            Content = new Border(),
            Template = template
        };
        window.Content = new StackPanel().Children(box);
        window.PerformLayout();

        Assert.AreEqual(120, box.DesiredSize.Width, "measure follows the template root, not the GroupBox chrome");
        Assert.AreEqual(40, box.DesiredSize.Height, "measure follows the template root, not the GroupBox chrome");
    }

    [TestMethod]
    public void ClearTemplate_RestoresGroupBoxOwnLayout()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var header = new TextBlock { Text = "title" };
        var content = new Border();

        var window = HeadlessWindow.Create();
        var box = new GroupBox { Header = header, Content = content, Template = PresenterTemplate() };
        window.Content = box;
        window.PerformLayout();

        Assert.IsInstanceOfType<ContentPresenter>(header.Parent);
        Assert.IsInstanceOfType<ContentPresenter>(content.Parent);

        box.Template = null;
        window.PerformLayout();

        Assert.AreSame(box, header.Parent, "without a template the GroupBox hosts the header directly again");
        Assert.AreSame(box, content.Parent, "without a template the GroupBox hosts the content directly again");
        Assert.AreSame(box, header.LogicalParent);
        Assert.AreSame(box, content.LogicalParent);
    }
}
