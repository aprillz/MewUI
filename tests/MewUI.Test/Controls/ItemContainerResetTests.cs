using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

/// <summary>
/// A recycled item container starts the next item without what the previous item's hook wrote to it,
/// and a rebind leaves alone the values the owning control configures and the hook writes again.
/// </summary>
[TestClass]
public sealed class ItemContainerResetTests
{
    private const double WIDTH = 400;
    private const double HEIGHT = 300;
    private const double CORNER_RADIUS = 8;

    [TestMethod]
    public void ListBox_WithoutAHook_RebindLeavesTheItemRadiusAlone()
    {
        var box = new ListBox
        {
            ItemsSource = ItemsView.Create(Items(100)),
            CornerRadius = CORNER_RADIUS,
            BorderThickness = 0,
            Width = WIDTH,
            Height = HEIGHT,
        };

        Assert.AreEqual(0, CountChangesWhileScrolling(box, box.ScrollIntoView, Control.CornerRadiusProperty),
            "a rebind cleared the radius the list configures and wrote it again");
    }

    [TestMethod]
    public void NavigationList_WithoutAHook_RebindLeavesTheItemRadiusAlone()
    {
        var list = new NavigationList
        {
            ItemsSource = ItemsView.Create(Items(100)),
            CornerRadius = CORNER_RADIUS,
            BorderThickness = 0,
            Width = WIDTH,
            Height = HEIGHT,
        };

        Assert.AreEqual(0, CountChangesWhileScrolling(list, list.ScrollIntoView, Control.CornerRadiusProperty),
            "a rebind cleared the radius the list configures and wrote it again");
    }

    [TestMethod]
    public void TreeView_RebindLeavesTheItemRadiusAlone()
    {
        var tree = new TreeView
        {
            CornerRadius = CORNER_RADIUS,
            BorderThickness = 0,
            Width = WIDTH,
            Height = HEIGHT,
        };
        tree.ItemsSource(Items(100).Select(item => new TreeViewNode(item)).ToArray());
        // A tree row gets a container only while a container hook is registered.
        tree.PrepareContainer<TreeViewNode>((_, _, _, _) => { });

        Assert.AreEqual(0, CountChangesWhileScrolling(tree, tree.ScrollIntoView, Control.CornerRadiusProperty),
            "a rebind cleared the radius the tree configures and wrote it again");
    }

    [TestMethod]
    public void GridView_WithoutAHook_RebindLeavesHitTestingAlone()
    {
        var grid = MakeGrid();
        Layout(grid);
        var log = new BindingDiagnosticsLog();
        System.Diagnostics.Trace.Listeners.Add(log);
        try
        {
            for (int step = 5; step <= 95; step += 5)
            {
                grid.ScrollIntoView(step);
                Layout(grid);
            }
        }
        finally
        {
            System.Diagnostics.Trace.Listeners.Remove(log);
        }

        // The diagnostics only exist in debug builds of the framework.
        Assert.IsEmpty(log.Lines, "a rebind cleared a row value nobody set: " + string.Join(" | ", log.Lines.Distinct()));
    }

    [TestMethod]
    public void ListBox_ValuesTheHookWroteForOneItem_DoNotReachTheNextItem()
    {
        var red = Color.FromArgb(255, 255, 0, 0);
        var box = MakeListBox();
        box.PrepareContainer<string>((container, _, index, _) =>
        {
            if (index == 0)
            {
                container.Background = red;
                container.Padding = new Thickness(7);
                container.BorderThickness = 3;
            }
        });

        AssertTheFirstContainerIsCleanOnReuse(box, box.ScrollIntoView, box.VisitRealizedContainers, container =>
        {
            Assert.AreNotEqual(red, container.Background, "the first item's background followed the container");
            Assert.AreNotEqual(new Thickness(7), container.Padding, "the first item's padding followed the container");
            Assert.AreNotEqual(3, container.BorderThickness, "the first item's border followed the container");
        });
    }

