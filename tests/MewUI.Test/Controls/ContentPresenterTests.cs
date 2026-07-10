using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class ContentPresenterTests
{
    private static DelegateControlTemplate<ContentControl> PresenterTemplate()
        => new(static (owner, ctx) =>
        {
            var presenter = new ContentPresenter();
            ctx.Register("Presenter", presenter);
            return new Border { Child = presenter };
        });

    private static (Window Window, ContentControl Host) CreateShown(ControlTemplate? template, Element? content)
    {
        var window = HeadlessWindow.Create();
        var host = new ContentControl();
        if (content != null)
        {
            host.Content = content;
        }
        if (template != null)
        {
            host.Template = template;
        }
        window.Content = host;
        window.PerformLayout();
        return (window, host);
    }

    [TestMethod]
    public void TemplatedContentControl_ProjectsContentThroughPresenter()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var content = new TextBlock { Text = "hello" };
        var (_, host) = CreateShown(PresenterTemplate(), content);

        Assert.AreSame(host, content.LogicalParent, "logical ownership stays with the control");
        Assert.IsInstanceOfType<ContentPresenter>(content.Parent, "the presenter owns the visual attach");
        Assert.AreSame(host, content.Parent!.Parent!.Parent, "visual chain runs control -> border -> presenter -> content");
    }

    [TestMethod]
    public void ContentChange_WhileTemplated_Reprojects()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var first = new TextBlock();
        var (window, host) = CreateShown(PresenterTemplate(), first);
        var second = new TextBlock();

        host.Content = second;

        Assert.IsNull(first.LogicalParent);
        Assert.IsNull(first.Parent, "the replaced content leaves the presenter");
        Assert.AreSame(host, second.LogicalParent);
        Assert.IsInstanceOfType<ContentPresenter>(second.Parent);

        window.PerformLayout();
    }

    [TestMethod]
    public void TemplateWithoutPresenter_ContentStaysLogicalOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var content = new TextBlock();
        var template = new DelegateControlTemplate<ContentControl>(static (owner, ctx) => new Border());

        var (_, host) = CreateShown(template, content);

        Assert.AreSame(host, content.LogicalParent, "logical ownership survives without a visual slot");
        Assert.IsNull(content.Parent, "no presenter means no visual attach and no silent fallback");
    }

    [TestMethod]
    public void ClearTemplate_RestoresDirectHosting()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var content = new TextBlock();
        var (window, host) = CreateShown(PresenterTemplate(), content);
        Assert.IsInstanceOfType<ContentPresenter>(content.Parent);

        host.Template = null;

        Assert.AreSame(host, content.Parent, "the control hosts its content directly again");
        window.PerformLayout();
        Assert.AreSame(host, content.Parent);
    }

    [TestMethod]
    public void HeaderSlot_ProjectsViaContentSource()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var template = new DelegateControlTemplate<GroupBox>(static (owner, ctx) =>
        {
            var headerPresenter = new ContentPresenter { ContentSource = HeaderedContentControl.HeaderProperty };
            var contentPresenter = new ContentPresenter();
            return new StackPanel().Children(headerPresenter, contentPresenter);
        });

        var window = HeadlessWindow.Create();
        var box = new GroupBox();
        var header = new TextBlock { Text = "title" };
        var content = new Border();
        box.Header = header;
        box.Content = content;
        box.Template = template;
        window.Content = box;
        window.PerformLayout();

        Assert.IsInstanceOfType<ContentPresenter>(header.Parent, "the header projects through its own presenter");
        Assert.IsInstanceOfType<ContentPresenter>(content.Parent);
        Assert.AreNotSame(header.Parent, content.Parent, "each slot lands in its own presenter");
        Assert.AreSame(box, header.LogicalParent);
        Assert.AreSame(box, content.LogicalParent);
    }

    [TestMethod]
    public void CtxBind_SyncsNowAndFollowsOwnerChanges()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        Border? part = null;
        var template = new DelegateControlTemplate<ContentControl>((owner, ctx) =>
        {
            var border = new Border();
            part = border;
            ctx.Bind(border, Control.BackgroundProperty, Control.BackgroundProperty);
            return border;
        });

        var initial = Color.FromRgb(10, 20, 30);
        var window = HeadlessWindow.Create();
        var host = new ContentControl { Background = initial, Template = template };
        window.Content = host;
        window.PerformLayout();

        Assert.IsNotNull(part);
        Assert.AreEqual(initial, part.Background, "the binding applies the current owner value on build");

        var changed = Color.FromRgb(200, 0, 0);
        host.Background = changed;
        Assert.AreEqual(changed, part.Background, "the binding forwards later owner changes");

        host.Template = null;
        var afterRelease = Color.FromRgb(0, 200, 0);
        host.Background = afterRelease;
        Assert.AreEqual(changed, part.Background, "released bindings no longer forward");
    }

    [TestMethod]
    public void CompatAttach_MovesToPresenterOnApply()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var content = new TextBlock();
        var host = new ContentControl { Content = content };
        window.Content = host;
        window.PerformLayout();
        Assert.AreSame(host, content.Parent, "compat path hosts the content directly");

        host.Template = PresenterTemplate();
        window.PerformLayout();

        Assert.IsInstanceOfType<ContentPresenter>(content.Parent, "applying a template hands the visual attach to the presenter");
        Assert.AreSame(host, content.LogicalParent);
    }
}
