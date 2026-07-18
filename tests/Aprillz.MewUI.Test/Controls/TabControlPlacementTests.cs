using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class TabControlPlacementTests
{
    [TestMethod]
    public void TabPlacement_DefaultsToTop()
    {
        Assert.AreEqual(TabPlacement.Top, new TabControl().TabPlacement);
    }

    [TestMethod]
    [DataRow(TabPlacement.Top)]
    [DataRow(TabPlacement.Bottom)]
    [DataRow(TabPlacement.Left)]
    [DataRow(TabPlacement.Right)]
    public void Arrange_PlacesContentOppositeHeaderStrip(TabPlacement placement)
    {
        var content = new Border { Width = 80, Height = 40 };
        var tabControl = new TabControl
        {
            TabPlacement = placement,
            BorderThickness = 0,
            Padding = Thickness.Zero,
        };
        tabControl.AddTab(new TabItem
        {
            Header = new Border { Width = 30, Height = 12 },
            Content = content,
        });

        tabControl.Measure(new Size(200, 100));
        tabControl.Arrange(new Rect(0, 0, 200, 100));

        switch (placement)
        {
            case TabPlacement.Top:
                Assert.IsGreaterThan(0, content.Bounds.Top);
                break;
            case TabPlacement.Bottom:
                Assert.AreEqual(0, content.Bounds.Top);
                break;
            case TabPlacement.Left:
                Assert.IsGreaterThan(0, content.Bounds.Left);
                break;
            case TabPlacement.Right:
                Assert.AreEqual(0, content.Bounds.Left);
                break;
        }
    }

    [TestMethod]
    public void ChangingPlacement_ReusesHeaderAndRepositionsContent()
    {
        var header = new Border { Width = 30, Height = 12 };
        var content = new Border { Width = 80, Height = 40 };
        var tabControl = new TabControl
        {
            BorderThickness = 0,
            Padding = Thickness.Zero,
        };
        tabControl.AddTab(new TabItem { Header = header, Content = content });

        tabControl.Measure(new Size(200, 100));
        tabControl.Arrange(new Rect(0, 0, 200, 100));
        Assert.IsGreaterThan(0, content.Bounds.Top);

        tabControl.TabPlacement = TabPlacement.Left;
        tabControl.Measure(new Size(200, 100));
        tabControl.Arrange(new Rect(0, 0, 200, 100));

        Assert.IsGreaterThan(0, content.Bounds.Left);
        Assert.AreSame(header, tabControl.Tabs[0].Header);
    }

    [TestMethod]
    public void HorizontalOverflow_KeepsSelectedHeaderVisible()
    {
        var tabControl = new TabControl
        {
            BorderThickness = 0,
            Padding = Thickness.Zero,
        };

        tabControl.AddTabs(
            CreateTab(100, "One"),
            CreateTab(100, "Two"),
            CreateTab(100, "Three"),
            CreateTab(100, "Four"));
        tabControl.SelectedIndex = 3;

        tabControl.Measure(new Size(220, 100));
        tabControl.Arrange(new Rect(0, 0, 220, 100));

        var headers = GetHeaders(tabControl);

        Assert.HasCount(4, headers);
        Assert.IsGreaterThan(0, headers[3].Bounds.Width);
        Assert.IsTrue(headers.Any(h => h.Bounds.Width == 0));
    }

    [TestMethod]
    public void HeaderString_SetsBindableHeaderText()
    {
        var title = new ObservableValue<string?>("Original");
        var tab = new TabItem().Header("Initial");

        Assert.AreEqual("Initial", tab.HeaderText);

        tab.SetBinding(TabItem.HeaderTextProperty, title);
        title.Value = "Renamed";

        Assert.AreEqual("Renamed", tab.HeaderText);
    }

    [TestMethod]
    public void SelectedContentChange_ReattachesVisualParent()
    {
        var oldContent = new Border { Width = 80, Height = 40 };
        var newContent = new Border { Width = 70, Height = 30 };
        var tab = new TabItem
        {
            Header = new Border { Width = 30, Height = 12 },
            Content = oldContent,
        };
        var tabControl = new TabControl
        {
            BorderThickness = 0,
            Padding = Thickness.Zero,
        };
        tabControl.AddTab(tab);

        tabControl.Measure(new Size(200, 100));
        tabControl.Arrange(new Rect(0, 0, 200, 100));

        Assert.AreSame(tabControl, oldContent.Parent);

        tab.Content = newContent;

        Assert.IsNull(oldContent.Parent);
        Assert.AreSame(tabControl, newContent.Parent);
    }

    private static TabItem CreateTab(double headerWidth, string name) =>
        new()
        {
            Header = new Border { Width = headerWidth, Height = 12 },
            HeaderText = name,
            Content = new Border { Width = 80, Height = 40 },
        };

    private static List<TabHeaderButton> GetHeaders(TabControl tabControl)
    {
        var result = new List<TabHeaderButton>();
        ((IVisualTreeHost)tabControl).VisitChildren(child =>
        {
            if (child is TabHeaderButton header)
            {
                result.Add(header);
            }
            return true;
        });
        return result;
    }
}