    [TestMethod]
    public void GridView_ValuesTheHookWroteForOneItem_DoNotReachTheNextItem()
    {
        var red = Color.FromArgb(255, 255, 0, 0);
        var grid = MakeGrid();
        grid.PrepareContainer<string>((row, _, index, _) =>
        {
            if (index == 0)
            {
                row.Background = red;
            }
        });
        Layout(grid);

        GridViewRow? first = null;
        grid.VisitRealizedRows((index, row) => first = index == 0 ? row : first);
        Assert.IsNotNull(first);

        bool reused = false;
        for (int step = 5; step <= 95; step += 5)
        {
            grid.ScrollIntoView(step);
            Layout(grid);
            grid.VisitRealizedRows((index, row) =>
            {
                if (ReferenceEquals(row, first) && index != 0)
                {
                    reused = true;
                    Assert.AreNotEqual(red, row.Background, $"row {index} kept the first item's background");
                }
            });
        }

        Assert.IsTrue(reused, "the first row was never reused, so this proves nothing");
    }

    [TestMethod]
    public void ListBox_AValueTheHookOverrode_ReturnsToTheListsOwn()
    {
        var box = MakeListBox();
        box.CornerRadius = CORNER_RADIUS;
        box.BorderThickness = 0;
        box.PrepareContainer<string>((container, _, index, _) =>
        {
            if (index == 0)
            {
                container.CornerRadius = 1;
            }
        });

        AssertTheFirstContainerIsCleanOnReuse(box, box.ScrollIntoView, box.VisitRealizedContainers, container =>
            Assert.AreEqual(CORNER_RADIUS, container.CornerRadius, 1e-9, "the hook's radius outlived its item"));
    }

    private sealed class BindingDiagnosticsLog : System.Diagnostics.TraceListener
    {
        public List<string> Lines { get; } = [];

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (message != null && message.StartsWith("[Binding]", StringComparison.Ordinal))
            {
                Lines.Add(message);
            }
        }
    }

    private static int CountChangesWhileScrolling(Control control, Action<int> scrollIntoView, MewProperty property)
    {
        Layout(control);
        int changes = 0;
        var watched = VisualTree.FindAll(control, element => element is ItemContainer);
        Assert.IsGreaterThan(0, watched.Count, "no container was realized, so nothing was watched");
        foreach (var element in watched)
        {
            ((ItemContainer)element).AddPropertyBindingCallback(property.Id, () => changes++);
        }

        for (int step = 5; step <= 95; step += 5)
        {
            scrollIntoView(step);
            Layout(control);
        }

        return changes;
    }

    private static void AssertTheFirstContainerIsCleanOnReuse(
        Control control,
        Action<int> scrollIntoView,
        Action<Action<int, FrameworkElement>> visitRealized,
        Action<ItemContainer> assertClean)
    {
        Layout(control);
        ItemContainer? first = null;
        visitRealized((index, element) => first = index == 0 ? (ItemContainer)element : first);
        Assert.IsNotNull(first);

        bool reused = false;
        for (int step = 5; step <= 95; step += 5)
        {
            scrollIntoView(step);
            Layout(control);
            visitRealized((index, element) =>
            {
                if (ReferenceEquals(element, first) && index != 0)
                {
                    reused = true;
                    assertClean(first!);
                }
            });
        }

        Assert.IsTrue(reused, "the first container was never reused, so this proves nothing");
    }

    private static ListBox MakeListBox()
        => new()
        {
            ItemsSource = ItemsView.Create(Items(100)),
            Width = WIDTH,
            Height = HEIGHT,
        };

    private static GridView MakeGrid()
    {
        var grid = new GridView { Width = WIDTH, Height = HEIGHT };
        grid.ItemsSource = ItemsView.Create(Items(100));
        grid.SetColumns(
        [
            new GridViewColumn<string>
            {
                Header = "Text",
                Width = 200,
                CellTemplate = new DelegateTemplate<string>(
                    build: _ => new TextBlock(),
                    bind: (view, item, _, _) => ((TextBlock)view).Text = item),
            },
        ]);
        return grid;
    }

    private static string[] Items(int count)
        => Enumerable.Range(0, count).Select(index => "Item " + index).ToArray();

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(WIDTH, HEIGHT));
        element.Arrange(new Rect(0, 0, WIDTH, HEIGHT));
    }
}
