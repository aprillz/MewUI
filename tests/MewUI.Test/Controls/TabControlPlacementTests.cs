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
}
