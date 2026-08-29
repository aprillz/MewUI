using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Commands;

/// <summary>
/// Target-preservation coverage: menus resolve and execute command items against the context
/// captured when they opened, not against the popup's own focus.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MenuCommandTargetTests
{
    [TestMethod]
    public void CommandText_NormalizesDefaultAccessKey()
    {
        var command = new Command("file.save", "_Save");

        Assert.AreEqual("Save", command.Text, "non-access-key consumers receive display-ready text");
        Assert.AreEqual('S', command.Presentation.AccessKey);
        Assert.AreEqual(0, command.Presentation.AccessKeyIndex);
    }

    [TestMethod]
    public void MenuItem_UsesCommandDefaultAccessKeyAndAllowsCompleteOverride()
    {
        var command = new Command("file.save", "_Save");
        var defaultItem = new MenuItem(command);
        var overriddenItem = new MenuItem("저장(_S)", command);

        Assert.AreEqual(("Save", 'S', 0), defaultItem.GetParsedText());
        Assert.AreEqual(("저장(S)", 'S', 3), overriddenItem.GetParsedText());
    }

    [TestMethod]
    public void CommandText_UnescapesLiteralUnderscoreWithoutCreatingAccessKey()
    {
        var command = new Command("view.guides", "Layout__Guides");
        var item = new MenuItem(command);

        Assert.AreEqual("Layout_Guides", command.Text);
        Assert.AreEqual(("Layout_Guides", default(char), -1), item.GetParsedText());
    }

    [TestMethod]
    public void CommandBindText_TracksLocalizedAccessText()
    {
        var text = new ObservableValue<string>("_Copy");
        var command = new Command("edit.copy").BindText(text);

        Assert.AreEqual("Copy", command.Text);
        Assert.AreEqual('C', command.Presentation.AccessKey);
        Assert.AreEqual(0, command.Presentation.AccessKeyIndex);

        text.Value = "복사(_C)";

        Assert.AreEqual("복사(C)", command.Text);
        Assert.AreEqual('C', command.Presentation.AccessKey);
        Assert.AreEqual(3, command.Presentation.AccessKeyIndex);
    }

    [TestMethod]
    public void CommandBindIcon_TracksPresentationIcon()
    {
        var first = new IconTemplate(static _ => new Border());
        var second = new IconTemplate(static _ => new Border());
        var icon = new ObservableValue<IconTemplate?>(first);
        var command = new Command("edit.copy").BindIcon(icon);

        icon.Value = second;

        Assert.AreSame(second, command.Icon);
    }

    [TestMethod]
    public void IconTemplateSize_ResolvesDipAndRasterPixelRequirements()
    {
        Assert.AreEqual(new IconTemplateSize(16, 16), IconTemplate.ResolveSize(16, 1));
        Assert.AreEqual(new IconTemplateSize(16, 20), IconTemplate.ResolveSize(16, 1.25));
        Assert.AreEqual(new IconTemplateSize(24, 36), IconTemplate.ResolveSize(24, 1.5));
    }

    [TestMethod]
    public void MenuItem_TracksCommandPresentationAndPreservesLocalOverride()
    {
        var text = new ObservableValue<string>("_Copy");
        var command = new Command("edit.copy").BindText(text);
        var inherited = new MenuItem(command);
        var overridden = new MenuItem("Local __ Copy", command);

        text.Value = "복사(_C)";

        Assert.AreEqual(("복사(C)", 'C', 3), inherited.GetParsedText());
        Assert.AreEqual(("Local _ Copy", default(char), -1), overridden.GetParsedText());
    }

    [TestMethod]
    public void MenuItem_ExplicitEmptyTextAndNullIconSuppressCommandDefaults()
    {
        var command = new Command(
            "edit.copy",
            "_Copy",
            new IconTemplate(static _ => new Border()));
        var item = new MenuItem(command)
        {
            Text = string.Empty,
            Icon = null,
        };

        Assert.AreEqual((string.Empty, default(char), -1), item.GetParsedText());
        Assert.IsNull(item.ResolveIconTemplate());
    }

    [TestMethod]
    public void MenuItem_CommandCanExecuteDoesNotReplaceLocalEnabledValue()
    {
        var item = new MenuItem(new Command("edit.copy", "Copy")) { IsEnabled = false };

        item.ApplyCommandState(canExecute: true, shortcutDisplayText: null);
        Assert.IsFalse(item.IsEnabled);
        Assert.IsFalse(item.IsEffectivelyEnabled);

        item.IsEnabled = true;
        item.ApplyCommandState(canExecute: false, shortcutDisplayText: null);
        Assert.IsTrue(item.IsEnabled);
        Assert.IsFalse(item.IsEffectivelyEnabled);
    }

    [TestMethod]
    public void MenuItem_BindingExtensionsCreateLivePlacementOverrides()
    {
        var text = new ObservableValue<string>("_Copy here");
        var enabled = new ObservableValue<bool>(true);
        var command = new ObservableValue<Command?>(new Command("edit.copy", "_Copy"));
        var item = new MenuItem()
            .BindCommand(command)
            .BindText(text)
            .BindIsEnabled(enabled);

        text.Value = "복사(_C)";
        enabled.Value = false;
        command.Value = new Command("edit.copy.alternate", "Alternate");

        Assert.AreEqual("edit.copy.alternate", item.Command?.Id);
        Assert.AreEqual(("복사(C)", 'C', 3), item.GetParsedText());
        Assert.IsFalse(item.IsEnabled);
    }

    [TestMethod]
    public void StandardCommandPresentation_TracksMewUIStrings()
    {
        string original = MewUIStrings.CommandCopy.Value;
        try
        {
            var item = new MenuItem(StandardCommands.Copy);

            MewUIStrings.CommandCopy.Value = "복사(_C)";

            Assert.AreEqual("복사(C)", StandardCommands.Copy.Text);
            Assert.AreEqual(("복사(C)", 'C', 3), item.GetParsedText());
            Assert.AreSame(MewUIStrings.CommandCopy, MewUIStrings.TextBoxContextMenuCopy);
        }
        finally
        {
            MewUIStrings.CommandCopy.Value = original;
        }
    }

    [TestMethod]
    public void Button_CommandPresentationIsOptInAndExplicitContentWins()
    {
        var command = new Command("file.save", "_Save");
        var button = new Button { Command = command };

        Assert.IsNull(GetOnlyVisualChild(button));

        button.CommandPresentationMode = CommandPresentationMode.Text;
        Assert.IsInstanceOfType<CommandContentPresenter>(GetOnlyVisualChild(button));

        var explicitContent = new Border();
        button.Content = explicitContent;
        Assert.AreSame(explicitContent, GetOnlyVisualChild(button));
    }

    [TestMethod]
    public void Button_CommandPresentationTracksTextBinding()
    {
        var text = new ObservableValue<string>("_Save");
        var button = new Button
        {
            Command = new Command("file.save").BindText(text),
            CommandPresentationMode = CommandPresentationMode.Text,
        };

        var presenter = (CommandContentPresenter)GetOnlyVisualChild(button)!;
        Assert.AreEqual("_Save", ((AccessText)presenter[0]).RawText);

        text.Value = "저장(_S)";

        Assert.AreSame(presenter, GetOnlyVisualChild(button));
        Assert.AreEqual("저장(_S)", ((AccessText)presenter[0]).RawText);
    }

    private static Element? GetOnlyVisualChild(Button button)
    {
        Element? child = null;
        ((IVisualTreeHost)button).VisitChildren(candidate =>
        {
            Assert.IsNull(child, "button exposes at most one effective content child");
            child = candidate;
            return true;
        });
        return child;
    }

    [TestMethod]
    public void ContextMenu_ExecutesAgainstCapturedOwner()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var editor = new Button
        {
            Width = 60,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = editor;
        window.PerformLayout();
        window.FocusManager.SetFocus(editor);

        var copyCommand = new Command("edit.copy", "Copy");
        int executed = 0;
        editor.Commands.Register(copyCommand, () => executed++);

        var menu = new ContextMenu();
        menu.AddEntry(new MenuItem(copyCommand));
        menu.Show(editor, new Point(100, 100));
        window.PerformLayout();

        Assert.AreNotSame(editor, window.FocusManager.FocusedElement, "the open menu takes focus");

        var bounds = menu.Bounds;
        window.SendClick(new Point(bounds.X + bounds.Width / 2, bounds.Y + 12));

        Assert.AreEqual(1, executed, "the item executes against the captured editor target");
    }

    [TestMethod]
    public void ContextMenu_EnabledStateComesFromCapturedTarget()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var editor = new Button
        {
            Width = 60,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = editor;
        window.PerformLayout();

        var copyCommand = new Command("edit.copy", "Copy");
        bool hasSelection = false;
        editor.Commands.Register(copyCommand, static () => { }, () => hasSelection);

        var item = new MenuItem(copyCommand);
        var menu = new ContextMenu();
        menu.AddEntry(item);

        menu.Show(editor, new Point(100, 100));
        window.PerformLayout();
        Assert.IsTrue(item.IsEnabled, "command state does not replace the local enabled value");
        Assert.IsFalse(item.IsEffectivelyEnabled, "menu open queries CanExecute against the owner");
        menu.CloseTree(window);

        hasSelection = true;
        menu.Show(editor, new Point(100, 100));
        window.PerformLayout();
        Assert.IsTrue(item.IsEffectivelyEnabled, "reopening re-queries current state");
    }

    [TestMethod]
    public void MenuItem_UsesCommandTextAndEffectiveShortcut()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var editor = new Button
        {
            Width = 60,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = editor;
        window.PerformLayout();

        var copyCommand = new Command("edit.copy", "Copy");
        editor.Commands.Register(copyCommand, static () => { });
        var gesture = new KeyGesture(Key.C, ModifierKeys.Control);
        window.InputMap.Map(copyCommand, gesture);

        var item = new MenuItem(copyCommand);
        var menu = new ContextMenu();
        menu.AddEntry(item);
        menu.Show(editor, new Point(100, 100));
        window.PerformLayout();

        Assert.AreEqual("Copy", item.GetParsedText().displayText, "Command.Text supplies the label");
        Assert.AreEqual(gesture.ToDisplayString(), item.GetShortcutDisplayText(), "shortcut label is the effective input-map gesture");
    }

    [TestMethod]
    public void OpenMenu_ReflectsStateChangeThroughEvaluationPass()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var editor = new Button
        {
            Width = 60,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = editor;
        window.PerformLayout();

        var copyCommand = new Command("edit.copy", "Copy");
        bool hasSelection = false;
        editor.Commands.Register(copyCommand, static () => { }, () => hasSelection);

        var item = new MenuItem(copyCommand);
        var menu = new ContextMenu();
        menu.AddEntry(item);
        menu.Show(editor, new Point(100, 100));
        window.PerformLayout();
        Assert.IsTrue(item.IsEnabled, "command evaluation preserves local enabled state");
        Assert.IsFalse(item.IsEffectivelyEnabled);

        hasSelection = true;
        window.EvaluateCommandStates();

        Assert.IsTrue(item.IsEffectivelyEnabled, "the open menu is a tracked command source");
    }

    [TestMethod]
    public void CommandIcon_IsBuiltAtMenuSizeForEachPopupLifetime()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        var owner = new Button { Width = 60, Height = 30 };
        window.Content = owner;
        window.PerformLayout();

        var built = new List<FrameworkElement>();
        var sizes = new List<IconTemplateSize>();
        var icon = new IconTemplate(size =>
        {
            sizes.Add(size);
            var element = new Border();
            built.Add(element);
            return element;
        });
        var command = new Command("edit.copy", "Copy", icon);
        owner.Commands.Register(command, static () => { });

        var menu = new ContextMenu().Apply(x => x.AddItem(command));
        menu.Show(owner, new Point(100, 100));
        window.PerformLayout();

        Assert.HasCount(1, built);
        Assert.AreEqual(new IconTemplateSize(16, 16), sizes[0]);
        Assert.AreSame(menu, built[0].Parent);
        Assert.AreEqual(16, built[0].Width);
        Assert.AreEqual(16, built[0].Height);

        var first = built[0];
        menu.CloseTree(window);
        Assert.IsNull(first.Parent, "closing the popup releases its materialized icon visual");

        menu.Show(owner, new Point(100, 100));
        window.PerformLayout();

        Assert.HasCount(2, built);
        Assert.AreNotSame(first, built[1], "each popup lifetime receives an independent visual");
        Assert.AreSame(menu, built[1].Parent);
    }

    [TestMethod]
    public void MenuItemIcon_OverridesCommandIcon()
    {
        int commandBuilds = 0;
        int overrideBuilds = 0;
        var commandIcon = new IconTemplate(size =>
        {
            commandBuilds++;
            return new Border();
        });
        var overrideIcon = new IconTemplate(size =>
        {
            overrideBuilds++;
            return new Border();
        });

        var item = new MenuItem(new Command("test.icon", icon: commandIcon))
        {
            Icon = overrideIcon,
        };

        _ = item.ResolveIconTemplate()!.Build(new IconTemplateSize(16, 16));

        Assert.AreEqual(0, commandBuilds);
        Assert.AreEqual(1, overrideBuilds);
    }
}
