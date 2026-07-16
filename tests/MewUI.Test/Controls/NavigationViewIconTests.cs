using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class NavigationViewIconTests
{
    [TestMethod]
    public void Items_HostsArbitraryElementIcons()
    {
        var path = new PathShape();
        var emoji = new TextBlock { Text = "😀" };
        var image = new Image();
        var items = new[]
        {
            new IconItem("PathShape", path),
            new IconItem("Emoji", emoji),
            new IconItem("Image", image),
        };
        var view = CreateView(items);

        Layout(view);

        Assert.AreSame(path, VisualTree.Find(view, element => ReferenceEquals(element, path)));
        Assert.AreSame(emoji, VisualTree.Find(view, element => ReferenceEquals(element, emoji)));
        Assert.AreSame(image, VisualTree.Find(view, element => ReferenceEquals(element, image)));
        Assert.IsInstanceOfType<ContentControl>(path.LogicalParent);
        Assert.IsInstanceOfType<ContentControl>(emoji.LogicalParent);
        Assert.IsInstanceOfType<ContentControl>(image.LogicalParent);
    }

    [TestMethod]
    public void Items_PathGeometryOverloadRemainsAvailable()
    {
        var geometry = PathGeometry.Parse("M 0,0 L 16,0 L 16,16 Z");
        var items = new[] { new PathIconItem("Path", geometry) };
        var view = new NavigationView { PaneDisplayMode = PaneDisplayMode.Expanded };
        view.Items(items, item => item.Text, icon: item => item.Icon);

        Layout(view);

        var icon = VisualTree.Find(view,
            element => element is PathShape path && ReferenceEquals(path.Data, geometry));
        Assert.IsInstanceOfType<PathShape>(icon);
    }

    [TestMethod]
    public void RefreshItems_ReattachesSameIconInstance()
    {
        var icon = new GlyphElement { Kind = GlyphKind.Hamburger };
        var view = CreateView([new IconItem("Glyph", icon)]);
        Layout(view);
        var firstHost = icon.LogicalParent;

        view.Pane.RefreshItems();
        Layout(view);

        Assert.AreSame(firstHost, icon.LogicalParent);
        Assert.AreSame(icon, VisualTree.Find(view, element => ReferenceEquals(element, icon)));
    }

    [TestMethod]
    public void MovingItems_ReleasesIconsBeforeContainersAreRebound()
    {
        var firstIcon = new GlyphElement { Kind = GlyphKind.ChevronLeft };
        var secondIcon = new Image();
        var items = new ObservableCollection<IconItem>
        {
            new("First", firstIcon),
            new("Second", secondIcon),
        };
        var view = CreateView(items);
        Layout(view);

        items.Move(0, 1);
        Layout(view);

        Assert.AreSame(firstIcon, VisualTree.Find(view, element => ReferenceEquals(element, firstIcon)));
        Assert.AreSame(secondIcon, VisualTree.Find(view, element => ReferenceEquals(element, secondIcon)));
        Assert.IsNotNull(firstIcon.LogicalParent);
        Assert.IsNotNull(secondIcon.LogicalParent);
    }

    [TestMethod]
    public void Header_DoesNotCreateOrHostAnIcon()
    {
        int headerIconCalls = 0;
        int itemIconCalls = 0;
        var items = new[]
        {
            new IconItem("Group", null, NavigationItemKind.Header),
            new IconItem("Item", new GlyphElement(), NavigationItemKind.Item),
        };
        var view = new NavigationView { PaneDisplayMode = PaneDisplayMode.Expanded };
        view.Items(
            items,
            item => item.Text,
            icon: item =>
            {
                if (item.Kind == NavigationItemKind.Header)
                {
                    headerIconCalls++;
                }
                else
                {
                    itemIconCalls++;
                }
                return item.Icon;
            },
            kind: item => item.Kind);

        Layout(view);

        Assert.AreEqual(0, headerIconCalls);
        Assert.IsGreaterThanOrEqualTo(1, itemIconCalls);
    }

    private static NavigationView CreateView(IReadOnlyList<IconItem> items)
    {
        var view = new NavigationView { PaneDisplayMode = PaneDisplayMode.Expanded };
        view.Items(items, item => item.Text, icon: item => item.Icon);
        return view;
    }

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(800, 400));
        element.Arrange(new Rect(0, 0, 800, 400));
    }

    private sealed record IconItem(
        string Text,
        Element? Icon,
        NavigationItemKind Kind = NavigationItemKind.Item);

    private sealed record PathIconItem(string Text, PathGeometry Icon);
}
