using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A tree view wraps each row in an <see cref="ItemContainer"/> only while a container hook is
/// registered, and that container spans the whole row: indent and expander included.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TreeViewContainerTests
{
    private const double WIDTH = 400;
    private const double HEIGHT = 300;

    [TestMethod]
    public void NoHook_PlacesTheTemplateRootInTheContentAreaWithoutAContainer()
    {
        var (tree, _, _) = MakeTree();
        Layout(tree);

        int containers = 0;
        tree.VisitRealizedContainers((_, element) =>
        {
            if (element is ItemContainer)
            {
                containers++;
            }
        });

        Assert.AreEqual(0, containers, "applications that do not use the hooks must not pay for a wrapper");
    }

    [TestMethod]
    public void Hook_WrapsEachRowInAContainerThatSpansTheRowAndPadsPastTheIndent()
    {
        var (tree, root, _) = MakeTree();
        tree.PrepareContainer<TreeViewNode>((_, _, _, _) => { });
        tree.Expand(root);
        Layout(tree);

        ItemContainer? rootRow = null;
        ItemContainer? childRow = null;
        tree.VisitRealizedContainers((index, element) =>
        {
            if (index == 0)
            {
                rootRow = (ItemContainer)element;
            }
            else if (index == 1)
            {
                childRow = (ItemContainer)element;
            }
        });

        Assert.IsNotNull(rootRow);
        Assert.IsNotNull(childRow);
        Assert.AreSame(root, rootRow.Item);
        Assert.AreSame(root.Children[0], childRow.Item);

        Assert.AreEqual(rootRow.Bounds.X, childRow.Bounds.X, "every container starts at the row's left edge regardless of depth");
        Assert.AreEqual(tree.Indent, rootRow.Padding.Left, "a root row pads past its expander");
        Assert.AreEqual(tree.Indent * 2, childRow.Padding.Left, "a child row pads past the indent and its expander");

        var childContent = (FrameworkElement)childRow.Content!;
        Assert.AreEqual(childRow.Bounds.X + childRow.Padding.Left, childContent.Bounds.X, 0.5,
            "the content sits where the content area used to start");
    }

    [TestMethod]
    public void IsSelected_FollowsTheSelection()
    {
        var (tree, root, _) = MakeTree();
        tree.PrepareContainer<TreeViewNode>((_, _, _, _) => { });
        tree.Expand(root);
        Layout(tree);

        tree.SelectedNode = root.Children[0];
        Layout(tree);

        tree.VisitRealizedContainers((index, element) =>
            Assert.AreEqual(index == 1, ((ItemContainer)element).IsSelected, $"row {index}"));
    }

    [TestMethod]
    public void RightClickOnTheExpander_OpensTheRowMenuWithTheNodeAndLeftClickStillExpands()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var (tree, root, _) = MakeTree();
        tree.HorizontalAlignment = HorizontalAlignment.Left;
        tree.VerticalAlignment = VerticalAlignment.Top;

        var command = new Command("test.node", "Node");
        TreeViewNode? received = null;
        window.Commands.Register(command, (TreeViewNode node) => received = node);
        var menu = new ContextMenu();
        menu.AddEntry(new MenuItem(command));
        tree.PrepareContainer<TreeViewNode>((container, _, _, _) => container.ContextMenu = menu);

        window.Content = tree;
        window.PerformLayout();

        // Two pixels into the container's left edge lands on the expander glyph, not on the content.
        var rootRow = RealizedRow(tree, 0);
        var onGlyph = new Point(rootRow.Bounds.X + 2, rootRow.Bounds.Y + rootRow.Bounds.Height / 2);

        Assert.IsFalse(tree.IsExpanded(root));
        window.SendClick(onGlyph);
        Assert.IsTrue(tree.IsExpanded(root), "the tree still hit-tests the expander through the container");

        // Expanding rebuilds the rows, so the containers exist again only after the next layout.
        window.PerformLayout();
        Assert.AreEqual(rootRow.Bounds, RealizedRow(tree, 0).Bounds, "the root row keeps its place");

        window.SendClick(onGlyph, MouseButton.Right);
        window.PerformLayout();
        Assert.IsGreaterThan(0.0, menu.Bounds.Width, "the row's menu opens from the expander area too");

        var bounds = menu.Bounds;
        window.SendClick(new Point(bounds.X + bounds.Width / 2, bounds.Y + 12));
        Assert.AreSame(root, received, "the row's node reaches the handler as the operand");
    }

    private static ItemContainer RealizedRow(TreeView tree, int rowIndex)
    {
        ItemContainer? found = null;
        tree.VisitRealizedContainers((index, element) =>
        {
            if (index == rowIndex)
            {
                found = (ItemContainer)element;
            }
        });

        Assert.IsNotNull(found, $"row {rowIndex} is realized");
        return found;
    }

    private static (TreeView Tree, TreeViewNode Root, TreeViewNode Leaf) MakeTree()
    {
        var leaf = new TreeViewNode("leaf");
        var root = new TreeViewNode("root", [new TreeViewNode("child"), new TreeViewNode("sibling")]);
        var tree = new TreeView { Width = WIDTH, Height = HEIGHT };
        tree.ItemsSource([root, leaf]);
        return (tree, root, leaf);
    }

    private static void Layout(TreeView tree)
    {
        tree.Measure(new Size(WIDTH, HEIGHT));
        tree.Arrange(new Rect(0, 0, WIDTH, HEIGHT));
    }
}
