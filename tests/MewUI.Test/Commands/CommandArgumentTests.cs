using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Commands;

/// <summary>
/// Typed handlers receive the operand of the nearest <see cref="ICommandArgumentSource"/> above the
/// invocation anchor, and a context menu keeps the operand it opened over.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CommandArgumentTests
{
    [TestMethod]
    public async Task TypedHandler_ReceivesTheNearestSourceAboveTheAnchor()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var outer = new ArgumentHost { CommandArgument = "outer" };
        var inner = new ArgumentHost { CommandArgument = "inner" };
        var button = new Button();
        inner.Add(button);
        outer.Add(inner);
        window.Content = outer;
        window.PerformLayout();

        var command = new Command("test.typed");
        string? received = null;
        window.Commands.Register(command, (string value) => received = value);

        Assert.IsTrue(await window.CommandRouter.ExecuteAsync(command, CommandTarget.From(button)));
        Assert.AreEqual("inner", received, "the source closest to the anchor supplies the operand");
    }

    [TestMethod]
    public void TypedHandler_CannotExecuteWithoutAnOperandOfItsType()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var host = new ArgumentHost();
        var button = new Button();
        host.Add(button);
        window.Content = host;
        window.PerformLayout();

        var command = new Command("test.typed");
        window.Commands.Register(command, (string _) => { });
        var target = CommandTarget.From(button);

        Assert.IsFalse(window.CommandRouter.CanExecute(command, target), "no operand on the chain");

        host.CommandArgument = 42;
        Assert.IsFalse(window.CommandRouter.CanExecute(command, target), "operand of another type");

        host.CommandArgument = "text";
        Assert.IsTrue(window.CommandRouter.CanExecute(command, target));
    }

    [TestMethod]
    public async Task TypedHandler_EvaluatesItsPredicateAgainstTheOperand()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var host = new ArgumentHost { CommandArgument = 3 };
        window.Content = host;
        window.PerformLayout();

        var command = new Command("test.typed");
        int received = 0;
        window.Commands.Register(command, (int value) => received = value, (int value) => value > 5);
        var target = CommandTarget.From(host);

        Assert.IsFalse(await window.CommandRouter.ExecuteAsync(command, target), "the predicate rejects 3");
        Assert.AreEqual(0, received);

        host.CommandArgument = 8;
        Assert.IsTrue(await window.CommandRouter.ExecuteAsync(command, target), "struct operands need no class constraint");
        Assert.AreEqual(8, received);
    }

    [TestMethod]
    public void ContextMenu_ActsOnTheOperandItOpenedOver()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var host = MakeHost("first");
        window.Content = host;
        window.PerformLayout();

        var command = new Command("test.typed", "Typed");
        string? received = null;
        window.Commands.Register(command, (string value) => received = value);

        var menu = new ContextMenu();
        menu.AddEntry(new MenuItem(command));
        menu.Show(host, new Point(100, 100));
        window.PerformLayout();

        // The host moves on to another operand while the menu is open, as a recycled item container does.
        host.CommandArgument = "second";

        var bounds = menu.Bounds;
        window.SendClick(new Point(bounds.X + bounds.Width / 2, bounds.Y + 12));

        Assert.AreEqual("first", received, "the menu keeps the operand captured when it opened");
    }

    [TestMethod]
    public void ContextMenu_EnablesItemsPerOperand()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var host = MakeHost("locked");
        window.Content = host;
        window.PerformLayout();

        var command = new Command("test.typed", "Typed");
        window.Commands.Register(command, (string _) => { }, (string value) => value == "open");

        var item = new MenuItem(command);
        var menu = new ContextMenu();
        menu.AddEntry(item);

        menu.Show(host, new Point(100, 100));
        window.PerformLayout();
        Assert.IsFalse(item.IsEffectivelyEnabled, "the predicate sees the captured operand");
        menu.CloseTree(window);

        host.CommandArgument = "open";
        menu.Show(host, new Point(100, 100));
        window.PerformLayout();
        Assert.IsTrue(item.IsEffectivelyEnabled, "reopening captures the current operand");
    }

    [TestMethod]
    public async Task ItemContainer_SuppliesItsItemToHandlersAbove()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var box = new ListBox
        {
            ItemsSource = ItemsView.Create(new[] { "a", "b", "c" }),
            ItemTemplate = new DelegateTemplate<object?>(
                build: _ => new TextBlock(),
                bind: (view, item, _, _) => ((TextBlock)view).Text = (string)item!),
            Width = 200,
            Height = 200,
        };
        box.PrepareContainer<string>((_, _, _, _) => { });
        window.Content = box;
        window.PerformLayout();

        var command = new Command("test.item");
        string? received = null;
        window.Commands.Register(command, (string item) => received = item);

        ItemContainer? second = null;
        box.VisitRealizedContainers((index, element) =>
        {
            if (index == 1)
            {
                second = (ItemContainer)element;
            }
        });

        Assert.IsNotNull(second);
        Assert.AreEqual("b", second.Item);

        var anchorInsideTheContainer = CommandTarget.From((Element)second.Content!);
        Assert.IsTrue(await window.CommandRouter.ExecuteAsync(command, anchorInsideTheContainer));
        Assert.AreEqual("b", received, "an anchor inside the container resolves to the container's item");
    }

    [TestMethod]
    public void GridViewRow_ContextMenuCommandReceivesTheRowItem()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var grid = new GridView { Width = 400, Height = 300 };
        grid.ItemsSource = ItemsView.Create(Enumerable.Range(0, 20).Select(i => "Item " + i).ToArray());
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

        var command = new Command("test.row", "Row");
        string? received = null;
        window.Commands.Register(command, (string item) => received = item);
        var menu = new ContextMenu();
        menu.AddEntry(new MenuItem(command));
        grid.PrepareContainer<string>((row, _, _, _) => row.ContextMenu = menu);

        window.Content = grid;
        window.PerformLayout();

        GridViewRow? third = null;
        grid.VisitRealizedRows((index, row) =>
        {
            if (index == 2)
            {
                third = row;
            }
        });
        Assert.IsNotNull(third);

        // Right-click inside the cell text rather than the row itself, so the event has to bubble
        // through the cell to the row that carries the menu.
        var cellBounds = ((FrameworkElement)third.Children[0]).Bounds;
        window.SendClick(new Point(cellBounds.X + 5, cellBounds.Y + cellBounds.Height / 2), MouseButton.Right);
        window.PerformLayout();
        Assert.IsGreaterThan(0.0, menu.Bounds.Width, "the row's menu opened from a right-click on its cell");

        var bounds = menu.Bounds;
        window.SendClick(new Point(bounds.X + bounds.Width / 2, bounds.Y + 12));

        Assert.AreEqual("Item 2", received, "the row's item reaches the handler as the operand");
    }

    private static ArgumentHost MakeHost(object? argument)
        => new()
        {
            CommandArgument = argument,
            Width = 60,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

    private sealed class ArgumentHost : StackPanel, ICommandArgumentSource
    {
        public object? CommandArgument { get; set; }
    }
}
