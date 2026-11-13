using Aprillz.MewUI;
using Aprillz.MewUI.Automation;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A window's automation tree as a UI Automation client in another process sees it: the roles, names, and patterns
/// of its controls, the patterns operating them, focus changes reaching the client, and a drop-down's items in the
/// popup window that shows them.
/// </summary>
[TestClass]
public sealed class UiaProviderTests
{
    private static Task RunAsync(Func<AutomationForm, Task> body)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("UI Automation is the Windows provider.");
        }

        if (!RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs the real application loop.");
        }

        return RealAppSession.RunAsync(async () =>
        {
            var form = new AutomationForm("UiaProvider");
            try
            {
                form.Window.Show();
                form.Window.MoveTo(80, 80);
                form.Window.Activate();
                await Task.Delay(400);
                await body(form);
            }
            finally
            {
                form.Window.Close();
            }
        });
    }

    private sealed record Element(int Depth, string Type, string Name, string AutomationId, string[] Patterns);

    private static Element[] Parse(IEnumerable<string> lines)
        => lines.Where(line => line.Contains('|'))
            .Select(line =>
            {
                string trimmed = line.TrimStart();
                int depth = (line.Length - trimmed.Length) / 2;
                string[] parts = trimmed.Split('|');
                return new Element(depth, parts[0], parts[1], parts[2], parts[3].Split(',', StringSplitOptions.RemoveEmptyEntries));
            })
            .ToArray();

    private static Element Single(Element[] elements, string type, string name)
    {
        var matches = elements.Where(element => element.Type == type && element.Name == name).ToArray();
        Assert.AreEqual(1, matches.Length, $"{type} '{name}' in:\n{string.Join("\n", elements.Select(Describe))}");
        return matches[0];
    }

    private static string Describe(Element element)
        => $"{new string(' ', element.Depth * 2)}{element.Type}|{element.Name}|{element.AutomationId}|{string.Join(",", element.Patterns)}";

    private static async Task WaitForAsync(Func<bool> condition, string what)
    {
        for (int attempt = 0; attempt < 40 && !condition(); attempt++)
        {
            await Task.Delay(50);
        }

        Assert.IsTrue(condition(), what);
    }

    [TestMethod]
    public Task Tree_ShowsTheControlsWithTheirRolesNamesAndPatterns() => RunAsync(async form =>
    {
        var lines = await UiaProbe.RunAsync(form.Window, "tree");
        var tree = Parse(lines);
        string dump = string.Join("\n", lines);

        CollectionAssert.Contains(Single(tree, "Button", "Save").Patterns, "Invoke");
        CollectionAssert.Contains(tree.Single(element => element.AutomationId == "name").Patterns, "Value");
        Assert.AreEqual("Edit", tree.Single(element => element.AutomationId == "name").Type);
        CollectionAssert.Contains(Single(tree, "CheckBox", "Agree").Patterns, "Toggle");
        var list = tree.Single(element => element.Type == "List");
        CollectionAssert.Contains(list.Patterns, "Selection");
        var items = tree.Where(element => element.Type == "ListItem" && element.Depth == list.Depth + 1).Select(element => element.Name).ToArray();
        CollectionAssert.AreEqual(new[] { "Red", "Green", "Blue" }, items, dump);
        Assert.IsFalse(tree.Any(element => element.Depth > list.Depth + 1 && element.Type == "Text"), "a text item names itself and has no text child:\n" + dump);
        CollectionAssert.Contains(Single(tree, "ListItem", "Red").Patterns, "SelectionItem");
        CollectionAssert.Contains(Single(tree, "ComboBox", "Size").Patterns, "ExpandCollapse");
        CollectionAssert.Contains(Single(tree, "Slider", "Volume").Patterns, "RangeValue");
    });

    [TestMethod]
    public Task Patterns_OperateTheControls() => RunAsync(async form =>
    {
        await UiaProbe.RunAsync(form.Window, "invoke", "Save");
        await WaitForAsync(() => form.Saves == 1, "invoking the button clicks it once");

        await UiaProbe.RunAsync(form.Window, "setvalue", "#name", "hello");
        Assert.AreEqual("hello", form.NameBox.Text);
        CollectionAssert.AreEqual(new[] { "hello" }, await UiaProbe.RunAsync(form.Window, "value", "#name"));

        await UiaProbe.RunAsync(form.Window, "toggle", "Agree");
        Assert.IsTrue(form.Agree.IsChecked);

        await UiaProbe.RunAsync(form.Window, "rangevalue", "Volume", "7");
        Assert.AreEqual(7, form.Volume.Value);

        await UiaProbe.RunAsync(form.Window, "select", "Blue");
        Assert.AreEqual(2, form.Colors.SelectedIndex);
    });

    [TestMethod]
    public Task Text_ReadsTheBoxByUnitAndSelection() => RunAsync(async form =>
    {
        form.NameBox.Text = "hello world";
        form.NameBox.Select(6, 5);

        var lines = await UiaProbe.RunAsync(form.Window, "textinfo", "#name");

        CollectionAssert.AreEqual(new[] { "hello world", "world", "world" }, lines, string.Join("\n", lines));
    });

    [TestMethod]
    public Task FocusChanges_ReachTheClient() => RunAsync(async form =>
    {
        using var watch = await UiaProbe.WatchFocusAsync(form.Window, count: 2);
        form.NameBox.Focus();
        await Task.Delay(300);
        form.Colors.Focus();
        var focused = await watch.CollectAsync();

        CollectionAssert.AreEqual(new[] { "Edit|", "ListItem|Red" }, focused, string.Join(", ", focused));
    });

    [TestMethod]
    public Task DropDown_ShowsItsItemsInThePopupWindow() => RunAsync(async form =>
    {
        await UiaProbe.RunAsync(form.Window, "expand", "Size");
        await WaitForAsync(() => form.SizeBox.IsDropDownOpen, "expanding opens the drop-down");
        await Task.Delay(300);

        // A popup window is owned by the window, so UI Automation lists it below the window's own element.
        var lines = await UiaProbe.RunAsync(form.Window, "tree");
        var tree = Parse(lines);
        string dump = string.Join("\n", lines);
        var two = Single(tree, "ListItem", "Two");
        Single(tree, "ListItem", "One");
        CollectionAssert.Contains(two.Patterns, "SelectionItem", dump);
        int twoIndex = Array.IndexOf(tree, two);
        var surface = tree.Take(twoIndex).Last(element => element.Depth < two.Depth && element.Type == "Pane");
        Assert.AreEqual(1, surface.Depth, "the popup window sits right below the owner window:\n" + dump);

        var twoPeer = FindPeer(form.SizeBox, "Two");
        var center = AutomationSurface.ScreenBoundsOf(twoPeer).Center;
        var atPoint = Parse(await UiaProbe.RunAsync(form.Window, "frompoint", $"{center.X:0},{center.Y:0}"));
        Assert.AreEqual("ListItem|Two", $"{atPoint[0].Type}|{atPoint[0].Name}");

        await UiaProbe.RunAsync(form.Window, "select", "Two");
        Assert.AreEqual(1, form.SizeBox.SelectedIndex);
    });

    private static AutomationPeer FindPeer(UIElement owner, string name)
    {
        var root = owner.FindVisualRoot() is Window window ? window.GetAutomationPeer() : null;
        Assert.IsNotNull(root, "the window has a peer");
        var stack = new Stack<AutomationPeer>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var peer = stack.Pop();
            if (peer.GetName() == name && peer.GetControlType() == AutomationControlType.ListItem)
            {
                return peer;
            }

            foreach (var child in peer.GetChildren())
            {
                stack.Push(child);
            }
        }

        Assert.Fail($"no list item '{name}'");
        return root;
    }
}
