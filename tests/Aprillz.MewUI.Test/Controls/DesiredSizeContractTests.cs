using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Measure desired-size contract (issue #199): desired size is the natural content size.
/// Alignment and virtualization must not echo the available constraint back as desired.
/// </summary>
[TestClass]
public sealed class DesiredSizeContractTests
{
    private const double PROBE_W = 500;
    private const double PROBE_H = 300;

    [TestMethod]
    public void EmptyListBox_FiniteConstraintWithStretch_DesiredIsChromeOnly()
    {
        var listBox = new ListBox();

        listBox.Measure(new Size(PROBE_W, PROBE_H));

        Assert.IsLessThan(50, listBox.DesiredSize.Width, $"desired width {listBox.DesiredSize.Width} must be chrome only, not the constraint");
        Assert.IsLessThan(50, listBox.DesiredSize.Height, $"desired height {listBox.DesiredSize.Height} must be chrome only, not the constraint");
    }

    [TestMethod]
    public void ListBox_DesiredWidth_IsNaturalItemWidth_IndependentOfConstraint()
    {
        var narrow = CreateListBox("a", "bb", "ccc");
        var wide = CreateListBox("a", "bb", "ccc");

        narrow.Measure(new Size(PROBE_W, PROBE_H));
        wide.Measure(new Size(PROBE_W + 300, PROBE_H));

        Assert.AreEqual(narrow.DesiredSize.Width, wide.DesiredSize.Width, 0.5,
            "natural width must not depend on the offered constraint");
        Assert.IsLessThan(PROBE_W / 2, narrow.DesiredSize.Width);
        Assert.IsGreaterThan(0, narrow.DesiredSize.Width);
    }

    [TestMethod]
    public void ListBox_ItemWiderThanConstraint_DesiredIsCappedAtConstraint()
    {
        var listBox = CreateListBox(new string('w', 200));

        listBox.Measure(new Size(200, PROBE_H));

        Assert.IsLessThanOrEqualTo(200 + 0.5, listBox.DesiredSize.Width);
    }

    [TestMethod]
    public void TreeView_DesiredWidth_IsNaturalItemWidth()
    {
        var narrow = CreateTreeView("n0", "n1");
        var wide = CreateTreeView("n0", "n1");

        narrow.Measure(new Size(PROBE_W, PROBE_H));
        wide.Measure(new Size(PROBE_W + 300, PROBE_H));

        Assert.AreEqual(narrow.DesiredSize.Width, wide.DesiredSize.Width, 0.5);
        Assert.IsLessThan(PROBE_W / 2, narrow.DesiredSize.Width);
    }

    [TestMethod]
    public void ItemsControl_DesiredHeight_IsPreferredViewportClampedToConstraint()
    {
        var empty = new ItemsControl();
        empty.Measure(new Size(PROBE_W, 1000));
        Assert.IsLessThan(50, empty.DesiredSize.Height, $"empty desired height {empty.DesiredSize.Height} must be chrome only");

        var three = CreateItemsControl(3);
        three.Measure(new Size(PROBE_W, 1000));

        var twenty = CreateItemsControl(20);
        twenty.Measure(new Size(PROBE_W, 1000));

        // 1..11 items follow the item count; 12+ stays at the preferred viewport cap.
        Assert.IsGreaterThan(empty.DesiredSize.Height, three.DesiredSize.Height);
        Assert.IsGreaterThan(three.DesiredSize.Height, twenty.DesiredSize.Height);
        Assert.IsLessThan(500, twenty.DesiredSize.Height, $"20 items must cap at the preferred viewport, got {twenty.DesiredSize.Height}");

        var clamped = CreateItemsControl(20);
        clamped.Measure(new Size(PROBE_W, 100));
        Assert.IsLessThanOrEqualTo(100 + 0.5, clamped.DesiredSize.Height, "the preferred viewport must clamp to the finite constraint");
    }

    [TestMethod]
    public void ItemsControl_StackPresenter_DesiredHeightIsTotalMeasuredHeight()
    {
        var itemsControl = CreateItemsControl(3);
        itemsControl.SetPresenter(new StackItemsPresenter());

        itemsControl.Measure(new Size(PROBE_W, 1000));

        Assert.IsGreaterThan(0, itemsControl.DesiredSize.Height);
        Assert.IsLessThan(1000, itemsControl.DesiredSize.Height);
    }

    [TestMethod]
    public void FitContentWindow_EmptyListBox_ClientIsContentSized_DockPanelIrrelevant()
    {
        var withDock = LayoutFitWindow(new DockPanel().Children(new ListBox()));
        var withoutDock = LayoutFitWindow(new ListBox());

        Assert.IsLessThan(100, withDock.Width, $"client width {withDock.Width} must be content sized, not the max");
        Assert.IsLessThan(100, withDock.Height, $"client height {withDock.Height} must be content sized, not the max");
        Assert.AreEqual(withDock, withoutDock, "DockPanel must not change the fit result");
    }

    [TestMethod]
    public void FitContentWindow_VirtualizedItemsControl_DoesNotExpandToMaxHeight()
    {
        var client = LayoutFitWindow(CreateItemsControl(50));

        Assert.IsLessThan(500, client.Height, $"client height {client.Height} must follow the preferred viewport, not the max");
    }

    [TestMethod]
    public void PlainWindow_StretchListBox_StillFillsViewportAtArrange()
    {
        var window = CreateHeadlessWindow(500, 300);
        var listBox = new ListBox();
        window.Content = listBox;

        window.PerformLayout();

        Assert.AreEqual(new Rect(0, 0, 500, 300), listBox.Bounds);
    }

    [TestMethod]
    public void StretchListBox_ItemWiderThanViewport_ExtentExceedsViewport()
    {
        var window = CreateHeadlessWindow(200, 150);
        var listBox = CreateListBox(new string('w', 200));
        window.Content = listBox;

        window.PerformLayout();

        var presenter = (IItemsPresenter)typeof(ListBox)
            .GetField("_presenter", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(listBox)!;

        Assert.IsGreaterThan(200, presenter.Extent.Width, $"extent {presenter.Extent.Width} must expose the natural item width for horizontal scrolling");
    }

    private static ListBox CreateListBox(params string[] items)
    {
        var listBox = new ListBox();
        listBox.ItemsSource = ItemsView.Create(items);
        return listBox;
    }

    private static TreeView CreateTreeView(params string[] names)
    {
        var tree = new TreeView();
        var nodes = new Node[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            nodes[i] = new Node(names[i]);
        }

        tree.ItemsSource = TreeItemsView.Create<Node>(nodes, node => node.Children, textSelector: node => node.Name);
        return tree;
    }

    private static ItemsControl CreateItemsControl(int count)
    {
        var items = new string[count];
        for (int i = 0; i < count; i++)
        {
            items[i] = $"item {i}";
        }

        var itemsControl = new ItemsControl();
        itemsControl.ItemsSource = ItemsView.Create(items);
        return itemsControl;
    }

    private static Window CreateHeadlessWindow(double width, double height)
    {
        var window = HeadlessWindow.Create(width, height);
        window.Padding = new Thickness(0);
        return window;
    }

    private static Size LayoutFitWindow(Element content)
    {
        var backend = new ApplyingWindowBackend();
        var window = new Window();
        window.Padding = new Thickness(0);
        window.AttachBackend(backend);
        backend.Window = window;
        window.SetClientSizeDip(800, 600);
        window.WindowSize = WindowSize.FitContentSize(1000, 1000);
        window.Content = content;

        window.PerformLayout();
        return window.ClientSize;
    }

    private sealed record Node(string Name)
    {
        public Node[] Children { get; } = [];
    }
}
