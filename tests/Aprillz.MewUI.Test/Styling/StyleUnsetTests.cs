using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Styling;

/// <summary>
/// Covers <see cref="Setter.Unset"/>: a derived (BasedOn) style can revert a property to the
/// inherited value instead of the value the base style provides.
/// </summary>
[TestClass]
public sealed class StyleUnsetTests
{
    private static readonly Color AMBIENT = Color.FromRgb(10, 120, 200);
    private static readonly Color BASE_FG = Color.FromRgb(220, 30, 30);

    private static Style BaseStyle() => new(typeof(Border))
    {
        Setters = [Setter.Create(TextElement.ForegroundProperty, BASE_FG)],
    };

    [TestMethod]
    public void Unset_RevertsToInheritedValue()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var sheet = new StyleSheet();
        var baseStyle = BaseStyle();
        sheet.Define("derived", () => new Style(typeof(Border))
        {
            BasedOn = baseStyle,
            Setters = [Setter.Unset(TextElement.ForegroundProperty)],
        });

        var window = HeadlessWindow.Create();
        var container = new Border { StyleSheet = sheet };
        container.Foreground = AMBIENT;
        var child = new Border { StyleName = "derived" };
        container.Child = child;
        window.Content = container;
        window.PerformLayout();

        Assert.AreEqual(AMBIENT, child.Foreground, "Unset reverts Foreground to the inherited ambient value");
    }

    [TestMethod]
    public void WithoutUnset_BaseSetterWinsOverInheritance()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var sheet = new StyleSheet();
        var baseStyle = BaseStyle();
        sheet.Define("derived", () => new Style(typeof(Border)) { BasedOn = baseStyle });

        var window = HeadlessWindow.Create();
        var container = new Border { StyleSheet = sheet };
        container.Foreground = AMBIENT;
        var child = new Border { StyleName = "derived" };
        container.Child = child;
        window.Content = container;
        window.PerformLayout();

        Assert.AreEqual(BASE_FG, child.Foreground, "Without Unset, the BasedOn base setter (Style tier) wins over inheritance");
    }

    [TestMethod]
    public void Unset_DoesNotTouchLocalValue()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var local = Color.FromRgb(0, 200, 0);
        var sheet = new StyleSheet();
        var baseStyle = BaseStyle();
        sheet.Define("derived", () => new Style(typeof(Border))
        {
            BasedOn = baseStyle,
            Setters = [Setter.Unset(TextElement.ForegroundProperty)],
        });

        var window = HeadlessWindow.Create();
        var container = new Border { StyleSheet = sheet };
        container.Foreground = AMBIENT;
        var child = new Border { StyleName = "derived" };
        child.Foreground = local;
        container.Child = child;
        window.Content = container;
        window.PerformLayout();

        Assert.AreEqual(local, child.Foreground, "Unset leaves a higher-priority Local value untouched");
    }

    // The MenuBar-style menu popup: a ContextMenu whose style unsets the font follows the ambient
    // font from its owner instead of the pinned theme font.
    [TestMethod]
    public void MenuStyleWithUnset_PopupInheritsAmbientFontSize()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var sheet = new StyleSheet();
        sheet.Define("menu-like", () => new Style(typeof(ContextMenu))
        {
            BasedOn = Style.ForType<ContextMenu>(),
            Setters = [Setter.Unset(TextElement.FontSizeProperty)],
        });

        var window = HeadlessWindow.Create();
        var container = new Border { StyleSheet = sheet };
        container.FontSize = 18;
        window.Content = container;
        window.PerformLayout();

        var menu = new ContextMenu();
        menu.Item("A");
        menu.Item("B");
        menu.StyleName = "menu-like";
        menu.ShowAt(container, new Point(0, 0));
        window.PerformLayout();

        Assert.AreEqual(18.0, menu.FontSize, "The Unset menu style lets the popup inherit the owner's ambient FontSize");
    }

}
