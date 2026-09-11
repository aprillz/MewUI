using System.Reflection;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace Aprillz.MewUI.Markdown.Test;

/// <summary>
/// Copied text follows the plain text of the reference viewers: WPF FlowDocument (markdig.wpf) and
/// Markdown.Avalonia, taking WPF where the two differ. Copy and select all are commands reached through
/// the input map and the default context menu.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MarkdownCopyTests
{
    private const double WIDTH = 420;
    private const double HEIGHT = 300;

    [TestMethod]
    [DataRow("1. one\n2. two", "1.\tone\n2.\ttwo")]
    [DataRow("3. three\n4. four", "3.\tthree\n4.\tfour")]
    [DataRow("- a\n- b", "•\ta\n•\tb")]
    [DataRow("- a\n\n- b", "•\ta\n•\tb")]
    [DataRow("- a\n  - b\n- c", "•\ta\n•\tb\n•\tc")]
    public void ListItemsCopyWithTheirMarkerAndATab(string markdown, string expected)
    {
        EnsureGdi();
        using var presenter = Layout(new MarkdownPresenter { Markdown = markdown });

        presenter.SelectAll();

        Assert.AreEqual(expected, presenter.SelectedText);
    }

    [TestMethod]
    public void BlocksJoinWithASingleLineBreakAndTablesKeepCellTabs()
    {
        EnsureGdi();
        using var presenter = Layout(new MarkdownPresenter
        {
            Markdown = "# Title\n\npara\n\n> quote\n\n| a | b |\n| --- | --- |\n| c | d |\n\n```\ncode\n```"
        });

        presenter.SelectAll();

        Assert.AreEqual("Title\npara\nquote\na\tb\nc\td\ncode", presenter.SelectedText);
    }

    [TestMethod]
    public void SelectionStartingInsideAnItemLeavesThatItemsMarkerOut()
    {
        EnsureGdi();
        using var presenter = Layout(new MarkdownPresenter { Markdown = "1. one\n2. two" });
        var paragraphs = Paragraphs(presenter);

        presenter.BeginSelectionAt(CharacterPoint(paragraphs[0], 1));
        presenter.ExtendSelectionTo(CharacterPoint(paragraphs[1], 2));

        Assert.AreEqual("ne\n2.\ttw", presenter.SelectedText);
    }

    [TestMethod]
    public void PartialCodeSelectionCopiesOnlyTheSelectedPart()
    {
        EnsureGdi();
        using var presenter = Layout(new MarkdownPresenter { Markdown = "```\nabcdef\n```" });
        var code = Paragraphs(presenter).Single();

        presenter.BeginSelectionAt(CharacterPoint(code, 1));
        presenter.ExtendSelectionTo(CharacterPoint(code, 4));

        Assert.AreEqual("bcd", presenter.SelectedText);
    }

    [TestMethod]
    public void PrimaryShortcutsReachThePresenterThroughTheInputMap()
    {
        EnsureGdi();
        var (window, viewer) = Host("first paragraph\n\nsecond paragraph");
        window.InputMap.Map(StandardCommands.Copy, new KeyGesture(Key.C, ModifierKeys.Primary));
        window.InputMap.Map(StandardCommands.SelectAll, new KeyGesture(Key.A, ModifierKeys.Primary));
        window.FocusManager.SetFocus(viewer);
        string? copied = null;
        viewer.Copying += args =>
        {
            copied = args.Text;
            args.Cancel = true;
        };
        Assert.IsFalse(window.CommandRouter.CanExecute(StandardCommands.Copy), "copy is disabled without a selection");

        WindowInputRouter.KeyDown(window, new KeyEventArgs(Key.A, 0, ModifierKeys.Control));
        Assert.AreEqual("first paragraph\nsecond paragraph", viewer.SelectedText);
        Assert.IsTrue(window.CommandRouter.CanExecute(StandardCommands.Copy));

        WindowInputRouter.KeyDown(window, new KeyEventArgs(Key.C, 0, ModifierKeys.Control));
        Assert.AreEqual(viewer.SelectedText, copied);
    }

    [TestMethod]
    public void KeysWithoutAnInputMapGestureDoNotCopy()
    {
        EnsureGdi();
        var (window, viewer) = Host("some text");
        window.FocusManager.SetFocus(viewer);
        viewer.SelectAll();
        bool copying = false;
        viewer.Copying += args =>
        {
            copying = true;
            args.Cancel = true;
        };

        WindowInputRouter.KeyDown(window, new KeyEventArgs(Key.C, 0, ModifierKeys.Control));

        Assert.IsFalse(copying, "the presenter no longer handles the key itself");
    }

    [TestMethod]
    public void DisablingSelectionDisablesBothCommands()
    {
        EnsureGdi();
        var (window, viewer) = Host("some text");
        viewer.SelectAll();
        var target = window.CommandRouter.CaptureTarget(viewer);
        Assert.IsTrue(window.CommandRouter.CanExecute(StandardCommands.Copy, target));

        viewer.IsSelectionEnabled = false;

        Assert.IsFalse(window.CommandRouter.CanExecute(StandardCommands.Copy, target));
        Assert.IsFalse(window.CommandRouter.CanExecute(StandardCommands.SelectAll, target));
    }

    [TestMethod]
    public void CopyingHandlerSeesTheSelectedTextAndCanCancel()
    {
        EnsureGdi();
        using var presenter = Layout(new MarkdownPresenter { Markdown = "- a\n- b" });
        presenter.SelectAll();
        MarkdownCopyingEventArgs? seen = null;
        presenter.Copying += args =>
        {
            seen = args;
            args.Cancel = true;
        };

        Assert.IsFalse(presenter.CopySelection());
        Assert.IsNotNull(seen);
        Assert.AreEqual("•\ta\n•\tb", seen.Text);
    }

    [TestMethod]
    public void RightClickOpensAMenuWithCopyAndSelectAll()
    {
        EnsureGdi();
        // A headless window has no native surface to host the popup, as in the core test fixture.
        bool previousPreference = PopupManager.PreferNativePopups;
        PopupManager.PreferNativePopups = false;
        try
        {
            var (window, viewer) = Host("some text to copy");
            var paragraph = Paragraphs(viewer).Single();
            var point = CharacterPoint(paragraph, 2);

            WindowInputRouter.MouseButton(window, point, point, MouseButton.Right, isDown: true, leftDown: false, rightDown: true, middleDown: false, clickCount: 1, ModifierKeys.None, PointerType.Mouse);
            WindowInputRouter.MouseButton(window, point, point, MouseButton.Right, isDown: false, leftDown: false, rightDown: false, middleDown: false, clickCount: 1, ModifierKeys.None, PointerType.Mouse);
            window.PerformLayout();

            var field = typeof(MarkdownPresenter).GetField("_defaultContextMenu", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var menu = (ContextMenu?)field.GetValue(viewer);
            Assert.IsNotNull(menu, "the default context menu opened");
            Assert.AreSame(window, menu.FindVisualRoot(), "the menu is shown in the window");
            var commands = menu.Items.OfType<MenuItem>().Select(item => item.Command).ToArray();
            CollectionAssert.AreEqual(new[] { StandardCommands.Copy, StandardCommands.SelectAll }, commands);
        }
        finally
        {
            PopupManager.PreferNativePopups = previousPreference;
        }
    }

    private static (Window Window, MarkdownViewer Viewer) Host(string markdown)
    {
        var viewer = new MarkdownViewer { Markdown = markdown };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = viewer;
        window.PerformLayout();
        return (window, viewer);
    }

    private static Point CharacterPoint(MarkdownParagraph paragraph, int offset)
    {
        var layout = paragraph.GetLayout(paragraph.Bounds.Width);
        var bounds = new List<Rect>();
        layout.GetRangeBounds(offset, 1, bounds);
        var glyph = bounds[0];
        return new Point(paragraph.Bounds.X + glyph.X + 0.5, paragraph.Bounds.Y + glyph.Y + glyph.Height / 2);
    }

    private static MarkdownParagraph[] Paragraphs(MarkdownPresenter presenter) =>
        Descendants(presenter.DocumentRoot!).OfType<MarkdownParagraph>().Where(paragraph => paragraph.TextUnit >= 0).ToArray();

    private static MarkdownPresenter Layout(MarkdownPresenter presenter)
    {
        presenter.Measure(new Size(WIDTH, double.PositiveInfinity));
        presenter.Arrange(new Rect(0, 0, WIDTH, presenter.DesiredSize.Height));
        return presenter;
    }

    private static IEnumerable<Element> Descendants(Element root)
    {
        yield return root;
        if (root is IVisualTreeHost host)
        {
            var children = new List<Element>();
            host.VisitChildren(child => { children.Add(child); return true; });
            foreach (var child in children)
            {
                foreach (var descendant in Descendants(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static void EnsureGdi()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
        }
        GdiBackend.Register();
    }
}
