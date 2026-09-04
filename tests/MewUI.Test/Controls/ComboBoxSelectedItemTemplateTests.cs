using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// The selected item in a ComboBox header is presented through SelectedItemTemplate (falling back to
/// ItemTemplate): the view is built once, rebound as the selection moves, kept out of hit testing,
/// and never changes the control's own size.
/// </summary>
[TestClass]
public sealed class ComboBoxSelectedItemTemplateTests
{
    private sealed class CountingTemplate
    {
        public int Builds;
        public int Binds;
        public int Unbinds;
        public object? LastItem;

        public DelegateTemplate<string> Create(Func<FrameworkElement>? build = null)
            => new(
                _ =>
                {
                    Builds++;
                    return build?.Invoke() ?? new TextBlock();
                },
                (_, item, _, _) =>
                {
                    Binds++;
                    LastItem = item;
                },
                (_, _, _, _) => Unbinds++);
    }

    private static ComboBox CreateComboBox()
        => new() { ItemsSource = ItemsView.Create(new[] { "Alpha", "Beta", "Gamma" }) };

    private static List<Element> VisitedChildren(ComboBox comboBox)
    {
        var children = new List<Element>();
        ((IVisualTreeHost)comboBox).VisitChildren(child =>
        {
            children.Add(child);
            return true;
        });
        return children;
    }

    [TestMethod]
    public void SelectedItemTemplate_BuildsOnceAndRebindsAsSelectionMoves()
    {
        var counter = new CountingTemplate();
        var comboBox = CreateComboBox();
        comboBox.SelectedItemTemplate = counter.Create();

        Assert.AreEqual(1, counter.Builds, "the header view is built when the template is set");
        Assert.AreEqual(0, counter.Binds, "nothing is bound while there is no selection");

        comboBox.SelectedIndex = 0;
        comboBox.SelectedIndex = 1;
        comboBox.SelectedIndex = -1;
        comboBox.SelectedIndex = 2;

        Assert.AreEqual(1, counter.Builds, "changing the selection rebinds the existing view instead of rebuilding it");
        Assert.AreEqual(3, counter.Binds);
        Assert.AreEqual("Gamma", counter.LastItem);
    }

    [TestMethod]
    public void NoTemplate_HostsNoHeaderView()
    {
        var comboBox = CreateComboBox();
        comboBox.SelectedIndex = 0;

        Assert.AreEqual(0, VisitedChildren(comboBox).Count, "without a template the header keeps drawing text and hosts nothing");
    }

    [TestMethod]
    public void SelectedItemTemplate_FallsBackToItemTemplate()
    {
        var itemCounter = new CountingTemplate();
        var selectedCounter = new CountingTemplate();
        var comboBox = CreateComboBox();

        comboBox.ItemTemplate = itemCounter.Create();
        comboBox.SelectedIndex = 0;
        Assert.AreEqual(1, itemCounter.Builds, "with no SelectedItemTemplate the header is built from ItemTemplate");
        Assert.AreEqual(1, VisitedChildren(comboBox).Count);

        comboBox.SelectedItemTemplate = selectedCounter.Create();
        Assert.AreEqual(1, selectedCounter.Builds, "setting SelectedItemTemplate replaces the fallback view");
        Assert.AreEqual(1, itemCounter.Unbinds, "the fallback view is unbound when it is replaced");
        Assert.AreEqual(1, VisitedChildren(comboBox).Count, "only one header view is hosted at a time");
    }

    [TestMethod]
    public void HeaderView_DoesNotChangeDesiredSize()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var plain = CreateComboBox();
        var templated = CreateComboBox();
        templated.SelectedItemTemplate = new CountingTemplate().Create(() => new Border { Width = 400, Height = 200 });
        plain.SelectedIndex = 1;
        templated.SelectedIndex = 1;

        window.Content = new StackPanel().Children(plain, templated);
        window.PerformLayout();

        Assert.AreEqual(plain.DesiredSize.Width, templated.DesiredSize.Width, 0.01, "the header view must not widen the control");
        Assert.AreEqual(plain.DesiredSize.Height, templated.DesiredSize.Height, 0.01, "the header view must not grow the control");
    }

    [TestMethod]
    public void HeaderView_IsNotHitTestable()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var comboBox = CreateComboBox();
        comboBox.SelectedItemTemplate = new CountingTemplate().Create(() => new Button().Content("inside"));
        comboBox.SelectedIndex = 0;

        window.Content = new StackPanel().Children(comboBox);
        window.PerformLayout();

        var probe = new Point(comboBox.Bounds.X + 4, comboBox.Bounds.Y + comboBox.Bounds.Height / 2);
        Assert.AreSame(comboBox, comboBox.HitTest(probe), "a click on the presented item must reach the ComboBox, not the view");
    }

    [TestMethod]
    public void ReplacingItemsSource_RebindsHeaderView()
    {
        var counter = new CountingTemplate();
        var comboBox = CreateComboBox();
        comboBox.SelectedItemTemplate = counter.Create();
        comboBox.SelectedIndex = 1;
        Assert.AreEqual("Beta", counter.LastItem);

        comboBox.ItemsSource = ItemsView.Create(new[] { "One", "Two", "Three" });

        Assert.AreEqual(1, counter.Builds, "a new source rebinds the view rather than rebuilding it");
        Assert.AreEqual("Two", counter.LastItem, "the same index in the new source is a different item");
    }

    [TestMethod]
    public void ControlTemplate_SuppressesHeaderView()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var comboBox = CreateComboBox();
        comboBox.SelectedItemTemplate = new CountingTemplate().Create();
        comboBox.SelectedIndex = 0;
        comboBox.Template = new DelegateControlTemplate<ComboBox>(static (_, _) => new Border { Width = 150, Height = 60 });

        window.Content = new StackPanel().Children(comboBox);
        window.PerformLayout();

        var children = VisitedChildren(comboBox);
        Assert.AreEqual(1, children.Count);
        Assert.AreSame(comboBox.TemplateVisualRoot, children[0], "under a control template only the template root is hosted");
    }

    [TestMethod]
    public void Dispose_UnbindsHeaderView()
    {
        var counter = new CountingTemplate();
        var comboBox = CreateComboBox();
        comboBox.SelectedItemTemplate = counter.Create();
        comboBox.SelectedIndex = 0;

        comboBox.Dispose();

        Assert.AreEqual(1, counter.Unbinds);
        Assert.AreEqual(0, VisitedChildren(comboBox).Count);
    }
}
