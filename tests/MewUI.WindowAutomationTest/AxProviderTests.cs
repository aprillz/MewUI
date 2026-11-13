using Aprillz.MewUI;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A window's automation tree as an accessibility client in another process sees it on macOS: the roles, labels,
/// and actions of its controls, the actions and settable values operating them, focus changes reaching the client,
/// and a drop-down's items in the popup window that shows them.
/// </summary>
[TestClass]
public sealed class AxProviderTests
{
    private const string TITLE = "AxProvider";

    private static Task RunAsync(Func<AutomationForm, Task> body)
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("NSAccessibility is the macOS provider.");
        }

        if (!RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs the real application loop.");
        }

        return RealAppSession.RunAsync(async () =>
        {
            var form = new AutomationForm(TITLE);
            try
            {
                form.Window.Show();
                form.Window.MoveTo(80, 80);
                form.Window.Activate();
                await Task.Delay(500);
                await body(form);
            }
            finally
            {
                form.Window.Close();
            }
        });
    }

    private sealed record Element(int Depth, string Role, string Label, string Identifier, string[] Actions);

    private static Element[] Parse(IEnumerable<string> lines)
        => lines.Where(line => line.Contains('|'))
            .Select(line =>
            {
                string trimmed = line.TrimStart();
                string[] parts = trimmed.Split('|');
                return new Element((line.Length - trimmed.Length) / 2, parts[0], parts[1], parts[2], parts[3].Split(',', StringSplitOptions.RemoveEmptyEntries));
            })
            .ToArray();

    private static Element Single(Element[] elements, string role, string label, string dump)
    {
        var matches = elements.Where(element => element.Role == role && element.Label == label).ToArray();
        Assert.AreEqual(1, matches.Length, $"{role} '{label}' in:\n{dump}");
        return matches[0];
    }

    private static async Task WaitForAsync(Func<bool> condition, string what)
    {
        for (int attempt = 0; attempt < 40 && !condition(); attempt++)
        {
            await Task.Delay(50);
        }

        Assert.IsTrue(condition(), what);
    }

    [TestMethod]
    public Task Tree_ShowsTheControlsWithTheirRolesLabelsAndActions() => RunAsync(async form =>
    {
        var lines = await AxProbe.RunAsync("tree", TITLE);
        string dump = string.Join("\n", lines);
        var tree = Parse(lines);

        CollectionAssert.Contains(Single(tree, "AXButton", "Save", dump).Actions, "AXPress", dump);
        Assert.AreEqual("AXTextField", tree.Single(element => element.Identifier == "name").Role, dump);
        CollectionAssert.Contains(Single(tree, "AXCheckBox", "Agree", dump).Actions, "AXPress", dump);
        var list = tree.Single(element => element.Role == "AXList");
        var items = tree.Where(element => element.Depth == list.Depth + 1 && element.Role == "AXStaticText").Select(element => element.Label).ToArray();
        CollectionAssert.AreEqual(new[] { "Red", "Green", "Blue" }, items, dump);
        CollectionAssert.Contains(Single(tree, "AXPopUpButton", "Size", dump).Actions, "AXPress", dump);
        var volume = Single(tree, "AXSlider", "Volume", dump);
        CollectionAssert.Contains(volume.Actions, "AXIncrement", dump);
        CollectionAssert.Contains(volume.Actions, "AXDecrement", dump);
    });

    [TestMethod]
    public Task Actions_OperateTheControls() => RunAsync(async form =>
    {
        await AxProbe.RunAsync("press", "Save");
        await WaitForAsync(() => form.Saves == 1, "pressing the button clicks it once");

        await AxProbe.RunAsync("setvalue", "#name", "hello");
        Assert.AreEqual("hello", form.NameBox.Text);
        CollectionAssert.AreEqual(new[] { "hello" }, await AxProbe.RunAsync("value", "#name"));

        await AxProbe.RunAsync("press", "Agree");
        Assert.IsTrue(form.Agree.IsChecked);

        await AxProbe.RunAsync("increment", "Volume");
        Assert.IsGreaterThan(0, form.Volume.Value);

        await AxProbe.RunAsync("select", "Blue");
        Assert.AreEqual(2, form.Colors.SelectedIndex);
    });

    [TestMethod]
    public Task Text_ReadsTheBoxByRangeAndSelection() => RunAsync(async form =>
    {
        form.NameBox.Text = "hello world";
        form.NameBox.Select(6, 5);

        var lines = await AxProbe.RunAsync("textinfo", "#name");

        CollectionAssert.AreEqual(new[] { "world", "11", "world" }, lines, string.Join("\n", lines));
    });

    [TestMethod]
    public Task FocusChanges_ReachTheClient() => RunAsync(async form =>
    {
        using var watch = await AxProbe.WatchFocusAsync(count: 2);
        form.NameBox.Focus();
        await Task.Delay(300);
        form.Colors.Focus();
        var focused = await watch.CollectAsync();

        Assert.HasCount(2, focused, string.Join("\n", focused));
        StringAssert.StartsWith(focused[0], "AXTextField||name|");
        StringAssert.StartsWith(focused[1], "AXStaticText|Red|");
    });

    [TestMethod]
    public Task DropDown_ShowsItsItemsInThePopupWindow() => RunAsync(async form =>
    {
        await AxProbe.RunAsync("press", "Size");
        await WaitForAsync(() => form.SizeBox.IsDropDownOpen, "pressing the drop-down opens it");
        await Task.Delay(300);

        var lines = await AxProbe.RunAsync("windows");
        string dump = string.Join("\n", lines);
        var all = Parse(lines);
        Single(all, "AXStaticText", "One", dump);
        Single(all, "AXStaticText", "Two", dump);
        var owner = Parse(await AxProbe.RunAsync("tree", TITLE));
        Assert.IsFalse(owner.Any(element => element.Label == "Two"), "the items belong to the popup window's tree, not the owner's:\n" + dump);

        var atCenter = Parse(await AxProbe.RunAsync("atcenter", "Two"));
        Assert.AreEqual("AXStaticText|Two", $"{atCenter[0].Role}|{atCenter[0].Label}");

        await AxProbe.RunAsync("select", "Two");
        Assert.AreEqual(1, form.SizeBox.SelectedIndex);
    });
}
