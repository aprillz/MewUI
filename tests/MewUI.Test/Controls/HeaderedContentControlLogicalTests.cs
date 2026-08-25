using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class HeaderedContentControlLogicalTests
{
    [TestMethod]
    public void SetHeader_AttachesLogicallyAndVisually()
    {
        var host = new HeaderedContentControl();
        var header = new TextBlock();

        host.Header = header;

        Assert.AreSame(host, header.LogicalParent);
        Assert.AreSame(host, header.Parent);
    }

    [TestMethod]
    public void ReplaceHeader_ReleasesOldAndAttachesNew()
    {
        var host = new HeaderedContentControl();
        var first = new TextBlock();
        var second = new TextBlock();
        host.Header = first;

        host.Header = second;

        Assert.IsNull(first.LogicalParent);
        Assert.IsNull(first.Parent);
        Assert.AreSame(host, second.LogicalParent);
    }

    [TestMethod]
    public void SetHeader_AlreadyContent_Throws()
    {
        var host = new HeaderedContentControl();
        var element = new Border();
        host.Content = element;

        Assert.ThrowsExactly<InvalidOperationException>(() => host.Header = element);

        Assert.IsNull(host.Header, "rejected set must not commit");
        Assert.AreSame(element, host.Content);
        Assert.AreSame(host, element.LogicalParent);
    }

    [TestMethod]
    public void SetContent_AlreadyHeader_Throws()
    {
        var host = new HeaderedContentControl();
        var element = new Border();
        host.Header = element;

        Assert.ThrowsExactly<InvalidOperationException>(() => host.Content = element);

        Assert.IsNull(host.Content);
        Assert.AreSame(element, host.Header);
    }

    [TestMethod]
    public void SetHeader_OwnedElsewhere_Throws()
    {
        var owner = new ContentControl();
        var host = new HeaderedContentControl();
        var element = new Border();
        owner.Content = element;

        Assert.ThrowsExactly<InvalidOperationException>(() => host.Header = element);

        Assert.AreSame(owner, element.LogicalParent);
    }

    [TestMethod]
    public void LogicalTraversal_VisitsHeaderThenContentOnce()
    {
        var host = new HeaderedContentControl();
        var header = new TextBlock();
        var content = new Border();
        host.Header = header;
        host.Content = content;

        var visited = new List<Element>();
        LogicalTree.Visit(host, visited.Add);

        int headerIndex = visited.IndexOf(header);
        int contentIndex = visited.IndexOf(content);
        Assert.AreEqual(1, visited.Count(element => ReferenceEquals(element, header)));
        Assert.AreEqual(1, visited.Count(element => ReferenceEquals(element, content)));
        Assert.IsLessThan(contentIndex, headerIndex, "traversal order must be Header before Content");
    }

    [TestMethod]
    public void GroupBox_InheritsLogicalSlots()
    {
        var box = new GroupBox();
        var header = new TextBlock();
        var content = new Border();

        box.Header = header;
        box.Content = content;

        Assert.AreSame(box, header.LogicalParent);
        Assert.AreSame(box, content.LogicalParent);
        Assert.ThrowsExactly<InvalidOperationException>(() => box.Header = content);
    }

    [TestMethod]
    public void Expander_InheritsLogicalSlots()
    {
        var expander = new Expander();
        var header = new TextBlock();
        var content = new Border();

        expander.Header = header;
        expander.Content = content;

        Assert.AreSame(expander, header.LogicalParent);
        Assert.AreSame(expander, content.LogicalParent);
        Assert.AreSame(expander, content.FindLogicalRoot());
    }
}
