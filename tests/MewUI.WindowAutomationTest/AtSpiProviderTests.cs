using Aprillz.MewUI;
using Aprillz.MewUI.Automation;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A window's automation tree as an AT-SPI2 client sees it on Linux, through the accessibility bus: the roles,
/// names, interfaces, and actions of its controls, the actions and settable values operating them, focus changes
/// reaching the client, and a drop-down's items in the popup window that shows them.
/// </summary>
[TestClass]
public sealed class AtSpiProviderTests
{
    private const string TITLE = "AtSpiProvider";
    private const uint ROLE_CHECK_BOX = 7;
    private const uint ROLE_COMBO_BOX = 11;
    private const uint ROLE_FRAME = 23;
    private const uint ROLE_LIST = 31;
    private const uint ROLE_LIST_ITEM = 32;
    private const uint ROLE_PUSH_BUTTON = 43;
    private const uint ROLE_SLIDER = 51;
    private const uint ROLE_ENTRY = 79;
    private const int STATE_CHECKED = 4;

    private sealed record Element(AtSpiClient.Reference Reference, int Depth, uint Role, string Name, string Id);

    private static Task RunAsync(Func<AutomationForm, AtSpiClient, AtSpiClient.Reference, Task> body)
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("AT-SPI2 is the Linux provider.");
        }

        if (!RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs the real application loop.");
        }

        return RealAppSession.RunAsync(async () =>
        {
            using var client = await AtSpiClient.ConnectAsync();
            var form = new AutomationForm(TITLE);
            try
            {
                form.Window.Show();
                form.Window.MoveTo(80, 80);
                form.Window.Activate();
                await Task.Delay(500);
                var application = await client.FindApplicationAsync(AppDomain.CurrentDomain.FriendlyName);
                await body(form, client, application);
            }
            finally
            {
                form.Window.Close();
            }
        });
    }

    private static async Task<List<Element>> TreeAsync(AtSpiClient client, AtSpiClient.Reference root)
    {
        var elements = new List<Element>();
        async Task Walk(AtSpiClient.Reference reference, int depth)
        {
            elements.Add(new Element(reference, depth, await client.RoleAsync(reference), await client.NameAsync(reference), await client.AccessibleIdAsync(reference)));
            if (depth < 10)
            {
                foreach (var child in await client.ChildrenAsync(reference))
                {
                    await Walk(child, depth + 1);
                }
            }
        }

        await Walk(root, 0);
        return elements;
    }

    private static string Dump(IEnumerable<Element> elements)
        => string.Join("\n", elements.Select(element => $"{new string(' ', element.Depth * 2)}{element.Role}|{element.Name}|{element.Id}"));

    private static Element Single(List<Element> elements, uint role, string name)
    {
        var matches = elements.Where(element => element.Role == role && element.Name == name).ToArray();
        Assert.AreEqual(1, matches.Length, $"role {role} '{name}' in:\n{Dump(elements)}");
        return matches[0];
    }

    private static async Task<AtSpiClient.Reference> FrameAsync(AtSpiClient client, AtSpiClient.Reference application, string name)
    {
        foreach (var child in await client.ChildrenAsync(application))
        {
            if (await client.RoleAsync(child) == ROLE_FRAME && await client.NameAsync(child) == name)
            {
                return child;
            }
        }

        throw new AssertFailedException($"no frame '{name}'");
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
    public Task Tree_ShowsTheControlsWithTheirRolesNamesAndInterfaces() => RunAsync(async (form, client, application) =>
    {
        var tree = await TreeAsync(client, await FrameAsync(client, application, TITLE));

        CollectionAssert.Contains(await client.ActionsAsync(Single(tree, ROLE_PUSH_BUTTON, "Save").Reference), "click");
        var name = tree.Single(element => element.Id == "name");
        Assert.AreEqual(ROLE_ENTRY, name.Role);
        CollectionAssert.Contains(await client.InterfacesAsync(name.Reference), "org.a11y.atspi.EditableText");
        CollectionAssert.Contains(await client.ActionsAsync(Single(tree, ROLE_CHECK_BOX, "Agree").Reference), "toggle");
        var list = tree.Single(element => element.Role == ROLE_LIST);
        var items = tree.Where(element => element.Depth == list.Depth + 1 && element.Role == ROLE_LIST_ITEM).Select(element => element.Name).ToArray();
        CollectionAssert.AreEqual(new[] { "Red", "Green", "Blue" }, items, Dump(tree));
        CollectionAssert.Contains(await client.ActionsAsync(Single(tree, ROLE_COMBO_BOX, "Size").Reference), "expand or collapse");
        CollectionAssert.Contains(await client.InterfacesAsync(Single(tree, ROLE_SLIDER, "Volume").Reference), "org.a11y.atspi.Value");
    });

    [TestMethod]
    public Task Actions_OperateTheControls() => RunAsync(async (form, client, application) =>
    {
        var tree = await TreeAsync(client, await FrameAsync(client, application, TITLE));

        Assert.IsTrue(await client.DoActionAsync(Single(tree, ROLE_PUSH_BUTTON, "Save").Reference, "click"));
        await WaitForAsync(() => form.Saves == 1, "clicking the button runs it once");

        var name = tree.Single(element => element.Id == "name").Reference;
        Assert.IsTrue(await client.SetTextAsync(name, "hello"));
        Assert.AreEqual("hello", form.NameBox.Text);
        Assert.AreEqual("hello", await client.TextAsync(name));

        var agree = Single(tree, ROLE_CHECK_BOX, "Agree").Reference;
        Assert.IsTrue(await client.DoActionAsync(agree, "toggle"));
        Assert.IsTrue(form.Agree.IsChecked);
        Assert.AreNotEqual(0UL, await client.StatesAsync(agree) & (1UL << STATE_CHECKED), "the check box reports itself checked");

        await client.SetCurrentValueAsync(Single(tree, ROLE_SLIDER, "Volume").Reference, 7);
        Assert.AreEqual(7, form.Volume.Value);

        Assert.IsTrue(await client.DoActionAsync(Single(tree, ROLE_LIST_ITEM, "Blue").Reference, "select"));
        Assert.AreEqual(2, form.Colors.SelectedIndex);
    });

    [TestMethod]
    public Task Text_ReadsTheBoxByUnitAndSelection() => RunAsync(async (form, client, application) =>
    {
        form.NameBox.Text = "hello world";
        form.NameBox.Select(6, 5);
        var tree = await TreeAsync(client, await FrameAsync(client, application, TITLE));
        var name = tree.Single(element => element.Id == "name").Reference;

        Assert.AreEqual("hello world", await client.TextAsync(name));
        Assert.AreEqual((6, 11), await client.SelectionAsync(name));
        const uint WORD_START = 1;
        var word = await client.TextAtOffsetAsync(name, 7, WORD_START);
        Assert.AreEqual(("world", 6, 11), word);
    });

    [TestMethod]
    public Task FocusChanges_ReachTheClient() => RunAsync(async (form, client, application) =>
    {
        var tree = await TreeAsync(client, await FrameAsync(client, application, TITLE));
        var focused = await client.WatchFocusAsync(application);

        form.NameBox.Focus();
        await Task.Delay(300);
        form.Colors.Focus();
        await WaitForAsync(() => { lock (focused) { return focused.Count >= 2; } }, "two focus changes arrive");

        string namePath = tree.Single(element => element.Id == "name").Reference.Path;
        string redPath = Single(tree, ROLE_LIST_ITEM, "Red").Reference.Path;
        lock (focused)
        {
            CollectionAssert.AreEqual(new[] { namePath, redPath }, focused.Take(2).ToArray(), string.Join(", ", focused));
        }
    });

    [TestMethod]
    public Task DropDown_ShowsItsItemsInThePopupWindow() => RunAsync(async (form, client, application) =>
    {
        var owner = await FrameAsync(client, application, TITLE);
        var size = Single(await TreeAsync(client, owner), ROLE_COMBO_BOX, "Size").Reference;
        Assert.IsTrue(await client.DoActionAsync(size, "expand or collapse"));
        await WaitForAsync(() => form.SizeBox.IsDropDownOpen, "the action opens the drop-down");
        await Task.Delay(300);

        var surfaces = new List<Element>();
        foreach (var child in await client.ChildrenAsync(application))
        {
            if (child.Path != owner.Path)
            {
                surfaces.AddRange(await TreeAsync(client, child));
            }
        }

        var two = Single(surfaces, ROLE_LIST_ITEM, "Two");
        Single(surfaces, ROLE_LIST_ITEM, "One");
        Assert.IsFalse((await TreeAsync(client, owner)).Any(element => element.Name == "Two"), "the items belong to the popup window, not the owner");

        var extents = await client.ExtentsAsync(two.Reference);
        var root = surfaces.First(element => element.Depth == 0).Reference;
        var hit = root;
        for (int depth = 0; depth < 10; depth++)
        {
            var next = await client.AccessibleAtPointAsync(hit, extents.X + extents.Width / 2, extents.Y + extents.Height / 2);
            if (next.Path == "/org/a11y/atspi/null")
            {
                break;
            }

            hit = next;
        }

        Assert.AreEqual(two.Reference.Path, hit.Path, "the point inside the item finds the item");

        Assert.IsTrue(await client.DoActionAsync(two.Reference, "select"));
        Assert.AreEqual(1, form.SizeBox.SelectedIndex);
    });
}
