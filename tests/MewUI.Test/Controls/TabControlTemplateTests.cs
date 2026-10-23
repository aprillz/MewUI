using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class TabControlTemplateTests
{
    private static DelegateControlTemplate<TabControl> HeadersAndContentTemplate(Border headersHost, ContentPresenter presenter)
        => new((owner, ctx) =>
        {
            ctx.Register(TabControl.PART_HEADERS_HOST, headersHost);
            return new DockPanel().Children(headersHost.DockTop(), presenter);
        });

    private static (Window Window, TabControl Tabs, Border Host, ContentPresenter Presenter, Border First, Border Second) CreateTemplated()
    {
        var first = new Border();
        var second = new Border();
        var host = new Border();
        var presenter = new ContentPresenter();
        var tabs = new TabControl();
        tabs.AddTab(new TabItem { Header = new TextBlock { Text = "One" }, Content = first });
        tabs.AddTab(new TabItem { Header = new TextBlock { Text = "Two" }, Content = second });
        tabs.Template = HeadersAndContentTemplate(host, presenter);

        var window = HeadlessWindow.Create(400, 300);
        window.Content = tabs;
        window.PerformLayout();
        return (window, tabs, host, presenter, first, second);
    }

    [TestMethod]
    public void HeadersHostAndPresenter_ShowTheHeaderRowAndTheSelectedContent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (_, tabs, host, presenter, first, _) = CreateTemplated();

        Assert.IsNotNull(host.Child, "the header row did not move into the host part");
        Assert.IsGreaterThan(0.0, host.Bounds.Height, "the hosted header row has no height");
        Assert.AreSame(presenter, first.Parent, "the selected content is not projected by the presenter");
        Assert.AreSame(tabs, first.LogicalParent, "logical ownership of the content stays with the TabControl");
    }

    [TestMethod]
    public void ClickOnAHostedHeader_SelectsItsTabAndSwapsTheProjection()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (window, tabs, host, presenter, first, second) = CreateTemplated();
        var secondHeader = FindHeader(host, "Two");

        window.SendClick(secondHeader.CenterOf());
        window.PerformLayout();

        Assert.AreEqual(1, tabs.SelectedIndex, "a click on the hosted header did not select its tab");
        Assert.AreSame(presenter, second.Parent, "the presenter does not show the newly selected content");
        Assert.IsNull(first.Parent, "the previous content kept a visual place");
    }

    [TestMethod]
    public void ClearTemplate_RestoresTheDrawnHeadersAndContent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (window, tabs, host, _, first, _) = CreateTemplated();

        tabs.Template = null;
        window.PerformLayout();

        Assert.IsNull(host.Child, "the discarded host still holds the header row");
        Assert.AreSame(tabs, first.Parent, "the TabControl does not host the selected content again");

        window.SendClick(FindHeader(tabs, "Two").CenterOf());
        Assert.AreEqual(1, tabs.SelectedIndex, "headers stopped working after the template was cleared");
    }

    [TestMethod]
    public void MissingHeadersHostPart_Throws()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var tabs = new TabControl();
        tabs.AddTab(new TabItem { Header = new TextBlock { Text = "One" }, Content = new Border() });
        tabs.Template = new DelegateControlTemplate<TabControl>((_, _) => new ContentPresenter());
        var window = HeadlessWindow.Create(400, 300);
        window.Content = tabs;

        Assert.ThrowsExactly<InvalidOperationException>(() => window.PerformLayout());
    }

    private static UIElement FindHeader(Element root, string text)
    {
        UIElement? found = null;
        VisualTree.Visit(root, element =>
        {
            if (found == null && element is TabHeaderButton header && header.Content is TextBlock label && label.Text == text)
            {
                found = header;
            }
        });

        Assert.IsNotNull(found, $"no header shows '{text}'");
        return found;
    }
}
