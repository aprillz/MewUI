using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class ExpanderTemplateTests
{
    private static DelegateControlTemplate<Expander> PresenterTemplate()
        => new(static (owner, ctx) =>
        {
            var headerPresenter = new ContentPresenter { ContentSource = HeaderedContentControl.HeaderProperty };
            var contentPresenter = new ContentPresenter();
            ctx.Bind(contentPresenter, ContentPresenter.IsVisibleProperty, Expander.IsExpandedProperty);
            return new StackPanel().Children(headerPresenter, contentPresenter);
        });

    [TestMethod]
    public void TemplatedExpander_ProjectsHeaderAndContentThroughPresenters()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var header = new TextBlock { Text = "title" };
        var content = new Border();

        var window = HeadlessWindow.Create();
        var expander = new Expander { Header = header, Content = content, Template = PresenterTemplate() };
        window.Content = expander;
        window.PerformLayout();

        Assert.IsInstanceOfType<ContentPresenter>(header.Parent, "the header projects through its own presenter");
        Assert.IsInstanceOfType<ContentPresenter>(content.Parent, "the content projects through its own presenter");
        Assert.AreNotSame(header.Parent, content.Parent, "each slot lands in its own presenter");
        Assert.AreSame(expander, header.LogicalParent, "logical ownership of the header stays with the Expander");
        Assert.AreSame(expander, content.LogicalParent, "logical ownership of the content stays with the Expander");
    }

    [TestMethod]
    public void TemplatedExpander_ContentPresenterVisibilityFollowsIsExpanded()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var header = new TextBlock { Text = "title" };
        var content = new Border();

        var window = HeadlessWindow.Create();
        var expander = new Expander { Header = header, Content = content, Template = PresenterTemplate(), IsExpanded = true };
        window.Content = expander;
        window.PerformLayout();

        var contentPresenter = (ContentPresenter)content.Parent!;
        Assert.IsTrue(contentPresenter.IsVisible, "presenter starts visible while expanded");

        expander.IsExpanded = false;

        Assert.IsFalse(contentPresenter.IsVisible, "collapsing follows the ctx.Bind wiring in the template");
    }

    [TestMethod]
    public void ClearTemplate_RestoresExpanderOwnLayout()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var header = new TextBlock { Text = "title" };
        var content = new Border();

        var window = HeadlessWindow.Create();
        var expander = new Expander { Header = header, Content = content, Template = PresenterTemplate() };
        window.Content = expander;
        window.PerformLayout();

        Assert.IsInstanceOfType<ContentPresenter>(header.Parent);
        Assert.IsInstanceOfType<ContentPresenter>(content.Parent);

        expander.Template = null;
        window.PerformLayout();

        Assert.AreSame(expander, header.Parent, "without a template the Expander hosts the header directly again");
        Assert.AreSame(expander, content.Parent, "without a template the Expander hosts the content directly again");
        Assert.AreSame(expander, header.LogicalParent);
        Assert.AreSame(expander, content.LogicalParent);
    }

    [TestMethod]
    public void CollapsedContent_DoesNotParticipateInVisualTree()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var content = new Border();
        var window = HeadlessWindow.Create();
        var expander = new Expander { Header = new TextBlock { Text = "title" }, Content = content, IsExpanded = true };
        window.Content = expander;
        window.PerformLayout();

        Assert.IsTrue(VisitsChild(expander, content), "expanded content participates in the visual tree");

        expander.IsExpanded = false;
        window.PerformLayout();

        Assert.IsFalse(VisitsChild(expander, content), "collapsed content is not part of this frame's visual tree");
    }

    private static bool VisitsChild(IVisualTreeHost host, Element child)
    {
        bool visited = false;
        host.VisitChildren(candidate =>
        {
            if (ReferenceEquals(candidate, child))
            {
                visited = true;
                return false;
            }
            return true;
        });
        return visited;
    }
}
