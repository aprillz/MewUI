using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock;
using Aprillz.MewUI.MewDock.Controls;
using Aprillz.MewUI.Rendering;

using MewUI.Test.Infrastructure;

using static MewUI.MewDock.Test.DockTestSupport;

namespace MewUI.MewDock.Test;

/// <summary>Explicit pane content shows as soon as the pane is added (in the pane layer), and tab headers draw once.</summary>
[TestClass]
public sealed class PaneViewTests
{
    [TestMethod]
    public void AddedDocumentShowsItsContentAtOnce()
    {
        var manager = Load(DockedTools(Tool("Explorer")));
        manager.ContentFactory = _ => null;
        var view = new TextBlock { Text = "document" };

        manager.AddDocumentPane("File.st", view, "document");

        Assert.IsTrue(IsInside<PaneLayer>(view), "the new tab's content is shown without another selection change");
    }

    [TestMethod]
    public void PaneAddedToAGroupShowsItsContentAtOnce()
    {
        var manager = Load(DockedTools(Tool("Explorer")));
        manager.ContentFactory = _ => null;
        var group = Pane(manager, "Explorer").Group!;
        var view = new TextBlock { Text = "tool" };

        group.AddPane("Extra", view, "extra");

        Assert.IsTrue(IsInside<PaneLayer>(view));
    }

    [TestMethod]
    public void DocumentTabDrawsItsHeaderOnce()
    {
        var headers = new List<CountingHeader>();
        var manager = new DockingManager { HeaderFactory = _ => Track(headers, new CountingHeader()) };
        manager.LoadLayout(DockedTools(Tool("Explorer")));
        var button = Descendants(manager).OfType<FlexTabButton>().First(candidate => IsHeaderOf(candidate, headers));

        RenderAlone(button);

        Assert.AreEqual(1, headers.Single(header => IsInside(header, button)).RenderCount);
    }

    [TestMethod]
    public void BorderTabDrawsItsHeaderOnce()
    {
        var headers = new List<CountingHeader>();
        var manager = new DockingManager { HeaderFactory = _ => Track(headers, new CountingHeader()) };
        manager.LoadLayout(AutoHiddenTools(Tool("Explorer")));
        var button = Descendants(manager).OfType<FlexBorderButton>().First();

        RenderAlone(button);

        Assert.AreEqual(1, headers.Single(header => IsInside(header, button)).RenderCount);
    }

    private static CountingHeader Track(List<CountingHeader> headers, CountingHeader header)
    {
        headers.Add(header);
        return header;
    }

    private static bool IsInside<TAncestor>(Element element)
    {
        for (Element? current = element.Parent; current is not null; current = current.Parent)
        {
            if (current is TAncestor)
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsHeaderOf(Element button, List<CountingHeader> headers) => headers.Any(header => IsInside(header, button));

    private static bool IsInside(Element element, Element ancestor)
    {
        for (Element? current = element; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }
        return false;
    }

    private static void RenderAlone(UIElement element)
    {
        element.Measure(new Size(200, 30));
        element.Arrange(new Rect(0, 0, 200, 30));
        element.Render(new SinkContext());
    }

    private sealed class SinkContext : NoOpGraphicsContext
    {
    }

    /// <summary>A header that counts how often it is drawn.</summary>
    private sealed class CountingHeader : FrameworkElement
    {
        public int RenderCount { get; private set; }

        protected override Size MeasureContent(Size availableSize) => new(40, 16);

        protected override void OnRender(IGraphicsContext context) => RenderCount++;
    }
}
