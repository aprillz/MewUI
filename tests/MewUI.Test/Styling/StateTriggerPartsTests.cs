using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Styling;

/// <summary>
/// State colors that used to be picked in render code come from style triggers: menu bar items,
/// calendar cells, the breadcrumb chevron and the text selection.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class StateTriggerPartsTests
{
    [TestMethod]
    public void MenuBarItems_TakeHoverAndDisabledColorsFromTheirStyle()
    {
        var edit = new MenuItem("_Edit") { IsEnabled = false };
        var bar = new MenuBar().Items(new MenuItem("_File"), edit);
        var window = HeadlessWindow.Create(400, 200);
        window.Content = new DockPanel().Children(bar);
        window.PerformLayout();

        var rows = Children<MenuBarRow>(bar);
        Assert.HasCount(2, rows);
        var palette = bar.ThemeInternal.Palette;
        Assert.AreEqual(0, rows[0].Background.A, "an idle item has no fill");
        Assert.AreEqual(palette.DisabledText, rows[1].Foreground);

        window.SendMouseMove(rows[0].CenterOf());
        window.UpdateVisualStates();
        Assert.AreEqual(palette.SelectionBackground.WithAlpha((byte)(0.6 * 255)), rows[0].Background);

        window.SendMouseMove(rows[1].CenterOf());
        window.UpdateVisualStates();
        Assert.AreEqual(0, rows[0].Background.A, "the hover leaves the item it left");
        Assert.AreEqual(palette.SelectionBackground.WithAlpha((byte)(0.6 * 255)), rows[1].Background, "a disabled item still shows the pointer");
    }

    [TestMethod]
    public void CalendarCells_TakeSelectionAndHoverFromTheirStyle()
    {
        var calendar = new Calendar
        {
            DisplayDate = new DateTime(2026, 3, 1),
            SelectedDate = new DateTime(2026, 3, 10),
            FirstDayOfWeek = DayOfWeek.Sunday,
        };
        var window = HeadlessWindow.Create(400, 400);
        window.Content = calendar;
        window.PerformLayout();

        // March 2026 starts on a Sunday, so the cell index is the day minus one.
        var cells = Children<CalendarCell>(calendar);
        Assert.HasCount(42, cells);
        var palette = calendar.ThemeInternal.Palette;
        Assert.AreEqual(palette.Accent, cells[9].Background);
        Assert.AreEqual(palette.AccentText, cells[9].Foreground);
        Assert.AreEqual(0, cells[3].Background.A);

        window.SendMouseMove(cells[3].CenterOf());
        window.UpdateVisualStates();
        Assert.AreEqual(palette.AccentHoverOverlay, cells[3].Background);

        calendar.SelectedDate = new DateTime(2026, 3, 20);
        window.PerformLayout();
        window.UpdateVisualStates();
        Assert.AreEqual(0, cells[9].Background.A, "a deselected cell drops its fill");
        Assert.AreEqual(calendar.Foreground, cells[9].Foreground);
        Assert.AreEqual(palette.Accent, cells[19].Background);

        calendar.DisplayMode = CalendarMode.Decade;
        window.PerformLayout();
        cells = Children<CalendarCell>(calendar);
        Assert.HasCount(12, cells);
        window.SendMouseMove(cells[0].CenterOf());
        window.UpdateVisualStates();
        Assert.AreEqual(0, cells[0].Background.A, "the year before the decade takes no hover");
    }

    [TestMethod]
    public void BreadcrumbChevron_FillsWhileOpen()
    {
        var chevron = new BreadcrumbDropDown("C:\\", _ => ["Windows"], _ => { });
        var window = HeadlessWindow.Create(400, 200);
        window.Content = new StackPanel().Children(chevron);
        window.PerformLayout();
        // Off screen, so the background transition lands at once.
        chevron.Arrange(new Rect(1000, 1000, 20, 20));

        Assert.AreEqual(0, chevron.Background.A);
        Assert.IsFalse(chevron.PropertyStore.HasValue(Control.BackgroundProperty.Id, ValueSource.Local), "a local fill would beat the triggers");

        chevron.IsDropDownOpen = true;
        window.UpdateVisualStates();
        Assert.AreEqual(chevron.ThemeInternal.Palette.ButtonHoverBackground.WithAlpha(128), chevron.Background);

        chevron.IsDropDownOpen = false;
        window.UpdateVisualStates();
        Assert.AreEqual(0, chevron.Background.A);
    }

    [TestMethod]
    public void TextSelection_DimsWhileUnfocused()
    {
        var textBox = new TextBox { Text = "hello" };
        var viewer = new SyntaxViewer { Text = "world" };
        var window = HeadlessWindow.Create(400, 200);
        window.Content = new StackPanel().Children(textBox, viewer);
        window.PerformLayout();
        window.SetIsActive(true);

        var palette = textBox.ThemeInternal.Palette;
        var inactive = palette.ControlBackground.Lerp(palette.WindowText, 0.16);
        Assert.AreEqual(inactive, textBox.SelectionBackground);
        Assert.AreEqual(inactive, viewer.SelectionBackground);

        Assert.IsTrue(textBox.Focus());
        window.UpdateVisualStates();
        Assert.AreEqual(palette.SelectionBackground, textBox.SelectionBackground);
        Assert.AreEqual(inactive, viewer.SelectionBackground);

        window.SetIsActive(false);
        window.UpdateVisualStates();
        Assert.AreEqual(inactive, textBox.SelectionBackground, "an inactive window shows the unfocused selection");

        var custom = Color.FromRgb(0x20, 0xA0, 0x40);
        textBox.SelectionBackground = custom;
        window.SetIsActive(true);
        window.UpdateVisualStates();
        Assert.AreEqual(custom, textBox.SelectionBackground, "a local color holds in every state");
    }

    private static List<TElement> Children<TElement>(Element host) where TElement : Element
    {
        var found = new List<TElement>();
        ((IVisualTreeHost)host).VisitChildren(child =>
        {
            if (child is TElement match)
            {
                found.Add(match);
            }

            return true;
        });
        return found;
    }
}
