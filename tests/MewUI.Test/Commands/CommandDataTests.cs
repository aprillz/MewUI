using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Commands;

/// <summary>
/// A surface item that declares data hands it to the command as the invocation argument, so one
/// command can serve several items that differ only in the value they pass.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CommandDataTests
{
    [TestMethod]
    public void MenuItemData_ReplacesTheOperandTheMenuCaptured()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var host = MakeHost("operand");
        window.Content = host;
        window.PerformLayout();

        var command = new Command("test.data", "Data");
        string? received = null;
        window.Commands.Register(command, (string value) => received = value);

        var declaring = new ContextMenu().Item("Declared", command, "declared");
        declaring.Show(host, new Point(100, 100));
        window.PerformLayout();
        ClickFirstRow(window, declaring);
        Assert.AreEqual("declared", received, "an item with data passes that data");

        var plain = new ContextMenu().Item("Plain", command);
        plain.Show(host, new Point(100, 100));
        window.PerformLayout();
        ClickFirstRow(window, plain);
        Assert.AreEqual("operand", received, "an item without data falls back to the captured operand");
    }

    [TestMethod]
    public void MenuItemData_EnablesEachItemOnItsOwnValue()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var host = MakeHost(null);
        window.Content = host;
        window.PerformLayout();

        var command = new Command("test.data", "Data");
        window.Commands.Register(command, (int _) => { }, (int value) => value > 5);

        var three = new MenuItem("Three", command, 3);
        var eight = new MenuItem("Eight", command, 8);
        var menu = new ContextMenu();
        menu.AddEntry(three);
        menu.AddEntry(eight);
        menu.Show(host, new Point(100, 100));
        window.PerformLayout();

        Assert.IsFalse(three.IsEffectivelyEnabled, "the predicate sees this item's data");
        Assert.IsTrue(eight.IsEffectivelyEnabled);
    }

    [TestMethod]
    public void ButtonData_IsTheInvocationArgument()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var button = new Button
        {
            Width = 60,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = button;
        window.PerformLayout();

        var command = new Command("test.data", "Data");
        int received = 0;
        window.Commands.Register(command, (int value) => received = value, (int value) => value > 5);

        button.Command = command;
        button.CommandData =3;
        Assert.IsFalse(button.IsEffectivelyEnabled, "the button's data fails the predicate");

        button.CommandData =8;
        Assert.IsTrue(button.IsEffectivelyEnabled, "changing the data re-evaluates");

        window.SendClick(new Point(30, 15));
        Assert.AreEqual(8, received);
    }

    [TestMethod]
    public void InputMapData_PassesTheMappedValueAndKeepsPlainMappings()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var editor = new Button();
        window.Content = editor;
        window.PerformLayout();
        window.FocusManager.SetFocus(editor);

        var align = new Command("test.align", "Align");
        string? received = null;
        window.Commands.Register(align, (string value) => received = value);
        window.InputMap.Map(align, "left", new KeyGesture(Key.F5));
        window.InputMap.Map(align, "right", new KeyGesture(Key.F6));

        var plain = new Command("test.plain", "Plain");
        int plainRuns = 0;
        window.Commands.Register(plain, () => plainRuns++);
        window.InputMap.Map(plain, new KeyGesture(Key.F7), new KeyGesture(Key.F8));

        window.SendKeyDown(Key.F5);
        Assert.AreEqual("left", received);
        window.SendKeyDown(Key.F6);
        Assert.AreEqual("right", received, "mapping the same command with other data keeps both gestures");
        window.SendKeyDown(Key.F8);
        Assert.AreEqual(1, plainRuns, "alternative gestures of a plain mapping still fire");

        Assert.IsTrue(window.InputMap.TryGetPrimaryGesture(align, "right", out var rightGesture));
        Assert.AreEqual(Key.F6, rightGesture.Key);
    }

    [TestMethod]
    public void ShortcutLabel_FindsTheGestureMappedWithTheItemData()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var host = MakeHost(null);
        window.Content = host;
        window.PerformLayout();

        var align = new Command("test.align", "Align");
        window.Commands.Register(align, (string _) => { });
        window.InputMap.Map(align, "left", new KeyGesture(Key.F5));
        window.InputMap.Map(align, "right", new KeyGesture(Key.F6));

        Assert.AreEqual(new KeyGesture(Key.F6).ToDisplayString(),
            InputMapResolver.GetEffectiveGestureText(window, align, null, "right"));
        Assert.IsNull(InputMapResolver.GetEffectiveGestureText(window, align, null),
            "no gesture is mapped without data");

        var right = new MenuItem("Right", align, "right");
        var menu = new ContextMenu();
        menu.AddEntry(right);
        menu.Show(host, new Point(100, 100));
        window.PerformLayout();

        Assert.AreEqual(new KeyGesture(Key.F6).ToDisplayString(), right.GetShortcutDisplayText(),
            "the row labels the gesture mapped with its own data");
    }

    [TestMethod]
    public void ToolBarEntryData_ReachesTheControlWithItsOwnIcon()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        int iconBuilds = 0;
        var leftIcon = new IconTemplate(_ =>
        {
            iconBuilds++;
            return new Border();
        });
        var align = new Command("test.align", "Align");
        var bar = new ToolBar()
            .ItemPresentation(CommandPresentationMode.Icon)
            .Band(new ToolBarGroup()
                .Item(align, "left", leftIcon)
                .Toggle(align, "right"));

        var window = HeadlessWindow.Create();
        window.Content = bar;
        window.PerformLayout();

        var data = new List<object?>();
        VisualTree.Visit(bar, element =>
        {
            if (element is CommandSourceControl source && ReferenceEquals(source.Command, align))
            {
                data.Add(source.CommandData);
            }
        });

        CollectionAssert.AreEqual(new object?[] { "left", "right" }, data);
        Assert.AreEqual(1, iconBuilds, "the entry's own icon replaces the command's");
    }

    private static void ClickFirstRow(Window window, ContextMenu menu)
    {
        var bounds = menu.Bounds;
        window.SendClick(new Point(bounds.X + bounds.Width / 2, bounds.Y + 12));
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
